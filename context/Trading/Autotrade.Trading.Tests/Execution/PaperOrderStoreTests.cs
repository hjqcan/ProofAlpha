using Autotrade.Trading.Application.Execution;
using Xunit;

namespace Autotrade.Trading.Tests.Execution;

public sealed class PaperOrderStoreTests
{
    [Fact]
    public void GenerateExchangeOrderId_ShouldRemainUniqueAcrossStoreInstances()
    {
        var first = new PaperOrderStore().GenerateExchangeOrderId();
        var second = new PaperOrderStore().GenerateExchangeOrderId();

        Assert.StartsWith("PAPER-", first);
        Assert.StartsWith("PAPER-", second);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void GenerateExchangeOrderId_ShouldRemainMonotonicWithinStoreInstance()
    {
        var store = new PaperOrderStore();

        var first = store.GenerateExchangeOrderId();
        var second = store.GenerateExchangeOrderId();

        Assert.EndsWith("-00000001", first);
        Assert.EndsWith("-00000002", second);
    }
}
