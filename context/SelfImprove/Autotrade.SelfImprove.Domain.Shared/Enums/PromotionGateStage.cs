namespace Autotrade.SelfImprove.Domain.Shared.Enums;

public enum PromotionGateStage
{
    StaticValidation = 0,
    UnitTest = 1,
    Replay = 2,
    Shadow = 3,
    Paper = 4,
    LiveCanary = 5
}
