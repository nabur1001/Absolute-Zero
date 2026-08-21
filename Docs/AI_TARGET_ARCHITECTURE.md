# Absolute Zero — AI Target Architecture Contract v2

> Audience: AI coding agents and maintainers.  
> Status: approved design target, not proof that the refactor is already implemented.  
> Baseline audit: 2026-08-18; migration and scale review: 2026-08-19; Phase 1~4 implementation sync: 2026-08-19.  
> Human-facing rationale and diagrams: `Docs/ARCHITECTURE_EVOLUTION_KO.md`.
> Canonical implementation order and phase gates: `Docs/Plans/PLAN_018_architecture_migration.md`.
> Future four-player feature plan: `Docs/Plans/PLAN_019_four_player_expansion.md`.

## 1. How to Use This Document

Read this document when a task changes architecture, networking, async behavior, initialization, manager ownership, player identity, turn flow, presentation events, resource loading, pooling, or player-count scaling.

For implementation truth, inspect current code and serialized assets. For intended gameplay behavior, read `Docs/GAME_DESIGN.md`. This document defines the desired direction and migration constraints; it does not authorize opportunistic broad refactors.

When code and this target differ:

1. Preserve current behavior unless the user requests a behavior change.
2. Introduce seams incrementally.
3. Keep old and new paths temporarily compatible when a single atomic migration is unsafe.
4. Verify Host and Client behavior after every network-facing phase.

### Player-count scope

The executable baseline remains a two-player game. `PLAN_018` is a behavior-preserving architecture migration: new contracts must avoid hardcoded pair ownership, but its runtime gates use `RequiredPlayerCount = 2`. It must not silently introduce four-player combat rules, UI, or balance changes.

The target seams support a configured roster of two to four players. Actual four-player gameplay is a separate feature migration in `PLAN_019`, after target selection, victory, elimination, disconnect, reconnect, information-visibility, and presentation-order rules are approved in `GAME_DESIGN.md`.

| In `PLAN_018` | Deferred to `PLAN_019` |
|---|---|
| Collection-based Registry and player iteration | Lobby-assigned stable PlayerSeat policy |
| Explicit ClientId/PlayerIndex conversion | ParticipantId reconnect mapping |
| Two-player behavior through pure seams | N-player action ordering and combat rewrite |
| Item-effect mutation seam with unchanged results | Multi-target rules and variable-length combat batches |
| Local/remote presenter role separation | Multi-opponent layout and visibility policy |
| Match-scoped events and lifecycle cleanup | Elimination/spectator lifecycle and four-player UI |

### Terminology

| Term | Meaning and lifetime |
|---|---|
| `ParticipantId` | Stable authentication/lobby participant key used by the future roster; it is not an NGO connection ID. |
| `PlayerIndex` | Numeric key of a match seat. PLAN_018 currently assigns `0..1`; PLAN_019 preserves the assigned value across reconnect instead of renumbering it. |
| `PlayerSeat` | PLAN_019 roster record/policy position identified by `PlayerIndex`; never use `PlayerSlot`, which is ambiguous with an inventory slot. |
| inventory slot | Position in `PlayerInventory.SlotStates` (`0..11`); unrelated to identity, roster order, or spawn position. |
| `PlayerIdentity` | Pure mapping value for logical player index and current ClientId. |
| `PlayerBinding` | Match-lifetime bundle of spawned Unity/NGO object references for one identity. |
| Player Registry | Spawn/despawn-driven lookup of currently bound player objects; it does not own reconnect, victory, or elimination policy. |
| Match Roster | Server-owned PLAN_019 participant/seat policy, including disconnected, eliminated, and reconnectable states. |

## 2. Reference-Project Adoption Decision

The MiniSurvival reference demonstrates useful techniques but has a different runtime model. It is a small single-player educational project; Absolute Zero is a host-authoritative NGO multiplayer game.

### Adopt

- Interfaces at external, variable, and test-relevant boundaries.
- Explicit initialization pipelines.
- Lifecycle-linked cancellation.
- Unity `IObjectPool<T>` for high-churn presentation objects.
- Dependency direction enforced by composition and eventually by asmdefs.
- Typed event contracts between domain results and local presentation.
- Functional Core / Imperative Shell for combat and item effects: pure rules return typed outcomes; the authoritative network shell applies them.

### Adopt Selectively

- Unity `Awaitable` for Unity frame/time/lifecycle operations.
- `Task` for Unity Services, Lobby, Relay, file, and web I/O.
- Coroutines for existing interruptible VFX and animation sequences until a focused migration is justified.
- ScriptableObject catalogs for read-only configuration and asset references.
- Addressables only for measured non-network asset-loading needs.

### Do Not Copy Literally

- Do not introduce a static global EventBus.
- Do not replace replicated state with ScriptableObject EventChannels.
- Do not convert every coroutine to Awaitable in one change.
- Do not move NGO Player/NetworkPrefabs to Addressables without a dedicated migration plan.
- Do not add interfaces to every class.
- Do not add the Job System to two-player turn calculations without profiler evidence.

## 3. Current Baseline and Known Structural Debt

- Unity: `6000.3.11f1`.
- NGO: `2.11.2`; Transport: `2.7.2`.
- 72+ C# files under `Assets/Scripts`.
- 21 item ScriptableObject assets.
- No project asmdef/asmref files.
- 19 files expose a static `Instance` property.
- Async usage is `Task`/`async void`; the project currently has no Unity `Awaitable` implementation.

### Resolved (Phase 1~4)

- ~~Production lobby flow is split across `LobbyManager`, `RelayManager`, `SessionManager`, and `AZLobbyUI`.~~ → `AZLobbyUI` routes through `NetworkSessionCoordinator`; old managers remain as legacy adapters via Strangler Fig.
- ~~Player identity expressed as ClientId, OwnerClientId, logical player index, or list position inconsistently.~~ → `PlayerIdentity`/`PlayerBinding`/`PlayerRegistry` in place; consumers use registry lookup.
- ~~TurnManager reads Host-local VFX Singleton state.~~ → `PresentationBarrier` with sender-validated ACK.
- ~~Unity Services dual initialization race between LobbyManager and coordinator.~~ → `AppBootstrapper` owns single init sequence.
- ~~No external disconnect recovery.~~ → Coordinator subscribes to `OnClientStopped` and resets state.

### Remaining Debt

