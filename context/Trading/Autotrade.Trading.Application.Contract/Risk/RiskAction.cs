namespace Autotrade.Trading.Application.Contract.Risk;

/// <summary>
/// Risk action for rejected requests.
/// </summary>
public enum RiskAction
{
    Allow = 0,
    Block = 1,
    KillSwitch = 2
}
