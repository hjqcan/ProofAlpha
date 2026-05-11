using Autotrade.Trading.Application.Contract.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Autotrade.Trading.Application.Execution;

/// <summary>
/// 执行模式工厂：根据配置选择 Live 或 Paper 执行服务。
/// </summary>
public sealed class ExecutionModeFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ExecutionOptions _options;

    public ExecutionModeFactory(
        IServiceProvider serviceProvider,
        IOptions<ExecutionOptions> options)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// 创建执行服务实例。
    /// </summary>
    public IExecutionService Create()
    {
        return _options.Mode switch
        {
            ExecutionMode.Live => _serviceProvider.GetRequiredService<LiveExecutionService>(),
            ExecutionMode.Paper => _serviceProvider.GetRequiredService<PaperExecutionService>(),
            _ => throw new InvalidOperationException($"未知的执行模式: {_options.Mode}")
        };
    }

    /// <summary>
    /// 获取当前执行模式。
    /// </summary>
    public ExecutionMode CurrentMode => _options.Mode;
}
