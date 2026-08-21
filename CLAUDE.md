# Absolute Zero — Claude Working Guidelines

> Unity 6 (6000.3.11f1) | C# | 2.5D current 1v1, future up-to-4-player Multiplayer Turn-Based Deathmatch | Korean comments in code

---

## Core Principles (ALWAYS FOLLOW)

1. **Server-authoritative: all game state mutations must happen on Host/Server** — clients send action selection only via Rpc; temperature, turn state, and win/loss are computed server-side. Direct client-side state mutation is forbidden — causes desync.
2. **Replicate current state by visibility contract** — reconnectable public state such as temperature, turn phase, and timer uses server-written NetworkVariable/NetworkList state. Secret pending action, selected item, target, and ready-order data remain server-only until an approved reveal; never place them in an Everyone-readable snapshot merely to reduce NetworkVariable count.
3. **Never modify NetworkVariable.Value on client** — only the server/host may write to NetworkVariable. Client writes cause silent failure or exception depending on NetworkVariable permissions.
4. **Rpc direction must match authority model** — new NGO code uses universal `[Rpc(SendTo.Server)]` for client intent and an appropriate server-to-client `[Rpc(...)]` target for result/presentation publication. Validate sender identity from receive params. Legacy `[ServerRpc]`/`[ClientRpc]` call sites migrate only inside an approved phase.
5. **Turn phase transitions must be atomic** — PrepTurn→AttackTurn→Resolution must go through a single state machine. Direct enum assignment from multiple call sites causes race conditions in networked context.
6. **Cache `WaitForSeconds`** — repeated `new WaitForSeconds()` forbidden (GC pressure in coroutines).
7. **Check `Docs/SAFETY_RULES.md` before any code modification**
8. **Match repository documentation language policy** — write new Markdown in English unless the user explicitly requests Korean, and preserve the language/style of an existing file. `ARCHITECTURE_EVOLUTION_KO.md` and the Korean execution plans are intentional human-facing exceptions.
9. **3-strike retry limit** — if the same action fails 3 times in a row, stop retrying and switch to an alternative approach.
10. **Follow SOLID principles** — apply pragmatically for core systems (turn manager, network, UI).
11. **ScriptableObject configs are runtime read-only** — SO field modification at runtime permanently corrupts Editor asset data. Read at init, copy to runtime class.
12. **Never call `mcp__unityMCP__recompile_scripts`** — breaks MCP WebSocket connection, requires manual restart. Rely on Unity Editor auto-recompile.
13. **Invoke `unity-*` skills when modifying Unity C# code** — 22 Unity reference skills are installed (lifecycle, state-machines, async-patterns, npc-behavior, procedural-gen, etc.). Before writing or refactoring Unity systems, invoke the matching skill to check correct patterns and avoid common mistakes.
14. **Prefer MCP automation over manual instructions** — when Unity Editor settings, scene config, or component setup needs changing, use MCP tools (`execute_code`, `manage_components`, `manage_gameobject`, etc.) to do it directly. Only give manual instructions for things that genuinely cannot be automated (e.g., Unity Cloud Dashboard web UI).
15. **Namespace final segment must not collide with imported type names** — `AbsoluteZero.UI.Lobby` conflicts with `Unity.Services.Lobbies.Models.Lobby`, causing CS0118. Suffix with category instead (e.g., `LobbyUI`, `PlayerVisuals`).
16. **Design-first development** — before implementing or modifying gameplay systems (items, combat, temperature, turn flow, mini-games), read `Docs/GAME_DESIGN.md` and verify target values/behavior match the design spec. Implementation must not diverge from design without explicit user approval.
17. **Architecture migration follows the approved phase sequence** — for architecture, async, initialization, player identity, event, pooling, player-count scaling, or asmdef refactoring, read `Docs/AI_TARGET_ARCHITECTURE.md` (target contracts), `Docs/Plans/PLAN_018_architecture_migration.md` (behavior-preserving phase sequence), and `Docs/ARCHITECTURE_EVOLUTION_KO.md` (design rationale and flows). Actual four-player gameplay belongs to `Docs/Plans/PLAN_019_four_player_expansion.md` and must not be mixed into PLAN_018. Priority: (1) current user request and actual code, (2) AI_TARGET_ARCHITECTURE contracts, (3) the active plan. Do not implement beyond the current active Phase without user approval.
18. **Design-pattern-first planning** — before writing implementation plans:
    1. **Pattern selection**: identify candidate design patterns (e.g., Command, Observer, Mediator, State Machine, Strangler Fig) for the target system, compare trade-offs, and select one with rationale
    2. **API/package audit**: inspect exact installed package source/lock data and official documentation; check newer compatible APIs, deprecations, and version-specific capabilities. Do not change a package version without explicit approval plus compatibility, rollback, and multiplayer test gates
    3. **Pattern-to-code mapping**: document in the plan how the pattern maps to concrete classes, responsibilities, and data flow before writing any code
    Plans without these three steps are considered incomplete.
