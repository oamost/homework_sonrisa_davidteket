# 2026-10-21: user-observed runtime and follow-up correction

## User evidence and original assumptions challenged
- `dotnet build` and tests ultimately passed in the user Docker engine after time-dependent disaster fixture fix; container started and `/api/health` returned HTTP 200 with `{"status":"ok"}`.
- User screenshots confirm admin sign-in/out works with local-demo password, source polling shows healthy/succeeded, and Important News had only 5 eligible entries, including the same BBC COVID headline duplicated. Only 2 were dated 2026-10-21 in the user's browser timezone.
- User screenshot shows the private SMTP form blank; mail could not work without saving a real provider account. Slack sign-in in another browser tab is not authorization for this application's outgoing webhook.
- Admin `queued: 0, failed: 0` is **not** evidence of no source polling; those counters only reflect pending/failed notification jobs. Add a sent counter for dispatch visibility.

## Decisions and changes
1. Preserve strict classifier and provider timestamps; don't fabricate "Important" articles. Deduplicate matching family/headline in the important preview. Prefer newer displayed articles.
2. Add a separately labeled `latestHeadlines` read-only collection (at most 20, 24h, valid provider date/link, deduplicated) to show real current stories rejected by the strict classifier. None is silently promoted or notified as important.
3. In synthetic TEST sends, explicit private email/Slack settings take precedence over stale same-event-type legacy rows; no double send for a configured channel.
4. After a same-origin/CSRF-protected, rate-limited TEST request, attempt to dispatch only `synthetic-test:` jobs immediately, using the existing durable queue and error/retry semantics; do **not** consume unrelated production notification jobs on the button press.
5. Admin delivery projection now includes `sent` count as well as queued/failed. `sent` means SMTP server or Slack HTTP endpoint accepted transmission, not that a human received/read it.
6. Never bake SMTP passwords, Slack webhook tokens, or live user data into project archives. Document safe UI configuration and real verification of inbox/channel arrival.

## Validation and limitations
- Angular tests: 42/42 passed in this sandbox (`npm test -- --watch=false`); Angular production build passed with pre-existing initial-bundle-size warning.
- Added .NET xUnit regressions for current configured channel preference over disabled legacy, immediate TEST queue scoping, recent dated title deduplication, Important title deduplication, and sent dashboard count. **Not executed here**: .NET SDK and Docker are unavailable in this sandbox, so the user must run `./run.sh up` and confirm the full Docker build & .NET tests.
- Real Gmail/Slack handshake and end-to-end delivery **not executed**: private application password and Slack webhook are intentionally supplied by the owner at runtime, and we do not access the user's local service.
