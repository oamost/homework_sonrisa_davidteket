# Task 18 evidence — 24-hour catch-up selection (started)

## Delivered

`src/api/CatchUp/`: `CatchUpWindow` (default 24h, configurable, inclusive), `CatchUpEligibility`/evaluator, `CatchUpCandidate`, `CatchUpCoverageGap`, `CatchUpSelection`, `CatchUpSelector`.

- Eligibility follows task 01 D3: publication time inside the window even when the event is older (late item); no provider timestamps never eligible; any future provider timestamp excluded; exactly 24h inclusive.
- Deterministic ordering by eligibility timestamp, then key.
- Dedupe by trimmed native id, with a stable composite key for markets; delivered-key set suppresses re-notification.
- Coverage gaps returned as data: `EmptyCategory`, `StaleSource`, `MissingProviderTimestamps` (for admin display later).

## Verification

- 15 catch-up tests; full suite 190 passed; build 0 warnings.
- Tests cover recent news, late-published old event, missing provider times, future timestamps, market inside/outside window, disaster event-time-only, duplicate ids, already delivered, empty store, ordering and the 24h boundary.

## Remaining before the task is complete

Startup invocation, store reads, persisted delivered ids, pacing/queueing (task 19) and admin rendering are not wired yet. The selection service is pure and clock-injected so integration can call it from a hosted service.