19. **Two-to-four-player readiness without premature gameplay change** — new architecture APIs use Registry/roster collections and never introduce `_p1`/`_p2`, `1 - index`, or ClientId-as-seat assumptions. PLAN_018 preserves current 1v1 behavior. Stable seats, reconnect mapping, multi-target combat, victory rules, and four-player UI are implemented only under PLAN_019 after GAME_DESIGN decisions.
20. **End-of-phase harness verification** — after completing each work phase:
    - `Docs/Plans/PLAN_NNN_*.md` — all completed tasks marked `[x]`
    - `Docs/RECENT_CHANGES.md` — change list recorded at top
    - `Docs/ACTIVE_CONTEXT.md` — status + last modified files updated
    - `Docs/CHANGES.md` — high-level entry added
    - `Docs/SAFETY_RULES.md` — new rules added if lessons learned

---

## Reference Table

| Task Type | Read First | Path |
|-----------|-----------|------|
| **New session start** | **SESSION_SETUP.md** | **`Docs/SESSION_SETUP.md`** |
| Before code changes | SAFETY_RULES.md | `Docs/SAFETY_RULES.md` |
| **Session resume** | **ACTIVE_CONTEXT.md** | **`Docs/ACTIVE_CONTEXT.md`** |
| Recent code changes | RECENT_CHANGES.md | `Docs/RECENT_CHANGES.md` |
| Plan history | Plans/ | `Docs/Plans/PLAN_NNN_*.md` |
| **Before gameplay changes** | **GAME_DESIGN.md** | **`Docs/GAME_DESIGN.md`** |
| Game design & rules | GAME_DESIGN.md | `Docs/GAME_DESIGN.md` |
| Game systems | GAME_SYSTEMS.md | `Docs/GAME_SYSTEMS.md` |
| Network architecture | NETWORK_ARCHITECTURE.md | `Docs/NETWORK_ARCHITECTURE.md` |
| **Architecture refactor** | **AI_TARGET_ARCHITECTURE.md** | **`Docs/AI_TARGET_ARCHITECTURE.md`** |
| Architecture phases | PLAN_018 | `Docs/Plans/PLAN_018_architecture_migration.md` |
| Future four-player feature | PLAN_019 | `Docs/Plans/PLAN_019_four_player_expansion.md` |
| Architecture rationale | ARCHITECTURE_EVOLUTION_KO | `Docs/ARCHITECTURE_EVOLUTION_KO.md` |
| Known bugs | KNOWN_ISSUES.md | `Docs/KNOWN_ISSUES.md` |
| Network code reference | ArenaCombat_server | `C:\Users\paek6\Unity Project\ArenaCombat_server` (source project for network migration) |

---

## Architecture Summary