- `TurnManager` owns server phase orchestration, environment effects, combat invocation, round reset, RPC broadcast, and player discovery.
- `AZGameUI`, `CombatVFXManager`, `InventoryPresenter`, and `EnvironmentVFXManager` have broad mixed responsibilities.
- Static events and scene singletons cross GameScene lifetime boundaries.
- `TurnManager`, `CombatResultData`, `MatchManager`, combat VFX, inventory presentation, and UI contain explicit P1/P2 or single-opponent assumptions.
- `TurnManager` allocates `PlayerModifiers[2]`; `PlayerState.BuildContext()` resolves one opponent through `TurnManager.Instance`; `InventoryPresenter` owns one `_opponentPlayer`/`_opponentInventory` binding.
- `ItemContext`, `CombatResolver`, `TemperatureSystem`, `BuffDebuffSystem`, and all seven `ItemDataSO.ExecuteEffect` subclasses cross the intended Domain/Networking boundary by reading or mutating `PlayerState`, NetworkVariables, and NetworkLists directly.
- The current Lobby configuration defaults to two players; future support up to four players is a design target, not current runtime behavior.
- Legacy `LobbyManager.CreateLobbyAsync()`, `JoinLobbyByCodeAsync()`, `LeaveLobbyAsync()` remain public but are no longer called by UI; they should be made internal or removed when all consumers migrate to the coordinator.

Do not treat raw call counts as defects. Prioritize operations that repeat per frame, per network poll, per inventory rebuild, or per combat sequence.

## 4. Target Dependency Model

```text
Presentation
    -> Application
    -> Foundation

Application
    -> Domain
    -> Infrastructure contracts
    -> Foundation

Infrastructure
    -> Foundation
    -> external Unity/UGS/NGO APIs

Domain
    -> Foundation only
```

Rules:

- Domain must not reference UGUI, TMP, Lobby, Relay, NGO, scene objects, or presenters.
- Presentation must not mutate authoritative gameplay state directly.
- Infrastructure adapters translate external SDK objects and exceptions into project contracts.
- Application coordinators own operation order and compensation, not visual construction.
- Foundation contains small contracts, identity types, results, event DTOs, clocks, and random abstractions.
- Domain commands and results must not implement `INetworkSerializable` or reference `PlayerState`. Networking DTOs map to and from pure Domain values at the bridge boundary.
- In the final target, `TurnFlowCoordinator` owns phase decisions while a `TurnNetworkState`/`TurnNetworkBridge` `NetworkBehaviour` owns NetworkVariables and RPC publication. During migration, the existing `TurnManager` NetworkBehaviour remains the single driver until that cutover is verified.
- Do not create a general-purpose runtime service locator. Manual composition is preferred.

## 5. Lifetime Scopes and Composition Roots

### App Scope

Created in `LobbyScene`, persists until application shutdown.

Owner: `AppBootstrapper` (singleton on Managers DDOL GameObject).

Services (on the same Managers GameObject, DDOL):

- `AppBootstrapper` — initialization sequence owner
- `NetworkSessionCoordinator` — session state machine, gateway consumer
- `LobbyManager` — heartbeat/poll loop, coordinator sync bridge (Strangler Fig)
- `RelayManager` — relay state holder (legacy, used by coordinator.LeaveAsync cleanup)
- `SessionManager` — NGO disconnect callbacks (legacy, coordinator OnClientStopped replaces)
- `PlayerSpawnManager` — player spawn on GameScene

Gateway instances (created in coordinator.Awake, not MonoBehaviours):

- `IUnityServicesGateway` → `UnityServicesGateway`
- `ILobbyGateway` → `LobbyGateway`
- `IRelayGateway` → `RelayGateway`
- `INetworkRuntime` → `NgoNetworkRuntime`
- `ISceneTransitionService` → `SceneTransitionService`

Responsibilities:

- Initialize Unity Services and Authentication once via `AppBootstrapper.Start()` → `coordinator.InitializeAsync()`.
- Expose explicit app readiness via `AppBootstrapper.IsReady` and `AppBootstrapper.OnReady`.
- Coordinator UI waits for `SessionState.Ready` before enabling lobby commands.
- LobbyManager no longer self-initializes; bootstrapper owns the single init path.
- Prevent duplicate DDOL roots (LobbyManager.Awake calls DontDestroyOnLoad on the shared GameObject).

Future: `AppLifetime` cancellation token, app-scoped local event hub (Phase 5+).

### Match Scope

Created on `GameScene` entry, disposed on scene exit.

Owner: `MatchCompositionRoot`.

Services:

- `IReadOnlyPlayerRegistry`
- match configuration/roster policy when the four-player feature is activated
- `TurnFlowCoordinator`
- `MatchCoordinator`
- `IItemCatalog`
- `CombatEngine`
- `EnvironmentRuleService`
- `RoundLifecycleService`
- `TurnNetworkBridge`
- `IPresentationBarrier`
- match-scoped local event hub
- UI/VFX/Audio presenters and presentation pools

Responsibilities:

- Validate scene references once.
- Bind spawned players by explicit identity.
- Construct server-only domain services on the server.
- Bind client presentation without making it authoritative.
- Dispose subscriptions, pools, and cancellation sources on scene exit.

## 6. Target Management Owners

Only three top-level orchestration owners should remain:

1. `AppBootstrapper`: application initialization sequence — calls `coordinator.InitializeAsync()`, exposes `IsReady`/`OnReady`.
2. `NetworkSessionCoordinator`: session state machine — lobby create/join, relay, NGO startup, scene entry, disconnect, external disconnect recovery via `OnClientStopped`.
3. `MatchCompositionRoot` plus `TurnFlowCoordinator`: GameScene wiring and authoritative match flow.

Legacy managers during Strangler Fig transition:

- `LobbyManager`: heartbeat/poll loop, event bridge for UI (`OnLobbyCreated`, `OnLobbyUpdated`, `OnLobbyLeft`). Coordinator calls `SyncFromCoordinator` + `Fire*Event` to keep it in sync. Its public lobby CRUD methods (`CreateLobbyAsync`, `JoinLobbyByCodeAsync`, `LeaveLobbyAsync`) are no longer called by UI but remain for backward compatibility until all consumers migrate.
- `RelayManager`: relay state holder (`IsRelayConnected`, `CurrentJoinCode`). Coordinator calls `ClearState()` on leave.
- `SessionManager`: NGO disconnect callbacks (`OnClientStopped`, `OnTransportFailure`). The coordinator's own `OnClientStopped` handler provides the same recovery; SessionManager's `Disconnect()` is retained as a fallback path.

