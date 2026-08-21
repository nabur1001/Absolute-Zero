# Absolute Zero — System Design Specification

> Generated: 2026-08-20 | Unity 6 (6000.3.11f1) | NGO 2.11.2 | C# | 2.5D 1v1 Turn-Based Deathmatch
> Post-Architecture Migration (PLAN_018 Phase 1~7 Complete)

---

## Table of Contents

1. [Architecture Overview](#1-architecture-overview)
2. [Session & Network System](#2-session--network-system)
3. [Turn System (State Machine)](#3-turn-system)
4. [Player System](#4-player-system)
5. [Item System](#5-item-system)
6. [Combat System](#6-combat-system)
7. [Visual System](#7-visual-system)
8. [UI System](#8-ui-system)
9. [Audio System](#9-audio-system)
10. [Emote System](#10-emote-system)
11. [Environment System](#11-environment-system)
12. [Mini-Game System](#12-mini-game-system)
13. [Match System](#13-match-system)
14. [Assembly Architecture](#14-assembly-architecture)
15. [Design Patterns Summary](#15-design-patterns-summary)
16. [Data Flow Diagrams](#16-data-flow-diagrams)
17. [File Index](#17-file-index)

---

## 1. Architecture Overview

### Network Model

```
┌───────────────────────────────────────────────────────────┐
│                     HOST (Server)                         │
│                                                           │
│  AppBootstrapper ──► NetworkSessionCoordinator             │
│       │                    ├── UnityServicesGateway         │
│       │                    ├── LobbyGateway                │
│       │                    ├── RelayGateway                 │
│       │                    ├── NgoNetworkRuntime            │
│       │                    └── SceneTransitionService       │
│       ▼                                                    │
│  MatchCompositionRoot                                      │
│       ├── PlayerRegistry (Identity system)                  │
│       ├── ItemManager                                      │
│       └── MatchManager                                     │
│                                                           │
│  TurnManager ──► CombatEngine ──► CombatResolver           │
│       │               │                │                   │
│       │               │         ItemEffectApplicator        │
│       │               ▼                │                   │
│       │     PresentationBarrier    TemperatureSystem        │
│       │                                                    │
│       ├── RoundLifecycleService                            │
│       └── EnvironmentRuleService                           │
│                                                           │
│  PlayerState[] ◄──── NetworkVariable ────► PlayerState[]   │
│                   (Temperature, Phase,                     │
│                    Ready, Timer...)                         │
│       │                                    │               │
│       ▼            Unity Relay             ▼               │
│  ┌─────────────── (DTLS) ──────────────────┐              │
│  │              Network Sync                │              │
└──┼──────────────────────────────────────────┼──────────────┘
   │                                          │
   ▼                                          ▼
┌──────────────┐                    ┌──────────────┐
│ CLIENT (P1)  │                    │ CLIENT (P2)  │
│              │                    │              │
│ AZGameUI     │                    │ AZGameUI     │
│ CombatVFXMgr │                    │ CombatVFXMgr │
│ FPSVisual    │                    │ FPSVisual    │
│ (1P View)    │                    │ (1P View)    │
└──────────────┘                    └──────────────┘
```

- **Authority:** Host-authoritative — all state mutations (temperature, turn phase, item effects) happen on server only
- **Sync:** `NetworkVariable<T>` for shared state, `[Rpc(SendTo.Server)]` for client→server input, `[Rpc(SendTo.Everyone)]` for server→client broadcast
- **Transport:** Unity Relay (DTLS) via `UnityTransport`
- **Lobby:** Unity Lobby Service (join by code, heartbeat, polling)
- **Assembly Separation:** `AbsoluteZero.Core` (domain) + `AbsoluteZero.UI` (presentation), compiler-enforced unidirectional dependency (UI→Core only)

### Scene Flow

```
LobbyScene (Build Index 0)            GameScene (Build Index 1)
┌──────────────────────────────┐      ┌──────────────────────────────┐
│ NetworkManager (DDOL)        │      │ MatchCompositionRoot         │
│ AppBootstrapper (DDOL)       │      │ TurnManager                  │
│ NetworkSessionCoordinator    │      │ ItemManager                  │
│   (DDOL)                     │      │ MatchManager                 │
│ LobbyManager (DDOL)         │ ──►  │ CombatVFXManager             │
│ RelayManager (DDOL)         │ Net  │ EnvironmentVFXManager        │
│ SessionManager (DDOL)       │ Load │ GameAudioManager             │
│ PlayerSpawnManager (DDOL)   │      │ AZGameUI                     │
│ LoadingScreenManager (DDOL) │      │ EnemyPlayer (scene object)   │
│ AZLobbyUI                   │      │ FPSAnimSpawn (marker)        │
└──────────────────────────────┘      │ PlayerItem1~10 (markers)     │
                                      │ EnemyItem1~10 (markers)      │
                                      └──────────────────────────────┘
```

### Singleton Landscape

| Singleton | Lifetime | Base | Purpose |
|-----------|----------|------|---------|
| `AppBootstrapper` | DDOL | MonoBehaviour | Async initialization orchestrator |
| `NetworkSessionCoordinator` | DDOL | MonoBehaviour | Session state machine (lobby/relay/connect) |
| `LobbyManager` | DDOL | MonoBehaviour | Unity Lobby Service CRUD, heartbeat, polling |
| `RelayManager` | DDOL | MonoBehaviour | Relay allocation/join, transport config |
| `SessionManager` | DDOL | MonoBehaviour | Disconnect handling, scene-level events |
| `PlayerSpawnManager` | DDOL | MonoBehaviour | Server-side player prefab spawning |
| `LoadingScreenManager` | DDOL | MonoBehaviour | Scene transition overlay |
| `MatchCompositionRoot` | GameScene | MonoBehaviour | PlayerRegistry + scene service locator |
| `TurnManager` | GameScene | NetworkBehaviour | Server-authoritative turn state machine |
| `ItemManager` | GameScene | NetworkBehaviour | Item SO catalog + drop table |
| `MatchManager` | GameScene | NetworkBehaviour | Bo3 round/match scoring |
| `CombatVFXManager` | GameScene | MonoBehaviour | Client-side combat animation sequencer |
| `EnvironmentVFXManager` | GameScene | MonoBehaviour | Environment NPC/effect builder |
| `GameAudioManager` | GameScene | MonoBehaviour | 6-channel audio management |
| `AZGameUI` | GameScene | MonoBehaviour | Runtime-built game Canvas |
| `InventoryPresenter` | GameScene | MonoBehaviour | World-space item card management |
| `HoverRaycaster` | GameScene | MonoBehaviour | Physics raycast for item selection |
| `CameraShake` | GameScene | MonoBehaviour | Screen shake effect |
| `FPSVisualController` | GameScene | MonoBehaviour | 1P hand/item sprite |
| `ScreenVFXManager` | GameScene | MonoBehaviour | Lazy (auto-creates on first access) |

---

## 2. Session & Network System

### Initialization Sequence (AppBootstrapper)

```
App Launch → LobbyScene loads
      │
      ▼
AppBootstrapper.Start()
      │  async InitializeSequenceAsync()
      │
      ▼
NetworkSessionCoordinator.InitializeAsync()
      │  Offline → Initializing
      │
      ├── UnityServicesGateway.InitializeAndSignInAsync(profile)
      │     ├── UnityServices.InitializeAsync(options)
      │     └── AuthenticationService.Instance.SignInAnonymouslyAsync()
      │
      ├── TrySubscribeNgoCallbacks()
      │     └── NetworkManager.OnClientStopped += OnNetworkStopped
      │
      └── Initializing → Ready
               │
               ▼
         AppBootstrapper.IsReady = true
         OnReady?.Invoke()
```

### Session State Machine (NetworkSessionCoordinator)

```
┌──────────┐     Init     ┌──────────────┐     Success    ┌────────┐
│ Offline  │ ──────────► │ Initializing  │ ────────────► │ Ready  │
└──────────┘              └──────────────┘                └────┬───┘
      ▲                         │ Failure                      │
      │                         ▼                              │
      │                   ┌──────────┐                         │
      │                   │ Failed   │ ◄─── any error ─────────│
      │                   └──────────┘                         │
      │                                                        │
      │              Create / Join / Start                     │
      │                                                        ▼
      │                                               ┌──────────────┐
      │                                               │ Connecting   │
      │                                               │              │
      │                                               │ Operations:  │
      │                                               │ • Creating   │
      │                                               │   Lobby      │
      │                                               │ • Joining    │
      │                                               │   Lobby      │
      │                                               │ • Allocating │
      │                                               │   Relay      │
      │                                               │ • Starting   │
      │                                               │   Host/Client│
      │                                               └──────┬───────┘
      │                                                      │
      │                                                      ▼
      │                                               ┌──────────────┐
      │          LeaveAsync()                          │ LoadingGame  │
      │  ┌──────────────────────┐                     └──────┬───────┘
      └──│   Disconnecting     │ ◄─────────────────────┐     │
         └──────────────────────┘                       │     ▼
                                                  ┌─────┴────────┐
                                                  │   InGame     │
                                                  └──────────────┘
```

### Gateway / Interface Contracts

| Interface | Implementation | Responsibility |
|-----------|---------------|----------------|
| `IUnityServicesGateway` | `UnityServicesGateway` | Unity Services init + auth sign-in |
| `ILobbyGateway` | `LobbyGateway` | Lobby CRUD (Create/Join/Update/Delete/RemovePlayer) |
| `IRelayGateway` | `RelayGateway` | Relay Allocate/Join, returns server data + join code |
| `INetworkRuntime` | `NgoNetworkRuntime` | NGO StartHost/StartClient/Shutdown/LoadScene |
| `ISceneTransitionService` | `SceneTransitionService` | Scene load fallback (non-network) |

All gateway methods return `Result<T>` — a discriminated union carrying either `Value` or `(ErrorCode, ErrorMessage)`.

### OperationScope (Compensation Pattern)

```csharp
var scope = BeginOperation();             // increments generation counter
scope.PushCompensation(() => Cleanup());  // stack-based rollback

// If scope.IsStale → another operation superseded this one
// On failure → scope.RunCompensations() runs LIFO cleanup
```

### Connection Flow (Host)

```
[Player A: Host]                         [Player B: Client]
      │                                        │
      ▼                                        │
 Coordinator.CreateLobbyAsync()                │
      │  LobbyGateway.CreateAsync()            │
      ▼                                        │
 Coordinator.StartMatchAsHostAsync()            │
      │  1. RelayGateway.AllocateAsync()        │
      │  2. NgoNetworkRuntime.StartHost()       │
      │  3. LobbyGateway.UpdateAsync()          │
      │     (publish RelayJoinCode)             │
      │  4. LoadNetworkScene("GameScene")       │
      │                                        ▼
      │                              Coordinator.JoinGameAsync(code)
      │                                        │
      │                              1. LobbyGateway.JoinByCodeAsync()
      │                              2. WaitForRelayCodeAsync()
      │                              3. RelayGateway.JoinAsync(code)
      │                              4. NgoNetworkRuntime.StartClient()
      │                              5. (auto-loaded via NGO SceneManager)
      │                                        │
      ▼                                        ▼
 PlayerSpawnManager.SpawnPlayerForClient()
      │  for each connected client
      ▼
 MatchCompositionRoot → PlayerRegistry
      │  RegisterPending → PromoteToReady
      ▼
 TurnManager.WaitForPlayersRoutine()
      │  Registry.ReadyCount >= 2
      ▼
 PrepPhaseRoutine() ─── game loop starts
```

### Key Network Classes

| Class | Base | Purpose |
|-------|------|---------|
| `AppBootstrapper` | MonoBehaviour (DDOL) | Orchestrates async initialization of Coordinator |
| `NetworkSessionCoordinator` | MonoBehaviour (DDOL) | Session state machine, gateway orchestration, compensation rollback |
| `LobbyManager` | MonoBehaviour (DDOL) | Unity Lobby Service heartbeat (15s), polling (2s), player data, event bridge for UI |
| `RelayManager` | MonoBehaviour (DDOL) | Relay allocation/join state, transport config |
| `SessionManager` | MonoBehaviour (DDOL) | Legacy disconnect handling, static events for loading screen |
| `PlayerSpawnManager` | MonoBehaviour (DDOL) | Server-side player prefab spawning at scene markers |

### Core→UI Decoupling Events (SessionManager)

```csharp
// SessionManager fires these instead of directly calling LoadingScreenManager
public static event Action OnLoadingShow;
public static event Action OnLoadingHide;

// LoadingScreenManager subscribes in Awake()
SessionManager.OnLoadingShow += Show;
SessionManager.OnLoadingHide += ForceHide;
```

---

## 3. Turn System

### State Machine

```
                    ┌──────────────────┐
                    │ WaitingForPlayers│ ◄── game start
                    │    (Phase 0)     │
                    └────────┬─────────┘
                             │ Registry.ReadyCount >= 2
                             ▼
              ┌──────────────────────────┐
     ┌───────►│      PrepPhase           │
     │        │       (Phase 1)          │
     │        │                          │
     │        │  • Fan cooling ticks     │
     │        │  • Recovery ticks        │
     │        │  • Item selection        │
     │        │  • Mini-games            │
     │        │  • Emotes                │
     │        │  • Timer countdown       │
     │        │  • Env effects (Kids,    │
     │        │    Ambulance at Turn 3)  │
     │        └────────┬─────────────────┘
     │                 │ both Ready or timeout
     │                 │ + emote display wait
     │                 ▼
     │        ┌──────────────────────────┐
     │        │     AttackPhase          │
     │        │      (Phase 2)          │
     │        │                          │
     │        │  • BuffSystem.Process    │
     │        │  • CombatEngine.Resolve  │
     │        │  • CombatVFX sequence    │
     │        │  • PresentationBarrier   │
     │        │    wait for client ACK   │
     │        └────────┬─────────────────┘
     │                 │
     │                 ▼
     │        ┌──────────────────────────┐
     │        │   ResolutionPhase        │
     │        │      (Phase 3)          │
     │        │                          │
     │        │  • Check winner → Round  │
     │        │  • Compact inventories   │
     │        │  • Environment announce  │
     │        │    (after Turn 1)        │
     │        └────┬──────────┬──────────┘
     │             │          │
     │   no death  │          │ someone died
     │             │          ▼
     │             │  ┌───────────────────┐
     └─────────────┘  │   RoundOver       │
                      │    (Phase 4)      │
                      │                   │
                      │  • Death sequence  │
                      │  • MatchManager    │
                      │    score update    │
                      │  • 3s wait         │
                      └────┬──────────────┘
                           │
                   ┌───────┴────────┐
                   │                │
             match complete    next round
                   │                │
                   ▼                ▼
              [game ends]    StartNextRound()
                              RoundLifecycleService
                                .ResetPlayersForNewRound()
                              → PrepPhase
```

### TurnManager Key Properties

| NetworkVariable | Type | Purpose |
|-----------------|------|---------|
| `CurrentPhase` | `TurnPhase` | Current state machine phase |
| `TurnNumber` | `int` | Current turn within round |
| `PrepStartServerTime` | `double` | Server time when prep started (for client timer sync) |
| `PrepDuration` | `float` | Prep phase length (20s default, 10s for SummerVacation) |
| `RemainingTime` | `int` | Countdown seconds (synced for UI) |
| `LastRoundWinner` | `int` | -1=none, 0=P1, 1=P2 |
| `ActiveEnvironment` | `EnvironmentType` | Current environment variable |

### Extracted Services (from TurnManager)

| Service | Type | Responsibility |
|---------|------|----------------|
| `RoundLifecycleService` | Plain class | Round reset, force-ready, fan revert, death determination |
| `EnvironmentRuleService` | Plain class | Environment selection, duration/recovery rules, Kids/Ambulance effects |
| `CombatEngine` | Plain class | Pre-combat snapshot capture, combat resolution orchestration |
| `ITurnContext` | Interface | Phase/state queries for external consumers |

### Emote Window Logic

```
PrepPhase start → _emoteWindowClosed = false
                  AcceptEmotes = true (PrepPhase && !closed)

Both players Ready (or timeout)
     │
     ▼
_emoteWindowClosed = true   ← no more emotes accepted
     │
     ▼
Calculate emoteRemain = EMOTE_DISPLAY_SEC(1.0) - (now - lastEmote)
     │
     ▼ wait emoteRemain (if positive)
     │
     ▼
AttackPhaseRoutine()
```

---

## 4. Player System

### Identity & Registry (Phase 1)

```
PlayerIdentity (readonly struct)
  ├── PlayerIndex (byte)       ← 0=P1, 1=P2
  └── ClientId (ulong)         ← NGO ClientId

PlayerBinding (sealed class)
  ├── Identity (PlayerIdentity)
  ├── State (PlayerState)
  ├── Inventory (PlayerInventory)
  ├── NetworkObject
  └── IsValid (bool)

PlayerRegistry (IReadOnlyPlayerRegistry)
  ├── _byClientId: Dictionary<ulong, PlayerBinding>
  ├── _byIndex: Dictionary<byte, PlayerBinding>
  ├── _pending: Dictionary<ulong, PlayerBinding>
  ├── _readyList: List<PlayerBinding>
  │
  ├── RegisterPending(clientId, binding)   ← on player connect
  ├── PromoteToReady(clientId, index)      ← after assignment
  ├── Unregister(identity)                 ← on disconnect
  │
  ├── TryGetByPlayerIndex(index) → PlayerBinding
  ├── TryGetByClientId(clientId) → PlayerBinding
  │
  └── Events:
        ├── Registered(PlayerBinding)
        └── Unregistered(PlayerIdentity)

MatchCompositionRoot.Instance.Registry  ← access point
```

### Player Prefab Structure

```
Player (NetworkObject)
  ├── PlayerState (NetworkBehaviour)
  │     • Temperature, FanSpeed, IsReady, IsFanActive
  │     • HasSelectedItem, SyncedPlayerIndex
  │     • IsFanUpgraded, IsBasicBlocked
  │     • SelectItemServerRpc(), PressReadyServerRpc()
  │     • SendEmoteServerRpc()
  │     • OnEmoteRequested (static event → EmoteBubble)
  │
  ├── PlayerInventory (NetworkBehaviour)
  │     • SlotStates (NetworkList<ItemSlotNetData>)
  │     • GrantRandomItems(), ConsumeItem(), StealRandomItem()
  │
  └── AZPlayerVisual (NetworkBehaviour)
        • If IsOwner → FPSVisualController (1P view)
        • If Remote → binds to "EnemyPlayer" scene object (3P view)
```

### PlayerState — RPC Flow

```
[Client]                              [Server]
   │                                     │
   │  SelectItemServerRpc(slot)          │
   │ ────────────────────────────────►   │
   │                                     │  validate: PrepPhase, !Ready,
   │                                     │  slot valid, CanUse, !HasSelected
   │                                     │  → queue in ActionQueue
   │                                     │  → HasSelectedItem = true
   │                                     │
   │  PressReadyServerRpc()              │
   │ ────────────────────────────────►   │
   │                                     │  IsReady = true
   │                                     │  IsFanActive = false
   │                                     │
   │  CancelSelectionServerRpc()         │
   │ ────────────────────────────────►   │
   │                                     │  clear ActionQueue
   │                                     │  HasSelectedItem = false
   │                                     │
   │  SendEmoteServerRpc(id)             │
   │ ────────────────────────────────►   │
   │                                     │  validate: AcceptEmotes, id < 5
   │                                     │  record _lastEmoteServerTime
   │  ◄──── ShowEmoteClientRpc(id) ──────│
   │        (skips if IsOwner)           │
```

### ActionQueue Structure

```
ActionQueue (per player, server-side only)
  ├── selectedAction: QueuedAction?    ← main item (one per turn)
  │     • SlotIndex (byte)
  │     • ItemData (ItemDataSO)
  │
  ├── subAction: QueuedAction?         ← sub item (auto-queued by Sub slot type)
  │     • SlotIndex (byte)
  │     • ItemData (ItemDataSO)
  │
  ├── readyTimestamp (float)           ← used for action order tiebreak
  └── isReady (bool)
```

### PlayerModifiers (per-turn combat flags)

```
PlayerModifiers (struct, reset each turn)
  ├── BasicItemsBlocked (bool)    ← set by Sabotage:BlockBasic
  ├── ActionNeutralized (bool)    ← set by Sabotage:Neutralize
  ├── HasExtraAction (bool)       ← set by Special:ExtraAction
  ├── OpponentRevealed (bool)     ← set by Special:RevealOpponent
  └── ActiveDefense (DefenseInfo?)
        ├── Filter (DamageFilter)
        └── BlockAmount (float)
```

### Temperature System

```
Temperature Range: 0 (dead) ←──────────────────────► 37 (full health)

PrepPhase Ticks (every 1 second):
  ┌─────────────────────────────────────────────────┐
  │ IsFanActive == true                              │
  │   → temp -= FanSpeed (default 1°/tick)           │
  │   → player is actively cooling down              │
  │                                                  │
  │ IsFanActive == false && IsReady == true           │
  │   → temp += recoveryRate (default 1°/tick)        │
  │   → SunnyDay: 2°/tick, CoolBreeze: 0°/tick       │
  └─────────────────────────────────────────────────┘

Threshold Grants (one-time per round):
  30° → 1 random item
  20° → 2 random items
  10° → 3 random items

Death: temp <= 0 → round ends
```

---

## 5. Item System

### Class Hierarchy

```
ItemDataSO (abstract ScriptableObject)
  │
  │  ComputeEffect(ctx) → ItemEffectOutcome
  │  ExecuteEffect(ctx)  ← calls ComputeEffect + ItemEffectApplicator.Apply
  │
  ├── AttackItemDataSO        → deals temp damage to target
  │     • Damage, AttackFilter, EqualizeToUserTemp
  │
  ├── RecoveryItemDataSO      → heals user's temp
  │     • HealPerUse[] (variable heal per use index)
  │
  ├── DefenseItemDataSO       → blocks incoming damage
  │     • BlockAmount, Filter (DamageFilter)
  │
  ├── BuffItemDataSO          → self-buff (immediate + delayed)
  │     • ImmediateTempDelta, DelayedTempDelta, DelayTurns
  │
  ├── DebuffItemDataSO        → opponent debuff (immediate + delayed)
  │     • ImmediateTempDelta, DelayedTempDelta, AttackFilter
  │
  ├── SpecialItemDataSO       → unique effects
  │     • FanSpeedChange / ExtraAction / RevealOpponent
  │
  └── SabotageItemDataSO      → opponent disruption
        • Reroll / Steal / BlockBasic / Neutralize
```

### Item Effect Pipeline (Phase 5C)

```
  itemData.ComputeEffect(ctx)
        │
        ▼
  ItemEffectOutcome (value struct)
        │  Pure data — no side effects
        │
        │  Fields:
        │  ├── UserHeal / UserDamage / UserDamageFilter
        │  ├── TargetHeal / TargetDamage / TargetDamageFilter
        │  ├── TargetDefenseCheck
        │  ├── SetUserDefense
        │  ├── NeutralizeTarget / GrantExtraAction / RevealOpponent
        │  ├── BlockTargetBasics
        │  ├── WriteUserFanSpeed / WriteTargetFanSpeed
        │  ├── HasScheduledEffect (buff/debuff scheduling)
        │  ├── InventoryAction (RerollTarget / StealFromTarget)
        │  └── Blocked
        │
        ▼
  ItemEffectApplicator.Apply(ctx, outcome)
        │  Single mutation point — all NV writes happen here:
        │
        ├── TempSystem.ApplyHeal / ApplyDamage
        ├── Modifier writes (defense, neutralize, extra action)
        ├── NV writes (FanSpeed, IsBasicBlocked)
        ├── BuffSystem.Schedule (delayed effects)
        └── Inventory mutations (reroll, steal)
```

### ItemDataSO Common Fields

| Field | Type | Purpose |
|-------|------|---------|
| `ItemName` | string | Display name (Korean) |
| `Category` | ItemCategory | Attack/Defense/Recovery/Buff/Debuff/Sabotage/Special |
| `Persistence` | ItemPersistence | Permanent / BasicConsumable / RandomConsumable |
| `SlotType` | ItemSlotType | Main (normal) / Sub (executes at attack-turn start) |
| `MaxUses` | int | Uses per round (255 = unlimited) |
| `DropWeight` | float | Weight in random drop table |
| `AnimTrigger` | string | 1P animation trigger name |
| `OpponentAnimTrigger` | string | 3P animation trigger name |
| `AnimDuration` | float | Animation length in seconds |
| `EffectDelay` | float | Delay before hit effect (0.75s default) |
| `EffectHitCount` | int | Multi-hit count |
| `EffectInterval` | float | Delay between multi-hits |
| `RequiresMiniGame` | bool | Requires mini-game to use |
| `MiniGameType` | MiniGameType | Which mini-game |

### Item Categories

| Category | Target | Timing | Examples |
|----------|--------|--------|----------|
| **Attack** | Opponent temp ↓ | Immediate | Fan (3°), Hand Fan (4°), Ice Cream (5°), Water Gun (7°) |
| **Defense** | Block incoming | Immediate | Windbreaker (4° partial), Mask (100% food block) |
| **Recovery** | Self temp ↑ | Immediate | Warm Tea (7°), Hot Americano (5°), Hot Pack (10°) |
| **Buff** | Self benefit | Delayed | Buldak Noodles (+17°), Soda (-5° now / +15° next) |
| **Debuff** | Opponent penalty | Delayed | Samgyetang (+3° now / -7° next turn) |
| **Sabotage** | Disrupt opponent | Varies | Cat (reroll), Claw Machine (steal), Blue Tape (block basic) |
| **Special** | Unique mechanic | Varies | Screwdriver (2× fan), Tarot Card (reveal), Red Card (neutralize) |

### Inventory Structure

```
PlayerInventory (NetworkList<ItemSlotNetData>, max 12 slots)

ItemSlotNetData (4-byte struct):
  ├── ItemId (short)         → index into ItemManager.allItems (-1 = empty)
  ├── RemainingUses (byte)   → 255 = unlimited
  └── Flags (byte)           → bit 1 = Sub slot type

Initial Setup (per round):
  Slot 0: Fan (Permanent, unlimited)        ← basic
  Slot 1: Windbreaker (Permanent, unlimited) ← basic
  Slot 2: Warm Tea (BasicConsumable, 2 uses) ← basic
  Slot 3: Cat (BasicConsumable, 1 use, Sub)  ← basic
  Slot 4-7: Random items from drop table     ← random

Round Reset (via RoundLifecycleService):
  • Remove all RandomConsumable slots
  • Restore basic items to max uses
  • Grant 4 new random items

Max random items: 8 (threshold grants add more during round)
```

---

## 6. Combat System

### Resolution Flow

```
AttackPhaseRoutine (server)
  │
  ▼
BuffDebuffSystem.ProcessTurnStart()     ← fire delayed effects from prior turns
  │
  ▼
CombatEngine.CapturePreCombatState()    ← snapshot temps + item IDs
  │
  ▼
CombatEngine.ExecuteSubItems()          ← per-player sub items (Cat, etc.)
  │
  ▼
CombatEngine.ResolveCombat()
  │
  ├── CombatResolver.Resolve()
  │     │
  │     ├── DetermineOrder()
  │     │     • HeatWave: lower-temp player first
  │     │     • Normal: earlier readyTimestamp first
  │     │     • Tiebreak: lower temperature first
  │     │
  │     ├── ApplyDefense() for each player
  │     │     • DefenseItemDataSO.ComputeEffect → Outcome
  │     │     • ItemEffectApplicator.Apply → sets ActiveDefense modifier
  │     │
  │     ├── ExecuteMain(firstPlayer)
  │     │     • Check ActionNeutralized (skip if true)
  │     │     • itemData.ComputeEffect(ctx) → ItemEffectOutcome
  │     │     • ItemEffectApplicator.Apply(ctx, outcome)
  │     │     • Consume item from inventory
  │     │     • Record CombatEvent (temps before/after)
  │     │
  │     ├── Check death after first action
  │     │     • If dead → skip second action, record winner
  │     │
  │     ├── ExecuteMain(secondPlayer)
  │     │     • Same flow as above
  │     │
  │     └── Return CombatResult
  │           • FirstPlayerIndex, WinnerIndex
  │           • Events[] (up to 2 CombatEvents)
  │           • Temperature snapshots
  │           • Item IDs for display
  │
  ▼
PresentationBarrier.Begin(sequence, clientIds)    ← server waits for client VFX
  │
  ▼
OnCombatResultClientRpc(resultData)    ← broadcast to all clients
  │
  ▼ (client-side)
CombatVFXManager.PlayCombatVFXSequence(result)
  │
  ▼ (client-side, on complete)
PresentationAckServerRpc(sequence)     ← client reports VFX done
  │
  ▼ (server-side)
PresentationBarrier.ReceiveAck()
  │  WaitForCompletion(timeout) → all clients acknowledged
  ▼
ResolutionPhaseRoutine()
```

### PresentationBarrier (Phase 2)

```
PresentationBarrier (sealed class)
  ├── _pendingClients: HashSet<ulong>
  ├── _currentSequence: uint
  │
  ├── Begin(sequence, expectedClientIds)    ← server: start waiting
  ├── ReceiveAck(sequence, senderClientId)  ← server: mark client done
  ├── HandleDisconnect(clientId)            ← server: remove if disconnected
  ├── WaitForCompletion(timeoutSeconds)     ← coroutine: wait or timeout
  └── Cancel(reason)                        ← force-complete

Ensures: server does not advance to Resolution before all clients finish
         playing combat animations. Timeout fallback prevents hang on
         client crash.
```

### CombatResult → CombatResultData (Network Serialization)

```
CombatResult (server, rich)         CombatResultData (network, flat)
  Events: List<CombatEvent>    ──►    EventCount (byte)
                                      Event0Source/Target/Type/UserTemp/TargetTemp
                                      Event1Source/Target/Type/UserTemp/TargetTemp
  WinnerIndex                  ──►    WinnerIndex (sbyte)
  P1/P2 TempAtTurnStart       ──►    P1/P2TempAtTurnStart (float)
  P1/P2 TempBeforeCombat       ──►    P1/P2TempBeforeCombat (float)
  P1/P2 TempAfterCombat        ──►    P1/P2TempAfterCombat (float)
  P1/P2 MainItemId             ──►    P1/P2MainItemId (short)
  P1/P2 SubItemId              ──►    P1/P2SubItemId (short)
  ResultSequence               ──►    ResultSequence (uint)
```

### TwoPlayerCombatMapper (Phase 5D)

Isolates `idx == 0 ? p1 : p2` patterns behind indexed accessors:

```csharp
TwoPlayerCombatMapper.GetTempAtTurnStart(resultData, playerIndex)
TwoPlayerCombatMapper.GetTempBeforeCombat(resultData, playerIndex)
TwoPlayerCombatMapper.GetTempAfterCombat(resultData, playerIndex)
TwoPlayerCombatMapper.GetSubItemId(resultData, playerIndex)
TwoPlayerCombatMapper.GetMainItemId(resultData, playerIndex)
```

Works with `CombatResultData`, `CombatResult`, and `CombatSnapshot`.

### VFX Sequence (Client-Side)

```
PlayCombatVFXSequence(result)
  │
  ├── OverrideTempTargets(p0BeforeCombat, p1BeforeCombat)   ← freeze HP bars
  │       (via static event → AZGameUI)
  │
  ├── Play first player's item sequence:
  │     ├── Show item sprite on character
  │     ├── Play 3P animation (userVisual.PlayCombatAnimation)
  │     ├── Play 1P animation (FPSVisualController.PlayFPSAnimation)
  │     ├── Play SFX (GameAudioManager.PlayItemSfx)
  │     ├── Wait EffectDelay (0.75s)
  │     ├── Hit effect:
  │     │     ├── ApplyEventTemps() → OverridePlayerTemp for affected players
  │     │     ├── PlayDamageFlash() or PlayCombatAnimation("defence")
  │     │     ├── PlayHitAt() particle (ObjectPool)
  │     │     ├── ScreenVFXManager.PlayHitVFX() (frost overlay)
  │     │     └── GameAudioManager.PlayDamaged()
  │     ├── Special sequences: Cat / Buldak / Hug / Feed
  │     └── ReturnToIdle()
  │
  ├── Check death → PlayDeathSequence if dead
  │
  ├── Play second player's item sequence (same flow)
  │
  └── ClearTempOverrides()   ← unfreeze HP bars (via static event → AZGameUI)
```

### Core→UI Decoupling Events (CombatVFXManager)

```csharp
// CombatVFXManager fires these instead of calling AZGameUI.Instance directly
public static event Action OnTempOverridesClear;
public static event Action<float, float> OnTempTargetsOverride;
public static event Action<int, float> OnPlayerTempOverride;

// AZGameUI subscribes in SubscribeToEvents()
CombatVFXManager.OnTempOverridesClear += ClearTempOverrides;
CombatVFXManager.OnTempTargetsOverride += OverrideTempTargets;
CombatVFXManager.OnPlayerTempOverride += OverridePlayerTemp;
```

### ObjectPool (CombatVFX Particles)

```
CombatVFXManager uses UnityEngine.Pool.ObjectPool<ParticleSystem>

Per-prefab pools:
  _hitPool, _healPool  (auto-created for each particle prefab)

actionOnRelease:
  ParticleSystem.Stop(withChildren: true, StopBehavior.StopEmittingAndClear)

ReturnAfterPlay coroutine:
  yield return WaitWhile(ps.isPlaying) → pool.Release(ps)
```

---

## 7. Visual System

### 2.5D Rendering Setup

```
Camera: Perspective, FOV 67°, pos (0, 3.5, -4), rot (16°, 0, 0)
  │
  │  looks forward into the stage
  │
  ▼
┌─────────────────────────────────────────────┐
│                  Stage                       │
│                                              │
│   z=8  ┌────────────┐  EnemyPlayer          │
│        │ body       │  (SpriteRenderer,      │
│        │ lowerbody  │   SpriteResolver,      │
│        │ head/arms  │   SortingGroup,        │
│        │ item       │   Animator: playerA)   │
│        └────────────┘                        │
│                                              │
│   z=5.5  OppItemSpawnRoot (enemy items)      │
│                                              │
│   z=1.5  MyItemSpawnRoot (player items)      │
│          PlayerItem1~10 markers              │
│                                              │
│   z=1.2  ReadyWorldCanvas (world-space btn)  │
│                                              │
│   z=-1   SpawnPoint_1 (local player spawn)   │
│                                              │
│   z=-4   Camera                              │
└─────────────────────────────────────────────┘

Lighting:
  • Directional Light: rot (50°, -30°), intensity 2, soft shadows, 5000K
  • Spot Light: top-down stage, intensity 200, range 15, angle 70°
  • Ambient: Trilight dark (near-black)
  • Skybox: disabled (solid black camera background)
  • URP: Deferred, SSAO, HDR, shadow 2048 4-cascade
```

### Player Visuals

```
IsOwner (1P Local Player):
  ┌───────────────────────────────────┐
  │  FPSVisualController              │
  │  (child of Camera.main)           │
  │                                   │
  │  FPS (GameObject)                 │
  │    ├── hand1 (SpriteRenderer)     │
  │    ├── hand2 (SpriteRenderer)     │
  │    ├── item  (SpriteRenderer)     │
  │    └── Particle (ParticleSystem)  │
  │                                   │
  │  Animator: Resources/FPS/FPSA     │
  │  Triggers: swing, defence, use,   │
  │            feed                   │
  │  Sprites: Resources/FPS/FPS_*     │
  └───────────────────────────────────┘

IsRemote (3P Enemy Player):
  ┌───────────────────────────────────┐
  │  EnemyPlayer (scene object)       │
  │  ├── body      (SpriteRenderer)   │
  │  │   ├── arm1  (SpriteRenderer)   │
  │  │   ├── arm2  (SpriteRenderer)   │
  │  │   └── head  (SpriteRenderer)   │
  │  ├── lowerbody (SpriteRenderer)   │
  │  ├── item      (SpriteRenderer)   │
  │  ├── freezeice (SpriteRenderer)   │
  │  ├── IceBreakEffect (Particle)    │
  │  └── FinalBreakEffect (Particle)  │
  │                                   │
  │  Animator: playerA.controller     │
  │  Material: SpriteFlash.mat        │
  │    Shader: Sprite3DLit            │
  │    _FlashAmount: white flash      │
  │                                   │
  │  Triggers: attack, swing, drink,  │
  │    defence, damage, feed, eat,    │
  │    hug, card, button, freeze,     │
  │    jump, heal, disappoint, end    │
  │                                   │
  │  BlendTree: degree param          │
  │    0 = normal (>20°)              │
  │    1 = cold (10-20°)              │
  │    2 = very cold (<10°)           │
  └───────────────────────────────────┘
```

### Death Sequence

```
PlayDeathSequence():
  1. freeze sprite phase 1 → freezeice.SetActive(true), freeze1 sprite
  2. Wait 0.3s → freeze sprite phase 2
  3. Wait 0.3s → ice break particles + camera shake
  4. freezeice.SetActive(false)
  5. Move visualRoot to y=-100 (hide off-screen)

ReviveVisual():
  1. Stop death coroutine
  2. Hide freezeice, stop particles
  3. Restore position
  4. Reset all animator triggers
  5. Play("Idle_Tree"), degree=0
```

---

## 8. UI System

### Construction Pattern

All UI is **runtime-built in C# code** — no prefabs, no Inspector wiring.

```
Pattern for every UI class:
  1. Start() or Awake() → BuildUI() / BuildOverlayUI()
  2. Create Canvas + CanvasScaler (1920x1080 reference)
  3. Create child GameObjects with RectTransform
  4. Add Image/TMP/Button/Slider components
  5. Load sprites via GameSprites static class
  6. Subscribe to NetworkVariable.OnValueChanged callbacks
  7. Update() for continuous display (lerping, timer, score)
```

### Canvas Stack (by Sort Order)

| Sort Order | Canvas | Render Mode | Owner | Purpose |
|------------|--------|-------------|-------|---------|
| 0 | OverlayCanvas | ScreenSpaceOverlay | AZGameUI | HP bar, timer, score, panels |
| 10 | MainUI | ScreenSpaceOverlay | AZLobbyUI | Lobby interface |
| 50 | MiniGameCanvas | ScreenSpaceOverlay | MiniGameHub | Mini-game overlay |
| 90 | EmoteBubble | ScreenSpaceOverlay | EmoteBubble | Opponent head bubble |
| 100 | LoadingScreenCanvas | ScreenSpaceOverlay | LoadingScreenManager | Scene transition |
| 120 | EmoteWheelCanvas | ScreenSpaceOverlay | EmoteWheel | Emote picker |
| N/A | EnemyCanvas | WorldSpace (0.007) | AZGameUI | Opponent HP bar |
| N/A | ReadyWorldCanvas | WorldSpace (0.005) | AZGameUI | Ready button |

### HP Bar Color Gradient

```
Temperature → Color mapping (static GetTempColor):
  37°─────30°: Green (#4CAF50) → Pink (#E91E63)
  30°─────20°: Pink (#E91E63) → Sky Blue (#29B6F6)
  20°─────10°: Sky Blue (#29B6F6) → Deep Blue (#0D47A1)
  <10°───── 0°: Deep Blue (#0D47A1)
```

### Temperature Display Override System

```
Normal mode:
  UpdateTempDisplay() reads Temperature.Value
  _displayedMyTemp lerps toward it at HP_LERP_SPEED (6/sec)

During combat VFX:
  1. OverrideTempTargets(p0Before, p1Before)   ← freeze both bars at pre-combat
  2. Per hit: OverridePlayerTemp(idx, eventTemp) ← move affected bar only
  3. ClearTempOverrides()                        ← bars catch up to actual values

On PrepPhase start:
  SnapTempDisplay()  ← instant jump (no lerp on round reset refill)
```

### Event Subscription Map

| Source | Event | Handler | Purpose |
|--------|-------|---------|---------|
| `TurnManager.CurrentPhase` | `OnValueChanged` | `AZGameUI.OnPhaseChanged` | Phase text, ready btn, audio |
| `TurnManager.LastRoundWinner` | `OnValueChanged` | `AZGameUI.OnWinnerChanged` | Game over panel |
| `TurnManager.OnCombatResult` | static event | `CombatVFXManager.PlayCombatVFXSequence` | Attack animations |
| `TurnManager.OnEnvironmentAnnounced` | static event | `AZGameUI.OnEnvironmentAnnounced` | Env panel + audio |
| `TurnManager.OnEnvironmentAnnounced` | static event | `EnvironmentVFXManager.OnEnvironmentAnnounced` | Env visuals |
| `TurnManager.OnOpponentRevealed` | static event | `AZGameUI.OnOpponentRevealed` | Tarot card reveal |
| `CombatVFXManager.OnTempOverridesClear` | static event | `AZGameUI.ClearTempOverrides` | Unfreeze HP bars |
| `CombatVFXManager.OnTempTargetsOverride` | static event | `AZGameUI.OverrideTempTargets` | Freeze HP bars |
| `CombatVFXManager.OnPlayerTempOverride` | static event | `AZGameUI.OverridePlayerTemp` | Per-hit HP update |
| `SessionManager.OnLoadingShow` | static event | `LoadingScreenManager.Show` | Loading overlay |
| `SessionManager.OnLoadingHide` | static event | `LoadingScreenManager.ForceHide` | Hide loading |
| `PlayerState.OnEmoteRequested` | static event | `EmoteBubble.Show` | Emote bubble display |
| `PlayerState.HasSelectedItem` | `OnValueChanged` | `AZGameUI.OnHasSelectedItemChanged` | Status text |
| `PlayerState.IsBasicBlocked` | `OnValueChanged` | `InventoryPresenter.OnBasicBlockedChanged` | Banned overlay |
| `PlayerInventory.SlotStates` | `OnListChanged` | `InventoryPresenter` | Rebuild item views |
| `PlayerState.OnMiniGameStart` | delegate | `MiniGameHub` | Launch mini-game UI |
| `InventoryPresenter.OnWorldItemClicked` | delegate | `AZGameUI.OnItemClicked` | Item selection |

### World-Space Items

```
Item_{slot}_{name} (layer: Interactable)
  ├── Card (SpriteRenderer, sortOrder 5)
  ├── Label (TextMesh: "ItemName\nUses")
  ├── BannedOverlay (SpriteRenderer, hidden by default)
  ├── BoxCollider (0.75 x 1.2 x 0.5)
  └── HoverEffect (scale 1.15x on hover, outline)

Click detection: HoverRaycaster → Physics.Raycast(Interactable layer)
  → InventoryPresenter.HandleClick()
  → AZGameUI.OnItemClicked()
  → PlayerState.SelectItemServerRpc(slot)
```

---

## 9. Audio System

### Channel Architecture

```
GameAudioManager (6 AudioSource children)
  ├── BGM     (loop, volume 0.3)
  ├── SFX     (one-shot combat effects)
  ├── UI      (one-shot clicks, hovers)
  ├── ENV     (loop or one-shot environment sounds)
  ├── FanLoop (loop, fan wind sound)
  └── Clock   (loop, clock tick during prep)
```

### Clip Mapping

```
Animation Trigger → SFX:
  "swing" → SFX_swing, "attack" → SFX_swing
  "use" → SFX_watergun, "drink" → SFX_drink
  "defence" → SFX_defence, "eat" → SFX_eat
  "card" → SFX_card, "hug" → SFX_hug

Item Name Override (priority over trigger):
  "Buldak Noodles" → SFX_fire
  "Cat" → SFX_cat
  "Hot Pack" → SFX_drink

Environment → SFX:
  SunnyDay → SFX_cicada (loop)
  CoolBreeze → SFX_wind (loop)
  CicadaSong → SFX_cicada (loop)
  Kids → SFX_kid (one-shot)
  Ambulance → SFX_ambulance (one-shot)
```

---

## 10. Emote System

### Architecture

```
EmoteWheel (on Ready button)
  │ condition: IsReady && PrepPhase
  │
  │  [press & hold Ready btn]
  │         │
  │         ▼
  │  Fan arc (150°→30°, 5 icons)
  │    0: slow (느려요)
  │    1: tongue (메롱)
  │    2: sneer (비웃음)
  │    3: excited (신난다)
  │    4: lose (지겠는데)
  │
  │  [drag to select, release]
  │         │
  │         ▼
  │  Fire(id)
  │    ├── Local confirm pop (0.14s in, 0.55s hold, 0.3s out)
  │    └── SendEmoteServerRpc(id)
  │              │
  │              ▼ (server validates AcceptEmotes)
  │         ShowEmoteClientRpc(id)
  │              │
  │         ┌────┴────┐
  │    IsOwner      Remote
  │    (skip)    EmoteBubble.Show()
  │                 │
  │                 ▼
  │            ObjectPool<EmoteBubble>
  │            (static pool, DontDestroyOnLoad)
  │            ScreenSpaceOverlay (sortOrder 90)
  │            Anchored to enemy head (+1.9 world up)
  │            0.16s pop-in → 0.55s hold → 0.3s fade-out
  │            Then pool.Release(bubble)
```

### EmoteBubble ObjectPool

```
Static pool with RuntimeInitializeOnLoadMethod(SubsystemRegistration):
  ├── _pool = null  ← reset on domain reload
  ├── PlayerState.OnEmoteRequested += Show

Pool config:
  ├── actionOnCreate: Instantiate canvas + image + emoji text
  ├── actionOnGet: SetActive(true), assign anchor/position/sprite
  ├── actionOnRelease: StopAllCoroutines, anchor = null, SetActive(false)
  └── actionOnDestroy: Destroy(gameObject)

Lifecycle ensures no event leaks across domain reload (Editor Play Mode).
```

---

## 11. Environment System

### Environment Types

| Type | Korean | Turn 1→2 Effect | Turn 3 Trigger |
|------|--------|-----------------|----------------|
| SunnyDay | 햇살쨍쨍 | Recovery rate 1→2°/s | — |
| CoolBreeze | 바람선선 | Recovery rate 1→0°/s | — |
| CicadaSong | 매미울음 | Audio/visual only | — |
| Kids | 잼민이들 | Visual NPCs appear | Steal 1 random item from each player |
| Ambulance | 앰뷸런스 | Visual NPC appears | Heal +10° to lower-temp player |
| SummerVacation | 여름방학 | Prep duration 20s→10s | — |
| HeatWaveWarning | 폭염경보 | Lower-temp player acts first | — |

### EnvironmentRuleService (Extracted)

```csharp
class EnvironmentRuleService
  ├── SelectRandom() → EnvironmentType
  ├── GetPrepDuration(env, base) → float
  ├── GetRecoveryRate(env) → float
  ├── ShouldApplyKidsEffect(env, turnNumber) → bool
  ├── ShouldApplyAmbulanceEffect(env, turnNumber) → bool
  ├── DetermineAmbulanceTarget(p1Temp, p2Temp) → int
  ├── RemoveRandomUnusedItem(inventory) → void
  └── GetName(env) → string (Korean display name)
```

### Activation Flow

```
Turn 1 Resolution (no environment)
  │
  ▼
EnvironmentAnnouncementRoutine()
  │  EnvironmentRuleService.SelectRandom()
  │  ActiveEnvironment.Value = picked
  │
  ├── AnnounceEnvironmentClientRpc()
  │     ├── AZGameUI: show env panel + play audio
  │     ├── EnvironmentVFXManager: build NPCs/props + light changes
  │     └── Camera pan (-25° Y, 0.6s ease, 2s hold, return)
  │
  └── 4 second wait
```

### EnvironmentVFXManager Visuals

```
Per-environment runtime construction:
  Kids: 2 kid characters (9-part sprite hierarchy each + Animator)
  Ambulance: 1 rescue character (7-part sprite hierarchy + Animator)
  SunnyDay/HeatWave: directional light color/intensity lerp
  SummerVacation: timer shake effect in AZGameUI

Staging sequences (Turn 3):
  Kids Steal: kids sink → rise → steal trigger → sink all
  Ambulance Blanket: blanket overlay or rescue approach animation
```

---

## 12. Mini-Game System

### Architecture

```
MiniGameHub (singleton, owns MiniGameCanvas sortOrder 50)
  │
  │  subscribes to PlayerState.OnMiniGameStart
  │
  ▼
MiniGameUIBase (abstract)
  │  builds standard frame:
  │    • Full-screen dim (40% black)
  │    • Timer line (top edge, gold→red fill)
  │    • Content area (900x700)
  │    • Banner slide-in + result outro
  │
  ├── WaterGunMiniGameUI      (HitTargets: 5s, hit 3 moving targets)
  ├── HugCharacterMiniGameUI  (HugCharacter: 10s, timing both-side tap)
  ├── ClawGrabMiniGameUI      (ClawGrab: 7s, timing tap)
  ├── TapeCutMiniGameUI       (TimingCut: 5s, timing tap in green zone)
  ├── PatternUnlockMiniGameUI (PatternUnlock: 5s, 3×3 pattern drag)
  ├── ScrewdriverMiniGameUI   (TightenScrews: 7s, circular drag ×3)
  ├── RedCardMiniGameUI       (PickCard: 5s, find red among yellows)
  ├── HotPackMiniGameUI       (TapRepeat: 10s, rapid tap)
  └── BuldakMiniGameUI        (BoilWater: 10s, rapid tap gauge)
```

### Mini-Game Flow

```
[PrepPhase]
  │
  Player selects mini-game item
  │
  ▼
PlayerState.SelectItemServerRpc()
  │  RequiresMiniGame == true
  │  → stores _pendingMiniGameSlot
  │  → sets deadline (min of timeLimit, prepEnd) + grace 0.5s
  │  → StartMiniGameClientRpc(slot, type, timeLimit, goal)
  │
  ▼ (client)
MiniGameHub receives OnMiniGameStart
  │  → instantiates correct MiniGameUIBase subclass
  │  → mini-game plays
  │
  ▼ (on complete)
PlayerState.SubmitMiniGameResultServerRpc(slot, success)
  │
  ├── success=true → queue item as normal
  └── success=false → consume 1 use (wasted), compact slots
```

---

## 13. Match System

### Best-of-3 Structure

```
MatchManager (NetworkBehaviour)
  │
  │  NetworkVariables:
  │    RoundNumber (int)
  │    P1RoundWins (int)
  │    P2RoundWins (int)
  │    CurrentMatchState (MatchState)
  │
  │  WINS_TO_MATCH = 2
  │
  ▼
Round 1 ──► Round 2 ──► Round 3 (if needed)
  │                         │
  │  first to 2 wins        │
  ▼                         ▼
  Match Complete         Match Complete

Draw handling:
  • Same temp at death → round voided
  • Both dead simultaneously → round voided
  • Voided round → replay (not counted)
```

### MatchCompositionRoot (Phase 5D)

```
MatchCompositionRoot (MonoBehaviour, GameScene singleton)
  │
  ├── PlayerRegistry _registry        ← central player lookup
  │     • IReadOnlyPlayerRegistry (public)
  │     • PlayerRegistry (writable, for TurnManager)
  │
  ├── ItemManager _itemManager         ← FindAnyObjectByType on Awake
  └── MatchManager _matchManager       ← FindAnyObjectByType on Awake

Access: MatchCompositionRoot.Instance.Registry
        MatchCompositionRoot.Instance.ItemManager
        MatchCompositionRoot.Instance.MatchManager
```

### MatchScoreView (Phase 5D)

```csharp
static int GetRoundWins(MatchManager mm, int playerIndex)
    => playerIndex == 0 ? mm.P1RoundWins.Value : mm.P2RoundWins.Value;
```

Isolates P1/P2 field selection behind indexed accessor.

---

## 14. Assembly Architecture

### Assembly Definition (asmdef) Separation (Phase 7)

```
AbsoluteZero.Core (Assets/Scripts/Core/)
  │
  │  Domain logic: Turn, Combat, Player, Item, Network, Session,
  │                Match, Buff, Audio, Emote, Common
  │
  │  References:
  │    Unity.Netcode.Runtime, Unity.InputSystem,
  │    Unity.Services.Core, Unity.Services.Authentication,
  │    Unity.Services.Lobbies, Unity.Services.Relay,
  │    Unity.Networking.Transport, Unity.Collections,
  │    UnityEngine.UI
  │
  │  ⛔ CANNOT reference AbsoluteZero.UI
  │
  ▼ (one-way dependency)
  
AbsoluteZero.UI (Assets/Scripts/UI/)
  │
  │  Presentation: AZGameUI, AZLobbyUI, LoadingScreenManager,
  │                EmoteWheel, MiniGameHub, MiniGameUI*, UiFont
  │
  │  References:
  │    AbsoluteZero.Core,
  │    Unity.Netcode.Runtime, Unity.TextMeshPro,
  │    Unity.InputSystem, Unity.Services.Lobbies,
  │    UnityEngine.UI
```

### Core→UI Communication

Core cannot `using AbsoluteZero.UI`. Instead, Core classes fire **static events** that UI classes subscribe to:

| Core Class | Static Event | UI Subscriber |
|------------|-------------|---------------|
| `CombatVFXManager` | `OnTempOverridesClear` | `AZGameUI.ClearTempOverrides` |
| `CombatVFXManager` | `OnTempTargetsOverride` | `AZGameUI.OverrideTempTargets` |
| `CombatVFXManager` | `OnPlayerTempOverride` | `AZGameUI.OverridePlayerTemp` |
| `SessionManager` | `OnLoadingShow` | `LoadingScreenManager.Show` |
| `SessionManager` | `OnLoadingHide` | `LoadingScreenManager.ForceHide` |
| `PlayerState` | `OnEmoteRequested` | `EmoteBubble.Show` (via static lambda) |

---

## 15. Design Patterns Summary

| Pattern | Classes |
|---------|---------|
| **Singleton (DDOL)** | AppBootstrapper, NetworkSessionCoordinator, LobbyManager, RelayManager, SessionManager, PlayerSpawnManager, LoadingScreenManager |
| **Singleton (scene)** | MatchCompositionRoot, TurnManager, ItemManager, MatchManager, CombatVFXManager, EnvironmentVFXManager, GameAudioManager, AZGameUI, InventoryPresenter, HoverRaycaster, CameraShake, FPSVisualController |
| **Lazy Singleton** | ScreenVFXManager (auto-creates on first `.Instance` access) |
| **Registry** | PlayerRegistry (PlayerBinding indexed by ClientId and PlayerIndex) |
| **Composition Root** | MatchCompositionRoot (wires PlayerRegistry + scene services) |
| **Gateway (interface boundary)** | IUnityServicesGateway, ILobbyGateway, IRelayGateway, INetworkRuntime, ISceneTransitionService |
| **Coordinator (session orchestration)** | NetworkSessionCoordinator (state machine + gateway composition) |
| **Compensation Stack** | OperationScope (LIFO rollback on failure during multi-step connect) |
| **Presentation Barrier** | PresentationBarrier (server waits for all client VFX ACKs) |
| **Effect Pipeline (Compute→Apply)** | ItemDataSO.ComputeEffect → ItemEffectOutcome → ItemEffectApplicator.Apply |
| **Service Extraction** | RoundLifecycleService, EnvironmentRuleService, CombatEngine (pure logic extracted from TurnManager) |
| **Indexed Accessor (Mapper)** | TwoPlayerCombatMapper, MatchScoreView (isolate P1/P2 field selection) |
| **NetworkBehaviour + NetworkVariable** | PlayerState, PlayerInventory, TurnManager, ItemManager, MatchManager |
| **ScriptableObject Hierarchy (Template Method)** | ItemDataSO → 7 concrete subclasses |
| **ObjectPool** | CombatVFXManager (per-prefab particle pools), EmoteBubble (static DontDestroyOnLoad pool) |
| **Observer (static events)** | Core→UI decoupling (6 events), TurnManager static events, NetworkVariable.OnValueChanged |
| **Context / Parameter Object** | ItemContext, CombatSnapshot |
| **Pure Logic (server-only)** | CombatResolver, TemperatureSystem, BuffDebuffSystem, ActionQueue, ItemDropTable |
| **Value Type DTO** | PlayerIdentity, PlayerModifiers, DefenseInfo, QueuedAction, ItemSlotNetData, CombatEvent, CombatResultData, ItemEffectOutcome, Result\<T\> |
| **Static Utility** | GameSprites, EmoteCatalog, UiFont, NetworkTickRate |

---

## 16. Data Flow Diagrams

### Lobby → Game Transition

```
AZLobbyUI        NetworkSessionCoordinator     LobbyGateway    RelayGateway
    │                      │                       │               │
    │  CreateClicked       │                       │               │
    ├─────────────────────►│  CreateLobbyAsync     │               │
    │                      ├──────────────────────►│               │
    │                      │  ◄── lobby created ───│               │
    │                      │                       │               │
    │  StartClicked        │                       │               │
    ├─────────────────────►│  StartMatchAsHostAsync│               │
    │                      ├───────────────────────┼──────────────►│
    │                      │                       │  AllocateAsync│
    │                      │  ◄── relay data ──────┼───────────────│
    │                      │                       │               │
    │                      │  NgoNetworkRuntime.StartHost()        │
    │                      │  LobbyGateway.UpdateAsync(joinCode)   │
    │                      │  LoadNetworkScene("GameScene")        │
    │                      │                       │               │
    │                      │         ────── scene loads ────────►  │
    │                      │                                       │
    │                      │  PlayerSpawnManager                   │
    │                      │    → Registry.RegisterPending         │
    │                      │    → Registry.PromoteToReady          │
    │                      │                                       │
    │                      │  TurnManager.WaitForPlayersRoutine    │
    │                      │    → game begins                      │
```

### Combat Turn Cycle

```
PrepPhase                      AttackPhase                  Resolution
┌─────────────────┐    ┌──────────────────────┐    ┌─────────────────┐
│ Fan ticks        │    │ Buff/Debuff process   │    │ Check winner     │
│ Recovery ticks   │    │ CombatEngine          │    │ Compact inventory│
│ Item selection   │    │   .CaptureSnapshot    │    │ Env announce     │
│ Mini-games       │    │   .ExecuteSubItems    │    │ (after Turn 1)  │
│ Emotes           │    │   .ResolveCombat      │    │                 │
│ Timer countdown  │    │     → CombatResolver  │    │ → PrepPhase or  │
│ Env effects      │    │     → ItemPipeline    │    │   RoundOver     │
│                  │    │ PresentationBarrier   │    │                 │
│                  │    │   .Begin → .WaitACK   │    │                 │
│                  │    │ VFX sequence (client) │    │                 │
└────────┬────────┘    └──────────┬───────────┘    └────────┬────────┘
         │ both ready             │ result + ACK             │
         │ or timeout             │ broadcast                │
         └────────────────────────┴──────────────────────────┘
```

---

## 17. File Index

### Core Assembly — `AbsoluteZero.Core` (Assets/Scripts/Core/)

| Path | Class | System |
|------|-------|--------|
| `Session/AppBootstrapper.cs` | AppBootstrapper | Bootstrap |
| `Session/NetworkSessionCoordinator.cs` | NetworkSessionCoordinator | Session |
| `Session/IUnityServicesGateway.cs` | IUnityServicesGateway | Session |
| `Session/UnityServicesGateway.cs` | UnityServicesGateway | Session |
| `Session/ILobbyGateway.cs` | ILobbyGateway | Session |
| `Session/LobbyGateway.cs` | LobbyGateway | Session |
| `Session/IRelayGateway.cs` | IRelayGateway | Session |
| `Session/RelayGateway.cs` | RelayGateway | Session |
| `Session/INetworkRuntime.cs` | INetworkRuntime | Session |
| `Session/NgoNetworkRuntime.cs` | NgoNetworkRuntime | Session |
| `Session/ISceneTransitionService.cs` | ISceneTransitionService | Session |
| `Session/SceneTransitionService.cs` | SceneTransitionService | Session |
| `Session/Result.cs` | Result\<T\>, OperationErrorCode, Unit | Session |
| `Session/OperationScope.cs` | OperationScope | Session |
| `Network/LobbyManager.cs` | LobbyManager | Network |
| `Network/RelayManager.cs` | RelayManager | Network |
| `Network/SessionManager.cs` | SessionManager | Network |
| `Network/PlayerSpawnManager.cs` | PlayerSpawnManager | Network |
| `Network/PlayerSpawnPoint3D.cs` | PlayerSpawnPoint3D | Network |
| `Network/LobbyServiceHelper.cs` | LobbyServiceHelper | Network |
| `Network/SceneLoadSyncManager.cs` | SceneLoadSyncManager | Network |
| `Network/NetworkConstants.cs` | NetworkTickRate + enums | Network |
| `Player/Identity/PlayerIdentity.cs` | PlayerIdentity | Identity |
| `Player/Identity/PlayerBinding.cs` | PlayerBinding | Identity |
| `Player/Identity/PlayerRegistry.cs` | PlayerRegistry | Identity |
| `Player/Identity/IReadOnlyPlayerRegistry.cs` | IReadOnlyPlayerRegistry | Identity |
| `Player/PlayerState.cs` | PlayerState | Player |
| `Player/PlayerInventory.cs` | PlayerInventory | Player |
| `Player/PlayerModifiers.cs` | PlayerModifiers | Player |
| `Player/ActionQueue.cs` | ActionQueue | Player |
| `Player/AZPlayerVisual.cs` | AZPlayerVisual | Visual |
| `Player/FPSVisualController.cs` | FPSVisualController | Visual |
| `Turn/TurnManager.cs` | TurnManager | Turn |
| `Turn/ITurnContext.cs` | ITurnContext | Turn |
| `Turn/RoundLifecycleService.cs` | RoundLifecycleService | Turn |
| `Turn/EnvironmentRuleService.cs` | EnvironmentRuleService | Turn |
| `Combat/CombatEngine.cs` | CombatEngine, CombatSnapshot | Combat |
| `Combat/CombatResolver.cs` | CombatResolver | Combat |
| `Combat/CombatResult.cs` | CombatResult + CombatResultData | Combat |
| `Combat/CombatVFXManager.cs` | CombatVFXManager | VFX |
| `Combat/ScreenVFXManager.cs` | ScreenVFXManager | VFX |
| `Combat/EnvironmentVFXManager.cs` | EnvironmentVFXManager | VFX |
| `Combat/TemperatureSystem.cs` | TemperatureSystem | Combat |
| `Combat/PresentationBarrier.cs` | PresentationBarrier | Combat |
| `Combat/TwoPlayerCombatMapper.cs` | TwoPlayerCombatMapper | Combat |
| `Item/ItemManager.cs` | ItemManager | Item |
| `Item/ItemDropTable.cs` | ItemDropTable | Item |
| `Item/ItemSlotNetData.cs` | ItemSlotNetData | Item |
| `Item/ItemEnums.cs` | Enums (15+) | Item |
| `Item/ItemWorldView.cs` | ItemWorldView | Item/Visual |
| `Item/ItemEffectApplicator.cs` | ItemEffectApplicator | Item |
| `Item/ItemEffectOutcome.cs` | ItemEffectOutcome, InventoryMutationType | Item |
| `Item/Data/ItemDataSO.cs` | ItemDataSO (abstract) | Item |
| `Item/Data/AttackItemDataSO.cs` | AttackItemDataSO | Item |
| `Item/Data/RecoveryItemDataSO.cs` | RecoveryItemDataSO | Item |
| `Item/Data/DefenseItemDataSO.cs` | DefenseItemDataSO | Item |
| `Item/Data/BuffItemDataSO.cs` | BuffItemDataSO | Item |
| `Item/Data/DebuffItemDataSO.cs` | DebuffItemDataSO | Item |
| `Item/Data/SpecialItemDataSO.cs` | SpecialItemDataSO | Item |
| `Item/Data/SabotageItemDataSO.cs` | SabotageItemDataSO | Item |
| `Item/Data/ItemContext.cs` | ItemContext | Item |
| `Buff/BuffDebuffSystem.cs` | BuffDebuffSystem | Buff |
| `Match/MatchManager.cs` | MatchManager | Match |
| `Match/MatchCompositionRoot.cs` | MatchCompositionRoot | Match |
| `Match/MatchScoreView.cs` | MatchScoreView | Match |
| `Audio/GameAudioManager.cs` | GameAudioManager | Audio |
| `Common/GameEnums.cs` | TurnPhase, EnvironmentType | Common |
| `Common/GameSprites.cs` | GameSprites | Common |
| `Common/HoverRaycaster.cs` | HoverRaycaster | Input |
| `Common/HoverEffect.cs` | HoverEffect | Input |
| `Common/CameraShake.cs` | CameraShake | VFX |
| `Common/FanBladeSpinner.cs` | FanBladeSpinner | Visual |
| `Common/IceboxController.cs` | IceboxController | Visual |
| `Emote/EmoteCatalog.cs` | EmoteCatalog | Emote |
| `Emote/EmoteBubble.cs` | EmoteBubble | Emote |
| `Inventory/InventoryPresenter.cs` | InventoryPresenter | Inventory/UI |

### UI Assembly — `AbsoluteZero.UI` (Assets/Scripts/UI/)

| Path | Class | System |
|------|-------|--------|
| `Game/AZGameUI.cs` | AZGameUI | Game UI |
| `Lobby/AZLobbyUI.cs` | AZLobbyUI | Lobby UI |
| `Loading/LoadingScreenManager.cs` | LoadingScreenManager | Loading |
| `Emote/EmoteWheel.cs` | EmoteWheel | Emote UI |
| `Common/UiFont.cs` | UiFont | Font Utility |
| `MiniGame/MiniGameHub.cs` | MiniGameHub | MiniGame |
| `MiniGame/MiniGameUIBase.cs` | MiniGameUIBase (abstract) | MiniGame |
| `MiniGame/MiniGameArt.cs` | MiniGameArt | MiniGame |
| `MiniGame/WaterGunMiniGameUI.cs` | WaterGunMiniGameUI | MiniGame |
| `MiniGame/TapeCutMiniGameUI.cs` | TapeCutMiniGameUI | MiniGame |
| `MiniGame/PatternUnlockMiniGameUI.cs` | PatternUnlockMiniGameUI | MiniGame |
| `MiniGame/ScrewdriverMiniGameUI.cs` | ScrewdriverMiniGameUI | MiniGame |
| `MiniGame/RedCardMiniGameUI.cs` | RedCardMiniGameUI | MiniGame |
| `MiniGame/HotPackMiniGameUI.cs` | HotPackMiniGameUI | MiniGame |
| `MiniGame/BuldakMiniGameUI.cs` | BuldakMiniGameUI | MiniGame |
| `MiniGame/ClawGrabMiniGameUI.cs` | ClawGrabMiniGameUI | MiniGame |
| `MiniGame/HugCharacterMiniGameUI.cs` | HugCharacterMiniGameUI | MiniGame |
| `Utility/ScrollableLogDisplay.cs` | ScrollableLogDisplay | Utility |

### Other

| Path | Class | System |
|------|-------|--------|
| `Test/AnimationTestRunner.cs` | AnimationTestRunner | Test |
| `Test/FPSSpawnTest.cs` | FPSSpawnTest | Test |
| `Game/DebugItemGranter.cs` | DebugItemGranter | Debug |
| `TestUI/OutgoingDataLog.cs` | OutgoingDataLog | Test UI |
| `TestUI/LobbyPlayerSlotsUI.cs` | LobbyPlayerSlotsUI | Test UI |
| `TestUI/LobbyTestUI.cs` | LobbyTestUI | Test UI |

### Total: ~94 C# files (Core: ~72, UI: ~18, Test: ~4)
