namespace Autotrade.SelfImprove.Domain.Shared.Enums;

public enum ImprovementRunStatus
{
    Created = 0,
    EpisodeBuilt = 1,
    Analyzed = 2,
    ManualReview = 3,
    PatchApplied = 4,
    CodeGenerated = 5,
    ValidationFailed = 6,
    Completed = 7,
    Failed = 8
}
