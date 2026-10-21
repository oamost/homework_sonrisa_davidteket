# Decision: task 01 acceptance boundaries

Date: 2026-10-21. Directive: the human reviewed and approved the agent-proposed defaults below for Deliverable 3 task 01 ("Settle blocking acceptance boundaries"). No implementation is approved by this record.

## Context

Task 01 required testable acceptance for importance scoring per family, the event timestamp used for 24-hour eligibility, catch-up pacing, retries, admin access, and what "new channel without production-code change" accepts, while keeping provider gaps explicit. The defaults were proposed by the agent and approved by the human on 2026-10-21. The testable cases are in `docs/evidence/task-01-example-cases.md`.

## D1 — Event-type taxonomy (subscribable codes)

Subscriptions use these codes only. "Verified source" reflects evidence recorded by tasks 05–11, not a promise of worldwide or 24-hour coverage.

| Code | Display name | Family | Verified source |
| --- | --- | --- | --- |
| `world-news` | World news | News | Yes (multiple tier-1 RSS feeds) |
| `conflict` | Armed conflict | News | Yes (news-topic coverage) |
| `politics` | Politics | News | Yes (news-topic coverage) |
| `diplomacy` | Diplomacy | News | Yes — two verified feeds (U.S. State Department, UN press) |
| `disaster.earthquake` | Earthquake | Disaster | Yes (GDACS) |
| `disaster.flood` | Flood | Disaster | Yes (GDACS) |
| `disaster.cyclone` | Cyclone | Disaster | Yes (GDACS/EONET) |
| `disaster.wildfire` | Wildfire | Disaster | Yes (GDACS/EONET) |
| `disaster.volcano` | Volcano | Disaster | Yes (GDACS/EONET) |
| `disaster.drought` | Drought | Disaster | Yes (GDACS) |
| `disaster.tsunami` | Tsunami | Disaster | Yes (flag on GDACS events; live tsunami-only feed unproven) |
| `disaster.landslide` | Landslide | Disaster | Yes (GDACS events; live landslide-only feed unproven) |
| `disaster.epidemic` | Epidemic | Disaster | Yes (IFRC) |
| `market.crypto` | Crypto markets | Market | Yes (Binance) |
| `market.fx` | FX markets | Market | Yes (Frankfurter/ECB) |
| `market.indices` | Equity indices | Market | Yes (Cboe `^SPX`, TWSE, MOEX — single-venue, delayed/EOD) |
| `health.humanitarian` | Humanitarian emergency | Disaster | Yes (IFRC severity) |
| `tech.cyber` | Cyber incidents | News | News-topic coverage |
| `tech.ai` | AI developments | News | News-topic coverage |
| `tech.acquisitions` | Major acquisitions | News | News-topic coverage |
| `tech.critical-infrastructure` | Critical infrastructure | News | News-topic coverage |
| `environment.energy` | Environment and energy | News | News-topic coverage |
| `science.transformative` | Transformative science | News | Yes (Nature); topic filter unproven |

## D2 — Deterministic importance

Importance is rule-based, metadata-only, and applied per family. The rules and numeric values below are **approved acceptance examples, not universal weights**; they are not a claim about real-world editorial importance. Scoring tasks 14–16 must use them as test fixtures.

- **News:** important when a tier-1 global source reports a story with a breaking/conflict/disaster keyword, **or** when two independent tier-1 sources carry the same story within 6 hours. Not important: a single specialist item with no urgency keyword.
- **Disaster:** GDACS Orange/Red alert is always important. A Green earthquake at magnitude ≥ 6.0 is important. Any tsunami flag is important. An epidemic/humanitarian event with IFRC severity ≥ 2 is important. Other green/low-severity events are not important.
- **Markets:** important when the absolute move over the observation interval is ≥ 5% for crypto (24h interval), ≥ 1% for FX (daily), or ≥ 2% for indices (daily), **and** the observation is fresher than twice its interval. Otherwise not important.

## D3 — 24-hour eligibility

Timestamp basis is event time → published time → fetched time, using the basis flag defined by task 04. An item is eligible when its published time falls within the previous 24 hours, even if the event itself is older. Items with no provider timestamps at all never count as new. Future timestamps are flagged and excluded. Items whose event is older than 24 hours but published within the window are delivered normally as late items.

## D4 — Catch-up pacing

On startup, allow one immediate burst of at most 50 notifications. After that burst, pace the remainder and steady-state notifications at no more than 10 notifications per minute. The startup burst is an explicit exception to the steady-state limit. Any remainder is persisted and queued rather than dropped. No quiet hours by default.

## D5 — Retries

Three attempts at 1, 5 and 15 minutes after the first failure, then the send is marked failed and surfaced in the admin view. No infinite retry. Restart must not duplicate an already-sent notification.

## D6 — Admin wall

Local password plus a session cookie. Contents: source health, poll status, delivery queue, and failed sends. Secrets are never displayed. Unauthorized access returns 401 or redirects to the login page.

## D7 — New channel without production code

A config-driven generic webhook: URL, method, headers, JSON body template, and test-send. Adding such a channel requires no code edit. Secret values are stored outside source control.

## Known provider gaps

Regional-only heat/cold coverage, no Africa-only feed, tsunami/landslide not proven live as standalone feeds, and the dropped WHO feed remain. These are documented limitations, not solved coverage, and must stay visible in user-facing documentation.

## Consequences

- Tasks 14–16 adopt these examples as named fixtures; changing a numeric example requires human approval but does not change the rule.
- Tasks 18–22 use D3–D5; the admin task uses D6; channel work uses D7.
- This record does not rewrite the frozen `ctx/goals/plan.md`.

## Review log

- Defaults proposed by the agent as a bounded acceptance draft.
- Reviewed and approved by the human on 2026-10-21.
- Numeric example values are acceptance fixtures and may be revised by the human without changing the underlying rule.
- Closure review: the human directed correction of MK5 to important but ineligible at 37 hours, and approved a one-time startup burst of up to 50 as an exception to the subsequent 10-per-minute limit. Task 01 closes the acceptance decisions only; implementation and runtime verification remain with the corresponding implementation tasks.
