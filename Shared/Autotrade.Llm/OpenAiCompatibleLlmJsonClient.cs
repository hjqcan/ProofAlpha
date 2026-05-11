using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Autotrade.Llm;

public sealed class OpenAiCompatibleLlmJsonClient : ILlmJsonClient
{
    private static readonly Regex ThinkingBlockRegex = new(
        "<think>[\\s\\S]*?</think>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly IOptionsMonitor<OpenAiCompatibleLlmOptions> _options;
    private readonly ILogger<OpenAiCompatibleLlmJsonClient> _logger;

    public OpenAiCompatibleLlmJsonClient(
        HttpClient httpClient,
        IOptionsMonitor<OpenAiCompatibleLlmOptions> options,
        ILogger<OpenAiCompatibleLlmJsonClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<LlmJsonResult<T>> CompleteJsonAsync<T>(
        LlmJsonRequest request,
        Func<T, IReadOnlyList<string>>? validator = null,
        CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(request);

        var attempts = Math.Max(1, _options.CurrentValue.MaxRetries);
        Exception? lastError = null;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                var rawText = await RequestTextAsync(request, cancellationToken).ConfigureAwait(false);
                var rawJson = ExtractStructuredJsonObject(rawText);
                var value = JsonSerializer.Deserialize<T>(rawJson, JsonOptions)
                    ?? throw new LlmClientException("LLM JSON response was empty.");

                var errors = Validate(value, validator);
                if (errors.Count > 0)
                {
                    throw new LlmJsonValidationException(errors);
                }

                return new LlmJsonResult<T>(value, rawJson, rawText);
            }
            catch (Exception ex) when (IsRetryableJsonFailure(ex) && attempt < attempts)
            {
                lastError = ex;
                _logger.LogWarning(ex, "LLM JSON attempt {Attempt}/{Attempts} failed.", attempt, attempts);
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken).ConfigureAwait(false);
            }
        }

        throw new LlmClientException("LLM structured JSON request failed.", lastError);
    }

    private async Task<string> RequestTextAsync(LlmJsonRequest request, CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        var baseUrl = string.IsNullOrWhiteSpace(options.BaseUrl)
            ? "https://api.openai.com/v1"
            : options.BaseUrl.TrimEnd('/');
        var requestUri = new Uri($"{baseUrl}/chat/completions");
        var apiKey = ResolveApiKey(options.ApiKeyEnvVar);
        var timeout = TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 10, 600));
        var attempts = Math.Max(1, options.MaxRetries);
        Exception? lastError = null;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, requestUri);
            httpRequest.Headers.Accept.ParseAdd("application/json");
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            }

            httpRequest.Content = JsonContent.Create(new
            {
                model = options.Model,
                messages = new[]
                {
                    new { role = "system", content = request.SystemPrompt },
                    new { role = "user", content = request.UserPrompt }
                },
                temperature = request.Temperature,
                response_format = new { type = "json_object" }
            }, options: JsonOptions);

            try
            {
                using var response = await _httpClient.SendAsync(httpRequest, cts.Token).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);

                if (IsRetryableStatusCode(response.StatusCode) && attempt < attempts)
                {
                    lastError = new LlmClientException($"LLM gateway returned {(int)response.StatusCode}.");
                    await Task.Delay(ComputeDelay(attempt), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new LlmClientException($"LLM gateway returned {(int)response.StatusCode}: {TrimForError(body)}");
                }

                return ExtractCompletionText(body);
            }
            catch (Exception ex) when (IsRetryableTransportFailure(ex) && attempt < attempts)
            {
                lastError = ex;
                _logger.LogWarning(ex, "LLM text attempt {Attempt}/{Attempts} failed.", attempt, attempts);
                await Task.Delay(ComputeDelay(attempt), cancellationToken).ConfigureAwait(false);
            }
        }

        throw new LlmClientException("LLM text request failed.", lastError);
    }

    private static IReadOnlyList<string> Validate<T>(
        T value,
        Func<T, IReadOnlyList<string>>? validator)
        where T : class
    {
        var errors = new List<string>();
        if (value is ILlmJsonValidatable validatable)
        {
            errors.AddRange(validatable.Validate());
        }

        if (validator is not null)
        {
            errors.AddRange(validator(value));
        }

        return errors.Where(error => !string.IsNullOrWhiteSpace(error)).ToArray();
    }

    private static bool IsRetryableTransportFailure(Exception exception)
    {
        return exception is TaskCanceledException or HttpRequestException or TimeoutException;
    }

    private static bool IsRetryableJsonFailure(Exception exception)
    {
        return exception is JsonException or LlmJsonValidationException or LlmClientException;
    }

    private static bool IsRetryableStatusCode(HttpStatusCode statusCode)
    {
        return statusCode is HttpStatusCode.TooManyRequests
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;
    }

    private static TimeSpan ComputeDelay(int attempt)
    {
        return TimeSpan.FromMilliseconds(Math.Min(5000, 500 * attempt * attempt));
    }

    private static string ExtractCompletionText(string body)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (root.TryGetProperty("error", out var error))
        {
            throw new LlmClientException($"LLM gateway error: {TrimForError(error.ToString())}");
        }

        if (!root.TryGetProperty("choices", out var choices)
            || choices.ValueKind != JsonValueKind.Array
            || choices.GetArrayLength() == 0)
        {
            throw new LlmClientException("LLM response did not include choices.");
        }

        var choice = choices[0];
        if (choice.TryGetProperty("message", out var message)
            && message.TryGetProperty("content", out var content)
            && content.ValueKind == JsonValueKind.String)
        {
            var text = content.GetString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return StripThinkingBlocks(text);
            }
        }

        if (choice.TryGetProperty("text", out var textElement)
            && textElement.ValueKind == JsonValueKind.String)
        {
            var text = textElement.GetString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return StripThinkingBlocks(text);
            }
        }

        throw new LlmClientException("LLM response choice did not include message content.");
    }

    private static string ExtractStructuredJsonObject(string text)
    {
        var stripped = StripThinkingBlocks(text).Trim();
        var start = stripped.IndexOf('{', StringComparison.Ordinal);
        var end = stripped.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            throw new LlmClientException("LLM response did not contain a JSON object.");
        }

        return stripped[start..(end + 1)];
    }

    private static string StripThinkingBlocks(string value)
    {
        return ThinkingBlockRegex.Replace(value, string.Empty);
    }

    private static string TrimForError(string value)
    {
        return value.Length <= 500 ? value : value[..500];
    }

    private static string? ResolveApiKey(string envVarName)
    {
        if (string.IsNullOrWhiteSpace(envVarName))
        {
            return null;
        }

        var value = Environment.GetEnvironmentVariable(envVarName);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value.Trim();
        }

        return DotEnvReader.TryGetValue(envVarName);
    }
}
