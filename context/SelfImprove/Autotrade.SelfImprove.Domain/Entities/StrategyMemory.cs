using NetDevPack.Domain;

namespace Autotrade.SelfImprove.Domain.Entities;

public sealed class StrategyMemory : Entity, IAggregateRoot
{
    private StrategyMemory()
    {
        StrategyId = string.Empty;
        MemoryJson = "{}";
        PlaybookJson = "{}";
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public StrategyMemory(string strategyId, string memoryJson, string playbookJson, DateTimeOffset updatedAtUtc)
    {
        StrategyId = string.IsNullOrWhiteSpace(strategyId)
            ? throw new ArgumentException("StrategyId cannot be empty.", nameof(strategyId))
            : strategyId.Trim();
        MemoryJson = string.IsNullOrWhiteSpace(memoryJson) ? "{}" : memoryJson.Trim();
        PlaybookJson = string.IsNullOrWhiteSpace(playbookJson) ? "{}" : playbookJson.Trim();
        UpdatedAtUtc = updatedAtUtc == default ? DateTimeOffset.UtcNow : updatedAtUtc;
    }

    public string StrategyId { get; private set; }

    public string MemoryJson { get; private set; }

    public string PlaybookJson { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public void Update(string memoryJson, string playbookJson)
    {
        MemoryJson = string.IsNullOrWhiteSpace(memoryJson) ? "{}" : memoryJson.Trim();
        PlaybookJson = string.IsNullOrWhiteSpace(playbookJson) ? "{}" : playbookJson.Trim();
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }
}
