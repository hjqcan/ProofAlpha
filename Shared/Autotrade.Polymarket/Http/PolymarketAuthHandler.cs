using Autotrade.Polymarket.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Autotrade.Polymarket.Http;

/// <summary>
/// 在发送请求前自动注入 Polymarket 鉴权头（L1/L2）。
/// </summary>
public sealed class PolymarketAuthHandler : DelegatingHandler
{
    public static readonly HttpRequestOptionsKey<PolymarketSigningContext> SigningContextKey =
        new("PolymarketSigningContext");

    private readonly PolymarketClobOptions _options;
    private readonly ILogger<PolymarketAuthHandler> _logger;

    public PolymarketAuthHandler(
        IOptions<PolymarketClobOptions> options,
        ILogger<PolymarketAuthHandler> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!request.Options.TryGetValue(SigningContextKey, out var ctx) || ctx.AuthLevel == PolymarketAuthLevel.None)
        {
            return base.SendAsync(request, cancellationToken);
        }

        switch (ctx.AuthLevel)
        {
            case PolymarketAuthLevel.L1:
                ApplyL1(request, ctx);
                break;

            case PolymarketAuthLevel.L2:
                ApplyL2(request, ctx);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(ctx.AuthLevel), ctx.AuthLevel, "未知鉴权等级");
        }

        return base.SendAsync(request, cancellationToken);
    }

    private void ApplyL1(HttpRequestMessage request, PolymarketSigningContext ctx)
    {
        if (string.IsNullOrWhiteSpace(_options.PrivateKey))
        {
            throw new InvalidOperationException("缺少 Polymarket:Clob:PrivateKey，无法进行 L1 EIP-712 签名。");
        }

        var headers = PolymarketAuthHeaderFactory.CreateL1Headers(
            _options.PrivateKey!,
            _options.ChainId,
            nonce: ctx.Nonce,
            timestampSeconds: ctx.TimestampSeconds);

        ApplyHeaders(request, headers);

        _logger.LogDebug("已注入 Polymarket L1 鉴权头：{Method} {RequestPath}", request.Method, ctx.RequestPath);
    }

    private void ApplyL2(HttpRequestMessage request, PolymarketSigningContext ctx)
    {
        var address = ResolveAddress();

        if (string.IsNullOrWhiteSpace(_options.ApiKey) ||
            string.IsNullOrWhiteSpace(_options.ApiSecret) ||
            string.IsNullOrWhiteSpace(_options.ApiPassphrase))
        {
            throw new InvalidOperationException("缺少 Polymarket:Clob API 凭证（ApiKey/ApiSecret/ApiPassphrase），无法进行 L2 HMAC 鉴权。");
        }

        var headers = PolymarketAuthHeaderFactory.CreateL2Headers(
            address,
            _options.ApiKey!,
            _options.ApiSecret!,
            _options.ApiPassphrase!,
            request.Method,
            ctx.RequestPath,
            ctx.SerializedBody,
            timestampSeconds: ctx.TimestampSeconds);

        ApplyHeaders(request, headers);

        // 不输出敏感 header 值
        _logger.LogDebug("已注入 Polymarket L2 鉴权头：{Method} {RequestPath}", request.Method, ctx.RequestPath);
    }

    private string ResolveAddress()
    {
        if (!string.IsNullOrWhiteSpace(_options.Address))
        {
            return _options.Address!;
        }

        if (!string.IsNullOrWhiteSpace(_options.PrivateKey))
        {
            return PolymarketAuthHeaderFactory.GetAddressFromPrivateKey(_options.PrivateKey!);
        }

        throw new InvalidOperationException("缺少 Polymarket:Clob:Address 或 PrivateKey，无法设置 POLY_ADDRESS。");
    }

    private static void ApplyHeaders(HttpRequestMessage request, IReadOnlyDictionary<string, string> headers)
    {
        foreach (var (key, value) in headers)
        {
            request.Headers.Remove(key);
            request.Headers.TryAddWithoutValidation(key, value);
        }
    }
}

