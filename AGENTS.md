# Absolute Zero — Codex Project Guide

> Root-level operating context for Codex. Keep this file concise enough to load on every task.
> Last repository audit: 2026-08-19.

## Purpose

Use this file as the routing layer for project work. It records stable constraints, source-of-truth order, system boundaries, and validation expectations. Do not treat it as a replacement for the detailed design documents or for inspecting the current code.

Codex should not run the Claude session harness automatically. `.claude/`, `CLAUDE.md`, and `Docs/SESSION_SETUP.md` are Claude-oriented reference material. Read them only when a task concerns that harness or when they contain relevant historical context.

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
4. `Docs/SYSTEM_DESIGN.md` for the latest architecture snapshot. It was generated on 2026-08-18, but every task-specific claim must still be checked against code.
5. The first/current section of `Docs/ACTIVE_CONTEXT.md` for work status and known verification debt.
6. `Docs/RECENT_CHANGES.md`, `Docs/CHANGES.md`, and `Docs/Plans/` for history and rationale.
7. `Docs/SYSTEM_ARCHITECTURE.md`, `Docs/GAME_SYSTEMS.md`, old plans, and session compaction documents as historical or aspirational references only.
8. `CLAUDE.md` and `.claude/` as harness-specific guidance, not current implementation truth.

`Docs/AI_TARGET_ARCHITECTURE.md` defines the approved architectural direction for future refactors. Read it for architecture, async, initialization, manager ownership, player identity, event, pooling, resource, player-count scaling, or asmdef work. It is a target contract, not evidence that the current code already follows that structure. The detailed Korean rationale and diagrams are in `Docs/ARCHITECTURE_EVOLUTION_KO.md`; the canonical behavior-preserving migration sequence is `Docs/Plans/PLAN_018_architecture_migration.md`; actual four-player gameplay belongs to the later `Docs/Plans/PLAN_019_four_player_expansion.md`.

Known documentation drift at the audit date:

- The installed Unity version and `CLAUDE.md` now say `6000.3.11f1`; the generated `Docs/SYSTEM_DESIGN.md` snapshot still says `6000.0.73f1` and must not override `ProjectSettings/ProjectVersion.txt`.
- The repository currently contains 72 C# files under `Assets/Scripts` (49 Core, 21 UI, 2 Test), while `SYSTEM_DESIGN.md` describes a 65-file snapshot.
- `Docs/KNOWN_ISSUES.md` is empty even though `Docs/ACTIVE_CONTEXT.md` lists real open issues.
- `Docs/ACTIVE_CONTEXT.md` contains duplicated and historical sections. Treat its topmost current-status block as the latest summary, then verify in code and Git history.

## Verified Technical Baseline

- Unity `6000.3.11f1`, C#, URP `17.3.0` using Deferred rendering.
- Netcode for GameObjects `2.11.2`, Unity Transport `2.7.2`, Unity Relay, Lobby, and Authentication.
- New Input System `1.19.0`, UGUI, and TextMesh Pro.
- 2.5D, currently 1v1, host-authoritative, turn-based temperature deathmatch. New architecture seams should support a configured two-to-four-player roster, but four-player gameplay is not implemented yet.
- Build scene 0: `Assets/Scenes/LobbyScene.unity`.
- Build scene 1: `Assets/Scenes/GameScene.unity`.
- `NetworkManager.PlayerPrefab` is intentionally null. `PlayerSpawnManager` performs server-side spawning, and network prefab lists contain the Player prefab.
- There are 21 item ScriptableObject assets under `Assets/Data/Items/`: 4 basic and 17 random items.
- Production game and lobby UI are primarily constructed at runtime in C#. Test UI under `Assets/Scripts/UI/TestUI/` uses serialized Inspector references and is an explicit exception.
- Root namespace is `AbsoluteZero`; subsystem namespaces follow the directory structure.

## Non-Negotiable Architecture Rules

