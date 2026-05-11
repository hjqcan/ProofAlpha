// ============================================================================
// 策略工厂
// ============================================================================
// 负责策略实例的创建和描述信息的获取。
// 通过策略注册列表动态创建策略实例。
// ============================================================================

using Autotrade.Strategy.Application.Contract.Strategies;

namespace Autotrade.Strategy.Application.Engine;

/// <summary>
/// 策略工厂接口。
/// </summary>
public interface IStrategyFactory
{
    /// <summary>
    /// 创建策略实例。
    /// </summary>
    ITradingStrategy CreateStrategy(string strategyId, StrategyContext context);

    /// <summary>
    /// 获取策略配置快照。
    /// </summary>
    StrategyOptionsSnapshot GetOptions(string strategyId);

    /// <summary>
    /// 获取所有策略描述符。
    /// </summary>
    IReadOnlyList<StrategyDescriptor> GetDescriptors();
}

/// <summary>
/// Supplies strategy registrations that may change while the process is running.
/// </summary>
public interface IStrategyRegistrationProvider
{
    IReadOnlyList<StrategyRegistration> GetRegistrations();
}

/// <summary>
/// 策略工厂。
/// </summary>
public sealed class StrategyFactory : IStrategyFactory
{
    private readonly IEnumerable<StrategyRegistration> _staticRegistrations;
    private readonly IEnumerable<IStrategyRegistrationProvider> _registrationProviders;
    private readonly IServiceProvider _serviceProvider;

    public StrategyFactory(
        IEnumerable<StrategyRegistration> registrations,
        IEnumerable<IStrategyRegistrationProvider> registrationProviders,
        IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        ArgumentNullException.ThrowIfNull(registrationProviders);
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _staticRegistrations = registrations;
        _registrationProviders = registrationProviders;
    }

    public ITradingStrategy CreateStrategy(string strategyId, StrategyContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!BuildRegistrations().TryGetValue(strategyId, out var registration))
        {
            throw new InvalidOperationException($"Unknown strategy id: {strategyId}");
        }

        return registration.Factory(_serviceProvider, context);
    }

    public StrategyOptionsSnapshot GetOptions(string strategyId)
    {
        if (!BuildRegistrations().TryGetValue(strategyId, out var registration))
        {
            throw new InvalidOperationException($"Unknown strategy id: {strategyId}");
        }

        return registration.OptionsProvider(_serviceProvider);
    }

    public IReadOnlyList<StrategyDescriptor> GetDescriptors()
    {
        return BuildRegistrations().Values
            .Select(r =>
            {
                var options = r.OptionsProvider(_serviceProvider);
                return new StrategyDescriptor(r.StrategyId, r.Name, options.Enabled, options.ConfigVersion, r.OptionsSectionName);
            })
            .OrderBy(d => d.StrategyId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private IReadOnlyDictionary<string, StrategyRegistration> BuildRegistrations()
    {
        var registrations = new Dictionary<string, StrategyRegistration>(StringComparer.OrdinalIgnoreCase);
        foreach (var registration in _staticRegistrations)
        {
            registrations[registration.StrategyId] = registration;
        }

        foreach (var provider in _registrationProviders)
        {
            foreach (var registration in provider.GetRegistrations())
            {
                if (registrations.ContainsKey(registration.StrategyId))
                {
                    throw new InvalidOperationException(
                        $"Duplicate strategy registration id: {registration.StrategyId}");
                }

                registrations[registration.StrategyId] = registration;
            }
        }

        return registrations;
    }
}
