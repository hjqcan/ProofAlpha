using Autotrade.SelfImprove.Domain.Shared.Enums;
using NetDevPack.Domain;

namespace Autotrade.SelfImprove.Domain.Entities;

public sealed class GeneratedStrategyVersion : Entity, IAggregateRoot
{
    private GeneratedStrategyVersion()
    {
        StrategyId = string.Empty;
        Version = string.Empty;
        ArtifactRoot = string.Empty;
        PackageHash = string.Empty;
        ManifestJson = "{}";
        RiskEnvelopeJson = "{}";
        Stage = GeneratedStrategyStage.Generated;
        CreatedAtUtc = DateTimeOffset.UtcNow;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public GeneratedStrategyVersion(
        Guid proposalId,
        string strategyId,
        string version,
        string artifactRoot,
        string packageHash,
        string manifestJson,
        string riskEnvelopeJson,
        DateTimeOffset createdAtUtc)
    {
        ProposalId = proposalId == Guid.Empty ? throw new ArgumentException("ProposalId cannot be empty.", nameof(proposalId)) : proposalId;
        StrategyId = string.IsNullOrWhiteSpace(strategyId)
            ? throw new ArgumentException("StrategyId cannot be empty.", nameof(strategyId))
            : strategyId.Trim();
        Version = string.IsNullOrWhiteSpace(version)
            ? throw new ArgumentException("Version cannot be empty.", nameof(version))
            : version.Trim();
        ArtifactRoot = string.IsNullOrWhiteSpace(artifactRoot)
            ? throw new ArgumentException("ArtifactRoot cannot be empty.", nameof(artifactRoot))
            : artifactRoot.Trim();
        PackageHash = string.IsNullOrWhiteSpace(packageHash)
            ? throw new ArgumentException("PackageHash cannot be empty.", nameof(packageHash))
            : packageHash.Trim();
        ManifestJson = string.IsNullOrWhiteSpace(manifestJson) ? "{}" : manifestJson.Trim();
        RiskEnvelopeJson = string.IsNullOrWhiteSpace(riskEnvelopeJson) ? "{}" : riskEnvelopeJson.Trim();
        Stage = GeneratedStrategyStage.Generated;
        CreatedAtUtc = createdAtUtc == default ? DateTimeOffset.UtcNow : createdAtUtc;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid ProposalId { get; private set; }

    public string StrategyId { get; private set; }

    public string Version { get; private set; }

    public GeneratedStrategyStage Stage { get; private set; }

    public string ArtifactRoot { get; private set; }

    public string PackageHash { get; private set; }

    public string ManifestJson { get; private set; }

    public string RiskEnvelopeJson { get; private set; }

    public string? ValidationSummaryJson { get; private set; }

    public bool IsActiveCanary { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public string? QuarantineReason { get; private set; }

    public void AdvanceTo(GeneratedStrategyStage target, string? validationSummaryJson = null)
    {
        if (!IsAllowedTransition(Stage, target))
        {
            throw new InvalidOperationException($"Generated strategy cannot move from {Stage} to {target}.");
        }

        Stage = target;
        ValidationSummaryJson = string.IsNullOrWhiteSpace(validationSummaryJson)
            ? ValidationSummaryJson
            : validationSummaryJson.Trim();
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void ActivateCanary()
    {
        if (Stage != GeneratedStrategyStage.LiveCanary)
        {
            throw new InvalidOperationException("Only LiveCanary strategies can be activated as canary.");
        }

        IsActiveCanary = true;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void RollBack()
    {
        Stage = GeneratedStrategyStage.RolledBack;
        IsActiveCanary = false;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void Quarantine(string reason)
    {
        Stage = GeneratedStrategyStage.Quarantined;
        IsActiveCanary = false;
        QuarantineReason = string.IsNullOrWhiteSpace(reason) ? "Quarantined by SelfImprove gate." : reason.Trim();
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private static bool IsAllowedTransition(GeneratedStrategyStage current, GeneratedStrategyStage target)
    {
        return (current, target) switch
        {
            (GeneratedStrategyStage.Generated, GeneratedStrategyStage.StaticValidated) => true,
            (GeneratedStrategyStage.StaticValidated, GeneratedStrategyStage.UnitTested) => true,
            (GeneratedStrategyStage.UnitTested, GeneratedStrategyStage.ReplayValidated) => true,
            (GeneratedStrategyStage.ReplayValidated, GeneratedStrategyStage.ShadowRunning) => true,
            (GeneratedStrategyStage.ShadowRunning, GeneratedStrategyStage.PaperRunning) => true,
            (GeneratedStrategyStage.PaperRunning, GeneratedStrategyStage.LiveCanary) => true,
            (GeneratedStrategyStage.LiveCanary, GeneratedStrategyStage.Promoted) => true,
            (_, GeneratedStrategyStage.RolledBack) => true,
            (_, GeneratedStrategyStage.Quarantined) => true,
            _ => false
        };
    }
}
