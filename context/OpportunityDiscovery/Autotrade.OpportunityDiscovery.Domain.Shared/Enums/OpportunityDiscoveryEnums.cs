namespace Autotrade.OpportunityDiscovery.Domain.Shared.Enums;

public enum ResearchRunStatus
{
    Pending = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3
}

public enum OpportunityStatus
{
    Candidate = 0,
    NeedsReview = 1,
    Approved = 2,
    Rejected = 3,
    Published = 4,
    Expired = 5
}

public enum OpportunityReviewDecision
{
    Approve = 0,
    Reject = 1,
    Publish = 2
}

public enum OpportunityOutcomeSide
{
    Yes = 1,
    No = 2
}

public enum EvidenceSourceKind
{
    Polymarket = 0,
    Rss = 1,
    Gdelt = 2,
    OpenAiWebSearch = 3,
    Manual = 4
}
