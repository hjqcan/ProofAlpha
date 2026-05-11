namespace Autotrade.Strategy.Application.Persistence;

public sealed class StrategyRetentionOptions
{
    public const string SectionName = "StrategyEngine:Retention";

    public int DecisionLogRetentionDays { get; set; } = 30;

    public int CommandAuditRetentionDays { get; set; } = 30;

    public void Validate()
    {
        if (DecisionLogRetentionDays < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(DecisionLogRetentionDays), DecisionLogRetentionDays,
                "DecisionLogRetentionDays must be positive.");
        }

        if (CommandAuditRetentionDays < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(CommandAuditRetentionDays), CommandAuditRetentionDays,
                "CommandAuditRetentionDays must be positive.");
        }
    }
}
