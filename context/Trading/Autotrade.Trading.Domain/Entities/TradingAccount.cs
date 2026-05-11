using Autotrade.Trading.Domain.Shared.Enums;
using NetDevPack.Domain;

namespace Autotrade.Trading.Domain.Entities;

/// <summary>
/// 交易账户聚合根：聚合账户、订单、持仓与风控事件，保证交易状态的一致性。
/// </summary>
public sealed class TradingAccount : Entity, IAggregateRoot
{
    // EF Core
    private TradingAccount()
    {
        WalletAddress = "unknown";
        TotalCapital = 0m;
        AvailableCapital = 0m;
        AccountUpdatedAtUtc = DateTimeOffset.UtcNow;
        Orders = new List<Order>();
        Positions = new List<Position>();
        RiskEvents = new List<RiskEvent>();
        Trades = new List<Trade>();
        CreatedAtUtc = DateTimeOffset.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public TradingAccount(string walletAddress, decimal totalCapital, decimal availableCapital)
    {
        if (string.IsNullOrWhiteSpace(walletAddress))
        {
            throw new ArgumentException("钱包地址不能为空", nameof(walletAddress));
        }

        ValidateCapital(totalCapital, availableCapital);

        // 账户标识统一规范化为小写，确保大小写不敏感语义下的唯一性（DB 侧唯一索引依赖该规范化）。
        WalletAddress = walletAddress.Trim().ToLowerInvariant();
        TotalCapital = totalCapital;
        AvailableCapital = availableCapital;
        AccountUpdatedAtUtc = DateTimeOffset.UtcNow;
        Orders = new List<Order>();
        Positions = new List<Position>();
        RiskEvents = new List<RiskEvent>();
        Trades = new List<Trade>();
        CreatedAtUtc = DateTimeOffset.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    /// <summary>
    /// 钱包地址（EOA 场景：funder == signer；Paper 场景：可为固定 key，例如 "paper"）。
    /// 规范化：trim + lower。
    /// </summary>
    public string WalletAddress { get; private set; }

    /// <summary>
    /// 风控总资金上限（USDC）。
    /// 注意：这不是链上真实余额，真实余额/allowance 属于外部快照。
    /// </summary>
    public decimal TotalCapital { get; private set; }

    /// <summary>
    /// 风控可用资金上限（USDC）。
    /// </summary>
    public decimal AvailableCapital { get; private set; }

    /// <summary>
    /// 账户资金快照更新时间（用于审计/对账）。
    /// </summary>
    public DateTimeOffset AccountUpdatedAtUtc { get; private set; }

    public List<Order> Orders { get; private set; }

    public List<Position> Positions { get; private set; }

    public List<RiskEvent> RiskEvents { get; private set; }

    /// <summary>
    /// 成交记录。
    /// </summary>
    public List<Trade> Trades { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>
    /// 乐观并发控制版本号。
    /// </summary>
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

    public void RecordRiskEvent(string code, RiskSeverity severity, string message, string? contextJson = null)
    {
        var riskEvent = new RiskEvent(Id, code, severity, message, contextJson);
        RiskEvents.Add(riskEvent);
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void UpdateCapital(decimal totalCapital, decimal availableCapital)
    {
        ValidateCapital(totalCapital, availableCapital);

        TotalCapital = totalCapital;
        AvailableCapital = availableCapital;
        AccountUpdatedAtUtc = DateTimeOffset.UtcNow;
        UpdatedAtUtc = AccountUpdatedAtUtc;
    }

    public void Debit(decimal amount)
    {
        if (amount <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "扣款金额必须大于 0");
        }

        if (amount > AvailableCapital)
        {
            throw new InvalidOperationException("可用资金不足");
        }

        AvailableCapital -= amount;
        AccountUpdatedAtUtc = DateTimeOffset.UtcNow;
        UpdatedAtUtc = AccountUpdatedAtUtc;
    }

    public void Credit(decimal amount)
    {
        if (amount <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "入账金额必须大于 0");
        }

        AvailableCapital += amount;
        TotalCapital += amount;
        AccountUpdatedAtUtc = DateTimeOffset.UtcNow;
        UpdatedAtUtc = AccountUpdatedAtUtc;
    }

    private static void ValidateCapital(decimal totalCapital, decimal availableCapital)
    {
        if (totalCapital < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(totalCapital), totalCapital, "总资金不能为负数");
        }

        if (availableCapital < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(availableCapital), availableCapital, "可用资金不能为负数");
        }

        if (availableCapital > totalCapital)
        {
            throw new ArgumentException("可用资金不能大于总资金", nameof(availableCapital));
        }
    }
}

