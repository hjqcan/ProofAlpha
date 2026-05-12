using System.Text.Json;
using System.Text.Json.Serialization;
using Autotrade.Application.Configuration;
using Autotrade.SelfImprove.Application.Contract;
using Autotrade.SelfImprove.Application.Contract.Episodes;
using Autotrade.SelfImprove.Application.Contract.GeneratedStrategies;
using Autotrade.SelfImprove.Application.Contract.Llm;
using Autotrade.SelfImprove.Application.Contract.Proposals;
using Autotrade.SelfImprove.Application.Episodes;
using Autotrade.SelfImprove.Application.GeneratedStrategies;
using Autotrade.SelfImprove.Application.Proposals;
using Autotrade.SelfImprove.Domain.Entities;
using Autotrade.SelfImprove.Domain.Shared.Enums;
using Autotrade.Strategy.Application.Engine;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Autotrade.SelfImprove.Application;

public sealed class SelfImproveService : ISelfImproveService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly IImprovementRunRepository _runRepository;
    private readonly IStrategyEpisodeRepository _episodeRepository;
    private readonly IStrategyMemoryRepository _memoryRepository;
    private readonly IImprovementProposalRepository _proposalRepository;
    private readonly IParameterPatchRepository _parameterPatchRepository;
    private readonly IPatchOutcomeRepository _patchOutcomeRepository;
    private readonly IGeneratedStrategyVersionRepository _generatedStrategyRepository;
    private readonly IPromotionGateResultRepository _gateResultRepository;
    private readonly IStrategyEpisodeBuilder _episodeBuilder;
    private readonly ILLmClient _llmClient;
    private readonly IProposalValidator _proposalValidator;
    private readonly IConfigurationMutationService _configurationMutationService;
    private readonly IGeneratedStrategyPackageService _packageService;
    private readonly IStrategyManager _strategyManager;
    private readonly SelfImproveOptions _options;
    private readonly ILogger<SelfImproveService> _logger;

    public SelfImproveService(
        IImprovementRunRepository runRepository,
        IStrategyEpisodeRepository episodeRepository,
        IStrategyMemoryRepository memoryRepository,
        IImprovementProposalRepository proposalRepository,
        IParameterPatchRepository parameterPatchRepository,
        IPatchOutcomeRepository patchOutcomeRepository,
        IGeneratedStrategyVersionRepository generatedStrategyRepository,
        IPromotionGateResultRepository gateResultRepository,
        IStrategyEpisodeBuilder episodeBuilder,
        ILLmClient llmClient,
        IProposalValidator proposalValidator,
        IConfigurationMutationService configurationMutationService,
        IGeneratedStrategyPackageService packageService,
        IStrategyManager strategyManager,
        IOptions<SelfImproveOptions> options,
        ILogger<SelfImproveService> logger)
    {
        _runRepository = runRepository ?? throw new ArgumentNullException(nameof(runRepository));
        _episodeRepository = episodeRepository ?? throw new ArgumentNullException(nameof(episodeRepository));
        _memoryRepository = memoryRepository ?? throw new ArgumentNullException(nameof(memoryRepository));
        _proposalRepository = proposalRepository ?? throw new ArgumentNullException(nameof(proposalRepository));
        _parameterPatchRepository = parameterPatchRepository ?? throw new ArgumentNullException(nameof(parameterPatchRepository));
        _patchOutcomeRepository = patchOutcomeRepository ?? throw new ArgumentNullException(nameof(patchOutcomeRepository));
        _generatedStrategyRepository = generatedStrategyRepository ?? throw new ArgumentNullException(nameof(generatedStrategyRepository));
        _gateResultRepository = gateResultRepository ?? throw new ArgumentNullException(nameof(gateResultRepository));
        _episodeBuilder = episodeBuilder ?? throw new ArgumentNullException(nameof(episodeBuilder));
        _llmClient = llmClient ?? throw new ArgumentNullException(nameof(llmClient));
        _proposalValidator = proposalValidator ?? throw new ArgumentNullException(nameof(proposalValidator));
        _configurationMutationService = configurationMutationService ?? throw new ArgumentNullException(nameof(configurationMutationService));
        _packageService = packageService ?? throw new ArgumentNullException(nameof(packageService));
        _strategyManager = strategyManager ?? throw new ArgumentNullException(nameof(strategyManager));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<SelfImproveRunResult> RunAsync(
        BuildStrategyEpisodeRequest request,
        string trigger,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            throw new InvalidOperationException("SelfImprove is disabled. Set SelfImprove:Enabled=true to run analysis.");
        }

        var run = new ImprovementRun(
            request.StrategyId,
            request.MarketId,
            request.WindowStartUtc,
            request.WindowEndUtc,
            trigger,
            DateTimeOffset.UtcNow);

        await _runRepository.AddAsync(run, cancellationToken).ConfigureAwait(false);

        try
        {
            var episode = await _episodeBuilder.BuildAsync(request, cancellationToken).ConfigureAwait(false);
            await _episodeRepository.AddAsync(episode, cancellationToken).ConfigureAwait(false);
            run.AttachEpisode(episode.Id);
            await _runRepository.UpdateAsync(run, cancellationToken).ConfigureAwait(false);

            var memory = await _memoryRepository.GetByStrategyIdAsync(request.StrategyId, cancellationToken)
                .ConfigureAwait(false);
            var proposals = await _llmClient.AnalyzeEpisodeAsync(
                new StrategyEpisodeAnalysisRequest(
                    episode.ToDto(),
                    memory?.MemoryJson ?? "{}",
                    memory?.PlaybookJson ?? "{}"),
                cancellationToken).ConfigureAwait(false);

            var entities = new List<ImprovementProposal>();
            var requiresManual = false;
            foreach (var proposal in proposals)
            {
                var validation = _proposalValidator.Validate(proposal);
                requiresManual |= validation.RequiresManualReview || !validation.IsValid;
                var entity = proposal.ToEntity(run.Id, request.StrategyId, validation.RequiresManualReview || !validation.IsValid);
                entities.Add(entity);
            }

            await _proposalRepository.AddRangeAsync(entities, cancellationToken).ConfigureAwait(false);
            await PersistParameterPatchesAsync(entities, cancellationToken).ConfigureAwait(false);
            var generatedStrategies = await PersistGeneratedStrategiesAsync(entities, proposals, episode.ToDto(), cancellationToken)
                .ConfigureAwait(false);

            run.MarkAnalyzed(entities.Count, requiresManual);
            if (!requiresManual)
            {
                run.MarkCompleted();
            }

            await _runRepository.UpdateAsync(run, cancellationToken).ConfigureAwait(false);
            return new SelfImproveRunResult(
                run.ToDto(),
                episode.ToDto(),
                entities.Select(x => x.ToDto()).ToList(),
                generatedStrategies.Select(x => x.ToDto()).ToList());
        }
        catch (Exception ex)
        {
            run.MarkFailed(ex.Message);
            await _runRepository.UpdateAsync(run, cancellationToken).ConfigureAwait(false);
            _logger.LogError(ex, "SelfImprove run {RunId} failed for strategy {StrategyId}.", run.Id, run.StrategyId);
            throw;
        }
    }

    public async Task<IReadOnlyList<ImprovementRunDto>> ListRunsAsync(
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var runs = await _runRepository.ListAsync(Math.Clamp(limit, 1, 500), cancellationToken).ConfigureAwait(false);
        return runs.Select(run => run.ToDto()).ToList();
    }

    public async Task<SelfImproveRunResult?> GetRunAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        var run = await _runRepository.GetAsync(runId, cancellationToken).ConfigureAwait(false);
        if (run is null)
        {
            return null;
        }

        var episode = run.EpisodeId is null
            ? null
            : await _episodeRepository.GetAsync(run.EpisodeId.Value, cancellationToken).ConfigureAwait(false);
        var proposals = await _proposalRepository.GetByRunIdAsync(run.Id, cancellationToken).ConfigureAwait(false);
        var generatedStrategies = await _generatedStrategyRepository.GetByProposalIdsAsync(
            proposals.Select(proposal => proposal.Id).ToArray(),
            cancellationToken).ConfigureAwait(false);

        return new SelfImproveRunResult(
            run.ToDto(),
            episode?.ToDto(),
            proposals.Select(proposal => proposal.ToDto()).ToList(),
            generatedStrategies.Select(version => version.ToDto()).ToList());
    }

    public async Task<PatchOutcomeDto> ApplyProposalAsync(
        ApplyProposalRequest request,
        CancellationToken cancellationToken = default)
    {
        var proposal = await _proposalRepository.GetAsync(request.ProposalId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Proposal {request.ProposalId} was not found.");

        if (proposal.Status is not (ImprovementProposalStatus.Proposed or ImprovementProposalStatus.Approved))
        {
            throw new InvalidOperationException($"Proposal {proposal.Id} cannot be applied from status {proposal.Status}.");
        }

        var patches = DeserializePatches(proposal.ParameterPatchJson);
        if (patches.Count == 0)
        {
            throw new InvalidOperationException($"Proposal {proposal.Id} has no parameter patches.");
        }

        var mutation = await _configurationMutationService.MutateAsync(
            new ConfigurationMutationRequest(
                patches.Select(patch => new ConfigurationMutationPatch(
                    patch.Path,
                    patch.ValueJson,
                    patch.Reason)).ToList(),
                request.DryRun,
                request.Actor,
                "SelfImprove"),
            cancellationToken).ConfigureAwait(false);

        var status = !mutation.Success
            ? PatchOutcomeStatus.Rejected
            : request.DryRun
                ? PatchOutcomeStatus.DryRunPassed
                : PatchOutcomeStatus.Applied;

        var outcome = new PatchOutcome(
            proposal.Id,
            proposal.StrategyId,
            status,
            mutation.DiffJson,
            mutation.RollbackJson,
            string.Join("; ", mutation.Errors.Concat(mutation.Warnings)),
            DateTimeOffset.UtcNow);
        await _patchOutcomeRepository.AddAsync(outcome, cancellationToken).ConfigureAwait(false);

        if (!mutation.Success)
        {
            return outcome.ToDto();
        }

        if (!request.DryRun)
        {
            proposal.MarkApplied();
            await _proposalRepository.UpdateAsync(proposal, cancellationToken).ConfigureAwait(false);
            await _strategyManager.ReloadConfigAsync(proposal.StrategyId, cancellationToken).ConfigureAwait(false);
        }

        return outcome.ToDto();
    }

    public async Task<GeneratedStrategyVersionDto> PromoteGeneratedStrategyAsync(
        Guid generatedStrategyVersionId,
        GeneratedStrategyStage targetStage,
        CancellationToken cancellationToken = default)
    {
        var version = await _generatedStrategyRepository.GetAsync(generatedStrategyVersionId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Generated strategy version {generatedStrategyVersionId} was not found.");

        if (targetStage == GeneratedStrategyStage.LiveCanary)
        {
            if (!_options.LiveAutoApplyEnabled)
            {
                throw new InvalidOperationException("SelfImprove:LiveAutoApplyEnabled must be true before generated strategies can enter LiveCanary.");
            }

            var activeCanaries = await _generatedStrategyRepository.GetActiveCanariesAsync(cancellationToken)
                .ConfigureAwait(false);
            if (activeCanaries.Any(canary => canary.Id != version.Id)
                && activeCanaries.Count >= _options.Canary.MaxActiveLiveCanaries)
            {
                throw new InvalidOperationException("Only one generated strategy LiveCanary is allowed by default.");
            }
        }

        version.AdvanceTo(targetStage);
        if (targetStage == GeneratedStrategyStage.LiveCanary)
        {
            version.ActivateCanary();
        }

        await _generatedStrategyRepository.UpdateAsync(version, cancellationToken).ConfigureAwait(false);
        await _gateResultRepository.AddAsync(new PromotionGateResult(
            version.Id,
            ToGateStage(targetStage),
            true,
            $"Promoted to {targetStage}.",
            JsonSerializer.Serialize(new { version.StrategyId, version.Version, targetStage }, JsonOptions),
            DateTimeOffset.UtcNow), cancellationToken).ConfigureAwait(false);

        return version.ToDto();
    }

    public async Task<GeneratedStrategyVersionDto> RollbackGeneratedStrategyAsync(
        Guid generatedStrategyVersionId,
        CancellationToken cancellationToken = default)
    {
        var version = await _generatedStrategyRepository.GetAsync(generatedStrategyVersionId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Generated strategy version {generatedStrategyVersionId} was not found.");

        version.RollBack();
        await _generatedStrategyRepository.UpdateAsync(version, cancellationToken).ConfigureAwait(false);
        return version.ToDto();
    }

    public async Task<GeneratedStrategyVersionDto> QuarantineGeneratedStrategyAsync(
        Guid generatedStrategyVersionId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var version = await _generatedStrategyRepository.GetAsync(generatedStrategyVersionId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Generated strategy version {generatedStrategyVersionId} was not found.");

        version.Quarantine(reason);
        await _generatedStrategyRepository.UpdateAsync(version, cancellationToken).ConfigureAwait(false);
        return version.ToDto();
    }

    private async Task PersistParameterPatchesAsync(
        IReadOnlyList<ImprovementProposal> proposals,
        CancellationToken cancellationToken)
    {
        var patches = new List<ParameterPatch>();
        foreach (var proposal in proposals)
        {
            foreach (var patch in DeserializePatches(proposal.ParameterPatchJson))
            {
                patches.Add(new ParameterPatch(
                    proposal.Id,
                    proposal.StrategyId,
                    patch.Path,
                    null,
                    patch.ValueJson,
                    DateTimeOffset.UtcNow));
            }
        }

        await _parameterPatchRepository.AddRangeAsync(patches, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<GeneratedStrategyVersion>> PersistGeneratedStrategiesAsync(
        IReadOnlyList<ImprovementProposal> entities,
        IReadOnlyList<ImprovementProposalDocument> documents,
        StrategyEpisodeDto episode,
        CancellationToken cancellationToken)
    {
        var generatedStrategies = new List<GeneratedStrategyVersion>();
        for (var index = 0; index < entities.Count; index++)
        {
            var entity = entities[index];
            var document = documents[index];
            if (document.Kind != ProposalKind.GeneratedStrategy)
            {
                continue;
            }

            var spec = document.GeneratedStrategy
                ?? await _llmClient.GenerateStrategyAsync(document, episode, cancellationToken).ConfigureAwait(false);
            var version = await _packageService.CreatePackageAsync(entity.Id, spec, cancellationToken).ConfigureAwait(false);
            var validation = await _packageService.ValidatePackageAsync(version, cancellationToken).ConfigureAwait(false);
            for (var gateIndex = 0; gateIndex < validation.Gates.Count; gateIndex++)
            {
                var gate = validation.Gates[gateIndex];
                if (gate.Passed)
                {
                    var evidenceJson = validation.Passed && gateIndex == validation.Gates.Count - 1
                        ? validation.EvidenceJson
                        : gate.EvidenceJson;
                    version.AdvanceTo(ToGeneratedStrategyStage(gate.Stage), evidenceJson);
                    continue;
                }

                version.Quarantine(string.Join("; ", gate.Errors));
                break;
            }

            if (validation.Gates.Count == 0 && !validation.Passed)
            {
                version.Quarantine(string.Join("; ", validation.Errors));
            }

            await _generatedStrategyRepository.AddAsync(version, cancellationToken).ConfigureAwait(false);
            foreach (var gate in validation.Gates)
            {
                await _gateResultRepository.AddAsync(new PromotionGateResult(
                    version.Id,
                    gate.Stage,
                    gate.Passed,
                    gate.Passed ? $"{gate.Stage} passed." : $"{gate.Stage} failed.",
                    gate.EvidenceJson,
                    DateTimeOffset.UtcNow), cancellationToken).ConfigureAwait(false);
            }

            generatedStrategies.Add(version);
        }

        return generatedStrategies;
    }

    private static IReadOnlyList<ParameterPatchSpec> DeserializePatches(string? patchJson)
    {
        if (string.IsNullOrWhiteSpace(patchJson))
        {
            return Array.Empty<ParameterPatchSpec>();
        }

        return JsonSerializer.Deserialize<IReadOnlyList<ParameterPatchSpec>>(patchJson, JsonOptions)
            ?? Array.Empty<ParameterPatchSpec>();
    }

    private static PromotionGateStage ToGateStage(GeneratedStrategyStage stage)
    {
        return stage switch
        {
            GeneratedStrategyStage.StaticValidated => PromotionGateStage.StaticValidation,
            GeneratedStrategyStage.UnitTested => PromotionGateStage.UnitTest,
            GeneratedStrategyStage.ReplayValidated => PromotionGateStage.Replay,
            GeneratedStrategyStage.ShadowRunning => PromotionGateStage.Shadow,
            GeneratedStrategyStage.PaperRunning => PromotionGateStage.Paper,
            GeneratedStrategyStage.LiveCanary => PromotionGateStage.LiveCanary,
            GeneratedStrategyStage.Promoted => PromotionGateStage.LiveCanary,
            _ => PromotionGateStage.StaticValidation
        };
    }

    private static GeneratedStrategyStage ToGeneratedStrategyStage(PromotionGateStage stage)
    {
        return stage switch
        {
            PromotionGateStage.StaticValidation => GeneratedStrategyStage.StaticValidated,
            PromotionGateStage.UnitTest => GeneratedStrategyStage.UnitTested,
            PromotionGateStage.Replay => GeneratedStrategyStage.ReplayValidated,
            PromotionGateStage.Shadow => GeneratedStrategyStage.ShadowRunning,
            PromotionGateStage.Paper => GeneratedStrategyStage.PaperRunning,
            PromotionGateStage.LiveCanary => GeneratedStrategyStage.LiveCanary,
            _ => throw new InvalidOperationException($"Unsupported generated strategy gate stage {stage}.")
        };
    }
}
