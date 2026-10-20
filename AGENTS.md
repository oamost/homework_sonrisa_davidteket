# Repository working rules

These rules apply to all work in this repository. Communicate in English.

## Authority and decision loop

- The human directs which task to work on next. Do not decide unspecified project scope, design, or behavior without asking. If unclear, ask concise questions with numbered options.
- Stop and consult the human when unexpected obstacles arise; do not chase rabbit holes such as a failing API request.
- Be honest about what is implemented, tested, blocked, and awaiting human verification. A deliverable is not done until the human verifies it.
- Work only within this repository.

## Milestones and commits

- Commit only after a milestone has tangible evidence **and** the human has verified and approved it. Never treat automated checks as human approval.
- The commit subject must be exactly the name of the deliverable shipped. The commit body must describe what was done and what was verified, distinguishing automated evidence from human verification.
- Do not commit or push merely because work appears finished; ask for approval first.

## Files and retrieval

- `prompts.md` at the root is the prompt history: append only the human's prompts in their original order, numbered without timestamps; never add agent reasoning or responses. Preserve each prompt verbatim, even if it mentions superseded rules.
- `docs/` contains all human-oriented documentation produced during the work, including plans other than the required goal tracker, decisions, evidence, logs, and screenshots.
- `out/` contains produced working artifacts and deliverables.
- `src/` contains all source code, including tests.
- `ctx/` contains all persistent agentic knowledge, including the required goal trackers `ctx/goals/plan.md` and `ctx/goals/tasks.md`. Follow the tracker rules below.
- When adding a Markdown file under `ctx/`, update this file with its location and purpose so it is discoverable. Current files: `ctx/goals/plan.md` (human-specified goals only); `ctx/goals/tasks.md` (per-deliverable task matrices and status history).
- Root exceptions to the documentation layout are this `AGENTS.md` and `prompts.md`, as explicitly required.

## Goal and task tracker rules

- Keep `ctx/goals/plan.md` exclusively for goals expressly provided by the human. Its **first section** must preserve the original full requirement verbatim, visibly enclosed in quote symbols, as the root of all later detail. Leave it without setup history, pre-flight notes, invented project requirements, or implementation tasks. Add derived detail only after coverage review and human approval; ask instead of filling gaps.
- Keep `ctx/goals/tasks.md` as the work tracker. Give **each defined deliverable its own named section and its own Markdown matrix**; never combine different deliverables into one large matrix. Keep the current repository-initialization/preparation history together in its own matrix. Do not create a future deliverable matrix until the human defines that deliverable's goals.
- Every deliverable matrix must include, at minimum, `Order`, `Goal`, `Description`, and `Status` columns, with **`Status` as the last column**. Each status cell must contain exactly one of these symbols: `TODO`, `WIP`, `TESTING`, `DONE`. Use `TODO` for work not started or awaiting direction, `WIP` for work in progress, `TESTING` for agent-finished work pending checks or human verification, and `DONE` only after human verification. Keep blockers and approval needs in the description or surrounding section text, not in the status cell. Do not add timestamp columns or fabricated time metadata. Number tasks in execution order and make new tasks fine-grained enough to work through one by one.
- Derive future deliverable tasks from the human-specified goals in `ctx/goals/plan.md`; preserve all requirements without omissions or contradictions. Before the human-approved freeze, update both trackers together when goals change, but keep historical preparation tasks in `tasks.md` rather than moving them into `plan.md`. After the freeze, keep `plan.md` immutable and limit `tasks.md` edits to status cells unless the human explicitly directs lifting the freeze. The human decides which task to work on next.
- Before freezing `ctx/goals/plan.md`, build a reviewed coverage matrix for the original alert-setting/world-event requirement using real source evidence. Once the human approves and freezes the plan, use the per-deliverable matrices in `ctx/goals/tasks.md` as its coverage matrix: map every agreed goal and acceptance criterion to concrete tasks, with no omissions. Do not freeze or invent details without the human's approval.
- When changing a task's status, use only the four permitted symbols and do not add timestamps. Agent-finished work is `TESTING`, not `DONE`, until the human verifies it; milestone commit approval remains a separate human decision.

## Agent workflow

- GPT-6 Sol (medium effort) is the parent orchestrator. Delegating suitable small tasks to DeepSeek, GLM, or local Qwen 64k is mandatory, not merely optional. **Only the parent** decides whether a particular task is unsuitable for delegation (for example, because it is too small or cannot be scoped safely); this is not a blanket requirement to delegate every action. Reserve local Qwen for very small tasks.
- Each delegation must provide the subagent with applicable repository rules, available/relevant tools, the exact small-scope objective and requirements, allowed files or research boundaries, and expected output and verification. Instruct subagents not to read whole files: retrieve only targeted sections or symbols (prefer Serena MCP for symbol retrieval), and make only targeted changes within explicitly allowed files. Keep research assignments equally narrow; split broad exploration into separate small requests.
- Parallelize only independent delegations with non-overlapping edit scopes. The parent reviews, verifies, and integrates each result, retains all design and human-approval decisions, and remains accountable for the deliverable.
- Prefer Serena MCP for efficient symbol retrieval; use applicable Superpowers skills when available for the task. The project enables Superpowers through `opencode.jsonc`; its workflows do not override human direction, verification, or commit approval. The parent must also avoid whole-file reads and instead use targeted retrieval.
- Do not invent missing requirements to fill `plan.md` or `tasks.md`; seek human direction before expanding them.
- During event-source research, if a probed provider refuses to respond (including rate-limit rejection), blacklist that provider as a candidate, document the result in `docs/requirement-coverage.md`, do not retry or troubleshoot it, and investigate a different suitable source for the same feed type. Explicitly brief subagents on the blacklist; discard and disclose results from any subagent that probes a blacklisted source. A successful single probe does not prove worldwide coverage.
