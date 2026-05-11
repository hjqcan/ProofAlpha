using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Autotrade.Hosting;

/// <summary>
/// Applies EF Core migrations for all registered bounded-context DbContexts at host startup.
/// </summary>
public sealed class AutotradeDatabaseMigrationHostedService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<AutotradeDatabaseMigrationHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var enabled = configuration.GetValue("Database:AutoMigrate", true);
        if (!enabled)
        {
            logger.LogInformation("Database migrations disabled by Database:AutoMigrate=false.");
            return;
        }

        using var scope = scopeFactory.CreateScope();
        foreach (var definition in AutotradeDatabaseContextCatalog.Definitions)
        {
            await MigrateContextAsync(scope, definition, cancellationToken).ConfigureAwait(false);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task MigrateContextAsync(
        IServiceScope scope,
        AutotradeDatabaseContextDefinition definition,
        CancellationToken cancellationToken)
    {
        var context = scope.ServiceProvider.GetService(definition.ContextType) as DbContext;
        if (context is null)
        {
            logger.LogInformation(
                "Skipping database migrations for {Context}; DbContext is not registered.",
                definition.ContextType.Name);
            return;
        }

        var appliedMigrations = await context.Database
            .GetAppliedMigrationsAsync(cancellationToken)
            .ConfigureAwait(false);
        var pendingMigrations = await context.Database
            .GetPendingMigrationsAsync(cancellationToken)
            .ConfigureAwait(false);

        var appliedList = appliedMigrations.ToList();
        var pendingList = pendingMigrations.ToList();
        if (pendingList.Count == 0)
        {
            logger.LogInformation("Database migrations already up to date for {Context}.", definition.ContextType.Name);
            return;
        }

        if (appliedList.Count == 0
            && await TableExistsAsync(context, definition.SentinelTable, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                $"Detected an existing {definition.ContextType.Name} schema without EF migration history. " +
                "Drop and recreate the database, or explicitly baseline the schema before enabling Database:AutoMigrate. " +
                $"SentinelTable={definition.SentinelTable}.");
        }

        logger.LogInformation(
            "Applying {Count} database migrations for {Context}: {Migrations}",
            pendingList.Count,
            definition.ContextType.Name,
            string.Join(", ", pendingList));

        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Database migrations applied for {Context}.", definition.ContextType.Name);
    }

    private static async Task<bool> TableExistsAsync(
        DbContext context,
        string tableName,
        CancellationToken cancellationToken)
    {
        var provider = context.Database.ProviderName ?? string.Empty;
        var connection = context.Database.GetDbConnection();
        var openedHere = false;
        try
        {
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                openedHere = true;
            }

            await using var command = connection.CreateCommand();
            if (provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
            {
                command.CommandText = "SELECT to_regclass(@tableName) IS NOT NULL;";
                var parameter = command.CreateParameter();
                parameter.ParameterName = "tableName";
                parameter.Value = $"public.\"{tableName.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
                command.Parameters.Add(parameter);
            }
            else if (provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                command.CommandText = "SELECT COUNT(1) FROM sqlite_master WHERE type = 'table' AND name = $tableName;";
                var parameter = command.CreateParameter();
                parameter.ParameterName = "$tableName";
                parameter.Value = tableName;
                command.Parameters.Add(parameter);
            }
            else
            {
                throw new NotSupportedException($"Unsupported database provider: '{provider}'.");
            }

            var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return result is bool exists
                ? exists
                : Convert.ToInt32(result) > 0;
        }
        catch (Exception ex) when (IsMissingPostgresDatabase(ex))
        {
            return false;
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync().ConfigureAwait(false);
            }
        }
    }

    private static bool IsMissingPostgresDatabase(Exception exception)
    {
        var current = exception;
        while (current is not null)
        {
            if (string.Equals(current.GetType().FullName, "Npgsql.PostgresException", StringComparison.Ordinal)
                && string.Equals(
                    current.GetType().GetProperty("SqlState")?.GetValue(current)?.ToString(),
                    "3D000",
                    StringComparison.Ordinal))
            {
                return true;
            }

            current = current.InnerException;
        }

        return false;
    }
}
