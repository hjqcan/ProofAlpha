using System.Net;
using Autotrade.Api.ControlRoom;
using Autotrade.Api.Middleware;
using Microsoft.AspNetCore.Http;

namespace Autotrade.Api.Tests;

public sealed class ControlRoomLocalAccessMiddlewareTests
{
    [Fact]
    public async Task RemoteControlRoomRequestIsForbiddenWhenLocalAccessIsRequired()
    {
        var nextCalled = false;
        var context = CreateContext("/api/control-room/snapshot", IPAddress.Parse("203.0.113.10"));
        var middleware = CreateMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        var body = await reader.ReadToEndAsync();
        Assert.Contains("CONTROL_ROOM_LOCAL_ACCESS_REQUIRED", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RemoteReadinessRequestIsForbiddenWhenLocalAccessIsRequired()
    {
        var nextCalled = false;
        var context = CreateContext("/api/readiness", IPAddress.Parse("203.0.113.10"));
        var middleware = CreateMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        var body = await reader.ReadToEndAsync();
        Assert.Contains("CONTROL_ROOM_LOCAL_ACCESS_REQUIRED", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoopbackControlRoomRequestInvokesNextMiddleware()
    {
        var nextCalled = false;
        var context = CreateContext("/api/control-room/snapshot", IPAddress.Loopback);
        var middleware = CreateMiddleware(innerContext =>
        {
            nextCalled = true;
            innerContext.Response.StatusCode = StatusCodes.Status204NoContent;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status204NoContent, context.Response.StatusCode);
    }

    [Fact]
    public async Task RemoteNonControlRoomRequestInvokesNextMiddleware()
    {
        var nextCalled = false;
        var context = CreateContext("/health", IPAddress.Parse("203.0.113.10"));
        var middleware = CreateMiddleware(innerContext =>
        {
            nextCalled = true;
            innerContext.Response.StatusCode = StatusCodes.Status204NoContent;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status204NoContent, context.Response.StatusCode);
    }

    [Fact]
    public async Task RemoteControlRoomRequestCanBeAllowedByExplicitConfiguration()
    {
        var nextCalled = false;
        var context = CreateContext("/api/control-room/snapshot", IPAddress.Parse("203.0.113.10"));
        var middleware = CreateMiddleware(
            innerContext =>
            {
                nextCalled = true;
                innerContext.Response.StatusCode = StatusCodes.Status204NoContent;
                return Task.CompletedTask;
            },
            new ControlRoomOptions { RequireLocalAccess = false });

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status204NoContent, context.Response.StatusCode);
    }

    private static ControlRoomLocalAccessMiddleware CreateMiddleware(
        RequestDelegate next,
        ControlRoomOptions? options = null)
    {
        return new ControlRoomLocalAccessMiddleware(
            next,
            new TestOptionsMonitor<ControlRoomOptions>(options ?? new ControlRoomOptions()));
    }

    private static DefaultHttpContext CreateContext(string path, IPAddress remoteIpAddress)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Connection.RemoteIpAddress = remoteIpAddress;
        context.Response.Body = new MemoryStream();
        return context;
    }
}
