using NetDevPack.Messaging;

namespace Autotrade.Testing.Db;

/// <summary>
/// 测试用：不执行任何分发的 DomainEventDispatcher。
/// </summary>
internal sealed class NullDomainEventDispatcher : IDomainEventDispatcher
{
    public void Dispatch(IEnumerable<DomainEvent> domainEvents)
    {
        // no-op
    }

    public Task DispatchAsync(IEnumerable<DomainEvent> domainEvents)
    {
        return Task.CompletedTask;
    }
}

