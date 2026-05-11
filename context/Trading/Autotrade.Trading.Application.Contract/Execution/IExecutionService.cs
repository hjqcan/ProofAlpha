using Autotrade.Application.Services;

namespace Autotrade.Trading.Application.Contract.Execution;

/// <summary>
/// 执行服务接口：定义下单、撤单、查询订单状态的核心操作。
/// </summary>
public interface IExecutionService : IApplicationService
{
    /// <summary>
    /// 下单。
    /// </summary>
    /// <param name="request">执行请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>执行结果。</returns>
    Task<ExecutionResult> PlaceOrderAsync(ExecutionRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量下单。实现可以使用交易所批量接口，也可以按单笔顺序降级。
    /// </summary>
    /// <param name="requests">执行请求列表。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>每笔订单的执行结果，顺序与请求顺序一致。</returns>
    Task<IReadOnlyList<ExecutionResult>> PlaceOrdersAsync(
        IReadOnlyList<ExecutionRequest> requests,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 撤单。
    /// </summary>
    /// <param name="clientOrderId">客户端订单 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>执行结果。</returns>
    Task<ExecutionResult> CancelOrderAsync(string clientOrderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 查询订单状态。
    /// </summary>
    /// <param name="clientOrderId">客户端订单 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>订单状态结果。</returns>
    Task<OrderStatusResult> GetOrderStatusAsync(string clientOrderId, CancellationToken cancellationToken = default);
}
