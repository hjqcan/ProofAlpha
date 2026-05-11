namespace Autotrade.Hosting;

public interface IAutotradeModuleInventory
{
    bool Enabled { get; }

    string ConnectionStringName { get; }

    bool EventBusRegistered { get; }

    bool HangfireRegistered { get; }

    bool PersistenceRegistered { get; }

    IReadOnlyList<AutotradeModuleDescriptor> Modules { get; }
}
