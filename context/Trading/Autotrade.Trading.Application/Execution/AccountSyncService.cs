using Autotrade.Polymarket.Abstractions;
using Autotrade.Polymarket.Models;
using Autotrade.Trading.Application.Contract.Accounts;
using Autotrade.Trading.Application.Contract.Repositories;
using Autotrade.Trading.Application.Execution;
using Autotrade.Trading.Domain.Shared.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Autotrade.Trading.Application.Execution;

/// <summary>
/// 账户同步服务实现。
/// 负责从 Polymarket API 同步外部余额、持仓、挂单等快照，并执行对账。
/// </summary>
public sealed class AccountSyncService : IAccountSyncService
{
    private readonly IPolymarketClobClient _clobClient;
    private readonly IPolymarketDataClient _dataClient;
    private readonly IPositionRepository _positionRepository;
    private readonly IOrderStateTracker _stateTracker;
    private readonly TradingAccountContext _accountContext;
    private readonly ExternalAccountSnapshotStore _snapshotStore;
    private readonly AccountSyncOptions _options;
    private readonly ILogger<AccountSyncService> _logger;

    public AccountSyncService(
        IPolymarketClobClient clobClient,
        IPolymarketDataClient dataClient,
        IPositionRepository positionRepository,
        IOrderStateTracker stateTracker,
        TradingAccountContext accountContext,
        ExternalAccountSnapshotStore snapshotStore,
        IOptions<AccountSyncOptions> options,
        ILogger<AccountSyncService> logger)
    {
        _clobClient = clobClient ?? throw new ArgumentNullException(nameof(clobClient));
        _dataClient = dataClient ?? throw new ArgumentNullException(nameof(dataClient));
        _positionRepository = positionRepository ?? throw new ArgumentNullException(nameof(positionRepository));
        _stateTracker = stateTracker ?? throw new ArgumentNullException(nameof(stateTracker));
        _accountContext = accountContext ?? throw new ArgumentNullException(nameof(accountContext));
        _snapshotStore = snapshotStore ?? throw new ArgumentNullException(nameof(snapshotStore));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public DateTimeOffset? LastSyncTime => _snapshotStore.LastSyncTime;

    /// <inheritdoc />
    public ExternalBalanceSnapshot? LastBalanceSnapshot
        => _snapshotStore.BalanceSnapshot;

    /// <inheritdoc />
    public IReadOnlyList<ExternalPositionSnapshot>? LastPositionsSnapshot
        => _snapshotStore.PositionsSnapshot;

    /// <inheritdoc />
    public async Task<BalanceSyncResult> SyncBalanceAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("开始同步外部余额...");

        try
        {
            var result = await _clobClient.GetBalanceAllowanceAsync(cancellationToken).ConfigureAwait(false);

            if (!result.IsSuccess)
            {
                _logger.LogWarning("获取外部余额失败: {Error}", result.Error?.Message);
                return new BalanceSyncResult(false, null, null, result.Error?.Message ?? "未知错误");
            }

            // 解析余额（API 返回字符串，USDC 通常为 6 位精度）
            if (!UsdcAmountParser.TryParse(result.Data!.Balance, out var balance))
            {
                var error = $"无法解析余额: {result.Data.Balance}";
                _logger.LogWarning(error);
                return new BalanceSyncResult(false, null, null, error);
            }

            if (!UsdcAmountParser.TryParse(result.Data.Allowance, out var allowance))
            {
                var error = $"无法解析 Allowance: {result.Data.Allowance}";
                _logger.LogWarning(error);
                return new BalanceSyncResult(false, null, null, error);
            }

            var snapshot = new ExternalBalanceSnapshot(balance, allowance, DateTimeOffset.UtcNow);
            _snapshotStore.SetBalanceSnapshot(snapshot);

            _logger.LogInformation(
                "外部余额同步成功: Balance={Balance} USDC, Allowance={Allowance} USDC",
                balance, allowance);

            return new BalanceSyncResult(true, balance, allowance);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "同步外部余额时发生异常");
            return new BalanceSyncResult(false, null, null, ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<PositionsSyncResult> SyncPositionsAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("开始同步外部持仓...");

        try
        {
            var accountKey = _accountContext.AccountKey;

            // 获取外部持仓
            var externalPositions = new List<UserPosition>();
            var offset = 0;
            const int limit = 100;

            while (true)
            {
                var result = await _dataClient.GetPositionsAsync(
                    accountKey, limit: limit, offset: offset, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (!result.IsSuccess)
                {
                    _logger.LogWarning("获取外部持仓失败: {Error}", result.Error?.Message);
                    return new PositionsSyncResult(false, 0, 0, null, result.Error?.Message ?? "未知错误");
                }

                externalPositions.AddRange(result.Data!);

                if (result.Data!.Count < limit)
                {
                    break;
                }

                offset += limit;
            }

            // 转换为快照
            var syncedAtUtc = DateTimeOffset.UtcNow;
            var snapshots = externalPositions
                .Select(p => new ExternalPositionSnapshot(
                    p.ConditionId,
                    p.Asset,
                    p.Outcome,
                    p.SizeDecimal,
                    p.AvgPriceDecimal,
                    syncedAtUtc))
                .ToList();

            // 获取内部持仓进行对账
            var internalPositions = await _positionRepository
                .GetByTradingAccountIdAsync(_accountContext.TradingAccountId, cancellationToken)
                .ConfigureAwait(false);

            // 执行对账
            var drifts = DetectPositionDrifts(internalPositions, snapshots);
            _snapshotStore.SetPositionsSnapshot(snapshots, syncedAtUtc);

            _logger.LogInformation(
                "外部持仓同步成功: External={ExternalCount}, Internal={InternalCount}, Drifts={DriftCount}",
                snapshots.Count, internalPositions.Count, drifts.Count);

            if (drifts.Count > 0)
            {
                foreach (var drift in drifts)
                {
                    _logger.LogWarning(
                        "持仓漂移: Market={MarketId}, Outcome={Outcome}, Type={DriftType}, Internal={Internal}, External={External}",
                        drift.MarketId, drift.Outcome, drift.DriftType, drift.InternalQuantity, drift.ExternalQuantity);
                }
            }

            return new PositionsSyncResult(true, snapshots.Count, drifts.Count, drifts);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "同步外部持仓时发生异常");
            return new PositionsSyncResult(false, 0, 0, null, ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<OpenOrdersSyncResult> SyncOpenOrdersAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("开始同步外部挂单...");

        try
        {
            if (!_options.DetectExternalOpenOrders)
            {
                return new OpenOrdersSyncResult(true, 0, 0, 0);
            }

            // 获取外部挂单
            var result = await _clobClient.GetOpenOrdersAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!result.IsSuccess)
            {
                _logger.LogWarning("获取外部挂单失败: {Error}", result.Error?.Message);
                return new OpenOrdersSyncResult(false, 0, 0, 0, result.Error?.Message ?? "未知错误");
            }

            var externalOrders = result.Data!;
            var syncedAtUtc = DateTimeOffset.UtcNow;

            // 获取本地仍处于挂单状态的订单快照（包含 ExchangeOrderId）
            var internalOpenOrders = _stateTracker.GetOpenOrders();
            var internalExchangeOrderIds = internalOpenOrders
                .Select(o => o.ExchangeOrderId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet(StringComparer.Ordinal);

            var externalExchangeOrderIds = externalOrders
                .Select(o => o.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet(StringComparer.Ordinal);

            // 对账：查找未知外部挂单和缺失内部挂单
            // - UnknownExternal：外部存在，但本地 tracker 不认识（可能是手动下单/历史挂单/状态丢失）
            // - MissingInternal：本地认为仍挂单，但外部已不在 open 列表（可能已成交/撤单但本地未更新）
            var unknownExternalCount = externalExchangeOrderIds.Except(internalExchangeOrderIds).Count();
            var missingInternalCount = internalExchangeOrderIds.Except(externalExchangeOrderIds).Count();

            _snapshotStore.Touch(syncedAtUtc);

            _logger.LogInformation(
                "外部挂单同步成功: External={ExternalCount}, Internal={InternalCount}, Unknown={Unknown}, Missing={Missing}",
                externalOrders.Count, internalOpenOrders.Count, unknownExternalCount, missingInternalCount);

            if (unknownExternalCount > 0)
            {
                _logger.LogWarning("发现 {Count} 个未知外部挂单", unknownExternalCount);
            }

            return new OpenOrdersSyncResult(true, externalOrders.Count, unknownExternalCount, missingInternalCount);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "同步外部挂单时发生异常");
            return new OpenOrdersSyncResult(false, 0, 0, 0, ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<FullSyncResult> SyncAllAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("开始完整账户同步...");

        var balanceResult = await SyncBalanceAsync(cancellationToken).ConfigureAwait(false);
        if (!balanceResult.IsSuccess)
        {
            return new FullSyncResult(false, balanceResult, null, null, balanceResult.ErrorMessage);
        }

        var positionsResult = await SyncPositionsAsync(cancellationToken).ConfigureAwait(false);
        if (!positionsResult.IsSuccess)
        {
            return new FullSyncResult(false, balanceResult, positionsResult, null, positionsResult.ErrorMessage);
        }

        var openOrdersResult = await SyncOpenOrdersAsync(cancellationToken).ConfigureAwait(false);
        if (!openOrdersResult.IsSuccess)
        {
            return new FullSyncResult(false, balanceResult, positionsResult, openOrdersResult, openOrdersResult.ErrorMessage);
        }

        var fullResult = new FullSyncResult(true, balanceResult, positionsResult, openOrdersResult);

        _logger.LogInformation(
            "完整账户同步完成: HasDrift={HasDrift}",
            fullResult.HasDrift);

        return fullResult;
    }

    private List<PositionDrift> DetectPositionDrifts(
        IReadOnlyList<PositionDto> internalPositions,
        IReadOnlyList<ExternalPositionSnapshot> externalSnapshots)
    {
        var drifts = new List<PositionDrift>();

        // 建立外部持仓索引
        var externalMap = externalSnapshots
            .Select(p => new
            {
                MarketId = NormalizeMarketId(p.MarketId),
                Outcome = NormalizeOutcome(p.Outcome),
                p.Quantity,
                p.AvgPrice
            })
            .Where(x => x.Outcome is not null)
            .GroupBy(x => (x.MarketId, Outcome: x.Outcome!.Value))
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var qty = g.Sum(x => x.Quantity);
                    var avg = qty > 0m ? g.Sum(x => x.Quantity * x.AvgPrice) / qty : 0m;
                    return (Quantity: qty, AvgCost: avg);
                });

        // 建立内部持仓索引（内部账本权威）
        var internalMap = internalPositions
            .Select(p => new
            {
                MarketId = NormalizeMarketId(p.MarketId),
                p.Outcome,
                p.Quantity,
                p.AverageCost
            })
            .GroupBy(x => (x.MarketId, x.Outcome))
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var qty = g.Sum(x => x.Quantity);
                    // 内部一条记录即可，但为安全起见做加权平均
                    var avg = qty > 0m ? g.Sum(x => x.Quantity * x.AverageCost) / qty : 0m;
                    return (Quantity: qty, AvgCost: avg);
                });

        // 检查所有内部持仓
        foreach (var (key, internalPos) in internalMap)
        {
            externalMap.TryGetValue(key, out var external);
            var externalQty = external.Quantity;
            var diff = Math.Abs(internalPos.Quantity - externalQty);

            if (diff > _options.QuantityDriftTolerance)
            {
                drifts.Add(new PositionDrift(
                    key.MarketId,
                    key.Outcome.ToString(),
                    internalPos.Quantity,
                    externalQty,
                    diff,
                    internalPos.AvgCost,
                    external.AvgCost,
                    null,
                    externalQty == 0 ? "MissingExternal" : "QuantityMismatch"));
            }
            else if (internalPos.Quantity > _options.QuantityDriftTolerance &&
                     externalQty > _options.QuantityDriftTolerance)
            {
                var avgDiff = Math.Abs(internalPos.AvgCost - external.AvgCost);
                if (avgDiff > _options.AverageCostDriftTolerance)
                {
                    drifts.Add(new PositionDrift(
                        key.MarketId,
                        key.Outcome.ToString(),
                        internalPos.Quantity,
                        externalQty,
                        0m,
                        internalPos.AvgCost,
                        external.AvgCost,
                        avgDiff,
                        "AvgCostMismatch"));
                }
            }
        }

        // 检查外部持仓中不存在于内部的
        foreach (var (key, external) in externalMap)
        {
            if (!internalMap.ContainsKey(key) && external.Quantity > _options.QuantityDriftTolerance)
            {
                drifts.Add(new PositionDrift(
                    key.MarketId,
                    key.Outcome.ToString(),
                    0m,
                    external.Quantity,
                    external.Quantity,
                    null,
                    external.AvgCost,
                    null,
                    "UnknownExternal"));
            }
        }

        return drifts;
    }

    private static string NormalizeMarketId(string marketId)
        => (marketId ?? string.Empty).Trim().ToLowerInvariant();

    private static OutcomeSide? NormalizeOutcome(string outcome)
        => Enum.TryParse<OutcomeSide>(outcome?.Trim() ?? string.Empty, ignoreCase: true, out var o) ? o : null;
}
