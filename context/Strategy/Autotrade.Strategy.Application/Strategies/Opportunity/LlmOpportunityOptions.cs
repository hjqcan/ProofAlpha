namespace Autotrade.Strategy.Application.Strategies.Opportunity;

public sealed class LlmOpportunityOptions
{
    public const string SectionName = "Strategies:LlmOpportunity";

    public bool Enabled { get; set; }

    public string ConfigVersion { get; set; } = "v1";

    public int MaxMarkets { get; set; } = 20;

    public int EntryCooldownSeconds { get; set; } = 60;

    public int ExitCooldownSeconds { get; set; } = 15;

    public int MaxOrderBookAgeSeconds { get; set; } = 10;

    public decimal MaxSlippage { get; set; } = 0.01m;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ConfigVersion))
        {
            throw new ArgumentException("ConfigVersion cannot be empty.", nameof(ConfigVersion));
        }

        if (MaxMarkets <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxMarkets), MaxMarkets, "MaxMarkets must be positive.");
        }

        if (EntryCooldownSeconds < 0 || ExitCooldownSeconds < 0 || MaxOrderBookAgeSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(EntryCooldownSeconds), EntryCooldownSeconds, "Cooldown and freshness settings must be non-negative.");
        }

        if (MaxSlippage < 0m || MaxSlippage >= 1m)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxSlippage), MaxSlippage, "MaxSlippage must be between 0 and 1.");
        }
    }
}
