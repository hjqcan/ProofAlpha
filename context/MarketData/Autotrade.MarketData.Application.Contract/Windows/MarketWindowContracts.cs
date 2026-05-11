using System.Text.RegularExpressions;
using Autotrade.Application.Services;
using Autotrade.MarketData.Application.Contract.Catalog;

namespace Autotrade.MarketData.Application.Contract.Windows;

public enum MarketWindowType
{
    Unknown = 0,
    CryptoUpDown15m = 1
}

public enum MarketWindowBoundaryPolicy
{
    Unknown = 0,
    StartPriceVersusEndPrice = 1
}

public enum MarketWindowOracleStatus
{
    Unspecified = 0,
    ParsedButUnconfirmed = 1,
    Configured = 2,
    Confirmed = 3
}

public sealed record MarketWindowTokenMap(
    string YesTokenId,
    string NoTokenId,
    string YesOutcomeName = "Up",
    string NoOutcomeName = "Down");

public sealed record MarketWindowSpec(
    string MarketId,
    string? Slug,
    MarketWindowType WindowType,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc,
    string UnderlyingSymbol,
    MarketWindowBoundaryPolicy BoundaryPolicy,
    string SettlementOracle,
    string SettlementReference,
    MarketWindowOracleStatus OracleStatus,
    MarketWindowTokenMap TokenMap,
    decimal? ThresholdPrice = null)
{
    public bool IsOracleConfirmed => OracleStatus == MarketWindowOracleStatus.Confirmed;

    public bool Contains(DateTimeOffset timestampUtc)
    {
        var utc = timestampUtc.ToUniversalTime();
        return utc >= WindowStartUtc && utc < WindowEndUtc;
    }
}

public interface IMarketWindowSpecProvider : IApplicationService
{
    MarketWindowSpec? GetSpec(string marketId);

    MarketWindowSpec? TryCreate(MarketInfoDto market);
}

public static partial class MarketWindowSpecParser
{
    public const int CryptoUpDownWindowMinutes = 15;

    private static readonly IReadOnlyDictionary<string, string> DefaultSymbolMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["btc"] = "BTCUSDT",
            ["eth"] = "ETHUSDT",
            ["sol"] = "SOLUSDT",
            ["xrp"] = "XRPUSDT"
        };

    public static MarketWindowSpec? TryParseCryptoUpDown15m(
        MarketInfoDto market,
        string settlementOracle,
        string settlementReference,
        MarketWindowOracleStatus oracleStatus)
    {
        ArgumentNullException.ThrowIfNull(market);

        if (market.TokenIds.Count < 2 || string.IsNullOrWhiteSpace(market.Slug))
        {
            return null;
        }

        var match = CryptoUpDownSlugRegex().Match(market.Slug);
        if (!match.Success)
        {
            return null;
        }

        var symbolKey = match.Groups["symbol"].Value;
        if (!DefaultSymbolMap.TryGetValue(symbolKey, out var underlyingSymbol))
        {
            underlyingSymbol = $"{symbolKey.ToUpperInvariant()}USDT";
        }

        if (!long.TryParse(match.Groups["timestamp"].Value, out var startUnixSeconds))
        {
            return null;
        }

        var startUtc = DateTimeOffset.FromUnixTimeSeconds(startUnixSeconds);
        var endUtc = startUtc.AddMinutes(CryptoUpDownWindowMinutes);

        return new MarketWindowSpec(
            market.MarketId,
            market.Slug,
            MarketWindowType.CryptoUpDown15m,
            startUtc,
            endUtc,
            underlyingSymbol,
            MarketWindowBoundaryPolicy.StartPriceVersusEndPrice,
            settlementOracle,
            settlementReference,
            oracleStatus,
            new MarketWindowTokenMap(market.TokenIds[0], market.TokenIds[1]));
    }

    [GeneratedRegex("^(?<symbol>[a-z0-9]+)-updown-15m-(?<timestamp>\\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CryptoUpDownSlugRegex();
}
