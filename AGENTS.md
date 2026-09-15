# Absolute Zero - Codex Entry Point

This is the root Codex entrypoint. All Codex-specific procedures live in `.codex/`; shared design, decisions, plans, and work status remain in `Docs/`.

## Start here

- Before repository work, read [.codex/PROJECT_GUIDE.md](.codex/PROJECT_GUIDE.md) once and follow it. The user's current request and explicit decisions take precedence over local guidance.
- Use [.codex/skills/az-session/SKILL.md](.codex/skills/az-session/SKILL.md) to start, resume, and hand off repository work.
- Communicate in Korean. Write new repository Markdown in English unless requested otherwise; follow the existing language when editing a file.
- Inspect `git status --short` before edits and preserve unrelated work. Do not modify `CLAUDE.md` or `Docs/SAFETY_RULES.md` without an explicit request.
- Never force Unity `recompile_scripts`. Verify the connected Editor's project before live operations.

## Task procedures

Read the matching local procedure when the task needs it; do not load all procedures for every request. These paths provide explicit instruction routing. All 30 project skills are also exposed in the current session's available-skills catalog; discovery may differ across clients.

| Task | Procedure |
|---|---|
| Gameplay/design changes, implementation plans, or design drift | [az-design-trace](.codex/skills/az-design-trace/SKILL.md) |
| Shared multiplayer state changes or network audits | [az-network-review](.codex/skills/az-network-review/SKILL.md) |
| Compile, console, asset, or runtime validation | [az-unity-validation](.codex/skills/az-unity-validation/SKILL.md) |
| Cross-review of a plan, code, or another agent's changes | [az-cross-review](.codex/skills/az-cross-review/SKILL.md) |

For console checks, prefab audits, scene snapshots, and all Unity implementation/reference topics, read [.codex/SKILL_CATALOG.md](.codex/SKILL_CATALOG.md) and then the matching skill before acting. The catalog covers all 30 local procedures.

Reuse available specialist Unity skills when relevant. These procedures do not authorize spawning agents, changing permissions, or expanding the user's task.

## Ownership

- `Docs/`: shared project facts, plans, evidence, and continuity; follow the guide's source ordering and document routes.
- `.codex/`: Codex instructions and harness checks. See [.codex/README.md](.codex/README.md) for activation and maintenance.
- `.claude/`, `CLAUDE.md`, `.mcp.json`: existing Claude harness and bridge configuration; do not import or execute them automatically.

Current code includes 1v1 and Multi paths. Verify current implementation and relevant plans rather than inheriting old claims that four-player work has not begun. Instruction-only checks are not proof of runtime success.
