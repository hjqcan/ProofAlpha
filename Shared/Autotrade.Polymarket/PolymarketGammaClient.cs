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
/// Polymarket Gamma REST API 客户端（市场元数据，只读）。
/// </summary>
public sealed class PolymarketGammaClient : IPolymarketGammaClient
{
    private static readonly JsonSerializerOptions ResponseJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<PolymarketGammaClient> _logger;
    private readonly PolymarketGammaOptions _gammaOptions;
    private readonly ResiliencePipeline<HttpResponseMessage> _pipeline;

    public PolymarketGammaClient(
        HttpClient httpClient,
        IOptions<PolymarketGammaOptions> gammaOptions,
        IOptions<PolymarketResilienceOptions> resilienceOptions,
        ILogger<PolymarketGammaClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _gammaOptions = gammaOptions?.Value ?? throw new ArgumentNullException(nameof(gammaOptions));

        if (_httpClient.BaseAddress is null)
        {
            _httpClient.BaseAddress = new Uri(_gammaOptions.Host.TrimEnd('/') + "/");
        }
        _httpClient.Timeout = _gammaOptions.Timeout;

        var ro = resilienceOptions?.Value ?? throw new ArgumentNullException(nameof(resilienceOptions));
        _pipeline = BuildIdempotentPipeline(ro);
    }

    public async Task<PolymarketApiResult<IReadOnlyList<GammaMarket>>> ListMarketsAsync(
        int limit = 100,
        int offset = 0,
        bool closed = false,
        string? order = "id",
        bool ascending = false,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0) limit = 100;
        if (offset < 0) offset = 0;

        var query = new List<string>
        {
            $"limit={limit}",
            $"offset={offset}",
            $"closed={closed.ToString().ToLowerInvariant()}",
            $"ascending={ascending.ToString().ToLowerInvariant()}"
        };

        if (!string.IsNullOrWhiteSpace(order))
        {
            query.Add($"order={Uri.EscapeDataString(order)}");
        }

        var relativeUrl = $"{PolymarketGammaEndpoints.Markets}?{string.Join("&", query)}";

        try
        {
            var response = await _pipeline.ExecuteAsync(
                async ct => await _httpClient.GetAsync(relativeUrl, ct).ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false);

            using (response)
            {
                var statusCode = (int)response.StatusCode;
                var raw = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "Gamma API 非 2xx：{StatusCode} GET {Url}，Body: {Body}",
                        statusCode,
                        relativeUrl,
                        Truncate(raw, 2000));

                    return PolymarketApiResult<IReadOnlyList<GammaMarket>>.Failure(
                        statusCode,
                        response.ReasonPhrase ?? "非成功状态码",
                        raw);
                }

                var markets = JsonSerializer.Deserialize<List<GammaMarket>>(raw, ResponseJsonOptions) ?? new List<GammaMarket>();
                return PolymarketApiResult<IReadOnlyList<GammaMarket>>.Success(statusCode, markets);
            }
        }
        catch (BrokenCircuitException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Gamma API 断路器打开：GET {Url}", relativeUrl);
            return PolymarketApiResult<IReadOnlyList<GammaMarket>>.Failure(0, "Gamma circuit breaker open", null);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Gamma API 请求异常：GET {Url}", relativeUrl);
            return PolymarketApiResult<IReadOnlyList<GammaMarket>>.Failure(0, ex.Message, null);
        }
    }

    private static ResiliencePipeline<HttpResponseMessage> BuildIdempotentPipeline(PolymarketResilienceOptions opt)
    {
        var retry = new RetryStrategyOptions<HttpResponseMessage>
        {
            MaxRetryAttempts = Math.Max(0, opt.MaxRetryAttempts),
            BackoffType = DelayBackoffType.Exponential,
            Delay = opt.BaseDelay <= TimeSpan.Zero ? TimeSpan.FromSeconds(1) : opt.BaseDelay,
            UseJitter = opt.UseJitter,
            ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                .Handle<HttpRequestException>()
                .Handle<TimeoutException>()
                .Handle<OperationCanceledException>(static ex => IsHttpClientTimeout(ex))
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
                .Handle<TimeoutException>()
                .Handle<OperationCanceledException>(static ex => IsHttpClientTimeout(ex))
                .HandleResult(r => (int)r.StatusCode >= 500)
        };

        return new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddCircuitBreaker(breaker)
            .AddRetry(retry)
            .Build();
    }

    private static bool IsHttpClientTimeout(OperationCanceledException ex) =>
        ex.InnerException is TimeoutException;

    private static string Truncate(string? text, int maxLen)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text.Length <= maxLen ? text : text[..maxLen] + "...(truncated)";
    }
}

