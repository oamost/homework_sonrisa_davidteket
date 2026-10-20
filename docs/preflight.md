# Pre-flight findings

Recorded on 2026-10-20 (UTC). These are capability probes, not human acceptance of a milestone.

| Capability | Probe | Result |
| --- | --- | --- |
| Serena | `initial_instructions`, project activation, `get_current_config` | Responding; active project `sonrisa_homework`, language server ready. |
| Superpowers skills | Available skill registry and attempted `skill("superpowers")` | Not available in this session; loader returned `Unable to load skill superpowers`. The available skills are `opencode` and `report`. |
| Local Qwen | Local Qwen subagent, minimal inference | Returned `QWEN_PROBE_OK`. Catalog includes `ollama/qwen3.8:27b`. |
| OpenCode DeepSeek | DeepSeek read-only subagent, minimal inference | Returned `DEEPSEEK_PROBE_OK`. The DeepSeek agent's default is a free route; catalog listings for `opencode/deepseek-v4.1-flash` and `opencode/deepseek-v4-pro` show nonzero prices, so do not conflate those with a free listing. |
| OpenRouter (free) | `openrouter/cohere/north-mini-code:free` via general subagent | Returned `OPENROUTER_PROBE_OK`; catalog reports zero input/output cost. No paid OpenRouter model was invoked. |

No provider settings were changed. If superpowers skills are required for the next work, their unavailability needs human direction.

## Follow-up: enable Superpowers

After the human requested availability, added the official OpenCode V2 plugin to the repository's `opencode.jsonc`. In the active session, the native `skill` tool loaded both `using-superpowers` and `verification-before-completion`, and the available skill registry expanded to include the Superpowers skills. This supersedes the earlier unavailability finding. `git diff --check` returned no errors. No commit was created; human verification remains pending.
