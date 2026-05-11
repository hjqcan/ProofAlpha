using System.Xml.Linq;
using Autotrade.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using NetDevPack.Messaging;

namespace Autotrade.Api.Tests;

public sealed class ArchitectureBoundaryTests
{
    private static readonly string[] HostingOwnedBoundedContextInfraReferences =
    [
        "context/MarketData/Autotrade.MarketData.Infra.BackgroundJobs/Autotrade.MarketData.Infra.BackgroundJobs.csproj",
        "context/MarketData/Autotrade.MarketData.Infra.CrossCutting.IoC/Autotrade.MarketData.Infra.CrossCutting.IoC.csproj",
        "context/MarketData/Autotrade.MarketData.Infra.Data/Autotrade.MarketData.Infra.Data.csproj",
        "context/OpportunityDiscovery/Autotrade.OpportunityDiscovery.Infra.BackgroundJobs/Autotrade.OpportunityDiscovery.Infra.BackgroundJobs.csproj",
        "context/OpportunityDiscovery/Autotrade.OpportunityDiscovery.Infra.CrossCutting.IoC/Autotrade.OpportunityDiscovery.Infra.CrossCutting.IoC.csproj",
        "context/OpportunityDiscovery/Autotrade.OpportunityDiscovery.Infra.Data/Autotrade.OpportunityDiscovery.Infra.Data.csproj",
        "context/SelfImprove/Autotrade.SelfImprove.Infra.BackgroundJobs/Autotrade.SelfImprove.Infra.BackgroundJobs.csproj",
        "context/SelfImprove/Autotrade.SelfImprove.Infra.CrossCutting.IoC/Autotrade.SelfImprove.Infra.CrossCutting.IoC.csproj",
        "context/SelfImprove/Autotrade.SelfImprove.Infra.Data/Autotrade.SelfImprove.Infra.Data.csproj",
        "context/Strategy/Autotrade.Strategy.Infra.BackgroundJobs/Autotrade.Strategy.Infra.BackgroundJobs.csproj",
        "context/Strategy/Autotrade.Strategy.Infra.CrossCutting.IoC/Autotrade.Strategy.Infra.CrossCutting.IoC.csproj",
        "context/Strategy/Autotrade.Strategy.Infra.Data/Autotrade.Strategy.Infra.Data.csproj",
        "context/Trading/Autotrade.Trading.Infra.BackgroundJobs/Autotrade.Trading.Infra.BackgroundJobs.csproj",
        "context/Trading/Autotrade.Trading.Infra.CrossCutting.IoC/Autotrade.Trading.Infra.CrossCutting.IoC.csproj",
        "context/Trading/Autotrade.Trading.Infra.Data/Autotrade.Trading.Infra.Data.csproj"
    ];

    private static readonly string[] ForbiddenInterfaceNamespaces =
    [
        "Autotrade.MarketData.Infra.",
        "Autotrade.OpportunityDiscovery.Infra.",
        "Autotrade.SelfImprove.Infra.",
        "Autotrade.Strategy.Infra.",
        "Autotrade.Trading.Infra."
    ];

    [Fact]
    public void ApiAndCliUseSharedHostingComposition()
    {
        var apiReferences = GetProjectReferences("interfaces/Autotrade.Api/Autotrade.Api.csproj");
        var cliReferences = GetProjectReferences("interfaces/Autotrade.Cli/Autotrade.Cli.csproj");

        Assert.Contains("Shared/Autotrade.Hosting/Autotrade.Hosting.csproj", apiReferences);
        Assert.Contains("Shared/Autotrade.Hosting/Autotrade.Hosting.csproj", cliReferences);
    }

    [Fact]
    public void ApiHasNoDirectBoundedContextInfraReferences()
    {
        var directInfraReferences = GetBoundedContextInfraProjectReferences(
            "interfaces/Autotrade.Api/Autotrade.Api.csproj");

        Assert.Empty(directInfraReferences);
    }

    [Fact]
    public void CliHasNoDirectBoundedContextInfraReferences()
    {
        var directInfraReferences = GetBoundedContextInfraProjectReferences(
            "interfaces/Autotrade.Cli/Autotrade.Cli.csproj");

        Assert.Empty(directInfraReferences);
    }

