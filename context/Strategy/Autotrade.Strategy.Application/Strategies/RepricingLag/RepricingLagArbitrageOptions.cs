using Autotrade.MarketData.Application.Contract.Windows;

namespace Autotrade.Strategy.Application.Strategies.RepricingLag;

public sealed class RepricingLagArbitrageOptions
{
    public const string SectionName = "Strategies:RepricingLagArbitrage";

    public bool Enabled { get; set; } = false;

    public string ConfigVersion { get; set; } = "v1";

    public int ConfirmWaitDurationSeconds { get; set; } = 180;

    public decimal MinMoveBps { get; set; } = 35m;

    public decimal MinEdge { get; set; } = 0.06m;

    public decimal BaseFairProbability { get; set; } = 0.90m;

    public decimal MaxFairProbability { get; set; } = 0.99m;

    public decimal FairProbabilityPerMoveBps { get; set; } = 0.0002m;

    public decimal MaxSlippagePct { get; set; } = 0.015m;

    public int MaxDataStalenessSeconds { get; set; } = 3;

    public int MaxBaselineSpotAgeSeconds { get; set; } = 10;

    public int MaxOrderBookAgeSeconds { get; set; } = 3;

    public int MaxOrderAgeSeconds { get; set; } = 8;

    public int MaxHoldSeconds { get; set; } = 600;

    public int MaxMarkets { get; set; } = 12;

    public decimal MinLiquidity { get; set; } = 250m;

    public decimal MinVolume24h { get; set; } = 0m;

    public decimal MaxNotionalPerMarket { get; set; } = 50m;

    public decimal MaxNotionalPerOrder { get; set; } = 15m;

    public decimal DefaultOrderQuantity { get; set; } = 5m;

    public decimal MinOrderQuantity { get; set; } = 1m;

    public int EntryCooldownSeconds { get; set; } = 15;

    public bool RequireConfirmedOracle { get; set; } = false;

    public bool TriggerKillSwitchOnOracleMismatch { get; set; } = true;

    public string[] AllowedSpotSources { get; set; } =
    [
        "rtds:crypto_prices",
        "rtds:crypto_prices_chainlink"
    ];

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ConfigVersion))
        {
            throw new ArgumentException("ConfigVersion cannot be empty.", nameof(ConfigVersion));
        }

        if (ConfirmWaitDurationSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ConfirmWaitDurationSeconds), ConfirmWaitDurationSeconds,
                "ConfirmWaitDurationSeconds must be non-negative.");
        }

        if (MinMoveBps <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(MinMoveBps), MinMoveBps, "MinMoveBps must be positive.");
        }

        if (MinEdge <= 0m || MinEdge >= 1m)
        {
            throw new ArgumentOutOfRangeException(nameof(MinEdge), MinEdge, "MinEdge must be between 0 and 1.");
        }

        if (BaseFairProbability <= 0m || BaseFairProbability >= 1m ||
            MaxFairProbability <= 0m || MaxFairProbability >= 1m ||
            BaseFairProbability > MaxFairProbability)
        {
            throw new ArgumentOutOfRangeException(nameof(BaseFairProbability), BaseFairProbability,
                "Fair probability bounds must be in 0..1 and BaseFairProbability <= MaxFairProbability.");
        }

        if (FairProbabilityPerMoveBps < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(FairProbabilityPerMoveBps), FairProbabilityPerMoveBps,
                "FairProbabilityPerMoveBps must be non-negative.");
        }

        if (MaxSlippagePct < 0m || MaxSlippagePct >= 1m)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxSlippagePct), MaxSlippagePct,
                "MaxSlippagePct must be between 0 and 1.");
        }

        if (MaxDataStalenessSeconds <= 0 ||
            MaxBaselineSpotAgeSeconds <= 0 ||
            MaxOrderBookAgeSeconds <= 0 ||
            MaxOrderAgeSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxDataStalenessSeconds), MaxDataStalenessSeconds,
                "Staleness, baseline, and order age limits must be positive.");
        }

        if (MaxHoldSeconds < 0 || MaxMarkets <= 0 || EntryCooldownSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxMarkets), MaxMarkets,
                "Hold, market, and cooldown settings are invalid.");
        }

        if (MaxNotionalPerMarket <= 0m || MaxNotionalPerOrder <= 0m ||
            DefaultOrderQuantity <= 0m || MinOrderQuantity <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxNotionalPerMarket), MaxNotionalPerMarket,
                "Notional and quantity settings must be positive.");
        }

        if (AllowedSpotSources is null || AllowedSpotSources.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("AllowedSpotSources cannot contain empty values.", nameof(AllowedSpotSources));
        }
    }

    public bool IsOracleAccepted(MarketWindowSpec spec)
        => !RequireConfirmedOracle || spec.OracleStatus == MarketWindowOracleStatus.Confirmed;
}
