using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Autotrade.Polymarket.Abstractions;
using Autotrade.Polymarket.BuilderAttribution;
using Autotrade.Polymarket.Http;
using Autotrade.Polymarket.Models;
using Autotrade.Polymarket.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace Autotrade.Polymarket;

/// <summary>
/// Polymarket CLOB REST API 客户端（基于 HttpClientFactory）。
/// </summary>
public sealed class PolymarketClobClient : IPolymarketClobClient
{
    private static readonly JsonSerializerOptions ResponseJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private static readonly JsonSerializerOptions RequestJsonOptions = new()
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private readonly HttpClient _httpClient;
    private readonly IPolymarketOrderSigner _orderSigner;
    private readonly ILogger<PolymarketClobClient> _logger;
    private readonly PolymarketClobOptions _clobOptions;
    private readonly ResiliencePipeline<HttpResponseMessage> _idempotentPipeline;
    private readonly ResiliencePipeline<HttpResponseMessage> _nonIdempotentPipeline;

    public PolymarketClobClient(
        HttpClient httpClient,
        IPolymarketOrderSigner orderSigner,
        IOptions<PolymarketClobOptions> clobOptions,
        IOptions<PolymarketResilienceOptions> resilienceOptions,
        ILogger<PolymarketClobClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _orderSigner = orderSigner ?? throw new ArgumentNullException(nameof(orderSigner));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _clobOptions = clobOptions?.Value ?? throw new ArgumentNullException(nameof(clobOptions));

        // 兜底设置 BaseAddress/Timeout（也可以在 DI 注册时设置）
        if (_httpClient.BaseAddress is null)
        {
            // BaseAddress 需要尾随斜杠以正确处理相对路径
            _httpClient.BaseAddress = new Uri(_clobOptions.Host.TrimEnd('/') + "/");
        }

        _httpClient.Timeout = _clobOptions.Timeout;

        var ro = resilienceOptions?.Value ?? throw new ArgumentNullException(nameof(resilienceOptions));
        _idempotentPipeline = BuildIdempotentPipeline(ro);
        _nonIdempotentPipeline = BuildNonIdempotentPipeline(ro);
    }

    public async Task<PolymarketApiResult<long>> GetServerTimeAsync(CancellationToken cancellationToken = default)
    {
        // /time 返回的是一个数字（秒级时间戳）
        var result = await SendRawAsync(
            HttpMethod.Get,
            relativeUrl: PolymarketClobEndpoints.Time,
            signingRequestPath: PolymarketClobEndpoints.Time,
            authLevel: PolymarketAuthLevel.None,
            body: null,
            nonce: null,
            cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return PolymarketApiResult<long>.Failure(result.StatusCode, result.Error?.Message, result.Error?.RawBody);
        }

        if (long.TryParse(result.Data, out var ts))
        {
            return PolymarketApiResult<long>.Success(result.StatusCode, ts);
        }

        // 有些服务会返回 JSON 数字或包装结构，这里做兼容解析
        try
        {
            var doc = JsonDocument.Parse(result.Data ?? "null");
            if (doc.RootElement.ValueKind == JsonValueKind.Number && doc.RootElement.TryGetInt64(out var v))
            {
                return PolymarketApiResult<long>.Success(result.StatusCode, v);
            }
        }
        catch
        {
            // ignored
        }

        return PolymarketApiResult<long>.Failure(
            result.StatusCode,
            "无法解析 /time 响应为 long 时间戳",
            result.Data);
    }

    public async Task<PolymarketApiResult<ApiKeyCreds>> CreateApiKeyAsync(int? nonce = null, CancellationToken cancellationToken = default)
    {
        var raw = await SendAsync<ApiKeyRaw>(
            HttpMethod.Post,
            relativeUrl: PolymarketClobEndpoints.CreateApiKey,
            signingRequestPath: PolymarketClobEndpoints.CreateApiKey,
            authLevel: PolymarketAuthLevel.L1,
            body: null,
            nonce: nonce,
            cancellationToken).ConfigureAwait(false);

        return raw.IsSuccess
            ? PolymarketApiResult<ApiKeyCreds>.Success(raw.StatusCode, new ApiKeyCreds(raw.Data!.ApiKey, raw.Data.Secret, raw.Data.Passphrase))
            : PolymarketApiResult<ApiKeyCreds>.Failure(raw.StatusCode, raw.Error?.Message, raw.Error?.RawBody);
    }

