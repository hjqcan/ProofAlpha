using Autotrade.Trading.Application.Contract.Risk;
using Microsoft.Extensions.Options;

namespace Autotrade.Trading.Application.Risk;

/// <summary>
/// In-memory capital provider based on configuration.
/// </summary>
public sealed class InMemoryRiskCapitalProvider : IRiskCapitalProvider
{
    private readonly RiskCapitalOptions _options;

    public InMemoryRiskCapitalProvider(IOptions<RiskCapitalOptions> options)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public RiskCapitalSnapshot GetSnapshot()
        => new(_options.TotalCapital, _options.AvailableCapital, _options.RealizedDailyPnl);
}
