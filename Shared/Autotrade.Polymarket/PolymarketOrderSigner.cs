using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Autotrade.Polymarket.Abstractions;
using Autotrade.Polymarket.Http;
using Autotrade.Polymarket.Models;
using Autotrade.Polymarket.Options;
using Microsoft.Extensions.Options;
using Nethereum.Signer;
using Nethereum.Signer.EIP712;

namespace Autotrade.Polymarket;

public sealed class PolymarketOrderSigner : IPolymarketOrderSigner
{
    private const string ZeroBytes32 = "0x0000000000000000000000000000000000000000000000000000000000000000";
    private const decimal TokenDecimals = 1_000_000m;

    private static readonly JsonSerializerOptions TypedDataJsonOptions = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = false
    };

    private static long _lastTimestampMs;

    private readonly PolymarketClobOptions _options;

    public PolymarketOrderSigner(IOptions<PolymarketClobOptions> options)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public PostOrderRequest CreatePostOrderRequest(OrderRequest request, string? idempotencyKey = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_options.OrderSigningVersion != 2)
        {
            throw new InvalidOperationException(
                "Polymarket CLOB V1 order signing is disabled after the 2026-04-28 V2 cutover. Set Polymarket:Clob:OrderSigningVersion=2.");
        }

        if (string.IsNullOrWhiteSpace(_options.PrivateKey))
        {
            throw new InvalidOperationException("Polymarket order signing requires Polymarket:Clob:PrivateKey.");
        }

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException("Polymarket order envelope owner requires Polymarket:Clob:ApiKey.");
        }

        var signer = PolymarketAuthHeaderFactory.GetAddressFromPrivateKey(_options.PrivateKey);
        var maker = string.IsNullOrWhiteSpace(_options.Funder) ? signer : _options.Funder.Trim();
        var deterministicSeed = string.IsNullOrWhiteSpace(idempotencyKey)
            ? null
            : BuildDeterministicSeed(request, idempotencyKey);
        var salt = string.IsNullOrWhiteSpace(request.Salt)
            ? GenerateSalt(deterministicSeed)
            : request.Salt.Trim();
        var side = string.Equals(request.Side, "SELL", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        var signatureType = _options.SignatureType;
        var expiration = request.Expiration.GetValueOrDefault(0).ToString(CultureInfo.InvariantCulture);
        var timestamp = string.IsNullOrWhiteSpace(request.Timestamp)
            ? GenerateTimestamp()
            : request.Timestamp.Trim();
        var metadata = ResolveMetadata(request.Metadata);
        var builder = ResolveBuilder(request.Builder);
        var (makerAmount, takerAmount) = ComputeAmounts(request, side);

        var order = new SignedClobOrder
        {
            Salt = salt,
            Maker = maker,
            Signer = signer,
            TokenId = request.TokenId,
            MakerAmount = makerAmount,
            TakerAmount = takerAmount,
            Expiration = expiration,
            Side = side == 0 ? "BUY" : "SELL",
            SignatureType = signatureType,
            Timestamp = timestamp,
            Metadata = metadata,
            Builder = builder,
            Signature = SignOrder(
                salt,
                maker,
                signer,
                request.TokenId,
                makerAmount,
                takerAmount,
                expiration,
                side,
                signatureType,
                timestamp,
                metadata,
                builder,
                request.NegRisk == true)
        };

        return new PostOrderRequest
        {
            Order = order,
            Owner = _options.ApiKey.Trim(),
            OrderType = string.IsNullOrWhiteSpace(request.TimeInForce) ? "GTC" : request.TimeInForce.Trim(),
            DeferExecution = false
        };
    }

    private string SignOrder(
        string salt,
        string maker,
        string signer,
        string tokenId,
        string makerAmount,
        string takerAmount,
        string expiration,
        int side,
        int signatureType,
        string timestamp,
        string metadata,
        string builder,
        bool negRisk)
    {
        var typedData = new
        {
            types = new
            {
                EIP712Domain = new object[]
                {
                    new { name = "name", type = "string" },
                    new { name = "version", type = "string" },
                    new { name = "chainId", type = "uint256" },
                    new { name = "verifyingContract", type = "address" }
                },
                Order = new object[]
                {
                    new { name = "salt", type = "uint256" },
                    new { name = "maker", type = "address" },
                    new { name = "signer", type = "address" },
                    new { name = "tokenId", type = "uint256" },
                    new { name = "makerAmount", type = "uint256" },
                    new { name = "takerAmount", type = "uint256" },
                    new { name = "side", type = "uint8" },
                    new { name = "signatureType", type = "uint8" },
                    new { name = "timestamp", type = "uint256" },
                    new { name = "metadata", type = "bytes32" },
                    new { name = "builder", type = "bytes32" }
                }
            },
            primaryType = "Order",
            domain = new
            {
                name = "Polymarket CTF Exchange",
                version = "2",
                chainId = _options.ChainId,
                verifyingContract = ResolveExchangeAddress(negRisk)
            },
            message = new
            {
                salt,
                maker,
                signer,
                tokenId,
                makerAmount,
                takerAmount,
                side,
                signatureType,
                timestamp,
                metadata,
                builder
            }
        };

        var json = JsonSerializer.Serialize(typedData, TypedDataJsonOptions);
        var signature = new Eip712TypedDataSigner()
            .SignTypedDataV4(json, new EthECKey(NormalizePrivateKey(_options.PrivateKey!)));

        return signature.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? signature
            : $"0x{signature}";
    }

    private string ResolveExchangeAddress(bool negRisk)
    {
        var configured = negRisk ? _options.NegRiskExchangeAddress : _options.ExchangeAddress;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim();
        }

        return (_options.ChainId, negRisk) switch
        {
            (137, false) => "0xE111180000d2663C0091e4f400237545B87B996B",
            (137, true) => "0xe2222d279d744050d28e00520010520000310F59",
            _ => throw new InvalidOperationException(
                $"No default Polymarket CLOB V2 exchange contract is known for chain {_options.ChainId}. Configure Polymarket:Clob:ExchangeAddress.")
        };
    }

    private static (string MakerAmount, string TakerAmount) ComputeAmounts(OrderRequest request, int side)
    {
        var price = decimal.Parse(request.Price, CultureInfo.InvariantCulture);
        var size = decimal.Parse(request.Size, CultureInfo.InvariantCulture);

        var sizeAmount = ToTokenDecimals(size);
        var priceAmount = ToTokenDecimals(size * price);

        return side == 0
            ? (priceAmount, sizeAmount)
            : (sizeAmount, priceAmount);
    }

    private static string ToTokenDecimals(decimal value)
    {
        var scaled = decimal.Round(value * TokenDecimals, 0, MidpointRounding.AwayFromZero);
        return scaled.ToString("0", CultureInfo.InvariantCulture);
    }

    private string ResolveMetadata(string? requestMetadata)
    {
        var metadata = string.IsNullOrWhiteSpace(requestMetadata)
            ? _options.OrderMetadata
            : requestMetadata;
        metadata = string.IsNullOrWhiteSpace(metadata) ? ZeroBytes32 : metadata.Trim();

        if (!metadata.StartsWith("0x", StringComparison.OrdinalIgnoreCase) || metadata.Length != 66)
        {
            throw new InvalidOperationException("Polymarket CLOB V2 order metadata must be a bytes32 hex string.");
        }

        return metadata;
    }

    private string ResolveBuilder(string? requestBuilder)
    {
        var builder = string.IsNullOrWhiteSpace(requestBuilder)
            ? _options.BuilderCode
            : requestBuilder;
        builder = string.IsNullOrWhiteSpace(builder) ? ZeroBytes32 : builder.Trim();

        if (!builder.StartsWith("0x", StringComparison.OrdinalIgnoreCase) || builder.Length != 66)
        {
            throw new InvalidOperationException("Polymarket CLOB V2 builder metadata must be a bytes32 hex string.");
        }

        return builder;
    }

    private static string GenerateSalt(string? deterministicSeed)
    {
        Span<byte> bytes = stackalloc byte[32];
        if (string.IsNullOrWhiteSpace(deterministicSeed))
        {
            RandomNumberGenerator.Fill(bytes);
        }
        else
        {
            SHA256.HashData(Encoding.UTF8.GetBytes($"salt\0{deterministicSeed}"), bytes);
        }

        var value = new BigInteger(bytes, isUnsigned: true, isBigEndian: true);
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static string GenerateTimestamp()
    {
        while (true)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var previous = Volatile.Read(ref _lastTimestampMs);
            var next = Math.Max(now, previous + 1);

            if (Interlocked.CompareExchange(ref _lastTimestampMs, next, previous) == previous)
            {
                return next.ToString(CultureInfo.InvariantCulture);
            }
        }
    }

    private static string BuildDeterministicSeed(OrderRequest request, string idempotencyKey)
    {
        return string.Join(
            "\u001f",
            idempotencyKey.Trim(),
            request.TokenId,
            request.Price,
            request.Size,
            request.Side,
            request.TimeInForce ?? string.Empty,
            request.Expiration?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            request.NegRisk?.ToString() ?? string.Empty,
            request.Metadata ?? string.Empty,
            request.Builder ?? string.Empty);
    }

    private static string NormalizePrivateKey(string privateKey) =>
        privateKey.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? privateKey
            : $"0x{privateKey}";
}