    [Theory]
    [InlineData("interfaces/Autotrade.Api")]
    [InlineData("interfaces/Autotrade.Cli")]
    public void InterfaceSourceDoesNotReachIntoBoundedContextInfrastructure(string relativeSourceDirectory)
    {
        var root = FindRepositoryRoot();
        var sourceDirectory = Path.Combine(root, relativeSourceDirectory.Replace('/', Path.DirectorySeparatorChar));
        var violations = Directory
            .EnumerateFiles(sourceDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsGeneratedOrBuildOutput(path))
            .SelectMany(path => FindForbiddenNamespaceUsages(root, path))
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void SharedHostingOwnsAllBoundedContextModuleInfrastructureReferences()
    {
        var hostingReferences = GetBoundedContextInfraProjectReferences(
            "Shared/Autotrade.Hosting/Autotrade.Hosting.csproj");

        Assert.Equal(HostingOwnedBoundedContextInfraReferences, hostingReferences);
    }

    [Fact]
    public void SharedHostingRegistersLocalDomainDispatcherWithoutEventBus()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:AutotradeDatabase"] =
                    "Host=localhost;Database=autotrade_test;Username=autotrade;Password=autotrade"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAutotradeModules(
            configuration,
            new TestHostEnvironment(),
            options =>
            {
                options.RegisterEventBus = false;
                options.RegisterHangfireCore = false;
                options.RegisterDataContexts = true;
                options.RegisterApplicationServices = false;
                options.RegisterBackgroundJobServices = false;
            });

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IDomainEventDispatcher>());
    }

    [Fact]
    public void ControlRoomQueryServiceDoesNotHardCodeConcreteStrategyOptions()
    {
        var root = FindRepositoryRoot();
        var sourcePath = Path.Combine(root, "interfaces", "Autotrade.Api", "ControlRoom", "ControlRoomQueryService.cs");
        var source = File.ReadAllText(sourcePath);
        var forbiddenStrategyOptions =
            new[]
            {
                "DualLegArbitrageOptions",
                "EndgameSweepOptions",
                "LiquidityPulseOptions",
                "LiquidityMakerOptions",
                "MicroVolatilityScalperOptions",
                "RepricingLagArbitrageOptions",
                "LlmOpportunityOptions"
            };

        foreach (var forbidden in forbiddenStrategyOptions)
        {
            Assert.DoesNotContain(forbidden, source, StringComparison.Ordinal);
        }

        Assert.Contains("IStrategyControlRoomReadModelProvider", source, StringComparison.Ordinal);
    }

    private static IEnumerable<string> FindForbiddenNamespaceUsages(string root, string sourcePath)
    {
        var source = File.ReadAllText(sourcePath);
        foreach (var forbiddenNamespace in ForbiddenInterfaceNamespaces)
        {
            if (source.Contains(forbiddenNamespace, StringComparison.Ordinal))
            {
                yield return $"{Path.GetRelativePath(root, sourcePath).Replace('\\', '/')} uses {forbiddenNamespace}";
            }
        }
    }

    private static bool IsGeneratedOrBuildOutput(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase);
    }

    private static string[] GetBoundedContextInfraProjectReferences(string relativeProjectPath)
    {
        return GetProjectReferences(relativeProjectPath)
            .Where(IsBoundedContextInfraProjectReference)
            .OrderBy(reference => reference, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] GetProjectReferences(string relativeProjectPath)
    {
        var root = FindRepositoryRoot();
        var projectPath = Path.Combine(root, relativeProjectPath.Replace('/', Path.DirectorySeparatorChar));
        var projectDirectory = Path.GetDirectoryName(projectPath)
            ?? throw new InvalidOperationException($"Project path has no directory: {projectPath}");

        return XDocument
            .Load(projectPath)
            .Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => Path.GetFullPath(Path.Combine(projectDirectory, include!)))
            .Select(fullPath => Path.GetRelativePath(root, fullPath).Replace('\\', '/'))
            .OrderBy(reference => reference, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsBoundedContextInfraProjectReference(string reference)
    {
        return reference.StartsWith("context/", StringComparison.OrdinalIgnoreCase)
            && reference.Contains(".Infra.", StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Autotrade.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate Autotrade.sln from the test output directory.");
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "Autotrade.Api.Tests";

        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
