# Source-quality remediation — bleeding issues

The human rejected the first coverage matrix as containing critical limitations: no important news event from the last 24 hours in human-readable form, and sources that are obsolete or technically unfitting. This record tracks the issues one by one. A source is only "fixed" when a live probe shows recent, relevant items, the API exposes readable fields, tests cover the parser/filter, and the limitation is either removed or explicitly labelled.

## Freshness audit (fresh SQLite, 2026-10-21 ~02:35 UTC)

| Family | Source | Rows | In last 24h | Newest item | Verdict |
| --- | --- | --- | --- | --- | --- |
| News | BBC / NYT / Al Jazeera / Guardian | 35/53/25/45 | 23/29/25/30 | 01:22–02:08 UTC | Fresh, but general feeds only |
| News | Ars Technica | 20 | 17 | 2026-10-20 22:04 | Fresh tech |
| News | Nature | 75 | 14 | 2026-10-20 00:00 | Daily science cadence, not breaking |
| News | UN press | 10 | 9 | 2026-10-20 23:55 | Fresh diplomacy |
| News | State Dept | 0 | 0 | — | Live poll failed; not retried |
| Disaster | GDACS | 248 | 79 | 2026-10-20 23:21 | Fresh but archive rows exist |
| Disaster | USGS | 213 | 213 | 2026-10-21 02:30 | Fresh |
| Disaster | EMSC | 50 | 50 | 2026-10-21 02:10 | Fresh |
| Disaster | IFRC GO | 50 | 1 | 2026-10-20 11:08 | Mostly old; no event time |
| Disaster | Copernicus EMS | 268 | 0 | 2026-10-19 16:03 | Archive activations back to 2003 |
| Disaster | EONET | 32 | 0 | 2026-06-15 | Open volcanoes, obsolete for 24h |
| Disaster | DWD / NWS | 0/0 | 0/0 | — | Valid empty, regional only |
| Advisory | CISA KEV | 1,754 | 6 added | 2026-10-20 | Vulnerability catalog, not an incident feed |
| Market | Binance / Cboe / Frankfurter / MOEX / TWSE | 30 | fresh/daily | — | Approved narrow scope; single-venue/delayed limitations |

## Issues to eliminate or explicitly bound

1. **News has no human-readable summary and no topic-specific coverage.** Titles are readable but there is no description/summary; conflict, politics, cyber, AI, acquisitions, critical infrastructure, environment/energy and science rely on generic world/tech feeds.
2. **EONET volcanoes are obsolete for alerts.** Open volcano events can have event dates months old; they must not be presented as last-24h events. Replace or filter with a live volcano notice source.
3. **Copernicus EMS is an activation archive.** It returns historical activations; only recent or materially updated activations are useful. Add a documented recency filter or replace it as a live feed.
4. **IFRC GO is mostly stale and lacks event time.** Only 1 of 50 rows is in the last 24h and the provider does not supply an event timestamp. Filter on its update/creation field or replace it for live humanitarian alerts.
5. **Heat/cold is regional only.** DWD (Germany) and NWS (US) are official but empty at snapshot; worldwide heat/cold is not covered. Expand to other national warning feeds where keyless, and keep the limitation visible.
6. **Tsunami and landslide are not proven live.** GDACS flags and NWS tsunami warnings are not a dedicated live tsunami feed; landslide has no dedicated live source.
7. **CISA KEV is technically unfitting as an event feed.** It is a vulnerability catalog; keep it only as an advisory lookup or replace/augment it with a dated cyber incident/advisory feed.
8. **State Department feed failed live.** Not retried; UN press currently covers diplomacy. Record, do not hide.
9. **Market sources remain single-venue/delayed/EOD.** This was the human-approved narrow scope, not a silent claim of worldwide coverage; improve only if a documented keyless source is found.

## Working rules

- Do not probe blacklisted providers: GDELT, ReliefWeb, Meteoalarm, Stooq, Yahoo Finance, Nasdaq API, StockAnalysis, tsunami.gov, news.un.org, consilium.europa.eu, volcano.si.edu; do not retry `www.state.gov`.
- A refusal is final for that provider: record it, do not retry, find an alternative.
- A successful one-off probe does not prove 24-hour or worldwide coverage; verify newest item age and fields.
- Prefer official/keyless feeds with publication times and attribution; no keyed services.

## Research findings (2026-10-21)

- **News:** The Record (cyber/critical infrastructure), TechCrunch AI (AI/acquisitions) and Guardian Environment (environment/energy) are fresh, expose title + time + description, and are selected. AP News returned 403 (blacklisted). CISA `news.xml` is 13 days stale and has empty summaries; CISA `cybersecurity-advisories/all.xml` is dated/summarized but weekly. KEV stays an advisory lookup only; The Record is the event feed.
- **GDACS:** filter on `gdacs:iscurrent` and `gdacs:datemodified`; current feed is fresh (newest ~3.6h). GDACS also exposes `gdacs:calculationtype` (e.g. `tsunami`), so a tsunami flag can be derived from official metadata instead of left null; this needs a parser change.
- **EONET:** live source, but open volcano records can be months old. Filter on the latest `geometry[].date`; a zero-result volcano poll is valid.
- **IFRC:** add `?ordering=-created_at`; live recent rows expose numeric `ifrc_severity_level` (often 0), `disaster_start_date`, `created_at` and `updated_at`. Map `disaster_start_date` to event time; filter on recent `created_at`/`updated_at`.
- **Copernicus EMS:** keep open activations or recent `lastUpdate`; drop closed historical activations.
- **Heat/cold:** ECCC Canada weather alerts (38 alerts, newest ~3h) is the best new regional feed; UK Met Office is live but has no item `pubDate`; NWS US and DWD Germany remain. No global heat/cold source.
- **Tsunami:** JMA extra feed is live (~7 min) but Japan/NW-Pacific only; no global keyless feed. BOM returned 403 (blacklisted).
- **Volcano:** USGS VNS `rss.php` returned 404 and is email-only; GeoNet NZ provides alert levels without timestamps (change detection only); no global live volcano feed. EONET filtering plus GeoNet regional is the honest maximum.
- **Landslide:** EONET landslides returned zero open events; Copernicus mass-movement remains the only signal.

## Status

| Issue | Owner | Status |
| --- | --- | --- |
| 1. News summaries/topics | news integration worker | WIP |
| 2. EONET obsolete | disaster filter worker | WIP |
| 3. Copernicus archive | disaster filter worker | WIP |
| 4. IFRC stale/no event time | disaster filter worker | WIP |
| 5. Heat/cold regional | follow-up integration (ECCC/UK) | TODO |
| 6. Tsunami/landslide | follow-up integration (JMA/EMS) | TODO |
| 7. CISA KEV fit | news feed + advisory labelling | WIP |
| 8. State Dept failure | documented | OPEN (limitation) |
| 9. Market narrow scope | documented | OPEN (approved limitation) |
