namespace Autotrade.OpportunityDiscovery.Domain.Exceptions;

public sealed class OpportunityLifecycleException : InvalidOperationException
{
    public OpportunityLifecycleException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = string.IsNullOrWhiteSpace(errorCode)
            ? "OpportunityLifecycle.Invalid"
            : errorCode.Trim();
    }

    public string ErrorCode { get; }
}
