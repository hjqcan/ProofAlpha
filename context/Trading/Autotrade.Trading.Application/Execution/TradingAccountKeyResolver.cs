using Autotrade.Polymarket.Options;

namespace Autotrade.Trading.Application.Execution;

/// <summary>
/// 交易账户 key 解析器（用于 TradingAccountContext / TradingAccount provisioning）。
/// </summary>
public static class TradingAccountKeyResolver
{
    public static string ResolveForPaper(PaperTradingOptions options)
        => string.IsNullOrWhiteSpace(options.WalletAddress) ? "paper" : NormalizeKey(options.WalletAddress);

    public static string ResolveForLiveOrThrow(PolymarketClobOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Address))
        {
            throw new InvalidOperationException(
                "Live mode requires Polymarket:Clob:Address to be set (fail-fast). " +
                "Please configure Polymarket:Clob:Address in appsettings / env vars.");
        }

        return NormalizeAndValidateEthAddress(options.Address);
    }

    private static string NormalizeKey(string key)
        => key.Trim().ToLowerInvariant();

    private static string NormalizeAndValidateEthAddress(string address)
    {
        var a = address.Trim();
        if (!a.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            a = $"0x{a}";
        }

        if (a.Length != 42)
        {
            throw new InvalidOperationException(
                $"Invalid Polymarket:Clob:Address '{address}'. Expected 42 chars hex address (0x + 40 hex chars).");
        }

        for (var i = 2; i < a.Length; i++)
        {
            var c = a[i];
            var isHex = (c >= '0' && c <= '9') ||
                        (c >= 'a' && c <= 'f') ||
                        (c >= 'A' && c <= 'F');
            if (!isHex)
            {
                throw new InvalidOperationException(
                    $"Invalid Polymarket:Clob:Address '{address}'. Non-hex character '{c}' at position {i}.");
            }
        }

        return a.ToLowerInvariant();
    }
}

