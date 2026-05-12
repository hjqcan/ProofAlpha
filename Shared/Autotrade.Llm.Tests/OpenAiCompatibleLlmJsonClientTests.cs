using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Autotrade.Llm.Tests;

public sealed class OpenAiCompatibleLlmJsonClientTests
{
    [Fact]
    public async Task CompleteJsonAsync_ParsesJsonObjectAfterThinkingBlock()
    {
        var client = CreateClient(new QueueHttpHandler(
            _ => Response(HttpStatusCode.OK, ChatResponse("<think>private</think>{\"name\":\"alpha\",\"score\":2}"))));

        var result = await client.CompleteJsonAsync<TestDocument>(
            new LlmJsonRequest("system", "user"));

        Assert.Equal("alpha", result.Value.Name);
        Assert.Equal(2, result.Value.Score);
        Assert.Contains("\"name\":\"alpha\"", result.RawJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteJsonAsync_RetriesRetryableGatewayStatus()
    {
        var handler = new QueueHttpHandler(
            _ => Response(HttpStatusCode.TooManyRequests, "{\"error\":\"rate limited\"}"),
            _ => Response(HttpStatusCode.OK, ChatResponse("{\"name\":\"retry\",\"score\":3}")));
        var client = CreateClient(handler, maxRetries: 2);

        var result = await client.CompleteJsonAsync<TestDocument>(
            new LlmJsonRequest("system", "user"));

        Assert.Equal("retry", result.Value.Name);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task CompleteJsonAsync_ThrowsOnInvalidJson()
    {
        var client = CreateClient(new QueueHttpHandler(
            _ => Response(HttpStatusCode.OK, ChatResponse("not-json"))));

        await Assert.ThrowsAsync<LlmClientException>(() =>
            client.CompleteJsonAsync<TestDocument>(new LlmJsonRequest("system", "user")));
    }

    [Fact]
    public async Task CompleteJsonAsync_RunsAppLevelValidation()
    {
        var client = CreateClient(new QueueHttpHandler(
            _ => Response(HttpStatusCode.OK, ChatResponse("{\"name\":\"bad\",\"score\":0}"))));

        var exception = await Assert.ThrowsAsync<LlmJsonValidationException>(() =>
            client.CompleteJsonAsync<ValidatingDocument>(new LlmJsonRequest("system", "user")));

        Assert.Contains("score must be positive", exception.Errors);
    }

    [Fact]
    public async Task CompleteJsonAsync_UsesGoodMemoryStyleEnvPrefixOverrides()
    {
        const string prefix = "PROOFALPHA_TEST_LLM";
        var previousProvider = Environment.GetEnvironmentVariable($"{prefix}_PROVIDER");
        var previousModel = Environment.GetEnvironmentVariable($"{prefix}_MODEL");
        var previousApiKey = Environment.GetEnvironmentVariable($"{prefix}_API_KEY");
        var previousBaseUrl = Environment.GetEnvironmentVariable($"{prefix}_BASE_URL");
        try
        {
            Environment.SetEnvironmentVariable($"{prefix}_PROVIDER", "openai");
            Environment.SetEnvironmentVariable($"{prefix}_MODEL", "env-model");
            Environment.SetEnvironmentVariable($"{prefix}_API_KEY", "env-key");
            Environment.SetEnvironmentVariable($"{prefix}_BASE_URL", "https://llm-gateway.test/v1");

            HttpRequestMessage? capturedRequest = null;
            string? capturedBody = null;
            var client = CreateClient(
                new QueueHttpHandler(request =>
                {
                    capturedRequest = request;
                    capturedBody = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
                    return Response(HttpStatusCode.OK, ChatResponse("{\"name\":\"env\",\"score\":5}"));
                }),
                envPrefix: prefix);

            var result = await client.CompleteJsonAsync<TestDocument>(
                new LlmJsonRequest("system", "user"));

            Assert.Equal("env", result.Value.Name);
            Assert.Equal(new Uri("https://llm-gateway.test/v1/chat/completions"), capturedRequest?.RequestUri);
            Assert.Equal("Bearer", capturedRequest?.Headers.Authorization?.Scheme);
            Assert.Equal("env-key", capturedRequest?.Headers.Authorization?.Parameter);
            Assert.Contains("\"model\":\"env-model\"", capturedBody, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable($"{prefix}_PROVIDER", previousProvider);
            Environment.SetEnvironmentVariable($"{prefix}_MODEL", previousModel);
            Environment.SetEnvironmentVariable($"{prefix}_API_KEY", previousApiKey);
            Environment.SetEnvironmentVariable($"{prefix}_BASE_URL", previousBaseUrl);
        }
    }

    private static OpenAiCompatibleLlmJsonClient CreateClient(
        HttpMessageHandler handler,
        int maxRetries = 1,
        string? envPrefix = null)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        };
        var options = new OpenAiCompatibleLlmOptions
        {
            BaseUrl = "http://localhost",
            ApiKeyEnvVar = "AUTOTRADE_TEST_MISSING_API_KEY",
            EnvPrefix = envPrefix,
            Model = "test-model",
            MaxRetries = maxRetries,
            TimeoutSeconds = 10
        };
        return new OpenAiCompatibleLlmJsonClient(
            httpClient,
            new StaticOptionsMonitor<OpenAiCompatibleLlmOptions>(options),
            NullLogger<OpenAiCompatibleLlmJsonClient>.Instance);
    }

    private static HttpResponseMessage Response(HttpStatusCode statusCode, string body)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }

    private static string ChatResponse(string content)
    {
        return JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new
                {
                    message = new
                    {
                        content
                    }
                }
            }
        });
    }

    private sealed record TestDocument(string Name, int Score);

    private sealed record ValidatingDocument(string Name, int Score) : ILlmJsonValidatable
    {
        public IReadOnlyList<string> Validate()
        {
            return Score > 0 ? Array.Empty<string>() : new[] { "score must be positive" };
        }
    }

    private sealed class QueueHttpHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses;

        public QueueHttpHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responses)
        {
            _responses = new Queue<Func<HttpRequestMessage, HttpResponseMessage>>(responses);
        }

        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("No test response queued.");
            }

            return Task.FromResult(_responses.Dequeue()(request));
        }
    }

    private sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
    {
        private readonly T _value;

        public StaticOptionsMonitor(T value)
        {
            _value = value;
        }

        public T CurrentValue => _value;

        public T Get(string? name) => _value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
