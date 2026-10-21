# Decision: deterministic market importance scoring

Date: 2026-10-21. Scope: Deliverable 3 task 15. This note records how the market
scorer applies the approved task-01 rule, what metadata is actually available,
which edge cases are handled, and what shared-contract changes are proposed but
**not** applied.

## Approved rule mapping

The only rule applied is D2 in
`docs/decisions/2026-10-21-task-01-acceptance-boundaries.md`:

| Family | Absolute move over the observation interval | Approved fixture interval | Freshness |
| --- | --- | --- | --- |
| Crypto | ≥ 5% | 24 hours | age < 2 × interval |
| FX | ≥ 1% | daily (24 hours) | age < 2 × interval |
| Indices | ≥ 2% | daily (24 hours) | age < 2 × interval |

The numeric values are acceptance fixtures, not universal weights. No
instrument-specific threshold is added. The scorer is metadata-only, reads no
clock, and returns an `ImportanceDecision` whose `Score` is exactly `1.0` or
`0.0`:

- `market.move-threshold` — the move met the configured family threshold and
  the observation is fresh.
- `market.below-threshold` — the move did not meet the threshold.
- `market.stale-observation` — the observation is at least twice its interval
  old, or is in the future.
- `market.missing-baseline` — no previous value was supplied.
- `market.invalid-baseline` — the baseline is zero or non-finite.
- `market.invalid-interval` — the observation interval is zero, negative,
  or differs from the approved family move horizon.

## Input and output shape

`MarketImportanceInput` carries:

- `Family`: `Crypto`, `Fx` or `Index`.
- `CurrentValue`: the stored observation value.
- `BaselineValue`: the previous value for the move horizon, or `null`.
- `ObservationInterval`: the **move horizon**, not the provider sampling
  cadence.
- `ObservationUtc`: provider time from the stored observation.
- `NowUtc`: the caller's evaluation time.

`MarketObservationMapping` is the adapter from the stored
`MarketObservation`:

- `FamilyForSourceKind(kind)` maps the six sanctioned configured kinds to the
  three families; it does not guess from instrument or venue.
- `TryParseInterval(label)` parses `24h`, `1h`, `1d`, `daily`, and generic
  `Nh`/`Nd` labels. `quote` has no fixed duration and returns false.
- `ApprovedIntervalFor(family)` returns the approved 24-hour fixture.
- `Create(family, observation, baselineValue, nowUtc, observationInterval?)`
  uses the observation value/time and an explicit or parsed horizon. When the
  label is `quote` and no horizon is supplied, it passes `TimeSpan.Zero` so the
  scorer returns `market.invalid-interval` instead of guessing a daily horizon.

## Metadata actually available

| Source (configured kind) | Family | Current value | Baseline / history | Interval label | Observation time | Notes |
| --- | --- | --- | --- | --- | --- | --- |
| Binance 24h ticker (`binance-ticker`) | Crypto | Yes (`lastPrice`) | No. The payload has `openPrice`/`priceChange`, but the parser persists only the last price. | `24h` | Ticker `closeTime` | Real-time rolling 24h ticker. |
| Binance hourly klines (`binance-klines`) | Crypto | Yes (candle close) | No in one row. The poll stores 25 hourly candles, enough for a 24h baseline if the caller queries the series. | `1h` | Candle open time | A 1h row is a 1h observation, not the approved 24h fixture. |
| Frankfurter / ECB (`frankfurter`) | FX | Yes (reference rate) | No. Only the latest reference is parsed. | `1d` | Reference date at 00:00Z | Business-day cadence; a date marker can look hours old immediately. |
| Cboe delayed `^SPX` (`cboe`) | Indices | Yes (`last_trade_price`/`close`) | No. `price_change` is not persisted. | `quote` | `last_trade_time`/`updated`/`quote_time`/`timestamp` | Delayed single-venue quote; no fixed horizon. |
| TWSE EOD (`twse`) | Indices | Yes (`ClosingPrice`) | No. Only the current EOD close is parsed. | `1d` | ROC trading date marker at 00:00Z, else Taipei fetch date | Single-venue, end-of-day, allowlisted instrument only. |
| MOEX ISS (`moex`) | Indices | Yes (`CURRENTVALUE`/`LASTVALUE`) | No. `LASTVALUE` is not persisted as a baseline. | `quote` | Exchange `UPDATEDATE`/`UPDATETIME` or `SYSTIME` | Single-venue snapshot; may be delayed. |

`FetchedUtc`, `Delayed`, `DelaySemantics`, `QuoteUnit` and `Provenance` are
stored and exposed, but the approved rule uses only the value, baseline,
interval and observation time. They are not converted into hidden weights.

Common missing metadata:

- **Baseline/history:** `MarketObservation` has no previous value.
  `MarketStore.GetRecentAsync` returns newest-first rows with an optional exact
  source filter, but there is no per-instrument history query. The caller must
  supply `BaselineValue`; otherwise the decision is explicitly
  `market.missing-baseline`.
- **Evaluation time:** not stored; `NowUtc` is always caller-supplied.
- **Family:** not stored; the configured source kind is needed for the mapping.
- **Move horizon for `quote`:** not supplied by the provider; the caller must
  pass an explicit horizon when it has a matching baseline.

The metadata gaps are documented rather than filled by inference. The market
scope remains the human-approved keyless crypto + FX coverage plus the three
single-venue delayed/EOD index supplements; scoring does not claim worldwide
index coverage.

## Edge cases

- **Inclusive threshold:** a move exactly equal to 5%, 1% or 2% is important
  (`>=`).
- **No rounding before comparison:** the raw percentage is compared, so
  `4.999999%` is below 5% even though a display rounding would show 5.
  Display uses up to 12 decimal places.
