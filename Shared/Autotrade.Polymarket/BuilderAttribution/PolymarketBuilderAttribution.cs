using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Autotrade.Polymarket.Models;
using Autotrade.Polymarket.Options;

namespace Autotrade.Polymarket.BuilderAttribution;

public sealed record PolymarketBuilderReadiness(
    bool BuilderCodeConfigured,
    bool FormatValid,
    string Mode,
    string? BuilderCodeHash,
    string Summary);

public sealed record PolymarketBuilderEvidenceRequest(
    PostOrderRequest SignedOrder,
    string ClientOrderId,
    string? StrategyId,
    string MarketId,
    string ArcSignalId,
    string Price,
    string Size,
    DateTimeOffset CreatedAtUtc,
    string? ExchangeOrderId = null,
    string? RunSessionId = null,
    string? CommandAuditId = null);

public sealed record PolymarketBuilderAttributionEvidence(
    string ClientOrderId,
    string? StrategyId,
    string MarketId,
    string ArcSignalId,
    string BuilderCodeHash,
    string SignedOrderHash,
    string OrderType,
    string Price,
    string Size,
    DateTimeOffset CreatedAtUtc,
    PolymarketBuilderExecutionCorrelation Correlation,
    RedactedPolymarketOrderEnvelope OrderEnvelope,
    PolymarketBuilderExternalVerification ExternalVerification);

public sealed record PolymarketBuilderExecutionCorrelation(
    string ArcSignalId,
    string ClientOrderId,
    string? OrderId,
    string BuilderCodeHash,
    string? RunSessionId,
    string? CommandAuditId);

public sealed record RedactedPolymarketOrderEnvelope(
    string OwnerHash,
    string MakerHash,
    string SignerHash,
    string TokenIdHash,
    string SaltHash,
    string Timestamp,
    string Side,
    string MetadataHash,
    string BuilderCodeHash,
    string SignatureHash);

public sealed record PolymarketBuilderExternalVerification(
    string Status,
    string Reason);

public static class PolymarketBuilderAttribution
{
    public const string ZeroBytes32 = "0x0000000000000000000000000000000000000000000000000000000000000000";

    private static readonly JsonSerializerOptions CanonicalJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public static PolymarketBuilderReadiness EvaluateReadiness(
        PolymarketClobOptions options,
        string? executionMode)
    {
        ArgumentNullException.ThrowIfNull(options);

        var builderCode = NormalizeBuilderCode(options.BuilderCode);
        var configured = !string.IsNullOrWhiteSpace(options.BuilderCode) &&
            !string.Equals(builderCode, ZeroBytes32, StringComparison.OrdinalIgnoreCase);
        var formatValid = string.IsNullOrWhiteSpace(options.BuilderCode) || IsBytes32Hex(builderCode);
        var mode = ResolveMode(configured, formatValid, executionMode);
        var builderCodeHash = configured && formatValid ? HashValue(builderCode) : null;
        var summary = mode switch
        {
            "disabled" => "Polymarket builder attribution is disabled because no non-zero builder code is configured.",
            "invalid" => "Polymarket builder attribution is configured but the builder code is not bytes32 hex.",
            "live" => "Polymarket builder attribution is configured for Live order flow.",
            _ => "Polymarket builder attribution is configured for demo or Paper evidence."
        };

        return new PolymarketBuilderReadiness(configured, formatValid, mode, builderCodeHash, summary);
    }

    public static PolymarketBuilderAttributionEvidence CreateEvidence(
        PolymarketBuilderEvidenceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.SignedOrder);

        if (string.IsNullOrWhiteSpace(request.ClientOrderId))
        {
            throw new ArgumentException("Client order id is required.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.MarketId))
        {
            throw new ArgumentException("Market id is required.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.ArcSignalId))
        {
            throw new ArgumentException("Arc signal id is required.", nameof(request));
        }

        var order = request.SignedOrder.Order;
        if (!IsBytes32Hex(order.Builder))
        {
            throw new ArgumentException("Signed order builder must be bytes32 hex.", nameof(request));
        }

        var builderCodeHash = HashValue(NormalizeBuilderCode(order.Builder));
        var signatureHash = HashValue(order.Signature);
        var envelope = new RedactedPolymarketOrderEnvelope(
            HashValue(request.SignedOrder.Owner),
            HashValue(order.Maker),
            HashValue(order.Signer),
            HashValue(order.TokenId),
            HashValue(order.Salt),
            order.Timestamp,
            order.Side,
            HashValue(order.Metadata),
            builderCodeHash,
            signatureHash);
        var signedOrderHash = HashStable(new
        {
            ownerHash = envelope.OwnerHash,
            makerHash = envelope.MakerHash,
            signerHash = envelope.SignerHash,
            tokenIdHash = envelope.TokenIdHash,
            saltHash = envelope.SaltHash,
            timestamp = envelope.Timestamp,
            side = envelope.Side,
            metadataHash = envelope.MetadataHash,
            builderCodeHash = envelope.BuilderCodeHash,
            signatureHash = envelope.SignatureHash,
            orderType = request.SignedOrder.OrderType,
            deferExecution = request.SignedOrder.DeferExecution
        });

        var correlation = new PolymarketBuilderExecutionCorrelation(
            request.ArcSignalId.Trim(),
            request.ClientOrderId.Trim(),
            request.ExchangeOrderId?.Trim(),
            builderCodeHash,
            request.RunSessionId?.Trim(),
            request.CommandAuditId?.Trim());

        return new PolymarketBuilderAttributionEvidence(
            request.ClientOrderId.Trim(),
            request.StrategyId?.Trim(),
            request.MarketId.Trim(),
            request.ArcSignalId.Trim(),
            builderCodeHash,
            signedOrderHash,
            request.SignedOrder.OrderType,
            request.Price.Trim(),
            request.Size.Trim(),
            request.CreatedAtUtc,
            correlation,
            envelope,
            new PolymarketBuilderExternalVerification(
                "not_used",
                "Demo evidence relies on the signed order envelope; builder-trades verification requires real attributed order flow."));
    }

    public static bool IsBytes32Hex(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (!trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase) || trimmed.Length != 66)
        {
            return false;
        }

        for (var i = 2; i < trimmed.Length; i++)
        {
            var c = trimmed[i];
            var isHex = c is >= '0' and <= '9'
                or >= 'a' and <= 'f'
                or >= 'A' and <= 'F';
            if (!isHex)
            {
                return false;
            }
        }

        return true;
    }

    public static string HashValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim()));
        return $"0x{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }

    private static string NormalizeBuilderCode(string? builderCode)
    {
        return string.IsNullOrWhiteSpace(builderCode)
            ? ZeroBytes32
            : builderCode.Trim();
    }

    private static string ResolveMode(bool configured, bool formatValid, string? executionMode)
    {
        if (!formatValid)
        {
            return "invalid";
        }

        if (!configured)
        {
            return "disabled";
        }

        return string.Equals(executionMode, "Live", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(executionMode, "LiveServices", StringComparison.OrdinalIgnoreCase)
            ? "live"
            : "demo";
    }

    private static string HashStable<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, CanonicalJsonOptions);
        return HashValue(json);
    }
}
