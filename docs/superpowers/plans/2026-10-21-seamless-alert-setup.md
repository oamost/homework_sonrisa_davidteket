# Seamless Alert Setup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Repository rules override skill commit steps: **do not commit before human verification and separate approval**.

**Goal:** Deliver all eight approved feed, setup, notification-channel, test-event and admin improvements in the existing local app.

**Architecture:** Keep the Angular frontend and ASP.NET Core/SQLite backend. Introduce a persisted, explicit type-selection preference for new profiles; preserve legacy per-channel rows until the user intentionally migrates them. Channel settings and transports are independently registered, but feed selection, deduplication, rate limiting and durable queue remain shared. A synthetic public test event uses the same matching and dispatch rules with bounded abuse protection.

**Tech Stack:** Angular 22/TypeScript/Vitest, ASP.NET Core/.NET 10/C#, SQLite, Data Protection, Bootstrap.

**Spec:** `docs/superpowers/specs/2026-10-21-seamless-alert-setup-design.md` (human-approved).

## Global Constraints

- All 24 approved codes including `economy.financial`; no unapproved numeric `> 0.70` delivery rule.
- Feed refresh every 60 seconds while visible; admin status every 30 seconds while visible, without overlapping requests.
- New unsaved local profile selects all approved types; an explicit empty saved selection stays empty; existing legacy recipients are never silently broadened.
- Test button may be used without admin authentication, but requires same-origin antiforgery protection, durable global cooldown/daily cap, and consent via an explicit click; no secrets or recipients in responses.
- SMTP password, Slack incoming webhook and generic webhook authorization stay protected at rest and redacted in APIs; only non-secret addresses and labels are readable.
- Startup catch-up at most 50 jobs followed by at most 10/minute; persisted dedupe, retries, 24-hour provider-time eligibility and approved source limitations remain intact.
- Work inside this repository and preserve existing user edits; no commit, push or DONE status until the human verifies and approves.

## File Structure

- `src/api/Subscriptions/EventTypePreferenceStore.cs` owns preference schema, atomic selection save, and legacy-mode flag; `Program.cs` maps `/api/alert-settings/types` GET/PUT with validation and antiforgery for writes.
- `src/api/Queue/AlertPipeline.cs` maps selected events to preference/channel targets without bypassing legacy recipients; `NotificationQueueStore.cs` remains the durable queue; `NotificationDispatcher.cs` dispatches via a registered channel transport registry; `EmailNotificationWorker.cs` runs all configured transports.
- Existing `src/api/Delivery/{EmailSettingsStore.cs,Slack/SlackSettingsStore.cs,Slack/SlackEndpoints.cs}` remain private setting stores/endpoints; `src/api/Delivery/Webhooks/` owns protected generic webhook settings, destination validation and sender. `Program.cs` wires all three channels.
- `src/api/Queue/TestEventService.cs` owns synthetic event storage/expiry, deduped queue injection and durable global test-send throttling; `Program.cs` maps test token GET and public POST and includes current tests in feed read path without changing real provider selection.
- `src/web/src/app/landing.component.{ts,html}` owns debug/automatic-refresh/test controls; `src/web/src/app/subscriptions/` owns compact tags and setup cards, reusing `src/web/src/app/slack/slack-setup.component.ts`; `src/web/admin/admin.component.ts` owns operations polling.
- `src/tests/WorldAlerts.Api.Tests/` and adjacent `*.spec.ts` cover each code path. `docs/` holds setup instructions and evidence. Preserve frozen `ctx/goals/plan.md`; update `ctx/goals/tasks.md` only as repository rules allow.

## Review Focus

1. Pre-existing email/Slack rows: saving global preferences must not activate a previously paused or secret-bearing legacy recipient (Task 1).
2. Empty saved type set/restart: must not silently revert to all selected (Task 1).
3. Public test POST from cross-origin client or repeated after process restart: no send (Task 5).
4. Generic webhook DNS rebinding/redirect to a private IP: no outbound request to that IP (Task 4).
5. Offline/revoked admin session during periodic refresh: no stale healthy label or live timer after navigation (Task 7).

---

### Task 1: Atomic all-types preference and legacy migration

**Files:** Create `src/api/Subscriptions/EventTypePreferenceStore.cs`; modify `src/api/Program.cs`, `src/api/Queue/AlertPipeline.cs`; test `src/tests/WorldAlerts.Api.Tests/EventTypePreferenceTests.cs`, `src/tests/WorldAlerts.Api.Tests/AlertPipelineTests.cs`.

**Interfaces:** Produce `GetAsync(userId, ct) -> EventTypePreference` with `IsCustomized`, `SelectedTypes`; `SaveAsync(userId, IReadOnlyCollection<string> types, ct)` atomic and idempotent; `GET/PUT /api/alert-settings/types`; pipeline uses this state to match every configured channel, retaining legacy row behavior until intentional migration. No recipient address stored in type preference.

