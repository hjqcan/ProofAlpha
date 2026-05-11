using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Autotrade.Llm;
using Autotrade.MarketData.Application.Contract.Catalog;
using Autotrade.OpportunityDiscovery.Application.Contract;
using Autotrade.OpportunityDiscovery.Application.Contract.Analysis;
using Autotrade.OpportunityDiscovery.Application.Evidence;
using Autotrade.OpportunityDiscovery.Domain.Entities;
using Autotrade.OpportunityDiscovery.Domain.Shared.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Autotrade.OpportunityDiscovery.Application;

public sealed class OpportunityDiscoveryService : IOpportunityDiscoveryService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        PropertyNameCaseInsensitive = true
    };

    private readonly IMarketCatalogReader _marketCatalog;
    private readonly IEnumerable<IEvidenceSource> _evidenceSources;
    private readonly ILlmJsonClient _llmClient;
    private readonly IResearchRunRepository _runRepository;
    private readonly IEvidenceItemRepository _evidenceRepository;
    private readonly IMarketOpportunityRepository _opportunityRepository;
    private readonly IOpportunityReviewRepository _reviewRepository;
    private readonly OpportunityDiscoveryOptions _options;
    private readonly ILogger<OpportunityDiscoveryService> _logger;

    public OpportunityDiscoveryService(
        IMarketCatalogReader marketCatalog,
        IEnumerable<IEvidenceSource> evidenceSources,
        ILlmJsonClient llmClient,
        IResearchRunRepository runRepository,
        IEvidenceItemRepository evidenceRepository,
        IMarketOpportunityRepository opportunityRepository,
        IOpportunityReviewRepository reviewRepository,
        IOptions<OpportunityDiscoveryOptions> options,
        ILogger<OpportunityDiscoveryService> logger)
    {
        _marketCatalog = marketCatalog ?? throw new ArgumentNullException(nameof(marketCatalog));
        _evidenceSources = evidenceSources ?? throw new ArgumentNullException(nameof(evidenceSources));
        _llmClient = llmClient ?? throw new ArgumentNullException(nameof(llmClient));
        _runRepository = runRepository ?? throw new ArgumentNullException(nameof(runRepository));
        _evidenceRepository = evidenceRepository ?? throw new ArgumentNullException(nameof(evidenceRepository));
        _opportunityRepository = opportunityRepository ?? throw new ArgumentNullException(nameof(opportunityRepository));
        _reviewRepository = reviewRepository ?? throw new ArgumentNullException(nameof(reviewRepository));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<OpportunityScanResult> ScanAsync(
        OpportunityScanRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _options.Validate();

        var markets = SelectMarkets(request);
        var run = new ResearchRun(
            string.IsNullOrWhiteSpace(request.Trigger) ? "manual" : request.Trigger,
            JsonSerializer.Serialize(markets.Select(m => new
            {
                m.MarketId,
                m.Name,
                m.Slug,
                m.Volume24h,
                m.Liquidity,
                m.ExpiresAtUtc
            }), JsonOptions),
            DateTimeOffset.UtcNow);

        await _runRepository.AddAsync(run, cancellationToken).ConfigureAwait(false);
        run.MarkRunning(DateTimeOffset.UtcNow);
        await _runRepository.UpdateAsync(run, cancellationToken).ConfigureAwait(false);

        try
        {
            var allEvidence = new List<EvidenceItem>();
            var opportunities = new List<MarketOpportunity>();
            foreach (var market in markets)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var evidence = await CollectEvidenceAsync(run.Id, market, cancellationToken).ConfigureAwait(false);
                if (evidence.Count == 0)
                {
                    continue;
                }

                allEvidence.AddRange(evidence);
                await _evidenceRepository.AddRangeDedupAsync(evidence, cancellationToken).ConfigureAwait(false);

                var analysis = await AnalyzeMarketAsync(market, evidence, cancellationToken).ConfigureAwait(false);
                opportunities.AddRange(CompileOpportunities(run.Id, market, evidence, analysis));
            }

            if (opportunities.Count > 0)
            {
                await _opportunityRepository.AddRangeAsync(opportunities, cancellationToken).ConfigureAwait(false);
            }

            run.MarkSucceeded(allEvidence.Count, opportunities.Count, DateTimeOffset.UtcNow);
            await _runRepository.UpdateAsync(run, cancellationToken).ConfigureAwait(false);

            return new OpportunityScanResult(
                OpportunityMapper.ToDto(run),
                opportunities.Select(OpportunityMapper.ToDto).ToList());
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "OpportunityDiscovery scan failed for run {RunId}", run.Id);
            run.MarkFailed(ex.Message, DateTimeOffset.UtcNow);
            await _runRepository.UpdateAsync(run, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<MarketOpportunityDto> ApproveAsync(
        OpportunityReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        var opportunity = await RequireOpportunityAsync(request.OpportunityId, cancellationToken).ConfigureAwait(false);
        opportunity.Approve(DateTimeOffset.UtcNow);
        await _opportunityRepository.UpdateAsync(opportunity, cancellationToken).ConfigureAwait(false);
        await _reviewRepository.AddAsync(
            new OpportunityReview(opportunity.Id, OpportunityReviewDecision.Approve, request.Actor, request.Notes, DateTimeOffset.UtcNow),
            cancellationToken).ConfigureAwait(false);
        return OpportunityMapper.ToDto(opportunity);
    }

    public async Task<MarketOpportunityDto> RejectAsync(
        OpportunityReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        var opportunity = await RequireOpportunityAsync(request.OpportunityId, cancellationToken).ConfigureAwait(false);
        opportunity.Reject(DateTimeOffset.UtcNow);
        await _opportunityRepository.UpdateAsync(opportunity, cancellationToken).ConfigureAwait(false);
        await _reviewRepository.AddAsync(
            new OpportunityReview(opportunity.Id, OpportunityReviewDecision.Reject, request.Actor, request.Notes, DateTimeOffset.UtcNow),
            cancellationToken).ConfigureAwait(false);
        return OpportunityMapper.ToDto(opportunity);
    }

    public async Task<MarketOpportunityDto> PublishAsync(
        OpportunityReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        var opportunity = await RequireOpportunityAsync(request.OpportunityId, cancellationToken).ConfigureAwait(false);
        opportunity.Publish(DateTimeOffset.UtcNow);
        await _opportunityRepository.UpdateAsync(opportunity, cancellationToken).ConfigureAwait(false);
        await _reviewRepository.AddAsync(
            new OpportunityReview(opportunity.Id, OpportunityReviewDecision.Publish, request.Actor, request.Notes, DateTimeOffset.UtcNow),
            cancellationToken).ConfigureAwait(false);
        return OpportunityMapper.ToDto(opportunity);
    }

    public async Task<int> ExpireStaleAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var expirable = await _opportunityRepository.ListExpirableAsync(now, cancellationToken).ConfigureAwait(false);
        var expired = 0;
        foreach (var opportunity in expirable)
        {
            if (opportunity.TryExpire(now))
            {
                expired++;
                await _opportunityRepository.UpdateAsync(opportunity, cancellationToken).ConfigureAwait(false);
            }
        }

        return expired;
    }

    private IReadOnlyList<MarketInfoDto> SelectMarkets(OpportunityScanRequest request)
    {
        var maxMarkets = Math.Min(
            request.MaxMarkets <= 0 ? _options.MaxMarketsPerScan : request.MaxMarkets,
            _options.MaxMarketsPerScan);
        var minVolume = Math.Max(0m, request.MinVolume24h);
        var minLiquidity = Math.Max(0m, request.MinLiquidity);

        return _marketCatalog.GetActiveMarkets()
            .Where(m => m.TokenIds.Count >= 2)
            .Where(m => m.Volume24h >= minVolume || m.Liquidity >= minLiquidity)
            .OrderByDescending(m => m.Volume24h)
            .ThenByDescending(m => m.Liquidity)
            .Take(maxMarkets)
            .ToList();
    }

    private async Task<IReadOnlyList<EvidenceItem>> CollectEvidenceAsync(
        Guid runId,
        MarketInfoDto market,
        CancellationToken cancellationToken)
    {
        var normalized = new List<NormalizedEvidence>();
        foreach (var source in _evidenceSources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var results = await source.SearchAsync(
                        new EvidenceQuery(runId, market, _options.MaxEvidencePerMarket),
                        cancellationToken)
                    .ConfigureAwait(false);
                normalized.AddRange(results);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Evidence source {SourceName} failed for market {MarketId}", source.Name, market.MarketId);
            }
        }

        return normalized
            .Where(IsFresh)
            .GroupBy(e => NormalizeUrl(e.Url), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(e => e.SourceQuality).First())
            .Take(_options.MaxEvidencePerMarket)
            .Select(e => new EvidenceItem(
                runId,
                e.SourceKind,
                e.SourceName,
                e.Url,
                e.Title,
                e.Summary,
                e.PublishedAtUtc,
                e.ObservedAtUtc,
                ComputeHash($"{NormalizeUrl(e.Url)}|{e.Title}|{e.Summary}"),
                e.RawJson,
                e.SourceQuality))
            .ToList();
    }

    private bool IsFresh(NormalizedEvidence evidence)
    {
        var timestamp = evidence.PublishedAtUtc ?? evidence.ObservedAtUtc;
        return timestamp >= DateTimeOffset.UtcNow.AddHours(-_options.FreshEvidenceMaxAgeHours);
    }

    private async Task<OpportunityAnalysisResponse> AnalyzeMarketAsync(
        MarketInfoDto market,
        IReadOnlyList<EvidenceItem> evidence,
        CancellationToken cancellationToken)
    {
        var request = new LlmJsonRequest(
            "You discover Polymarket trading opportunities. Return only JSON. Do not suggest code or direct execution.",
            BuildAnalysisPrompt(market, evidence));

        var result = await _llmClient.CompleteJsonAsync<OpportunityAnalysisResponse>(
                request,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return result.Value;
    }

    private IReadOnlyList<MarketOpportunity> CompileOpportunities(
        Guid runId,
        MarketInfoDto market,
        IReadOnlyList<EvidenceItem> evidence,
        OpportunityAnalysisResponse analysis)
    {
        var documents = analysis.Opportunities ?? Array.Empty<OpportunityAnalysisDocument>();
        var valid = new List<MarketOpportunity>();
        foreach (var document in documents)
        {
            var errors = ValidateDocument(market, evidence, document);
            var evidenceIds = document.EvidenceIds.Where(id => id != Guid.Empty).Distinct().ToList();
            var status = errors.Count == 0 ? OpportunityStatus.Candidate : OpportunityStatus.NeedsReview;
            if (status == OpportunityStatus.NeedsReview && evidenceIds.Count == 0)
            {
                continue;
            }

            var validUntil = document.ValidUntilUtc == default
                ? DateTimeOffset.UtcNow.AddHours(_options.DefaultValidHours)
                : document.ValidUntilUtc.ToUniversalTime();
            var policy = new CompiledOpportunityPolicy(
                Guid.Empty,
                runId,
                market.MarketId,
                document.Outcome,
                document.FairProbability,
                document.Confidence,
                document.Edge,
                document.EntryMaxPrice,
                document.TakeProfitPrice,
                document.StopLossPrice,
                document.MaxSpread,
                document.Quantity,
                document.MaxNotional,
                validUntil,
                evidenceIds);

            var matchedEvidence = evidence.Where(item => evidenceIds.Contains(item.Id)).ToList();
            var sourceQuality = matchedEvidence.Count == 0
                ? 0m
                : matchedEvidence.Average(item => item.SourceQuality);
            var scoreJson = JsonSerializer.Serialize(new
            {
                errors,
                document.Edge,
                document.Confidence,
                evidenceCount = evidenceIds.Count,
                sourceQuality
            }, JsonOptions);

            var opportunity = new MarketOpportunity(
                runId,
                market.MarketId,
                (OpportunityOutcomeSide)document.Outcome,
                document.FairProbability,
                document.Confidence,
                document.Edge,
                validUntil,
                string.IsNullOrWhiteSpace(document.Reason) ? document.AbstainReason ?? "LLM did not provide a reason." : document.Reason,
                JsonSerializer.Serialize(evidenceIds, JsonOptions),
                JsonSerializer.Serialize(document, JsonOptions),
                scoreJson,
                JsonSerializer.Serialize(policy, JsonOptions),
                status,
                DateTimeOffset.UtcNow);

            policy = policy with { OpportunityId = opportunity.Id };
            opportunity.ReplaceCompiledPolicyJson(JsonSerializer.Serialize(policy, JsonOptions), DateTimeOffset.UtcNow);
            valid.Add(opportunity);
        }

        return valid;
    }

    private IReadOnlyList<string> ValidateDocument(
        MarketInfoDto market,
        IReadOnlyList<EvidenceItem> evidence,
        OpportunityAnalysisDocument document)
    {
        var errors = new List<string>();
        if (!string.Equals(document.MarketId, market.MarketId, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Document marketId does not match requested market.");
        }

        if (document.FairProbability < 0m || document.FairProbability > 1m)
        {
            errors.Add("fairProbability must be in 0..1.");
        }

        if (document.Confidence < _options.MinConfidence || document.Confidence > 1m)
        {
            errors.Add($"confidence must be between {_options.MinConfidence} and 1.");
        }

        if (document.Edge < _options.MinEdge)
        {
            errors.Add($"edge must be at least {_options.MinEdge}.");
        }

        if (document.EntryMaxPrice is < 0.01m or > 0.99m ||
            document.TakeProfitPrice is < 0.01m or > 0.99m ||
            document.StopLossPrice is < 0.01m or > 0.99m)
        {
            errors.Add("entry, take-profit, and stop-loss prices must be in 0.01..0.99.");
        }

        if (document.StopLossPrice >= document.EntryMaxPrice || document.TakeProfitPrice <= document.EntryMaxPrice)
        {
            errors.Add("stopLossPrice must be below entryMaxPrice and takeProfitPrice must be above entryMaxPrice.");
        }

        if (document.Quantity <= 0m || document.MaxNotional <= 0m)
        {
            errors.Add("quantity and maxNotional must be positive.");
        }

        var evidenceIds = document.EvidenceIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (evidenceIds.Count == 0)
        {
            errors.Add("at least one evidence id is required.");
        }

        var knownIds = evidence.Select(item => item.Id).ToHashSet();
        if (evidenceIds.Any(id => !knownIds.Contains(id)))
        {
            errors.Add("all evidence ids must reference evidence from this run.");
        }

        if (document.ValidUntilUtc != default && document.ValidUntilUtc <= DateTimeOffset.UtcNow)
        {
            errors.Add("validUntilUtc must be in the future.");
        }

        return errors;
    }

    private static string BuildAnalysisPrompt(MarketInfoDto market, IReadOnlyList<EvidenceItem> evidence)
    {
        var payload = new
        {
            schema = new
            {
                opportunities = new[]
                {
                    new
                    {
                        marketId = market.MarketId,
                        outcome = "Yes|No",
                        fairProbability = 0.62,
                        confidence = 0.7,
                        edge = 0.05,
                        reason = "short explanation",
                        evidenceIds = new[] { Guid.Empty },
                        entryMaxPrice = 0.55,
                        takeProfitPrice = 0.62,
                        stopLossPrice = 0.48,
                        maxSpread = 0.05,
                        quantity = 1,
                        maxNotional = 5,
                        validUntilUtc = DateTimeOffset.UtcNow.AddHours(24),
                        abstainReason = (string?)null
                    }
                },
                abstainReason = (string?)null
            },
            rules = new[]
            {
                "Use only the provided market and evidence.",
                "Every non-abstain opportunity must cite at least one evidenceIds value from the evidence list.",
                "Do not return execution instructions or code.",
                "If evidence is weak or conflicting, return no opportunities and an abstainReason."
            },
            market = new
            {
                market.MarketId,
                market.Name,
                market.Category,
                market.Slug,
                market.Volume24h,
                market.Liquidity,
                market.ExpiresAtUtc,
                market.TokenIds
            },
            evidence = evidence.Select(item => new
            {
                id = item.Id,
                item.SourceKind,
                item.SourceName,
                item.Url,
                item.Title,
                item.Summary,
                item.PublishedAtUtc,
                item.SourceQuality
            })
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private async Task<MarketOpportunity> RequireOpportunityAsync(
        Guid opportunityId,
        CancellationToken cancellationToken)
    {
        if (opportunityId == Guid.Empty)
        {
            throw new ArgumentException("OpportunityId cannot be empty.", nameof(opportunityId));
        }

        return await _opportunityRepository.GetAsync(opportunityId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Opportunity {opportunityId} was not found.");
    }

    private static string ComputeHash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string NormalizeUrl(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().TrimEnd('/');
    }
}
