# Task 10 evidence — market integration

## Live run (fresh SQLite)

| Source | Rows | Instrument | Delay semantics |
| --- | --- | --- | --- |
| Binance ticker | 1 | BTCUSDT | real-time 24h rolling ticker |
| Binance klines | 25 | BTCUSDT | real-time hourly candles, 25-point history |
| Cboe delayed | 1 | `^SPX` | delayed last-trade quote |
| Frankfurter/ECB | 1 | EUR/USD | daily reference rate |
| MOEX ISS | 1 | IMOEX | exchange snapshot, may be delayed |
| TWSE EOD | 1 | 0050 | end-of-day, ROC date parsed |

Total 30 observations. TWSE is restricted to a configured single instrument (`0050`); the 19,910-row full listing is discarded at ingestion.

## Behaviour verified

- `GET /api/markets?limit=` clamps 1–200 and orders newest first.
- `GET /api/markets?source=<id>` returns exact-source rows (case-insensitive), so one source cannot hide another.
- Timestamps: `observationUtc` is provider time (or the documented date marker), `fetchedUtc` is local fetch time; delayed flag and delay semantics are exposed per row.
- Stale/conflicting price cases are covered by unit tests (`MarketParserTests`, `MarketStoreTests`, `MarketIngestionTests`).
- TWSE ROC date `1151020` parses to 2026-10-20 rather than the fetch date.

## Verification

- Build 0 warnings/0 errors; market tests green in the full suite.
- Live samples match the human-reviewed API output; the human marked the task DONE.
