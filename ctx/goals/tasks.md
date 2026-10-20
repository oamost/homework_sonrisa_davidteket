# Task matrices by deliverable

Each deliverable has its own section and matrix. `ctx/goals/plan.md` is reserved for human-specified goals. `Status` is always the last column: `TODO` = not started/awaiting direction, `WIP` = in progress, `TESTING` = agent-finished but awaiting checks or human verification, `DONE` = human-verified. No timestamps are recorded in this tracker; no milestone commit has been approved.

## Deliverable 1 — Repository initialization and preparation (human-verified)

| Order | Goal | Description | Status |
| --- | --- | --- | --- |
| 01 | Repository setup (historical) | Initialize local Git repository and set the specified `origin` URL. | DONE |
| 02 | Repository setup (historical) | Create root `AGENTS.md` with the provided working rules. | DONE |
| 03 | Repository setup (historical) | Create root `prompts.md` and begin recording human prompts in order without agent reasoning; keep appending future prompts under repository rules. | DONE |
| 04 | Repository setup (historical) | Establish `docs/`, `out/`, `src/`, and `ctx/` with goal trackers in `ctx/goals/`. | DONE |
| 05 | Repository setup (historical) | Check Git configuration and paths and present setup evidence. | DONE |
| 06 | Repository setup (historical) | Obtain human verification and approval before any setup milestone commit; if approved, use the deliverable name as subject and describe work and verification in the body. | DONE |
| 07 | Pre-flight (historical) | Probe Serena, Superpowers availability, local Qwen, OpenCode DeepSeek, and a free OpenRouter model; record and report results. | DONE |
| 08 | Superpowers availability (historical) | Configure the official Superpowers OpenCode V2 plugin in this repository. | DONE |
| 09 | Superpowers availability (historical) | Load a Superpowers skill in OpenCode and report the result or blocker. | DONE |
| 10 | Goal tracking format (historical) | Remove setup content from `plan.md`, preserve task history in a matrix, and update the repository rules. | DONE |
| 11 | Goal tracking format (historical) | Group tasks into separate matrices by deliverable, keeping preparation work together. | DONE |
| 12 | Goal tracking format (historical) | Record the plan-only-goals and per-deliverable task-matrix conventions as explicit rules in `AGENTS.md`. | DONE |
| 13 | Agent workflow rules (historical) | Make suitable small-task delegation mandatory at parent discretion and define subagent briefing, targeted retrieval, edit, and research boundaries. | DONE |
| 14 | Goal tracking format (historical) | Remove unreliable timestamp metadata from the prompt history and task tracker; update repository rules. | DONE |
| 15 | Goal tracking format (historical) | Restrict task statuses to `TODO`, `WIP`, `TESTING`, and `DONE`, with `Status` last; update `AGENTS.md` and this matrix. | DONE |
| 16 | Project goals (awaiting definition) | Obtain deliverable, functionality, constraints, acceptance criteria, and milestones from the human; add only specified goals to `ctx/goals/plan.md`. | TODO |

## Upcoming deliverables — awaiting human-defined goals

Add one separately named section and matrix per deliverable after the human defines it. Derive its fine-grained tasks from the specified goals without loss or inconsistency; the human chooses the next task. Implement and verify only approved work, then request human review. No future deliverable or milestone is complete before human verification.
