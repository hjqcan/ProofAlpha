using System.Globalization;

namespace Autotrade.Trading.Application.Execution;

/// <summary>
/// USDC 金额解析器（可测试）。
/// 约定：
/// - 若字符串包含小数点（例如 "12.34"），按“USDC 单位”解析。
/// - 若字符串为整数（例如 "12340000"），按“微单位（6 decimals）”解析并除以 1e6。
/// </summary>
public static class UsdcAmountParser
{
    private const decimal MicroUnit = 1_000_000m;

    public static bool TryParse(string? raw, out decimal usdc)
    {
        usdc = 0m;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var s = raw.Trim();

        // 有小数点则按 USDC 单位解析
        if (s.Contains('.', StringComparison.Ordinal) ||
            s.Contains('e', StringComparison.OrdinalIgnoreCase))
        {
            return decimal.TryParse(
                s,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out usdc);
        }

        // 整数字符串按 USDC 微单位（6 位）解析
        if (!decimal.TryParse(
                s,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var micro))
        {
            return false;
        }

        if (micro < 0m)
        {
            return false;
        }

        usdc = micro / MicroUnit;
        return true;
    }
}

