namespace Autotrade.Hosting;

public sealed class AutotradeModuleInventory : IAutotradeModuleInventory
{
    public static AutotradeModuleInventory Disabled { get; } = new(
        enabled: false,
        connectionStringName: string.Empty,
        eventBusRegistered: false,
        hangfireRegistered: false,
        persistenceRegistered: false,
        modules: Array.Empty<AutotradeModuleDescriptor>());

    public AutotradeModuleInventory(
        bool enabled,
        string connectionStringName,
        bool eventBusRegistered,
        bool hangfireRegistered,
        bool persistenceRegistered,
        IEnumerable<AutotradeModuleDescriptor> modules)
    {
        Enabled = enabled;
        ConnectionStringName = connectionStringName;
        EventBusRegistered = eventBusRegistered;
        HangfireRegistered = hangfireRegistered;
        PersistenceRegistered = persistenceRegistered;
        Modules = modules.ToArray();
    }

    public bool Enabled { get; }

    public string ConnectionStringName { get; }

    public bool EventBusRegistered { get; }

    public bool HangfireRegistered { get; }

    public bool PersistenceRegistered { get; }

    public IReadOnlyList<AutotradeModuleDescriptor> Modules { get; }
}