- **Floating-point boundary:** representable market values use their round-trip
  double text parsed as decimal before comparison (a direct cast loses digits):
  `0.101` vs `0.1` meets exactly 1%,
  but `104.9999999999999` vs `100` does not meet 5%. Extreme finite doubles
  outside decimal precision/range use a strict floating-point fallback; no
  arbitrary near-threshold tolerance rounds a below-threshold move upward.
- **Freshness:** an age exactly equal to twice the interval is stale; one tick
  less is fresh. A future observation or a default/missing observation or
  evaluation timestamp is not fresh (`market.stale-observation`) with an
  explicit factor.
- **Invalid interval:** zero and negative intervals return
  `market.invalid-interval`.
- **Missing/zero baseline:** missing returns `market.missing-baseline`; zero
  returns `market.invalid-baseline`. A negative baseline is allowed and its
  absolute value is the denominator, with a factor saying so. If the baseline
  is missing and the supplied positive interval also differs from the fixture,
  factors explain both missing prerequisites.
- **Non-positive current:** zero and negative current values are used as
  supplied; the absolute move is computed and a factor says the value is
  non-positive. This keeps the approved rule purely a percentage-move rule and
  does not invent a data-quality threshold.
- **Non-finite values:** a non-finite baseline returns
  `market.invalid-baseline`; a non-finite current returns a conservative
  not-important decision with an explicit factor. The shared contract has no
  dedicated rule id for the latter (see proposals).
- **Interval mismatch:** when the supplied horizon differs from the configured
  family fixture, return `market.invalid-interval` with an explicit factor;
  do not use an approved daily/24h threshold to classify a 1h candle.
- **Threshold configuration:** the constructor rejects non-finite or
  non-positive percentages, non-positive intervals, duplicate families and a
  missing family. Defaults are exposed through a read-only dictionary.

## Mapping the real observation intervals

| Configured kind | Family | Parsed horizon | Scoring consequence |
| --- | --- | --- | --- |
| `binance-ticker` | Crypto | `24h` | Matches the approved crypto fixture directly. |
| `binance-klines` | Crypto | `1h` | Not scoreable against the approved 24h fixture directly. To use it, the caller must supply a 24h baseline and `TimeSpan.FromHours(24)`. |
| `frankfurter` | FX | `1d` → 24h | Matches the approved FX fixture; reference-date freshness is business-day cadence. |
| `cboe` | Indices | `quote` → no horizon | Not scoreable until the caller supplies a horizon and baseline. |
| `twse` | Indices | `1d` → 24h | Matches the approved index fixture; EOD date marker freshness. |
| `moex` | Indices | `quote` → no horizon | Not scoreable until the caller supplies a horizon and baseline. |

The `quote` sources are deliberately not assigned a guessed daily horizon: a
quote is a point-in-time value, and only the caller knows which baseline
horizon it has.

## Proposed shared-contract changes (not applied)

1. **`market.invalid-value` rule id.** Add a stable id for non-finite current
   or baseline values so a non-finite current does not have to reuse
   `market.below-threshold`. This task uses the frozen ids only.
2. **Baseline history API.** Add a `MarketStore` query for the latest
   observation of one `(sourceId, instrument, interval)` before a given time,
   or persist a provider baseline such as Binance `openPrice` / Cboe
   `price_change`. This would let callers supply a real baseline without
   inventing one.
3. **Source family in stored metadata.** Add the configured family (or source
   kind) to the observation or expose it from a source-metadata endpoint, so
   scoring does not need a separate configuration lookup to map a row.
4. **Typed interval metadata.** Replace the free-form `Interval` string with a
   typed duration plus a `quote` marker, avoiding repeated parsing and making
   the missing-horizon case explicit.

These are proposals only. No shared file was edited.

## Deviations and limitations

- The 1h Binance kline source is mapped to the crypto family but is not the
  approved 24h crypto fixture. The scorer returns `market.invalid-interval` for
  the supplied 1h horizon; it does not classify a 1h move with a 24h threshold.
- `quote` sources cannot be scored without a caller-supplied horizon. This is
  an explicit missing-metadata decision, not a hidden default.
- Non-finite current values reuse `market.below-threshold` because the shared
  rule-id list is frozen.
- Index scoring covers only the three single-venue, delayed/EOD supplements
  approved in `docs/decisions/2026-10-21-market-scope-narrowing.md`; it does
  not imply worldwide index coverage.

## Verification / remaining integration

- Before the market fix, filtered scorer tests reproduced five expected
  failures: 1h threshold incompatibility, three below-threshold precision
  cases, and an explicitly identified missing timestamp. The later combined
  baseline/interval explanation test also failed before its implementation.
- `MarketObservationMappingTests.cs` now asserts a 1h kline is unscorable
  against the approved 24h crypto fixture. Its ticker test explicitly supplies
  a 24h baseline and checks a qualifying 24h move. Neither test derives a
  24h baseline from an hourly row.
- Concurrent Dedupe edits briefly caused eleven `CS1501` test-build errors;
  that blocker cleared without market edits to Dedupe files.
- An intermediate focused run exposed two precision errors (68 passed,
  2 failed). Round-trip parsing fixed those: the subsequent focused run
  passed 70/70, before the combined-factor change. The final focused rerun
  failed during compilation with `MSB4018`: another process had locked
  `obj/Debug/net10.0/MvcTestingAppManifest.json`. No final test result or
  full-suite pass is claimed; rerun focused and full tests when concurrent
  builds have settled.
- Shared IDs cannot distinguish a non-finite current from below-threshold,
  nor unsupported interval from a nonpositive interval. Distinct IDs are
  proposed cross-family/market contract changes; none were applied here.
