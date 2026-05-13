using System.Text.Json;
using Autotrade.OpportunityDiscovery.Application;
using Autotrade.OpportunityDiscovery.Application.Evidence;
using Autotrade.OpportunityDiscovery.Domain.Shared.Enums;
using Microsoft.Extensions.Options;

namespace Autotrade.OpportunityDiscovery.Infra.Sources;

public sealed class PolymarketAccountTradeSource : IEvidenceSource
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly OpportunityDiscoveryOptions _options;

    public PolymarketAccountTradeSource(HttpClient httpClient, IOptions<OpportunityDiscoveryOptions> options)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public string Name => "polymarket_account_trades";

    public async Task<IReadOnlyList<NormalizedEvidence>> SearchAsync(
        EvidenceQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!_options.PolymarketAccounts.Enabled || _options.PolymarketAccounts.WalletAddresses.Count == 0)
        {
            return Array.Empty<NormalizedEvidence>();
        }

        var results = new List<NormalizedEvidence>();
        var wallets = _options.PolymarketAccounts.WalletAddresses
            .Where(wallet => !string.IsNullOrWhiteSpace(wallet))
            .Select(wallet => wallet.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, query.MaxItems))
            .ToList();
        foreach (var wallet in wallets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var requestUrl = BuildTradesUrl(wallet, query);
            var json = await _httpClient.GetStringAsync(requestUrl, cancellationToken).ConfigureAwait(false);
            var trades = ParseTrades(json)
                .Where(trade => MatchesMarket(trade, query))
                .OrderByDescending(trade => trade.ExecutedAtUtc ?? DateTimeOffset.MinValue)
                .Take(Math.Max(1, _options.PolymarketAccounts.MaxTradesPerWallet))
                .ToList();
            if (trades.Count == 0)
            {
                continue;
            }

            results.Add(BuildEvidence(wallet, requestUrl, query, trades));
            if (results.Count >= query.MaxItems)
            {
                break;
            }
        }

        return results;
    }

    internal static IReadOnlyList<PolymarketAccountTrade> ParseTrades(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var array = root.ValueKind == JsonValueKind.Array
            ? root
            : root.TryGetProperty("trades", out var tradesElement) && tradesElement.ValueKind == JsonValueKind.Array
                ? tradesElement
                : default;
        if (array.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<PolymarketAccountTrade>();
        }

        var trades = new List<PolymarketAccountTrade>();
        foreach (var item in array.EnumerateArray())
        {
            var conditionId = ReadString(item, "conditionId") ?? ReadString(item, "condition_id");
            var marketId = ReadString(item, "market") ?? ReadString(item, "marketId") ?? conditionId;
            var title = ReadString(item, "title") ?? ReadString(item, "marketTitle") ?? marketId ?? "Polymarket trade";
            var side = NormalizeSide(ReadString(item, "side"));
            var outcome = ReadString(item, "outcome") ?? ReadString(item, "outcomeName") ?? string.Empty;
            var price = ReadDecimal(item, "price");
            var size = ReadDecimal(item, "size") ?? ReadDecimal(item, "amount") ?? ReadDecimal(item, "quantity");
            var timestamp = ReadTimestamp(item, "timestamp") ?? ReadTimestamp(item, "createdAt") ?? ReadTimestamp(item, "created_at");

            if (string.IsNullOrWhiteSpace(marketId) || price is null || size is null || string.IsNullOrWhiteSpace(side))
            {
                continue;
            }

            trades.Add(new PolymarketAccountTrade(
                marketId,
                conditionId,
                title,
                string.IsNullOrWhiteSpace(outcome) ? "unknown" : outcome,
                side,
                price.Value,
                size.Value,
                timestamp,
                ReadString(item, "transactionHash") ?? ReadString(item, "transaction_hash"),
                item.GetRawText()));
        }

        return trades;
    }

    private NormalizedEvidence BuildEvidence(
        string wallet,
        string requestUrl,
        EvidenceQuery query,
        IReadOnlyList<PolymarketAccountTrade> trades)
    {
        var now = DateTimeOffset.UtcNow;
        var latest = trades
            .Select(trade => trade.ExecutedAtUtc)
            .Where(timestamp => timestamp is not null)
            .OrderByDescending(timestamp => timestamp)
            .FirstOrDefault();
        var aggregates = trades
            .GroupBy(trade => new { trade.Outcome, trade.Side })
            .Select(group =>
            {
                var quantity = group.Sum(trade => trade.Size);
                var notional = group.Sum(trade => trade.Price * trade.Size);
                var signedQuantity = string.Equals(group.Key.Side, "buy", StringComparison.OrdinalIgnoreCase)
                    ? quantity
                    : -quantity;
                return new
                {
                    group.Key.Outcome,
                    group.Key.Side,
                    tradeCount = group.Count(),
                    quantity,
                    signedQuantity,
                    notional,
                    averagePrice = quantity == 0m ? null : (decimal?)(notional / quantity)
                };
            })
            .OrderBy(item => item.Outcome, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Side, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var summaryPayload = new
        {
            kind = "polymarket-public-account-trades",
            walletAddress = wallet,
            marketId = query.Market.MarketId,
            conditionId = query.Market.ConditionId,
            tradeCount = trades.Count,
            aggregates
        };
        var summary = JsonSerializer.Serialize(summaryPayload, JsonOptions);
        var raw = JsonSerializer.Serialize(new
        {
            kind = "polymarket-public-account-trades",
            requestUrl,
            walletAddress = wallet,
            market = new
            {
                query.Market.MarketId,
                query.Market.ConditionId,
                query.Market.Name,
                query.Market.Slug
            },
            trades,
            aggregates
        }, JsonOptions);

        return new NormalizedEvidence(
            EvidenceSourceKind.Polymarket,
            $"polymarket_account_trades:{WalletLabel(wallet)}",
            requestUrl,
            Truncate($"Public Polymarket trades for {WalletLabel(wallet)} on {query.Market.Name}", 512),
            Truncate(summary, 4096),
            latest,
            now,
            raw,
            _options.PolymarketAccounts.SourceQuality);
    }

    private string BuildTradesUrl(string wallet, EvidenceQuery query)
    {
        var baseUrl = string.IsNullOrWhiteSpace(_options.PolymarketAccounts.BaseUrl)
            ? "https://data-api.polymarket.com"
            : _options.PolymarketAccounts.BaseUrl.TrimEnd('/');
        var parameters = new List<string>
        {
            $"user={Uri.EscapeDataString(wallet)}",
            $"limit={Math.Clamp(_options.PolymarketAccounts.MaxTradesPerWallet, 1, 500)}",
            $"takerOnly={_options.PolymarketAccounts.TakerOnly.ToString().ToLowerInvariant()}"
        };

        if (!string.IsNullOrWhiteSpace(query.Market.ConditionId))
        {
            parameters.Add($"market={Uri.EscapeDataString(query.Market.ConditionId)}");
        }

        return $"{baseUrl}/trades?{string.Join("&", parameters)}";
    }

    private static bool MatchesMarket(PolymarketAccountTrade trade, EvidenceQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.Market.ConditionId) &&
            string.Equals(trade.ConditionId, query.Market.ConditionId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(trade.MarketId, query.Market.MarketId, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim()
            : value.ToString().Trim();
    }

    private static decimal? ReadDecimal(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
        {
            return number;
        }

        return decimal.TryParse(ReadString(element, propertyName), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static DateTimeOffset? ReadTimestamp(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var epoch))
        {
            return epoch > 9_999_999_999
                ? DateTimeOffset.FromUnixTimeMilliseconds(epoch).ToUniversalTime()
                : DateTimeOffset.FromUnixTimeSeconds(epoch).ToUniversalTime();
        }

        var text = ReadString(element, propertyName);
        return DateTimeOffset.TryParse(text, out var parsed) ? parsed.ToUniversalTime() : null;
    }

    private static string NormalizeSide(string? side)
    {
        if (string.IsNullOrWhiteSpace(side))
        {
            return string.Empty;
        }

        return side.Trim().Equals("sell", StringComparison.OrdinalIgnoreCase) ? "sell" : "buy";
    }

    private static string WalletLabel(string wallet)
    {
        var trimmed = wallet.Trim();
        return trimmed.Length <= 12 ? trimmed : $"{trimmed[..6]}...{trimmed[^4..]}";
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];
}

public sealed record PolymarketAccountTrade(
    string MarketId,
    string? ConditionId,
    string Title,
    string Outcome,
    string Side,
    decimal Price,
    decimal Size,
    DateTimeOffset? ExecutedAtUtc,
    string? TransactionHash,
    string RawJson);
