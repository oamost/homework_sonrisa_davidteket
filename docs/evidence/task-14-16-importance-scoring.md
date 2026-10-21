# Tasks 14–16 evidence — deterministic importance scoring

## Delivered

`src/api/Scoring/`: `ImportanceDecision`, `ImportanceRuleIds`, `ScoringText`, `NewsSourceTiers`, `NewsImportanceScorer`, `MarketImportanceScorer`, `DisasterImportanceScorer`.

- News: tier-1 source + urgency keyword, or two independent tier-1 sources within 6 hours; tier lists configurable (BBC/NYT/Al Jazeera/Guardian tier-1; Ars Technica/Nature specialist).
- Market: crypto ≥5% / 24h, FX ≥1% / daily, indices ≥2% / daily, inclusive boundaries, freshness < 2× interval; missing/zero baseline returns not-important with a factor.
- Disaster: GDACS Orange/Red; Green/unspecified earthquake ≥6.0; tsunami flag; IFRC severity ≥2; missing fields explicit.
- No clock access inside rules; callers pass `nowUtc`. Decision is deterministic binary 1.0/0.0 because the approved rules define no graded weights.

## Verification

- Tests: `ScoringTestFixtures`, `NewsImportanceScorerTests`, `MarketImportanceScorerTests`, `DisasterImportanceScorerTests` — 45 scoring tests; full suite 190 passed; build 0 warnings.
- Fixtures reproduce the task-01 example cases, including boundary values (exactly 5%/1%/2%, exactly M6.0, stale vs fresh, missing baseline/severity).

## Not yet wired

Scoring is a pure library. Tasks 17–19 will call it from subscription matching and catch-up; no endpoint exposes decisions yet.
