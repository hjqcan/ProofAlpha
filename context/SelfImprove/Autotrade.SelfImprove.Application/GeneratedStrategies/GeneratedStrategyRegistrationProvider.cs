using System.Text.Json;
using Autotrade.SelfImprove.Application.Python;
using Autotrade.SelfImprove.Domain.Shared.Enums;
using Autotrade.Strategy.Application.Contract.Strategies;
using Autotrade.Strategy.Application.Engine;
using Microsoft.Extensions.DependencyInjection;

namespace Autotrade.SelfImprove.Application.GeneratedStrategies;

public sealed class GeneratedStrategyRegistrationProvider : IStrategyRegistrationProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IServiceScopeFactory _scopeFactory;

    public GeneratedStrategyRegistrationProvider(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
    }

    public IReadOnlyList<StrategyRegistration> GetRegistrations()
    {
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IGeneratedStrategyVersionRepository>();
        var versions = repository.GetRegistrableAsync().ConfigureAwait(false).GetAwaiter().GetResult();

        return versions
            .Where(version => version.Stage is GeneratedStrategyStage.PaperRunning
                or GeneratedStrategyStage.LiveCanary
                or GeneratedStrategyStage.Promoted)
            .Select(ToRegistration)
            .ToList();
    }

    private static StrategyRegistration ToRegistration(Autotrade.SelfImprove.Domain.Entities.GeneratedStrategyVersion version)
    {
        var manifest = BuildManifest(version);

        return new StrategyRegistration(
            manifest.StrategyId,
            manifest.Name,
            typeof(PythonStrategyAdapter),
            $"SelfImprove:GeneratedStrategies:{manifest.StrategyId}",
            (sp, context) => sp.GetRequiredService<IPythonStrategyAdapterFactory>().Create(manifest, context),
            _ => new StrategyOptionsSnapshot(
                manifest.StrategyId,
                Enabled: true,
                manifest.ConfigVersion));
    }

    private static PythonStrategyManifest BuildManifest(Autotrade.SelfImprove.Domain.Entities.GeneratedStrategyVersion version)
    {
        var manifest = JsonSerializer.Deserialize<Autotrade.SelfImprove.Application.Contract.GeneratedStrategies.GeneratedStrategyManifest>(
            version.ManifestJson,
            JsonOptions) ?? throw new InvalidOperationException($"Generated strategy {version.Id} manifest is invalid.");

        var parameters = ExtractParameters(version.ManifestJson);
        var configVersion = string.IsNullOrWhiteSpace(manifest.ConfigVersion)
            ? manifest.Version
            : manifest.ConfigVersion;

        return new PythonStrategyManifest(
            manifest.StrategyId,
            manifest.Name,
            manifest.Version,
            configVersion,
            version.ArtifactRoot,
            version.PackageHash,
            manifest.EntryPoint,
            File.ReadAllText(Path.Combine(version.ArtifactRoot, manifest.ParameterSchemaPath)),
            version.RiskEnvelopeJson,
            parameters);
    }

    private static IReadOnlyDictionary<string, object?> ExtractParameters(string manifestJson)
    {
        using var document = JsonDocument.Parse(manifestJson);
        if (!document.RootElement.TryGetProperty("parameters", out var parameters)
            || parameters.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        }

        return JsonSerializer.Deserialize<Dictionary<string, object?>>(
            parameters.GetRawText(),
            JsonOptions) ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    }
}