    public async Task<PolymarketApiResult<ApiKeyCreds>> DeriveApiKeyAsync(int? nonce = null, CancellationToken cancellationToken = default)
    {
        var raw = await SendAsync<ApiKeyRaw>(
            HttpMethod.Get,
            relativeUrl: PolymarketClobEndpoints.DeriveApiKey,
            signingRequestPath: PolymarketClobEndpoints.DeriveApiKey,
            authLevel: PolymarketAuthLevel.L1,
            body: null,
            nonce: nonce,
            cancellationToken).ConfigureAwait(false);

        return raw.IsSuccess
            ? PolymarketApiResult<ApiKeyCreds>.Success(raw.StatusCode, new ApiKeyCreds(raw.Data!.ApiKey, raw.Data.Secret, raw.Data.Passphrase))
            : PolymarketApiResult<ApiKeyCreds>.Failure(raw.StatusCode, raw.Error?.Message, raw.Error?.RawBody);
    }

    public Task<PolymarketApiResult<ApiKeysResponse>> GetApiKeysAsync(CancellationToken cancellationToken = default) =>
        SendAsync<ApiKeysResponse>(
            HttpMethod.Get,
            relativeUrl: PolymarketClobEndpoints.GetApiKeys,
            signingRequestPath: PolymarketClobEndpoints.GetApiKeys,
            authLevel: PolymarketAuthLevel.L2,
            body: null,
            nonce: null,
            cancellationToken);

    public Task<PolymarketApiResult<BanStatus>> GetClosedOnlyModeAsync(CancellationToken cancellationToken = default) =>
        SendAsync<BanStatus>(
            HttpMethod.Get,
            relativeUrl: PolymarketClobEndpoints.ClosedOnly,
            signingRequestPath: PolymarketClobEndpoints.ClosedOnly,
            authLevel: PolymarketAuthLevel.L2,
            body: null,
            nonce: null,
            cancellationToken);

    public Task<PolymarketApiResult<string>> DeleteApiKeyAsync(CancellationToken cancellationToken = default) =>
        SendRawAsync(
            HttpMethod.Delete,
            relativeUrl: PolymarketClobEndpoints.DeleteApiKey,
            signingRequestPath: PolymarketClobEndpoints.DeleteApiKey,
            authLevel: PolymarketAuthLevel.L2,
            body: null,
            nonce: null,
            cancellationToken);

