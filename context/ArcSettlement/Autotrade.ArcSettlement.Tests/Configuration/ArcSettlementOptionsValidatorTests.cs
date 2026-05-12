using Autotrade.ArcSettlement.Application.Configuration;
using Autotrade.ArcSettlement.Application.Contract.Configuration;

namespace Autotrade.ArcSettlement.Tests.Configuration;

public sealed class ArcSettlementOptionsValidatorTests
{
    [Fact]
    public void DisabledConfig_DoesNotRequireRpcContractsOrPrivateKey()
    {
        var validator = new ArcSettlementOptionsValidator(new StaticSecretSource(hasSecret: false));
        var options = new ArcSettlementOptions
        {
            Enabled = false
        };

        var result = validator.Validate(options, ArcSettlementOptionsValidationMode.Write);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void EnabledReadConfig_ValidatesPublicFieldsWithoutPrivateKey()
    {
        var validator = new ArcSettlementOptionsValidator(new StaticSecretSource(hasSecret: false));
        var options = CreateValidEnabledOptions();

        var result = validator.Validate(options, ArcSettlementOptionsValidationMode.ReadOnly);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void EnabledWriteConfig_RequiresSecretPresenceWithoutExposingSecretValue()
    {
        var validator = new ArcSettlementOptionsValidator(new StaticSecretSource(hasSecret: false));
        var options = CreateValidEnabledOptions() with
        {
            Wallet = new ArcSettlementWalletOptions
            {
                PrivateKeyEnvironmentVariable = "ARC_SETTLEMENT_PRIVATE_KEY"
            },
            SignalProof = new ArcSettlementSignalProofOptions
            {
                AgentAddress = "0x9999999999999999999999999999999999999999"
            }
        };

        var result = validator.Validate(options, ArcSettlementOptionsValidationMode.Write);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("ARC_SETTLEMENT_PRIVATE_KEY", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Errors, error => error.Contains("0xabc", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EnabledWriteConfig_AcceptsSecretPresence()
    {
        var validator = new ArcSettlementOptionsValidator(new StaticSecretSource(hasSecret: true));
        var options = CreateValidEnabledOptions();

        var result = validator.Validate(options, ArcSettlementOptionsValidationMode.Write);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void EnabledReadConfig_ValidatesConfiguredSubscriptionPlans()
    {
        var validator = new ArcSettlementOptionsValidator(new StaticSecretSource(hasSecret: false));
        var options = CreateValidEnabledOptions() with
        {
            SubscriptionPlans =
            [
                new ArcSettlementSubscriptionPlanOptions
                {
                    PlanId = 1,
                    StrategyKey = "",
                    Tier = "SignalViewer",
                    PriceUsdc = -1m,
                    DurationDays = 0,
                    DurationSeconds = 0,
                    Permissions = ["ViewSignals", "UnknownPermission"]
                },
                new ArcSettlementSubscriptionPlanOptions
                {
                    PlanId = 1,
                    StrategyKey = "repricing_lag_arbitrage",
                    Tier = "",
                    PriceUsdc = 10m,
                    DurationDays = 7,
                    Permissions = ["ViewSignals"]
                }
            ]
        };

        var result = validator.Validate(options, ArcSettlementOptionsValidationMode.ReadOnly);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("StrategyKey is required", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("PriceUsdc cannot be negative", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("DurationSeconds or DurationDays", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("UnknownPermission", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("PlanId must be unique", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("Tier is required", StringComparison.Ordinal));
    }

    private static ArcSettlementOptions CreateValidEnabledOptions()
        => new()
        {
            Enabled = true,
            ChainId = 31337,
            RpcUrl = "http://127.0.0.1:8545",
            BlockExplorerBaseUrl = "http://127.0.0.1:8545",
            Contracts = new ArcSettlementContractsOptions
            {
                SignalRegistry = "0x1111111111111111111111111111111111111111",
                StrategyAccess = "0x2222222222222222222222222222222222222222",
                PerformanceLedger = "0x3333333333333333333333333333333333333333",
                RevenueSettlement = "0x4444444444444444444444444444444444444444"
            },
            Wallet = new ArcSettlementWalletOptions
            {
                PrivateKeyEnvironmentVariable = "ARC_SETTLEMENT_PRIVATE_KEY"
            },
            SignalProof = new ArcSettlementSignalProofOptions
            {
                AgentAddress = "0x9999999999999999999999999999999999999999"
            }
        };

    private sealed class StaticSecretSource(bool hasSecret) : IArcSettlementSecretSource
    {
        public bool HasSecret(string environmentVariableName)
            => hasSecret;
    }
}
