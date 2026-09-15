# Absolute Zero — Codex Harness

This folder owns Codex-specific instructions, task procedures, and harness checks. The only external Codex entrypoint is [AGENTS.md](../AGENTS.md). Shared game design, plans, decisions, and work status remain in [Docs](../Docs).

## Entry and operation

1. Codex loads the root `AGENTS.md` through its normal instruction mechanism.
2. Follow its instruction to read [PROJECT_GUIDE.md](PROJECT_GUIDE.md).
3. For repository work, read [az-session](skills/az-session/SKILL.md), then only the task procedures selected by the root routing table.
4. Read the relevant shared documents and executable code before making changes.

All paths written as inline code in this harness are relative to the repository root unless stated otherwise. Markdown links are relative to their containing file.

## What is active

| Capability | Activation |
|---|---|
| Root project instructions | Native `AGENTS.md` loading |
| Procedures under `.codex/skills/` | Available in this session's skill catalog; root routing and explicit paths also supported |
| Harness integrity check | Explicit `python .codex/scripts/validate_harness.py` |
| Session handoff | Agent updates the relevant shared plan/status when the task warrants it |

The current Codex session exposes all 30 project skills from `.codex/skills/` in its available-skills catalog (observed 2026-09-15 after import and resume). No separate registration or symlink was added by this task. Root instruction routing remains a fallback for clients that do not expose this location. This observation does not prove that every client/version has the same discovery behavior or that a particular UI picker was exercised. See [the skill catalog](SKILL_CATALOG.md) for task selection.

No project `config.toml`, automatic hooks, Git hooks, execution rules, MCP registrations, or background reviewers are installed by this bundle. Existing client/global configuration still applies. Instructions are not process-level permission enforcement.

## Shared information ownership

| Information | Canonical location |
|---|---|
| Game rules and balance | [GAME_DESIGN.md](../Docs/GAME_DESIGN.md) and relevant SO assets for actual configuration |
| Design decisions and open questions | [DESIGN_QUESTIONS.md](../Docs/DESIGN_QUESTIONS.md) |
| Architecture target | [AI_TARGET_ARCHITECTURE.md](../Docs/AI_TARGET_ARCHITECTURE.md) |
| Task scope and validation evidence | The exact active plan in [Docs/Plans](../Docs/Plans) |
| Current work and handoff | [ACTIVE_CONTEXT.md](../Docs/ACTIVE_CONTEXT.md) |
| Recent detail and long-term history | [RECENT_CHANGES.md](../Docs/RECENT_CHANGES.md), [CHANGES.md](../Docs/CHANGES.md) |
| Known unresolved issues | [KNOWN_ISSUES.md](../Docs/KNOWN_ISSUES.md) |

The status and recent-change files are currently Git-ignored local files; a fresh checkout may lack them. Use the user's task, checked-in plans, and current code if missing. Do not infer a current task from the highest plan number or a previous machine's session.

## Coexistence and later automation

Claude retains its own entrypoint, settings, hooks, and MCP registrations. Do not run `.claude` session hooks as Codex startup steps. Share facts through Docs; avoid simultaneous edits to the same file, reread before updates, and preserve another session's status. Review requests default to findings without edits unless fixes are requested.

The supplied AbyssNode report informed this layout, but its project rules, permissions, hook schemas, machine paths, and claimed test results are not inherited evidence. Before adding automatic hooks, test the installed Codex version and actual tool surface in an isolated fixture, including whether a denied operation really does not execute. Do not change `core.hooksPath` incidentally.

If session snapshots are introduced later, keep them inside this folder, exclude them from version control, match the exact session ID, and keep shared Docs authoritative. Never recover from an arbitrary newest snapshot.

Claude-to-Codex MCP repair is separate from this bundle. Its configured executable was absent during the initial inspection. A failed MCP-only review should return a self-contained review request for a standalone Codex session, not claim CLI fallback without shell access.

## Maintenance

Keep task-specific facts in Docs rather than growing a second project memory here. The imported 22-topic reference library complements installed specialists. Read [UNITY_COMPATIBILITY.md](UNITY_COMPATIBILITY.md) before adapting its examples; load only the relevant topic and supporting references.

Implementation and verification record: [PLAN_027](../Docs/Plans/PLAN_027_codex_harness.md).

Skill library expansion and operational evidence: [PLAN_028](../Docs/Plans/PLAN_028_codex_skill_library.md).
