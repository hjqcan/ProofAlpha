namespace Autotrade.Application.Configuration;

public sealed record ConfigurationMutationPatch(
    string Path,
    string ValueJson,
    string Reason);

public sealed record ConfigurationMutationRequest(
    IReadOnlyList<ConfigurationMutationPatch> Patches,
    bool DryRun,
    string Actor,
    string Source);

public sealed record ConfigurationMutationResult(
    bool Success,
    bool DryRun,
    string ConfigVersion,
    string DiffJson,
    string RollbackJson,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

public interface IConfigurationMutationService
{
    Task<ConfigurationMutationResult> MutateAsync(
        ConfigurationMutationRequest request,
        CancellationToken cancellationToken = default);
}
