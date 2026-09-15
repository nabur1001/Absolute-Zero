# PLAN_027 — Contained Codex Harness

## Scope and decision

User decision (2026-09-15): retain a single root agent entrypoint, group Codex-specific material in one folder, and reference shared Docs for design and overall status. The supplied AbyssNode session report is a reference, not a configuration to import verbatim.

Use root `AGENTS.md` and `.codex/` for the harness. Use instruction routing for `.codex/skills/` so no external skill directory or machine-dependent junction is required. Document the distinction from native skill discovery.

## Implementation

- [x] Inspect existing instructions, dirty worktree, shared document routes, and supplied report.
- [x] Reduce root AGENTS.md to an entrypoint and task routing table.
- [x] Move stable project constraints into `.codex/PROJECT_GUIDE.md`; route mutable facts to existing Docs and current code.
- [x] Add session, design-trace, network-review, Unity-validation, and cross-review procedures.
- [x] Document activation boundaries, Claude coexistence, and future automation conditions in `.codex/README.md`.
- [x] Validate links, skill structure, and review the task's write scope; record evidence below.

## Non-goals

No gameplay/code/asset changes; no `.claude/`, `CLAUDE.md`, `Docs/SAFETY_RULES.md`, or `.mcp.json` changes; no global configuration imports, permissions changes, dependencies, Git hook registration, automatic hooks, reviewer processes, or MCP connection repair. No duplicate game design or separate Codex game-state memory.

## Design and implementation routing

The root routes to `.codex/PROJECT_GUIDE.md` and the five procedures under `.codex/skills/`. The guide routes to existing game design, architecture, plans, current status, and issues. Shared plans hold task evidence. This plan is the canonical record of the harness change; the current gameplay status is not replaced.

The previous guide's August runtime snapshot is stale in multiple areas: current TurnManager has Multi branches and current Docs record Multi implementation. The new guide points to live sources rather than perpetuating file counts, old class names, or unsupported completion claims.

## Validation

- `python .codex/scripts/validate_harness.py`: 8 instruction documents and 5 skills checked; zero errors or warnings for local file links and basic metadata.
- Bundled `skill-creator/scripts/quick_validate.py`: all five skills passed.
- Scoped `git diff --check -- AGENTS.md` and whitespace inspection of all 10 task files passed.
- An initial repository-wide whitespace check also reported existing whitespace in unrelated scenes and GAME_DESIGN.md; those files were not edited by this task.
- Unity compilation and Play Mode were not run; this change contains instructions and a read-only Python helper, with no gameplay changes.
- Fresh-session instruction routing was not exercised in a new Codex process. Native skill-picker discovery, automatic hooks, and MCP connectivity were not tested or installed. Existing stale Claude-to-Codex executable configuration remains separate work.

Exact write scope: `AGENTS.md`, `.codex/README.md`, `.codex/PROJECT_GUIDE.md`, five `.codex/skills/*/SKILL.md` files, `.codex/scripts/validate_harness.py`, and this plan (10 files total). No existing shared gameplay/status files or Claude configuration were written.

## Rollback

Restore only the AGENTS.md changes belonging to this task and remove this task's `.codex/` files and plan after confirming no later edits depend on them. Do not reset the worktree or restore unrelated user changes. There are no installed hooks, config overrides, or global settings to undo.

## Follow-up operational audit (2026-09-15)

User requested MCP connectivity, skill quality, and existing hook behavior verification. This was an audit; Claude settings, hooks, MCP configuration, skills, and gameplay files were not changed.

### Live MCP evidence

