using System.Net;
using Autotrade.Api.ControlRoom;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Autotrade.Api.Middleware;

public sealed class ControlRoomLocalAccessMiddleware(
    RequestDelegate next,
    IOptionsMonitor<ControlRoomOptions> options)
{
    private static readonly PathString[] ProtectedPaths =
    [
        new("/api/control-room"),
        new("/api/readiness")
    ];

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!IsProtectedPath(context.Request.Path)
            || !options.CurrentValue.RequireLocalAccess
            || IsLoopback(context.Connection.RemoteIpAddress))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        var problemDetails = new ProblemDetails
        {
            Title = "Control room access denied.",
            Detail = "Control room and readiness routes only accept loopback requests unless local access protection is explicitly disabled.",
            Status = StatusCodes.Status403Forbidden,
            Instance = context.Request.Path
        };
        problemDetails.Extensions["errorCode"] = "CONTROL_ROOM_LOCAL_ACCESS_REQUIRED";
        problemDetails.Extensions["traceId"] = context.TraceIdentifier;

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(problemDetails).ConfigureAwait(false);
    }

    private static bool IsProtectedPath(PathString path)
        => ProtectedPaths.Any(protectedPath => path.StartsWithSegments(
            protectedPath,
            StringComparison.OrdinalIgnoreCase));

    private static bool IsLoopback(IPAddress? remoteIpAddress)
    {
        if (remoteIpAddress is null)
        {
            return false;
        }

        if (IPAddress.IsLoopback(remoteIpAddress))
        {
            return true;
        }

        return remoteIpAddress.IsIPv4MappedToIPv6
            && IPAddress.IsLoopback(remoteIpAddress.MapToIPv4());
    }
}

public static class ControlRoomLocalAccessMiddlewareExtensions
{
    public static IApplicationBuilder UseControlRoomLocalAccess(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.UseMiddleware<ControlRoomLocalAccessMiddleware>();
    }
}
