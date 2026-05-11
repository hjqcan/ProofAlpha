namespace Autotrade.SelfImprove.Domain.Shared.Enums;

public enum PatchOutcomeStatus
{
    DryRunPassed = 0,
    Applied = 1,
    Rejected = 2,
    RolledBack = 3,
    Failed = 4
}
