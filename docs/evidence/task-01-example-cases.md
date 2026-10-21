# Task 01 example cases

Acceptance fixtures for the decisions in `docs/decisions/2026-10-21-task-01-acceptance-boundaries.md`. They are examples, not universal weights: the human may revise numeric values without changing the rule. Unless stated otherwise, the reference fetch time is **2026-10-21T09:00Z** and tier-1 means a global source such as BBC World, NYT World, Al Jazeera or The Guardian.

Scoring tasks 14–16 must use these cases as named fixtures.

## Importance and eligibility

| Case | Family | Metadata input | Important | Eligible | Tests |
| --- | --- | --- | --- | --- | --- |
| N1 | News | Tier-1 source; headline contains "breaking" and "earthquake"; event 08:40Z, published 08:50Z, fetched 09:00Z | Yes | Yes | D2 news keyword rule; D3 normal window |
| N2 | News | Two independent tier-1 sources on the same story, published 06:10Z and 08:30Z (2h20m apart) | Yes | Yes | D2 two-source 6-hour rule |
| N3 | News | Single specialist source (Nature); no urgency keyword; published 07:00Z | No | Yes | D2 not-important baseline |
| N4 | News | Two tier-1 sources on the same story, published 01:00Z and 08:30Z (7h30m apart) | No | Yes | D2 6-hour window boundary |
| N5 | News | Specialist/trade outlet; headline contains "conflict"; single source; published 06:00Z | No | Yes | D2 tier-1 requirement |
| N6 | News | Single tier-1 source; routine item; no urgency keyword; published 08:00Z | No | Yes | D2 keyword requirement |
| DS1 | Disaster | GDACS Orange alert; no magnitude field; event 07:00Z, published 07:30Z | Yes | Yes | D2 Orange always important |
| DS2 | Disaster | GDACS Green earthquake; magnitude 6.3; event 06:00Z, published 06:20Z | Yes | Yes | D2 magnitude ≥ 6.0 |
| DS3 | Disaster | GDACS Green earthquake; magnitude 5.2; event 06:00Z, published 06:20Z | No | Yes | D2 below magnitude threshold |
| DS4 | Disaster | GDACS Green flood; tsunami flag true; event 05:00Z, published 05:40Z | Yes | Yes | D2 tsunami flag |
| DS5 | Disaster | IFRC epidemic event; severity 2; published 03:00Z | Yes | Yes | D2 IFRC severity ≥ 2 |
| DS6 | Disaster | IFRC flood event; severity 1; published 03:00Z | No | Yes | D2 low severity |
| DS7 | Disaster | GDACS Red alert; magnitude missing; event time missing; published 08:00Z | Yes | Yes | D2 alert level sufficient; D3 published-time fallback |
| MK1 | Market | Crypto +6.2% over 24h; observation 08:00Z (1h old, limit 48h) | Yes | Yes | D2 crypto ≥ 5%; freshness |
| MK2 | Market | Crypto +4.9% over 24h; observation 08:00Z | No | Yes | D2 below crypto threshold |
| MK3 | Market | FX +1.3% daily; observation 2026-10-20T16:00Z (17h old, limit 48h) | Yes | Yes | D2 FX ≥ 1% |
| MK4 | Market | FX +0.8% daily; observation 2026-10-20T16:00Z | No | Yes | D2 below FX threshold |
| MK5 | Market | Index +2.4% daily; observation 2026-10-19T20:00Z (37h old, importance freshness limit 48h) | Yes | No | D2 index ≥ 2% with freshness; D3 observation outside the 24-hour eligibility window |
| MK6 | Market | Crypto +9.0% over 24h; observation 2026-10-18T20:00Z (61h old, limit 48h) | No | No | D2 stale observation overrides large move |
| T1 | News | Tier-1 breaking item; event 2026-10-20T02:00Z (31h old), published 08:00Z, fetched 09:00Z | Yes | Yes | D3 late item delivered normally |
| T2 | News | Tier-1 breaking item; no event or published timestamp from any provider; fetched 09:00Z | Yes | No | D3 missing timestamps never count as new |
| T3 | News | Tier-1 breaking item; published 2026-10-22T10:00Z (future relative to fetch) | n/a (excluded) | No | D3 future timestamps flagged and excluded |
| T4 | News | Tier-1 breaking item; event 2026-10-19T20:00Z, published 2026-10-20T01:00Z (32h old) | Yes | No | D3 outside the 24-hour published window |
| T5 | News | Tier-1 breaking item; event time missing, published 2026-10-20T23:00Z (10h old) | Yes | Yes | D3 precedence event → published |

## Delivery and access

| Case | Scenario | Expected result | Tests |
| --- | --- | --- | --- |
| P1 | 120 eligible events queued at startup | First 50 sent immediately; remainder persisted and paced at ≤ 10 per minute | D4 startup burst cap |
| P2 | 30 pending sends during steady-state operation | No more than 10 notifications in any minute; none dropped | D4 pacing |
| R1 | Email delivery fails continuously | Attempts at +1, +5 and +15 minutes, then marked failed and shown in admin | D5 retry schedule |
| R2 | Process restarts after a send but before acknowledgement | No duplicate notification on restart | D5 no duplicate sends |
| A1 | Unauthenticated request to the admin area | 401 or redirect to login; no operational data returned | D6 admin wall |
| A2 | Admin page with SMTP and Slack secrets configured | Source health, poll status, queue and failed sends visible; secret values never rendered | D6 contents and secret protection |
| C1 | New generic webhook channel added through configuration only | Test-send succeeds with no production-code change or rebuild | D7 config-driven channel |