- **Network model:** NGO 2.11.2 (Netcode for GameObjects), Unity Relay (DTLS), Host-authoritative
- **No DI** — singleton managers + `GetComponent<T>()` pattern (Unity standard)
- **Namespace:** `AbsoluteZero` for all code
- **Turn system:** `TurnManager` — single state machine controlling WaitingForPlayers / PrepPhase / AttackPhase / ResolutionPhase / RoundOver
- **Data sync:** current code uses individual server-written NetworkVariables and NetworkLists; any future snapshot grouping follows visibility and update-semantics rules in AI_TARGET_ARCHITECTURE
- **Action input:** `[Rpc(SendTo.Server)]` from client → host stores in local buffer → simultaneous resolution on AttackTurn
- **UI:** All UI is **runtime-built** (no Inspector wiring) — `AZGameUI` and `AZLobbyUI` construct Canvas/buttons/text in code. Uses TMP (TextMeshPro)

### Scenes
- **LobbyScene** (build index 0) — start scene, has NetworkManager + Managers (DDOL)
- **GameScene** (build index 1) — loaded via `NetworkManager.SceneManager` after Relay connect

### Key Singletons (all DDOL, on "Managers" GameObject in LobbyScene)
- `LobbyManager` — lobby CRUD, heartbeat, polling, auto-inits Unity Services in `Start()`
- `RelayManager` — Relay allocation + join
- `SessionManager` — scene transition + disconnect handling
- `PlayerSpawnManager` — spawns Player prefab on client connect

### Game Systems (in GameScene)
- `AbsoluteZeroTurnManager` (NetworkBehaviour) — server-authoritative state machine
- `AZGameUI` (MonoBehaviour) — runtime-built Canvas, no Inspector wiring
- `AZPlayerVisual` (NetworkBehaviour on Player prefab) — capsule with temp-based color

### NetworkManager Config
- UnityTransport component (DTLS via Relay)
- `DefaultNetworkPrefabs.asset` → contains Player prefab
- PlayerPrefab = null (PlayerSpawnManager handles spawning)
- EnableSceneManagement = true

---

## Project Layout

```
Assets/
├── Scenes/
│   ├── LobbyScene.unity           # Lobby + NetworkManager (build index 0)
│   └── GameScene.unity            # Turn-based game scene (build index 1)
├── Scripts/
│   ├── Core/
│   │   ├── Game/                  # AbsoluteZeroTurnManager
│   │   └── Network/              # LobbyManager, RelayManager, SessionManager, PlayerSpawnManager
│   └── UI/
│       ├── Game/                  # AZGameUI
│       └── Lobby/                 # AZLobbyUI
├── Prefabs/
│   └── Player.prefab             # NetworkObject with AZPlayerVisual
├── Settings/                     # URP render pipeline settings
└── TextMesh Pro/                 # TMP Essential Resources
```

---

## Session Continuity Protocol (ALWAYS FOLLOW)

> Mandatory workflow to minimize re-exploration on token limit / session restart.

### On Session Start (Required)
1. **Read `Docs/ACTIVE_CONTEXT.md`** — current state, in-progress work, last modified files
2. If work is in progress, read the active plan file (`Docs/Plans/PLAN_NNN_*.md`)
3. If needed, read `Docs/RECENT_CHANGES.md` for recent code changes

### On Work Start
1. **Create plan file:** `Docs/Plans/PLAN_NNN_short_description.md` (sequential numbering, never delete)
2. Write checklist (`- [ ]`) in the plan — detailed per-step items
3. **Update `Docs/ACTIVE_CONTEXT.md`** — change status to "In Progress", reference active plan
4. **Work Scope Declaration** — before writing any code:
   - Grep to confirm target symbol/file exists at expected location — never trust conversation history for file paths
   - Declare in the plan: files/symbols to modify AND "Will NOT touch" list
   - If grep reveals target moved or changed — update plan before proceeding
