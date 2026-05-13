using System.Text.Json;
using Autotrade.Cli.Infrastructure;
using Autotrade.Polymarket;
using Autotrade.Polymarket.Abstractions;
using Autotrade.Polymarket.BuilderAttribution;
using Autotrade.Polymarket.Models;
using Autotrade.Polymarket.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Autotrade.Cli.Commands;

public static class ArcBuilderCommands
{
    private const string DefaultDemoBuilderCode = "0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string DefaultDemoPrivateKey = "0x0000000000000000000000000000000000000000000000000000000000000001";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    public static async Task<int> ExportEvidenceAsync(
        CommandContext context,
        bool demo,
        string clientOrderId,
        string? strategyId,
        string marketId,
        string arcSignalId,
        string tokenId,
        string side,
        string price,
        string size,
        string timeInForce,
        string? builderCode,
        DateTimeOffset? createdAtUtc,
        string? exchangeOrderId,
        string? runSessionId,
        string? commandAuditId,
        bool verifyBuilderTrades,
        FileInfo? output,
        FileInfo? envelopeOutput,
        CancellationToken cancellationToken = default)
    {
        if (verifyBuilderTrades && demo)
        {
            OutputFormatter.WriteError(
                "Builder trades verification requires configured Polymarket credentials and cannot run in demo mode.",
                "BUILDER_TRADES_VERIFY_REQUIRES_CONFIGURED_MODE",
                context.GlobalOptions,
                ExitCodes.ValidationFailed);
            return ExitCodes.ValidationFailed;
        }

        if (verifyBuilderTrades && string.IsNullOrWhiteSpace(exchangeOrderId))
        {
            OutputFormatter.WriteError(
                "--exchange-order-id is required when --verify-builder-trades is set.",
                "BUILDER_TRADES_VERIFY_REQUIRES_ORDER_ID",
                context.GlobalOptions,
                ExitCodes.ValidationFailed);
            return ExitCodes.ValidationFailed;
        }

        var options = ResolveOptions(context, demo, builderCode);
        var orderRequest = new OrderRequest
        {
            TokenId = tokenId,
            Price = price,
            Size = size,
            Side = side,
            TimeInForce = timeInForce,
            Salt = demo ? "1234567890123456789012345678901234567890" : null,
            Timestamp = demo ? "1777374000000" : null,
            Builder = builderCode
        };

        try
        {
            var signer = new PolymarketOrderSigner(Options.Create(options));
            var signedOrder = signer.CreatePostOrderRequest(orderRequest, clientOrderId);
            var evidence = PolymarketBuilderAttribution.CreateEvidence(
                new PolymarketBuilderEvidenceRequest(
                    signedOrder,
                    clientOrderId,
                    strategyId,
                    marketId,
                    arcSignalId,
                    price,
                    size,
                    createdAtUtc ?? DateTimeOffset.UtcNow,
                    exchangeOrderId,
                    runSessionId,
                    commandAuditId));
            string? verificationFailure = null;

            if (verifyBuilderTrades)
            {
                var clobClient = context.Services.GetRequiredService<IPolymarketClobClient>();
                var trades = await clobClient.GetBuilderTradesAsync(
                        signedOrder.Order.Builder,
                        market: marketId,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (!trades.IsSuccess || trades.Data is null)
                {
                    OutputFormatter.WriteError(
                        $"Polymarket builder trades verification failed: {trades.Error?.Message ?? "empty response"}",
                        "BUILDER_TRADES_VERIFY_FAILED",
                        context.GlobalOptions,
                        ExitCodes.GeneralError);
                    return ExitCodes.GeneralError;
                }

                var verification = PolymarketBuilderAttribution.VerifyExternalTrades(
                    evidence,
                    trades.Data,
                    DateTimeOffset.UtcNow);
                evidence = evidence with { ExternalVerification = verification };

                if (!string.Equals(verification.Status, "matched", StringComparison.OrdinalIgnoreCase))
                {
                    verificationFailure = verification.Reason;
                }
            }

            if (output is not null &&
                !await WriteJsonAsync(context, output, evidence, cancellationToken).ConfigureAwait(false))
            {
                return ExitCodes.UserCancelled;
            }

            if (envelopeOutput is not null &&
                !await WriteJsonAsync(context, envelopeOutput, evidence.OrderEnvelope, cancellationToken).ConfigureAwait(false))
            {
                return ExitCodes.UserCancelled;
            }

            if (verificationFailure is not null)
            {
                OutputFormatter.WriteError(
                    verificationFailure,
                    "BUILDER_TRADES_NOT_MATCHED",
                    context.GlobalOptions,
                    ExitCodes.ValidationFailed);
                return ExitCodes.ValidationFailed;
            }

            OutputFormatter.WriteSuccess("Polymarket builder attribution evidence exported.", evidence, context.GlobalOptions);
            return ExitCodes.Success;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or JsonException)
        {
            OutputFormatter.WriteError(ex.Message, "BUILDER_EVIDENCE_EXPORT_FAILED", context.GlobalOptions, ExitCodes.ValidationFailed);
            return ExitCodes.ValidationFailed;
        }
    }

    private static PolymarketClobOptions ResolveOptions(
        CommandContext context,
        bool demo,
        string? builderCode)
    {
        if (demo)
        {
            return new PolymarketClobOptions
            {
                ApiKey = "demo-builder-owner",
                PrivateKey = DefaultDemoPrivateKey,
                ChainId = 137,
                BuilderCode = string.IsNullOrWhiteSpace(builderCode) ? DefaultDemoBuilderCode : builderCode,
                DisableProxy = true
            };
        }

        var configured = context.Services.GetRequiredService<IOptions<PolymarketClobOptions>>().Value;
        return new PolymarketClobOptions
        {
            Host = configured.Host,
            ChainId = configured.ChainId,
            PrivateKey = configured.PrivateKey,
            Address = configured.Address,
            Funder = configured.Funder,
            OrderSigningVersion = configured.OrderSigningVersion,
            SignatureType = configured.SignatureType,
            ExchangeAddress = configured.ExchangeAddress,
            NegRiskExchangeAddress = configured.NegRiskExchangeAddress,
            BuilderCode = string.IsNullOrWhiteSpace(builderCode) ? configured.BuilderCode : builderCode,
            OrderMetadata = configured.OrderMetadata,
            ApiKey = configured.ApiKey,
            ApiSecret = configured.ApiSecret,
            ApiPassphrase = configured.ApiPassphrase,
            UseServerTime = configured.UseServerTime,
            Timeout = configured.Timeout,
            DisableProxy = configured.DisableProxy
        };
    }

    private static async Task<bool> WriteJsonAsync(
        CommandContext context,
        FileInfo output,
        object payload,
        CancellationToken cancellationToken)
    {
        if (output.Directory is not null)
        {
            output.Directory.Create();
        }

        if (output.Exists &&
            !ConfirmationService.Confirm($"Confirm overwrite file '{output.FullName}'?", context.GlobalOptions))
        {
            return false;
        }

        await File.WriteAllTextAsync(
                output.FullName,
                JsonSerializer.Serialize(payload, JsonOptions),
                cancellationToken)
            .ConfigureAwait(false);

        return true;
    }
}
