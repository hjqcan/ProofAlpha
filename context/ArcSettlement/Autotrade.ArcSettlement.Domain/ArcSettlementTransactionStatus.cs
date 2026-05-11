namespace Autotrade.ArcSettlement.Domain;

public enum ArcSettlementTransactionStatus
{
    Pending = 0,
    Submitted = 1,
    Confirmed = 2,
    Failed = 3,
    Abandoned = 4
}