Other systems are services, adapters, state holders, or presenters. They must not independently discover the whole graph through `Find*` or unrelated Singleton access.

## 7. Required Boundary Contracts

### IUnityServicesGateway

```csharp
public interface IUnityServicesGateway
{
    bool IsInitialized { get; }
    bool IsSignedIn { get; }
    string PlayerId { get; }
    Task<Result<Unit>> InitializeAndSignInAsync(string profileOverride = null);
}
```

Wraps `UnityServices.InitializeAsync` and `AuthenticationService.Instance.SignInAnonymouslyAsync`. Guards against double-init with `ServicesInitializationState` check. Returns `Unit` because the caller accesses `PlayerId` through the property; a dedicated `AuthenticatedUser` DTO is deferred until a real consumer needs it. The `profileOverride` parameter supports ParrelSync clone detection. No `CancellationToken` — the installed SDK does not accept one; the coordinator uses `OperationGeneration` to reject stale completions.

### ILobbyGateway

```csharp
public interface ILobbyGateway
{
    Task<Result<Lobby>> CreateAsync(string name, int maxPlayers, CreateLobbyOptions options);
    Task<Result<Lobby>> JoinByCodeAsync(string code, JoinLobbyByCodeOptions options);
    Task<Result<Lobby>> JoinByIdAsync(string id, JoinLobbyByIdOptions options);
    Task<Result<Lobby>> QuickJoinAsync(QuickJoinLobbyOptions options);
    Task<Result<List<Lobby>>> QueryAsync(QueryLobbiesOptions options);
    Task<Result<Lobby>> GetAsync(string lobbyId);
    Task<Result<Lobby>> UpdateAsync(string lobbyId, UpdateLobbyOptions options);
    Task<Result<Lobby>> UpdatePlayerAsync(string lobbyId, string playerId, UpdatePlayerOptions options);
    Task<Result<Unit>> DeleteAsync(string lobbyId);
    Task<Result<Unit>> RemovePlayerAsync(string lobbyId, string playerId);
    Task<Result<Unit>> SendHeartbeatAsync(string lobbyId);
}
```

Current implementation passes Unity Lobby SDK types (`Lobby`, `CreateLobbyOptions`, etc.) through the gateway boundary. This is an intentional Strangler Fig trade-off: the coordinator already isolates UI from SDK details, and introducing domain DTOs (`LobbySnapshot`, `CreateLobbyRequest`) at this stage doubles the surface area without a consumer. When a Domain layer or test double needs a pure lobby model, introduce the anti-corruption mapping then. No `CancellationToken` — Lobby SDK 1.3.0 does not accept one.

### IRelayGateway

```csharp
public readonly struct RelayHostResult
{
    public RelayServerData ServerData { get; }
    public string JoinCode { get; }
}

public readonly struct RelayJoinResult
{
    public RelayServerData ServerData { get; }
}

public interface IRelayGateway
{
    Task<Result<RelayHostResult>> AllocateAsync(int maxConnections);
    Task<Result<RelayJoinResult>> JoinAsync(string joinCode);
}
```

The gateway allocates or joins Relay. It does not start NGO. `RelayServerData` is from `Unity.Networking.Transport.Relay` — it crosses the boundary because `INetworkRuntime.StartHost/Client` consumes it directly. A pure DTO would add a mapping layer with no consumer benefit. No `CancellationToken` — Relay SDK 1.0.5 does not accept one.

### INetworkRuntime

```csharp
public interface INetworkRuntime
{
    bool IsListening { get; }
    bool IsHost { get; }
    bool IsClient { get; }
    ulong LocalClientId { get; }
    int ConnectedClientCount { get; }
    Result<Unit> StartHost(RelayServerData relayData);
    Result<Unit> StartClient(RelayServerData relayData);
    void Shutdown();
    Result<Unit> LoadNetworkScene(string sceneName);
}
```

Owns UnityTransport configuration and `NetworkManager` start/stop/scene-load calls. `LoadNetworkScene` validates host authority and SceneManager availability. Returns synchronous `Result<Unit>` because NGO start/stop is immediate.

### ISceneTransitionService

```csharp
public interface ISceneTransitionService
{
    void LoadTitleScene();
}
```

Owns non-network scene transition back to LobbyScene. Called by `coordinator.LeaveAsync()` when leaving from GameScene. It must not own Lobby or Relay calls.

### IReadOnlyPlayerRegistry

```csharp
public interface IReadOnlyPlayerRegistry
{
    IReadOnlyCollection<PlayerBinding> Players { get; }
    int ReadyCount { get; }
    bool TryGetByPlayerIndex(byte playerIndex, out PlayerBinding player);
    bool TryGetByClientId(ulong clientId, out PlayerBinding player);
    event Action<PlayerBinding> Registered;
    event Action<PlayerIdentity> Unregistered;
}
```

Keep pure identity separate from live Unity references. `PlayerIdentity` contains the match-stable logical PlayerIndex and current NGO ClientId; `PlayerBinding` contains match-lifetime `PlayerState`, `PlayerInventory`, and `NetworkObject` references. Never derive identity by casting or array position. Give ordinary consumers only the read-only registry contract. Registration events replace polling, but consumers must still query the current snapshot when binding late. The registry reports bindings; it does not choose match size, victory rules, or reconnect policy.

### ILocalEventHub

Use an instance owned by App or Match scope. Subscriptions must return an `IDisposable` token or be explicitly unregistered. The hub is local-only; it never replaces NetworkVariables, NetworkLists, or authoritative RPC validation.

### IPresentationBarrier

Coordinates combat result presentation completion.

Requirements:

- Every combat result has a monotonically increasing `uint ResultSequence` within the match.
- The server snapshots the active match participants that are expected to present this result and opens the barrier before broadcasting the result RPC.
- Each client acknowledges the same `ResultSequence` after presentation completion.
- Server advances when required acknowledgements arrive or a timeout expires.
- Duplicate, late, or mismatched acknowledgements are ignored.
- Disconnect removes that client from the required acknowledgement set.
- A client connected after `Begin` is not added to the in-flight barrier.
- The RPC sender ID, not a client ID supplied in the payload, identifies the acknowledger.
- The initial authoritative timeout is a server-owned serialized 10 seconds, measured with unscaled/server elapsed time. Promote it to a read-only timing catalog only when multiple match timings need shared configuration.

