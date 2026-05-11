using Autotrade.SelfImprove.Application.Contract.Episodes;
using Autotrade.SelfImprove.Domain.Entities;

namespace Autotrade.SelfImprove.Application.Episodes;

public static class StrategyEpisodeMapper
{
    public static StrategyEpisodeDto ToDto(this StrategyEpisode episode)
    {
        ArgumentNullException.ThrowIfNull(episode);

        return new StrategyEpisodeDto(
            episode.Id,
            episode.StrategyId,
            episode.MarketId,
            episode.ConfigVersion,
            episode.WindowStartUtc,
            episode.WindowEndUtc,
            episode.DecisionCount,
            episode.ObservationCount,
            episode.OrderCount,
            episode.TradeCount,
            episode.RiskEventCount,
            episode.NetPnl,
            episode.FillRate,
            episode.RejectRate,
            episode.TimeoutRate,
            episode.MaxOpenExposure,
            episode.DrawdownLike,
            episode.SourceIdsJson,
            episode.MetricsJson,
            episode.CreatedAtUtc);
    }
}
