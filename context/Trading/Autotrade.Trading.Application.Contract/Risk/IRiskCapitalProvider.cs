namespace Autotrade.Trading.Application.Contract.Risk;

/// <summary>
/// Provides capital and PnL snapshots for risk checks.
/// </summary>
public interface IRiskCapitalProvider
{
    RiskCapitalSnapshot GetSnapshot();
}
