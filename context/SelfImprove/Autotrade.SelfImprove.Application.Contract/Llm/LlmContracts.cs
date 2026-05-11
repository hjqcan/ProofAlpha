using Autotrade.SelfImprove.Application.Contract.Episodes;
using Autotrade.SelfImprove.Application.Contract.Proposals;

namespace Autotrade.SelfImprove.Application.Contract.Llm;

public sealed record StrategyEpisodeAnalysisRequest(
    StrategyEpisodeDto Episode,
    string StrategyMemoryJson,
    string ImprovementPlaybookJson);

public interface ILLmClient
{
    Task<IReadOnlyList<ImprovementProposalDocument>> AnalyzeEpisodeAsync(
        StrategyEpisodeAnalysisRequest request,
        CancellationToken cancellationToken = default);

    Task<GeneratedStrategySpec> GenerateStrategyAsync(
        ImprovementProposalDocument proposal,
        StrategyEpisodeDto episode,
        CancellationToken cancellationToken = default);
}
