# Absolute Zero - Codex Project Guide

Read this guide once before repository work. This file owns Codex operating constraints; game design and current status remain in shared Docs. Paths in inline code are repository-relative.

The original root guide was audited on 2026-08-19. This extraction (2026-09-15) preserves its operating constraints while removing stale runtime snapshots. Read versions from `ProjectSettings/ProjectVersion.txt` and `Packages/manifest.json`, scenes from `ProjectSettings/EditorBuildSettings.asset`, and implementation from current source/assets. Do not infer counts, class names, pending bugs, or completed tests from historical summaries.

Codex does not automatically execute the Claude harness. Read `.claude/`, `CLAUDE.md`, and `Docs/SESSION_SETUP.md` only for relevant harness work or historical context. Existing session authorization takes precedence over local procedural guidance.

## Communication and Repository Etiquette

- Communicate with the user in Korean unless asked otherwise.
- Write new repository Markdown documentation in English unless the user explicitly requests Korean.
- Follow the language and style already used in the file being edited; existing C# comments may be Korean or English.
- Preserve the user's dirty worktree. Inspect `git status --short` before edits and never overwrite, revert, or reformat unrelated changes.
- Do not modify `CLAUDE.md` or `Docs/SAFETY_RULES.md` unless the user explicitly asks. If a rule appears wrong, report the evidence instead of silently rewriting it.
- Do not add dependencies, perform broad asset moves, or expand the requested scope without user approval.

## Source-of-Truth Order

When sources disagree, use this order and surface meaningful conflicts to the user:

1. The user's current request and explicit decisions.
2. Current executable state: C# source, scenes, prefabs, ScriptableObject assets, `Packages/manifest.json`, and `ProjectSettings/`.
3. `Docs/GAME_DESIGN.md` for intended gameplay rules, balance, item behavior, and unresolved design questions.
4. `Docs/SYSTEM_DESIGN.md` for the dated architecture snapshot. It was generated on 2026-08-18, but every task-specific claim must still be checked against code.
5. The first/current section of `Docs/ACTIVE_CONTEXT.md` for work status and known verification debt.
6. `Docs/RECENT_CHANGES.md`, `Docs/CHANGES.md`, and `Docs/Plans/` for history and rationale.
7. `Docs/SYSTEM_ARCHITECTURE.md`, `Docs/GAME_SYSTEMS.md`, old plans, and session compaction documents as historical or aspirational references only.
8. `CLAUDE.md` and `.claude/` as harness-specific guidance, not current implementation truth.

`Docs/AI_TARGET_ARCHITECTURE.md` defines the approved architectural direction for future refactors. Read it for architecture, async, initialization, manager ownership, player identity, event, pooling, resource, player-count scaling, or asmdef work. It is a target contract, not evidence that the current code already follows that structure. The detailed Korean rationale and diagrams are in `Docs/ARCHITECTURE_EVOLUTION_KO.md`; the canonical behavior-preserving migration sequence is `Docs/Plans/PLAN_018_architecture_migration.md`; the original four-player expansion plan is `Docs/Plans/PLAN_019_four_player_expansion.md`.

Design decisions also live in `Docs/DESIGN_QUESTIONS.md`. Older architecture snapshots and PLAN_019 describe earlier stages; current Multi work is also recorded in later plans. Follow the plan relevant to the user's task, not a fixed historical plan number. `Docs/SYSTEM_DESIGN.md` is a dated snapshot, not guaranteed to be the latest architecture.

## Non-Negotiable Architecture Rules

1. **Server authority:** mutate shared gameplay state only on the server/host. Clients submit intent with `[Rpc(SendTo.Server)]`; the server validates it and changes authoritative state.
2. **Network collections:** write `NetworkVariable<T>` and `NetworkList<T>` only from server-authorized paths. UI and visuals observe replicated state rather than becoming alternate state owners.
3. **Turn ownership:** `TurnManager` exclusively owns phase progression through `WaitingForPlayers -> PrepPhase -> AttackPhase -> ResolutionPhase -> RoundOver`. Do not create another phase driver or assign phase state from unrelated systems.
4. **ScriptableObject immutability:** treat item SOs as runtime-read-only configuration. Put mutable uses, buffs, selections, and per-round state in runtime objects or network data.
5. **Serialized Unity assets:** preserve `.meta` GUID pairings. Prefer the Unity Editor or a verified Unity automation path for scene, prefab, controller, and asset moves. Do not casually hand-edit large Unity YAML files.
6. **Event lifecycle:** pair every subscription with cleanup in the relevant network or Unity lifecycle method. Account for despawn, disconnect, scene changes, and coroutine cancellation.
7. **Identity domains:** never assume NGO `ClientId`, logical PlayerIndex/match seat, stable participant ID, owner ID, and list position are interchangeable. Convert explicitly and preserve the mapping.
8. **Player-count symmetry:** review all configured player seats. New architecture APIs use collections and must not introduce `_p1`/`_p2`, `1 - index`, or ClientId-as-seat assumptions. Preserve both existing 1v1 and Multi behavior unless the user requests a change. Current code already contains Multi paths; review every configured seat without assuming runtime validation is complete.
9. **Namespaces:** the final namespace segment must not collide with an imported type. The lobby UI uses `AbsoluteZero.UI.LobbyUI`, not `AbsoluteZero.UI.Lobby`, because `Lobby` is an imported service model type.
10. **Coroutine allocations:** cache constant-duration waits used repeatedly. Dynamic waits derived from runtime animation or effect values are allowed when a fixed cached instance would be incorrect.
11. **Entity iteration:** Unity or network entities may disappear during iteration; null-check potentially destroyed objects and make cleanup idempotent.
12. **Architecture scope:** respect the current ownership and composition boundaries, including existing singleton/component and `GetComponent<T>()` usage; migrate architecture only within the requested scope. Do not introduce a DI framework incidentally.