This replaces the server reading Host-local `CombatVFXManager.Instance.IsPlaying`.

## 8. Optional Testability Contracts

Introduce only when used by a real consumer or test:

- `IServerClock`: wraps NGO ServerTime.
- `ITimeSource`: wraps scaled/unscaled Unity time.
- `IRandomSource`: deterministic item/environment selection.
- `ICombatResolver`: alternative rules or test doubles.
- `IItemCatalog`: ItemId-to-SO lookup and validation.
- `IMatchResettable`: explicit turn, round, and match reset hooks.
- `ICombatSequencePlayer`: client presentation sequence.
- `IObjectPool<T>`: high-churn reusable presentation objects.
- `IPlayerIntentSink`: Phase 5 PlayerState RPC ingress delegation, only when the first concrete PlayerState consumer is migrated; otherwise bind a narrow TurnFlowCoordinator method directly.

Do not introduce empty marker interfaces or generic `IManager` abstractions.

## 9. Player Identity Contract

Never conflate:

- NGO ClientId;
- `NetworkObject.OwnerClientId`;
- logical player index / match seat (`0..RequiredPlayerCount-1`, currently `0..1`);
- lobby player ID;
- list or array index;
- local/remote presentation role.

Target identity and runtime binding split:

```csharp
public readonly struct PlayerIdentity
{
    public byte PlayerIndex { get; }
    public ulong ClientId { get; }
}

public sealed class PlayerBinding
{
    public PlayerIdentity Identity { get; }
    public PlayerState State { get; }
    public PlayerInventory Inventory { get; }
    public NetworkObject NetworkObject { get; }
}
```

`PlayerIdentity` is a pure value. `PlayerBinding` is valid only for the Match lifetime and must be removed idempotently on despawn, disconnect, or scene exit. Add `PlayerState.OnNetworkSpawn` and `OnNetworkDespawn`; the latter is the primary unregister hook on every peer. A spawned player whose `SyncedPlayerIndex` is still `-1` stays in a ClientId-keyed pending map. Check the current index snapshot first, subscribe only when it is invalid, and publish `Registered` exactly once after a valid index is observed. On despawn, unsubscribe the index callback and emit `Unregistered` from cached identity. Server disconnect handling removes pending spawn work and uses ClientId fallback unregister only when no despawn hook can run.

`PLAN_018` preserves the existing ascending-ClientId assignment for the two-player baseline. That rule is not the four-player or reconnect contract. Before four-player activation, a server-owned roster assigns an immutable match seat once and maps a stable participant identifier to the current ClientId. Disconnect or reconnect must never renumber active seats. All combat, match, VFX, and UI conversions go through the registry or roster mapping.

### Replicated player data grouping

Group fields into a custom NetworkVariable snapshot only when they share authority, visibility, lifetime, and update cadence. Reducing the number of NetworkVariable declarations is not by itself an optimization target.

The current `PlayerState` owns eight NetworkVariables. The default migration decision is to keep them individual until atomicity or payload profiling justifies a group.

| Current state | Semantic group | Guidance |
|---|---|---|
| `SyncedPlayerIndex` | identity | keep separate; assigned once by the server |
| `Temperature`, `FanSpeed` | public thermal state | group only if they must update atomically |
| `IsReady`, `IsFanActive`, `HasSelectedItem` | turn-visible state | reset per turn; do not include the secret selected item or target |
| `IsFanUpgraded`, `IsBasicBlocked` | rule/modifier state | keep separate until their exact reset lifetime is stabilized |

Match-owned round wins remain in match network state rather than being moved into PlayerState merely to complete a snapshot. Inventory remains a NetworkList because its incremental changes have different semantics. `IsAlive` is currently derived from Temperature rules rather than stored, and there is no Health field. Secret turn intent—including selected item, target, sub-action, and ready-order timestamp—remains server-only unless a specific owner-only replication requirement is approved.

Network DTOs use the current data widths unless a dedicated schema migration proves otherwise. In particular, item IDs remain `short` because the current protocol uses `-1` as an empty sentinel. Any custom NetworkVariable struct must implement the equality and serialization contracts required by the installed NGO version, and writes replace the whole value through a server-authorized path.

## 10. Authority and Event Rules

### Replicated State

Use `NetworkVariable` or `NetworkList` for current state that a late client or reconnect path must observe:

- phase;
- temperature;
- score;
- inventory;
- ready state;
- environment;
- match state.

Choose replication by semantics, not by field-count minimization:

- public reconnectable state -> Everyone-readable NetworkVariable/NetworkList;
- owner-private reconnectable state -> Owner-readable replicated state only when required;
- secret pending action intent -> server-only runtime command state;
- one-shot presentation instruction -> RPC DTO;
- local derived display state -> presenter-local state.

### Network Events

Use RPC plus a serializable DTO for a one-time server-issued command:

- combat result presentation;
- environment staging request;
- death presentation;
- presentation acknowledgement.

### Local Events

Use `ILocalEventHub` or direct C# events for derived same-process notifications:

- local player bound;
- inventory snapshot changed;
- combat presentation started/completed;
- HUD view updates;
- local audio requests.

Never use local events as the only source of authoritative or reconnectable state.

Pure Domain commands/results and NGO wire DTOs are separate types. For example, `ActionIntent` and `CombatResolution` belong to Domain, while `ActionInputNetData` and `CombatResolutionNetData` implement NGO serialization in Networking. A mapper at `TurnNetworkBridge` is the anti-corruption boundary; Domain never accepts an `INetworkSerializable` type merely for transport convenience.

## 11. Async Policy

### Use Task For

- Unity Services initialization;
- Authentication;
- Lobby operations;
- Relay allocation/join;
- file or web I/O;
- `Task.WhenAll` for genuinely independent I/O.

The installed Lobby 1.3.0 and Relay 1.0.5 public SDK methods do not accept a `CancellationToken`. For those calls, the coordinator uses `OperationGeneration` — a uint incremented on every session operation — to reject stale completions before committing state. `OperationScope` captures the generation at operation start and provides `IsStale` for post-await validation plus a reverse-order compensation stack for saga-pattern rollback.

