// ============================================================================
// 策略注册信息
// ============================================================================
// 策略注册的元数据和工厂函数。
// ============================================================================

using Autotrade.Strategy.Application.Contract.Strategies;

namespace Autotrade.Strategy.Application.Engine;

/// <summary>
/// 策略配置快照。
/// </summary>
/// <param name="StrategyId">策略 ID。</param>
/// <param name="Enabled">是否启用。</param>
/// <param name="ConfigVersion">配置版本。</param>
public sealed record StrategyOptionsSnapshot(
    string StrategyId,
    bool Enabled,
    string ConfigVersion);

/// <summary>
/// 策略注册信息。
/// </summary>
/// <param name="StrategyId">策略 ID。</param>
/// <param name="Name">策略名称。</param>
/// <param name="StrategyType">策略类型。</param>
/// <param name="OptionsSectionName">配置节名称。</param>
/// <param name="Factory">策略实例工厂函数。</param>
/// <param name="OptionsProvider">配置快照提供者。</param>
public sealed record StrategyRegistration(
    string StrategyId,
    string Name,
    Type StrategyType,
    string OptionsSectionName,
    Func<IServiceProvider, StrategyContext, ITradingStrategy> Factory,
    Func<IServiceProvider, StrategyOptionsSnapshot> OptionsProvider);
