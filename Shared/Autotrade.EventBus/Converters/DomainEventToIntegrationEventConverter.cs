using System.Reflection;
using Autotrade.Domain.Abstractions.EventBus;
using NetDevPack.Messaging;

namespace Autotrade.EventBus.Converters;

/// <summary>
/// 领域事件到集成事件的转换器。
/// 支持两种识别方式：
/// 1. 实现 IIntegrationEvent 接口（推荐）
/// 2. 标记 [IntegrationEvent] 特性（备选）
/// </summary>
public class DomainEventToIntegrationEventConverter
{
    /// <summary>
    /// 从领域事件集合中提取集成事件
    /// </summary>
    public IEnumerable<IntegrationEventWrapper> Convert(IEnumerable<Event> domainEvents)
    {
        foreach (var domainEvent in domainEvents)
        {
            // 方式 1：实现 IIntegrationEvent 接口（优先）
            if (domainEvent is IIntegrationEvent integrationEvent)
            {
                yield return new IntegrationEventWrapper(
                    integrationEvent.EventName,
                    integrationEvent.Version,
                    domainEvent);
                continue;
            }

            // 方式 2：标记 [IntegrationEvent] 特性（备选）
            var attribute = domainEvent.GetType().GetCustomAttribute<IntegrationEventAttribute>();
            if (attribute is not null)
            {
                yield return new IntegrationEventWrapper(
                    attribute.EventName,
                    attribute.Version,
                    domainEvent);
            }
        }
    }

    /// <summary>
    /// 检查事件是否为集成事件
    /// </summary>
    public bool IsIntegrationEvent(Event domainEvent)
    {
        return domainEvent is IIntegrationEvent
               || domainEvent.GetType().GetCustomAttribute<IntegrationEventAttribute>() is not null;
    }
}

/// <summary>
/// 集成事件包装器，封装事件元数据和内容
/// </summary>
public record IntegrationEventWrapper(
    string EventName,
    string Version,
    Event Payload)
{
    /// <summary>
    /// 构建 CAP 消息头
    /// </summary>
    public Dictionary<string, string?> BuildHeaders(string? correlationId = null)
    {
        return new Dictionary<string, string?>
        {
            ["event-version"] = Version,
            ["event-timestamp"] = DateTimeOffset.UtcNow.ToString("O"),
            ["correlation-id"] = correlationId ?? Guid.NewGuid().ToString(),
            ["aggregate-id"] = Payload.AggregateId.ToString(),
            ["event-type"] = Payload.GetType().FullName
        };
    }
}

