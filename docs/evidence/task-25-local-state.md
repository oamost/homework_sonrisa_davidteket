# Task 25 audit — Operate safely with local state

Scope: targeted audit only (no builds, tests, network, or system mutation). Earlier Qwen task 25 test output was truncated; the partial file `src/tests/WorldAlerts.Api.Tests/LocalStateSafetyTests.cs` was preserved and not edited. Task 27 was not touched per human direction.

## What was tested (targeted reads)

- `LocalStateSafetyTests.cs` public test methods:
  - `A_full_host_restart_preserves_subscriptions_queue_jobs_and_protected_email_settings`
  - `Protected_email_settings_round_trip_through_a_persistent_keyring_and_are_owner_only_on_unix`
  - (supporting fixture: an owned temp directory; an in-test `SendAsync` stub for mail recording.)
- `src/container/Dockerfile` (lines 14–16): creates `/app/data` and `/home/app/.aspnet/DataProtection-Keys`, owned by non-root `app`, keys dir `chmod 700`. run.sh/run.ps1 mount separate named per-instance volumes on those two paths (e.g. `*-data:/app/data`, `*-keys:/home/app/.aspnet/DataProtection-Keys`).
- `docs/startup.md` backup guidance: stop the service before copying, both data and key volumes plus the private env file must be safeguarded together, .NET warns the keys are **unencrypted at rest**, no backup automation supplied.

## Verified evidence (from parent runs, not this audit)

- Full parent .NET suite on the current tree: **541/541 passed**, including the local-state tests (`docs/evidence/task-26-current-smoke.log` line 58; corroborated by `task-26-current-relaunch.log`).
- Fresh Docker build + relaunch: **541/541** in-container tests, single running named container; a disabled subscription (same ID and `createdUtc`) and the DataProtection keys volume both survived container replacement — `docs/evidence/task-26-startup.md`, "Parent check of current source snapshot" and relaunch entry.
- Duplicate-process prevention: each launch is a single named container (e.g. `worldalerts-local` / smoke instance name); engine relaunch reports "original container replaced" and one running container — the same-instance relaunch path is the single-instance guarantee currently exercised.

## Current secret handling and known weaknesses (truthful, no invented requirements)

- Email/SMTP settings are written encrypted via the ASP.NET Core DataProtection key ring into a persistent keys volume (`task-26-startup.md`: protected email settings round-trip; `task-21-slack-runtime.md` shows the same DataProtection pattern for Slack secrets). The private env file is never baked into the image.
- **Weakness 1:** DataProtection keys are unencrypted at rest on the keys volume (.NET's own warning, acknowledged in `docs/startup.md`); volume privacy and Docker access control are the current mitigation.
- **Weakness 2:** Backup/restore consistency is not automatic: a consistent backup needs the service stopped (or SQLite online backup) **and** the data + key volumes plus env file saved/restored together; a raw copy while SQLite is active can be inconsistent (WAL). `docs/startup.md` supplies instructions only, no automation.
- **Weakness 3:** Native Windows-host execution and an actual end-to-end backup followed by a restore on a fresh host are not yet verified (Linux/PWSH smoke only; see `task-26-startup.md`).

## Disclosed remaining checks outside the verified task-25 evidence

- Restart-persistence evidence is recorded above; the human explicitly instructed that task 25 be marked DONE. This does not verify a recovery from lost volumes.
- Demonstrated backup **and** restore round-trip on a fresh instance using SQLite online backup (or stopped service) with data + key volumes + env file restored together.
- Native Windows-host startup and screenshot verification remain with task 26; key-volume-at-rest and backup limitations must remain visible to each user.

The human explicitly directed task 25 to be marked DONE. This status does not turn the unperformed backup/restore or Windows-host checks above into verified evidence; the native Windows launcher remains separately tracked under task 26. Preserve the limitations in the user guide and final review.
