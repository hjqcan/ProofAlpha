using System.Text.Json;
using Autotrade.SelfImprove.Application;
using Autotrade.SelfImprove.Application.Contract.Proposals;
using Autotrade.SelfImprove.Application.GeneratedStrategies;
using Autotrade.SelfImprove.Domain.Shared.Enums;
using Microsoft.Extensions.Options;

namespace Autotrade.SelfImprove.Tests.GeneratedStrategies;

public sealed class GeneratedStrategyPackageServiceTests
{
    [Fact]
    public async Task ValidatePackageAsync_RunsStaticUnitAndReplayGates()
    {
        var root = CreateArtifactRoot();
        try
        {
            var service = CreateService(root);
            var version = await service.CreatePackageAsync(Guid.NewGuid(), CreateValidSpec());

            var result = await service.ValidatePackageAsync(version);

            Assert.True(result.Passed, string.Join(Environment.NewLine, result.Errors));
            Assert.Equal(
                new[]
                {
                    PromotionGateStage.StaticValidation,
                    PromotionGateStage.UnitTest,
                    PromotionGateStage.Replay
                },
                result.Gates.Select(gate => gate.Stage));
            Assert.All(result.Gates, gate => Assert.True(gate.Passed, string.Join(Environment.NewLine, gate.Errors)));

            using var manifest = JsonDocument.Parse(version.ManifestJson);
            Assert.True(manifest.RootElement.TryGetProperty("parameters", out var parameters));
            Assert.Equal("0.03", parameters.GetProperty("threshold").GetString());
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task ValidatePackageAsync_RejectsRiskEnvelopeAboveConfiguredCanaryCaps()
    {
        var root = CreateArtifactRoot();
        try
        {
            var service = CreateService(root);
            var version = await service.CreatePackageAsync(Guid.NewGuid(), CreateValidSpec(
                riskEnvelopeJson: """
{
  "maxSingleOrderNotional": 99,
  "maxCycleNotional": 99,
  "maxTotalNotional": 99
}
"""));

            var result = await service.ValidatePackageAsync(version);

            Assert.False(result.Passed);
            var gate = Assert.Single(result.Gates);
            Assert.Equal(PromotionGateStage.StaticValidation, gate.Stage);
            Assert.False(gate.Passed);
            Assert.Contains(result.Errors, error => error.Contains("maxSingleOrderNotional", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static GeneratedStrategyPackageService CreateService(string artifactRoot)
    {
        return new GeneratedStrategyPackageService(Options.Create(new SelfImproveOptions
        {
            ArtifactRoot = artifactRoot,
            CodeGen = new SelfImproveCodeGenOptions
            {
                Enabled = true,
                PythonExecutable = "python"
            },
            Canary = new SelfImproveCanaryOptions
            {
                MaxSingleOrderNotional = 5m,
                MaxCycleNotional = 20m,
                MaxTotalNotional = 100m
            }
        }));
    }

    private static GeneratedStrategySpec CreateValidSpec(string? riskEnvelopeJson = null)
    {
        return new GeneratedStrategySpec(
            "generated_liquidity_probe",
            "Generated Liquidity Probe",
            "A deterministic generated strategy used by SelfImprove validation tests.",
            """
def evaluate(request):
    params = request.get("params", {})
    threshold = params.get("threshold", "0.03")
    return {
        "action": "skip",
        "reasonCode": "spread_too_wide",
        "reason": "spread is above threshold " + str(threshold),
        "intents": [],
        "telemetry": {"threshold": threshold},
        "statePatch": {"lastPhase": request.get("phase", "entry")}
    }
""",
            """
{
  "parameters": {
    "threshold": "0.03"
  }
}
""",
            """
{
  "type": "object",
  "properties": {
    "threshold": {
      "type": "string"
    }
  }
}
""",
            """
import unittest
import strategy

class GeneratedStrategyTests(unittest.TestCase):
    def test_evaluate_returns_contract(self):
        result = strategy.evaluate({"phase": "entry", "params": {"threshold": "0.03"}})
        self.assertEqual("skip", result["action"])
        self.assertEqual("spread_too_wide", result["reasonCode"])
        self.assertEqual([], result["intents"])

if __name__ == "__main__":
    unittest.main()
""",
            """
{
  "cases": [
    {
      "name": "entry skip is deterministic",
      "input": {
        "phase": "entry",
        "params": {
          "threshold": "0.03"
        }
      },
      "expected": {
        "action": "skip",
        "reasonCode": "spread_too_wide",
        "intents": []
      }
    }
  ]
}
""",
            riskEnvelopeJson ?? """
{
  "maxSingleOrderNotional": 5,
  "maxCycleNotional": 20,
  "maxTotalNotional": 100
}
""");
    }

    private static string CreateArtifactRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "autotrade-selfimprove-package-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Temporary artifact cleanup is best effort and must not hide assertion failures.
        }
    }
}
