using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Autotrade.Polymarket.Abstractions;
using Autotrade.Polymarket.Models;
using Autotrade.Polymarket.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace Autotrade.Polymarket;

/// <summary>
/// Polymarket Data API 客户端实现。
/// Data API 用于查询用户持仓等信息（公开 API，无需签名）。
/// </summary>
public sealed class PolymarketDataClient : IPolymarketDataClient
{
    private static readonly JsonSerializerOptions ResponseJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<PolymarketDataClient> _logger;
    private readonly ResiliencePipeline<HttpResponseMessage> _pipeline;

    public PolymarketDataClient(
        HttpClient httpClient,
        IOptions<PolymarketDataOptions> dataOptions,
        IOptions<PolymarketResilienceOptions> resilienceOptions,
        ILogger<PolymarketDataClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var opts = dataOptions?.Value ?? throw new ArgumentNullException(nameof(dataOptions));

        if (_httpClient.BaseAddress is null)
        {
            _httpClient.BaseAddress = new Uri(opts.Host.TrimEnd('/') + "/");
        }

        _httpClient.Timeout = opts.Timeout;

        var ro = resilienceOptions?.Value ?? new PolymarketResilienceOptions();
        _pipeline = BuildPipeline(ro);
    }

    /// <inheritdoc />
    public async Task<PolymarketApiResult<IReadOnlyList<UserPosition>>> GetPositionsAsync(
        string userAddress,
        string? market = null,
        bool? redeemable = null,
        bool? mergeable = null,
        int limit = 100,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userAddress))
        {
            throw new ArgumentException("userAddress 不能为空", nameof(userAddress));
        }

        var queryParams = new List<string>
        {
            $"user={Uri.EscapeDataString(userAddress)}",
            $"limit={Math.Clamp(limit, 1, 500)}",
            $"offset={Math.Max(0, offset)}"
        };

        if (!string.IsNullOrWhiteSpace(market))
        {
            queryParams.Add($"market={Uri.EscapeDataString(market)}");
        }

        if (redeemable.HasValue)
        {
            queryParams.Add($"redeemable={redeemable.Value.ToString().ToLowerInvariant()}");
        }

        if (mergeable.HasValue)
        {
            queryParams.Add($"mergeable={mergeable.Value.ToString().ToLowerInvariant()}");
        }

        var relativeUrl = $"{PolymarketDataEndpoints.GetPositions}?{string.Join("&", queryParams)}";

        return await SendAsync<IReadOnlyList<UserPosition>>(relativeUrl, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<PolymarketApiResult<IReadOnlyList<UserPosition>>> GetClosedPositionsAsync(
        string userAddress,
        int limit = 100,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userAddress))
        {
            throw new ArgumentException("userAddress 不能为空", nameof(userAddress));
        }

        var queryParams = new List<string>
        {
            $"user={Uri.EscapeDataString(userAddress)}",
            $"limit={Math.Clamp(limit, 1, 500)}",
            $"offset={Math.Max(0, offset)}"
        };

        var relativeUrl = $"{PolymarketDataEndpoints.GetClosedPositions}?{string.Join("&", queryParams)}";

        return await SendAsync<IReadOnlyList<UserPosition>>(relativeUrl, cancellationToken).ConfigureAwait(false);
    }

    private async Task<PolymarketApiResult<T>> SendAsync<T>(
        string relativeUrl,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await _pipeline.ExecuteAsync(
                async token =>
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, new Uri(relativeUrl, UriKind.Relative));
                    return await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (BrokenCircuitException ex)
        {
            _logger.LogWarning(ex, "Polymarket Data API 断路器开启：{Url}", relativeUrl);
            return PolymarketApiResult<T>.Failure((int)HttpStatusCode.ServiceUnavailable, "断路器已开启", ex.Message);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Polymarket Data API HTTP 请求异常：{Url}", relativeUrl);
            return PolymarketApiResult<T>.Failure((int)HttpStatusCode.ServiceUnavailable, "网络/HTTP 异常", ex.Message);
        }

        using (response)
        {
            var statusCode = (int)response.StatusCode;
            var raw = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Polymarket Data API 非 2xx：{StatusCode} {Url}，Body: {Body}",
                    statusCode,
                    relativeUrl,
                    Truncate(raw, 2000));

                return PolymarketApiResult<T>.Failure(statusCode, response.ReasonPhrase ?? "非成功状态码", raw);
            }

            try
            {
                // Data API 可能返回数组或带 data 字段的对象
                // 尝试两种格式解析
                T? obj;

                // 尝试直接解析为目标类型
                obj = JsonSerializer.Deserialize<T>(raw ?? "null", ResponseJsonOptions);

                // 如果目标是 IReadOnlyList 但解析失败，尝试从 data 字段提取
                if (obj is null && typeof(T).IsAssignableTo(typeof(IReadOnlyList<UserPosition>)))
                {
                    var wrapper = JsonSerializer.Deserialize<UserPositionsResponse>(raw ?? "null", ResponseJsonOptions);
                    if (wrapper?.Data is not null)
                    {
                        obj = (T)(object)wrapper.Data;
                    }
                }

                if (obj is null)
                {
                    return PolymarketApiResult<T>.Failure(statusCode, "响应 JSON 反序列化为 null", raw);
                }

                return PolymarketApiResult<T>.Success(statusCode, obj);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Polymarket Data API 响应解析失败：{Url}", relativeUrl);
                return PolymarketApiResult<T>.Failure(statusCode, "响应 JSON 解析失败", raw);
            }
        }
    }

    private ResiliencePipeline<HttpResponseMessage> BuildPipeline(PolymarketResilienceOptions opt)
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

        return new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddCircuitBreaker(breaker)
            .AddRetry(retry)
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