1. **Server authority:** mutate shared gameplay state only on the server/host. Clients submit intent with `[Rpc(SendTo.Server)]`; the server validates it and changes authoritative state.
2. **Network collections:** write `NetworkVariable<T>` and `NetworkList<T>` only from server-authorized paths. UI and visuals observe replicated state rather than becoming alternate state owners.
3. **Turn ownership:** `TurnManager` exclusively owns phase progression through `WaitingForPlayers -> PrepPhase -> AttackPhase -> ResolutionPhase -> RoundOver`. Do not create another phase driver or assign phase state from unrelated systems.
4. **ScriptableObject immutability:** treat item SOs as runtime-read-only configuration. Put mutable uses, buffs, selections, and per-round state in runtime objects or network data.
5. **Serialized Unity assets:** preserve `.meta` GUID pairings. Prefer the Unity Editor or a verified Unity automation path for scene, prefab, controller, and asset moves. Do not casually hand-edit large Unity YAML files.
6. **Event lifecycle:** pair every subscription with cleanup in the relevant network or Unity lifecycle method. Account for despawn, disconnect, scene changes, and coroutine cancellation.
7. **Identity domains:** never assume NGO `ClientId`, logical PlayerIndex/match seat, stable participant ID, owner ID, and list position are interchangeable. Convert explicitly and preserve the mapping.
8. **Player-count symmetry:** review all configured player seats. New architecture APIs use collections and must not introduce `_p1`/`_p2`, `1 - index`, or ClientId-as-seat assumptions. Preserve current 1v1 gameplay until PLAN_019 is explicitly activated.
9. **Namespaces:** the final namespace segment must not collide with an imported type. The lobby UI uses `AbsoluteZero.UI.LobbyUI`, not `AbsoluteZero.UI.Lobby`, because `Lobby` is an imported service model type.
10. **Coroutine allocations:** cache constant-duration waits used repeatedly. Dynamic waits derived from runtime animation or effect values are allowed when a fixed cached instance would be incorrect.
11. **Entity iteration:** Unity or network entities may disappear during iteration; null-check potentially destroyed objects and make cleanup idempotent.
12. **Architecture scope:** retain the current singleton/component and `GetComponent<T>()` style unless the user requests an architectural migration. Do not introduce a DI framework incidentally.

## Runtime Architecture Map

| Area | Main owners | Boundary |
|---|---|---|
| Lobby and transport | `LobbyManager`, `RelayManager`, `SessionManager`, `PlayerSpawnManager`, `SceneLoadSyncManager` | Lobby/Relay lifecycle, network start/stop, scene transition, server spawn |
| Turn orchestration | `TurnManager` | Sole server-side phase state machine and round loop |
| Player state | `PlayerState`, `PlayerInventory`, `ActionQueue`, `PlayerModifiers` | Replicated state plus server-only queued actions and per-turn modifiers |
| Items | `ItemManager`, `ItemDataSO` hierarchy, `ItemDropTable`, `ItemContext` | SO registry/configuration and server-side effect execution |
| Combat | `CombatResolver`, `TemperatureSystem`, `BuffDebuffSystem`, `CombatResult` | Server-side resolution and serializable outcome data |
| Match | `MatchManager` | Round score, best-of-three state, draw handling |
| Presentation | `CombatVFXManager`, `EnvironmentVFXManager`, `ScreenVFXManager`, `AZPlayerVisual`, `FPSVisualController` | Client visuals only; never authoritative gameplay |
| Inventory presentation | `InventoryPresenter`, `ItemWorldView`, `HoverRaycaster` | Replicated inventory to local/opponent world views and click routing |
| UI | `AZGameUI`, `AZLobbyUI`, `MiniGameHub`, `EmoteWheel`, `EmoteBubble` | Runtime-built player interaction and display |
| Audio | `GameAudioManager` | BGM, SFX, UI, environment, fan, and clock channels |

DDOL managers originate in `LobbyScene`. Game-scoped managers and presentation objects live in `GameScene`. Scene-scoped singleton references must be cleared or safely replaced across scene changes.

## Critical Data Flows

### Lobby to Game

```text
Lobby create/join
  -> Relay allocate/join and UnityTransport configuration
  -> NetworkManager starts host/client
  -> SessionManager requests NGO scene load
  -> PlayerSpawnManager spawns one Player NetworkObject per client
  -> TurnManager finds two PlayerState instances
  -> PrepPhase begins
```

### Turn and Combat

```text
PrepPhase
  -> server fan/recovery ticks
  -> client item intent / mini-game result / ready intent
  -> server validates and stores ActionQueue
AttackPhase
  -> delayed effects
  -> sub-item execution
  -> CombatResolver determines order, applies defense, executes main actions
  -> authoritative temperature and inventory mutations
  -> CombatResultData broadcast
  -> client VFX sequence and temporary HP-display overrides
ResolutionPhase
  -> death/winner check, inventory compaction, environment progression
  -> next PrepPhase or RoundOver
```

### Inventory and Item Selection

