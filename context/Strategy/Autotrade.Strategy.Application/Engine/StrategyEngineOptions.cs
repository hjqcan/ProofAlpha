namespace Autotrade.Strategy.Application.Engine;

/// <summary>
/// Global options for strategy engine.
/// </summary>
public sealed class StrategyEngineOptions
{
    public const string SectionName = "StrategyEngine";

    public bool Enabled { get; set; } = true;

    public int MaxConcurrentStrategies { get; set; } = 3;

    public int EvaluationIntervalSeconds { get; set; } = 2;

    public int OrderStatusPollIntervalSeconds { get; set; } = 3;

    public int MaxRestartAttempts { get; set; } = 3;

    public int RestartDelaySeconds { get; set; } = 5;

    public int MaxOrdersPerCycle { get; set; } = 4;

    public int SnapshotTimeoutSeconds { get; set; } = 5;

    public bool DecisionLogEnabled { get; set; } = true;

    public string ConfigVersion { get; set; } = "v1";

    /// <summary>
    /// 运行时期望状态（跨进程控制面）。
    /// 通过配置文件热更新：StrategyEngine:DesiredStates:{strategyId} = Running/Paused/Stopped
    /// </summary>
    public Dictionary<string, Autotrade.Strategy.Application.Contract.Strategies.StrategyState> DesiredStates { get; set; } = new();

    public void Validate()
    {
        if (MaxConcurrentStrategies <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxConcurrentStrategies), MaxConcurrentStrategies,
                "MaxConcurrentStrategies must be positive.");
        }

        if (EvaluationIntervalSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(EvaluationIntervalSeconds), EvaluationIntervalSeconds,
                "EvaluationIntervalSeconds must be positive.");
        }

        if (OrderStatusPollIntervalSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(OrderStatusPollIntervalSeconds), OrderStatusPollIntervalSeconds,
                "OrderStatusPollIntervalSeconds must be positive.");
        }

        if (MaxRestartAttempts < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxRestartAttempts), MaxRestartAttempts,
                "MaxRestartAttempts cannot be negative.");
        }

        if (RestartDelaySeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(RestartDelaySeconds), RestartDelaySeconds,
                "RestartDelaySeconds cannot be negative.");
        }

        if (MaxOrdersPerCycle <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxOrdersPerCycle), MaxOrdersPerCycle,
                "MaxOrdersPerCycle must be positive.");
        }

        if (SnapshotTimeoutSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(SnapshotTimeoutSeconds), SnapshotTimeoutSeconds,
                "SnapshotTimeoutSeconds must be positive.");
        }

        if (string.IsNullOrWhiteSpace(ConfigVersion))
        {
            throw new ArgumentException("ConfigVersion cannot be empty.", nameof(ConfigVersion));
        }

        foreach (var (strategyId, desired) in DesiredStates)
        {
            if (string.IsNullOrWhiteSpace(strategyId))
            {
                throw new ArgumentException("DesiredStates contains an empty strategy id.", nameof(DesiredStates));
            }

            if (desired is Autotrade.Strategy.Application.Contract.Strategies.StrategyState.Created
                or Autotrade.Strategy.Application.Contract.Strategies.StrategyState.Faulted)
            {
                throw new ArgumentException(
                    $"DesiredStates[{strategyId}] has an invalid target state: {desired}. Use Running/Paused/Stopped.",
                    nameof(DesiredStates));
            }
        }
    }
}
