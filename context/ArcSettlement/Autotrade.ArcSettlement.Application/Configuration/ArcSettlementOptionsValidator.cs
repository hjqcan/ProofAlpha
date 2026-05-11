using Autotrade.ArcSettlement.Application.Contract.Configuration;

namespace Autotrade.ArcSettlement.Application.Configuration;

public enum ArcSettlementOptionsValidationMode
{
    ReadOnly = 0,
    Write = 1
}

public interface IArcSettlementSecretSource
{
    bool HasSecret(string environmentVariableName);
}

public sealed class EnvironmentArcSettlementSecretSource : IArcSettlementSecretSource
{
    public bool HasSecret(string environmentVariableName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentVariableName);
        return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(environmentVariableName));
    }
}

public sealed record ArcSettlementOptionsValidationResult(IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

public sealed class ArcSettlementOptionsValidator(IArcSettlementSecretSource secretSource)
{
    public ArcSettlementOptionsValidationResult Validate(
        ArcSettlementOptions options,
        ArcSettlementOptionsValidationMode mode)
    {
        ArgumentNullException.ThrowIfNull(options);

        var errors = new List<string>();
        if (!options.Enabled)
        {
            return new ArcSettlementOptionsValidationResult(errors);
        }

        AddRequiredPublicFieldErrors(options, errors);

        if (mode == ArcSettlementOptionsValidationMode.Write)
        {
            AddWriteSecretErrors(options, errors);
        }

        return new ArcSettlementOptionsValidationResult(errors);
    }

    private static void AddRequiredPublicFieldErrors(ArcSettlementOptions options, List<string> errors)
    {
        if (options.ChainId <= 0)
        {
            errors.Add($"{ArcSettlementOptions.SectionName}:ChainId must be greater than zero when Arc settlement is enabled.");
        }

        if (string.IsNullOrWhiteSpace(options.RpcUrl))
        {
            errors.Add($"{ArcSettlementOptions.SectionName}:RpcUrl is required when Arc settlement is enabled.");
        }

        AddRequiredAddress(errors, "SignalRegistry", options.Contracts.SignalRegistry);
        AddRequiredAddress(errors, "StrategyAccess", options.Contracts.StrategyAccess);
        AddRequiredAddress(errors, "PerformanceLedger", options.Contracts.PerformanceLedger);
        AddRequiredAddress(errors, "RevenueSettlement", options.Contracts.RevenueSettlement);
    }

    private void AddWriteSecretErrors(ArcSettlementOptions options, List<string> errors)
    {
        var environmentVariableName = options.Wallet.PrivateKeyEnvironmentVariable;
        if (string.IsNullOrWhiteSpace(environmentVariableName))
        {
            errors.Add($"{ArcSettlementOptions.SectionName}:Wallet:PrivateKeyEnvironmentVariable is required for write mode.");
            return;
        }

        if (!secretSource.HasSecret(environmentVariableName))
        {
            errors.Add(
                $"{ArcSettlementOptions.SectionName}:Wallet private key secret is missing from environment variable '{environmentVariableName}'.");
        }
    }

    private static void AddRequiredAddress(List<string> errors, string contractName, string value)
    {
        if (IsLikelyEvmAddress(value))
        {
            return;
        }

        errors.Add($"{ArcSettlementOptions.SectionName}:Contracts:{contractName} must be a non-zero EVM address.");
    }

    private static bool IsLikelyEvmAddress(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length != 42 || !value.StartsWith("0x", StringComparison.Ordinal))
        {
            return false;
        }

        return value[2..].All(Uri.IsHexDigit) && !value[2..].All(character => character == '0');
    }
}
