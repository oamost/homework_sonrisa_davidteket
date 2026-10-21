# Task 17 — runtime selection preview (fixture-driven evidence)

`GET /api/alerts/selection` resolves the registered `AlertSelectionService`, reads persisted SQLite news/market/disaster observations and enabled subscriptions, and returns a read-only snapshot. `TimeProvider` supplies the evaluation instant. It does not fetch providers, send, queue, pace, or mark delivered. It does not expose subscription destinations.

## Fixture request and representative output

The `RuntimeSelectionTests` use a real `WebApplicationFactory<Program>`, a temporary SQLite file, and an injected clock at `2026-10-21T12:00:00Z`; polling is disabled. After inserting a BBC breaking-conflict item at 10:00Z, a BBC arts item at 11:00Z, a GDACS magnitude-6.4 earthquake, and matching email/Slack subscriptions, they issue:

```http
GET /api/alerts/selection
```

Representative response fields (illustrative excerpt **from seeded fixtures only**, not a live-provider claim):

```json
{
  "evaluatedUtc": "2026-10-21T12:00:00+00:00",
  "events": [
    {
      "family": "News", "sourceId": "bbc-world", "title": "Breaking conflict",
      "summary": "Provider summary", "link": "https://example.org/breaking",
      "eventTypes": ["world-news"],
      "eligibility": { "status": "Eligible", "isEligible": true },
      "importance": { "isImportant": true, "ruleId": "news.tier-1-urgency-keyword" },
      "matches": [{ "eventType": "world-news", "channel": "email" }], "selected": true
    },
    {
      "family": "News", "title": "Arts report", "selected": false,
      "importance": { "isImportant": false, "ruleId": "news.no-approved-trigger" }
    }
  ],
  "omittedRejectedCount": 0
}
```

The actual response includes provider provenance, provider timestamps, full scorer factors, eligibility timestamp, match reasons, additional seeded disaster and excluded news rows, and `limitations`. The fixture asserts the exact endpoint returns selected news and the correctly typed disaster, while unimportant, old, future, missing-provider-time, disabled-subscription, and absent-baseline cases do not select. The index regression uses an exact same-instrument/source daily baseline: +2.4% at age 37h is **important** under the scorer's strict 48h freshness rule yet **ineligible** under the separate 24h selection window.

## Explicit bounds and limitations

- All persisted family rows are evaluated (not the public 200-row feed cap); all selected items are shown. The response includes at most 100 non-selected items and an exact `omittedRejectedCount`. A fixture with 210 newer unimportant stories demonstrates that the 211th eligible important story still selects.
- Baselines require an exact 24-hour-old stored value with the same source ID, instrument, quote unit and interval label. No interpolation, cross-source comparison or conversion is invented. A 1h candle does not use a 24h percentage threshold. Incomplete/sparse historical data returns explicit missing-baseline/interval factors and can cause a genuinely consequential market move to remain unselected.
- No stored verified same-story relationship is available to this selector; independent-source corroboration is not inferred from similar titles, same organization, or coincident publication times. Only the tier-1 headline urgency rule currently promotes news. Specific news-topic subscriptions require an exact approved taxonomy code in stored feed family/categories; broad technology inference from source alone is not supported.
- No publisher-wide completeness promise: heat/cold coverage regional, Africa-only feed absent, standalone live tsunami/landslide feeds unproven, WHO feed dropped. Preview is not delivery; startup burst (up to 50) then 10/min, persisted queue/retries, delivered-state deduplication, admin access, and actual email/Slack sends remain outside this task.

## Verification

See serialized full-suite test output in the subagent report; this document records fixture-driven proof only, not a claim of live end-to-end alert delivery or human acceptance.
