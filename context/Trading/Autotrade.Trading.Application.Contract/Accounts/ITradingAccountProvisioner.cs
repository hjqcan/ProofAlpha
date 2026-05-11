using Autotrade.Application.Services;

namespace Autotrade.Trading.Application.Contract.Accounts;

/// <summary>
/// Trading 账户（TradingAccount）初始化器：
/// 在系统启动阶段确保指定账户对应的 TradingAccount 存在，并返回其 ID。
/// </summary>
public interface ITradingAccountProvisioner : IApplicationService
{
    /// <summary>
    /// 确保账户已初始化（幂等）：存在则返回已有 TradingAccountId；不存在则创建后返回。
    /// </summary>
    Task<Guid> ProvisionAsync(
        string accountKey,
        decimal totalCapital,
        decimal availableCapital,
        CancellationToken cancellationToken = default);
}

