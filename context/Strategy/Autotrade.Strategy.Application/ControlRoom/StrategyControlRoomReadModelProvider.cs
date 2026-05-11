using Autotrade.Strategy.Application.Contract.ControlRoom;
using Autotrade.Strategy.Application.Contract.Strategies;
using Autotrade.Strategy.Application.Engine;
using Autotrade.Strategy.Application.Strategies.DualLeg;
using Autotrade.Strategy.Application.Strategies.Endgame;
using Autotrade.Strategy.Application.Strategies.LiquidityMaking;
using Autotrade.Strategy.Application.Strategies.LiquidityPulse;
using Autotrade.Strategy.Application.Strategies.Opportunity;
using Autotrade.Strategy.Application.Strategies.RepricingLag;
using Autotrade.Strategy.Application.Strategies.Volatility;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Autotrade.Strategy.Application.ControlRoom;

public sealed class StrategyControlRoomReadModelProvider(
    IServiceProvider serviceProvider,
    IOptionsMonitor<StrategyEngineOptions> strategyEngineOptions,
    IOptionsMonitor<DualLegArbitrageOptions> dualLegOptions,
    IOptionsMonitor<EndgameSweepOptions> endgameOptions,
    IOptionsMonitor<LiquidityPulseOptions> liquidityPulseOptions,
    IOptionsMonitor<LiquidityMakerOptions> liquidityMakerOptions,
    IOptionsMonitor<MicroVolatilityScalperOptions> microVolatilityOptions,
    IOptionsMonitor<RepricingLagArbitrageOptions> repricingLagOptions,
    IOptionsMonitor<LlmOpportunityOptions> llmOpportunityOptions) : IStrategyControlRoomReadModelProvider
{
    private const string ModelVersion = "strategy-control-room-read-model.v1";
    private const string SourceVersion = "strategy-context.v1";

    public async Task<StrategyControlRoomReadModel> GetReadModelAsync(
        CancellationToken cancellationToken = default)
    {
        var strategies = await BuildStrategiesAsync(cancellationToken).ConfigureAwait(false);
        return new StrategyControlRoomReadModel(ModelVersion, SourceVersion, strategies);
    }

    private async Task<IReadOnlyList<StrategyControlRoomCard>> BuildStrategiesAsync(
        CancellationToken cancellationToken)
    {
        var manager = serviceProvider.GetService<IStrategyManager>();
        if (manager is not null)
        {
            var statuses = await manager.GetStatusesAsync(cancellationToken).ConfigureAwait(false);
            return manager
                .GetRegisteredStrategies()
                .OrderBy(item => item.StrategyId, StringComparer.OrdinalIgnoreCase)
                .Select(descriptor =>
                {
                    var status = statuses.FirstOrDefault(item =>
                        string.Equals(item.StrategyId, descriptor.StrategyId, StringComparison.OrdinalIgnoreCase));
                    return ToCard(descriptor, status, manager.GetDesiredState(descriptor.StrategyId));
                })
                .ToArray();
        }

        var factory = serviceProvider.GetService<IStrategyFactory>();
        if (factory is not null)
        {
            var engine = strategyEngineOptions.CurrentValue;
            return factory
                .GetDescriptors()
                .OrderBy(item => item.StrategyId, StringComparer.OrdinalIgnoreCase)
                .Select(descriptor => ToCard(descriptor, status: null, GetConfiguredDesiredState(descriptor.StrategyId, engine)))
                .ToArray();
        }

        return BuildConfiguredFallbackStrategies();
    }

    private StrategyControlRoomCard ToCard(
        StrategyDescriptor descriptor,
        StrategyStatus? status,
        StrategyState desiredState)
    {
        var state = status?.State ?? StrategyState.Created;
        var effectiveDesiredState = status?.DesiredState ?? desiredState;

        return new StrategyControlRoomCard(
            descriptor.StrategyId,
            descriptor.Name,
            state,
            descriptor.Enabled,
            descriptor.ConfigVersion,
            effectiveDesiredState.ToString(),
            status?.ActiveMarkets?.Count ?? 0,
            status?.CycleCount ?? 0,
            status?.SnapshotsProcessed ?? 0,
            status?.ChannelBacklog ?? 0,
            status?.IsKillSwitchBlocked ?? false,
            status?.LastHeartbeatUtc,
            status?.LastDecisionAtUtc,
            status?.LastError,
            status?.BlockedReason ?? ResolveBlockedReason(descriptor, status),
            BuildStrategyParameters(descriptor.StrategyId),
            ModelVersion,
            SourceVersion);
    }

    private IReadOnlyList<StrategyControlRoomCard> BuildConfiguredFallbackStrategies()
    {
        var engine = strategyEngineOptions.CurrentValue;
        return
        [
            BuildConfiguredStrategy(
                "dual_leg_arbitrage",
                "Dual Leg Arbitrage",
                dualLegOptions.CurrentValue.Enabled,
                dualLegOptions.CurrentValue.ConfigVersion,
                GetConfiguredDesiredState("dual_leg_arbitrage", engine),
                BuildStrategyParameters("dual_leg_arbitrage")),
            BuildConfiguredStrategy(
                "endgame_sweep",
                "Endgame Sweep",
                endgameOptions.CurrentValue.Enabled,
                endgameOptions.CurrentValue.ConfigVersion,
                GetConfiguredDesiredState("endgame_sweep", engine),
                BuildStrategyParameters("endgame_sweep")),
            BuildConfiguredStrategy(
                "liquidity_maker",
                "Liquidity Maker",
                liquidityMakerOptions.CurrentValue.Enabled,
                liquidityMakerOptions.CurrentValue.ConfigVersion,
                GetConfiguredDesiredState("liquidity_maker", engine),
                BuildStrategyParameters("liquidity_maker")),
            BuildConfiguredStrategy(
                "liquidity_pulse",
                "Liquidity Pulse",
                liquidityPulseOptions.CurrentValue.Enabled,
                liquidityPulseOptions.CurrentValue.ConfigVersion,
                GetConfiguredDesiredState("liquidity_pulse", engine),
                BuildStrategyParameters("liquidity_pulse")),
            BuildConfiguredStrategy(
                "micro_volatility_scalper",
                "Micro Volatility Scalper",
                microVolatilityOptions.CurrentValue.Enabled,
                microVolatilityOptions.CurrentValue.ConfigVersion,
                GetConfiguredDesiredState("micro_volatility_scalper", engine),
                BuildStrategyParameters("micro_volatility_scalper")),
            BuildConfiguredStrategy(
                "repricing_lag_arbitrage",
                "Repricing Lag Arbitrage",
                repricingLagOptions.CurrentValue.Enabled,
                repricingLagOptions.CurrentValue.ConfigVersion,
                GetConfiguredDesiredState("repricing_lag_arbitrage", engine),
                BuildStrategyParameters("repricing_lag_arbitrage")),
            BuildConfiguredStrategy(
                "llm_opportunity",
                "LLM Opportunity",
                llmOpportunityOptions.CurrentValue.Enabled,
                llmOpportunityOptions.CurrentValue.ConfigVersion,
                GetConfiguredDesiredState("llm_opportunity", engine),
                BuildStrategyParameters("llm_opportunity"))
        ];
    }

    private static StrategyControlRoomCard BuildConfiguredStrategy(
        string strategyId,
        string name,
        bool enabled,
        string configVersion,
        StrategyState desiredState,
        IReadOnlyList<StrategyControlRoomParameter> parameters)
    {
        return new StrategyControlRoomCard(
            strategyId,
            name,
            StrategyState.Stopped,
            enabled,
            configVersion,
            desiredState.ToString(),
            0,
            0,
            0,
            0,
            false,
            null,
            null,
            null,
            enabled ? null : StrategyBlockedReasons.DisabledConfig(strategyId),
            parameters,
            ModelVersion,
            SourceVersion);
    }

    private static StrategyBlockedReason? ResolveBlockedReason(
        StrategyDescriptor descriptor,
        StrategyStatus? status)
    {
        if (status?.BlockedReason is not null)
        {
            return status.BlockedReason;
        }

        if (status?.State == StrategyState.Faulted || !string.IsNullOrWhiteSpace(status?.LastError))
        {
            return StrategyBlockedReasons.StrategyFault(status?.LastError);
        }

        if (!descriptor.Enabled)
        {
            return StrategyBlockedReasons.DisabledConfig(descriptor.StrategyId);
        }

        if (status?.IsKillSwitchBlocked == true)
        {
            return StrategyBlockedReasons.KillSwitch();
        }

        return null;
    }

    private IReadOnlyList<StrategyControlRoomParameter> BuildStrategyParameters(string strategyId)
    {
        if (string.Equals(strategyId, "dual_leg_arbitrage", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                new StrategyControlRoomParameter("Pair cost", dualLegOptions.CurrentValue.PairCostThreshold.ToString("0.000")),
                new StrategyControlRoomParameter("Max markets", dualLegOptions.CurrentValue.MaxMarkets.ToString("0")),
                new StrategyControlRoomParameter("Hedge timeout", $"{dualLegOptions.CurrentValue.HedgeTimeoutSeconds}s")
            ];
        }

        if (string.Equals(strategyId, "endgame_sweep", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                new StrategyControlRoomParameter("Win probability", endgameOptions.CurrentValue.MinWinProbability.ToString("0.000")),
                new StrategyControlRoomParameter("Expiry window", $"{endgameOptions.CurrentValue.MaxSecondsToExpiry / 60}m"),
                new StrategyControlRoomParameter("Entry ceiling", endgameOptions.CurrentValue.MaxEntryPrice.ToString("0.000"))
            ];
        }

        if (string.Equals(strategyId, "liquidity_pulse", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                new StrategyControlRoomParameter("Dominance", liquidityPulseOptions.CurrentValue.MinBidDominance.ToString("0.000")),
                new StrategyControlRoomParameter("Max spread", liquidityPulseOptions.CurrentValue.MaxSpread.ToString("0.000")),
                new StrategyControlRoomParameter("Max markets", liquidityPulseOptions.CurrentValue.MaxMarkets.ToString("0"))
            ];
        }

        if (string.Equals(strategyId, "liquidity_maker", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                new StrategyControlRoomParameter("Spread", $"{liquidityMakerOptions.CurrentValue.MinSpread:0.000}-{liquidityMakerOptions.CurrentValue.MaxSpread:0.000}"),
                new StrategyControlRoomParameter("Max markets", liquidityMakerOptions.CurrentValue.MaxMarkets.ToString("0")),
                new StrategyControlRoomParameter("Order age", $"{liquidityMakerOptions.CurrentValue.MaxPassiveOrderAgeSeconds}s")
            ];
        }

        if (string.Equals(strategyId, "micro_volatility_scalper", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                new StrategyControlRoomParameter("Dip", microVolatilityOptions.CurrentValue.MinDipFromAverage.ToString("0.000")),
                new StrategyControlRoomParameter("Samples", $"{microVolatilityOptions.CurrentValue.MinSamples}/{microVolatilityOptions.CurrentValue.SampleWindowSize}"),
                new StrategyControlRoomParameter("Max spread", microVolatilityOptions.CurrentValue.MaxSpread.ToString("0.000"))
            ];
        }

        if (string.Equals(strategyId, "repricing_lag_arbitrage", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                new StrategyControlRoomParameter("Min move", $"{repricingLagOptions.CurrentValue.MinMoveBps:0} bps"),
                new StrategyControlRoomParameter("Min edge", repricingLagOptions.CurrentValue.MinEdge.ToString("0.000")),
                new StrategyControlRoomParameter("Max markets", repricingLagOptions.CurrentValue.MaxMarkets.ToString("0"))
            ];
        }

        if (string.Equals(strategyId, "llm_opportunity", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                new StrategyControlRoomParameter("Max markets", llmOpportunityOptions.CurrentValue.MaxMarkets.ToString("0")),
                new StrategyControlRoomParameter("Entry cooldown", $"{llmOpportunityOptions.CurrentValue.EntryCooldownSeconds}s"),
                new StrategyControlRoomParameter("Max slippage", llmOpportunityOptions.CurrentValue.MaxSlippage.ToString("0.000"))
            ];
        }

        return Array.Empty<StrategyControlRoomParameter>();
    }

    private StrategyState GetConfiguredDesiredState(string strategyId, StrategyEngineOptions engine)
    {
        if (engine.DesiredStates.TryGetValue(strategyId, out var desired))
        {
            return desired;
        }

        return engine.Enabled ? StrategyState.Running : StrategyState.Stopped;
    }
}
