using Autotrade.Application.Configuration;
using Microsoft.Extensions.Options;

namespace Autotrade.SelfImprove.Tests.Configuration;

public sealed class ConfigurationMutationServiceTests
{
    [Fact]
    public async Task MutateAsync_DryRun_DoesNotWriteOverride()
    {
        var root = CreateTempConfig();
        var service = CreateService(root);

        var result = await service.MutateAsync(new ConfigurationMutationRequest(
            new[]
            {
                new ConfigurationMutationPatch(
                    "Strategies:LiquidityPulse:MaxSpread",
                    "0.04",
                    "test")
            },
            DryRun: true,
            Actor: "test",
            Source: "unit"));

        Assert.True(result.Success);
        Assert.True(result.DryRun);
        Assert.DoesNotContain("0.04", await File.ReadAllTextAsync(Path.Combine(root, "appsettings.local.json")));
    }

    [Fact]
    public async Task MutateAsync_Apply_StampsStrategyConfigVersion()
    {
        var root = CreateTempConfig();
        var service = CreateService(root);

        var result = await service.MutateAsync(new ConfigurationMutationRequest(
            new[]
            {
                new ConfigurationMutationPatch(
                    "Strategies:LiquidityPulse:MaxSpread",
                    "0.04",
                    "test")
            },
            DryRun: false,
            Actor: "test",
            Source: "unit"));

        var overrideJson = await File.ReadAllTextAsync(Path.Combine(root, "appsettings.local.json"));
        Assert.True(result.Success);
        Assert.Contains("\"MaxSpread\": 0.04", overrideJson);
        Assert.Contains("\"ConfigVersion\": \"si-", overrideJson);
    }

    [Fact]
    public async Task MutateAsync_RejectsExecutionMode()
    {
        var root = CreateTempConfig();
        var service = CreateService(root);

        var result = await service.MutateAsync(new ConfigurationMutationRequest(
            new[]
            {
                new ConfigurationMutationPatch("Execution:Mode", "\"Live\"", "unsafe")
            },
            DryRun: true,
            Actor: "test",
            Source: "unit"));

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("forbidden", StringComparison.OrdinalIgnoreCase)
            || error.Contains("whitelist", StringComparison.OrdinalIgnoreCase));
    }

    private static JsonConfigurationMutationService CreateService(string root)
    {
        return new JsonConfigurationMutationService(Options.Create(new ConfigurationMutationOptions
        {
            BasePath = Path.Combine(root, "appsettings.json"),
            OverridePath = Path.Combine(root, "appsettings.local.json"),
            AllowedPathPrefixes = new[] { "Strategies:" }
        }));
    }

    private static string CreateTempConfig()
    {
        var root = Path.Combine(Path.GetTempPath(), "autotrade-selfimprove-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "appsettings.json"), """
{
  "Strategies": {
    "LiquidityPulse": {
      "ConfigVersion": "v1",
      "MaxSpread": 0.05
    }
  },
  "Execution": {
    "Mode": "Paper"
  }
}
""");
        File.WriteAllText(Path.Combine(root, "appsettings.local.json"), "{}");
        return root;
    }
}
