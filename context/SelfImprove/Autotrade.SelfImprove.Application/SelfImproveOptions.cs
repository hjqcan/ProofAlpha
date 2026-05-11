namespace Autotrade.SelfImprove.Application;

public sealed class SelfImproveOptions
{
    public const string SectionName = "SelfImprove";

    public bool Enabled { get; set; }

    public bool LiveAutoApplyEnabled { get; set; }

    public string ArtifactRoot { get; set; } = "artifacts/self-improve";

    public SelfImproveLlmOptions Llm { get; set; } = new();

    public SelfImproveCodeGenOptions CodeGen { get; set; } = new();

    public SelfImproveCanaryOptions Canary { get; set; } = new();
}

public sealed class SelfImproveLlmOptions
{
    public string Provider { get; set; } = "OpenAICompatible";

    public string Model { get; set; } = "gpt-4.1-mini";

    public string? BaseUrl { get; set; }

    public string ApiKeyEnvVar { get; set; } = "OPENAI_API_KEY";

    public int TimeoutSeconds { get; set; } = 120;

    public int MaxRetries { get; set; } = 3;
}

public sealed class SelfImproveCodeGenOptions
{
    public bool Enabled { get; set; } = true;

    public string PythonExecutable { get; set; } = "python";

    public string? PythonDllPath { get; set; }

    public string DotnetExecutable { get; set; } = "dotnet";

    public string? WorkerAssemblyPath { get; set; }

    public int WorkerTimeoutSeconds { get; set; } = 5;
}

public sealed class SelfImproveCanaryOptions
{
    public int MaxActiveLiveCanaries { get; set; } = 1;

    public decimal MaxSingleOrderNotional { get; set; } = 5m;

    public decimal MaxCycleNotional { get; set; } = 20m;

    public decimal MaxTotalNotional { get; set; } = 100m;
}
