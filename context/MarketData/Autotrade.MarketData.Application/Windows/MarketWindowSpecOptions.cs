using Autotrade.MarketData.Application.Contract.Windows;

namespace Autotrade.MarketData.Application.Windows;

public sealed class MarketWindowSpecOptions
{
    public const string SectionName = "MarketData:MarketWindow";

    public string SettlementOracle { get; set; } = "polymarket-rtds-crypto-prices";

    public string SettlementReference { get; set; } =
        "15m crypto Up/Down window: end reference price compared with start reference price.";

    public MarketWindowOracleStatus OracleStatus { get; set; } = MarketWindowOracleStatus.Configured;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(SettlementOracle))
        {
            throw new ArgumentException("SettlementOracle cannot be empty.", nameof(SettlementOracle));
        }

        if (string.IsNullOrWhiteSpace(SettlementReference))
        {
            throw new ArgumentException("SettlementReference cannot be empty.", nameof(SettlementReference));
        }
    }
}
