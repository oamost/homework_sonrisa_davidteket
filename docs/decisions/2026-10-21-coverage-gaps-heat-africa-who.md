# Decision: heat/cold, Africa coverage, and the dead WHO feed

Date: 2026-10-21. Human-approved after research rounds 1–5.

## Context

Five keyless discovery rounds produced no global heat/cold event feed and no live Africa-only hazard feed:

- EONET `category=temperatureExtremes` returned non-temperature events, so the filter is unverified.
- National met services publish actionable heat/cold warnings, but only for their own country (NWS US, DWD Germany confirmed).
- ICPAC's feed path returned 404; Africa CDC's RSS is valid but currently empty; BOM's guessed XML path returned 404.
- The WHO disease-outbreak feed is effectively dead: stored items are 2014–2021, and WHO's OData outbreak API sampled back to 2006.

## Decisions

1. **Heat/cold coverage is regional, not global.** Integrate official national warnings (DWD Germany, NWS United States) and label them by country. Do not claim worldwide heat/cold coverage.
2. **Africa coverage comes from global sources.** GDACS (which already stores African events) remains the source; no Africa-only feed is claimed until one verifies live.
3. **No further probing of failed paths.** ICPAC, BOM, and similar guessed endpoints are not retried. Provider refusals already blacklisted stay blacklisted.
4. **Drop WHO DON as a current-event source.** Keep IFRC GO and health-related news for public-health coverage. Document the removal; do not silently present 2014 items as current events.
5. **Eligibility stays a task-01 decision.** These changes do not define the 24-hour rule; task 04's timestamp basis and task 18's lookback will use it.

## Consequences

- `docs/evidence/task-11-additional-source-discovery.md` records the failures and the regional scope.
- `docs/requirement-coverage.md` states the heat/cold and Africa limitations instead of implying worldwide coverage.
- The integration adds DWD and NWS sources, removes WHO DON, and keeps per-source labels honest (country/region).
