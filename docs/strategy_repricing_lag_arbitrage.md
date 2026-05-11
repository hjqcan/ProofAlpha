# Repricing Lag Arbitrage Strategy

Status: implemented as disabled-by-default paper/live strategy wiring.

## Scope

The first implementation targets Polymarket crypto 15m Up/Down binary markets whose slugs match:

```text
{asset}-updown-15m-{unix_window_start_seconds}
```

Supported assets map to RTDS spot symbols:

| Slug asset | Spot symbol |
| --- | --- |
| btc | BTCUSDT |
| eth | ETHUSDT |
| sol | SOLUSDT |
| xrp | XRPUSDT |

The parser treats the slug timestamp as the UTC window start and adds 15 minutes for the window end. Outcome token mapping is positional: token 0 is YES/UP, token 1 is NO/DOWN.

## Source Alignment

The strategy requires a `MarketWindowSpec` for every candidate market. The default settlement oracle label is:

```text
polymarket-rtds-crypto-prices
```

The strategy has two independent safety gates:

- `RequireConfirmedOracle`: when true, the `MarketWindowSpec.OracleStatus` must be `Confirmed`.
- `AllowedSpotSources`: latest and baseline spot ticks must come from configured RTDS sources.

Default app settings keep the strategy stopped and disabled. The sample config sets `RequireConfirmedOracle` to true so a live deployment must explicitly confirm oracle/source alignment before enabling this strategy.

## Example MarketWindowSpec

Queried from Polymarket Gamma API on 2026-05-04 14:46 UTC:

```text
GET https://gamma-api.polymarket.com/events/slug/btc-updown-15m-1777905900
```

Observed event summary:

```text
slug: btc-updown-15m-1777905900
title: Bitcoin Up or Down - May 4, 10:45AM-11:00AM ET
active: true
closed: false
endDate: 2026-05-04T15:00:00Z
markets: 1
```

Parsed `MarketWindowSpec`:

```json
{
  "marketSlug": "btc-updown-15m-1777905900",
  "windowType": "CryptoUpDown15m",
  "windowStartUtc": "2026-05-04T14:45:00Z",
  "windowEndUtc": "2026-05-04T15:00:00Z",
  "underlyingSymbol": "BTCUSDT",
  "boundaryPolicy": "StartPriceVersusEndPrice",
  "settlementOracle": "polymarket-rtds-crypto-prices",
  "oracleStatus": "Configured",
  "tokenMap": {
    "yesTokenId": "<market.clobTokenIds[0]>",
    "noTokenId": "<market.clobTokenIds[1]>"
  }
}
```

## Runtime Flow

The strategy state machine is:

```text
Wait -> Confirm -> Signal -> Submit -> Monitor -> Exit
```

Fault conditions move the market state to `Faulted`.

Entry flow:

1. Select active 15m crypto Up/Down markets with parsed window specs.
2. Wait until the configured confirmation time after window start.
3. Read a unified market-data snapshot: market metadata, window spec, latest spot, baseline spot, top-of-book, and depth.
4. Reject stale spot/orderbook data or unaccepted spot sources.
5. Compute spot move in bps and infer the confirmed direction.
6. Estimate fair probability, compare with best ask, and require `edge >= MinEdge`.
7. Ask the risk manager to validate the order.
8. Emit a FOK limit buy signal through the strategy engine/execution service path.

Exit flow:

- Cancel stale entry orders after `MaxOrderAgeSeconds`.
- Exit held positions after window end or `MaxHoldSeconds`.
- Trigger the strategy kill switch on oracle mismatch when configured.

## Replay Format

The deterministic replay runner consumes ordered frames:

```json
{
  "timestampUtc": "2026-05-04T14:46:00Z",
  "spotPrice": 101000.0,
  "yesBid": 0.52,
  "yesAsk": 0.56,
  "noBid": 0.42,
  "noAsk": 0.46
}
```

Replay output includes:

- frame count
- detected signal count
- average edge
- realized win rate against final spot direction

The current harness is intentionally deterministic and unit-tested; recorded dataset ingestion can be layered on the same frame contract without changing live strategy behavior.
