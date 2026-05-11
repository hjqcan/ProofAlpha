using WireMock.Server;
using WireMock.Settings;

namespace Autotrade.Polymarket.Tests;

/// <summary>
/// WireMock mock 服务器 fixture，供契约测试使用。
/// </summary>
public sealed class MockServerFixture : IDisposable
{
    public WireMockServer Server { get; }

    // 优先使用 Server.Urls 中的第一个 URL（通常是 http://127.0.0.1:xxx 或 http://localhost:xxx）
    public string BaseUrl
    {
        get
        {
            var url = Server.Urls?.FirstOrDefault() ?? Server.Url;
            if (string.IsNullOrEmpty(url))
            {
                throw new InvalidOperationException("Server not started");
            }
            // 确保使用 127.0.0.1 而非 localhost（避免 IPv6 解析问题）
            return url.Replace("localhost", "127.0.0.1");
        }
    }

    public MockServerFixture()
    {
        Server = WireMockServer.Start(new WireMockServerSettings
        {
            UseSSL = false,
            Port = null, // 自动分配端口
            StartAdminInterface = false,
            ReadStaticMappings = false,
            WatchStaticMappings = false,
            AllowPartialMapping = false
        });
    }

    public void Reset() => Server.Reset();

    public void Dispose() => Server.Stop();
}
