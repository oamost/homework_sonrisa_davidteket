# Task 04 evidence — normalized event records

## Delivered

| File | Purpose |
| --- | --- |
| `src/api/Events/EventRecord.cs` | Shared normalized record, timestamp basis and anomaly flags |
| `src/api/Events/EventRecordMappers.cs` | `FromNews`, `FromMarket`, `FromDisaster` adapters |
| `src/tests/WorldAlerts.Api.Tests/EventRecordTimestampTests.cs` | 7 timestamp/precedence tests |
| `src/tests/WorldAlerts.Api.Tests/EventRecordMappingTests.cs` | 9 family-mapping tests |

## Record contract

Every family maps to one record carrying: family, source id/name, provider category, event type, region, country, ISO3, event time, publication time, provider update time, local fetch/first-seen time, latitude/longitude, severity + unit, provider item id, provenance URL, metadata bag and anomaly flags.

Timestamps stay separate. `EffectiveTimestampUtc` is deterministic: event time → publication time → fetch time, with `EffectiveTimestampBasis` naming the source. This is **not** an alert-eligibility decision; task 01 owns the 24-hour rule.

Anomaly flags (informational only): `MissingProviderTimes`, `PublishedAfterFetched`, `EventAfterPublished`, `FutureTimestamp` (evaluated against an explicit `nowUtc` for deterministic tests).

## Verification

- `dotnet build src/WorldAlerts.sln --nologo`: 0 warnings, 0 errors.
- `dotnet test src/WorldAlerts.sln --nologo`: **95 passed, 0 failed** (16 new record tests; the rest are the existing suite plus concurrently added coverage tests).
- Tests cover: each family mapping, missing event time, missing publication time, conflicting times, fetch-only fallback, future timestamp, provenance/metadata preservation, null region/severity.

## Sample records (test fixtures)

News:

```json
{
  "family": "News",
  "sourceId": "news-test",
  "sourceName": "Test Feed",
  "category": "world-news",
  "eventTimeUtc": null,
  "publishedTimeUtc": "-1h",
  "fetchedUtc": "now",
  "effectiveTimestampBasis": "PublishedTime",
  "anomalies": "None",
  "sourceItemId": "story-1",
  "metadata": { "nativeId": "story-1", "title": "Test story", "categories": "world,security" }
}
```

Market:

```json
{
  "family": "Market",
  "sourceId": "market-test",
  "sourceName": "Test Venue",
  "region": "global",
  "eventTimeUtc": "-2h",
  "fetchedUtc": "now",
  "effectiveTimestampBasis": "EventTime",
  "metadata": { "instrument": "BTCUSDT", "quoteUnit": "USD" }
}
```

Missing provider times:

```json
{
  "family": "News",
  "eventTimeUtc": null,
  "publishedTimeUtc": null,
  "effectiveTimestampUtc": "fetched time",
  "effectiveTimestampBasis": "FetchedTime",
  "anomalies": "MissingProviderTimes"
}
```

Samples are derived from the mapping tests; no HTTP endpoint consumes `EventRecord` yet, by design.

## Out of scope

Alert eligibility, 24-hour lookback, importance scoring and API wiring. Task 01 fixes the eligibility rule, and tasks 14–18 consume these records.
