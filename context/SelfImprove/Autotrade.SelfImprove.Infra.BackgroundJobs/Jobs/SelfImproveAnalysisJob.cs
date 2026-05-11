using Autotrade.Infra.BackgroundJobs.Core;
using Autotrade.SelfImprove.Application;
using Autotrade.SelfImprove.Application.Contract;
using Autotrade.SelfImprove.Application.Contract.Episodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Autotrade.SelfImprove.Infra.BackgroundJobs.Jobs;

public sealed class SelfImproveAnalysisJob : JobBase<SelfImproveAnalysisJob>
{
    private readonly ISelfImproveService _selfImproveService;
    private readonly SelfImproveOptions _options;
    private readonly IConfiguration _configuration;

    public SelfImproveAnalysisJob(
        ISelfImproveService selfImproveService,
        IOptions<SelfImproveOptions> options,
        IConfiguration configuration,
        ILogger<SelfImproveAnalysisJob> logger)
        : base(logger)
    {
        _selfImproveService = selfImproveService ?? throw new ArgumentNullException(nameof(selfImproveService));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    protected override async Task ExecuteJobAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return;
        }

        var section = _configuration.GetSection("BackgroundJobs:SelfImproveAnalysis");
        var strategyId = section["StrategyId"];
        if (string.IsNullOrWhiteSpace(strategyId))
        {
            Logger.LogWarning("SelfImproveAnalysis job skipped because BackgroundJobs:SelfImproveAnalysis:StrategyId is not configured.");
            return;
        }

        var windowMinutes = Math.Clamp(section.GetValue("WindowMinutes", 60), 5, 24 * 60);
        var end = DateTimeOffset.UtcNow;
        var start = end.AddMinutes(-windowMinutes);
        await _selfImproveService.RunAsync(
            new BuildStrategyEpisodeRequest(strategyId, section["MarketId"], start, end),
            "hangfire",
            cancellationToken).ConfigureAwait(false);
    }
}
