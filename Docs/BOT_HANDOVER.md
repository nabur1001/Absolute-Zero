# Bot AI Handover — Single-Player Opponent (초급/중급/고급/신)

> Purpose: hand this package to an external developer to build an AI opponent so the
> game can be played solo. Four difficulty tiers: 초급(beginner) / 중급(intermediate) /
> 고급(advanced) / 신(god). This doc lists the files to hand over and how the bot plugs in.

---

## 1. Recommended architecture — **server-side virtual player**

The match is **turn-based and host-authoritative** (NGO). All game state mutates on the host.
So the bot should be a **second `PlayerState` owned by the host/server** (NOT a second Relay
client). The host spawns a "bot player object" and a server-side AI driver picks the bot's
action every PrepPhase. No networking/Relay work is needed for the bot — it runs on the host.

Why not a headless second client: it would require a real NGO connection, a Relay slot, and a
separate build — heavy and unnecessary for a turn-based 1v1. The virtual-player approach reuses
the existing turn/combat pipeline verbatim.

### What the bot developer actually builds
A single decision module, e.g. `BotBrain` with:
```
ItemChoice DecideAction(BotObservation obs, Difficulty tier);
```
- Input: a read-only snapshot of the match (own/opponent temperature, own inventory, turn #,
  environment, timer, lock/ban flags).
- Output: which inventory slot to use this turn (or "none"), plus fan on/off intent.
Everything else (spawning the bot object, wiring it into the turn loop, submitting the action)
is glue the project owner provides — see §4 integration points.

### Recommended implementation: **Behavior Tree (BT)**
`DecideAction` is best implemented as a Behavior Tree — a great fit for turn-based decisioning:
- **Tick once per PrepPhase**, not per frame. One evaluation → one chosen action. (This is
  "decision on demand", not a continuously-running reactive tree.)
- **Blackboard = `BotObservation`** (temps, inventory/usable items, locks, environment, timer).
- **Leaf/action nodes select an intent** (an inventory slot or "none") — they do NOT execute
  game actions; the returned choice is handed to the submit glue (§2 outputs). Keep leaves
  side-effect-free so the tree is pure "read state → return choice".
- **Condition nodes** read the blackboard (e.g. `OpponentIsHot`, `SelfNearDeath`,
  `HasHealItem`, `PermanentLocked`).
- **Difficulty** = either separate trees per tier, or one tree whose branches are gated by a
  `Difficulty` blackboard value (higher tiers unlock lookahead / opponent-modeling subtrees).
- Library-agnostic: hand-rolled BT, Unity's `com.unity.behavior` package, or a 3rd-party BT
  asset all work — the `DecideAction` signature stays stable so the tree is swappable.
- Keep the tree **deterministic + unit-testable**: feed a `BotObservation`, assert the chosen slot.

---

## 2. The decision surface (inputs → outputs)

Each PrepPhase the bot reads state and submits one action, then "readies".

**Inputs (read):**
- `PlayerState.Temperature` (self & opponent via `TurnManager.GetPlayer(0/1)`)
- `PlayerInventory.SlotStates` + `PlayerInventory.GetItemData(slot)` → available items
- `ItemDataSO`: `Category`, `Persistence`, `SlotType`, `RequiresMiniGame`, `MiniGameType`, effect fields
- `PlayerState.IsBasicBlocked` (Blue Tape), `PlayerState.IsPermanentLocked` (permanent-item cooldown)
- `TurnManager`: `CurrentPhase`, `TurnNumber`, `RemainingTime`, `ActiveEnvironment`, `PrepStartServerTime`/`PrepDuration`
- `TemperatureSystem` rules (fan tick, recovery, thresholds, `MAX_TEMP`, death)
- `EnvironmentType` active effect (order/recovery modifiers)

**Outputs (submit, server-side):**
- Select an item: `PlayerState.SelectItemServerRpc(slot)` — the bot object is host-owned, so the
  host may invoke it directly (ownership check passes on the host).
- Ready up: `PlayerState.PressReadyServerRpc()`.
- Fan intent: `PlayerState.IsFanActive` (server-written).

---

## 3. Files to hand over

### A. Must understand + interface with (the "contract")
| File | Why |
|------|-----|
| `Assets/Scripts/Core/Player/PlayerState.cs` | Action API: SelectItem / PressReady / mini-game submit; NVs (Temperature, IsReady, HasSelectedItem, IsFanActive, IsBasicBlocked, IsPermanentLocked) |
| `Assets/Scripts/Core/Player/PlayerInventory.cs` | Slot states, item lookup, what's usable |
| `Assets/Scripts/Core/Player/ActionQueue.cs` | How a selected action is queued for the attack phase |
| `Assets/Scripts/Core/Turn/TurnManager.cs` | Turn state machine, phases, timer, `GetPlayer`, `WaitForPlayersRoutine` (currently needs **2** PlayerStates), attack resolution entry |
| `Assets/Scripts/Core/Combat/CombatResolver.cs` | How actions resolve (turn order, damage, defense) — needed to reason about outcomes |
| `Assets/Scripts/Core/Combat/TemperatureSystem.cs` | Temperature math: fan/recovery ticks, thresholds, MAX_TEMP, death |
| `Assets/Scripts/Core/Player/PlayerModifiers.cs` | Per-turn modifiers (defense, neutralize) |
| `Assets/Scripts/Core/Buff/BuffDebuffSystem.cs` | Delayed buffs/debuffs applied at turn start |

