namespace Autotrade.Trading.Application.Contract.Compliance;

public sealed class ComplianceOptions
{
    public const string SectionName = "Compliance";

    public bool Enabled { get; set; } = true;

    public bool GeoKycAllowed { get; set; }

    public bool AllowUnsafeLiveParameters { get; set; }

    public int MinLiveEvaluationIntervalSeconds { get; set; } = 1;

    public int MaxLiveOrdersPerCycle { get; set; } = 10;

    public int MaxLiveOpenOrders { get; set; } = 100;

    public int MaxLiveOpenOrdersPerMarket { get; set; } = 20;

    public int MinLiveReconciliationIntervalSeconds { get; set; } = 5;

    public decimal MaxLiveCapitalPerMarket { get; set; } = 0.25m;

    public decimal MaxLiveCapitalPerStrategy { get; set; } = 0.50m;

    public decimal MaxLiveTotalCapitalUtilization { get; set; } = 0.80m;
}

public sealed class ComplianceStrategyEngineOptions
{
    public const string SectionName = "StrategyEngine";

    public int EvaluationIntervalSeconds { get; set; } = 2;

    public int MaxOrdersPerCycle { get; set; } = 4;
}