5. **Design Pattern Verification** — before writing any implementation code:
   - List candidate design patterns for the target system
   - Select one with trade-off rationale (why this pattern, why not alternatives)
   - Verify against the exact installed Unity/NGO/package versions and official vendor documentation:
     - Are there newer APIs that simplify the pattern? (e.g., NetworkVariable vs NetworkList, Awaitable vs Coroutine)
     - Are any planned APIs deprecated in current version?
     - Does the package version support the pattern's requirements?
     - Is a newer compatible non-legacy API available, and can it be adopted without broadening the phase?
   - Document in plan: candidate patterns → selected trade-off → Class mapping → Data flow → Authority/visibility model → version evidence → validation and rollback gate
   - Never turn an API audit into an unapproved package upgrade.

### During Work
1. **After each step completion** — update plan checkboxes (`- [ ]` → `- [x]`)
2. **After each code change** — add record at top of `Docs/RECENT_CHANGES.md`:
   - File path, change type (modified/created/deleted), change summary
   - For significant code changes: include before/after code snippets
3. **Mid-save:** periodically update ACTIVE_CONTEXT.md during complex work (next steps, blockers, etc.)
4. **Scope Guard** — do not expand scope without approval:
   - When discovering unexpected issues during work, STOP expanding scope
   - Record in plan under a "Discovered Issues" section
   - Report to user: ask whether to fix now or track for later
   - Continue with original task only — exception: blocking issues that prevent current task

### On Work Completion
1. Mark plan file status as `✅ Complete`
2. Update `Docs/ACTIVE_CONTEXT.md` — record completion + set status to "Idle"
3. Add high-level entry to `Docs/CHANGES.md`
4. If errors/lessons learned — add rules to `Docs/SAFETY_RULES.md`

### File Structure
```
Docs/
├── ACTIVE_CONTEXT.md      # Current work state (read first on session start)
├── RECENT_CHANGES.md      # Recent code change details (file/line/content)
├── CHANGES.md             # High-level change history (commit message style)
└── Plans/
    ├── PLAN_001_*.md      # Completed plans (never delete)
    ├── PLAN_002_*.md      # Next plan
    └── ...                # Plans accumulate → full work history
```

### RECENT_CHANGES.md Record Format
```markdown
## [Date] Session: Work Description

### Summary
One-line summary

### Change List
| # | File | Type | Description |
|---|------|------|-------------|
| 1 | `path/file.cs` | Modified | Change description |

### Code Change Details (for modifications)
**file.cs line 42:** `oldCode` → `newCode` (reason)
```

---

## Knowledge Placement Decision Tree

> Where to write new knowledge — single source of truth, no duplicates.

| Question | Destination |
|----------|-------------|
| Violating this breaks the system? | `Docs/SAFETY_RULES.md` |
| Changes between sessions? (status, progress) | `Docs/ACTIVE_CONTEXT.md` or `Docs/RECENT_CHANGES.md` |
| Only relevant to a specific task type? | `.claude/skills/{skill}/SKILL.md` |
| Otherwise (permanent project knowledge) | `CLAUDE.md` |

**Single source of truth is required** — if information exists in one location, do not duplicate it in another. Reference the source instead.

---

## Knowledge Proposal Protocol

> Protect harness integrity — propose changes, don't silently inject.

- **CLAUDE.md and SAFETY_RULES.md modifications require user approval** — propose as a diff, wait for explicit confirmation before writing
- **Exception: agent-updated files** — `Docs/RECENT_CHANGES.md`, `Docs/ACTIVE_CONTEXT.md`, plan checkboxes (`- [ ]` → `- [x]`), and `Docs/CHANGES.md` are updated directly without approval
- Format proposals as: "Proposed addition to [file]: `[content]`" — user responds approve/reject/modify

---

## Deterministic Rule Writing Standard

> All rules in CLAUDE.md and SAFETY_RULES.md must follow this format.

- **Format:** `[Action] is forbidden/required — [concrete consequence]`
- **Rules must be:**
  - **Measurable** — can be verified by grep, read, or test (not subjective)
  - **Consequential** — states what breaks if violated
  - **Actionable** — clear what to do or not do

**Bad:** "Be careful with network state"
**Good:** "Client-side NetworkVariable.Value assignment is forbidden — causes silent desync in host-authoritative model"
