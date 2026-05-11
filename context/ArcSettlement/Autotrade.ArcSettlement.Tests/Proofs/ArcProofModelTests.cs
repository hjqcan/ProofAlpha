using Autotrade.ArcSettlement.Application.Contract.Proofs;
using Autotrade.ArcSettlement.Application.Proofs;

namespace Autotrade.ArcSettlement.Tests.Proofs;

public sealed class ArcProofModelTests
{
    private readonly ArcProofHashService _hashService = new();

    [Fact]
    public void HashSignal_IsDeterministicAndChangesWhenMaterialFieldsChange()
    {
        var signal = CreateSignal();
        var reordered = signal with { EvidenceIds = ["ev-2", "ev-1"] };
        var changedMarket = signal with { MarketId = "market-2" };
        var changedEdge = signal with { ExpectedEdgeBps = signal.ExpectedEdgeBps + 1m };

        var hash = _hashService.HashSignal(signal);

        Assert.Equal(hash, _hashService.HashSignal(signal));
        Assert.Equal(hash, _hashService.HashSignal(reordered));
        Assert.NotEqual(hash, _hashService.HashSignal(changedMarket));
        Assert.NotEqual(hash, _hashService.HashSignal(changedEdge));
    }

    [Fact]
    public void HashOutcome_IsDeterministicAndChangesWhenOutcomeChanges()
    {
        var outcome = CreateOutcome(ArcSignalOutcomeStatus.Executed, realizedPnlBps: 12m);
        var reordered = outcome with { ClientOrderIds = ["order-2", "order-1"] };
        var changedOutcome = outcome with { Status = ArcSignalOutcomeStatus.Failed, RealizedPnlBps = null };

        var hash = _hashService.HashOutcome(outcome);

        Assert.Equal(hash, _hashService.HashOutcome(outcome));
        Assert.Equal(hash, _hashService.HashOutcome(reordered));
        Assert.NotEqual(hash, _hashService.HashOutcome(changedOutcome));
    }

