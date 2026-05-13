using Autotrade.Application.Services;
using Autotrade.MarketData.Application.Contract.WebSocket.Events;

namespace Autotrade.MarketData.Application.Contract.Tape;

public interface IMarketTapeRecorder : IApplicationService
{
    Task RecordBookEventAsync(
        ClobBookEvent bookEvent,
        CancellationToken cancellationToken = default);

    Task RecordPriceChangeEventAsync(
        ClobPriceChangeEvent priceChangeEvent,
        CancellationToken cancellationToken = default);

    Task RecordLastTradePriceEventAsync(
        ClobLastTradePriceEvent tradeEvent,
        CancellationToken cancellationToken = default);
}
