# Task 03 plan — persist category subscriptions

Human direction: start task 03 while the market/disaster integration runs. To avoid two agents editing the same files, this slice is **storage + tests only**; API endpoints and UI wiring follow once the integration agent releases `Program.cs`.

## Agreed inputs

- A subscription is "one event-type code from the approved matrix + one delivery channel", per the product concept. Per-instrument/per-location conditions are not agreed requirements, so none are added.
- Channels are stored as stable string ids (`email`, `slack`, plus future ids) — not an enum — because task 22 requires adding a channel without production-code changes.
- Event-type codes are stored as non-empty strings. The definitive taxonomy is still task 01 (`Settle blocking acceptance boundaries`), so this slice deliberately does **not** invent or hard-code a type list; it persists whatever approved codes are supplied.
- Local single-user install: records carry a `UserId` that defaults to `local`, keeping the table usable if multi-user ever appears without redesigning keys.

## Storage contract

`src/api/Subscriptions/` with:

- `Subscription` record: id, user id, event type, channel, optional destination reference (no secrets), enabled flag, created/updated UTC.
- `SubscriptionStore`: creates its own table in the one configured SQLite file, validates non-empty type/channel, normalizes case/whitespace, and enforces uniqueness on (user, event type, channel) so saving the same choice twice updates rather than duplicates.
- Upsert returns the stored record; delete removes by key or id; list returns the user's subscriptions ordered deterministically.
- No delivery credential is stored here; destination references stay opaque and task 20/21 decide secret storage.

## Tests (DeepSeek-owned, offline)

- save → list shows the record with trimmed/lower-cased channel.
- save → new store instance on the same file still sees it (restart persistence).
- saving the same (user, type, channel) twice updates in place and does not duplicate.
- enabling/disabling and changing destination persist across reopen.
- delete removes only the targeted row.
- empty event type or channel is rejected.
- arbitrary future channel ids are accepted, proving the table is not tied to email/Slack.

## Out of scope for this slice

HTTP endpoints (`Program.cs` is being edited by the integration agent), UI, subscription-to-event matching, importance decisions, delivery, and the final event-type taxonomy. The alert-setup screenshot required by the task matrix cannot exist until the frontend task 23; that gap will be stated in the evidence instead of faked.

## Acceptance for this slice

`dotnet test` green including existing tests, store survives reopen, no new NuGet packages, no edits outside `src/api/Subscriptions/**` and new subscription test files.
