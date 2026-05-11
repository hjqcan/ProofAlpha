namespace Autotrade.MarketData.Application.Spot;

public sealed class RtdsSpotPriceFeedOptions
{
    public const string SectionName = "MarketData:SpotPriceFeed:Rtds";

    public bool Enabled { get; set; } = false;

    public bool UseChainlinkTopic { get; set; } = false;

    public string[] DefaultSymbols { get; set; } = ["BTCUSDT", "ETHUSDT"];

    public void Validate()
    {
        if (DefaultSymbols.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("DefaultSymbols cannot contain empty values.", nameof(DefaultSymbols));
        }
    }
}
