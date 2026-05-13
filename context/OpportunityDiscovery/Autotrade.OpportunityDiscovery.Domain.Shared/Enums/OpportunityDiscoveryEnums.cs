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
    Manual = 4,
    CryptoPriceOracle = 5,
    MacroOfficial = 6,
    SecFilings = 7,
    WeatherAlerts = 8,
    SportsOfficial = 9,
    ElectionOfficial = 10,
    Polling = 11
}

public enum SourceAuthorityKind
{
    Unknown = 0,
    Official = 1,
    PrimaryExchange = 2,
    Regulator = 3,
    DataOracle = 4,
    Aggregator = 5,
    News = 6,
    Search = 7,
    Manual = 8
}

public enum SourceObservationKind
{
    EvidenceIngested = 0,
    ConflictDetected = 1,
    OfficialConfirmation = 2,
    GateContribution = 3,
    ReliabilityAdjustment = 4,
    IngestionFailure = 5
}

public enum EvidenceSnapshotLiveGateStatus
{
    Blocked = 0,
    Eligible = 1
}

public enum EvidenceConflictSeverity
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3
}

public enum EvidenceConfirmationKind
{
    OfficialApi = 0,
    StrongMultiSource = 1,
    ManualOfficialReview = 2
}

public enum OpportunityHypothesisStatus
{
    Discovered = 0,
    Scored = 1,
    BacktestPassed = 2,
    PaperValidated = 3,
    LiveEligible = 4,
    LivePublished = 5,
    Suspended = 6,
    Expired = 7
}

public enum OpportunityEvaluationKind
{
    Scoring = 0,
    Backtest = 1,
    Shadow = 2,
    Paper = 3,
    Promotion = 4,
    Live = 5
}

public enum OpportunityEvaluationRunStatus
{
    Running = 0,
    Succeeded = 1,
    Failed = 2
}

public enum OpportunityPromotionGateKind
{
    Evidence = 0,
    Backtest = 1,
    Paper = 2,
    ExecutionQuality = 3,
    Risk = 4,
    Compliance = 5,
    Shadow = 6
}

public enum OpportunityPromotionGateStatus
{
    Pending = 0,
    Passed = 1,
    Failed = 2
}

public enum ExecutableOpportunityPolicyStatus
{
    Draft = 0,
    Active = 1,
    Suspended = 2,
    Expired = 3
}

public enum OpportunityLiveAllocationStatus
{
    Active = 0,
    Suspended = 1,
    Expired = 2
}

public enum OpportunityType
{
    InformationAsymmetry = 0,
    OrderBookMicrostructure = 1,
    CrossMarketConsistency = 2,
    DelayedRepricing = 3,
    NearResolution = 4,
    LiquidityMismatch = 5
}
