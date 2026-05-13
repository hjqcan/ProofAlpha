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
    private readonly ISourceProfileRepository _sourceProfileRepository;
    private readonly IEvidenceSnapshotRepository _evidenceSnapshotRepository;
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
        ISourceProfileRepository sourceProfileRepository,
        IEvidenceSnapshotRepository evidenceSnapshotRepository,
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
        _sourceProfileRepository = sourceProfileRepository ?? throw new ArgumentNullException(nameof(sourceProfileRepository));
        _evidenceSnapshotRepository = evidenceSnapshotRepository ?? throw new ArgumentNullException(nameof(evidenceSnapshotRepository));
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
            var allEvidenceById = new Dictionary<Guid, EvidenceItem>();
            var opportunities = new List<MarketOpportunity>();
            var evidenceSnapshotBundles = new List<EvidenceSnapshotBundle>();
            foreach (var market in markets)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var collectedEvidence = await CollectEvidenceAsync(run.Id, market, cancellationToken).ConfigureAwait(false);
                if (collectedEvidence.Count == 0)
                {
                    continue;
                }

                await _evidenceRepository.AddRangeDedupAsync(collectedEvidence, cancellationToken).ConfigureAwait(false);

                var evidence = await ResolvePersistedEvidenceForMarketAsync(
                        run.Id,
                        collectedEvidence,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (evidence.Count == 0)
                {
                    continue;
                }

                foreach (var item in evidence)
                {
                    allEvidenceById.TryAdd(item.Id, item);
                }

                var analysis = await AnalyzeMarketAsync(market, evidence, cancellationToken).ConfigureAwait(false);
                var compiledOpportunities = CompileOpportunities(run.Id, market, evidence, analysis);
                opportunities.AddRange(compiledOpportunities);
                evidenceSnapshotBundles.AddRange(
                    await BuildEvidenceSnapshotBundlesAsync(
                            run.Id,
                            market,
                            evidence,
                            compiledOpportunities,
                            cancellationToken)
                        .ConfigureAwait(false));
            }

            if (opportunities.Count > 0)
            {
                await _opportunityRepository.AddRangeAsync(opportunities, cancellationToken).ConfigureAwait(false);
            }

            if (evidenceSnapshotBundles.Count > 0)
            {
                await _evidenceSnapshotRepository.AddRangeAsync(evidenceSnapshotBundles, cancellationToken).ConfigureAwait(false);
            }

            run.MarkSucceeded(allEvidenceById.Count, opportunities.Count, DateTimeOffset.UtcNow);
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

    private async Task<IReadOnlyList<EvidenceItem>> ResolvePersistedEvidenceForMarketAsync(
        Guid runId,
        IReadOnlyList<EvidenceItem> collectedEvidence,
        CancellationToken cancellationToken)
    {
        var contentHashes = collectedEvidence
            .Select(item => item.ContentHash)
            .Where(hash => !string.IsNullOrWhiteSpace(hash))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (contentHashes.Count == 0)
        {
            return Array.Empty<EvidenceItem>();
        }

        var persistedEvidence = await _evidenceRepository.GetByRunAsync(runId, cancellationToken).ConfigureAwait(false);
        return persistedEvidence
            .Where(item => contentHashes.Contains(item.ContentHash))
            .Take(_options.MaxEvidencePerMarket)
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

    private async Task<IReadOnlyList<EvidenceSnapshotBundle>> BuildEvidenceSnapshotBundlesAsync(
        Guid runId,
        MarketInfoDto market,
        IReadOnlyList<EvidenceItem> evidence,
        IReadOnlyList<MarketOpportunity> opportunities,
        CancellationToken cancellationToken)
    {
        if (opportunities.Count == 0)
        {
            return Array.Empty<EvidenceSnapshotBundle>();
        }

        var sourceKeys = evidence
            .Select(item => SourceProfileKeys.ForEvidence(item.SourceKind, item.SourceName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var sourceProfiles = await _sourceProfileRepository
            .GetCurrentByKeysAsync(sourceKeys, cancellationToken)
            .ConfigureAwait(false);

        var evidenceById = evidence.ToDictionary(item => item.Id);
        var bundles = new List<EvidenceSnapshotBundle>();
        foreach (var opportunity in opportunities)
        {
            var evidenceIds = DeserializeEvidenceIds(opportunity.EvidenceIdsJson);
            var citationInputs = evidenceIds
                .Select(id => evidenceById.TryGetValue(id, out var item) ? item : null)
                .Where(item => item is not null)
                .Select(item => CreateCitationInput(item!, market, opportunity, sourceProfiles))
                .ToList();
            var liveGateReasons = EvaluateSnapshotLiveGate(citationInputs);
            var liveGateStatus = liveGateReasons.Count == 0
                ? EvidenceSnapshotLiveGateStatus.Eligible
                : EvidenceSnapshotLiveGateStatus.Blocked;
            var snapshot = new EvidenceSnapshot(
                opportunity.Id,
                runId,
                market.MarketId,
                opportunity.CreatedAtUtc,
                liveGateStatus,
                JsonSerializer.Serialize(liveGateReasons, JsonOptions),
                JsonSerializer.Serialize(new
                {
                    opportunityId = opportunity.Id,
                    marketId = market.MarketId,
                    evidenceIds,
                    citationCount = citationInputs.Count,
                    sourceKeys = citationInputs
                        .Select(item => item.SourceKey)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(item => item)
                        .ToList(),
                    officialConfirmationCount = citationInputs.Count(item => item.CanProvideLiveConfirmation)
                }, JsonOptions),
                opportunity.CreatedAtUtc);

            var citations = citationInputs
                .Select(item => item.ToCitation(snapshot.Id, opportunity.CreatedAtUtc))
                .ToList();
            var confirmations = citationInputs
                .Where(item => item.CanProvideLiveConfirmation)
                .Select(item => item.ToOfficialConfirmation(snapshot.Id, market, opportunity))
                .ToList();
            bundles.Add(new EvidenceSnapshotBundle(
                snapshot,
                citations,
                Array.Empty<EvidenceConflict>(),
                confirmations));
        }

        return bundles;
    }

    private static CitationInput CreateCitationInput(
        EvidenceItem evidence,
        MarketInfoDto market,
        MarketOpportunity opportunity,
        IReadOnlyDictionary<string, SourceProfile> sourceProfiles)
    {
        var sourceKey = SourceProfileKeys.ForEvidence(evidence.SourceKind, evidence.SourceName);
        if (sourceProfiles.TryGetValue(sourceKey, out var sourceProfile))
        {
            return new CitationInput(
                evidence.Id,
                sourceProfile.SourceKey,
                sourceProfile.SourceKind,
                sourceProfile.SourceName,
                sourceProfile.IsOfficial,
                sourceProfile.AuthorityKind,
                sourceProfile.CanProvideLiveConfirmation,
                evidence.Url,
                evidence.Title,
                evidence.PublishedAtUtc,
                evidence.ObservedAtUtc,
                evidence.ContentHash,
                Math.Clamp(evidence.SourceQuality, 0m, 1m),
                JsonSerializer.Serialize(new
                {
                    marketId = market.MarketId,
                    opportunityId = opportunity.Id,
                    outcome = opportunity.Outcome,
                    evidence.Title,
                    evidence.Summary
                }, JsonOptions));
        }

        var definition = SourcePackCatalog.Resolve(evidence.SourceKind, evidence.SourceName);
        var canProvideLiveConfirmation = definition.IsOfficial ||
            definition.AuthorityKind is SourceAuthorityKind.Official
                or SourceAuthorityKind.PrimaryExchange
                or SourceAuthorityKind.Regulator
                or SourceAuthorityKind.DataOracle;
        return new CitationInput(
            evidence.Id,
            definition.SourceKey,
            definition.SourceKind,
            definition.SourceName,
            definition.IsOfficial,
            definition.AuthorityKind,
            canProvideLiveConfirmation,
            evidence.Url,
            evidence.Title,
            evidence.PublishedAtUtc,
            evidence.ObservedAtUtc,
            evidence.ContentHash,
            Math.Clamp(evidence.SourceQuality, 0m, 1m),
            JsonSerializer.Serialize(new
            {
                marketId = market.MarketId,
                opportunityId = opportunity.Id,
                outcome = opportunity.Outcome,
                evidence.Title,
                evidence.Summary
            }, JsonOptions));
    }

    private static IReadOnlyList<string> EvaluateSnapshotLiveGate(IReadOnlyList<CitationInput> citations)
    {
        var reasons = new List<string>();
        if (citations.Count == 0)
        {
            reasons.Add("Live promotion requires at least one point-in-time evidence citation.");
        }

        if (!citations.Any(item => item.CanProvideLiveConfirmation))
        {
            reasons.Add("Live promotion requires official API confirmation or strong multi-source confirmation; non-official news/search-only evidence is not sufficient.");
        }

        return reasons;
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

    private static IReadOnlyList<Guid> DeserializeEvidenceIds(string evidenceIdsJson)
    {
        if (string.IsNullOrWhiteSpace(evidenceIdsJson))
        {
            return Array.Empty<Guid>();
        }

        try
        {
            return JsonSerializer.Deserialize<IReadOnlyList<Guid>>(evidenceIdsJson, JsonOptions)
                ?? Array.Empty<Guid>();
        }
        catch (JsonException)
        {
            return Array.Empty<Guid>();
        }
    }

    private static string NormalizeUrl(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().TrimEnd('/');
    }

    private sealed record CitationInput(
        Guid EvidenceItemId,
        string SourceKey,
        EvidenceSourceKind SourceKind,
        string SourceName,
        bool IsOfficial,
        SourceAuthorityKind AuthorityKind,
        bool CanProvideLiveConfirmation,
        string Url,
        string Title,
        DateTimeOffset? PublishedAtUtc,
        DateTimeOffset ObservedAtUtc,
        string ContentHash,
        decimal RelevanceScore,
        string ClaimJson)
    {
        public EvidenceCitation ToCitation(Guid evidenceSnapshotId, DateTimeOffset createdAtUtc)
            => new(
                evidenceSnapshotId,
                EvidenceItemId,
                SourceKey,
                SourceKind,
                SourceName,
                IsOfficial,
                AuthorityKind,
                Url,
                Title,
                PublishedAtUtc,
                ObservedAtUtc,
                ContentHash,
                RelevanceScore,
                ClaimJson,
                createdAtUtc);

        public OfficialConfirmation ToOfficialConfirmation(
            Guid evidenceSnapshotId,
            MarketInfoDto market,
            MarketOpportunity opportunity)
            => new(
                evidenceSnapshotId,
                SourceKey,
                EvidenceConfirmationKind.OfficialApi,
                $"{market.MarketId}:{opportunity.Outcome}",
                Url,
                RelevanceScore,
                ObservedAtUtc,
                JsonSerializer.Serialize(new
                {
                    evidenceItemId = EvidenceItemId,
                    marketId = market.MarketId,
                    opportunityId = opportunity.Id,
                    sourceKey = SourceKey
                }, JsonOptions));
    }
}
