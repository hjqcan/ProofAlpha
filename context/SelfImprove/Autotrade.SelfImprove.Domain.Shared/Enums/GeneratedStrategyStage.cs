namespace Autotrade.SelfImprove.Domain.Shared.Enums;

public enum GeneratedStrategyStage
{
    Generated = 0,
    StaticValidated = 1,
    UnitTested = 2,
    ReplayValidated = 3,
    ShadowRunning = 4,
    PaperRunning = 5,
    LiveCanary = 6,
    Promoted = 7,
    RolledBack = 8,
    Quarantined = 9
}
