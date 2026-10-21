# Decision: deterministic disaster importance scoring

Date: 2026-10-21. Scope: Deliverable 3 task 16. This note records the approved rule mapping, the actual metadata available per disaster source, the hardened edge cases, and proposed shared-contract changes. It does not approve any change to the shared scoring contract or to the disaster parsers.

## Approved rule mapping

The source of truth is D2 in `docs/decisions/2026-10-21-task-01-acceptance-boundaries.md`; the named examples are in `docs/evidence/task-01-example-cases.md`. The numeric values are acceptance fixtures, not universal weights.

| Rule id | Trigger | Notes |
| --- | --- | --- |
| `disaster.gdacs-orange-red` | Source family is GDACS and alert level is Orange or Red | Alert comparison is case-insensitive. Missing/other levels do not trigger. |
| `disaster.tsunami-flag` | `DisasterEvent.Tsunami == true` | Applies to any event type and any source that supplies the flag. |
| `disaster.earthquake-magnitude` | Event type is an earthquake variant and severity (magnitude) is at least 6.0 | Applies when the GDACS alert is Green or absent. A specified non-Green GDACS alert does not use this rule. Non-GDACS alert levels do not suppress it because they are not GDACS alert levels. IFRC severity is never read as magnitude. |
| `disaster.ifrc-severity` | Source family is IFRC or the severity unit identifies IFRC severity, and severity is at least 2.0 | Event-type casing is irrelevant; IFRC GO includes flood, epidemic and other humanitarian event types. |
| `disaster.no-approved-trigger` | No rule matched | The decision is not important and the factors say why. |

Boundaries are inclusive: exactly M6.0 and exactly IFRC severity 2.0 are important. A GDACS Yellow earthquake at M6.5 is not important under the approved fixture because the magnitude rule is limited to Green/unspecified alerts. GDACS Orange/Red at any magnitude is important.

### Input and output shape

Input is the existing normalized `WorldAlerts.Api.Disasters.DisasterEvent`. The scorer reads `SourceId`, `EventType`, `Severity`, `SeverityUnit`, `AlertLevel` and `Tsunami`; it does not read titles, times, geography or provenance. Output is the shared `ImportanceDecision`: `IsImportant`, a stable `RuleId`, human-readable `Factors`, and a binary `Score` of 1.0 or 0.0.

The disaster rule is time-independent, so `Score(DisasterEvent)` has no `nowUtc` parameter. The 24-hour eligibility window is a separate concern (D3). There is no clock access inside the rule.

When several rules match, the reported `RuleId` follows a fixed precedence: GDACS Orange/Red, tsunami flag, earthquake magnitude, IFRC severity. Every matched rule still contributes a factor. This keeps the single-rule-id contract deterministic while making combined matches visible.

### Source-id and severity-unit matching

A source family token must be the first normalized token. Thus `gdacs` and `gdacs-rss` match GDACS, while `not-gdacs` does not; `ifrc-go` matches IFRC, while `my-ifrc-feed` does not. The IFRC rule can also be selected by a severity unit such as `IFRC severity level` (case- and punctuation-insensitive), because that unit identifies the scale even if the source id is unfamiliar.

Earthquake event-type variants accepted by the scorer are `earthquake`, `earthquakes`, `eq`, `quake` and `quakes` (case-insensitive, first token). This covers GDACS `EQ`, EONET `earthquakes`, USGS/EMSC `earthquake`, and IFRC's source-native casing.

## Metadata actually available per source