### B. Item data (what the bot chooses among)
| File | Why |
|------|-----|
| `Assets/Scripts/Core/Item/ItemEnums.cs` | `ItemCategory`, `ItemPersistence`, `ItemSlotType`, `SabotageType`, `MiniGameType`, `EnvironmentType` |
| `Assets/Scripts/Core/Item/Data/ItemDataSO.cs` + `AttackItemDataSO`, `DefenseItemDataSO`, `RecoveryItemDataSO`(if present), `SpecialItemDataSO`, `SabotageItemDataSO`, `DebuffItemDataSO`, `BuffItemDataSO` | Item semantics + `CanUse` gating |
| `Assets/Scripts/Core/Item/ItemManager.cs` | Item registry / drop table |
| `Assets/Data/Items/**/*.asset` | The actual item roster + tuned values |

### C. Spawn / match integration (glue the owner provides)
| File | Why |
|------|-----|
| `Assets/Scripts/Core/Network/PlayerSpawnManager.cs` | How players spawn (`SpawnAsPlayerObject(clientId)`); the bot needs a host-owned spawn path |
| `Assets/Scripts/Core/Match/MatchManager.cs` | Round/match lifecycle (`StartRound`) |
| `Assets/Prefabs/Player.prefab` | The player NetworkObject (PlayerState + PlayerInventory + AZPlayerVisual) the bot object mirrors |

### D. Mini-games (bots must bypass the UI)
| File | Why |
|------|-----|
| `Assets/Scripts/UI/MiniGame/MiniGameHub.cs`, `MiniGameType` (in ItemEnums) | Items with `RequiresMiniGame` start a mini-game on the **owner client**. A bot has no UI → it must **auto-submit** a result via `PlayerState.SubmitMiniGameResultServerRpc(slot, success)` instead of opening the UI (success rate can scale with difficulty). |

### E. Design / rules (play by these)
| File | Why |
|------|-----|
| `Docs/GAME_DESIGN.md` | Rules, target values, win/loss |
| `Docs/GAME_SYSTEMS.md` | System behavior |
| `Docs/NETWORK_ARCHITECTURE.md` | Host-authority model |

---

## 4. Integration points the project owner wires (NOT the bot dev)

These are the seams between "the game" and "the bot brain". Recommended to add:

1. **Spawn a host-owned bot player.** New path in `PlayerSpawnManager` (or a `BotSpawnManager`)
   that instantiates the Player prefab and `SpawnAsPlayerObject(NetworkManager.LocalClientId)`
   as a *second* player owned by the host, tagged as a bot.
2. **Let the turn loop start with 1 human + 1 bot.** `TurnManager.WaitForPlayersRoutine`
   currently blocks until **2 PlayerState** objects exist — a bot object satisfies this once spawned.
3. **A bot flag on `PlayerState`** (e.g. `IsBot`) so:
   - `StartMiniGameClientRpc` is NOT shown for the bot; instead the bot auto-resolves and calls
     `SubmitMiniGameResultServerRpc`.
   - A `BotController` MonoBehaviour on the bot object drives `DecideAction` + submit each PrepPhase.
4. **Single-player entry (UI):** the lobby's "봇 생성" button + difficulty dropdown → host starts
   a local session (host only, no Relay join needed) and spawns the bot at the chosen difficulty.

The **bot dev only needs A + B + E** to write `DecideAction`. C + D are the owner's glue, but
share them so the dev understands the constraints (server-authority, mini-game bypass, one-action-per-turn).

---

## 5. Gotchas

- **Server authority:** the bot must mutate state only on the host. Since the bot object is
  host-owned, calling its ServerRpcs on the host executes locally — fine. Never write NetworkVariables from a non-server context.
- **One action per turn:** `HasSelectedItem` gates a single main selection; sub/instant items resolve immediately.
- **Locks:** respect `IsBasicBlocked` (Blue Tape) and `IsPermanentLocked` (permanent-item one-turn cooldown) — `ItemDataSO.CanUse` already rejects, but the bot should not *pick* a locked item.
- **Mini-game items:** at low difficulty prefer non-mini-game items or auto-fail some; at high difficulty auto-succeed.
- **Timing:** the bot should decide within the prep window (`PrepDuration`), then `PressReady`.

## 6. Difficulty knobs (as BT complexity per tier)
Same tree family, growing capability. Suggested per tier:
- **초급:** shallow tree — pick a safe/random usable item, ignore opponent temp, low mini-game success, slow to ready.
- **중급:** basic heuristic branches (heal when hot, attack when opponent hot), medium mini-game success.
- **고급:** adds a 1-turn lookahead subtree (temperature after fan/recovery + likely combat), uses defense/sabotage, high mini-game success.
- **신:** adds resolution-modeling nodes (`CombatResolver`/`TemperatureSystem`) + opponent modeling; counters permanent-lock/blue-tape windows; ~100% mini-games.

Reference skills in this repo for patterns: `unity-state-machines` (FSM vs BT decision + testing), `unity-npc-behavior` (perception → decision → action layering).

## 7. Do NOT modify (for the bot dev)
Networking bootstrap (`RelayManager`, `SessionManager`, `LobbyManager`), scene load, UI build code.
The bot dev's surface is a single decision function; the owner owns all wiring.
