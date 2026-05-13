using System.Text.Json;
using Autotrade.OpportunityDiscovery.Domain.Entities;
using Autotrade.OpportunityDiscovery.Domain.Shared.Enums;

namespace Autotrade.OpportunityDiscovery.Application.Evidence;

public sealed record SourceProfileDefinition(
    string SourceKey,
    EvidenceSourceKind SourceKind,
    string SourceName,
    SourceAuthorityKind AuthorityKind,
    bool IsOfficial,
    int ExpectedLatencySeconds,
    IReadOnlyList<string> CoveredCategories,
    decimal HistoricalConflictRate,
    decimal HistoricalPassedGateContribution,
    decimal ReliabilityScore)
{
    public SourceProfile ToProfile(DateTimeOffset createdAtUtc)
        => new(
            SourceKey,
            SourceKind,
            SourceName,
            AuthorityKind,
            IsOfficial,
            ExpectedLatencySeconds,
            JsonSerializer.Serialize(CoveredCategories, SourcePackCatalog.JsonOptions),
            HistoricalConflictRate,
            HistoricalPassedGateContribution,
            ReliabilityScore,
            1,
            null,
            "default source pack",
            createdAtUtc);
}

public static class SourceProfileKeys
{
    public const string PolymarketMarkets = "polymarket-markets";
    public const string PolymarketOrderBook = "polymarket-orderbook";
    public const string PolymarketTrades = "polymarket-trades";
    public const string CryptoPriceOracle = "crypto-price-oracle";
    public const string MacroOfficialData = "macro-official-data";
    public const string SecFilings = "sec-filings";
    public const string WeatherAlerts = "weather-alerts";
    public const string SportsScheduleResults = "sports-schedule-results";
    public const string SportsInjuryReports = "sports-injury-reports";
    public const string ElectionOfficialResults = "election-official-results";
    public const string ElectionPolling = "election-polling";
    public const string RssNews = "rss-news";
    public const string GdeltDoc = "gdelt-doc";
    public const string OpenAiWebSearch = "openai-web-search";
    public const string ManualReview = "manual-review";

    public static string ForEvidence(EvidenceSourceKind sourceKind, string sourceName)
    {
        return sourceKind switch
        {
            EvidenceSourceKind.Polymarket when Contains(sourceName, "order") => PolymarketOrderBook,
            EvidenceSourceKind.Polymarket when Contains(sourceName, "trade") => PolymarketTrades,
            EvidenceSourceKind.Polymarket => PolymarketMarkets,
            EvidenceSourceKind.Rss => RssNews,
            EvidenceSourceKind.Gdelt => GdeltDoc,
            EvidenceSourceKind.OpenAiWebSearch => OpenAiWebSearch,
            EvidenceSourceKind.Manual => ManualReview,
            EvidenceSourceKind.CryptoPriceOracle => CryptoPriceOracle,
            EvidenceSourceKind.MacroOfficial => MacroOfficialData,
            EvidenceSourceKind.SecFilings => SecFilings,
            EvidenceSourceKind.WeatherAlerts => WeatherAlerts,
            EvidenceSourceKind.SportsOfficial => SportsScheduleResults,
            EvidenceSourceKind.ElectionOfficial => ElectionOfficialResults,
            EvidenceSourceKind.Polling => ElectionPolling,
            _ => Normalize($"{sourceKind}-{sourceName}")
        };
    }

    public static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        var normalized = value.Trim().ToLowerInvariant();
        var chars = normalized.Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray();
        return new string(chars)
            .Split('-', StringSplitOptions.RemoveEmptyEntries)
            .DefaultIfEmpty("unknown")
            .Aggregate((left, right) => $"{left}-{right}");
    }

    private static bool Contains(string value, string term)
        => value.Contains(term, StringComparison.OrdinalIgnoreCase);
}

