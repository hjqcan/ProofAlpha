namespace Autotrade.Strategy.Application.Strategies.LiquidityPulse;

public sealed class LiquidityPulseOptions
{
    public const string SectionName = "Strategies:LiquidityPulse";

    public bool Enabled { get; set; } = true;

    public string ConfigVersion { get; set; } = "v1";

    public decimal MinLiquidity { get; set; } = 2500m;

    public decimal MinVolume24h { get; set; } = 500m;

    public int MaxMarkets { get; set; } = 40;

    public decimal MinEntryPrice { get; set; } = 0.08m;

    public decimal MaxEntryPrice { get; set; } = 0.92m;

    public decimal MaxSpread { get; set; } = 0.05m;

    public decimal MinTopSize { get; set; } = 3m;

    public decimal MinBidDominance { get; set; } = 0.58m;

    public decimal MaxNotionalPerMarket { get; set; } = 25m;

    public decimal MaxNotionalPerOrder { get; set; } = 5m;

    public decimal DefaultOrderQuantity { get; set; } = 2m;

    public decimal MinOrderQuantity { get; set; } = 1m;

    public int EntryCooldownSeconds { get; set; } = 120;

    public int ExitCooldownSeconds { get; set; } = 30;

    public int MaxHoldSeconds { get; set; } = 900;

    public decimal TakeProfitPriceDelta { get; set; } = 0.03m;

    public decimal StopLossPriceDelta { get; set; } = 0.05m;

    public decimal MaxSlippage { get; set; } = 0.01m;

    public int MaxOrderBookAgeSeconds { get; set; } = 10;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ConfigVersion))
        {
            throw new ArgumentException("ConfigVersion cannot be empty.", nameof(ConfigVersion));
        }

        if (MinLiquidity < 0m || MinVolume24h < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(MinLiquidity), MinLiquidity,
                "Liquidity and volume filters must be non-negative.");
        }

        if (MaxMarkets <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxMarkets), MaxMarkets, "MaxMarkets must be positive.");
        }

        if (MinEntryPrice < 0.01m || MaxEntryPrice > 0.99m || MinEntryPrice >= MaxEntryPrice)
        {
            throw new ArgumentOutOfRangeException(nameof(MinEntryPrice), MinEntryPrice,
                "Entry price bounds must be within 0.01..0.99 and MinEntryPrice < MaxEntryPrice.");
        }

        if (MaxSpread <= 0m || MaxSpread >= 1m)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxSpread), MaxSpread, "MaxSpread must be between 0 and 1.");
        }

        if (MinTopSize <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(MinTopSize), MinTopSize, "MinTopSize must be positive.");
        }

        if (MinBidDominance <= 0.5m || MinBidDominance >= 1m)
        {
            throw new ArgumentOutOfRangeException(nameof(MinBidDominance), MinBidDominance,
                "MinBidDominance must be between 0.5 and 1.");
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

        if (EntryCooldownSeconds < 0 || ExitCooldownSeconds < 0 || MaxHoldSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(EntryCooldownSeconds), EntryCooldownSeconds,
                "Cooldown and hold settings must be non-negative.");
        }

        if (TakeProfitPriceDelta <= 0m || StopLossPriceDelta <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(TakeProfitPriceDelta), TakeProfitPriceDelta,
                "Profit and stop deltas must be positive.");
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
    }
}