Future: when app/session lifetime `CancellationTokenSource` are introduced (Phase 5+), service Tasks should accept tokens where the SDK permits. Until then, `OperationGeneration` is the canonical staleness guard.

### Use Unity Awaitable For

- Unity frame waits;
- Unity-time waits;
- scene-local async sequences;
- new lifecycle-bound loading/presentation flows;
- explicit main/background thread switching.

Rules:

- Link waits to `destroyCancellationToken`, match token, or operation token.
- Treat `OperationCanceledException` from expected teardown as normal.
- Return to the main thread before accessing Unity objects after background work.
- Never await the same pooled Awaitable instance more than once.

### Keep Coroutines For Now

- existing combat VFX choreography;
- environment staging;
- mini-game animation;
- animation routines already controlled by coroutine handles.

Migrate a coroutine only when cancellation, composition, error propagation, or readability measurably improves. Do not mix a migrated Awaitable and the old coroutine as two owners of the same visual state.

### Job System

Do not use for current combat, item, or turn calculations. Consider only after profiling a large, data-parallel pure computation. Unity object access is forbidden inside jobs.

### async void

Allowed only for thin Unity lifecycle or UI event entry points.

```csharp
private async void OnStartClicked()
{
    try
    {
        SetBusy(true);
        await StartClickedAsync(destroyCancellationToken);
    }
    catch (OperationCanceledException) { }
    catch (Exception exception)
    {
        ReportUnexpected(exception);
    }
    finally
    {
        SetBusy(false);
    }
}
```

Managers, gateways, and coordinators must return `Task`, `Task<T>`, `Awaitable`, or `Awaitable<T>`.

### Lifetime Hierarchy

```text
Application.exitCancellationToken
    -> App lifetime
        -> Session lifetime
            -> Match-scene lifetime
                -> Turn operation
                -> Presentation operation
```

Cancel Session lifetime on disconnect. Cancel Match lifetime on GameScene exit. Cancel presentation operations when their owner is destroyed or a new exclusive sequence supersedes them.

## 12. Lobby Polling and Heartbeat

Replace `Update` timers plus `async void` calls with single-owner loops.

Rules:

- Do not start a new request until the previous one finishes.
- Use unscaled real time; Lobby maintenance must not depend on `Time.timeScale`.
- Capture lobby ID and `OperationGeneration` before awaiting.
- Ignore a completion if `OperationGeneration` changed.
- Cancellation is normal on leave, disconnect, or game start.
- NotFound/Conflict transitions to an explicit expired/disconnected state.
- Transient failures use bounded backoff.
- Never dereference mutable `currentLobby` after an await without revalidation.

`Task.Delay(interval, token)` is acceptable for low-frequency unscaled Lobby loops. Awaitable is not mandatory merely because the project uses Unity 6.

## 13. Session Coordinator State Machine

Required top-level `SessionState` values:

```text
Offline
Initializing
Ready
Connecting
LoadingGame
InGame
Disconnecting
Failed
```

Required `SessionOperation` details while Connecting or LoadingGame:

```text
None
CreatingLobby
JoiningLobby
WaitingRelayCode
AllocatingRelay
JoiningRelay
StartingHost
StartingClient
WaitingForPlayers
```

During `Connecting`, use CreatingLobby, JoiningLobby, WaitingRelayCode, AllocatingRelay, JoiningRelay, StartingHost, or StartingClient. After the network scene has loaded, `LoadingGame` owns MatchCompositionRoot readiness and WaitingForPlayers. Use None when no sub-operation is active.

Only `NetworkSessionCoordinator` transitions these states.

UI derives button enabled/visible state from `SessionState` and progress text from `SessionOperation`. UI must not orchestrate Lobby, Relay, and Session managers independently.

Host operation order (two-step UX):

Step 1 — `CreateLobbyAsync(name)`:
```text
Ready -> Connecting/CreatingLobby
-> create lobby via ILobbyGateway
-> sync LobbyManager (SyncFromCoordinator + FireCreatedEvent)
-> Ready (with _currentLobby set, _isHostRole = true)
```

Step 2 — `StartMatchAsHostAsync()` (user clicks "게임 시작"):
```text
Ready (with lobby + host) -> Connecting/AllocatingRelay
-> allocate Relay via IRelayGateway
-> Connecting/StartingHost -> start NGO host via INetworkRuntime
-> publish Relay code + GameStarted to lobby via ILobbyGateway.UpdateAsync
-> LoadingGame -> load GameScene via INetworkRuntime.LoadNetworkScene
-> InGame
```

Each step in `StartMatchAsHostAsync` pushes compensation onto `OperationScope` for reverse-order rollback on failure.

Client operation order — `JoinGameAsync(code)`:

```text
Ready -> Connecting/JoiningLobby
-> join lobby by code via ILobbyGateway
-> sync LobbyManager + FireJoinedEvent (UI shows lobby panel)
-> Connecting/WaitingRelayCode
-> wait for relay code via LobbyManager.OnLobbyUpdated + TaskCompletionSource (timeout)
-> Connecting/JoiningRelay -> join Relay
-> Connecting/StartingClient -> start NGO client
-> LoadingGame (host drives network scene load)
-> InGame
```

Leave operation — `LeaveAsync()`:

```text
any state -> Disconnecting
-> increment OperationGeneration (cancels in-flight operations)
-> INetworkRuntime.Shutdown()
-> RelayManager.ClearState()
-> delete (host) or leave (client) lobby via ILobbyGateway
-> sync LobbyManager + FireLeftEvent
-> Ready or Failed
-> if was in GameScene: ISceneTransitionService.LoadTitleScene()
```

External disconnect recovery:

The coordinator subscribes to `NetworkManager.OnClientStopped`. When an external shutdown is detected (SessionManager.Disconnect, transport failure) while in InGame/LoadingGame/Connecting state, the coordinator resets `_currentLobby`, `_isHostRole`, increments `_operationGeneration`, syncs LobbyManager, and transitions to Ready or Failed. This prevents stale state when the coordinator didn't initiate the shutdown.

On failure, the coordinator performs compensation appropriate to completed steps via `OperationScope.RunCompensations()`. Do not distribute rollback across UI callbacks.

Remote cleanup failure, such as Lobby leave/delete failure, is recorded as a warning after local NGO shutdown and local snapshot cleanup complete; the coordinator still returns to Ready. Transition to Failed only when local invariants cannot be restored. `Failed -> Disconnecting` is an explicit user retry, never an automatic loop.

