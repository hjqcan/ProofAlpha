using System.Text.Json;
using Autotrade.ArcSettlement.Application.Contract.Configuration;
using Autotrade.ArcSettlement.Application.Performance;
using Autotrade.ArcSettlement.Infra.Evm.Performance;
using Microsoft.Extensions.Options;

namespace Autotrade.ArcSettlement.Tests.Performance;

public sealed class HardhatArcPerformanceLedgerPublisherTests
{
    [Fact]
    public async Task PublishAsync_SerializesPerformanceLedgerRequestAndReturnsTransactionHash()
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

        var result = await publisher.PublishAsync(CreatePayload(
            ArcPerformanceLedgerOutcomeStatus.Rejected,
            realizedPnlBps: -15m,
            slippageBps: 4m));

        Assert.True(result.Confirmed);
        Assert.Equal("0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", result.TransactionHash);
        Assert.NotNull(runner.LastRequest);
        Assert.Equal("localhost", runner.LastRequest.NetworkName);
        Assert.Equal("http://127.0.0.1:8545", runner.LastRequest.EnvironmentVariables["LOCAL_RPC_URL"]);
        using var document = JsonDocument.Parse(runner.LastRequest.RequestJson);
        var root = document.RootElement;
        Assert.Equal(31337, root.GetProperty("chainId").GetInt64());
        Assert.Equal("0x3333333333333333333333333333333333333333", root.GetProperty("performanceLedger").GetString());
        Assert.Equal(Hash("signal-1"), root.GetProperty("signalId").GetString());
        Assert.Equal(2, root.GetProperty("status").GetInt32());
        Assert.Equal("-15", root.GetProperty("realizedPnlBps").GetString());
        Assert.Equal("4", root.GetProperty("slippageBps").GetString());
        Assert.Equal(Hash("outcome-1"), root.GetProperty("outcomeHash").GetString());
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
              "errorCode": "TerminalOutcomeAlreadyRecorded"
            }
            """);
        var publisher = CreatePublisher(workspace.Path, runner);

        await Assert.ThrowsAsync<ArcPerformanceLedgerDuplicateException>(
            () => publisher.PublishAsync(CreatePayload()));
    }

    [Fact]
    public async Task PublishAsync_WhenHardhatOmitsTransactionHash_ThrowsInvalidOperationException()
    {
        using var workspace = ArcContractsWorkspaceFixture.Create();
        var runner = new CapturingRunner(
            """
            {
              "transactionHash": null,
              "confirmed": false,
              "duplicate": false,
              "errorCode": null
            }
            """);
        var publisher = CreatePublisher(workspace.Path, runner);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => publisher.PublishAsync(CreatePayload()));
    }

    [Fact]
    public async Task PublishAsync_RejectsNonIntegerBpsBeforeProcessCall()
    {
        using var workspace = ArcContractsWorkspaceFixture.Create();
        var runner = new CapturingRunner(
            """{"transactionHash":"0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","confirmed":true,"duplicate":false}""");
        var publisher = CreatePublisher(workspace.Path, runner);

        await Assert.ThrowsAsync<ArgumentException>(
            () => publisher.PublishAsync(CreatePayload(realizedPnlBps: 12.5m)));
        Assert.Null(runner.LastRequest);
    }

    private static HardhatArcPerformanceLedgerPublisher CreatePublisher(
        string workspacePath,
        IArcHardhatPerformanceLedgerProcessRunner runner)
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

    private static ArcPerformanceLedgerPublishPayload CreatePayload(
        ArcPerformanceLedgerOutcomeStatus status = ArcPerformanceLedgerOutcomeStatus.Executed,
        decimal? realizedPnlBps = 12m,
        decimal? slippageBps = 1m)
        => new(
            Hash("signal-1"),
            status,
            realizedPnlBps,
            slippageBps,
            Hash("outcome-1"));

    private static string Hash(string value)
        => $"0x{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant()}";

    private sealed class CapturingRunner(string resultJson) : IArcHardhatPerformanceLedgerProcessRunner
    {
        public ArcHardhatPerformanceLedgerProcessRequest? LastRequest { get; private set; }

        public Task<ArcHardhatPerformanceLedgerProcessResult> RunAsync(
            ArcHardhatPerformanceLedgerProcessRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(new ArcHardhatPerformanceLedgerProcessResult(resultJson, string.Empty, string.Empty));
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
                "arc-contracts-performance-workspace-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(System.IO.Path.Combine(path, "scripts"));
            File.WriteAllText(System.IO.Path.Combine(path, "package.json"), "{}");
            File.WriteAllText(System.IO.Path.Combine(path, "scripts", "record-outcome.cjs"), string.Empty);
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
