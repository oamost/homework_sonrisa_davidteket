# Task 08 evidence — disaster integration

## Live run (fresh SQLite, all pollers enabled)

| Source | Rows stored | Types present | Region notes |
| --- | --- | --- | --- |
| GDACS RSS | 248 | earthquake, wildfire, flood, cyclone (VO/DR/TS mapped) | Global; sample Africa-heavy (AUS 72, MOZ 29, ZMB 20) |
| EONET (category merge) | 32 | volcanoes | Global point events |
| USGS | 212 | earthquake, explosion | Global coordinates, no country field |
| IFRC GO | 50 | flood, epidemic, cyclone, drought, volcano, landslide, movement | Global with ISO3 |
| Copernicus EMS | 268 | flood, wildfire, storm, earthquake, volcanic activity, mass movement, other | Europe-weighted, global activations |
| EMSC | 50 | earthquake | Global seismic, CC BY 4.0 |
| DWD warnings | 0 | — | Germany heat/cold filter; live payload had only wind warnings |
| NWS alerts | 0 | — | US/territories heat/cold/tsunami filters; empty at snapshot |

WHO DON was removed after proving stale (items 2014–2021; alternate API newest 2006). IFRC GO and health/diplomacy news remain.

## Normalization and known defects

- All sources carry source id, native id, event type, event time when supplied, publication/update time, coordinates when supplied, severity/alert level and provenance.
- IFRC rows have `eventTimeUtc: null`; publication time is stored and used for ordering/eligibility.
- IFRC `eventType` still arrives with source-native casing (`Flood`, `Pluvial/Flash Flood`) and is not normalized to the lowercase family values used by GDACS/EONET. This is a known follow-up defect, not a hidden success.
- DWD and NWS empty responses are treated as successful zero-event polls, not failures.
- USGS/EONET rows have no country/ISO3; region analysis must use coordinates.

## Verification

- Parser, store, ingestion and endpoint tests are in `src/tests/WorldAlerts.Api.Tests/` (`Disaster*`, `SourceCoverage*`, `WorldDataEndpointTests`).
- Live row counts above come from `docs/evidence/task-08-10-live-run.log` and a direct SQLite count.
- The human reviewed the disaster samples and marked the task DONE.
