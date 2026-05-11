using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Autotrade.Hosting;

public interface IAutotradeDatabaseDiagnostics
{
    Task<IReadOnlyList<AutotradeDatabaseConnectionDiagnostic>> CheckConnectionsAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AutotradeDatabaseMigrationDiagnostic>> CheckMigrationsAsync(
        CancellationToken cancellationToken = default);
}

public sealed record AutotradeDatabaseConnectionDiagnostic(
    string Name,
    bool CanConnect,
    Exception? Failure);

public sealed record AutotradeDatabaseMigrationDiagnostic(
    string Name,
    int PendingMigrationCount,
    Exception? Failure);

public sealed class AutotradeDatabaseDiagnostics(IServiceScopeFactory scopeFactory) : IAutotradeDatabaseDiagnostics
{
    public async Task<IReadOnlyList<AutotradeDatabaseConnectionDiagnostic>> CheckConnectionsAsync(
        CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var results = new List<AutotradeDatabaseConnectionDiagnostic>();

        foreach (var definition in AutotradeDatabaseContextCatalog.Definitions)
        {
            DbContext? context;
            try
            {
                context = ResolveContext(scope, definition);
                if (context is null)
                {
                    continue;
                }
            }
            catch (Exception ex)
            {
                results.Add(new AutotradeDatabaseConnectionDiagnostic(definition.Name, false, ex));
                continue;
            }

            try
            {
                var canConnect = await context.Database
                    .CanConnectAsync(cancellationToken)
                    .ConfigureAwait(false);
                results.Add(new AutotradeDatabaseConnectionDiagnostic(definition.Name, canConnect, null));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                results.Add(new AutotradeDatabaseConnectionDiagnostic(definition.Name, false, ex));
            }
        }

        return results;
    }

    public async Task<IReadOnlyList<AutotradeDatabaseMigrationDiagnostic>> CheckMigrationsAsync(
        CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var results = new List<AutotradeDatabaseMigrationDiagnostic>();

        foreach (var definition in AutotradeDatabaseContextCatalog.Definitions)
        {
            DbContext? context;
            try
            {
                context = ResolveContext(scope, definition);
                if (context is null)
                {
                    continue;
                }
            }
            catch (Exception ex)
            {
                results.Add(new AutotradeDatabaseMigrationDiagnostic(definition.Name, 0, ex));
                continue;
            }

            try
            {
                var pendingMigrations = await context.Database
                    .GetPendingMigrationsAsync(cancellationToken)
                    .ConfigureAwait(false);
                results.Add(new AutotradeDatabaseMigrationDiagnostic(
                    definition.Name,
                    pendingMigrations.Count(),
                    null));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                results.Add(new AutotradeDatabaseMigrationDiagnostic(definition.Name, 0, ex));
            }
        }

        return results;
    }

    private static DbContext? ResolveContext(IServiceScope scope, AutotradeDatabaseContextDefinition definition)
        => scope.ServiceProvider.GetService(definition.ContextType) as DbContext;
}
