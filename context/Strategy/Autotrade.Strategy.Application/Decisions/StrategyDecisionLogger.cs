// ============================================================================
// 策略决策日志记录器
// ============================================================================
// 将策略执行的决策信息持久化到数据库，用于审计和分析。
// ============================================================================

using System.Diagnostics;
using Autotrade.Application.RunSessions;
using Autotrade.Strategy.Application.Contract.Strategies;
using Autotrade.Strategy.Application.Engine;
using Autotrade.Strategy.Domain.Entities;
using Autotrade.Trading.Application.Contract.Execution;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Autotrade.Strategy.Application.Decisions;

/// <summary>
/// 策略决策日志记录器。
/// 将策略决策持久化到数据库。
/// </summary>
public sealed class StrategyDecisionLogger : IStrategyDecisionLogger
{
    private readonly IStrategyDecisionRepository _repository;
    private readonly StrategyEngineOptions _options;
    private readonly ExecutionOptions _executionOptions;
    private readonly IRunSessionAccessor? _runSessionAccessor;
    private readonly ILogger<StrategyDecisionLogger> _logger;

    public StrategyDecisionLogger(
        IStrategyDecisionRepository repository,
        IOptions<StrategyEngineOptions> options,
        IOptions<ExecutionOptions> executionOptions,
        ILogger<StrategyDecisionLogger> logger,
        IRunSessionAccessor? runSessionAccessor = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _executionOptions = executionOptions?.Value ?? throw new ArgumentNullException(nameof(executionOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _runSessionAccessor = runSessionAccessor;
    }

    /// <summary>
    /// 记录策略决策。
    /// </summary>
    public async Task LogAsync(StrategyDecision decision, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(decision);

        if (!_options.DecisionLogEnabled)
        {
            return;
        }

        var correlationId = string.IsNullOrWhiteSpace(decision.CorrelationId)
            ? Activity.Current?.Id
            : decision.CorrelationId;

        var executionMode = string.IsNullOrWhiteSpace(decision.ExecutionMode)
            ? _executionOptions.Mode.ToString()
            : decision.ExecutionMode;

        var runSessionId = decision.RunSessionId
            ?? await ResolveRunSessionIdAsync(executionMode, cancellationToken).ConfigureAwait(false);

        var log = new StrategyDecisionLog(
            decision.StrategyId,
            decision.Action,
            decision.Reason,
            decision.MarketId,
            decision.ContextJson,
            decision.TimestampUtc,
            _options.ConfigVersion,
            correlationId,
            executionMode,
            runSessionId);

        try
        {
            await _repository.AddAsync(log, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 日志失败不应影响主流程
            _logger.LogWarning(ex, "Failed to persist strategy decision log: {StrategyId}", decision.StrategyId);
        }
    }

    private async Task<Guid?> ResolveRunSessionIdAsync(
        string executionMode,
        CancellationToken cancellationToken)
    {
        if (_runSessionAccessor is null)
        {
            return null;
        }

        try
        {
            var session = await _runSessionAccessor
                .GetCurrentAsync(executionMode, cancellationToken)
                .ConfigureAwait(false);

            return session?.SessionId;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve active run session for execution mode {ExecutionMode}", executionMode);
            return null;
        }
    }
}
