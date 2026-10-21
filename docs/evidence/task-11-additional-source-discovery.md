# Additional source discovery (section 5 gaps) — task 11 / task 07 support

Qwen performed six read-only, keyless contacts (one each, no retries). Shell egress was blocked, so probes ran through the fetch client and returned provider status codes. Snapshots are single points in time, not proof of 24-hour history or worldwide completeness.

## Working candidates

| Source | Gap addressed | Evidence | Fields | Notes |
| --- | --- | --- | --- | --- |
| Copernicus EMS Rapid Mapping | drought/heat and Europe coverage | 200 JSON, 268 activations, `limit/offset` paging | name, category, countries, `eventTime`, `activationTime`, `lastUpdate`, WKT centroid | Categories seen: flood, earthquake; heat/drought unconfirmed. Terms page not read. |
| EMSC SeismicPortal FDSN | volcano/landslide/geophysical and Africa/Europe | 200 GeoJSON, documented `start/end` + magnitude filters | event id, time, magnitude, depth, region | License **CC BY 4.0** (stated). No volcano coverage. |
| CISA KEV catalog | critical infrastructure / cyber (task 11) | 200 JSON, 1,754 entries, dated feed | CVE id, vendor/product, `dateAdded`, `dueDate` | US-centric. Not a news or hazard feed. |

## Failed probes — blacklisted, no retry

| URL | Failure | Consequence |
| --- | --- | --- |
| `https://www.tsunami.gov/events/xml/PAAQAtom` | 403 | No tsunami source from this provider |
| `https://news.un.org/en/rss.xml` | 403 | No diplomacy source from this provider |

`https://earthquake.usgs.gov/feeds/geojson?format=geojson&limit=3` returned 404, but that is an invalid query combination invented by the investigator, not a provider refusal: USGS already works through the validated `summary/all_day.geojson` feed used in task 07, so USGS is **not** blacklisted.

### Round 2 (all failed, no new providers adopted)

| Probed URL | Failure | Interpretation |
| --- | --- | --- |
| `ec.europa.eu/commission/presscorner` | 200 landing page only | Reachable, but no API/feed surfaced; unusable as a feed |
| `gdacs.org/developers` | 200 page containing GDACS's own "Error 404" | Wrong docs path; the RSS feed itself works |
| `api.weather.gov/alerts/all/active?event=TSunami` | 404 | Invalid path/parameter, not a refused provider |
| `wpc.ncep.noaa.gov/xml/tw_warning.xml` | 404 | Guessed path, not a refused provider |
| `volcanodatalink.earth/api/gvp/events/...` | transport/connection failure | Host unreachable; not adopted |
| `droughtmonitor.unl.edu/.../wms?...` | 404 | Guessed WMS path, not a refused provider |
| `data.humdata.org/api/v1/datasets/?limit=2` | 404 | Guessed API path, not a refused provider |

No provider is blacklisted from round 2: the 404s came from invented paths, and a blacklist applies when a provider refuses to respond, not when a probe path is wrong. Only the exact probed URLs are excluded from retry.

## Coverage correction: GDACS already covers three "gaps"

The live integration run stored GDACS events of types `wildfire` 217, **`drought` 12**, `flood` 11, `earthquake` 5, `cyclone` 3. `src/api/Disasters/DisasterParser.cs` maps GDACS codes `VO → volcano`, `DR → drought`, `TS → tsunami`. So volcano, drought and tsunami **do not need separate sources**: GDACS is the integrated source for them, and its feed currently has no active volcano/tsunami events to display. A live volcanic or tsunami event is still needed to prove those rows end-to-end; the parser mapping is unit-level evidence only.

## Gaps: round 3 result

| Candidate | Result | Use |
| --- | --- | --- |
| `api.weather.gov/alerts/active?event=Excessive%20Heat%20Warning` | 200 GeoJSON, keyless, `updated` timestamp confirmed; **zero features** at snapshot (late October, plausible) | Heat extremes coverage, **US-only**, format verified but no item sample; adapter must accept an empty feature list |
| `state.gov/rss-feed/press-releases/feed/` | 200 RSS 2.0, keyless, dated items, hourly cadence declared | Diplomacy — adopted |
| `press.un.org/en/rss.xml` | 200 RSS 2.0, keyless, dated global items | Diplomacy — adopted (different host from blacklisted `news.un.org`) |
| `consilium.europa.eu` press RSS | **403 challenge** | Blacklisted per the no-retry rule |
| GDACS `geteventlist/SEARCH` API | **204 empty body** | Unverified; not adopted. GDACS RSS remains the working integration |

## Gaps: round 4 result — filters on already-working providers

| Candidate | Result | Use |
| --- | --- | --- |
| EONET `category=volcanoes` | 200 JSON, **5 live items** (e.g. Nevados del Chillán, Chile, 2026-06-15) | Global volcano coverage — adopted via EONET |
| EONET `category=landslides` | 200 JSON, empty array | Filter exists; no open landslides at probe time |
| `api.weather.gov` tsunami warning filter | 200 GeoJSON, empty features | Filter exists; **US/territories only** |
| `api.weather.gov` Heat Advisory filter | 200 GeoJSON, empty features | Filter exists; **US/territories only** |
| Smithsonian GVP weekly RSS | **403** | Blacklisted; EONET covers volcanoes |

EONET is already integrated, so its category filters add coverage by configuration/data selection, not a new provider.

## Gaps: round 5 result — national feeds only, no global/Africa winner

| Candidate | Result | Verdict |
| --- | --- | --- |
| EONET `category=temperatureExtremes` | 200 JSON, but the returned items were severe storms/wildfires, not temperature extremes | Filter **unverified**; cannot be counted as heat/cold coverage |
| DWD `warnapp/json/warnings.json` | 200 JSON, live German official warnings (e.g. wind gusts, Helgoland) | Works, **Germany only**; usable as regional heat/cold coverage if adopted |
| ICPAC `feed/` | 404 | Failed; site reachable, feed path absent. Not a provider refusal |
| Africa CDC `feed/` | 200 RSS with **zero items**, lastBuildDate 2026-10-19 | Valid but currently empty; adds no events |
| WHO outbreak OData API | 200 JSON, newest sampled item **2006** | Also stale; not a replacement for the 2014 feed |
| BOM warnings XML | 404 | Failed; path guess |
| GDACS tsunami list | 400 "Eventtype is required" | Query-contract error, not an empty success |

## Gaps after round 5

1. **Global heat/cold** — no keyless global feed exists among the probed providers; only national met services (NWS US, DWD Germany) publish actionable warnings. A worldwide claim would be false.
2. **Africa-specific hazards** — ICPAC feed absent, Africa CDC feed empty, no other Africa-only source secured. GDACS remains the global source that includes African events.
3. **Tsunami** — GDACS maps `TS` and NWS exposes a US-only filter; no global live example.
4. **Landslide** — EONET filter exists, no live event seen.
5. **WHO disease feed is effectively dead** (2014–2021 items; alternate API newest 2006) and must not be treated as a current-news source.



Candidate directions if a further round is needed: PTWC/JMA tsunami feeds, Smithsonian GVP weekly reports, an Africa-specific disaster authority feed, and a global heat/drought index feed.


## Integration status

Adapters for Copernicus EMS, EMSC and CISA KEV are **not yet written**. They are queued behind the market/disaster integration agent, which owns `src/api/Program.cs`; wiring them in parallel would race on DI registration. Terms/attribution for all three remain unread and must be checked before user-facing display.
