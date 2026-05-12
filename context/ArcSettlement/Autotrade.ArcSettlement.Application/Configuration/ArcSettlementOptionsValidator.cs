using Autotrade.ArcSettlement.Application.Contract.Access;
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
        AddRequiredAddress(errors, "SignalProof:AgentAddress", options.SignalProof.AgentAddress);
        AddSubscriptionPlanErrors(options, errors);
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

        var path = contractName.Contains(':', StringComparison.Ordinal)
            ? contractName
            : $"Contracts:{contractName}";
        errors.Add($"{ArcSettlementOptions.SectionName}:{path} must be a non-zero EVM address.");
    }

    private static void AddSubscriptionPlanErrors(ArcSettlementOptions options, List<string> errors)
    {
        var planIds = new HashSet<int>();
        foreach (var plan in options.SubscriptionPlans)
        {
            var planPath = $"{ArcSettlementOptions.SectionName}:SubscriptionPlans:{plan.PlanId}";
            if (plan.PlanId <= 0)
            {
                errors.Add($"{planPath}:PlanId must be greater than zero.");
            }
            else if (!planIds.Add(plan.PlanId))
            {
                errors.Add($"{planPath}:PlanId must be unique.");
            }

            if (string.IsNullOrWhiteSpace(plan.StrategyKey))
            {
                errors.Add($"{planPath}:StrategyKey is required.");
            }

            if (string.IsNullOrWhiteSpace(plan.Tier))
            {
                errors.Add($"{planPath}:Tier is required.");
            }

            if (plan.PriceUsdc < 0)
            {
                errors.Add($"{planPath}:PriceUsdc cannot be negative.");
            }

            if (plan.DurationSeconds <= 0 && plan.DurationDays <= 0)
            {
                errors.Add($"{planPath}:DurationSeconds or DurationDays must be greater than zero.");
            }

            foreach (var permission in plan.Permissions)
            {
                if (!Enum.TryParse<ArcEntitlementPermission>(permission, ignoreCase: true, out _))
                {
                    errors.Add($"{planPath}:Permissions contains unknown value '{permission}'.");
                }
            }
        }
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