- `PlayerInventory.SlotStates` is a `NetworkList<ItemSlotNetData>` with a 12-slot cap.
- `ItemSlotNetData` is the compact network DTO: item ID, remaining uses, and flags. `ItemManager.allItems` is the ID registry.
- Initial round setup supplies the four basic items and four weighted random items. Threshold grants occur at 30, 20, and 10 degrees.
- `InventoryPresenter` is the client presentation authority for local and opponent world-item views. Respect its rebuild lock during combat VFX and resolve pending selections by item identity when slot compaction can shift indices.
- Mini-games run on the owning client, but the server controls eligibility, deadline, final acceptance, failure consumption, and PrepPhase timeout.

## Important File Routes

| Task | Inspect first |
|---|---|
| Current status or resume | `Docs/ACTIVE_CONTEXT.md`, then the referenced plan if one is active |
| Gameplay or balance | `Docs/GAME_DESIGN.md`, relevant item `.asset`, relevant effect class, and `TurnManager`/`CombatResolver` path |
| Broad architecture | `Docs/SYSTEM_DESIGN.md`, then current implementation files |
| Architecture or async refactor | `Docs/AI_TARGET_ARCHITECTURE.md`, `Docs/Plans/PLAN_018_architecture_migration.md`, `Docs/ARCHITECTURE_EVOLUTION_KO.md`, then current implementation files |
| Network/lobby | `Assets/Scripts/Core/Network/`, `LobbyScene.unity`, `Player.prefab` |
| Turn/combat | `Core/Turn/TurnManager.cs`, `Core/Combat/`, `Core/Player/ActionQueue.cs` |
| Item behavior | `Core/Item/`, `Core/Item/Data/`, `Core/Player/PlayerInventory.cs`, `Assets/Data/Items/` |
| Inventory visuals | `Core/Inventory/InventoryPresenter.cs`, `Core/Item/ItemWorldView.cs`, `UI/Game/AZGameUI.cs` |
| Character/VFX | `Core/Player/AZPlayerVisual.cs`, `FPSVisualController.cs`, `Core/Combat/*VFXManager.cs` |
| Mini-games | `Docs/MINIGAME_SYSTEM.md`, `UI/MiniGame/`, `PlayerState.cs` |
| Scene or prefab wiring | relevant `.unity`/`.prefab`, `.meta`, and owning component source; use live Unity inspection when available |
| Change history | `Docs/RECENT_CHANGES.md`, `Docs/CHANGES.md`, then Git history |

Paths in this table below `Assets/Scripts/` omit that shared prefix where obvious.

## Task Workflow

1. Inspect `git status --short`, relevant current-status context, and the exact symbols/assets in scope. Use `rg` or `rg --files`; do not trust historical paths without checking.
2. Distinguish observed implementation from intended design. If changing gameplay where code, SO assets, and `GAME_DESIGN.md` disagree, stop and ask which behavior is authoritative.
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

Unity MCP is optional infrastructure, not a prerequisite for static analysis. If it is available, discover the current editor instance dynamically and use the configured HTTP connection. Never call a forced `recompile_scripts` operation; save files and rely on Unity auto-compilation. If live Unity validation is unavailable, state that limitation rather than claiming runtime success.

For network changes, verify at minimum:

- host and remote-client behavior;
- RPC sender validation and phase/ownership guards;
- late subscription or late spawn behavior;
- disconnect/despawn cleanup;
- round reset and second-round reuse;
- player-index versus client-ID mapping.
- for activated four-player work, every seat, Host plus three remote clients, target visibility, deterministic resolution, and per-client disconnect behavior.

## Current High-Risk Areas

Confirm these against `Docs/ACTIVE_CONTEXT.md` and current code before related work; they are known verification debt, not permission to fix them outside the requested scope:

- `CombatVFXManager` may conflate ClientId and logical player index.
- Parts of `TurnManager` may contain P1-specific assumptions.
- `EnemyPlayer` renderer visibility has an unresolved runtime cause.
- Runtime tests remain pending for late join, disconnect, round reset, VFX during inventory rebuild, `CompactSlots` index changes, threshold grants, and Kids stealing.
- Some art/audio resources are missing, including the CoolBreeze wind SFX and several item sprites. Distinguish missing assets from code defects.

## Definition of Done

A change is complete only when the requested behavior is implemented, relevant authority and lifecycle paths were reviewed, available focused validation passed, unrelated worktree changes were preserved, and remaining runtime or asset limitations were explicitly reported. Do not mark historical plan/checklist items complete merely because code was edited.
