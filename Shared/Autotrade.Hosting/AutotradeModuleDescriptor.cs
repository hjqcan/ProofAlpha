namespace Autotrade.Hosting;

public sealed record AutotradeModuleDescriptor(
    string Name,
    string DataContextName,
    bool DataContextRegistered,
    bool ApplicationServicesRegistered,
    bool BackgroundJobsRegistered);