## 14. Match and Turn Target

### Player Discovery

Replace TurnManager's repeated `FindObjectsByType<PlayerState>` loop with spawn/despawn-driven registration. Waiting for the configured roster must be cancellable and timeout-aware. `PLAN_018` supplies two players; the collection and readiness checks must not encode pair fields.

### Turn Ownership

`TurnFlowCoordinator` remains the single authoritative phase driver.

Current two-player behavior-preserving target flow:

```text
Begin Prep
-> reset turn state and modifiers
-> apply environment-specific Prep work inside PrepPhase
-> open Prep/emote input and snapshot starting temperatures
-> tick temperature/recovery/threshold rules while collecting validated intent
-> stop when both ready or Prep timeout/death occurs
-> force missing Ready and revert temporary fan upgrades
-> close emote acceptance and wait only for the final accepted emote display
-> enter AttackPhase and process delayed effects
-> resolve combat synchronously on server
-> allocate uint ResultSequence
-> snapshot participants and begin PresentationBarrier
-> broadcast one result DTO with the same ResultSequence
-> await ACKs or timeout
-> resolve death/inventory/environment/round transition
```

The `PLAN_018` implementation runs this flow with two players and preserves existing gameplay. It may use collection-shaped inputs, but it must not invent four-player resolution semantics.

### Future N-player combat boundary

The later four-player feature snapshots the eligible actor set for each turn; it does not wait blindly for every configured seat when a participant is eliminated or disconnected. It uses a deterministic command pipeline:

```text
validated server-only ActionIntent[]
-> immutable start-of-resolution MatchCombatSnapshot
-> deterministic ordering policy
-> target policy validation
-> pure CombatEngine resolution
-> ordered CombatEvent[] + final PlayerStateDelta[]
-> CombatResolutionBatchNetData publication
```

`ActionIntent` carries source seat, selected inventory slot, and a target selection value. Target selection must support the approved gameplay policy—self, one player, several players, or all—not assume that `1 - sourceIndex` identifies an opponent. `GAME_DESIGN.md` must approve the N-player ordering and final tie-break policy before PLAN_019 implementation; stable PlayerIndex is only a candidate deterministic key, not an architecture-mandated winner. If the approved policy uses randomness, it enters through an injected deterministic random source and records enough information to reproduce the result.

One action can create multiple events and affect several players, so the wire result is an ordered batch, not "one result per player." The batch has a bounded maximum size, the same `ResultSequence`, ordered event DTOs, and final state deltas needed by presentation. The exact victory, elimination, disconnect, target, information-visibility, and sequential-versus-parallel presentation rules are `GAME_DESIGN.md` decisions and block `PLAN_019` implementation until approved.

### TurnManager Decomposition

Extract in this order while preserving behavior:

1. `PlayerRegistry` lookup as the completed Phase 1 vertical slice.
2. `PresentationBarrier` as the completed Phase 2 vertical slice.
3. Characterize the complete `ItemContext -> ItemDataSO.ExecuteEffect` mutation surface before extraction.
4. Introduce a pure `CombatContext`/item-effect input and typed effect results; keep a temporary adapter from the current `ItemContext` while each item category migrates.
5. Keep `PlayerState` as RPC ingress and replicated state holder, but delegate phase/intent decisions through an explicitly bound `IPlayerIntentSink`/TurnFlowCoordinator instead of `TurnManager.Instance`.
6. Make the authoritative coordinator apply returned temperature, modifier, inventory, scheduled-effect, and presentation outcomes to NetworkVariables/NetworkLists. Domain item rules never write `.Value` directly.
7. Extract `EnvironmentRuleService` authoritative gameplay decisions, separate from presentation staging.
8. Extract `RoundLifecycleService` server-only, idempotent reset logic.
9. Add semantic network publication methods inside the existing NetworkBehaviour and replace TurnManager static events with the match-scoped event hub during the final cutover.
10. Move RPCs into a `TurnNetworkBridge` NetworkBehaviour only after its NetworkObject wiring and authority behavior are explicitly verified.
11. Add an optional `PrepPhaseRunner` only if TurnManager remains too large.

The item pipeline migration includes `ItemContext`, `ItemDataSO.CanUse/ExecuteEffect`, all seven item subclasses, `CombatResolver`, `TemperatureSystem`, `BuffDebuffSystem`, and inventory mutations. ScriptableObjects remain immutable authoring/catalog configuration; an adapter copies the required values into a pure `ItemEffectSpec`, and Domain strategies return typed outcomes without retaining a ScriptableObject reference. Reroll, steal, and drop-table selection receive an `IRandomSource`; live code may adapt Unity random while tests inject a fixed sequence. Migrate category by category with two-player golden-result tests; do not replace the entire pipeline in one commit.

Do not create multiple phase drivers.

## 15. Presentation Target

### AZGameUI

Split conceptual roles:

- `GameHudViewBuilder`: construct runtime UI once.
- `GameHudPresenter`: subscribe and bind state.
- `TemperaturePresenter`: interpolation and combat overrides.
- `ReadyInputPresenter`: Ready/emote input.
- `EnvironmentBannerPresenter`: environment announcements.

Runtime-built UI remains allowed. The target is responsibility separation, not mandatory prefabs or Inspector wiring.

### InventoryPresenter

Split conceptual roles:

- `InventoryBinder`: map network state to a stable local snapshot.
- `InventoryViewPool`: obtain/release ItemWorldView objects.
- `InventoryInteractionPresenter`: click, confirmation, selection, blocked state.

Prefer incremental handling of `NetworkListEvent`:

- Value -> patch one view.
- Add -> obtain one pooled view.
- Remove -> release one view.
- Reset or ambiguous compaction -> snapshot diff.

Retain item-ID-based pending selection resolution when indices can shift.

For future four-player presentation, each client has one interactive local inventory presenter and zero to three read-only remote inventory presenters according to visibility rules. Do not instantiate N interactive presenters or expose pending private selections to opponents.

The current single `_opponentPlayer`/`_opponentInventory` model is explicit PLAN_019 debt. PLAN_018 may separate local versus remote roles, but it must preserve one-opponent behavior until the four-player visibility and layout policy is approved.

### CombatVFXManager

Target roles:

- `CombatSequencePlayer`: sequence-level orchestration.
- `ItemPresentationRegistry`: ItemId/category to strategy.
- pooled `EffectPool`.
- `CombatAudioPresenter`.
- `ICombatSequencePlayer` completion contract.