- [ ] Write failing persistence/API tests: new profile returns 24 selected codes; save 23 without `world-news`, reload retains 23; save `[]`, restart retains `[]`; reject unknown/duplicate codes and missing CSRF; legacy disabled row is not activated on preference save.
- [ ] Run `dotnet test src/WorldAlerts.sln --filter 'FullyQualifiedName~EventTypePreference'` and confirm new tests fail.
- [ ] Implement preference store, API, explicit migration boundary, and pipeline target matching; no unsafe automatic cloning of legacy destinations.
- [ ] Run focused preference and existing pipeline tests; check startup catch-up/deduplication unchanged.

### Task 2: Wire Slack and make email/Slack automatically deliver

**Files:** Modify `src/api/Program.cs`, `src/api/Queue/EmailNotificationWorker.cs`, `src/api/Queue/NotificationDispatcher.cs`, `src/api/Delivery/Slack/SlackEndpoints.cs`, `src/api/Delivery/Slack/SlackSettingsStore.cs`; tests `src/tests/WorldAlerts.Api.Tests/SlackEndpointsTests.cs`, `src/tests/WorldAlerts.Api.Tests/NotificationDispatcherTests.cs`.

**Interfaces:** Register existing `MapSlackSettings()`, protected store and sender; dispatcher resolves configured channel without a default-disable switch, keeps endpoint test separate from automatic queue delivery; invalid/unconfigured channels never send.

- [ ] Write failing host/worker tests: authenticated Slack GET is mapped; protected POST accepts authorized webhook; valid enabled Slack and email jobs dispatch through existing queue including catch-up; missing settings remain `Needs setup`, never send; refusal retries without leaking URL.
- [ ] Run focused Slack/dispatcher tests and confirm the new cases fail.
- [ ] Wire Slack host and multi-channel worker, preserve approved 50/10 pacing and current SMTP opt-in for legacy installations until explicitly configured.
- [ ] Run focused tests, verify unauthorized GET remains 401 and no real network is called.

### Task 3: Compact type tags and frictionless channel setup

**Files:** Modify `src/web/src/app/subscriptions/subscriptions.component.{ts,html,spec.ts}`, `src/web/src/app/slack/slack-setup.component.ts` and its tests (if absent create adjacent `slack-setup.component.spec.ts`); optionally create focused `src/web/src/app/subscriptions/channel-setup.component.ts` to avoid an oversized component.

**Interfaces:** Consumes Task 1 GET/PUT and Task 2 admin-only email/Slack status APIs. Tags toggle locally, Save persists once; private credentials never repopulate from API; admin login remains required only for settings, not the public test button.

- [ ] Write failing Angular HTTP-mock tests: 24 adjacent tags selected initially; click/toggle/save/reload persists; empty selection stays empty; SMTP sender/recipient and Slack non-secret label are text/email inputs; passwords/webhook remain masked; config status displays `Needs setup`, then automatic readiness after save; failed saves show retryable errors without leaking secrets.
- [ ] Run `npm test -- --watch=false` in `src/web` and observe new tests fail.
- [ ] Replace verbose type list/per-type/channel subscription form with accessible tag controls, parallel email/Slack cards and short private credential instructions; preserve old rows for display until explicit migration; expose separate send-one-test controls after consent.
- [ ] Run focused frontend tests and `npm run build`; review mobile spacing and secret redaction.

### Task 4: Configure generic HTTPS webhook without production edits

**Files:** Create `src/api/Delivery/Webhooks/WebhookSettingsStore.cs`, `WebhookSender.cs`, `WebhookEndpoints.cs`; modify `src/api/Program.cs`, `src/api/Queue/NotificationDispatcher.cs`, `src/api/Queue/AlertPipeline.cs`, `src/web/src/app/subscriptions/subscriptions.component.{ts,html,spec.ts}`; test `src/tests/WorldAlerts.Api.Tests/WebhookChannelTests.cs`; document in `docs/startup.md` or existing setup guide.

**Interfaces:** Admin GET/PUT/test `/api/admin/webhooks` storing HTTPS destination, POST/PUT method, allowlisted non-secret headers and protected authorization values, validated JSON template and display label. The transport registry routes configured `webhook:<id>` jobs; public list exposes labels/status only, never URL or headers containing credentials.

- [ ] Write failing tests: add a webhook via API without a code edit; mock handler sees rendered JSON exactly once; rejected header/method/template/HTTP URL and private/resolved loopback IPv4/IPv6 fail; DNS rebinding or redirect to private address cannot be followed; credentials never appear in status, queue or error; rate limits/retries apply.
- [ ] Run focused webhook tests and confirm failure.
- [ ] Implement protected configuration, pinned/validated resolution per outbound connection (not just URL-string checks), redirect disabled, bounded payload and template substitution, and config-driven transport registration plus setup UI.
- [ ] Run focused .NET/Angular tests; update setup documentation with a fake HTTPS receiver example, not a real secret.

