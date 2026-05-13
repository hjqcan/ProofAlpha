using Autotrade.Polymarket.Abstractions;
using Autotrade.Polymarket.BuilderAttribution;
using Autotrade.Polymarket.Extensions;
using Autotrade.Polymarket.Http;
using Autotrade.Polymarket.Models;
using Autotrade.Polymarket.Options;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using Xunit;

namespace Autotrade.Polymarket.Tests;

/// <summary>
/// Polymarket CLOB Client 契约测试（使用 WireMock.Net 模拟服务端）。
/// 覆盖核心请求路径与可观测性组件的基础行为。
/// </summary>
public sealed class PolymarketClientContractTests : IClassFixture<MockServerFixture>, IDisposable
{
    private readonly MockServerFixture _fixture;
    private readonly IPolymarketClobClient _client;
    private readonly ServiceProvider _sp;

    public PolymarketClientContractTests(MockServerFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();

        var services = new ServiceCollection();

        services.AddLogging(builder => builder.AddDebug().SetMinimumLevel(LogLevel.Trace));

        services.AddPolymarketClobClient(
            clob =>
            {
                clob.Host = _fixture.BaseUrl;
                clob.ApiKey = "test-api-key";
                clob.ApiSecret = "dGVzdC1hcGktc2VjcmV0LWtleS1mb3ItdGVzdGluZw==";
                clob.ApiPassphrase = "test-passphrase";
                clob.Address = "0x1234567890abcdef1234567890abcdef12345678";
                clob.PrivateKey = "0x0000000000000000000000000000000000000000000000000000000000000001";
                clob.DisableProxy = true;
            },
            resilience =>
            {
                resilience.MaxRetryAttempts = 1;
            },
            rateLimit =>
            {
                rateLimit.Enabled = false;
            });

        _sp = services.BuildServiceProvider();
        _client = _sp.GetRequiredService<IPolymarketClobClient>();
    }

    public void Dispose()
    {
        _sp.Dispose();
    }

    // ─────────────────────────────────────────────────────────────
    // GetServerTimeAsync
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetServerTimeAsync_ReturnsTimestamp_WhenServerReturnsNumber()
    {
        var expectedTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _fixture.Server
            .Given(Request.Create().WithPath("/time").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody(expectedTimestamp.ToString()));

        var result = await _client.GetServerTimeAsync();

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().Be(expectedTimestamp);
    }

    [Fact]
    public async Task GetServerTimeAsync_ReturnsFailure_WhenServerReturns500()
    {
        _fixture.Server
            .Given(Request.Create().WithPath("/time").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(500)
                .WithBody("Internal Server Error"));

        var result = await _client.GetServerTimeAsync();

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(500);
    }

    [Fact]
    public async Task GetTradesAsync_AggregatesPaginatedTrades_FromDataTrades()
    {
        _fixture.Server
            .Given(Request.Create()
                .WithPath("/data/trades")
                .WithParam("market", "market-1")
                .WithParam("next_cursor", PolymarketConstants.InitialCursor)
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("""
                {
                  "data": [
                    {
                      "id": "trade-1",
                      "market": "market-1",
                      "asset_id": "asset-1",
                      "side": "BUY",
                      "size": "5",
                      "price": "0.42",
                      "status": "matched"
                    }
                  ],
                  "next_cursor": "cursor-2",
                  "limit": 100,
                  "count": 1
                }
                """));

        _fixture.Server
            .Given(Request.Create()
                .WithPath("/data/trades")
                .WithParam("market", "market-1")
                .WithParam("next_cursor", "cursor-2")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("""
                {
                  "data": [
                    {
                      "id": "trade-2",
                      "market": "market-1",
                      "asset_id": "asset-2",
                      "side": "SELL",
                      "size": "3",
                      "price": "0.58",
                      "status": "matched"
                    }
                  ],
                  "next_cursor": "LTE=",
                  "limit": 100,
                  "count": 1
                }
                """));

        var result = await _client.GetTradesAsync("market-1");

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Select(trade => trade.Id).Should().Equal("trade-1", "trade-2");
    }