Do not allow server rules to depend on concrete VFX state.

## 16. Pooling Policy

Priority candidates:

1. hit, ice-break, and final-break effects;
2. `EmoteBubble`;
3. `ItemWorldView`;
4. frequently recreated mini-game helper objects;
5. recurring environment NPC/overlay objects.

Do not pool:

- Player NetworkObjects without a dedicated NGO spawn-pool design;
- persistent managers;
- one-time Canvas roots;
- objects whose reset cost exceeds creation cost.

Every pooled type must define:

- create;
- get/reset;
- release/cleanup;
- destroy/dispose.

Release must unsubscribe events, stop coroutines, cancel owned async work, clear targets, reset Animator state, and remove stale IDs.

Prefer Unity 6 `UnityEngine.Pool.ObjectPool<T>` / `IObjectPool<T>` for ordinary presentation objects. Do not add a project-local generic pool until a measured lifecycle requirement cannot be expressed by the Unity pool. Treat `ItemWorldView` as a later candidate because slot compaction and item identity must be stable before diffing and reuse.

## 17. Resource Policy

Stage 1:

- Create typed read-only catalogs for visual, audio, marker, and item-presentation references.
- Load or bind catalog contents once per scope.
- Replace repeated string `Resources.Load` calls and scattered string paths.
- Keep ScriptableObject configuration runtime-read-only.

Stage 2, optional and evidence-driven:

- Move large non-network VFX, audio, and environment assets to Addressables.
- Make one resource service own handles and release.
- Define fallback behavior for failed loads.
- Do not migrate Player NetworkPrefab or required item registry in the first Addressables phase.

## 18. Assembly Definition Target

Target assemblies:

```text
AbsoluteZero.Foundation
AbsoluteZero.Domain
AbsoluteZero.Networking
AbsoluteZero.Application
AbsoluteZero.Presentation
AbsoluteZero.Tests
```

Dependency direction:

```text
Foundation <- Domain
Foundation <- Networking
Domain + Networking <- Application
Foundation + Application <- Presentation
Domain + Application <- Tests
```

Prerequisites:

- Remove Presentation concrete dependencies from Turn/domain logic.
- Resolve UI/Core circular references.
- Reduce cross-folder Singleton access.
- Add asmdefs as one planned migration, preserving `.meta` files.
- Verify all Unity package assembly references in the Editor.

Do not add a single isolated asmdef that leaves most project code in `Assembly-CSharp` and causes unintended reverse references.

## 19. Performance Priorities

Optimize in this order:

1. Eliminate duplicated work and overlapping async requests.
2. Fix lifetime leaks and stale subscriptions.
3. Cache scene/player/marker lookups.
4. Incrementally update views instead of full rebuilds.
5. Pool measured high-churn presentation objects.
6. Centralize repeated asset loading.
7. Reduce network message count or payload only after profiling.
8. Use Jobs/background threads only for measured CPU bottlenecks.

Existing strengths to preserve:

- server-authoritative mutation;
- compact `ItemSlotNetData`;
- once-per-second RemainingTime updates;
- server-owned `ActionQueue` and the existing combat/temperature/buff behavior, preserved as golden results while Unity and NGO dependencies are removed from Domain rules;
- item ScriptableObject authoring hierarchy as immutable configuration; preserve its data and behavior contract, not the current NGO-coupled `ItemContext` mutation path;
- inventory rebuild lock during combat presentation;
- cached constant-duration waits.

## 20. Migration Phases

`Docs/Plans/PLAN_018_architecture_migration.md` is canonical for detailed steps, stop conditions, and validation gates. This section is the compact routing summary.

### Phase 0 — Characterization

- Capture Host/Client lobby, join, match, turn, round-reset, and disconnect behavior.
- Record relevant console output and profiler baselines.
- Add focused test checklists before structural changes.

### Phase 1 — Player Identity Vertical Slice ✅

- `PlayerIdentity` (pure value), `PlayerBinding` (match-lifetime), `PlayerRegistry` (spawn/despawn-driven).
- `MatchCompositionRoot` on GameScene owns registry lifecycle.
- `PlayerState.OnNetworkSpawn/OnNetworkDespawn` registration hooks.
- `CombatVFXManager` and `BuildContext` migrated to registry lookups.
- `TurnManager` `FindObjectsByType` replaced with registry polling.

### Phase 2 — Combat Sequence and Presentation Barrier ✅

- `uint ResultSequence` on `CombatResult`/`CombatResultData`.
- `PresentationBarrier`: sender-validated ACK, duplicate/late rejection, disconnect removal, timeout.
- `CombatVFXManager` try/finally exit unification + ACK RPC.
- TurnManager `IsPlaying` poll replaced with `barrier.WaitForCompletion`.

### Phase 3 — Async Session Integration ✅

- `LobbyManager`: in-flight guards (`_heartbeatInFlight`/`_pollInFlight`), `async void`→`Task`, `OperationGeneration`, `SyncFromCoordinator`/`Fire*Event` internal methods.
- 13 files under `Core/Session/`: `Result<T>` + `OperationErrorCode`, 5 gateway interfaces + implementations, `OperationScope`, `NetworkSessionCoordinator`.
- `AZLobbyUI` button handlers → coordinator commands; `CheckForGameStart` removed (coordinator handles internally).

### Phase 4 — App Composition ✅

- `AppBootstrapper` on Managers GO — single init owner: `coordinator.InitializeAsync()`.
- `LobbyManager.Start()` and `Coordinator.Start()` no longer self-initialize.
- Coordinator `OnClientStopped` handler for external disconnect recovery.
- `LeaveAsync` loads LobbyScene when called from GameScene.
- `AZLobbyUI.WaitForManagers` waits for coordinator `SessionState.Ready`.
- `AZGameUI.OnBackToLobbyClicked` routes through `coordinator.LeaveAsync()`.
- App/session lifetime `CancellationTokenSource` deferred to Phase 5+.

### Phase 5 — Match Composition and Turn Separation

- Expand the Phase 1 MatchCompositionRoot with scene validation and server/client consumer composition; do not transfer registry ownership.
- Extract authoritative environment decisions separately from presentation staging.
- Migrate the NGO-coupled ItemContext/ExecuteEffect pipeline behind pure input/result seams category by category while preserving two-player golden results.
- Extract idempotent server-only round lifecycle logic.
- Stabilize semantic network publication seams before optionally moving RPCs to another NetworkBehaviour.
- Replace remaining `_p1`/`_p2` ownership with collection iteration while still resolving exactly two-player gameplay under `PLAN_018`.

