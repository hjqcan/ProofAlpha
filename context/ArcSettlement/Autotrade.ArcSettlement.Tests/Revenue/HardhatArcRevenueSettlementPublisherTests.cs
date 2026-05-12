using System.Text.Json;
using Autotrade.ArcSettlement.Application.Contract.Configuration;
using Autotrade.ArcSettlement.Application.Revenue;
using Autotrade.ArcSettlement.Infra.Evm.Revenue;
using Microsoft.Extensions.Options;

namespace Autotrade.ArcSettlement.Tests.Revenue;

public sealed class HardhatArcRevenueSettlementPublisherTests
{
    [Fact]
    public async Task PublishAsync_SerializesRevenueSettlementRequestAndReturnsTransactionHash()
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

        var result = await publisher.PublishAsync(CreatePayload());

        Assert.True(result.Confirmed);
        Assert.Equal("0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", result.TransactionHash);
        Assert.NotNull(runner.LastRequest);
        Assert.Equal("localhost", runner.LastRequest.NetworkName);
        Assert.Equal("http://127.0.0.1:8545", runner.LastRequest.EnvironmentVariables["LOCAL_RPC_URL"]);
        using var document = JsonDocument.Parse(runner.LastRequest.RequestJson);
        var root = document.RootElement;
        Assert.Equal(31337, root.GetProperty("chainId").GetInt64());
        Assert.Equal("0x4444444444444444444444444444444444444444", root.GetProperty("revenueSettlement").GetString());
        Assert.Equal(Hash("settlement-1"), root.GetProperty("settlementId").GetString());
        Assert.Equal(Hash("signal-1"), root.GetProperty("signalId").GetString());
        Assert.Equal("0x0000000000000000000000000000000000000001", root.GetProperty("tokenAddress").GetString());
        Assert.Equal("10000000", root.GetProperty("grossAmountMicroUsdc").GetString());
        Assert.Equal(3, root.GetProperty("recipients").GetArrayLength());
        Assert.Equal(7000, root.GetProperty("shareBps")[0].GetInt32());
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
              "errorCode": "DuplicateSettlement"
            }
            """);
        var publisher = CreatePublisher(workspace.Path, runner);

        await Assert.ThrowsAsync<ArcRevenueSettlementDuplicateException>(
            () => publisher.PublishAsync(CreatePayload()));
    }

    [Fact]
    public async Task PublishAsync_RejectsInvalidShareBpsBeforeProcessCall()
    {
        using var workspace = ArcContractsWorkspaceFixture.Create();
        var runner = new CapturingRunner(
            """{"transactionHash":"0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","confirmed":true,"duplicate":false}""");
        var publisher = CreatePublisher(workspace.Path, runner);

        await Assert.ThrowsAsync<ArgumentException>(
            () => publisher.PublishAsync(CreatePayload(shareBps: [9000, 500])));
        Assert.Null(runner.LastRequest);
    }

    private static HardhatArcRevenueSettlementPublisher CreatePublisher(
        string workspacePath,
        IArcHardhatRevenueSettlementProcessRunner runner)
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

    private static ArcRevenueSettlementPublishPayload CreatePayload(
        IReadOnlyList<int>? shareBps = null)
        => new(
            Hash("settlement-1"),
            Hash("signal-1"),
            "0x0000000000000000000000000000000000000001",
            "10000000",
            [
                "0x1000000000000000000000000000000000000001",
                "0x2000000000000000000000000000000000000002",
                "0x3000000000000000000000000000000000000003"
            ],
            shareBps ?? [7000, 2000, 1000]);

    private static string Hash(string value)
        => $"0x{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant()}";

    private sealed class CapturingRunner(string resultJson) : IArcHardhatRevenueSettlementProcessRunner
    {
        public ArcHardhatRevenueSettlementProcessRequest? LastRequest { get; private set; }

        public Task<ArcHardhatRevenueSettlementProcessResult> RunAsync(
            ArcHardhatRevenueSettlementProcessRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(new ArcHardhatRevenueSettlementProcessResult(resultJson, string.Empty, string.Empty));
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
                "arc-contracts-revenue-workspace-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(System.IO.Path.Combine(path, "scripts"));
            File.WriteAllText(System.IO.Path.Combine(path, "package.json"), "{}");
            File.WriteAllText(System.IO.Path.Combine(path, "scripts", "record-settlement.cjs"), string.Empty);
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
