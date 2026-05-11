using Autotrade.Strategy.Application.Contract.Strategies;

namespace Autotrade.SelfImprove.Application.Python;

public sealed record PythonStrategyManifest(
    string StrategyId,
    string Name,
    string Version,
    string ConfigVersion,
    string ArtifactRoot,
    string PackageHash,
    string EntryPoint,
    string ParameterSchemaJson,
    string RiskEnvelopeJson,
    IReadOnlyDictionary<string, object?> Parameters);

public sealed record PythonStrategyRequest(
    string StrategyId,
    string Phase,
    string MarketId,
    object MarketSnapshot,
    IReadOnlyDictionary<string, object?> Params,
    DateTimeOffset TimestampUtc,
    IReadOnlyDictionary<string, object?> State);

public sealed record PythonStrategyResponse(
    string Action,
    string ReasonCode,
    string Reason,
    IReadOnlyList<PythonOrderIntent> Intents,
    IReadOnlyDictionary<string, object?> Telemetry,
    IReadOnlyDictionary<string, object?> StatePatch);

public sealed record PythonOrderIntent(
    string MarketId,
    string TokenId,
    string Outcome,
    string Side,
    string OrderType,
    string TimeInForce,
    decimal Price,
    decimal Quantity,
    bool NegRisk,
    string Leg);

public interface IPythonStrategyRuntime
{
    Task<PythonStrategyResponse> EvaluateAsync(
        PythonStrategyManifest manifest,
        PythonStrategyRequest request,
        CancellationToken cancellationToken = default);
}

public interface IPythonStrategyAdapterFactory
{
    ITradingStrategy Create(PythonStrategyManifest manifest, StrategyContext context);
}
