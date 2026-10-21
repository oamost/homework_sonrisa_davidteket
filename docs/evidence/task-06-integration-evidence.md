# Task 06 — news-source integration evidence

DeepSeek implemented the integration and all of its tests; the parent ran the independent build, test suite and live verification below.

## Automated checks

| Check | Command | Result | Log |
| --- | --- | --- | --- |
| Build | `dotnet build src/WorldAlerts.sln --nologo` | succeeded, 0 warnings, 0 errors (reported by DeepSeek; suite run below rebuilt it) | — |
| Test suite | `dotnet test src/WorldAlerts.sln --nologo` | **20 passed, 0 failed, 0 skipped** | `docs/evidence/task-06-backend-tests.log` |

Coverage reported by DeepSeek: parser (7), store (5), ingestion (3), endpoint (2), foundation (3). The suite uses local fixtures plus a fake `HttpMessageHandler`; **no live network in `dotnet test`**, and a dedicated test asserts that disabled polling performs zero HTTP calls.

## What was integrated

Six keyless RSS feeds, configured in `src/api/appsettings.json`, with a single shared SQLite file:

| Source id | Feed | Family |
| --- | --- | --- |
| `bbc-world` | BBC World | world news |
| `nyt-world` | NYT World | world news |
| `aljazeera-all` | Al Jazeera English | world news (mixed topics) |
| `guardian-world` | The Guardian World | world news |
| `ars-technica` | Ars Technica | consequential tech/science |
| `nature` | Nature | transformative science |

`src/api/News/` contains the normalized item model, an RSS 2.0/1.0 parser, a SQLite store unique on (source id, native id), a per-feed poller, an ingestion service and poll result reporting. `GET /api/news?limit=n` (default 50, clamped 1–200) returns newest-first items with provenance.

## Live verification (real feeds)

- Started the API against a fresh database; the startup poll fetched all six feeds.
- Stored **253 items**, none with a missing title or link: aljazeera-all 25, ars-technica 20, bbc-world 35, guardian-world 45, nature 75, nyt-world 53.
- **Nature's RSS 1.0/RDF feed parsed correctly**: 75 items, identity from `<link>`, `dc:date` day-precision timestamps stored (e.g. `2026-10-20T00:00:00Z`), empty category list.
- Endpoint clamping verified: `limit=500` → 200 items, `limit=0` → 1 item, `limit=50` → 50 items.
- **Restart idempotency**: stopped and restarted the API. Rows stayed at 253, duplicate keys 0, and every feed logged `inserted 0, duplicates <n>` (BBC 35, NYT 53, Al Jazeera 25, Guardian 45, Ars 20, Nature 75). Nothing was re-inserted.
- Logs: `docs/evidence/task-06-live-run.log`, `docs/evidence/task-06-live-restart.log`. The API was stopped afterwards (health probe returned no connection).

Sample live item (NYT): title "China Criticizes U.S. Ban on DeepSeek A.I. in Government Systems", link, `publishedUtc` `2026-10-21T00:48:25+00:00`, `firstSeenUtc` `2026-10-21T00:52:20Z`, family `world-news`, categories list, `provenance` pointing at the feed.

## Limits, stated plainly

- No importance scoring, breaking detection, subscriptions or delivery exist yet; this task only collects and normalizes.
- Timestamps are publication times, not event-occurrence times; region metadata is absent and was not invented.
- Cross-source duplicates are stored separately (a story carried by two outlets yields two records); clustering is task 13.
- Snapshot counts do not prove 24-hour historical completeness or worldwide editorial coverage; those caveats remain in `docs/evidence/task-05-news-source-validation.md`.
- Poll cadence defaults to 15 minutes with no overlap; scheduling, restart progress and source-health surfacing belong to task 12.
- Task 04's event record is implemented for the news slice only; market and disaster families remain `TODO` under task 04/08/10.

## Disposition

Task 06 status: `DONE` — the human ran the manual test themselves, confirmed it worked and directed the commit.
