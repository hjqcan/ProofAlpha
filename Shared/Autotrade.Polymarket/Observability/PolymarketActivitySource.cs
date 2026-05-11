using System.Diagnostics;

namespace Autotrade.Polymarket.Observability;

/// <summary>
/// Polymarket API 客户端分布式追踪 ActivitySource。
/// </summary>
public static class PolymarketActivitySource
{
    public const string SourceName = "Autotrade.Polymarket";

    public static readonly ActivitySource Source = new(SourceName, "1.0.0");

    /// <summary>
    /// 开始一个 HTTP 请求 Activity。
    /// </summary>
    public static Activity? StartHttpRequest(string method, string endpoint)
    {
        return Source.StartActivity($"Polymarket {method} {SanitizeEndpoint(endpoint)}", ActivityKind.Client);
    }

    private static string SanitizeEndpoint(string endpoint)
    {
        var idx = endpoint.IndexOf('?');
        var path = idx > 0 ? endpoint[..idx] : endpoint;

        if (path.StartsWith("/markets/") && path.Length > "/markets/".Length)
        {
            return "/markets/{id}";
        }

        if (path.StartsWith("/data/order/") && path.Length > "/data/order/".Length)
        {
            return "/data/order/{id}";
        }

        return path;
    }
}