    [Fact]
    public void RedactionGuard_BlocksLikelySecrets()
    {
        var guard = new ArcProofRedactionGuard();
        var document = new
        {
            documentVersion = "test.v1",
            privateKey = "0x0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            apiSecret = "secret-value"
        };

        var ex = Assert.Throws<ArcProofRedactionException>(() => guard.ValidatePublicProof(document));

        Assert.Contains("secret", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RedactionGuard_AllowsExplicitlyRedactedEvidence()
    {
        var guard = new ArcProofRedactionGuard();
        var document = new
        {
            documentVersion = "test.v1",
            clobSignature = ArcProofRedactionGuard.RedactedText,
            orderSignature = "redacted in signed-order evidence",
            mnemonic = "redacted",
            signalHash = "0x0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
        };

        guard.ValidatePublicProof(document);
    }

    [Fact]
    public void Metrics_IncludeExpiredRejectedAndFailedOutcomesInReputationDenominator()
    {
        var calculator = new ArcUtilityMetricsCalculator();
        var signals = new[]
        {
            CreateSignal("signal-1"),
            CreateSignal("signal-2"),
            CreateSignal("signal-3")
        };
        var outcomes = new[]
        {
            CreateOutcome(ArcSignalOutcomeStatus.Executed, signalId: "signal-1", realizedPnlBps: 10m),
            CreateOutcome(ArcSignalOutcomeStatus.Expired, signalId: "signal-2", realizedPnlBps: null),
            CreateOutcome(ArcSignalOutcomeStatus.Rejected, signalId: "signal-3", realizedPnlBps: null)
        };

        var metrics = calculator.Calculate(
            "arc-utility-metrics.v1",
            "proofalpha-agent",
            "repricing_lag_arbitrage",
            signals,
            outcomes,
            new DateTimeOffset(2026, 5, 11, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal(3, metrics.PublishedSignalsCount);
        Assert.Equal(1, metrics.ExecutedSignalsCount);
        Assert.Equal(1, metrics.ExpiredSignalsCount);
        Assert.Equal(1, metrics.RejectedSignalsCount);
        Assert.Equal(0, metrics.PendingSignalsCount);
        Assert.Equal(3, metrics.ReputationDenominator);
        Assert.Equal(1m, metrics.ExecutedPaperWinRate);
    }

    [Fact]
    public void Metrics_DeriveExpiredSignalsFromValidUntilWhenTerminalOutcomeIsMissing()
    {
        var calculator = new ArcUtilityMetricsCalculator();
        var signals = new[]
        {
            CreateSignal("expired-signal"),
            CreateSignal("pending-signal") with { ValidUntilUtc = new DateTimeOffset(2026, 5, 11, 12, 30, 0, TimeSpan.Zero) }
        };

        var metrics = calculator.Calculate(
            "arc-utility-metrics.v1",
            "proofalpha-agent",
            "repricing_lag_arbitrage",
            signals,
            [],
            new DateTimeOffset(2026, 5, 11, 12, 20, 0, TimeSpan.Zero));

        Assert.Equal(2, metrics.PublishedSignalsCount);
        Assert.Equal(1, metrics.ExpiredSignalsCount);
        Assert.Equal(1, metrics.PendingSignalsCount);
        Assert.Equal(1, metrics.ReputationDenominator);
        Assert.Equal(1, metrics.TerminalOutcomeCount);
    }

    [Fact]
    public void Export_WritesFailedOrRejectedOutcomeProofArtifacts()
    {
        var exportDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "arc-proof-export-tests",
            Guid.NewGuid().ToString("N"));
        var exportService = new ArcProofExportService(_hashService, new ArcProofRedactionGuard());
        var signal = CreateSignal();
        var outcome = CreateOutcome(ArcSignalOutcomeStatus.Rejected, realizedPnlBps: null);
        var metrics = new ArcUtilityMetricsCalculator().Calculate(
            "arc-utility-metrics.v1",
            signal.AgentId,
            signal.StrategyId,
            [signal],
            [outcome],
            new DateTimeOffset(2026, 5, 11, 12, 5, 0, TimeSpan.Zero));

        try
        {
            var result = exportService.Export(
                exportDirectory,
                signal,
                outcome,
                metrics,
                new DateTimeOffset(2026, 5, 11, 12, 6, 0, TimeSpan.Zero));

            Assert.True(File.Exists(result.SignalProofPath));
            Assert.True(File.Exists(result.OutcomeProofPath));
            Assert.True(File.Exists(result.UtilityMetricsPath));
            Assert.True(File.Exists(result.HashManifestPath));
            Assert.Contains("\"status\":\"rejected\"", File.ReadAllText(result.OutcomeProofPath), StringComparison.Ordinal);
            Assert.Contains("\"reputationDenominator\":1", File.ReadAllText(result.UtilityMetricsPath), StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(exportDirectory))
            {
                Directory.Delete(exportDirectory, recursive: true);
            }
        }
    }

    private static ArcStrategySignalProofDocument CreateSignal(string sourceId = "signal-1")
        => new(
            "arc-strategy-signal-proof.v1",
            "proofalpha-agent",
            ArcProofSourceKind.Replay,
            sourceId,
            "repricing_lag_arbitrage",
            "market-1",
            "polymarket",
            new DateTimeOffset(2026, 5, 11, 12, 0, 0, TimeSpan.Zero),
            "config-v1",
            ["ev-1", "ev-2"],
            "opportunity-hash",
            "reasoning-hash",
            "risk-envelope-hash",
            42m,
            100m,
            new DateTimeOffset(2026, 5, 11, 12, 15, 0, TimeSpan.Zero));

    private static ArcStrategyOutcomeProofDocument CreateOutcome(
        ArcSignalOutcomeStatus status,
        string signalId = "signal-1",
        decimal? realizedPnlBps = 12m)
        => new(
            "arc-strategy-outcome-proof.v1",
            signalId,
            "execution-1",
            "run-session-1",
            ["order-1", "order-2"],
            ["event-1"],
            status == ArcSignalOutcomeStatus.Executed ? ["trade-1"] : [],
            ArcProofExecutionMode.Paper,
            status,
            realizedPnlBps,
            status == ArcSignalOutcomeStatus.Executed ? 2m : null,
            status == ArcSignalOutcomeStatus.Executed ? 1m : null,
            status == ArcSignalOutcomeStatus.Rejected ? "risk_limit" : null,
            new DateTimeOffset(2026, 5, 11, 12, 3, 0, TimeSpan.Zero));
}
