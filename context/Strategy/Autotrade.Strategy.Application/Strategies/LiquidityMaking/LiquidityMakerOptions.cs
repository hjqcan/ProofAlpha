namespace Autotrade.Strategy.Application.Strategies.LiquidityMaking;

public sealed class LiquidityMakerOptions
{
    public const string SectionName = "Strategies:LiquidityMaker";

    public bool Enabled { get; set; } = true;

    public string ConfigVersion { get; set; } = "v1";

    public decimal MinLiquidity { get; set; } = 2500m;

    public decimal MinVolume24h { get; set; } = 1000m;

    public int MaxMarkets { get; set; } = 30;

    public decimal MinEntryPrice { get; set; } = 0.04m;

    public decimal MaxEntryPrice { get; set; } = 0.96m;

    public decimal MinSpread { get; set; } = 0.015m;

    public decimal MaxSpread { get; set; } = 0.08m;

    public decimal QuoteImproveTicks { get; set; } = 0.001m;

    public decimal MinTopSize { get; set; } = 5m;

    public decimal MaxNotionalPerMarket { get; set; } = 20m;

    public decimal MaxNotionalPerOrder { get; set; } = 4m;

    public decimal DefaultOrderQuantity { get; set; } = 2m;

    public decimal MinOrderQuantity { get; set; } = 1m;

    public int EntryCooldownSeconds { get; set; } = 180;

    public int ExitCooldownSeconds { get; set; } = 30;

    public int MaxPassiveOrderAgeSeconds { get; set; } = 120;

    public int MaxHoldSeconds { get; set; } = 1200;

    public decimal TakeProfitPriceDelta { get; set; } = 0.025m;

    public decimal StopLossPriceDelta { get; set; } = 0.04m;

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

        if (MinSpread <= 0m || MaxSpread <= 0m || MinSpread >= MaxSpread)
        {
            throw new ArgumentOutOfRangeException(nameof(MinSpread), MinSpread,
                "Spread bounds must be positive and MinSpread < MaxSpread.");
        }

        if (QuoteImproveTicks <= 0m || QuoteImproveTicks >= 0.10m)
        {
            throw new ArgumentOutOfRangeException(nameof(QuoteImproveTicks), QuoteImproveTicks,
                "QuoteImproveTicks must be positive and less than 0.10.");
        }

        if (MinTopSize <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(MinTopSize), MinTopSize, "MinTopSize must be positive.");
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

        if (EntryCooldownSeconds < 0 || ExitCooldownSeconds < 0 ||
            MaxPassiveOrderAgeSeconds < 0 || MaxHoldSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(EntryCooldownSeconds), EntryCooldownSeconds,
                "Timing settings must be non-negative.");
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
