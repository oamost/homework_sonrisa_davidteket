# Runtime completion implementation plan

**Goal:** Execute the human-requested three parallel workstreams: task 26, task 24, and full integration of existing WIP tasks. Existing frozen goals and task-01 decisions remain the authority; no new matching or notification policy is assumed approved.

## Ownership and sequence

- [ ] Startup worker: root `run.sh`/`run.ps1`; container assets under `src/`; task-26 tests under `src/tests/`; startup documentation under `docs/`. No application-code edits.
- [ ] Admin worker: `src/api/Admin/`, admin frontend files isolated under `src/web/`, admin tests and documentation. Expose composition extensions; do not edit `Program.cs` or configuration shared with integration.
- [ ] Integration worker: finish existing source, collection, dedupe, catch-up and delivery modules, introduce the task-19 persistent queue required by those flows, and own `Program.cs`/application configuration. Preserve existing tests and previously committed scoring rules.
- [ ] Parent: connect the admin extensions to the composition root after both workers finish; reconcile operational data interfaces with the queue/source-health implementation.
- [ ] Parent: run one serialized build/test sequence, then container/startup checks where supported and live API/admin checks. Report Windows or delivery checks that cannot actually be executed.
- [ ] Parent: present evidence and unresolved policy/provider limitations. Commit separately by verified task under the human's approval, without labeling incomplete work DONE.

## Required behavior and verification

- Startup scripts invoke the same containerized build, tests, then run path; failing tests prevent startup. Repeat invocation does not duplicate the service. No script-level root/admin requirement and no GitHub Actions.
- Admin uses the approved local password and cookie wall; unauthorized operational access fails, secrets stay hidden, mutations require CSRF protection, and source/queue/failure data comes from runtime state rather than invented healthy/empty values.
- Runtime integration must persist notification state, respect the approved startup burst of 50 followed by 10/minute, bound retries at 1/5/15 minutes, and test restart behavior without claiming generic external exactly-once delivery.
- Matching/revision ambiguities are approval blockers, not permission to invent thresholds. Providers that refused must not be retried.
- All workers use targeted retrieval, TDD, isolated file scopes and no commits. Parent serializes full-suite verification to avoid the previous shared-build collisions.
- Task 17 stays TESTING pending human runtime review. Live provider samples and fixture-based proof must be visibly distinguished.