### Task 5: Bounded public synthetic test event

**Files:** Create `src/api/Queue/TestEventService.cs`; modify `src/api/Program.cs`, `src/api/Selection/ImportantNewsService.cs` or its feed composition endpoint, `src/web/src/app/landing.component.{ts,html,spec.ts}`; test `src/tests/WorldAlerts.Api.Tests/TestEventEndpointTests.cs`.

**Interfaces:** `GET /api/test-events/token` stores an antiforgery cookie and returns token (no admin login); `POST /api/test-events` requires same-origin Origin/Fetch-Metadata and token, accepts no destination, issues unique `TEST — not real news` synthetic `world-news` event. Durable SQLite limit plus five-minute feed TTL; only current saved selection and configured matching transports create deduped jobs.

- [ ] Write failing tests: `world-news` selected sends once to every configured matching transport; removed/empty selection sends none; cross-origin/missing token rejected; same or concurrent repeated clicks and restart obey durable cooldown/daily cap; no address/webhook leak; synthetic item disappears from preview after five minutes.
- [ ] Run focused tests and confirm failure.
- [ ] Implement atomic rate-limit reservation, queue insertion via same matching/dedupe path, safe synthetic feed item and antiforgery/origin rejection without provider writes.
- [ ] Run focused tests; verify no external send without explicit authorized human configuration.

### Task 6: Hide diagnostics and automatically refresh feed

**Files:** Modify `src/web/src/app/landing.component.{ts,html,spec.ts}`.

**Interfaces:** 60-second visible-page polling; one in-flight request maximum; Debug unchecked by default gates *entire* diagnostics block and Task 5 test control; no Refresh events button.

- [ ] Write failing fake-timer tests: default view hides counts/coverage/warnings/scores/button; Debug reveals all, then hides them; initial request and visible-page interval refresh without overlap; hidden tab, navigation and error stop/skip appropriately; previous good items survive transient failure with stale notice.
- [ ] Run focused Angular test and confirm failure.
- [ ] Implement lifecycle-safe refresh and debug grouping without altering normal event cards; label numeric scores as heuristic.
- [ ] Run frontend tests and `npm run build`.

### Task 7: Automatically refresh admin operations

**Files:** Modify `src/web/admin/admin.component.ts` and adjacent admin spec (`src/web/admin/admin.component.spec.ts` if not present); inspect `src/api/Admin/` reader only if stale server-side data is evidenced.

**Interfaces:** 30-second visible-page polling of authenticated `/api/admin/status`; stop on logout or destroyed component; never display unverified healthy state for absent source rows.

- [ ] Write failing fake-timer tests: status changes without manual action; offline/401 becomes stale or prompts login, not healthy; hidden tab and overlapping requests skip; timer stops after sign-out/navigation.
- [ ] Run focused Angular test and confirm failure.
- [ ] Implement refresh with timestamp, safe stale/error messaging and cleanup.
- [ ] Run frontend tests and `npm run build`.

### Task 8: Whole-path verification, evidence and safe handoff

**Files:** Update `docs/` existing user guide/evidence, `ctx/goals/tasks.md` **status cells only** where warranted, `prompts.md` with verbatim missed human prompts in order; do not modify frozen `ctx/goals/plan.md`.

**Interfaces:** No new feature. Capture both automated and manual test steps without claiming live inbox/Slack success from mocks.

- [ ] Run full `dotnet test src/WorldAlerts.sln --nologo -m:1 /nodeReuse:false`, `npm test -- --watch=false`, `npm run build`, and `git diff --check`; record exact results.
- [ ] Start local app only if safe, check public feed, first-run/setup, admin state, Slack host route, test token/CSRF/rate-limit using fake destinations; capture redacted UI evidence if available.
- [ ] Document how to create a Slack incoming webhook, enter SMTP settings privately, and authorize human-chosen real test destinations. **Do not trigger real Slack/email sends without explicit final consent and entered settings.**
- [ ] Mark finished agent tasks `TESTING`, not `DONE`; present remaining limitations and ask human to verify before any milestone commit.

## Self-review

Spec coverage: home Debug, automatic feed, email/Slack wiring, all-default tags and migration, readable fields/protected secrets, generic configurable webhook, auto admin, public bounded test, queue/catch-up, and honest live verification each have an owning task. Review Focus failures are attached to Tasks 1, 4, 5 and 7. Work is sequential where interfaces depend; Tasks 6 and 7 can be developed independently after their respective backend endpoints exist. No commits are authorized by this plan.
