# Application Foundation Implementation Plan

> **For agentic workers:** Work on `main` as directed by the human. Use test-first cycles for behavior; do not commit until a milestone has human verification and separate commit approval.

**Goal:** Deliver only Deliverable 3 task 02: a locally testable C# application, Angular shell, SQLite foundation and safe delivery-configuration pattern.

**Architecture:** One ASP.NET Core API owns the SQLite file. A minimal Angular frontend can run locally and communicate with the API; no alert authoring, source ingestion, admin functionality or notification sending belongs to this task. Container launch scripts are reserved for task 26.

**Tech Stack:** .NET 10 SDK, ASP.NET Core, Microsoft.Data.Sqlite, xUnit and ASP.NET Core integration testing; Angular 22, TypeScript, npm. These implementation versions are builder choices for the approved C#/Angular direction, not new user requirements.

**Spec:** `ctx/goals/plan.md` and Deliverable 3 task 02 in `ctx/goals/tasks.md`.

## Global Constraints

- Source code and tests go under `src/`; evidence under `docs/`, working artifacts under `out/`.
- Keep committed configuration free of SMTP passwords and Slack webhook URLs; only local environment or untracked files can supply secrets. Do not require delivery credentials to view the foundation shell.
- Do not implement later subscription, source, delivery, admin or root launch-script tasks prematurely. `tasks.md` permits status-only edits.
- Human verifies work before `DONE`; milestone commits require separate approval. Tests and launch evidence are not proof of worldwide coverage.

## Review Focus

- Missing or invalid SQLite path → API startup/health fails visibly, not a falsely healthy status.
- App restart → SQLite file remains usable without recreating persisted state.
- Missing delivery secrets → startup still works without logging or embedding credentials.
- API unavailable → Angular shell stays visible rather than presenting a false connected state.
- App rerun → no unsafe duplicate initialization or leaked runtime artifacts in Git.

---

### Step 1: Backend project and test harness

**Files:** `src/api/WorldAlerts.Api.csproj`, `src/api/Program.cs`, `src/tests/WorldAlerts.Api.Tests/WorldAlerts.Api.Tests.csproj`, `src/tests/WorldAlerts.Api.Tests/FoundationTests.cs`.

- [ ] Create .NET project/test-project scaffolding, with no event or notification feature code.
- [ ] Write API integration test for `GET /api/health` expecting healthy response and an initialized temporary SQLite database. Run it red against an unregistered route.
- [ ] Add the smallest SQLite connection/initialization and health route; run the test green.
- [ ] Add a test for invalid/unwritable DB path and for environment-only secret configuration; run red, then implement minimal failure/safety behavior and run green.

### Step 2: Angular shell and test harness

**Files:** `src/web/**` only (Angular CLI-generated project, package lock, shell component and its test).

- [ ] Generate Angular 22 project under `src/web/`; no app screens beyond a minimal foundation shell.
- [ ] Write a component test that the shell states the app is not yet configured for alerts and has an honest API-health state; observe failure before adding the behavior.
- [ ] Implement the smallest shell and API-health query; run its test and Angular production build.

### Step 3: Local integration and evidence

**Files:** `.gitignore`, `docs/evidence/**`, `docs/superpowers/plans/2026-10-21-application-foundation.md` (task evidence), and only the necessary API/web configuration under `src/`.

- [ ] Configure a local Angular-to-API dev proxy without adding delivery credentials to tracked configuration.
- [ ] Run the entire .NET and Angular test suites and both builds, then start both locally and verify the API and frontend responses.
- [ ] Record build/test/start logs under `docs/` and a redacted local-start screenshot if a browser is available; document any missing screenshot explicitly.
- [ ] Present evidence to the human; mark task 02 `TESTING`, not `DONE`, until they verify it. Do not commit without separate approval.
