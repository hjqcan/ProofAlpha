using System.Collections.Concurrent;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace Autotrade.Trading.Application.Execution;

/// <summary>
/// 内存幂等性存储：基于 ConcurrentDictionary 的线程安全实现。
/// </summary>
public sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    private readonly ConcurrentDictionary<string, OrderTrackingEntry> _entries = new();
    private readonly ConcurrentDictionary<string, string> _exchangeToClientMap = new();
    private readonly ILogger<InMemoryIdempotencyStore> _logger;
    private readonly Timer _cleanupTimer;
    private static long _lastOrderTimestampMs;

    public InMemoryIdempotencyStore(ILogger<InMemoryIdempotencyStore> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // 定期清理过期条目（每 5 分钟）
        _cleanupTimer = new Timer(
            CleanupExpiredEntries,
            state: null,
            dueTime: TimeSpan.FromMinutes(5),
            period: TimeSpan.FromMinutes(5));
    }

    /// <inheritdoc />
    public Task<(bool IsNew, string? ExistingExchangeOrderId)> TryAddAsync(
        string clientOrderId,
        string requestHash,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientOrderId))
        {
            throw new ArgumentException("ClientOrderId 不能为空", nameof(clientOrderId));
        }

        if (string.IsNullOrWhiteSpace(requestHash))
        {
            throw new ArgumentException("RequestHash 不能为空", nameof(requestHash));
        }

        var entry = new OrderTrackingEntry
        {
            ClientOrderId = clientOrderId,
            RequestHash = requestHash,
            ExpiresAtUtc = DateTimeOffset.UtcNow.Add(ttl)
        };

        // 使用 GetOrAdd 确保线程安全
        var existing = _entries.GetOrAdd(clientOrderId, entry);

        if (ReferenceEquals(existing, entry))
        {
            // 新添加成功
            _logger.LogDebug("幂等性存储：添加新条目 ClientOrderId={ClientOrderId}", clientOrderId);
            return Task.FromResult<(bool, string?)>((true, null));
        }

        // 已存在，检查是否过期
        if (existing.IsExpired)
        {
            // 过期条目，尝试替换
            if (_entries.TryUpdate(clientOrderId, entry, existing))
            {
                _logger.LogDebug("幂等性存储：替换过期条目 ClientOrderId={ClientOrderId}", clientOrderId);
                return Task.FromResult<(bool, string?)>((true, null));
            }

            // 并发更新失败，重新获取
            existing = _entries.GetOrAdd(clientOrderId, entry);
            if (ReferenceEquals(existing, entry))
            {
                return Task.FromResult<(bool, string?)>((true, null));
            }
        }

        // 检查请求哈希是否一致
        if (existing.RequestHash != requestHash)
        {
            _logger.LogWarning(
                "幂等性冲突：ClientOrderId={ClientOrderId} 已存在但请求参数不同",
                clientOrderId);

            throw new IdempotencyConflictException(
                clientOrderId,
                $"ClientOrderId '{clientOrderId}' 已存在，但请求参数不同。这可能表示重复使用了 ClientOrderId。");
        }

        // 相同请求，返回已存在的交易所订单 ID
        _logger.LogDebug(
            "幂等性存储：重复请求 ClientOrderId={ClientOrderId}, ExchangeOrderId={ExchangeOrderId}",
            clientOrderId,
            existing.ExchangeOrderId);

        return Task.FromResult<(bool, string?)>((false, existing.ExchangeOrderId));
    }

    /// <inheritdoc />
    public Task SetExchangeOrderIdAsync(
        string clientOrderId,
        string exchangeOrderId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientOrderId))
        {
            throw new ArgumentException("ClientOrderId 不能为空", nameof(clientOrderId));
        }

        if (string.IsNullOrWhiteSpace(exchangeOrderId))
        {
            throw new ArgumentException("ExchangeOrderId 不能为空", nameof(exchangeOrderId));
        }

        if (_entries.TryGetValue(clientOrderId, out var entry))
        {
            entry.ExchangeOrderId = exchangeOrderId;
            entry.IsUncertainSubmit = false;
            _exchangeToClientMap[exchangeOrderId] = clientOrderId;

            _logger.LogDebug(
                "幂等性存储：设置 ExchangeOrderId ClientOrderId={ClientOrderId} -> ExchangeOrderId={ExchangeOrderId}",
                clientOrderId,
                exchangeOrderId);
        }
        else
        {
            _logger.LogWarning(
                "幂等性存储：尝试设置 ExchangeOrderId 但 ClientOrderId={ClientOrderId} 不存在",
                clientOrderId);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<OrderTrackingEntry?> GetAsync(
        string clientOrderId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientOrderId))
        {
            return Task.FromResult<OrderTrackingEntry?>(null);
        }

        if (_entries.TryGetValue(clientOrderId, out var entry))
        {
            if (entry.IsExpired)
            {
                // 惰性清理
                _entries.TryRemove(clientOrderId, out _);
                if (entry.ExchangeOrderId is not null)
                {
                    _exchangeToClientMap.TryRemove(entry.ExchangeOrderId, out _);
                }

                return Task.FromResult<OrderTrackingEntry?>(null);
            }

            return Task.FromResult<OrderTrackingEntry?>(entry);
        }

        return Task.FromResult<OrderTrackingEntry?>(null);
    }

    /// <inheritdoc />
    public Task<string?> FindClientOrderIdByExchangeIdAsync(
        string exchangeOrderId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(exchangeOrderId))
        {
            return Task.FromResult<string?>(null);
        }

        _exchangeToClientMap.TryGetValue(exchangeOrderId, out var clientOrderId);
        return Task.FromResult(clientOrderId);
    }

    private void CleanupExpiredEntries(object? state)
    {
        var expiredCount = 0;
        var now = DateTimeOffset.UtcNow;

        foreach (var kvp in _entries)
        {
            if (kvp.Value.ExpiresAtUtc < now)
            {
                if (_entries.TryRemove(kvp.Key, out var removed))
                {
                    expiredCount++;
                    if (removed.ExchangeOrderId is not null)
                    {
                        _exchangeToClientMap.TryRemove(removed.ExchangeOrderId, out _);
                    }
                }
            }
        }

        if (expiredCount > 0)
        {
            _logger.LogDebug("幂等性存储：清理了 {Count} 个过期条目", expiredCount);
        }
    }

    /// <inheritdoc />
    public Task SetMarketInfoAsync(
        string clientOrderId,
        string marketId,
        string tokenId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientOrderId))
        {
            return Task.CompletedTask;
        }

        if (_entries.TryGetValue(clientOrderId, out var entry))
        {
            entry.MarketId = marketId;
            entry.TokenId = tokenId;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SetAuditInfoAsync(
        string clientOrderId,
        string? strategyId,
        string? correlationId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientOrderId))
        {
            return Task.CompletedTask;
        }

        if (_entries.TryGetValue(clientOrderId, out var entry))
        {
            entry.StrategyId = string.IsNullOrWhiteSpace(strategyId) ? null : strategyId.Trim();
            entry.CorrelationId = string.IsNullOrWhiteSpace(correlationId) ? null : correlationId.Trim();
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<OrderSigningPayload?> GetOrderSigningPayloadAsync(
        string clientOrderId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientOrderId) ||
            !_entries.TryGetValue(clientOrderId, out var entry) ||
            string.IsNullOrWhiteSpace(entry.OrderSalt) ||
            string.IsNullOrWhiteSpace(entry.OrderTimestamp))
        {
            return Task.FromResult<OrderSigningPayload?>(null);
        }

        return Task.FromResult<OrderSigningPayload?>(
            new OrderSigningPayload(entry.OrderSalt, entry.OrderTimestamp));
    }

    /// <inheritdoc />
    public Task<OrderSigningPayload> GetOrCreateOrderSigningPayloadAsync(
        string clientOrderId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientOrderId))
        {
            throw new ArgumentException("ClientOrderId 不能为空", nameof(clientOrderId));
        }

        if (!_entries.TryGetValue(clientOrderId, out var entry))
        {
            throw new InvalidOperationException(
                $"Cannot create order signing payload before idempotency reservation exists: {clientOrderId}");
        }

        lock (entry)
        {
            if (!string.IsNullOrWhiteSpace(entry.OrderSalt) &&
                !string.IsNullOrWhiteSpace(entry.OrderTimestamp))
            {
                return Task.FromResult(new OrderSigningPayload(entry.OrderSalt, entry.OrderTimestamp));
            }

            var payload = CreateOrderSigningPayload();
            entry.OrderSalt = payload.Salt;
            entry.OrderTimestamp = payload.Timestamp;
            return Task.FromResult(payload);
        }
    }

    /// <inheritdoc />
    public Task SeedAsync(OrderTrackingEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (string.IsNullOrWhiteSpace(entry.ClientOrderId))
        {
            return Task.CompletedTask;
        }

        _entries.AddOrUpdate(
            entry.ClientOrderId,
            entry,
            (_, existing) =>
            {
                return existing with
                {
                    ExchangeOrderId = entry.ExchangeOrderId ?? existing.ExchangeOrderId,
                    MarketId = entry.MarketId ?? existing.MarketId,
                    TokenId = entry.TokenId ?? existing.TokenId,
                    StrategyId = entry.StrategyId ?? existing.StrategyId,
                    CorrelationId = entry.CorrelationId ?? existing.CorrelationId,
                    OrderSalt = entry.OrderSalt ?? existing.OrderSalt,
                    OrderTimestamp = entry.OrderTimestamp ?? existing.OrderTimestamp,
                    IsUncertainSubmit = entry.IsUncertainSubmit || existing.IsUncertainSubmit,
                    RequestHash = entry.RequestHash,
                    ExpiresAtUtc = entry.ExpiresAtUtc
                };
            });

        if (!string.IsNullOrWhiteSpace(entry.ExchangeOrderId))
        {
            _exchangeToClientMap[entry.ExchangeOrderId] = entry.ClientOrderId;
        }

        _logger.LogDebug(
            "幂等性存储：恢复条目 ClientOrderId={ClientOrderId}, ExchangeOrderId={ExchangeOrderId}",
            entry.ClientOrderId,
            entry.ExchangeOrderId);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task MarkSubmitUncertainAsync(
        string clientOrderId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientOrderId))
        {
            return Task.CompletedTask;
        }

        if (_entries.TryGetValue(clientOrderId, out var entry))
        {
            entry.IsUncertainSubmit = true;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ClearSubmitUncertainAsync(
        string clientOrderId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientOrderId))
        {
            return Task.CompletedTask;
        }

        if (_entries.TryGetValue(clientOrderId, out var entry))
        {
            entry.IsUncertainSubmit = false;
        }

        return Task.CompletedTask;
    }

    private static OrderSigningPayload CreateOrderSigningPayload()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        var salt = new BigInteger(bytes, isUnsigned: true, isBigEndian: true)
            .ToString(CultureInfo.InvariantCulture);

        while (true)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var previous = Volatile.Read(ref _lastOrderTimestampMs);
            var next = Math.Max(now, previous + 1);

            if (Interlocked.CompareExchange(ref _lastOrderTimestampMs, next, previous) == previous)
            {
                return new OrderSigningPayload(salt, next.ToString(CultureInfo.InvariantCulture));
            }
        }
    }

    /// <inheritdoc />
    public Task<bool> RemoveAsync(
        string clientOrderId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientOrderId))
        {
            return Task.FromResult(false);
        }

        if (_entries.TryRemove(clientOrderId, out var removed))
        {
            if (removed.ExchangeOrderId is not null)
            {
                _exchangeToClientMap.TryRemove(removed.ExchangeOrderId, out _);
            }

            _logger.LogDebug("幂等性存储：移除条目 ClientOrderId={ClientOrderId}", clientOrderId);
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }
}
