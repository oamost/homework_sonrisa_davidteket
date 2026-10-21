# Important-news landing preview — handoff evidence

## Implemented

- Angular `/` route renders a live feed from `GET /api/news/important`, with provider headline, optional provider summary, source/feed provenance, publication time, importance reasons, explicit empty/error/loading and last-evaluated states, manual refresh, and a 60-second refresh interval. Pending HTTP requests are cancelled when leaving the page; refreshes do not overlap. Admin `/admin` route remains present.
- API reads **all persisted news** via `NewsStore.GetForSelectionAsync`, evaluates each through `EventRecordMappers.FromNews`, `CatchUpEligibilityEvaluator` (exact provider publication timestamp, 24-hour eligibility), and `NewsImportanceScorer` without subscriptions or unverified corroboration. Only important and eligible stories appear. Display is limited to 100 **after** filtering, with `omittedCount` reported. No collection, source requests, subscriptions, queueing, or delivery are invoked by the endpoint.
- Data depends on configured provider polling and available recent provider publications; neither the landing page nor the endpoint fetches new source material. The API's `/` remains the text API root; the Angular dev server's `/` is the landing page.

## Verification

- `cd src/web && npm test -- --watch=false`: 3 files, 13 tests passed, including existing admin navigation test.
- `cd src/web && npm run build`: successful.
- `git diff --check`: clean at handoff.
- .NET tests intentionally **not run** by this worker due to concurrent serialized backend verification. Parent should run `dotnet test src/tests/WorldAlerts.Api.Tests/WorldAlerts.Api.Tests.csproj --filter FullyQualifiedName~ImportantNewsEndpointTests` and then the full suite. Endpoint integration tests use persisted SQLite with no subscriptions; cover important/routine/stale/future/missing publication, provider summary/link/provenance, >200 unrelated rows, and omitted important-result count.

## Manual verification and integration

- Run the API with the normal local process on `https://localhost:7021`, then from `src/web` run `npm start`; open `http://localhost:4200/`. The dev server proxies `/api` to the API as configured in `src/web/proxy.conf.json`. Inspect `https://localhost:7021/api/news/important` for the raw response. Verify refresh, empty/error states, provider links, and `/admin` in a browser; human verification is still pending.
- Subscription UI integration is reserved for the parent: after the setup worker exports its component, add the `/subscriptions` route to `src/web/src/app/app.routes.ts` and a navigation link to `src/web/src/app/app.html`; do not import an absent component. Neither file was modified for subscriptions here.
