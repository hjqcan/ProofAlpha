using Autotrade.OpportunityDiscovery.Domain.Shared.Enums;
using NetDevPack.Domain;

namespace Autotrade.OpportunityDiscovery.Domain.Entities;

public sealed class SourceProfile : Entity, IAggregateRoot
{
    private SourceProfile()
    {
        SourceKey = string.Empty;
        SourceName = string.Empty;
        AuthorityKind = SourceAuthorityKind.Unknown;
        CoveredCategoriesJson = "[]";
        ChangeReason = string.Empty;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public SourceProfile(
        string sourceKey,
        EvidenceSourceKind sourceKind,
        string sourceName,
        SourceAuthorityKind authorityKind,
        bool isOfficial,
        int expectedLatencySeconds,
        string coveredCategoriesJson,
        decimal historicalConflictRate,
        decimal historicalPassedGateContribution,
        decimal reliabilityScore,
        int version,
        Guid? supersedesProfileId,
        string changeReason,
        DateTimeOffset createdAtUtc)
    {
        SourceKey = Required(sourceKey, nameof(sourceKey), 128);
        SourceKind = sourceKind;
        SourceName = Required(sourceName, nameof(sourceName), 128);
        AuthorityKind = authorityKind;
        IsOfficial = isOfficial;
        ExpectedLatencySeconds = Math.Max(0, expectedLatencySeconds);
        CoveredCategoriesJson = string.IsNullOrWhiteSpace(coveredCategoriesJson)
            ? "[]"
            : coveredCategoriesJson.Trim();
        HistoricalConflictRate = ClampProbability(historicalConflictRate, nameof(historicalConflictRate));
        HistoricalPassedGateContribution = ClampProbability(
            historicalPassedGateContribution,
            nameof(historicalPassedGateContribution));
        ReliabilityScore = ClampProbability(reliabilityScore, nameof(reliabilityScore));
        Version = version <= 0
            ? throw new ArgumentOutOfRangeException(nameof(version), version, "Version must be positive.")
            : version;
        SupersedesProfileId = supersedesProfileId;
        ChangeReason = string.IsNullOrWhiteSpace(changeReason)
            ? "initial"
            : changeReason.Trim()[..Math.Min(changeReason.Trim().Length, 1024)];
        CreatedAtUtc = createdAtUtc == default ? DateTimeOffset.UtcNow : createdAtUtc;
    }

    public string SourceKey { get; private set; }

    public EvidenceSourceKind SourceKind { get; private set; }

    public string SourceName { get; private set; }

    public SourceAuthorityKind AuthorityKind { get; private set; }

    public bool IsOfficial { get; private set; }

    public int ExpectedLatencySeconds { get; private set; }

    public string CoveredCategoriesJson { get; private set; }

    public decimal HistoricalConflictRate { get; private set; }

    public decimal HistoricalPassedGateContribution { get; private set; }

    public decimal ReliabilityScore { get; private set; }

    public int Version { get; private set; }

    public Guid? SupersedesProfileId { get; private set; }

    public string ChangeReason { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public bool CanProvideLiveConfirmation =>
        IsOfficial ||
        AuthorityKind is SourceAuthorityKind.Official
            or SourceAuthorityKind.PrimaryExchange
            or SourceAuthorityKind.Regulator
            or SourceAuthorityKind.DataOracle;

    public SourceProfile CreateNextVersion(
        SourceAuthorityKind authorityKind,
        bool isOfficial,
        int expectedLatencySeconds,
        string coveredCategoriesJson,
        decimal historicalConflictRate,
        decimal historicalPassedGateContribution,
        decimal reliabilityScore,
        string changeReason,
        DateTimeOffset createdAtUtc)
        => new(
            SourceKey,
            SourceKind,
            SourceName,
            authorityKind,
            isOfficial,
            expectedLatencySeconds,
            coveredCategoriesJson,
            historicalConflictRate,
            historicalPassedGateContribution,
            reliabilityScore,
            Version + 1,
            Id,
            changeReason,
            createdAtUtc);

    private static string Required(string value, string paramName, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{paramName} cannot be empty.", paramName);
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static decimal ClampProbability(decimal value, string paramName)
    {
        if (value < 0m || value > 1m)
        {
            throw new ArgumentOutOfRangeException(paramName, value, "Value must be in 0..1.");
        }

        return value;
    }
}
