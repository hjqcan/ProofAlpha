namespace Autotrade.OpportunityDiscovery.Application;

public sealed class OpportunityDiscoveryOptions
{
    public const string SectionName = "OpportunityDiscovery";

    public bool Enabled { get; set; }

    public bool PaperOnly { get; set; } = true;

    public decimal MinEdge { get; set; } = 0.03m;

    public decimal MinConfidence { get; set; } = 0.55m;

    public int FreshEvidenceMaxAgeHours { get; set; } = 72;

    public int MaxEvidencePerMarket { get; set; } = 8;

    public int DefaultValidHours { get; set; } = 24;

    public int MaxMarketsPerScan { get; set; } = 20;

    public RssEvidenceOptions Rss { get; set; } = new();

    public GdeltEvidenceOptions Gdelt { get; set; } = new();

    public OpenAiWebSearchOptions OpenAiWebSearch { get; set; } = new();

    public PolymarketAccountEvidenceOptions PolymarketAccounts { get; set; } = new();

    public void Validate()
    {
        if (!PaperOnly)
        {
            throw new InvalidOperationException("OpportunityDiscovery MVP is paper-only. Set PaperOnly=true.");
        }

        if (MinEdge < 0m || MinEdge > 1m)
        {
            throw new ArgumentOutOfRangeException(nameof(MinEdge), MinEdge, "MinEdge must be in 0..1.");
        }

        if (MinConfidence < 0m || MinConfidence > 1m)
        {
            throw new ArgumentOutOfRangeException(nameof(MinConfidence), MinConfidence, "MinConfidence must be in 0..1.");
        }

        if (FreshEvidenceMaxAgeHours <= 0 || DefaultValidHours <= 0 || MaxMarketsPerScan <= 0 || MaxEvidencePerMarket <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxMarketsPerScan), MaxMarketsPerScan, "Freshness, validity, max markets, and max evidence must be positive.");
        }

        if (PolymarketAccounts.MaxTradesPerWallet <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(PolymarketAccounts.MaxTradesPerWallet), PolymarketAccounts.MaxTradesPerWallet, "MaxTradesPerWallet must be positive.");
        }
    }
}

public sealed class RssEvidenceOptions
{
    public bool Enabled { get; set; }

    public List<string> FeedUrls { get; set; } = new();

    public int MaxItemsPerFeed { get; set; } = 25;
}

public sealed class GdeltEvidenceOptions
{
    public bool Enabled { get; set; } = true;

    public string BaseUrl { get; set; } = "https://api.gdeltproject.org/api/v2/doc/doc";

    public int MaxRecords { get; set; } = 10;
}

public sealed class OpenAiWebSearchOptions
{
    public bool Enabled { get; set; }

    public string? BaseUrl { get; set; }

    public string ApiKeyEnvVar { get; set; } = "OPENAI_API_KEY";

    public string Model { get; set; } = "gpt-4.1-mini";

    public int MaxResults { get; set; } = 5;
}

public sealed class PolymarketAccountEvidenceOptions
{
    public bool Enabled { get; set; }

    public string BaseUrl { get; set; } = "https://data-api.polymarket.com";

    public List<string> WalletAddresses { get; set; } = new();

    public int MaxTradesPerWallet { get; set; } = 50;

    public bool TakerOnly { get; set; }

    public decimal SourceQuality { get; set; } = 0.72m;
}
