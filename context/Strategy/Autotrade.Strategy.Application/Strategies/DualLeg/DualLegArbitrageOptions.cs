using Autotrade.Trading.Application.Contract.Risk;

namespace Autotrade.Strategy.Application.Strategies.DualLeg;

public sealed class DualLegArbitrageOptions
{
    public const string SectionName = "Strategies:DualLegArbitrage";

    public bool Enabled { get; set; } = true;

    public string ConfigVersion { get; set; } = "v1";

    public decimal PairCostThreshold { get; set; } = 0.98m;

    public decimal ExitPairValueThreshold { get; set; } = 1.01m;

    public decimal MinLiquidity { get; set; } = 1000m;

    public decimal MinVolume24h { get; set; } = 500m;

    /// <summary>
    /// Minimum time to expiry in minutes. Markets expiring sooner will be excluded.
    /// </summary>
    public int MinTimeToExpiryMinutes { get; set; } = 60;

    public int MaxMarkets { get; set; } = 20;

    public decimal MaxNotionalPerMarket { get; set; } = 50m;

    public decimal MaxNotionalPerOrder { get; set; } = 10m;

    public decimal DefaultOrderQuantity { get; set; } = 5m;

    public decimal MinOrderQuantity { get; set; } = 1m;

    public int EntryCooldownSeconds { get; set; } = 30;

    public int MaxHoldSeconds { get; set; } = 3600;

    /// <summary>
    /// Maximum seconds to wait for the second leg to fill after the first leg is filled.
    /// </summary>
    public int HedgeTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Action to take when hedge timeout occurs.
    /// </summary>
    public UnhedgedExitAction HedgeTimeoutAction { get; set; } = UnhedgedExitAction.CancelAndExit;

    public decimal MaxSlippage { get; set; } = 0.02m;

    /// <summary>
    /// Maximum age of order book data in seconds. Older snapshots will be ignored.
    /// </summary>
    public int MaxOrderBookAgeSeconds { get; set; } = 10;

    /// <summary>
    /// If true, use sequential order mode: first leg submits, waits for fill, then submits second leg.
    /// If false, both legs are submitted simultaneously (original behavior).
    /// </summary>
    public bool SequentialOrderMode { get; set; } = true;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ConfigVersion))
        {
            throw new ArgumentException("ConfigVersion cannot be empty.", nameof(ConfigVersion));
        }

        if (PairCostThreshold <= 0m || PairCostThreshold >= 1m)
        {
            throw new ArgumentOutOfRangeException(nameof(PairCostThreshold), PairCostThreshold,
                "PairCostThreshold must be between 0 and 1.");
        }

        if (ExitPairValueThreshold <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(ExitPairValueThreshold), ExitPairValueThreshold,
                "ExitPairValueThreshold must be positive.");
        }

        if (MinLiquidity < 0m || MinVolume24h < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(MinLiquidity), MinLiquidity,
                "MinLiquidity and MinVolume24h must be non-negative.");
        }

        if (MaxMarkets <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxMarkets), MaxMarkets, "MaxMarkets must be positive.");
        }

        if (MaxNotionalPerMarket <= 0m || MaxNotionalPerOrder <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxNotionalPerMarket), MaxNotionalPerMarket,
                "Notional limits must be positive.");
        }

        if (DefaultOrderQuantity <= 0m || MinOrderQuantity <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(DefaultOrderQuantity), DefaultOrderQuantity,
                "Order quantities must be positive.");
        }

        if (EntryCooldownSeconds < 0 || MaxHoldSeconds < 0 || HedgeTimeoutSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(EntryCooldownSeconds), EntryCooldownSeconds,
                "Timeout values must be non-negative.");
        }

        if (MaxSlippage < 0m || MaxSlippage >= 1m)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxSlippage), MaxSlippage,
                "MaxSlippage must be between 0 and 1.");
        }

        if (MaxOrderBookAgeSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxOrderBookAgeSeconds), MaxOrderBookAgeSeconds,
                "MaxOrderBookAgeSeconds must be non-negative.");
        }

        if (MinTimeToExpiryMinutes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MinTimeToExpiryMinutes), MinTimeToExpiryMinutes,
                "MinTimeToExpiryMinutes must be non-negative.");
        }
    }
}
