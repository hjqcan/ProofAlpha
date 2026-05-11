using Autotrade.Api.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Autotrade.Api.Tests;

public sealed class GlobalExceptionHandlerTests
{
    [Fact]
    public async Task InvokeAsync_RedactsSensitiveExceptionMessageFromProblemDetailsAndLogs()
    {
        var logger = new RecordingLogger();
        var middleware = new GlobalExceptionHandler(
            _ => throw new InvalidOperationException(
                "Failed with apiSecret=secret-value and Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9"),
            logger);
        var context = new DefaultHttpContext
        {
            Response =
            {
                Body = new MemoryStream()
            }
        };

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        var body = await reader.ReadToEndAsync();

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Contains("***REDACTED***", body, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-value", body, StringComparison.Ordinal);
        Assert.DoesNotContain("eyJhbGci", body, StringComparison.Ordinal);
        var log = Assert.Single(logger.Messages);
        Assert.Contains("***REDACTED***", log, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-value", log, StringComparison.Ordinal);
        Assert.DoesNotContain("eyJhbGci", log, StringComparison.Ordinal);
        Assert.Empty(logger.Exceptions);
    }

    private sealed class RecordingLogger : ILogger<GlobalExceptionHandler>
    {
        public List<string> Messages { get; } = [];

        public List<Exception> Exceptions { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
            if (exception is not null)
            {
                Exceptions.Add(exception);
            }
        }
    }
}
