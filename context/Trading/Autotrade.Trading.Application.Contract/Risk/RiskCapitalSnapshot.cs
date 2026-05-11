namespace Autotrade.Trading.Application.Contract.Risk;

/// <summary>
/// Capital snapshot for risk calculations.
/// </summary>
public sealed record RiskCapitalSnapshot(decimal TotalCapital, decimal AvailableCapital, decimal RealizedDailyPnl);
