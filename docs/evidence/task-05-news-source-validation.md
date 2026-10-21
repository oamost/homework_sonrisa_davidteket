# Task 05 — scoped news-source validation (probe log)

Scope set by the human: run a small validation on a few selected sources; task 05 is considered done after that. This record covers **three** keyless feeds, one GET each, made by a DeepSeek read-only investigator. The parent did **not** re-probe; results below are that investigator's report, recorded verbatim in substance.

- No registration, account, or API key was used.
- No blacklisted provider was contacted: GDELT, ReliefWeb v1, the probed Meteoalarm endpoint and Stooq were never queried.
- No provider refused a request, so `docs/requirement-coverage.md` needs no new blacklist entry.

## Probes

| Feed (single GET) | Sample item: title / link / pubDate / category | Items dated within 24 h of the probe |
| --- | --- | --- |
| `https://feeds.bbci.co.uk/news/world/rss.xml` | "Sudan army drones hit mourners…" / `bbc.co.uk/news/articles/cr3el70jz1nqo` / Tue, 20 Oct 2026 17:30:15 GMT / **no category field** | 22 of 35; channel `lastBuildDate` 21 Oct 2026 00:25 UTC |
| `https://www.aljazeera.com/xml/rss/all.xml` | "NATO to send additional patrol planes to Black Sea…" / `aljazeera.com/news/2026/10/20/nato-to-send-…` / 20 Oct 2026 23:19:52 +0000 / `News` | 25 of 25; channel `lastBuildDate` 21 Oct 2026 00:08 UTC |
| `https://feeds.arstechnica.com/arstechnica/index` | "Merging SpaceX and Tesla would be a 'good idea,' Musk says" / `arstechnica.com/space/2026/10/…` / 20 Oct 2026 22:04:54 +0000 / `Cars`, `Space`, `merger`, `Tesla` | 13 of 13 visible items; payload appeared truncated mid-feed, total unknown |

Each response was a complete, parseable RSS document. The retrieval tool exposed the body rather than status headers, so HTTP 200 is inferred from a valid payload, not read directly.

## Observations that affect integration (task 06)

- **Publication time is not event time.** Channel dates describe when an article was published. Reported examples: a BBC France-riot story and an Alberta-vote story described events from the weekend/Monday. Startup catch-up must not present publication time as the event occurrence time.
- **No usable region metadata.** All three feeds omit a region/geography field; region is only inferable from headline or URL slug. The integration must not invent geocoding.
- **Category is inconsistent.** BBC World exposes no category; Al Jazeera's all-feed mixes topical tags with `Sport` and `TV Shows` (noise for this product); Ars Technica exposes topical tags only.
- **Cross-feed duplication is real.** The Tesla–SpaceX merger story appeared in both BBC and Ars Technica, so deduplication is required before notification.
- **Item IDs vary.** BBC and Al Jazeera provide article URLs whose paths are stable; Ars Technica likewise. Native GUIDs, where present, were not reported. A feed-scoped ID derived from link/guid is the safest key.

## Limits of this validation — do not overstate

- The 24-hour counts are **snapshot counts only**. They do not prove that any feed returns a complete prior-24-hour set at startup, and no historical query was performed.
- Only English-language, editorially selective outlets were exercised. Conflict, politics, AI/security and transformative-science coverage is represented only by whichever items happened to be in these snapshots.
- Worldwide geographic coverage remains unproven.
- Attribution and reuse terms were **not** checked (BBC carries a terms link, Al Jazeera a copyright notice, Ars Technica said nothing in the payload). Display of publisher name plus original link is the assumed minimum; redistribution rights are undecided.
- A single successful probe at one moment says nothing about rate limits, long-run availability, or feed stability.

## Additional feeds probed for task 06 scope

A small Qwen probe (two GETs per feed: one check, one payload capture — one more than the single-GET brief, disclosed for honesty) checked the remaining three matrix feeds. All returned HTTP 200; nothing refused, so no blacklist change.

| Feed | Items | Shape | 24 h snapshot count |
| --- | --- | --- | --- |
| `https://rss.nytimes.com/services/xml/rss/nyt/World.xml` | 53 | `guid` and `link` on every item, `pubDate`, 490 `category` elements, dc/media/atom/nyt namespaces | 30 of 53 |
| `https://www.theguardian.com/world/rss` | 45 | `guid` and `link`, both `pubDate` and `dc:date`, 309 categories, mrss/dc namespaces | 62 date values within 24 h of the latest (some items carry both date elements) |
| `https://www.nature.com/nature.rss` | 75 | **RSS 1.0 / RDF**, not RSS 2.0: **no `guid`**, **no `pubDate`**, `dc:date` with **day precision only** (`2026-10-20`), **no categories**, CDATA in title/description | 29 of 75 |

Integration consequence: Nature must be identified by `link`, its publication value is a date rather than a timestamp, and CDATA must be unwrapped. None of this was truncated; each feed snapshot was internally consistent.

These remain one-off snapshots: no 24-hour history guarantee, no worldwide completeness, and rights/attribution are still unchecked.

## Evidence disposition

Task 05 status: `DONE` — the human approved the scoped validation and directed work to continue with task 06, which is integrated by DeepSeek.
