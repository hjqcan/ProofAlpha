using Autotrade.Application.RunSessions;
using Autotrade.Trading.Application.Contract.Execution;
using Autotrade.Trading.Domain.Shared.Enums;
using Microsoft.Extensions.Logging;

namespace Autotrade.Trading.Application.RunSessions;

internal static class TradingRunSessionResolver
{
    public static async Task<Guid?> ResolvePaperRunSessionIdAsync(
        IRunSessionAccessor? runSessionAccessor,
        ExecutionOptions executionOptions,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executionOptions);
        ArgumentNullException.ThrowIfNull(logger);

        if (runSessionAccessor is null || executionOptions.Mode != ExecutionMode.Paper)
        {
            return null;
        }

        try
        {
            var session = await runSessionAccessor
                .GetCurrentAsync(executionOptions.Mode.ToString(), cancellationToken)
                .ConfigureAwait(false);

            return session?.SessionId;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to resolve Paper run session for order audit.");
            return null;
        }
    }
}
