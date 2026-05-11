namespace Autotrade.Trading.Application.Contract.Risk;

/// <summary>
/// Capital snapshot configuration for risk checks.
/// </summary>
public sealed class RiskCapitalOptions
{
    public const string SectionName = "Risk:Capital";

    public decimal TotalCapital { get; set; } = 100000m;

    public decimal AvailableCapital { get; set; } = 100000m;

    public decimal RealizedDailyPnl { get; set; } = 0m;
}
