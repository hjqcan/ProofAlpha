using Autotrade.MarketData.Domain.Entities;
using Autotrade.MarketData.Domain.Shared.Enums;

namespace Autotrade.Testing.Builders;

public static class MarketDataTestData
{
    public static Market NewMarket(
        string marketId = "test-market",
        string name = "Test Market",
        DateTimeOffset? expiresAtUtc = null,
        MarketStatus status = MarketStatus.Active)
    {
        var m = new Market(marketId, name, expiresAtUtc);
        m.UpdateStatus(status);
        return m;
    }
}

