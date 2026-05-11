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
            }
        };

    private sealed class StaticSecretSource(bool hasSecret) : IArcSettlementSecretSource
    {
        public bool HasSecret(string environmentVariableName)
            => hasSecret;
    }
}
