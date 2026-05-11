namespace Autotrade.ArcSettlement.Infra.Evm;

public sealed record ArcEvmWriteIntent(
    string ContractName,
    string MethodName,
    string DomainIntentId,
    string PayloadHash);
