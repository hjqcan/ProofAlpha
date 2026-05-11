using Autotrade.SelfImprove.Application.Contract.Episodes;
using Autotrade.SelfImprove.Application.Contract.GeneratedStrategies;
using Autotrade.SelfImprove.Application.Contract.Proposals;
using Autotrade.SelfImprove.Domain.Shared.Enums;

namespace Autotrade.SelfImprove.Application.Contract;

public sealed record ImprovementRunDto(
    Guid Id,
    string StrategyId,
    string? MarketId,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc,
    string Trigger,
    ImprovementRunStatus Status,
    Guid? EpisodeId,
    int ProposalCount,
    string? ErrorMessage,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record PatchOutcomeDto(
    Guid Id,
    Guid ProposalId,
    string StrategyId,
    PatchOutcomeStatus Status,
    string DiffJson,
    string? RollbackJson,
    string? Message,
    DateTimeOffset CreatedAtUtc);

public sealed record SelfImproveRunResult(
    ImprovementRunDto Run,
    StrategyEpisodeDto? Episode,
    IReadOnlyList<ImprovementProposalDto> Proposals);

public sealed record ApplyProposalRequest(
    Guid ProposalId,
    bool DryRun = true,
    string Actor = "self-improve");

public interface ISelfImproveService
{
    Task<SelfImproveRunResult> RunAsync(
        BuildStrategyEpisodeRequest request,
        string trigger,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ImprovementRunDto>> ListRunsAsync(int limit = 50, CancellationToken cancellationToken = default);

    Task<SelfImproveRunResult?> GetRunAsync(Guid runId, CancellationToken cancellationToken = default);

    Task<PatchOutcomeDto> ApplyProposalAsync(ApplyProposalRequest request, CancellationToken cancellationToken = default);

    Task<GeneratedStrategyVersionDto> PromoteGeneratedStrategyAsync(
        Guid generatedStrategyVersionId,
        GeneratedStrategyStage targetStage,
        CancellationToken cancellationToken = default);

    Task<GeneratedStrategyVersionDto> RollbackGeneratedStrategyAsync(Guid generatedStrategyVersionId, CancellationToken cancellationToken = default);

    Task<GeneratedStrategyVersionDto> QuarantineGeneratedStrategyAsync(
        Guid generatedStrategyVersionId,
        string reason,
        CancellationToken cancellationToken = default);
}
