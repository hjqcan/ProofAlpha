using System.Text.Json;
using Autotrade.Cli.Infrastructure;
using Autotrade.Polymarket;
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
        FileInfo? output,
        FileInfo? envelopeOutput,
        CancellationToken cancellationToken = default)
    {
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
