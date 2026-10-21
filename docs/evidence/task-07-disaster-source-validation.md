# Task 07 — disaster-source coverage validation

Read-only probes, one request per endpoint, no retries, no accounts or keys. No blacklisted provider (Yahoo, Stooq, GDELT, ReliefWeb v1, probed Meteoalarm endpoint) was contacted. A DeepSeek investigator ran the probes; the parent recorded the results.

## Probe results

| Source | Result | Types present in snapshot | Timestamps | History |
| --- | --- | --- | --- | --- |
| GDACS RSS (`gdacs.org/xml/rss.xml`) | 200, 248 items | earthquake, wildfire, flood only | `pubDate`/`dateadded`/`datemodified` plus event `fromdate`/`todate` | rolling feed, no 24-hour filter; archive not probed |
| NASA EONET v3 (`status=open&limit=50`) | 200, 50 open events | severe storms, wildfires (including prescribed fires), sea/lake ice | per-point observation dates, no publication time, no severity scale | date filters documented but not probed |
| WHO Disease Outbreak News | **empty body** this run; status unconfirmable, no refusal code | prior evidence only (`docs/requirement-coverage.md`) | — | — |
| IFRC GO events | **empty body** this run; status unconfirmable, no refusal code | prior evidence only | — | — |
| USGS `all_day` GeoJSON | 200, 209 features | earthquakes, magnitude 0.8–5.4 | `time`, `updated`, native 24-hour window | yes, native 24-hour feed |

WHO and IFRC are **inconclusive, not blacklisted**: an empty body is not a refusal, and both returned data in earlier discovery probes. No retry was attempted in this run.

## Field availability

GDACS supplies event id, type, alert level/score, typed severity (magnitude, hectares), population, sometimes `iso3`, point/bbox geometry and a CAP link; `eventname` and `glide` were empty in the sample. EONET supplies category, geometry tracks and magnitude with units but no severity ranking or publication time. USGS supplies magnitude, time, updated, tsunami flag, significance and point geometry.

Minimum normalized fields supported across sources: id, type, title, event time, latitude/longitude, country, severity with unit, source, url. Missing values must stay absent — GDACS already shows blank `iso3`/`glide`.

Attribution: GDACS states public domain / CC BY 4.0. EONET and USGS terms were not read; WHO/IFRC unattempted this run.

## Coverage against the agreed matrix

- **Snapshot-proven:** earthquake (GDACS + USGS), flood (GDACS), cyclone (EONET), wildfire (GDACS and EONET).
- **Taxonomy or prior sample only:** tsunami, drought, heat/cold, volcano, landslide, epidemic/public health.
- **Regional skew:** Pacific, Americas and South-East Asia dominate; Africa, Europe and Central Asia are nearly absent in these snapshots.
- **Overlap requiring deduplication:** earthquakes (GDACS/USGS), US wildfires (EONET and others), cyclones/floods across GDACS and EONET and historically IFRC. Geometry plus time windows are the usable reconciliation keys.

## Gaps and what would close them

1. No verified live source for tsunami, volcano, landslide, drought or temperature extremes in this run.
2. WHO/IFRC need a successful re-sample before their timeliness and completeness can be trusted at all.
3. Africa/Europe/Central Asia coverage is unproven; the current feeds are not enough to claim worldwide major-hazard coverage.
4. 24-hour historical retrieval is proven only for USGS; GDACS's archive and EONET's date filters are documented but unprobed.

## Disposition

Task 07 status: `TESTING` — validation executed and gaps surfaced honestly. The task's instruction to resolve uncovered categories is **not yet satisfied**, so acceptance needs a human decision: accept the documented gaps and integrate the four working sources under task 08, authorise probing of additional keyless hazard providers, or narrow the disaster scope explicitly (which the task says should not be done quietly).
