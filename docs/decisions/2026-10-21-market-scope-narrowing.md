# Decision: market scope narrowed to keyless crypto + FX

Date: 2026-10-21. Directive: human instruction after the Yahoo probe failed ("in that case if yahoo fails too, let's go with option 3").

## Context

Task 09 required documented, dependable keyless market signals for a worldwide multi-asset scope, explicitly not just crypto, one daily FX pair and one undocumented index. Validation produced:

- **Working, documented, keyless:** Binance BTCUSDT 24-hour ticker and hourly klines (a replayable prior-24h series), and Frankfurter EUR/USD daily reference (ECB business-day cadence).
- **No documented keyless equity/index breadth:** documentation research found only single-venue APIs (TWSE Taiwan, MOEX Russia, mostly end-of-day), monthly World Bank commodity data, or key-required free tiers.
- **Yahoo Finance blacklisted:** the first scripted request returned HTTP 429 `Too Many Requests` (`text/html`), so it is excluded and not retried.

## Decision

Market coverage is narrowed to **documented keyless crypto + FX**:

- Binance public market data (crypto spot ticker and klines).
- Frankfurter / ECB reference rates (daily FX).

Worldwide equity/index and commodity coverage is **not provided** by this application as currently scoped. This is an explicit, human-approved reduction of the original worldwide market ambition, not a silent gap. A key-required provider was not accepted because event-data sources must remain keyless; only delivery credentials are exempt.

## Consequences

- Task 09's validation target becomes the narrowed scope; the recorded evidence already covers both sources.
- Task 10 (market integration) will ingest Binance and Frankfurter observations only, retaining source provenance, observation timestamps distinct from fetch time, and staleness handling.
- Task 15 (market importance scoring) must work with crypto/FX metadata and must not pretend to judge global equity markets.
- Later pricing work must not describe the product as covering worldwide market movements; the limitation stays visible in the user-facing documentation.
- `ctx/goals/plan.md` remains frozen, as the repository rules require. This decision is a documented approved deviation; the plan is not rewritten unless the human explicitly lifts the freeze.
- Reversal is possible only with a new human decision and fresh source evidence.

## Amendment — single-venue sources added by human direction

On 2026-10-21 the human directed integration of the three probed endpoints that responded during validation: **Cboe delayed `^SPX`**, **TWSE OpenAPI (Taiwan EOD)**, and **MOEX ISS (`IMOEX`)**. They are therefore added to the market scope as limited, single-venue, delayed/end-of-day supplements.

What remains excluded: broad worldwide equity/index coverage and commodities. The product must not claim worldwide market coverage; these three sources cover one US index, one Taiwanese market snapshot and one Russian index, with delayed or daily cadence.

## Sanctioned sources and blacklist

Active market sources: Binance, Frankfurter/ECB. Blacklisted: Stooq (404), Yahoo Finance (429), plus GDELT, ReliefWeb v1 and the probed Meteoalarm endpoint for other families.