## Task Workflow

1. Inspect `git status --short`, relevant current-status context, and the exact symbols/assets in scope. Use `rg` or `rg --files`; do not trust historical paths without checking.
2. Distinguish observed implementation from intended design. If changing gameplay where code, SO assets, and `GAME_DESIGN.md` disagree, use an explicit current user decision when it resolves the conflict; otherwise ask which behavior is authoritative before dependent edits and continue independent investigation.
3. Keep edits narrow. Preserve unrelated user changes and avoid mass formatting generated Unity files.
4. For a substantial multi-file feature, create or update a scoped plan under `Docs/Plans/` and record explicit non-goals. Do not create process documents for a trivial, self-contained fix.
5. Update continuity/change documents only when the work materially changes the project. Keep one canonical statement and link to it instead of duplicating full explanations across files.
6. Validate in proportion to risk, then report exactly what was and was not verified.
7. For architecture plans, compare viable patterns when ownership/authority/lifetime changes, map the selected pattern to concrete classes and flow, verify APIs against exact installed package source plus official documentation, and record newer compatible options. Never upgrade a package without explicit approval and a separate compatibility/rollback gate.

## Validation Ladder

Use the strongest available checks that fit the task:

1. Static inspection: references, call sites, namespaces, server guards, event subscribe/unsubscribe pairs, serialized field names, and `.meta` pairings.
2. Focused compile or analyzer checks where they are meaningful. A generated `.csproj` build is supporting evidence, not a substitute for Unity compilation.
3. Unity Editor auto-compilation and console inspection when a live editor connection is available.
4. Targeted Play Mode validation with separate host and client views for network, timing, UI, VFX, disconnect, and round-reset work.

Unity MCP is optional infrastructure, not a prerequisite for static analysis. If it is available, discover the current editor connection dynamically and verify that it targets this repository before using its configured transport. Do not assume HTTP or stdio from a historical guide. Never call a forced `recompile_scripts` operation; save files and rely on Unity auto-compilation. If live Unity validation is unavailable, state that limitation rather than claiming runtime success.

For network changes, verify at minimum:

- host and remote-client behavior;
- RPC sender validation and phase/ownership guards;
- late subscription or late spawn behavior;
- disconnect/despawn cleanup;
- round reset and second-round reuse;
- player-index versus client-ID mapping.
- for Multi work, every seat, Host plus three remote clients, target visibility, deterministic resolution, and per-client disconnect behavior.

## Definition of Done

A change is complete only when the requested behavior is implemented, relevant authority and lifecycle paths were reviewed, available focused validation passed, unrelated worktree changes were preserved, and remaining runtime or asset limitations were explicitly reported. Do not mark historical plan/checklist items complete merely because code was edited.

## Shared document routes

| Task | Inspect first |
|---|---|
| Resume or current status | [ACTIVE_CONTEXT](../Docs/ACTIVE_CONTEXT.md), then the relevant exact plan in [Plans](../Docs/Plans) |
| Gameplay and balance | [GAME_DESIGN](../Docs/GAME_DESIGN.md), [DESIGN_QUESTIONS](../Docs/DESIGN_QUESTIONS.md), relevant SO assets and effect code |
| Architecture, async, ownership, identity, events, pooling, resources, asmdef | [AI_TARGET_ARCHITECTURE](../Docs/AI_TARGET_ARCHITECTURE.md), [architecture rationale](../Docs/ARCHITECTURE_EVOLUTION_KO.md), relevant migration plan and current code |
| Architecture overview | [SYSTEM_DESIGN](../Docs/SYSTEM_DESIGN.md), verified against current implementation |
| Lobby UI | [LOBBY_UI_SPEC](../Docs/LOBBY_UI_SPEC.md), `Assets/Scripts/UI/Lobby/` and relevant scene |
| Mini-games | [MINIGAME_SYSTEM](../Docs/MINIGAME_SYSTEM.md), `Assets/Scripts/UI/MiniGame/`, PlayerState |
| Network or combat | `Assets/Scripts/Core/Network/`, `Core/Session/`, `Core/Turn/`, `Core/Combat/`, `Core/Match/`, and relevant plans; paths after the first share `Assets/Scripts/` |
| Inventory or presentation | `Assets/Scripts/Core/Inventory/`, `Core/Item/`, `UI/Game/`, and relevant player/VFX classes; verify current owners with `rg` |
| Scene/prefab wiring | Relevant `.unity`/`.prefab`, `.meta`, owning source, and verified live Editor connection |
| History and open issues | [RECENT_CHANGES](../Docs/RECENT_CHANGES.md), [CHANGES](../Docs/CHANGES.md), [KNOWN_ISSUES](../Docs/KNOWN_ISSUES.md), then Git history |

Some shared files may be local-only or untracked. Missing documents do not establish a design decision; use available primary sources and ask only when a material ambiguity blocks dependent work. For scene-scoped ownership, preserve cleanup across scene transitions. For inventory/VFX work, review selection identity across slot compaction and rebuild locks. For mini-games, preserve server-side eligibility, deadline, acceptance, and consumption authority.