public static class SourcePackCatalog
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<SourceProfileDefinition> Defaults { get; } =
    [
        new(
            SourceProfileKeys.PolymarketMarkets,
            EvidenceSourceKind.Polymarket,
            "polymarket-clob-markets",
            SourceAuthorityKind.PrimaryExchange,
            true,
            5,
            ["polymarket", "market-catalog", "resolution-rules"],
            0.02m,
            0.80m,
            0.92m),
        new(
            SourceProfileKeys.PolymarketOrderBook,
            EvidenceSourceKind.Polymarket,
            "polymarket-clob-orderbook",
            SourceAuthorityKind.PrimaryExchange,
            true,
            2,
            ["polymarket", "orderbook", "liquidity"],
            0.03m,
            0.76m,
            0.90m),
        new(
            SourceProfileKeys.PolymarketTrades,
            EvidenceSourceKind.Polymarket,
            "polymarket-clob-trades",
            SourceAuthorityKind.PrimaryExchange,
            true,
            10,
            ["polymarket", "trades", "market-microstructure"],
            0.03m,
            0.72m,
            0.88m),
        new(
            SourceProfileKeys.CryptoPriceOracle,
            EvidenceSourceKind.CryptoPriceOracle,
            "crypto-price-oracle",
            SourceAuthorityKind.DataOracle,
            true,
            10,
            ["crypto", "prices", "oracle"],
            0.04m,
            0.70m,
            0.86m),
        new(
            SourceProfileKeys.MacroOfficialData,
            EvidenceSourceKind.MacroOfficial,
            "macro-official-data",
            SourceAuthorityKind.Regulator,
            true,
            60,
            ["macro", "official-statistics", "economic-calendar"],
            0.01m,
            0.82m,
            0.94m),
        new(
            SourceProfileKeys.SecFilings,
            EvidenceSourceKind.SecFilings,
            "sec-edgar-filings",
            SourceAuthorityKind.Regulator,
            true,
            60,
            ["sec", "filings", "corporate-events"],
            0.01m,
            0.78m,
            0.93m),
        new(
            SourceProfileKeys.WeatherAlerts,
            EvidenceSourceKind.WeatherAlerts,
            "weather-official-alerts",
            SourceAuthorityKind.Official,
            true,
            60,
            ["weather", "alerts", "hazards"],
            0.02m,
            0.72m,
            0.90m),
        new(
            SourceProfileKeys.SportsScheduleResults,
            EvidenceSourceKind.SportsOfficial,
            "sports-official-schedule-results",
            SourceAuthorityKind.Official,
            true,
            30,
            ["sports", "schedule", "results"],
            0.02m,
            0.76m,
            0.91m),
        new(
            SourceProfileKeys.SportsInjuryReports,
            EvidenceSourceKind.SportsOfficial,
            "sports-injury-reports",
            SourceAuthorityKind.Aggregator,
            false,
            300,
            ["sports", "injuries", "availability"],
            0.12m,
            0.42m,
            0.66m),
        new(
            SourceProfileKeys.ElectionOfficialResults,
            EvidenceSourceKind.ElectionOfficial,
            "election-official-results",
            SourceAuthorityKind.Official,
            true,
            300,
            ["elections", "official-results", "certification"],
            0.01m,
            0.84m,
            0.95m),
        new(
            SourceProfileKeys.ElectionPolling,
            EvidenceSourceKind.Polling,
            "election-polling-aggregates",
            SourceAuthorityKind.Aggregator,
            false,
            3600,
            ["elections", "polling", "forecasting"],
            0.18m,
            0.36m,
            0.58m),
        new(
            SourceProfileKeys.RssNews,
            EvidenceSourceKind.Rss,
            "rss-news",
            SourceAuthorityKind.News,
            false,
            900,
            ["news", "lead-discovery"],
            0.20m,
            0.25m,
            0.55m),
        new(
            SourceProfileKeys.GdeltDoc,
            EvidenceSourceKind.Gdelt,
            "gdelt-doc",
            SourceAuthorityKind.Aggregator,
            false,
            900,
            ["news", "global-events", "lead-discovery"],
            0.20m,
            0.25m,
            0.58m),
        new(
            SourceProfileKeys.OpenAiWebSearch,
            EvidenceSourceKind.OpenAiWebSearch,
            "openai-web-search",
            SourceAuthorityKind.Search,
            false,
            900,
            ["web-search", "lead-discovery"],
            0.22m,
            0.20m,
            0.52m),
        new(
            SourceProfileKeys.ManualReview,
            EvidenceSourceKind.Manual,
            "manual-review",
            SourceAuthorityKind.Manual,
            false,
            0,
            ["manual", "operator-review"],
            0.10m,
            0.40m,
            0.70m)
    ];

    public static SourceProfileDefinition Resolve(EvidenceSourceKind sourceKind, string sourceName)
    {
        var sourceKey = SourceProfileKeys.ForEvidence(sourceKind, sourceName);
        return Defaults.FirstOrDefault(
                definition => string.Equals(definition.SourceKey, sourceKey, StringComparison.OrdinalIgnoreCase))
            ?? new SourceProfileDefinition(
                sourceKey,
                sourceKind,
                string.IsNullOrWhiteSpace(sourceName) ? sourceKey : sourceName.Trim(),
                SourceAuthorityKind.Unknown,
                false,
                900,
                ["unknown"],
                0.50m,
                0m,
                0.25m);
    }
}
