using Autotrade.Application.Security;
using Microsoft.AspNetCore.Mvc;

namespace Autotrade.Api.Middleware;

public sealed class GlobalExceptionHandler(
    RequestDelegate next,
    ILogger<GlobalExceptionHandler> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            await next(context).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            context.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
        }
        catch (Exception exception)
        {
            await HandleExceptionAsync(context, exception).ConfigureAwait(false);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        if (context.Response.HasStarted)
        {
            logger.LogError(
                "Unhandled exception after the response started: {ExceptionType} {Message}",
                exception.GetType().FullName,
                SecretRedactor.Redact(exception.Message));
            throw exception;
        }

        var (statusCode, title, errorCode) = exception switch
        {
            ArgumentException => (
                StatusCodes.Status400BadRequest,
                "Request validation failed.",
                "REQUEST_VALIDATION_FAILED"),
            _ => (
                StatusCodes.Status500InternalServerError,
                "An unexpected server error occurred.",
                "INTERNAL_ERROR")
        };

        var safeMessage = SecretRedactor.Redact(exception.Message);
        if (statusCode >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(
                "Unhandled request exception {ExceptionType}: {Message}",
                exception.GetType().FullName,
                safeMessage);
        }
        else
        {
            logger.LogWarning(
                "Request exception {ExceptionType}: {Message}",
                exception.GetType().FullName,
                safeMessage);
        }

        var problemDetails = new ProblemDetails
        {
            Title = title,
            Detail = safeMessage,
            Status = statusCode,
            Instance = context.Request.Path
        };
        problemDetails.Extensions["errorCode"] = errorCode;
        problemDetails.Extensions["traceId"] = context.TraceIdentifier;

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(problemDetails).ConfigureAwait(false);
    }
}

public static class GlobalExceptionHandlerExtensions
{
    public static IApplicationBuilder UseGlobalExceptionHandler(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.UseMiddleware<GlobalExceptionHandler>();
    }
}
