using System.Net;
using System.Text;
using Autotrade.Polymarket.Options;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using OptionsFactory = Microsoft.Extensions.Options.Options;

namespace Autotrade.Polymarket.Tests;

public sealed class PolymarketGammaClientTests
{
    [Fact]
    public async Task ListMarketsAsync_WhenHttpClientTimeouts_RetriesIdempotentRequest()
    {
        var handler = new FailThenSuccessHandler(static () =>
            new OperationCanceledException(
                "The request was canceled due to the configured HttpClient.Timeout of 10 seconds elapsing.",
                new TimeoutException("The operation was canceled.")));
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://gamma.test/"),
            Timeout = TimeSpan.FromSeconds(10)
        };

        var client = new PolymarketGammaClient(
            httpClient,
            OptionsFactory.Create(new PolymarketGammaOptions
            {
                Host = "https://gamma.test",
                Timeout = TimeSpan.FromSeconds(10)
            }),
            OptionsFactory.Create(new PolymarketResilienceOptions
            {
                MaxRetryAttempts = 1,
                BaseDelay = TimeSpan.FromMilliseconds(1),
                UseJitter = false
            }),
            NullLogger<PolymarketGammaClient>.Instance);

        var result = await client.ListMarketsAsync();

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().ContainSingle();
        handler.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task ListMarketsAsync_WhenCallerCancels_DoesNotRetryAsTimeout()
    {
        using var callerCancellation = new CancellationTokenSource();
        var handler = new FailThenSuccessHandler(() =>
        {
            callerCancellation.Cancel();
            return new OperationCanceledException(
                "The operation was canceled.",
                new TimeoutException("The operation was canceled."),
                callerCancellation.Token);
        });
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://gamma.test/"),
            Timeout = TimeSpan.FromSeconds(10)
        };

        var client = new PolymarketGammaClient(
            httpClient,
            OptionsFactory.Create(new PolymarketGammaOptions
            {
                Host = "https://gamma.test",
                Timeout = TimeSpan.FromSeconds(10)
            }),
            OptionsFactory.Create(new PolymarketResilienceOptions
            {
                MaxRetryAttempts = 1,
                BaseDelay = TimeSpan.FromMilliseconds(1),
                UseJitter = false
            }),
            NullLogger<PolymarketGammaClient>.Instance);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            client.ListMarketsAsync(cancellationToken: callerCancellation.Token));

        handler.CallCount.Should().Be(1);
    }

    private sealed class FailThenSuccessHandler(Func<Exception> createFirstFailure) : HttpMessageHandler
    {
        private int _callCount;

        public int CallCount => _callCount;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _callCount);
            if (call == 1)
            {
                throw createFirstFailure();
            }

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    [
                      {
                        "id": "market-1",
                        "conditionId": "condition-1",
                        "question": "Question 1",
                        "active": true,
                        "closed": false
                      }
                    ]
                    """,
                    Encoding.UTF8,
                    "application/json")
            };

            return Task.FromResult(response);
        }
    }
}
