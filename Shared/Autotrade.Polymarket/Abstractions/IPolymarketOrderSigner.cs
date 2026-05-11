using Autotrade.Polymarket.Models;

namespace Autotrade.Polymarket.Abstractions;

public interface IPolymarketOrderSigner
{
    PostOrderRequest CreatePostOrderRequest(OrderRequest request, string? idempotencyKey = null);
}