    public Task<PolymarketApiResult<OrderBookSummary>> GetOrderBookAsync(string tokenId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tokenId))
        {
            throw new ArgumentException("tokenId 不能为空", nameof(tokenId));
        }

        // 签名时不包含 querystring（与官方客户端一致）
        var relativeUrl = $"{PolymarketClobEndpoints.GetOrderBook}?token_id={Uri.EscapeDataString(tokenId)}";

        return SendAsync<OrderBookSummary>(
            HttpMethod.Get,
            relativeUrl: relativeUrl,
            signingRequestPath: PolymarketClobEndpoints.GetOrderBook,
            authLevel: PolymarketAuthLevel.None,
            body: null,
            nonce: null,
            cancellationToken);
    }

    // ─────────────────────────────────────────────────────────────
    // Markets
    // ─────────────────────────────────────────────────────────────

    public async Task<PolymarketApiResult<IReadOnlyList<MarketInfo>>> GetMarketsAsync(string? nextCursor = null, CancellationToken cancellationToken = default)
    {
        var queryParams = new List<string>();
        if (!string.IsNullOrWhiteSpace(nextCursor))
        {
            queryParams.Add($"next_cursor={Uri.EscapeDataString(nextCursor)}");
        }

        var relativeUrl = queryParams.Count > 0
            ? $"{PolymarketClobEndpoints.GetMarkets}?{string.Join("&", queryParams)}"
            : PolymarketClobEndpoints.GetMarkets;

        return await SendAsync<IReadOnlyList<MarketInfo>>(
            HttpMethod.Get,
            relativeUrl: relativeUrl,
            signingRequestPath: PolymarketClobEndpoints.GetMarkets,
            authLevel: PolymarketAuthLevel.None,
            body: null,
            nonce: null,
            cancellationToken).ConfigureAwait(false);
    }

    public Task<PolymarketApiResult<MarketInfo>> GetMarketAsync(string conditionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(conditionId))
        {
            throw new ArgumentException("conditionId 不能为空", nameof(conditionId));
        }

        var relativeUrl = $"{PolymarketClobEndpoints.GetMarketPrefix}{Uri.EscapeDataString(conditionId)}";
        var signingPath = PolymarketClobEndpoints.GetMarkets;

        return SendAsync<MarketInfo>(
            HttpMethod.Get,
            relativeUrl: relativeUrl,
            signingRequestPath: signingPath,
            authLevel: PolymarketAuthLevel.None,
            body: null,
            nonce: null,
            cancellationToken);
    }

    // ─────────────────────────────────────────────────────────────
    // Orders
    // ─────────────────────────────────────────────────────────────

    public Task<PolymarketApiResult<OrderResponse>> PlaceOrderAsync(OrderRequest request, string? idempotencyKey = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var postOrderRequest = _orderSigner.CreatePostOrderRequest(request, idempotencyKey);
        return PlaceOrderAsync(postOrderRequest, idempotencyKey, cancellationToken);
    }

    public Task<PolymarketApiResult<OrderResponse>> PlaceOrderAsync(PostOrderRequest request, string? idempotencyKey = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return SendAsync<OrderResponse>(
            HttpMethod.Post,
            relativeUrl: PolymarketClobEndpoints.PostOrder,
            signingRequestPath: PolymarketClobEndpoints.PostOrder,
            authLevel: PolymarketAuthLevel.L2,
            body: request,
            nonce: null,
            cancellationToken,
            idempotencyKey: idempotencyKey);
    }

    public Task<PolymarketApiResult<IReadOnlyList<OrderResponse>>> PlaceOrdersAsync(
        IReadOnlyList<PostOrderRequest> requests,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requests);
        if (requests.Count == 0)
        {
            return Task.FromResult(PolymarketApiResult<IReadOnlyList<OrderResponse>>.Success(200, Array.Empty<OrderResponse>()));
        }

        return SendAsync<IReadOnlyList<OrderResponse>>(
            HttpMethod.Post,
            relativeUrl: PolymarketClobEndpoints.PostOrders,
            signingRequestPath: PolymarketClobEndpoints.PostOrders,
            authLevel: PolymarketAuthLevel.L2,
            body: requests,
            nonce: null,
            cancellationToken,
            idempotencyKey: idempotencyKey);
    }

    public Task<PolymarketApiResult<CancelOrderResponse>> CancelOrderAsync(string orderId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(orderId))
        {
            throw new ArgumentException("orderId 不能为空", nameof(orderId));
        }

        var body = new CancelOrderRequest { OrderId = orderId };

        return SendAsync<CancelOrderResponse>(
            HttpMethod.Delete,
            relativeUrl: PolymarketClobEndpoints.CancelOrder,
            signingRequestPath: PolymarketClobEndpoints.CancelOrder,
            authLevel: PolymarketAuthLevel.L2,
            body: body,
            nonce: null,
            cancellationToken);
    }

    public Task<PolymarketApiResult<CancelOrderResponse>> CancelAllOrdersAsync(string? market = null, string? assetId = null, CancellationToken cancellationToken = default)
    {
        var body = new CancelAllOrdersRequest
        {
            Market = market,
            AssetId = assetId
        };

        return SendAsync<CancelOrderResponse>(
            HttpMethod.Delete,
            relativeUrl: PolymarketClobEndpoints.CancelAll,
            signingRequestPath: PolymarketClobEndpoints.CancelAll,
            authLevel: PolymarketAuthLevel.L2,
            body: body,
            nonce: null,
            cancellationToken);
    }

    public Task<PolymarketApiResult<OrderInfo>> GetOrderAsync(string orderId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(orderId))
        {
            throw new ArgumentException("orderId 不能为空", nameof(orderId));
        }

        var relativeUrl = $"{PolymarketClobEndpoints.GetOrderPrefix}{Uri.EscapeDataString(orderId)}";

        // L2 签名必须用完整路径（包含 orderId），而非前缀
        return SendAsync<OrderInfo>(
            HttpMethod.Get,
            relativeUrl: relativeUrl,
            signingRequestPath: relativeUrl,
            authLevel: PolymarketAuthLevel.L2,
            body: null,
            nonce: null,
            cancellationToken);
    }

    public async Task<PolymarketApiResult<IReadOnlyList<OrderInfo>>> GetOpenOrdersAsync(string? market = null, string? assetId = null, CancellationToken cancellationToken = default)
    {
        var queryParams = new List<string>();
        if (!string.IsNullOrWhiteSpace(market))
        {
            queryParams.Add($"market={Uri.EscapeDataString(market)}");
        }

        if (!string.IsNullOrWhiteSpace(assetId))
        {
            queryParams.Add($"asset_id={Uri.EscapeDataString(assetId)}");
        }

        var relativeUrl = queryParams.Count > 0
            ? $"{PolymarketClobEndpoints.GetOpenOrders}?{string.Join("&", queryParams)}"
            : PolymarketClobEndpoints.GetOpenOrders;

        return await SendAsync<IReadOnlyList<OrderInfo>>(
            HttpMethod.Get,
            relativeUrl: relativeUrl,
            signingRequestPath: PolymarketClobEndpoints.GetOpenOrders,
            authLevel: PolymarketAuthLevel.L2,
            body: null,
            nonce: null,
            cancellationToken).ConfigureAwait(false);
    }

    // ─────────────────────────────────────────────────────────────
    // Trades
    // ─────────────────────────────────────────────────────────────

    public async Task<PolymarketApiResult<IReadOnlyList<TradeInfo>>> GetTradesAsync(string? market = null, string? nextCursor = null, CancellationToken cancellationToken = default)
    {
        var cursor = string.IsNullOrWhiteSpace(nextCursor)
            ? PolymarketConstants.InitialCursor
            : nextCursor;

        if (string.Equals(cursor, PolymarketConstants.EndCursor, StringComparison.Ordinal))
        {
            return PolymarketApiResult<IReadOnlyList<TradeInfo>>.Success(200, Array.Empty<TradeInfo>());
        }

        var trades = new List<TradeInfo>();
        var seenCursors = new HashSet<string>(StringComparer.Ordinal);
        var statusCode = 200;

        while (!string.Equals(cursor, PolymarketConstants.EndCursor, StringComparison.Ordinal))
        {
            if (!seenCursors.Add(cursor))
            {
                return PolymarketApiResult<IReadOnlyList<TradeInfo>>.Failure(
                    statusCode,
                    $"Polymarket /data/trades pagination cursor did not advance: {cursor}",
                    rawBody: null);
            }

            var relativeUrl = BuildTradesUrl(market, cursor);

            var page = await SendAsync<PaginatedResponse<TradeInfo>>(
                HttpMethod.Get,
                relativeUrl: relativeUrl,
                signingRequestPath: PolymarketClobEndpoints.GetTrades,
                authLevel: PolymarketAuthLevel.L2,
                body: null,
                nonce: null,
                cancellationToken).ConfigureAwait(false);

            if (!page.IsSuccess || page.Data is null)
            {
                return PolymarketApiResult<IReadOnlyList<TradeInfo>>.Failure(
                    page.StatusCode,
                    page.Error?.Message,
                    page.Error?.RawBody);
            }

            statusCode = page.StatusCode;

            if (page.Data.Data is { Count: > 0 } pageTrades)
            {
                trades.AddRange(pageTrades);
            }

            if (string.IsNullOrWhiteSpace(page.Data.NextCursor))
            {
                return PolymarketApiResult<IReadOnlyList<TradeInfo>>.Failure(
                    statusCode,
                    "Polymarket /data/trades response did not include next_cursor",
                    rawBody: null);
            }

            cursor = page.Data.NextCursor;
        }

        return PolymarketApiResult<IReadOnlyList<TradeInfo>>.Success(statusCode, trades);
    }

    private static string BuildTradesUrl(string? market, string nextCursor)
    {
        var queryParams = new List<string>();
        if (!string.IsNullOrWhiteSpace(market))
        {
            queryParams.Add($"market={Uri.EscapeDataString(market)}");
        }

        queryParams.Add($"next_cursor={Uri.EscapeDataString(nextCursor)}");

        var relativeUrl = queryParams.Count > 0
            ? $"{PolymarketClobEndpoints.GetTrades}?{string.Join("&", queryParams)}"
            : PolymarketClobEndpoints.GetTrades;

        return relativeUrl;
    }

    // ─────────────────────────────────────────────────────────────
    // Builder Trades
    // ─────────────────────────────────────────────────────────────

    public async Task<PolymarketApiResult<IReadOnlyList<BuilderTradeInfo>>> GetBuilderTradesAsync(
        string builderCode,
        string? market = null,
        string? assetId = null,
        string? tradeId = null,
        string? before = null,
        string? after = null,
        string? nextCursor = null,
        CancellationToken cancellationToken = default)
    {
        if (!PolymarketBuilderAttribution.IsBytes32Hex(builderCode))
        {
            throw new ArgumentException("builderCode must be bytes32 hex.", nameof(builderCode));
        }

        var cursor = string.IsNullOrWhiteSpace(nextCursor)
            ? PolymarketConstants.InitialCursor
            : nextCursor;

        if (string.Equals(cursor, PolymarketConstants.EndCursor, StringComparison.Ordinal))
        {
            return PolymarketApiResult<IReadOnlyList<BuilderTradeInfo>>.Success(200, Array.Empty<BuilderTradeInfo>());
        }

        var trades = new List<BuilderTradeInfo>();
        var seenCursors = new HashSet<string>(StringComparer.Ordinal);
        var statusCode = 200;

        while (!string.Equals(cursor, PolymarketConstants.EndCursor, StringComparison.Ordinal))
        {
            if (!seenCursors.Add(cursor))
            {
                return PolymarketApiResult<IReadOnlyList<BuilderTradeInfo>>.Failure(
                    statusCode,
                    $"Polymarket /builder/trades pagination cursor did not advance: {cursor}",
                    rawBody: null);
            }

            var relativeUrl = BuildBuilderTradesUrl(builderCode, market, assetId, tradeId, before, after, cursor);

            var page = await SendAsync<PaginatedResponse<BuilderTradeInfo>>(
                HttpMethod.Get,
                relativeUrl: relativeUrl,
                signingRequestPath: PolymarketClobEndpoints.GetBuilderTrades,
                authLevel: PolymarketAuthLevel.None,
                body: null,
                nonce: null,
                cancellationToken).ConfigureAwait(false);

            if (!page.IsSuccess || page.Data is null)
            {
                return PolymarketApiResult<IReadOnlyList<BuilderTradeInfo>>.Failure(
                    page.StatusCode,
                    page.Error?.Message,
                    page.Error?.RawBody);
            }

            statusCode = page.StatusCode;

            if (page.Data.Data is { Count: > 0 } pageTrades)
            {
                trades.AddRange(pageTrades);
            }

            if (string.IsNullOrWhiteSpace(page.Data.NextCursor))
            {
                return PolymarketApiResult<IReadOnlyList<BuilderTradeInfo>>.Failure(
                    statusCode,
                    "Polymarket /builder/trades response did not include next_cursor",
                    rawBody: null);
            }

            cursor = page.Data.NextCursor;
        }

        return PolymarketApiResult<IReadOnlyList<BuilderTradeInfo>>.Success(statusCode, trades);
    }

    private static string BuildBuilderTradesUrl(
        string builderCode,
        string? market,
        string? assetId,
        string? tradeId,
        string? before,
        string? after,
        string nextCursor)
    {
        var queryParams = new List<string>
        {
            $"builder_code={Uri.EscapeDataString(builderCode.Trim())}"
        };

        AddQueryParam(queryParams, "market", market);
        AddQueryParam(queryParams, "asset_id", assetId);
        AddQueryParam(queryParams, "id", tradeId);
        AddQueryParam(queryParams, "before", before);
        AddQueryParam(queryParams, "after", after);
        queryParams.Add($"next_cursor={Uri.EscapeDataString(nextCursor)}");

        return $"{PolymarketClobEndpoints.GetBuilderTrades}?{string.Join("&", queryParams)}";
    }

    private static void AddQueryParam(List<string> queryParams, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            queryParams.Add($"{name}={Uri.EscapeDataString(value.Trim())}");
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Balance
    // ─────────────────────────────────────────────────────────────

    public Task<PolymarketApiResult<BalanceAllowance>> GetBalanceAllowanceAsync(CancellationToken cancellationToken = default)
    {
        return SendAsync<BalanceAllowance>(
            HttpMethod.Get,
            relativeUrl: PolymarketClobEndpoints.GetBalanceAllowance,
            signingRequestPath: PolymarketClobEndpoints.GetBalanceAllowance,
            authLevel: PolymarketAuthLevel.L2,
            body: null,
            nonce: null,
            cancellationToken);
    }

    // ─────────────────────────────────────────────────────────────
    // Internal Helpers
    // ─────────────────────────────────────────────────────────────

    private async Task<PolymarketApiResult<T>> SendAsync<T>(
        HttpMethod method,
        string relativeUrl,
        string signingRequestPath,
        PolymarketAuthLevel authLevel,
        object? body,
        int? nonce,
        CancellationToken cancellationToken,
        string? idempotencyKey = null)
    {
        var raw = await SendRawAsync(
            method,
            relativeUrl,
            signingRequestPath,
            authLevel,
            body,
            nonce,
            cancellationToken,
            idempotencyKey).ConfigureAwait(false);

        if (!raw.IsSuccess)
        {
            return PolymarketApiResult<T>.Failure(raw.StatusCode, raw.Error?.Message, raw.Error?.RawBody);
        }

        try
        {
            var obj = JsonSerializer.Deserialize<T>(raw.Data ?? "null", ResponseJsonOptions);
            if (obj is null)
            {
                return PolymarketApiResult<T>.Failure(raw.StatusCode, "响应 JSON 反序列化为 null", raw.Data);
            }

            return PolymarketApiResult<T>.Success(raw.StatusCode, obj);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Polymarket API 响应解析失败：{Method} {Url}", method, relativeUrl);
            return PolymarketApiResult<T>.Failure(raw.StatusCode, "响应 JSON 解析失败", raw.Data);
        }
    }

    private async Task<PolymarketApiResult<string>> SendRawAsync(
        HttpMethod method,
        string relativeUrl,
        string signingRequestPath,
        PolymarketAuthLevel authLevel,
        object? body,
        int? nonce,
        CancellationToken cancellationToken,
        string? idempotencyKey = null)
    {
        var serializedBody = body is null ? null : JsonSerializer.Serialize(body, RequestJsonOptions);

        var pipeline = IsIdempotent(method)
            ? _idempotentPipeline
            : _nonIdempotentPipeline;

        HttpRequestMessage BuildRequest()
        {
            var req = new HttpRequestMessage(method, new Uri(relativeUrl, UriKind.Relative));

            if (serializedBody is not null)
            {
                req.Content = new StringContent(serializedBody, Encoding.UTF8, "application/json");
            }

            // 手动指定的 idempotency key 优先于 handler 自动生成
            if (!string.IsNullOrWhiteSpace(idempotencyKey))
            {
                req.Headers.TryAddWithoutValidation(PolymarketConstants.POLY_IDEMPOTENCY_KEY_HEADER, idempotencyKey);
            }

            req.Options.Set(
                PolymarketAuthHandler.SigningContextKey,
                new PolymarketSigningContext(
                    authLevel,
                    signingRequestPath,
                    serializedBody,
                    Nonce: nonce));

            return req;
        }

        HttpResponseMessage response;
        try
        {
            response = await pipeline.ExecuteAsync(
                async token =>
                {
                    using var req = BuildRequest();
                    return await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (BrokenCircuitException ex)
        {
            _logger.LogWarning(ex, "Polymarket 断路器开启，拒绝请求：{Method} {Url}", method, relativeUrl);
            return PolymarketApiResult<string>.Failure((int)HttpStatusCode.ServiceUnavailable, "断路器已开启", ex.Message);
        }
        catch (PolymarketRateLimitRejectedException ex)
        {
            // 客户端限流（本地），不等同于服务端 429
            _logger.LogWarning(ex, "Polymarket 客户端限流拒绝：{Method} {Url}", method, relativeUrl);
            return PolymarketApiResult<string>.Failure((int)HttpStatusCode.TooManyRequests, "客户端限流拒绝", ex.Message);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Polymarket HTTP 请求异常：{Method} {Url}", method, relativeUrl);
            return PolymarketApiResult<string>.Failure((int)HttpStatusCode.ServiceUnavailable, "网络/HTTP 异常", ex.Message);
        }

        using (response)
        {
            var statusCode = (int)response.StatusCode;
            var raw = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return PolymarketApiResult<string>.Success(statusCode, raw);
            }

            _logger.LogWarning(
                "Polymarket API 非 2xx：{StatusCode} {Method} {Url}，Body: {Body}",
                statusCode,
                method,
                relativeUrl,
                Truncate(raw, 2000));

            return PolymarketApiResult<string>.Failure(
                statusCode,
                response.ReasonPhrase ?? "非成功状态码",
                raw);
        }
    }

    private static bool IsIdempotent(HttpMethod method) =>
        method == HttpMethod.Get ||
        method == HttpMethod.Delete ||
        method == HttpMethod.Put ||
        method == HttpMethod.Head ||
        method == HttpMethod.Options;

    private ResiliencePipeline<HttpResponseMessage> BuildIdempotentPipeline(PolymarketResilienceOptions opt)
    {
        var retry = new RetryStrategyOptions<HttpResponseMessage>
        {
            MaxRetryAttempts = Math.Max(0, opt.MaxRetryAttempts),
            BackoffType = DelayBackoffType.Exponential,
            Delay = opt.BaseDelay <= TimeSpan.Zero ? TimeSpan.FromSeconds(1) : opt.BaseDelay,
            UseJitter = opt.UseJitter,
            ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                .Handle<HttpRequestException>()
                .HandleResult(r =>
                    r.StatusCode == HttpStatusCode.TooManyRequests ||
                    (int)r.StatusCode >= 500),
            DelayGenerator = static args =>
            {
                // 优先尊重 Retry-After（若存在）
                if (args.Outcome.Result is HttpResponseMessage response &&
                    response.Headers.RetryAfter?.Delta is TimeSpan delta &&
                    delta > TimeSpan.Zero)
                {
                    return new ValueTask<TimeSpan?>(delta);
                }

                return new ValueTask<TimeSpan?>((TimeSpan?)null);
            },
            OnRetry = static args =>
            {
                // 避免重试时泄露连接：Dispose 上一次响应
                args.Outcome.Result?.Dispose();
                return default;
            }
        };

        var breaker = new CircuitBreakerStrategyOptions<HttpResponseMessage>
        {
            FailureRatio = opt.CircuitFailureRatio <= 0 ? 0.5 : opt.CircuitFailureRatio,
            SamplingDuration = opt.CircuitSamplingDuration <= TimeSpan.Zero ? TimeSpan.FromSeconds(30) : opt.CircuitSamplingDuration,
            MinimumThroughput = Math.Max(1, opt.CircuitMinimumThroughput),
            BreakDuration = opt.CircuitBreakDuration <= TimeSpan.Zero ? TimeSpan.FromSeconds(30) : opt.CircuitBreakDuration,
            ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                .Handle<HttpRequestException>()
                .HandleResult(r => (int)r.StatusCode >= 500)
        };

        // 重要：先 CircuitBreaker 后 Retry，使断路器统计的是“重试后的最终结果”
        return new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddCircuitBreaker(breaker)
            .AddRetry(retry)
            .Build();
    }

    private ResiliencePipeline<HttpResponseMessage> BuildNonIdempotentPipeline(PolymarketResilienceOptions opt)
    {
        // 非幂等请求默认不自动重试，但依然使用断路器保护下游
        var breaker = new CircuitBreakerStrategyOptions<HttpResponseMessage>
        {
            FailureRatio = opt.CircuitFailureRatio <= 0 ? 0.5 : opt.CircuitFailureRatio,
            SamplingDuration = opt.CircuitSamplingDuration <= TimeSpan.Zero ? TimeSpan.FromSeconds(30) : opt.CircuitSamplingDuration,
            MinimumThroughput = Math.Max(1, opt.CircuitMinimumThroughput),
            BreakDuration = opt.CircuitBreakDuration <= TimeSpan.Zero ? TimeSpan.FromSeconds(30) : opt.CircuitBreakDuration,
            ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                .Handle<HttpRequestException>()
                .HandleResult(r => (int)r.StatusCode >= 500)
        };

        return new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddCircuitBreaker(breaker)
            .Build();
    }

    private static string Truncate(string? text, int maxLen)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text.Length <= maxLen ? text : text[..maxLen] + "...(truncated)";
    }
}