| Source family | Event type | Alert level | Severity / magnitude | Tsunami flag | Other relevant gaps |
| --- | --- | --- | --- | --- | --- |
| GDACS (`gdacs`) | Normalized from `gdacs:eventtype` (`EQ` → `earthquake`, etc.) | Yes, `gdacs:alertlevel` | Yes, typed `gdacs:severity` value/unit | **No.** The parser currently sets `Tsunami: null` for every GDACS item. | A live GDACS tsunami cannot trigger the tsunami rule today. DS4 is an approved acceptance fixture, not evidence that GDACS supplies this field. |
| USGS (`usgs`) | `properties.type`, normally `earthquake` | PAGER `properties.alert` (not a GDACS level) | Yes, `mag` with `magType` | Yes, `properties.tsunami` 0/1 → bool | No publication time; only event/update times. |
| EMSC (`emsc`) | Always `earthquake` | No | Yes, `mag`/`magnitude` with `magtype`/`magType` | No | No publication time. |
| EONET (`eonet`) | Category id or title, e.g. `earthquakes`, `volcanoes`, `landslides`, `drought` | No | Yes, `magnitudeValue`/`magnitudeUnit` when supplied | No | No publication time; no severity ranking. |
| IFRC GO (`ifrc-go`) | `dtype.name` or `disaster_type`, source-native casing (`Flood`, `Pluvial/Flash Flood`, `Earthquake`) | String `ifrc_severity_level` is stored as `AlertLevel` | Numeric `ifrc_severity_level` only, unit `IFRC severity level` | No | If the live API returns a string level such as `Orange`, `Severity` stays null and the IFRC rule reports missing severity. A string-to-number mapping is not approved, so it is not invented. |
| NWS (`nws-alerts`) | Alert event name, e.g. `Tsunami Warning` | NWS severity string (`Extreme`, `Severe`, …) | No numeric severity; unit `NWS severity` | No | A tsunami warning is not a `Tsunami` flag. Inferring one from the event name is not approved. |
| DWD (`dwd-warnings`) | Normalized warning event name | DWD warning level string | Numeric warning level, unit `DWD warning level` | No | Germany only. |
| Copernicus EMS (`copernicus-ems`) | Normalized category | No | No | No | Activation metadata only. |

## Hardened edge cases covered by tests

- Exactly M6.0 and just below 6.0.
- GDACS Green high magnitude vs Orange/Red at any magnitude vs Yellow high magnitude.
- Non-GDACS alert levels do not trigger the GDACS rule and do not suppress the earthquake magnitude rule.
- Missing alert level, magnitude, tsunami flag and IFRC severity each produce explicit factors; missing data never produces importance.
- IFRC event-type casing (`Flood`, `EPIDEMIC`, `Pluvial/Flash Flood`) and IFRC earthquake types below severity 2.0 (not read as magnitude).
- IFRC severity unit detection with mixed case, and source-id family matching that rejects unrelated ids such as `not-gdacs` and `not-ifrc`.
- Earthquake type variants (`Earthquake`, `earthquakes`, `EQ`, `quake`, `Earthquake (M6.0)`).
- Tsunami combined with GDACS Orange/Red, Green magnitude and IFRC severity, including precedence and missing-magnitude cases.
- NWS alert/severity metadata not being mistaken for GDACS or IFRC rules.

## Proposed shared-contract changes (not applied)

1. **Provider kind on the event.** `DisasterEvent` carries only a configured `SourceId`. A normalized provider/source-kind field would remove the first-token heuristic and make family matching explicit. Until then, the scorer documents the token rule.
2. **Multiple matched rules.** `ImportanceDecision` exposes one `RuleId`. A `MatchedRuleIds` collection would let consumers see every matching rule; today the additional matches are only in `Factors`. The fixed precedence is documented instead.
3. **Tsunami metadata.** The GDACS parser does not populate `Tsunami`, and NWS expresses tsunami risk as an alert event name rather than a boolean flag. Populating or deriving that field needs parser/approval work outside this task; without it, live GDACS and NWS tsunami events cannot use `disaster.tsunami-flag`.
4. **IFRC string severity.** The IFRC parser stores a string `ifrc_severity_level` in `AlertLevel` and only a numeric value in `Severity`. A human-approved mapping from IFRC string levels to numeric severity is needed before the IFRC rule can score those rows; no mapping is invented here.
5. **Uniform scorer signature.** The disaster rule needs no time input. If the project later wants one signature for all families, a `nowUtc` parameter could be added for uniformity, but it would be unused and is not proposed for adoption now.

No shared contract file, parser, endpoint or configuration file was changed by this task.
