using Autotrade.MarketData.Application.Contract.Spot;
using Autotrade.MarketData.Application.Spot;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Autotrade.MarketData.Infra.BackgroundJobs.Workers;

public sealed class SpotPriceFeedWorker : BackgroundService
{
    private readonly ISpotPriceFeed _spotPriceFeed;
    private readonly IOptionsMonitor<RtdsSpotPriceFeedOptions> _options;
    private readonly ILogger<SpotPriceFeedWorker> _logger;

    public SpotPriceFeedWorker(
        ISpotPriceFeed spotPriceFeed,
        IOptionsMonitor<RtdsSpotPriceFeedOptions> options,
        ILogger<SpotPriceFeedWorker> logger)
    {
        _spotPriceFeed = spotPriceFeed ?? throw new ArgumentNullException(nameof(spotPriceFeed));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = _options.CurrentValue;
        options.Validate();

        if (!options.Enabled)
        {
            _logger.LogInformation("RTDS spot price feed worker is disabled by configuration.");
            return;
        }

        try
        {
            await _spotPriceFeed.StartAsync(options.DefaultSymbols, stoppingToken).ConfigureAwait(false);
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            await _spotPriceFeed.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }
}