### Phase 6 — Presentation and Pools

- Split large UI/VFX responsibilities.
- Add measured view/effect/emote pools using Unity ObjectPool first.
- Add typed resource catalogs.

### Phase 7 — asmdef and Tests

- Enforce dependency direction.
- Add pure EditMode domain tests and focused network PlayMode tests.

### Phase 8 — Optional Addressables

- Apply only to measured non-network resource needs.

## 21. Per-Phase Guardrails

- Preserve `.meta` GUIDs.
- Do not combine folder migration, asmdef introduction, and behavior changes in one phase.
- Do not rewrite all managers at once.
- Keep Host and Client testable after every phase.
- Preserve the PlayerPrefab-null NetworkManager configuration and PlayerSpawnManager ownership until a dedicated spawn architecture change is approved.
- Preserve ScriptableObject runtime immutability.
- Preserve a single authoritative turn state machine.
- Do not mix the behavior-preserving `PLAN_018` migration with four-player rules, balance, UI layout, or reconnect feature work.
- Old and new paths may coexist only as a read-only compatibility bridge. They must never both assign identity, initialize inventory, start a session, write authoritative state, emit the same network event, or drive the same phase.
- Record runtime validation that was not performed.

### Required migration invariants

1. PlayerIndex has exactly one server writer.
2. PlayerInventory has exactly one initialization path.
3. A user session request starts NGO at most once.
4. Exactly one authoritative phase driver writes TurnPhase.
5. ResultSequence increases monotonically for the Match lifetime.
6. A ClientId has at most one ready registry binding.
7. Presentation ACK is verified by sender ClientId and ResultSequence.
8. Exactly one MatchCompositionRoot exists per loaded GameScene on each peer.
9. Barrier pending clients are a subset of the `Begin`-time expected presentation participants and remain connected; disconnect removes them and later connections are not added.

### Lifecycle contract

| Scenario | MatchCompositionRoot | Registry | Player binding | Barrier |
|---|---|---|---|---|
| LobbyScene Host/Client start | absent | absent | absent | absent |
| GameScene loaded | created once on each peer | created empty | waiting for spawn | inactive |
| Player spawned | maintained | pending or ready registration | pending -> ready | inactive |
| Client disconnect | maintained | idempotent remove | invalidated | remove sender from pending |
| player eliminated in PLAN_019 | maintained | binding remains while spawned | valid but excluded from eligible actors | include only if presentation policy requires ACK |
| GameScene unload | destroyed | clear and dispose | all invalidated | terminate |
| GameScene re-entry | newly created | new empty instance | re-register | reset |
| second round in same scene | maintained | maintained | maintained | next ResultSequence only |

## 22. Target Definition of Done

The architecture migration is complete only when:

- Lobby UI calls one session facade.
- App readiness gates session commands.
- Lobby polling and heartbeat cannot overlap and cancel on session end.
- ClientId and PlayerIndex conversions go through PlayerRegistry.
- new match/player APIs expose collections and do not create new hardcoded `_p1`/`_p2`, `1 - index`, or ClientId-as-seat assumptions;
- Turn progression does not inspect Host-local presentation Singleton state.
- repeated scene/player discovery is removed from normal match flow;
- inventory views update incrementally where possible;
- high-churn presentation objects use verified reset-safe pools;
- Domain tests run without NGO or UI dependencies;
- migrated Domain item rules return typed outcomes and do not mutate PlayerState, NetworkVariables, or NetworkLists directly;
- scene re-entry leaves no stale static event subscriptions or async loops;
- replicated player fields are grouped only when authority, visibility, lifetime, and update cadence match; secret action intent remains server-only;
- at least two consecutive rounds pass on both Host and Client;
- disconnect during Lobby, Relay connection, scene load, Prep, and presentation exits safely.

Completion of this migration does not claim that four-player gameplay is implemented. That claim belongs only to `PLAN_019` and its Host plus three-remote-client gates.

## 23. Agent Decision Checklist

Before an architecture-affecting edit, answer:

1. Is this current behavior or target behavior?
2. Which lifetime owns the object or operation?
3. Is the state authoritative, replicated, one-shot network, or local derived state?
4. Which ID domain is being used?
5. Is an interface protecting a real boundary or merely adding indirection?
6. Should this operation use Task, Awaitable, Coroutine, synchronous code, or no asynchronous mechanism?
7. What cancels it?
8. What cleanup is required on disconnect, despawn, or scene exit?
9. Can the change be made without moving serialized assets?
10. What Host and Client validation proves behavior was preserved?
11. Does the new contract support a configured two-to-four-player roster without changing current two-player behavior?
12. Which design patterns were considered, and how do ownership, authority, lifetime, and testability determine the choice?
13. Was the API verified against the exact installed package source and official documentation, and was any newer compatible option evaluated without silently upgrading dependencies?

If these questions cannot be answered from current code and design documents, inspect further or ask the user before broadening the refactor.

## 24. Architecture Planning and Version Audit

Before writing an architecture-affecting implementation plan:

1. Inspect current executable code, serialized assets, `Packages/manifest.json`, `packages-lock.json`, and the relevant installed package API.
2. Compare at least two viable patterns when the choice materially changes ownership, authority, lifecycle, networking, or testability.
3. Record the selected pattern, rejected alternatives, class/responsibility mapping, data flow, authority model, migration seam, and validation gate.
4. Verify planned APIs against the exact installed version and official vendor documentation. Check release notes for a newer compatible non-legacy option.
5. Prefer a supported modern API for new code when it does not broaden the phase. Do not rewrite stable legacy call sites merely to appear current.
6. Never change a package version as an incidental planning or refactoring step. Package upgrades require explicit approval, a compatibility review, and a separate rollback/test plan.

For the installed NGO line, new RPC code should use universal `[Rpc(...)]`; legacy ClientRpc/ServerRpc call sites migrate only inside an approved phase. Use Unity `Awaitable` for Unity lifecycle/frame operations where it improves cancellation and composition, keep `Task` at UGS SDK boundaries, and retain existing Coroutines for VFX choreography until a focused owner-safe conversion is justified.
