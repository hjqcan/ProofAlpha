namespace Autotrade.Hosting;

public sealed class AutotradeModuleRegistrationOptions
{
    public string ConnectionStringName { get; set; } = "AutotradeDatabase";

    public bool RegisterEventBus { get; set; } = true;

    public bool EnableEventBusDashboard { get; set; }

    public bool RegisterHangfireCore { get; set; }

    public bool RegisterDataContexts { get; set; } = true;

    public bool RegisterApplicationServices { get; set; } = true;

    public bool RegisterBackgroundJobServices { get; set; } = true;
}
