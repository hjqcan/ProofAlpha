using Autotrade.Infra.BackgroundJobs.Core;
using Autotrade.Trading.Application.Contract.Repositories;
using Autotrade.Trading.Application.Maintenance;
using Hangfire;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Autotrade.Trading.Infra.BackgroundJobs.Jobs;

/// <summary>
/// Trading 数据保留清理任务（Hangfire）。
/// </summary>
[DisableConcurrentExecution(timeoutInSeconds: 30 * 60)]
public sealed class TradingRetentionJob : JobBase<TradingRetentionJob>
{
    private readonly ITradingMaintenanceRepository _maintenanceRepository;
    private readonly TradingRetentionOptions _options;

    public TradingRetentionJob(
        ITradingMaintenanceRepository maintenanceRepository,
        IOptions<TradingRetentionOptions> options,
        Microsoft.Extensions.Logging.ILogger<TradingRetentionJob> logger)
        : base(logger)
    {
        _maintenanceRepository = maintenanceRepository ?? throw new ArgumentNullException(nameof(maintenanceRepository));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    protected override async Task ExecuteJobAsync(CancellationToken cancellationToken)
    {
        _options.Validate();

        // 全部为 0 时不清理
        if (_options.OrderRetentionDays == 0 &&
            _options.TradeRetentionDays == 0 &&
            _options.OrderEventRetentionDays == 0 &&
            _options.RiskEventRetentionDays == 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;

        DateTimeOffset? ordersCutoff = _options.OrderRetentionDays > 0 ? now.AddDays(-_options.OrderRetentionDays) : null;
        DateTimeOffset? tradesCutoff = _options.TradeRetentionDays > 0 ? now.AddDays(-_options.TradeRetentionDays) : null;
        DateTimeOffset? eventsCutoff = _options.OrderEventRetentionDays > 0 ? now.AddDays(-_options.OrderEventRetentionDays) : null;
        DateTimeOffset? riskEventsCutoff = _options.RiskEventRetentionDays > 0 ? now.AddDays(-_options.RiskEventRetentionDays) : null;

        var deleted = await _maintenanceRepository
            .CleanupAsync(ordersCutoff, tradesCutoff, eventsCutoff, riskEventsCutoff, cancellationToken)
            .ConfigureAwait(false);

        if (deleted > 0)
        {
            Logger.LogInformation("Trading retention cleanup completed: {DeletedCount} records deleted", deleted);
        }
    }
}

