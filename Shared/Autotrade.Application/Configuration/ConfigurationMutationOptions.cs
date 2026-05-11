namespace Autotrade.Application.Configuration;

public sealed class ConfigurationMutationOptions
{
    public const string SectionName = "ConfigurationMutation";

    public string BasePath { get; set; } = "appsettings.json";

    public string OverridePath { get; set; } = "appsettings.local.json";

    public IReadOnlyList<string> AllowedPathPrefixes { get; set; } = new[] { "Strategies:" };
}
