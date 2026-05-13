using Autotrade.Infra.BackgroundJobs.Core;
using Autotrade.MarketData.Application.Contract.Tape;
using Autotrade.MarketData.Application.Tape;
using Hangfire;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Autotrade.MarketData.Infra.BackgroundJobs.Jobs;

[DisableConcurrentExecution(timeoutInSeconds: 30 * 60)]
public sealed class MarketTapeGapRepairJob : JobBase<MarketTapeGapRepairJob>
{
    private readonly IMarketTapeGapRepairService _gapRepairService;
    private readonly MarketTapeGapRepairOptions _options;
    private readonly IConfiguration _configuration;

    public MarketTapeGapRepairJob(
        IMarketTapeGapRepairService gapRepairService,
        IOptions<MarketTapeGapRepairOptions> options,
        IConfiguration configuration,
        ILogger<MarketTapeGapRepairJob> logger)
        : base(logger)
    {
        _gapRepairService = gapRepairService ?? throw new ArgumentNullException(nameof(gapRepairService));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    protected override async Task ExecuteJobAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            Logger.LogInformation("Market tape gap repair skipped because MarketData:TapeGapRepair:Enabled is false.");
            return;
        }

        var section = _configuration.GetSection("BackgroundJobs:MarketTapeGapRepair");
        var result = await _gapRepairService.RepairAsync(
                new MarketTapeGapRepairRequest(
                    MarketId: EmptyToNull(section["MarketId"]),
                    ObservedAtUtc: DateTimeOffset.UtcNow,
                    MaxMarkets: section.GetValue<int?>("MaxMarkets"),
                    MaxTokensPerMarket: section.GetValue<int?>("MaxTokensPerMarket"),
                    MinVolume24h: section.GetValue<decimal?>("MinVolume24h"),
                    MinLiquidity: section.GetValue<decimal?>("MinLiquidity")),
                cancellationToken)
            .ConfigureAwait(false);

        Logger.LogInformation(
            "Market tape gap repair completed: markets={Markets} requested={Requested} recorded={Recorded} failed={Failed}",
            result.MarketsExamined,
            result.TokensRequested,
            result.TokensRecorded,
            result.TokensFailed);
    }

    private static string? EmptyToNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
