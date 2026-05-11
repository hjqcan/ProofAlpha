using System.Text.Json;
using Autotrade.ArcSettlement.Application.Contract.Configuration;
using Autotrade.ArcSettlement.Application.Signals;
using Autotrade.ArcSettlement.Infra.Evm.Signals;
using Microsoft.Extensions.Options;

namespace Autotrade.ArcSettlement.Tests.Signals;

public sealed class HardhatArcSignalRegistryPublisherTests
{
    [Fact]
    public async Task PublishAsync_SerializesSignalRegistryRequestAndReturnsTransactionHash()
    {
        using var workspace = ArcContractsWorkspaceFixture.Create();
        var runner = new CapturingRunner(
            """
            {
              "transactionHash": "0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
              "confirmed": true,
              "duplicate": false,
              "errorCode": null
            }
            """);
        var publisher = CreatePublisher(workspace.Path, runner);

        var result = await publisher.PublishAsync(CreatePayload(maxNotionalUsdc: 12.345678m));

        Assert.True(result.Confirmed);
        Assert.Equal("0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", result.TransactionHash);
        Assert.NotNull(runner.LastRequest);
        Assert.Equal("localhost", runner.LastRequest.NetworkName);
        using var document = JsonDocument.Parse(runner.LastRequest.RequestJson);
        var root = document.RootElement;
        Assert.Equal("0x1111111111111111111111111111111111111111", root.GetProperty("signalRegistry").GetString());
        Assert.Equal("42", root.GetProperty("expectedEdgeBps").GetString());
        Assert.Equal("12345678", root.GetProperty("maxNotionalUsdcAtomic").GetString());
    }

    [Fact]
    public async Task PublishAsync_WhenHardhatReportsDuplicate_ThrowsDuplicateException()
    {
        using var workspace = ArcContractsWorkspaceFixture.Create();
        var runner = new CapturingRunner(
            """
            {
              "transactionHash": null,
              "confirmed": false,
              "duplicate": true,
              "errorCode": "DuplicateSignal"
            }
            """);
        var publisher = CreatePublisher(workspace.Path, runner);

        await Assert.ThrowsAsync<ArcSignalRegistryDuplicateException>(
            () => publisher.PublishAsync(CreatePayload()));
    }

    [Fact]
    public async Task PublishAsync_RejectsNonIntegerExpectedEdgeBpsBeforeProcessCall()
    {
        using var workspace = ArcContractsWorkspaceFixture.Create();
        var runner = new CapturingRunner(
            """{"transactionHash":"0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","confirmed":true,"duplicate":false}""");
        var publisher = CreatePublisher(workspace.Path, runner);

        await Assert.ThrowsAsync<ArgumentException>(
            () => publisher.PublishAsync(CreatePayload(expectedEdgeBps: 42.5m)));
        Assert.Null(runner.LastRequest);
    }

    private static HardhatArcSignalRegistryPublisher CreatePublisher(
        string workspacePath,
        IArcHardhatSignalPublisherProcessRunner runner)
        => new(
            new StaticOptionsMonitor<ArcSettlementOptions>(CreateOptions(workspacePath)),
            runner);

    private static ArcSettlementOptions CreateOptions(string workspacePath)
        => new()
        {
            Enabled = true,
            ChainId = 31337,
            RpcUrl = "http://127.0.0.1:8545",
            Contracts = new ArcSettlementContractsOptions
            {
                SignalRegistry = "0x1111111111111111111111111111111111111111",
                StrategyAccess = "0x2222222222222222222222222222222222222222",
                PerformanceLedger = "0x3333333333333333333333333333333333333333",
                RevenueSettlement = "0x4444444444444444444444444444444444444444"
            },
            EvmPublisher = new ArcSettlementEvmPublisherOptions
            {
                ContractsWorkspacePath = workspacePath,
                NetworkName = "localhost",
                RequestTimeoutSeconds = 30
            }
        };

    private static ArcSignalRegistryPublishPayload CreatePayload(
        decimal expectedEdgeBps = 42m,
        decimal maxNotionalUsdc = 100m)
        => new(
            Hash("signal-1"),
            "0x9999999999999999999999999999999999999999",
            "polymarket",
            "repricing_lag_arbitrage",
            Hash("reasoning-1"),
            Hash("risk-1"),
            expectedEdgeBps,
            maxNotionalUsdc,
            new DateTimeOffset(2026, 5, 12, 10, 30, 0, TimeSpan.Zero));

    private static string Hash(string value)
        => $"0x{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant()}";

    private sealed class CapturingRunner(string resultJson) : IArcHardhatSignalPublisherProcessRunner
    {
        public ArcHardhatSignalPublisherProcessRequest? LastRequest { get; private set; }

        public Task<ArcHardhatSignalPublisherProcessResult> RunAsync(
            ArcHardhatSignalPublisherProcessRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(new ArcHardhatSignalPublisherProcessResult(resultJson, string.Empty, string.Empty));
        }
    }

    private sealed class StaticOptionsMonitor<T>(T currentValue) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = currentValue;

        public T Get(string? name)
            => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener)
            => null;
    }

    private sealed class ArcContractsWorkspaceFixture : IDisposable
    {
        private ArcContractsWorkspaceFixture(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static ArcContractsWorkspaceFixture Create()
        {
            var path = System.IO.Path.Combine(
                AppContext.BaseDirectory,
                "arc-contracts-workspace-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(System.IO.Path.Combine(path, "scripts"));
            File.WriteAllText(System.IO.Path.Combine(path, "package.json"), "{}");
            File.WriteAllText(System.IO.Path.Combine(path, "scripts", "publish-signal.cjs"), string.Empty);
            return new ArcContractsWorkspaceFixture(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