- `Unity_ManageEditor(GetProjectRoot)` succeeded and returned `C:/Users/paek6/Absolute Zero`.
- `Unity_GetUserGuidelines` returned success without substantive guidelines.
- `Unity_ManageEditor(GetState)` succeeded: Editor 6000.3.11f1, not playing, paused, compiling, or updating.
- `Unity_ReadConsole(Get, Error/Warning, Count=15)` returned three warnings and no errors in that response: legacy Input Manager deprecation and Windows executable-signature collection warnings for Claude and Codex. This was not a new compilation or gameplay test.
- The screenshot's pending clients cannot be mapped to this tool connection from the available response. Successful reads prove this session's direct Unity access, not that every pending entry is healthy.
- Global config has two Unity MCP entries, with relay and Unity CLI launch commands. Project identity must continue to be checked before Editor operations.
- The separate Claude-to-Codex bridge in `.mcp.json` still points to an absent executable. Direct Codex-to-Unity success does not repair or validate that bridge.

### Skill and checker evidence

- All five SKILL.md files passed the bundled skill-creator validator again. Their contents were reviewed for scope, authority/lifecycle checks, design traceability, handoff, and truthful validation reporting.
- The actual bundle passed its link/metadata check: eight documents, five skills, no errors/warnings.
- Four isolated checker cases produced the expected exit codes: valid fixture 0, missing mandatory link 1, absent optional local status 0, malformed skill frontmatter 1.
- Session and Unity-validation procedures were exercised in this current session through worktree inspection, explicit project identity, editor state, and console reads. No independent fresh Codex session, native skill-picker invocation, or full behavioral evaluation of all five procedures was performed.
- Minor instruction defects remain: AGENTS.md and PROJECT_GUIDE.md headings contain a literal `?` where a dash was intended; PROJECT_GUIDE.md calls SYSTEM_DESIGN the latest snapshot at line 25 but correctly qualifies it as dated at line 33. Neither prevented file routing, but they should be cleaned up in a follow-up edit.

### Hook inventory and execution evidence

There is no project Codex hooks.json/config.toml or Codex hook implementation in this bundle. Global hook state contains trust records for AbyssNode, not an Absolute Zero hook definition. Existing `.git/hooks` scripts are Git LFS post-checkout/post-commit/post-merge/pre-push; `core.hooksPath` is unset and no Git pre-commit hook is present.

The four existing Claude hooks are registered in `.claude/settings.json`. Fourteen cases were executed against the original scripts in an isolated temporary Git repository with synthetic JSON stdin. No commit was executed and the real repository index was not used. Initial test invocations without the registered execution-policy argument failed before script execution; the cases below were rerun with the configured `-ExecutionPolicy Bypass` process argument. System policy was not changed.

| Hook | Observed results | Finding / limitation |
|---|---|---|
| block-md-creation | Docs/Test.md -> 0; .codex/README.md, .claude/agents/reviewer.md, scratch.md -> 2; Docs/../scratch.md -> 0 | String matching does not resolve traversal. Claude Write cannot author the new harness paths under current policy. This is not a restriction on Codex's separate tool surface. |
| post-edit-check | Current TurnManager.cs and NetworkSessionCoordinator.cs produce no alert; old AbsoluteZeroTurnManager.cs and LobbyManager.cs produce alerts | File matchers still reference historical names and miss current owners. |
| pre-commit-check | Unsafe staged fixture warns but exits 0; adding an IsServer comment only to the worktree hides the warning; git -C . commit is not detected; cached static readonly WaitForSeconds still warns | Warning-only heuristic, reads working file instead of index, weak command matching, and false positives/negatives. No authority guarantee or effective blocking rule. |
| pre-compact-save | Emits systemMessage naming tracked Audit.cs; omits Untracked.cs; produces no snapshot file | Reminder only, not persisted session recovery, and excludes untracked work. |

These runs verify script return/output behavior, not Claude's end-to-end interpretation or dispatch of the hooks. No claim is made that exit 2 necessarily blocks every client/tool surface. Existing Claude files were kept intact.

### Follow-up priorities

1. Clean up the two Codex heading characters and contradictory snapshot wording.
2. If automatic protection/recovery is requested, design Codex-local hooks separately and test their actual dispatch and deny behavior in the installed client before enabling them.
3. Repair the stale Claude bridge and Claude hook limitations only as a separately scoped change that preserves Claude's current workflow.