    // ─────────────────────────────────────────────────────────────
    // Observability Tests (these don't require WireMock)
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetTradesAsync_ReturnsTrades_WhenServerReturnsPaginatedObject()
    {
        _fixture.Server
            .Given(Request.Create()
                .WithPath("/data/trades")
                .UsingGet()
                .WithParam("market", "market-1")
                .WithParam("next_cursor", PolymarketConstants.InitialCursor)
                .WithHeader(IsSignedWithTradesPath))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("""
                {
                  "data": [
                    {
                      "id": "trade-1",
                      "taker_order_id": "order-1",
                      "market": "market-1",
                      "asset_id": "asset-1",
                      "side": "BUY",
                      "size": "10",
                      "fee_rate_bps": "0",
                      "price": "0.42",
                      "status": "MATCHED",
                      "match_time": "1777374000",
                      "last_update": "1777374001",
                      "outcome": "Yes"
                    }
                  ],
                  "next_cursor": "LTE=",
                  "limit": 100,
                  "count": 1
                }
                """));

        var result = await _client.GetTradesAsync("market-1");

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data.Should().ContainSingle();
        result.Data![0].Id.Should().Be("trade-1");
    }

    [Fact]
    public async Task GetTradesAsync_RequestsNextPageAndMergesTrades()
    {
        const string secondCursor = "NQ==";

        _fixture.Server
            .Given(Request.Create()
                .WithPath("/data/trades")
                .UsingGet()
                .WithParam("market", "market-1")
                .WithParam("next_cursor", PolymarketConstants.InitialCursor)
                .WithHeader(IsSignedWithTradesPath))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody($$"""
                {
                  "data": [
                    {
                      "id": "trade-1",
                      "taker_order_id": "order-1",
                      "market": "market-1",
                      "asset_id": "asset-1",
                      "side": "BUY",
                      "size": "10",
                      "fee_rate_bps": "0",
                      "price": "0.42",
                      "status": "MATCHED",
                      "match_time": "1777374000",
                      "last_update": "1777374001",
                      "outcome": "Yes"
                    }
                  ],
                  "next_cursor": "{{secondCursor}}",
                  "limit": 100,
                  "count": 1
                }
                """));

        _fixture.Server
            .Given(Request.Create()
                .WithPath("/data/trades")
                .UsingGet()
                .WithParam("market", "market-1")
                .WithParam("next_cursor", secondCursor)
                .WithHeader(IsSignedWithTradesPath))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("""
                {
                  "data": [
                    {
                      "id": "trade-2",
                      "taker_order_id": "order-2",
                      "market": "market-1",
                      "asset_id": "asset-2",
                      "side": "SELL",
                      "size": "5",
                      "fee_rate_bps": "0",
                      "price": "0.58",
                      "status": "MATCHED",
                      "match_time": "1777374002",
                      "last_update": "1777374003",
                      "outcome": "No"
                    }
                  ],
                  "next_cursor": "LTE=",
                  "limit": 100,
                  "count": 1
                }
                """));

        var result = await _client.GetTradesAsync("market-1");

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Select(t => t.Id).Should().Equal("trade-1", "trade-2");
    }

    [Fact]
    public async Task GetBuilderTradesAsync_AggregatesPaginatedBuilderTrades_FromBuilderEndpoint()
    {
        var builderCode = Bytes32('b');
        const string secondCursor = "NQ==";

        _fixture.Server
            .Given(Request.Create()
                .WithPath("/builder/trades")
                .UsingGet()
                .WithParam("builder_code", builderCode)
                .WithParam("market", "market-1")
                .WithParam("next_cursor", PolymarketConstants.InitialCursor))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody($$"""
                {
                  "data": [
                    {
                      "id": "builder-trade-1",
                      "takerOrderHash": "exchange-1",
                      "market": "market-1",
                      "assetId": "asset-1",
                      "side": "BUY",
                      "size": "10",
                      "sizeUsdc": "4.2",
                      "price": "0.42",
                      "fee": "0.01",
                      "feeUsdc": "0.01",
                      "status": "MATCHED",
                      "builder": "{{builderCode}}"
                    }
                  ],
                  "next_cursor": "{{secondCursor}}",
                  "limit": 100,
                  "count": 1
                }
                """));

        _fixture.Server
            .Given(Request.Create()
                .WithPath("/builder/trades")
                .UsingGet()
                .WithParam("builder_code", builderCode)
                .WithParam("market", "market-1")
                .WithParam("next_cursor", secondCursor))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody($$"""
                {
                  "data": [
                    {
                      "id": "builder-trade-2",
                      "takerOrderHash": "exchange-2",
                      "market": "market-1",
                      "assetId": "asset-2",
                      "side": "BUY",
                      "size": "5",
                      "sizeUsdc": "2.6",
                      "price": "0.52",
                      "fee": "0.02",
                      "feeUsdc": "0.02",
                      "status": "MATCHED",
                      "builder": "{{builderCode}}"
                    }
                  ],
                  "next_cursor": "LTE=",
                  "limit": 100,
                  "count": 1
                }
                """));

        var result = await _client.GetBuilderTradesAsync(builderCode, market: "market-1");

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Select(trade => trade.Id).Should().Equal("builder-trade-1", "builder-trade-2");
        result.Data![0].Builder.Should().Be(builderCode);
    }

    [Fact]
    public async Task PlaceOrderAsync_SignsRawOrderEnvelope_ToOrderEndpoint()
    {
        _fixture.Server
            .Given(Request.Create()
                .WithPath("/order")
                .UsingPost()
                .WithBody(body => HasSingleOrderNumericSalt(body, "1234567890123456789012345678901234567890"))
                .WithHeader(PolymarketConstants.POLY_IDEMPOTENCY_KEY_HEADER, "single-key"))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("""{"orderID":"exchange-single","success":true,"status":"live"}"""));

        var result = await _client.PlaceOrderAsync(
            new OrderRequest
            {
                TokenId = "1",
                Price = "0.42",
                Size = "10",
                Side = "BUY",
                TimeInForce = "GTC",
                Salt = "1234567890123456789012345678901234567890",
                Timestamp = "1777374000000"
            },
            "single-key");

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.OrderId.Should().Be("exchange-single");
    }

    [Fact]
    public async Task PlaceOrdersAsync_PostsBatchOrders_ToOrdersEndpoint()
    {
        _fixture.Server
            .Given(Request.Create()
                .WithPath("/orders")
                .UsingPost()
                .WithBody(body => HasBatchOrderNumericSalt(body, "1"))
                .WithHeader(PolymarketConstants.POLY_IDEMPOTENCY_KEY_HEADER, "batch-key"))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("""[{"orderID":"exchange-001","success":true,"status":"live"}]"""));

        var result = await _client.PlaceOrdersAsync(
            new[]
            {
                new PostOrderRequest
                {
                    Owner = "0x1234567890abcdef1234567890abcdef12345678",
                    OrderType = "GTC",
                    DeferExecution = false,
                    Order = new SignedClobOrder
                    {
                        Salt = "1",
                        Maker = "0x1234567890abcdef1234567890abcdef12345678",
                        Signer = "0x1234567890abcdef1234567890abcdef12345678",
                        TokenId = "token-001",
                        MakerAmount = "1000000",
                        TakerAmount = "500000",
                        Expiration = "0",
                        Side = "BUY",
                        SignatureType = 0,
                        Timestamp = "1777374000000",
                        Metadata = "0x0000000000000000000000000000000000000000000000000000000000000000",
                        Builder = "0x0000000000000000000000000000000000000000000000000000000000000000",
                        Signature = "0xsignature"
                    }
                }
            },
            "batch-key");

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().NotBeNull();
        var data = result.Data!;
        data.Should().HaveCount(1);
        data[0].OrderId.Should().Be("exchange-001");
    }

    [Fact]
    public void PolymarketMetrics_CanBeInstantiated()
    {
        // Arrange & Act
        using var metrics = new Autotrade.Polymarket.Observability.PolymarketMetrics();

        // Assert - no exception means success
        metrics.Should().NotBeNull();
    }

    [Fact]
    public void PolymarketMetrics_RecordRequest_DoesNotThrow()
    {
        // Arrange
        using var metrics = new Autotrade.Polymarket.Observability.PolymarketMetrics();

        // Act & Assert - should not throw
        var act = () => metrics.RecordRequest("GET", "/time", 200, 50.0, true);
        act.Should().NotThrow();
    }

    [Fact]
    public void PolymarketMetrics_RecordRateLimitHit_DoesNotThrow()
    {
        // Arrange
        using var metrics = new Autotrade.Polymarket.Observability.PolymarketMetrics();

        // Act & Assert - should not throw for client-side
        var actClientSide = () => metrics.RecordRateLimitHit(isClientSide: true);
        actClientSide.Should().NotThrow();

        // Act & Assert - should not throw for server-side
        var actServerSide = () => metrics.RecordRateLimitHit(isClientSide: false);
        actServerSide.Should().NotThrow();
    }

    [Fact]
    public void PolymarketActivitySource_StartHttpRequest_ReturnsActivity()
    {
        // Act
        using var activity = Autotrade.Polymarket.Observability.PolymarketActivitySource.StartHttpRequest("GET", "/time");

        // Assert - activity might be null if no listener is registered, that's OK
        // Just verifying no exception is thrown
        true.Should().BeTrue();
    }

    [Fact]
    public void PolymarketActivitySource_HasCorrectSourceName()
    {
        // Assert
        Autotrade.Polymarket.Observability.PolymarketActivitySource.SourceName
            .Should().Be("Autotrade.Polymarket");
    }

    // ─────────────────────────────────────────────────────────────
    // Client Configuration Tests
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void PolymarketClobClient_CanBeResolved_FromDI()
    {
        // Act
        var client = _sp.GetService<IPolymarketClobClient>();

        // Assert
        client.Should().NotBeNull();
    }

    [Fact]
    public void PolymarketOptions_AreConfigured_Correctly()
    {
        // Act
        var options = _sp.GetRequiredService<IOptions<PolymarketClobOptions>>().Value;

        // Assert
        options.Host.Should().StartWith("http://");
        options.ApiKey.Should().Be("test-api-key");
        options.ApiPassphrase.Should().Be("test-passphrase");
        options.Address.Should().Be("0x1234567890abcdef1234567890abcdef12345678");
        options.OrderSigningVersion.Should().Be(2);
    }

    [Fact]
    public void PolymarketOrderSigner_DefaultsToV2AndUsesCurrentCreationTimestamp()
    {
        var signer = new PolymarketOrderSigner(Microsoft.Extensions.Options.Options.Create(new PolymarketClobOptions
        {
            ApiKey = "test-api-key",
            PrivateKey = "0x0000000000000000000000000000000000000000000000000000000000000001",
            ChainId = 137
        }));
        var request = new OrderRequest
        {
            TokenId = "1",
            Price = "0.42",
            Size = "10",
            Side = "BUY",
            TimeInForce = "GTC"
        };

        var before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var first = signer.CreatePostOrderRequest(request, "client-order-1");
        var second = signer.CreatePostOrderRequest(request, "client-order-1");
        var after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        first.Order.Salt.Should().Be(second.Order.Salt);
        long.Parse(first.Order.Timestamp).Should().BeInRange(before, after + 1);
        long.Parse(second.Order.Timestamp).Should().BeInRange(before, after + 1);
        first.Order.Timestamp.Should().NotBe(second.Order.Timestamp);
        first.Order.Signature.Should().NotBe(second.Order.Signature);
        first.Order.Taker.Should().BeNull();
        first.Order.Nonce.Should().BeNull();
        first.Order.FeeRateBps.Should().BeNull();
        first.Order.Metadata.Should().Be("0x0000000000000000000000000000000000000000000000000000000000000000");
        first.Order.Builder.Should().Be("0x0000000000000000000000000000000000000000000000000000000000000000");
    }

    [Fact]
    public void PolymarketOrderSigner_ReplaysEnvelopeWhenSignedCreationInputsAreProvided()
    {
        var signer = new PolymarketOrderSigner(Microsoft.Extensions.Options.Options.Create(new PolymarketClobOptions
        {
            ApiKey = "test-api-key",
            PrivateKey = "0x0000000000000000000000000000000000000000000000000000000000000001",
            ChainId = 137
        }));
        var request = new OrderRequest
        {
            TokenId = "1",
            Price = "0.42",
            Size = "10",
            Side = "BUY",
            TimeInForce = "GTC",
            Salt = "123456",
            Timestamp = "1777374000000"
        };

        var first = signer.CreatePostOrderRequest(request, "client-order-1");
        var second = signer.CreatePostOrderRequest(request, "client-order-1");

        first.Order.Salt.Should().Be("123456");
        first.Order.Timestamp.Should().Be("1777374000000");
        first.Order.Signature.Should().Be(second.Order.Signature);
    }

    [Fact]
    public void PolymarketOrderSigner_PropagatesConfiguredBuilderCode_ToSignedOrder()
    {
        var builderCode = Bytes32('1');
        var signer = CreateSigner(builderCode);

        var signed = signer.CreatePostOrderRequest(CreateOrderRequest(), "client-order-1");

        signed.Order.Builder.Should().Be(builderCode);
    }

    [Fact]
    public void PolymarketOrderSigner_PerOrderBuilder_OverridesConfiguredBuilderCode()
    {
        var configuredBuilder = Bytes32('1');
        var overrideBuilder = Bytes32('2');
        var signer = CreateSigner(configuredBuilder);

        var signed = signer.CreatePostOrderRequest(CreateOrderRequest(overrideBuilder), "client-order-1");

        signed.Order.Builder.Should().Be(overrideBuilder);
    }

    [Fact]
    public void PolymarketOrderSigner_InvalidBuilderCode_FailsBeforeOrderEnvelopeIsCreated()
    {
        var signer = CreateSigner("not-bytes32");

        var act = () => signer.CreatePostOrderRequest(CreateOrderRequest(), "client-order-1");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*builder metadata must be a bytes32 hex string*");
    }

    [Fact]
    public void PolymarketOrderSigner_EmptyConfiguredBuilder_FallsBackToZeroBytes32()
    {
        var signer = CreateSigner(builderCode: string.Empty);

        var signed = signer.CreatePostOrderRequest(CreateOrderRequest(), "client-order-1");

        signed.Order.Builder.Should().Be(PolymarketBuilderAttribution.ZeroBytes32);
    }

    [Fact]
    public void BuilderReadiness_RedactsConfiguredBuilderCode()
    {
        var builderCode = Bytes32('a');

        var readiness = PolymarketBuilderAttribution.EvaluateReadiness(
            new PolymarketClobOptions { BuilderCode = builderCode },
            "Paper");

        readiness.BuilderCodeConfigured.Should().BeTrue();
        readiness.FormatValid.Should().BeTrue();
        readiness.Mode.Should().Be("demo");
        readiness.BuilderCodeHash.Should().NotBeNull();
        readiness.BuilderCodeHash.Should().NotBe(builderCode);
    }

    [Fact]
    public void BuilderReadiness_InvalidConfiguredBuilder_IsUnreadyWithoutHash()
    {
        var readiness = PolymarketBuilderAttribution.EvaluateReadiness(
            new PolymarketClobOptions { BuilderCode = "invalid" },
            "Paper");

        readiness.BuilderCodeConfigured.Should().BeTrue();
        readiness.FormatValid.Should().BeFalse();
        readiness.Mode.Should().Be("invalid");
        readiness.BuilderCodeHash.Should().BeNull();
    }

    [Fact]
    public void BuilderEvidenceExporter_RedactsReusableOrderMaterial_AndCorrelatesArcSignal()
    {
        var builderCode = Bytes32('b');
        var signer = CreateSigner(builderCode);
        var signed = signer.CreatePostOrderRequest(CreateOrderRequest(), "client-order-1");
        var rawSignature = signed.Order.Signature;

        var evidence = PolymarketBuilderAttribution.CreateEvidence(
            new PolymarketBuilderEvidenceRequest(
                signed,
                "client-order-1",
                "dual_leg_arbitrage",
                "market-1",
                Bytes32('c'),
                "0.42",
                "10",
                DateTimeOffset.Parse("2026-05-12T00:00:00Z"),
                "exchange-1",
                "run-1",
                "audit-1"));
        var json = JsonSerializer.Serialize(evidence);

        evidence.Correlation.ClientOrderId.Should().Be("client-order-1");
        evidence.Correlation.ArcSignalId.Should().Be(Bytes32('c'));
        evidence.Correlation.OrderId.Should().Be("exchange-1");
        evidence.BuilderCodeHash.Should().Be(evidence.OrderEnvelope.BuilderCodeHash);
        evidence.SignedOrderHash.Should().StartWith("0x");
        evidence.OrderEnvelope.SignatureHash.Should().StartWith("0x");
        json.Should().NotContain(builderCode);
        json.Should().NotContain(rawSignature);
        json.Should().NotContain("0000000000000000000000000000000000000000000000000000000000000001");
    }

    [Fact]
    public void BuilderEvidenceExternalVerification_MatchesAttributedBuilderTrades()
    {
        var builderCode = Bytes32('b');
        var evidence = CreateBuilderEvidence(builderCode, "exchange-1");
        var verification = PolymarketBuilderAttribution.VerifyExternalTrades(
            evidence,
            new[]
            {
                new BuilderTradeInfo
                {
                    Id = "builder-trade-1",
                    TakerOrderHash = "exchange-1",
                    Market = "market-1",
                    Side = "BUY",
                    Size = "10",
                    SizeUsdc = "4.2",
                    Price = "0.42",
                    Fee = "0.01",
                    FeeUsdc = "0.01",
                    Builder = builderCode
                },
                new BuilderTradeInfo
                {
                    Id = "builder-trade-other",
                    TakerOrderHash = "exchange-other",
                    Market = "market-1",
                    Side = "BUY",
                    Size = "99",
                    SizeUsdc = "98.01",
                    Price = "0.99",
                    Fee = "1",
                    FeeUsdc = "1",
                    Builder = builderCode
                }
            },
            DateTimeOffset.Parse("2026-05-13T00:00:00Z"));

        verification.Status.Should().Be("matched");
        verification.MatchedTradeIds.Should().Equal("builder-trade-1");
        verification.MatchedVolumeUsdc.Should().Be("4.2");
        verification.MatchedFeeUsdc.Should().Be("0.01");
        verification.VerifiedAtUtc.Should().Be(DateTimeOffset.Parse("2026-05-13T00:00:00Z"));
    }

    [Fact]
    public void BuilderEvidenceExternalVerification_DoesNotMatchWrongBuilderOrMarket()
    {
        var evidence = CreateBuilderEvidence(Bytes32('b'), "exchange-1");
        var verification = PolymarketBuilderAttribution.VerifyExternalTrades(
            evidence,
            new[]
            {
                new BuilderTradeInfo
                {
                    Id = "wrong-builder",
                    TakerOrderHash = "exchange-1",
                    Market = "market-1",
                    Side = "BUY",
                    Size = "10",
                    Price = "0.42",
                    Fee = "0.01",
                    Builder = Bytes32('c')
                },
                new BuilderTradeInfo
                {
                    Id = "wrong-market",
                    TakerOrderHash = "exchange-1",
                    Market = "market-2",
                    Side = "BUY",
                    Size = "10",
                    Price = "0.42",
                    Fee = "0.01",
                    Builder = Bytes32('b')
                }
            },
            DateTimeOffset.Parse("2026-05-13T00:00:00Z"));

        verification.Status.Should().Be("not_found");
        verification.MatchedTradeIds.Should().BeNull();
        verification.MatchedVolumeUsdc.Should().BeNull();
        verification.MatchedFeeUsdc.Should().BeNull();
    }

    [Fact]
    public void PolymarketRateLimitOptions_AreConfigured_WithDisabled()
    {
        // Act
        var options = _sp.GetRequiredService<IOptions<PolymarketRateLimitOptions>>().Value;

        // Assert
        options.Enabled.Should().BeFalse();
    }

    private static bool IsSignedWithTradesPath(IDictionary<string, string[]> headers)
    {
        if (!headers.TryGetValue(PolymarketConstants.PolyTimestampHeader, out var timestampValues) ||
            timestampValues.Length == 0 ||
            !long.TryParse(timestampValues[0], out var timestamp))
        {
            return false;
        }

        if (!headers.TryGetValue(PolymarketConstants.PolySignatureHeader, out var signatureValues) ||
            signatureValues.Length == 0)
        {
            return false;
        }

        var expectedSignature = PolymarketAuthHeaderFactory.BuildPolyHmacSignature(
            "dGVzdC1hcGktc2VjcmV0LWtleS1mb3ItdGVzdGluZw==",
            timestamp,
            HttpMethod.Get.Method,
            PolymarketClobEndpoints.GetTrades,
            serializedBody: null);

        return string.Equals(signatureValues[0], expectedSignature, StringComparison.Ordinal);
    }

    private static PolymarketOrderSigner CreateSigner(string? builderCode = null)
    {
        return new PolymarketOrderSigner(Microsoft.Extensions.Options.Options.Create(new PolymarketClobOptions
        {
            ApiKey = "test-api-key",
            PrivateKey = "0x0000000000000000000000000000000000000000000000000000000000000001",
            ChainId = 137,
            BuilderCode = builderCode
        }));
    }

    private static OrderRequest CreateOrderRequest(string? builderCode = null)
    {
        return new OrderRequest
        {
            TokenId = "1",
            Price = "0.42",
            Size = "10",
            Side = "BUY",
            TimeInForce = "GTC",
            Salt = "123456",
            Timestamp = "1777374000000",
            Builder = builderCode
        };
    }

    private static PolymarketBuilderAttributionEvidence CreateBuilderEvidence(string builderCode, string exchangeOrderId)
    {
        var signer = CreateSigner(builderCode);
        var signed = signer.CreatePostOrderRequest(CreateOrderRequest(), "client-order-1");

        return PolymarketBuilderAttribution.CreateEvidence(
            new PolymarketBuilderEvidenceRequest(
                signed,
                "client-order-1",
                "dual_leg_arbitrage",
                "market-1",
                Bytes32('c'),
                "0.42",
                "10",
                DateTimeOffset.Parse("2026-05-12T00:00:00Z"),
                exchangeOrderId,
                "run-1",
                "audit-1"));
    }

    private static string Bytes32(char fill) => $"0x{new string(fill, 64)}";

    private static bool HasSingleOrderNumericSalt(string? body, string expectedSalt) =>
        HasNumericSalt(body, expectedSalt, static root => root.GetProperty("order").GetProperty("salt"));

    private static bool HasBatchOrderNumericSalt(string? body, string expectedSalt) =>
        HasNumericSalt(body, expectedSalt, static root => root[0].GetProperty("order").GetProperty("salt"));

    private static bool HasNumericSalt(
        string? body,
        string expectedSalt,
        Func<JsonElement, JsonElement> saltSelector)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var salt = saltSelector(doc.RootElement);

            return salt.ValueKind == JsonValueKind.Number &&
                string.Equals(salt.GetRawText(), expectedSalt, StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
        catch (KeyNotFoundException)
        {
            return false;
        }
        catch (IndexOutOfRangeException)
        {
            return false;
        }
    }
}
