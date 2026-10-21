# Task 11 evidence — coverage expansion (other event families)

## Integrated and live-verified

| Family | Source | Live result |
| --- | --- | --- |
| Diplomacy | UN press RSS (`press.un.org`) | 10 items in `/api/news`, family `diplomacy` |
| Diplomacy | US State Department RSS | **failed live poll** (`HttpRequestException`); no items stored, not retried |
| Cyber / critical infrastructure | CISA KEV | 1,754 advisories in `/api/advisories` |
| Disasters (Europe/global) | Copernicus EMS | 268 activations, types flood/wildfire/storm/earthquake/volcanic/mass movement |
| Disasters (seismic) | EMSC | 50 global earthquakes, CC BY 4.0 |
| Volcano | EONET `category=volcanoes` | 32 live volcano events |
| Heat/cold (Germany) | DWD warnings | 0 at snapshot (wind-only payload); filter configured for heat/frost |
| Heat/cold (US) | NWS alerts | 0 at snapshot; US/territories only |
| Landslide | EONET category | Filter exists, no live event at snapshot |

EONET `temperatureExtremes` was removed after the live probe returned non-temperature events; it is not claimed.

## Endpoint

`GET /api/advisories?limit=&source=` returns CISA KEV entries with CVE id, vendor, product, name, `dateAdded`, `dueDate`, known-ransomware flag and provenance. The `Advisories` table is unique on `(SourceId, CveId)`.

## Known gaps (approved limitation)

- No global heat/cold feed; only national warnings (DWD Germany, NWS US).
- No Africa-only hazard feed; African events come from GDACS/IFRC/Copernicus.
- Tsunami/landslide not proven live as standalone feeds.
- State Department feed failed in the app while the research probe returned 200; it is not retried, and UN press currently carries diplomacy coverage.

See `docs/decisions/2026-10-21-coverage-gaps-heat-africa-who.md` and `docs/evidence/task-11-additional-source-discovery.md`.
