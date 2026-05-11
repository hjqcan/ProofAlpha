using Autotrade.Trading.Application.Contract.Accounts;
using Autotrade.Trading.Domain.Entities;
using Autotrade.Trading.Infra.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Autotrade.Trading.Infra.Data.Repositories;

/// <summary>
/// Trading 账户初始化器（EF Core 实现）。
/// 说明：
/// - accountKey 在 Live 模式下为真实钱包地址（来自 Polymarket:Clob:Address）
/// - 在 Paper 模式下可使用固定 key（例如 "paper"）
/// </summary>
public sealed class EfTradingAccountProvisioner : ITradingAccountProvisioner
{
    private readonly TradingContext _context;

    public EfTradingAccountProvisioner(TradingContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<Guid> ProvisionAsync(
        string accountKey,
        decimal totalCapital,
        decimal availableCapital,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accountKey))
        {
            throw new ArgumentException("accountKey cannot be empty.", nameof(accountKey));
        }

        var normalizedKey = NormalizeAccountKey(accountKey);

        // 1) 查找是否已存在
        // 说明：WalletAddress 在写入时已统一规范化为小写，且 DB 有唯一索引，因此可直接等值查询并命中索引。
        var matches = await _context.TradingAccounts
            .AsNoTracking()
            .Where(x => x.WalletAddress == normalizedKey)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (matches.Count > 1)
        {
            // Fail-fast：同一账户 key 对应多个聚合根会导致语义混乱（且无法确定 FK 应指向谁）
            throw new InvalidOperationException(
                $"Multiple TradingAccounts found for accountKey='{normalizedKey}'. " +
                "Please deduplicate the database records and keep only one.");
        }

        if (matches.Count == 1)
        {
            return matches[0];
        }

        // 2) 不存在则创建
        var account = new TradingAccount(normalizedKey, totalCapital, availableCapital);
        _context.TradingAccounts.Add(account);

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return account.Id;
        }
        catch (DbUpdateException)
        {
            // 并发启动/初始化：可能是唯一约束冲突，回读确认
            var id = await _context.TradingAccounts
                .AsNoTracking()
                .Where(x => x.WalletAddress == normalizedKey)
                .Select(x => x.Id)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (id != Guid.Empty)
            {
                return id;
            }

            throw;
        }
    }

    private static string NormalizeAccountKey(string key)
        => key.Trim().ToLowerInvariant();
}

