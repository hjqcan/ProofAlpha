using Autotrade.Trading.Application.Execution;
using Microsoft.Extensions.Hosting;

namespace Autotrade.Trading.Infra.BackgroundJobs.Workers;

/// <summary>
/// Trading 账户启动初始化服务（fail-fast）：
/// - Live：使用 Polymarket:Clob:Address 作为账户 key
/// - Paper：使用固定账户 key（默认 "paper"）
/// 启动时确保 DB 中存在对应 TradingAccount，并把其 ID 缓存到 TradingAccountContext。
/// </summary>
public sealed class TradingAccountBootstrapWorker : IHostedService
{
    private readonly TradingAccountBootstrapper _bootstrapper;

    public TradingAccountBootstrapWorker(TradingAccountBootstrapper bootstrapper)
        => _bootstrapper = bootstrapper ?? throw new ArgumentNullException(nameof(bootstrapper));

    public Task StartAsync(CancellationToken cancellationToken)
        => _bootstrapper.BootstrapAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
}

