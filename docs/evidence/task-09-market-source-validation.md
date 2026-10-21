# Task 09 — market-source validation (Qwen probe)

Delegated to Qwen by human direction. Read-only probes, at most one GET per endpoint, no retries, no accounts or keys. No blacklisted provider (Stooq, GDELT, ReliefWeb v1, probed Meteoalarm endpoint) was contacted.

| Probe | Result | Freshness | History | Coverage |
| --- | --- | --- | --- | --- |
| `api.binance.com/api/v3/ticker/24hr?symbol=BTCUSDT` | 200, documented, keyless | rolling window ending at fetch time (live) | rolling aggregate only | crypto spot, global venue |
| `api.binance.com/api/v3/klines?symbol=BTCUSDT&interval=1h&limit=25` | 200, documented, keyless | candles through the current hour | **yes — replayable prior-24 h hourly series**, last close matched ticker price | crypto spot |
| `api.frankfurter.app/latest?from=EUR&to=USD` | 200, documented, keyless | one-day cadence (ECB business-day reference), date 2026-10-20 | not probed | one FX pair |
| `query1.finance.yahoo.com/v8/finance/chart/^N225` | 200, **undocumented** | recent session data | 5-day daily series with a non-trading-day gap | one index, no SLA |

## Verdict

Proven dependable coverage is **crypto (24/7, replayable 24 h) plus one daily FX pair**. No *documented* keyless equity/index source was identified or probed; the only index evidence is the undocumented Yahoo response previously flagged in `docs/requirement-coverage.md`. That does not satisfy task 09's requirement to verify documented, dependable keyless signals for the agreed worldwide market scope rather than crypto, daily FX and one undocumented index.

Rights notes: Binance market data is public under its terms; Frankfurter uses ECB official rates and asks for attribution. Rate-limit terms, multi-region instruments, per-venue trading calendars and depth beyond 24 h remain unverified.

## Disposition

Task 09 stays `WIP`: the central equity/index gap is unfilled. The next step needs human direction — keep searching for a documented keyless multi-asset source, or explicitly narrow the market scope. No files or statuses were changed by Qwen.

## Yahoo fallback attempt (human-directed) — failed, blacklisted

The human directed a fallback to Yahoo's undocumented chart endpoint. A bounded probe made exactly one request before the stop rule applied:

- `GET https://query1.finance.yahoo.com/v8/finance/chart/^GSPC?range=5d&interval=1d` → **HTTP 429**, `Content-Type: text/html`, body `Too Many Requests`, no redirect and no data.
- Per the repository rule, probing stopped immediately: the other instruments were not attempted. A first-request 429 indicates egress/IP-level throttling, so the failure is not instrument-specific.
- Yahoo Finance is now recorded in the blacklist table of `docs/requirement-coverage.md` and will not be retried. The earlier single Yahoo sample is excluded as unusable.

## Documented keyless alternatives researched (documentation only, no endpoints called)

| Candidate | Coverage | Keyless | Documented | 24 h history | Constraint |
| --- | --- | --- | --- | --- | --- |
| TWSE OpenAPI | Taiwan-listed equities/indices | yes | portal is a Swagger UI | end-of-day (inferred) | single market, Chinese docs |
| MOEX ISS | Russian shares/indices/FX/futures | yes | yes | candle/date-range endpoints listed | single market, sanctions/terms unread |
| World Bank Pink Sheet | commodities | yes | yes | no — monthly cadence | XLS download |
| Alpha Vantage | global equities/indices/commodities | **no — free key required** | yes | index/intraday are premium | signup + quota; violates the keyless event-source rule |

Verdict from that research: **no documented keyless source offers multi-region equity/index coverage.** Documented keyless breadth is crypto (Binance) plus FX (Frankfurter/ECB); single-venue equity APIs are narrow and mostly end-of-day.

Least-bad options, pending human choice: (1) probe undocumented keyless endpoints for broad equity data (same fragility that just failed with Yahoo); (2) accept a key-required free tier as an explicit exception to the keyless rule; (3) narrow the market scope to documented keyless crypto + FX; (4) supplement with single-venue documented APIs (TWSE, MOEX) at end-of-day cadence.

## Human decision and alternative-endpoint probe

The human chose option 3: **narrow market scope to documented keyless crypto + FX**. The decision record is `docs/decisions/2026-10-21-market-scope-narrowing.md`.

Six keyless endpoint probes were made once each to check whether broad equity coverage existed before closing the gap:

| Endpoint | Result |
| --- | --- |
| `api.nasdaq.com` quote + screener | `curl (92) HTTP/2 stream reset by server` on both; no status, no body — **blacklisted** |
| `cdn-api.cboe.com` delayed `^SPX` quote | 200 JSON, current price + observation timestamp, no history in that response; Cloudflare bot cookies |
| `stockanalysis.com/api/screener/s/f` | 404 `{"status":404}` — **blacklisted** (path may have moved) |
| TWSE OpenAPI | 200 JSON, Taiwan-listed ETFs, single-day end-of-day snapshot |
| MOEX ISS IMOEX | 200 JSON, single index point with timestamps |
| Binance / Frankfurter (already known) | documented, keyless, with 24-hour klines and daily FX |

Cboe, TWSE and MOEX do respond, but they are single-venue, mostly delayed/end-of-day, and their terms were not reviewed; none was adopted because the human narrowed the scope. Nasdaq and StockAnalysis join the blacklist in `docs/requirement-coverage.md`.

## Final task 09 scope

Validated market coverage is now: **Binance crypto (24-hour ticker plus replayable hourly klines) and Frankfurter/ECB daily FX**. Worldwide equity/index and commodity coverage is explicitly out of scope by human decision, not silently dropped. Integration of these two sources is task 10.


