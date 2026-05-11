namespace Autotrade.Trading.Application.Execution;

/// <summary>
/// 当前进程运行时的 Trading 账户上下文（Singleton）。
/// - 由启动阶段的 bootstrap/provisioning 设置
/// - 执行/落库链路只读取该上下文，不在交易过程中隐式创建账户
/// </summary>
public sealed class TradingAccountContext
{
    private readonly object _lock = new();
    private Guid? _tradingAccountId;
    private string? _accountKey;

    public Guid TradingAccountId
    {
        get
        {
            lock (_lock)
            {
                if (!_tradingAccountId.HasValue || _tradingAccountId.Value == Guid.Empty)
                {
                    throw new InvalidOperationException(
                        "TradingAccountContext is not initialized. " +
                        "Ensure TradingAccountBootstrapService has run successfully at startup.");
                }

                return _tradingAccountId.Value;
            }
        }
    }

    public string AccountKey
    {
        get
        {
            lock (_lock)
            {
                if (string.IsNullOrWhiteSpace(_accountKey))
                {
                    throw new InvalidOperationException(
                        "TradingAccountContext is not initialized. " +
                        "Ensure TradingAccountBootstrapService has run successfully at startup.");
                }

                return _accountKey!;
            }
        }
    }

    public void Initialize(Guid tradingAccountId, string accountKey)
    {
        if (tradingAccountId == Guid.Empty)
        {
            throw new ArgumentException("tradingAccountId cannot be empty.", nameof(tradingAccountId));
        }

        if (string.IsNullOrWhiteSpace(accountKey))
        {
            throw new ArgumentException("accountKey cannot be empty.", nameof(accountKey));
        }

        var normalizedKey = accountKey.Trim().ToLowerInvariant();

        lock (_lock)
        {
            if (_tradingAccountId.HasValue)
            {
                // 防止重复初始化/被覆盖
                if (_tradingAccountId.Value != tradingAccountId ||
                    !string.Equals(_accountKey, normalizedKey, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"TradingAccountContext has already been initialized. " +
                        $"Existing: Id={_tradingAccountId}, Key={_accountKey}; " +
                        $"New: Id={tradingAccountId}, Key={normalizedKey}");
                }

                return;
            }

            _tradingAccountId = tradingAccountId;
            _accountKey = normalizedKey;
        }
    }
}

