# Task 06 plan — integrate the validated news sources

Human direction: proceed with task 06, integrate all the validated sources into the backend, and do not delegate to Qwen (context too small). Integration code and tests are DeepSeek's; the parent keeps design decisions, review and evidence.

## Sources in scope

All six probed news/topic feeds from the coverage matrix, all keyless RSS:

| Feed id | Name | URL | Topic family |
| --- | --- | --- | --- |
| `bbc-world` | BBC World | `https://feeds.bbci.co.uk/news/world/rss.xml` | world news |
| `nyt-world` | NYT World | `https://rss.nytimes.com/services/xml/rss/nyt/World.xml` | world news |
| `aljazeera-all` | Al Jazeera English | `https://www.aljazeera.com/xml/rss/all.xml` | world news (mixed topics) |
| `guardian-world` | The Guardian World | `https://www.theguardian.com/world/rss` | world news (politics/economy tags) |
| `ars-technica` | Ars Technica | `https://feeds.arstechnica.com/arstechnica/index` | consequential tech/science |
| `nature` | Nature | `https://www.nature.com/nature.rss` | transformative science |

BBC, Al Jazeera and Ars Technica were re-validated live in task 05. NYT, Guardian and Nature were validated in the earlier discovery probes recorded in `docs/requirement-coverage.md`; a small fresh probe confirms their current field shapes while DeepSeek builds the config-driven adapter. If any feed refuses or changes shape, it is configured out and recorded, never quietly dropped.

No accounts, API keys, or credentials. The other candidate sources (GDACS, EONET, WHO DON, IFRC GO, Binance, Frankfurter, Yahoo) belong to disaster (08) and market (10) integration, not this task. No blacklisted provider may be contacted (GDELT, ReliefWeb v1, probed Meteoalarm endpoint, Stooq).

## What "integrated" means here

1. **Event record (news slice of task 04).** Persist, per story: source id and display name, native id (guid or link), title, original link, publication time when present, first-seen time, feed-level category, item categories, and provenance. Missing publication time is stored as absent, not fabricated; first-seen time is recorded separately. The record is designed to be extended for markets/disasters later, but those families are **out of scope**; task 04 remains `TODO` for them.
2. **Parser.** Read RSS 2.0 with `System.Xml.Linq`; tolerate missing `guid`, missing `pubDate`, unparsable dates, empty categories, CDATA/entities, and leading/trailing whitespace. Native id falls back from `guid` to `link`; an item with neither is skipped and counted, never invented.
3. **Persistence.** SQLite table keyed uniquely on (source id, native id), so a re-poll of the same story inserts nothing. Store inserts must be parameterized. Existing health check and schema must keep working.
4. **Collection.** A hosted service fetches all configured feeds at startup and then on a configurable interval (default 15 minutes), one poll at a time, no overlapping concurrent polls, per-feed failure isolated and logged (source id, error type, count) without stopping the loop. A failed fetch is not retried aggressively in this task; retry/backoff policy belongs to later tasks. Feeds are configured in `appsettings.json`.
5. **Read endpoint.** `GET /api/news?limit=<n>` returns stored stories newest-first (by publication time when present, else first-seen), each with provenance and explicit nulls. Limit is clamped to a sane maximum. This is what the human will inspect; UI work is task 23.
6. **Health stays honest.** `/api/health` keeps returning 200 only when the SQLite file is usable.

## Behaviour cases to exercise

- **New story** — inserted once, returned by the endpoint.
- **Repeated story** across polls — second poll inserts nothing, no duplicate rows.
- **Late-published story** — published hours before first sight; stored with its publication time and first-seen time, and treated as normal (no special suppression). Notification behaviour for lateness is a later task.
- **Missing metadata** — no `guid`, no `pubDate`, no categories: skip only when no title/link at all; otherwise store with explicit nulls.
- **Broken feed** — malformed XML or HTTP failure leaves other feeds and the API unaffected.

## Explicit non-goals

Importance scoring, breaking detection, subscriptions, notification delivery, 24-hour catch-up pacing, cross-source story clustering (task 13), other event families, frontend changes, and live-feed assertions inside automated tests.

## Verification expected

- Parser unit tests and ingestion tests using local fixtures and a fake HTTP handler — **no live network in the automated suite**.
- Store tests against a temporary SQLite file: first insert, duplicate insert, endpoint ordering.
- `dotnet build src/WorldAlerts.sln` warning-free and `dotnet test` green, logged under `docs/evidence/`.
- One manual live run documented separately (start app, let the startup poll fetch, `GET /api/news`, then stop) with feed ids and item counts; provider gaps stay as recorded in task 05.

## Delegation constraints

Implementation and tests: DeepSeek only. Allowed files: new files under `src/api/News/`, `src/api/Program.cs`, `src/api/appsettings.json`, new tests and fixtures under `src/tests/WorldAlerts.Api.Tests/`. No edits to `src/web`, `docs`, `ctx`, `prompts.md`, `.gitignore`, or project files unless a build genuinely requires it (then report why). No commits.
