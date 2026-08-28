# PLAN 020 — 1v1 UI Pipeline Refactoring

> Status: ✅ Complete — Phase 0~7 코드 완료, 테스트 미진행  
> Created: 2026-08-26 | Revised: 2026-08-26 (Codex review #4 sign-off — 9/10 APPROVED)  
> Prerequisites: PLAN_018 Phase 1~7 (complete)  
> Scope: **1v1 동작 유지 + UI 분해 + seat-keyed 구조 준비**  
> Out of scope: Core N-player 전환 (→ PLAN_019, GAME_DESIGN 12개 결정 후)

## 1. Goal

AZGameUI (1332줄 God Class) 분해 + 서버 상태 관찰 파이프라인 구축.
1v1 동작을 보존하면서, 향후 4인 확장 시 UI 코드 변경을 최소화할 수 있는 seat-keyed 구조로 전환한다.

### What This Plan Does vs Does NOT Do

| This plan (UI pipeline) | PLAN_019 (Core N-player + 4인 gameplay) |
|--------------------------|----------------------------------------|
| AZGameUI → seat-keyed presenters | P1/P2 fields → per-seat arrays |
| GameDataBridge (NV/event 총괄 관찰) | CombatResult/Engine/Resolver 시그니처 변경 |
| Temperature override → seat-keyed Dictionary | BuffSystem/EnvironmentRule N-player 전환 |
| 1v1 동작 100% 보존 | MatchManager seat-indexed scores |
| OpponentBarPresenter (1v1 단일) | OpponentBarPool 0..3 (PLAN_019) |
| PresentationBarrier 종료 상태 수정 | Target selection, death cascade, victory rules |

### Codex Review 반영 이력

<details>
<summary>Review #1 (8건)</summary>

1. 범위 축소: Core N-player → PLAN_019 복귀
2. asmdef 소유권: UI assembly GameUIRoot가 bridge 생성
3. RPC: `InvokePermission = RpcInvokePermission.Owner`
4. PresentationBarrier: BarrierState 종료 상태
5. 이벤트 배칭: level=LateUpdate, edge=즉시
6. Presenter 통합: MatchHudPresenter
7. Cross-presenter: GameUIManager 중재
8. 온도 주기: 1Hz
</details>

<details>
<summary>Review #2 (BLOCKER 4 + HIGH 2 + 문서 불일치)</summary>

1. BarrierState property 패턴
2. MatchSnapshot 추가
3. ILocalPlayerCommands 인터페이스
4. AZGameUI 32개 책임표
5. GameScene 수정 범위 포함
6. 초기 hydration 순서
7. CombatPresenter 제거, OpponentBarPool→단일, IsAlive 제거
</details>

**Review #3 (BLOCKER 2 + HIGH 4):**
1. InventoryPresenter Core assembly → bridge 미참조, TurnManager.Instance 유지
2. Phase 실행 순서 → GameScene 3단계 수정
3. ClientId-as-seat 범위 확대 (+TurnManager:614, +FanBladeSpinner:118)
4. LeaveMatchAsync Task 반환
5. RoundResult coalesced MatchSnapshot 렌더링
6. Safety Rule 8 wording 통일

**Review #4 Sign-Off (9/10 APPROVED — 2건 보완):**
1. **아이템 선택 후 로컬 상태 갱신**: `_presenter.NotifyItemConfirmed(slotIndex)` 호출이 Phase 5에서 누락됨. `ILocalPlayerCommands.SelectItem` 성공 시 GameUIManager가 `NotifyItemConfirmed` 호출하도록 계약 추가
2. **RoundResult 0.15초 안정화**: 기존 `Invoke(nameof(ShowRoundResult), 0.15f)` 보존. LateUpdate coalescing만으로는 다른 NetworkObject의 NV가 다음 네트워크 프레임에 도착하는 경우 불충분. `_roundResultPending`은 최소 0.15초 settle 후 최신 MatchSnapshot으로 발화
3. **구현 시 명시 사항 4건 추가**:
   - GameDataBridge.OnDestroy: Registry, seat NV, TurnManager/MatchManager NV, static events 전부 idempotent 해제
   - LocalPlayerCommandAdapter: PlayerState를 영구 캐시하지 않고 Registry rebind
   - GameUIRoot 실행 순서: `[DefaultExecutionOrder(-100)]` 사용 (인스턴스 속성 executionOrder 없음)
   - OnRoundResult + OnMatchEnd 동시 발생 시: OnRoundResult 먼저 → OnMatchEnd 후 발화, 최종 표시는 반드시 MATCH WIN/LOSE

## 2. Architecture

```
SERVER (Host)
  ├── PlayerState NVs × 2 seats    ← auto-replicate (per-player NetworkObject)
  ├── TurnManager NVs + static events
  ├── MatchManager NVs
  └── CombatVFXManager static events
         │
         ▼  (NV OnValueChanged + static events)
┌─────────────────────────────────────────────────────┐
│  GameDataBridge  (MonoBehaviour, UI assembly)        │
│                                                     │
│  Level state (dirty + LateUpdate flush):            │
│    • Per-seat SeatSnapshot (temp, fan, ready, etc.) │
│    • MatchSnapshot (phase, timer, score, round)     │
│                                                     │
│  Edge events (즉시 전달):                            │
│    • OnPhaseChanged, OnEnvironmentAnnounced          │
│    • OnTempOverride(seat, value), OnTempOverridesClear│
│    • OnOpponentRevealed                              │
│                                                     │
│  RoundResult 감지 (Review #3 HIGH):                  │
│    • CurrentPhase→RoundOver trigger                   │
│    • coalesced MatchSnapshot 기준 렌더링              │
│    • LastRoundWinner.OnValueChanged 단독 의존 금지     │
│                                                     │
│  Commands:                                           │
│    • ILocalPlayerCommands (별도 객체)                  │
└────────────────┬────────────────────────────────────┘
                 │
                 ▼
┌─────────────────────────────────────────────────────┐
│  GameUIManager  (MonoBehaviour, UI assembly)         │
│                                                     │
│  Creates/owns presenters, mediates cross-presenter:  │
│    • TemperaturePresenter (local + opponent)          │
│    • MatchHudPresenter (phase+timer+score+ready+env) │
│    • OpponentBarPresenter (single, 1v1)              │
│    • RoundResultPresenter (game over panel)           │
│    • FanSpawner (fan 생성 + tuning)                   │
│    • Audio integration (GameAudioManager)             │
│                                                     │
│  NOT managed (기존 코드 유지, 생성만 이전):             │
│    • InventoryPresenter (Core assembly — bridge 미참조)│
│    • MiniGameHub (생성만 이전)                         │
└─────────────────────────────────────────────────────┘
```

### 이벤트 배칭 전략

| 이벤트 유형 | 전달 방식 | 이유 |
|------------|----------|------|
| SeatSnapshot (temp, fan, ready 등) | dirty flag → LateUpdate 1회 flush | 같은 서버 틱에 여러 NV 변경 시 중간 상태 방지 |
| MatchSnapshot (phase, timer, score 등) | dirty flag → LateUpdate 1회 flush | 동일 이유 |
| PhaseChanged, Environment | 즉시 전달 (sequence 없음) | NV OnValueChanged 순서 보장됨 |
| TempOverride (VFX 연출 중) | 즉시 전달 | 프레임 정확 override 필요 |

### RoundResult 감지 전략 (Review #3 HIGH)

서로 다른 NetworkObject(TurnManager, MatchManager)의 NV callback 순서에 의존하면 안 됨.

```
CurrentPhase → RoundOver (edge event, 즉시)
  → Bridge sets _roundResultPending = true
  → LateUpdate에서 MatchSnapshot flush 시 함께 처리:
    → OnRoundResult(MatchSnapshot) 발화
    → _roundResultPending = false
```

이렇게 하면 MatchSnapshot의 LastRoundWinner, P1/P2RoundWins, MatchState가 모두 동일 프레임에 합쳐진 후 렌더링됨.

## 3. Design Pattern Selection

| Pattern | Where | Rationale |
|---------|-------|-----------|
| **Mediator** | GameDataBridge | NV/event 총괄 → presenter |
| **Mediator** | GameUIManager | cross-presenter 중재 (env→timer, commands) |
| **Observer** | Bridge events | loose coupling |
| **Strangler Fig** | AZGameUI 분해 | phase별 책임 이전, 매 phase 1v1 테스트 |
| **Command** | ILocalPlayerCommands | presenter→RPC 경로 분리 |

## 4. Data Contracts

### 4.1 SeatSnapshot
```csharp
public struct SeatSnapshot
{
    public byte SeatIndex;
    public ulong ClientId;
    public float Temperature;
    public float FanSpeed;
    public bool IsReady;
    public bool IsFanActive;
    public bool IsFanUpgraded;
    public bool IsBasicBlocked;
    public bool HasSelectedItem;
    public bool IsLocal;
}
```

### 4.2 MatchSnapshot
```csharp
public struct MatchSnapshot
{
    public TurnPhase CurrentPhase;
    public int TurnNumber;
    public int RemainingTime;
    public float PrepDuration;
    public EnvironmentType ActiveEnvironment;
    public int RoundNumber;
    public int P1RoundWins;    // 1v1 scope — PLAN_019에서 seat-indexed
    public int P2RoundWins;
    public MatchState MatchState;
    public int LastRoundWinner;
}
```

### 4.3 Bridge Interface
```csharp
public interface IGameDataBridge
{
    // Level state (LateUpdate flush)
    int SeatCount { get; }
    byte LocalSeatIndex { get; }
    bool TryGetSeat(byte seat, out SeatSnapshot snapshot);
    event Action<byte, SeatSnapshot> OnSeatSnapshotChanged;

    // Match state (LateUpdate flush)
    MatchSnapshot CurrentMatch { get; }
    event Action<MatchSnapshot> OnMatchSnapshotChanged;

    // Seat lifecycle
    event Action<byte, SeatSnapshot> OnSeatRegistered;
    event Action<byte> OnSeatUnregistered;

    // Edge events (immediate, no sequence)
    event Action<TurnPhase, TurnPhase> OnPhaseChanged;
    event Action<EnvironmentType> OnEnvironmentAnnounced;
    event Action<byte, float> OnTempOverride;
    event Action OnTempOverridesClear;
    event Action<bool> OnLocalHasSelectedItemChanged;
    event Action<byte, short> OnOpponentRevealed;

    // Round/Match result (LateUpdate coalesced — NOT from NV change directly)
    event Action<MatchSnapshot> OnRoundResult;   // CurrentPhase→RoundOver trigger + coalesced snapshot
    event Action<MatchSnapshot> OnMatchEnd;      // MatchState→MatchComplete trigger + coalesced snapshot
}

// Commands — 별도 객체 (Review #3 권고 수용)
public interface ILocalPlayerCommands
{
    bool TrySelectItem(byte slotIndex);  // Review #4: 성공 시 true → GameUIManager가 NotifyItemConfirmed 호출
    void PressReady();
    Task LeaveMatchAsync();   // Review #3: 비동기 — NetworkSessionCoordinator.LeaveAsync()
}
```

### 4.4 Command 구현 분리
```
GameDataBridge (IGameDataBridge) — read model only
LocalPlayerCommandAdapter (ILocalPlayerCommands) — 별도 객체, local PlayerState RPC 호출
  → GameUIManager가 두 객체를 모두 보유
  → Presenters는 GameUIManager 경유로만 command 접근

TrySelectItem 계약 (Review #4):
  1. pre-validation: IsReady, MiniGameHub.IsRunning, HasSelectedItem → false 반환
  2. PlayerState.SelectItemServerRpc(slotIndex) 호출
  3. return true
  → GameUIManager는 true 시: _inventoryPresenter.NotifyItemConfirmed(slotIndex) + audio + status text

LeaveMatchAsync 계약 (Review #3):
  → async Task: coordinator.LeaveAsync() with try/catch
  → 호출부 (RoundResultPresenter): async void handler + button.interactable=false

LocalPlayerCommandAdapter rebind (Review #4):
  → PlayerState를 영구 캐시하지 않음
  → Registry.Registered/Unregistered에 따라 재바인딩
  → 명령 호출 시 PlayerState==null이면 무시 + warning log
```

## 5. AZGameUI Responsibility Matrix (32 items)

| # | AZGameUI 책임 | Line | 새 Owner | Phase |
|---|-------------|------|---------|-------|
| 1 | EnsureEventSystem | 104 | GameUIRoot | 1 |
| 2-4 | BuildOverlayUI / OppBar / Ready | 105-107 | GameHudBuilder → GameHudRefs | 2 |
| 5 | InventoryPresenter 생성 + OnWorldItemClicked | 110-115 | GameUIManager (생성만 이전, 내부 수정 없음) | 5 |
| 6 | MiniGameHub 생성 + OnFinishedLocal | 117-119 | GameUIManager (생성만 이전) | 5 |
| 7-8 | SpawnStayItemFans + fan tuning (9 fields) | 121, 125-238 | FanSpawner (별도 MonoBehaviour) | 5 |
| 9 | EnsureAudioManager + PlayBGM | 241-250 | GameUIRoot | 1 |
| 10 | Update polling (TM/MM/Player find) | 252-275 | GameDataBridge (Registry 기반, polling 제거) | 1 |
| 11 | SubscribeToEvents (NV + static events) | 352-362 | GameDataBridge | 1 |
| 12 | OnPhaseChanged — phase text, ready btn, status, audio | 364-408 | MatchHudPresenter + audio via GameUIManager | 4 |
| 13 | OnWinnerChanged → ShowRoundResult | 410-448 | RoundResultPresenter (bridge OnRoundResult) | 4 |
| 14 | OnHasSelectedItemChanged — status text | 416-419 | MatchHudPresenter | 4 |
| 15-16 | UpdateTimerDisplay + SetAlarmActive + AlarmShake | 451-512 | MatchHudPresenter (MatchSnapshot) | 4 |
| 17-20 | UpdateTempDisplay + overrides + SnapTemp + GetTempColor | 515-593 | TemperaturePresenter | 3 |
| 21 | UpdateScoreDisplay | 595-599 | MatchHudPresenter (MatchSnapshot) | 4 |
| 22 | UpdateOppBarTransform — billboard + attach | 601-633 | OpponentBarPresenter | 5 |
| 23 | OnMiniGameFinished — status text | 635-638 | GameUIManager → MatchHudPresenter.status | 5 |
| 24 | OnItemClicked — SelectItemServerRpc + audio | 641-658 | GameUIManager → ILocalPlayerCommands.SelectItem() | 5 |
| 25 | OnReadyClicked — PressReadyServerRpc + button + audio | 661-681 | MatchHudPresenter(btn) + ILocalPlayerCommands.PressReady() | 4 |
| 26 | OnBackToLobbyClicked — LeaveAsync | 683-702 | RoundResultPresenter + ILocalPlayerCommands.LeaveMatchAsync() | 4 |
| 27 | OnEnvironmentAnnounced — panel + SummerVac shake + audio | 1148-1178 | MatchHudPresenter + audio via GameUIManager | 4 |
| 28 | OnOpponentRevealed — Tarot status text | 1216-1229 | MatchHudPresenter | 4 |
| 29 | OnDestroy — unsubscribe + audio stop | 277-305 | 각 presenter OnDestroy + GameUIRoot | 6 |
| 30 | Build helpers (CreateText/Panel/Button, Anchor*) | 1231-1328 | GameHudBuilder (static helpers) | 2 |
| 31 | Instance singleton | 97-100 | 제거 — GameUIRoot 대체 | 6 |
| 32 | FindLocalPlayer / FindAllPlayers / GetOpponent | 315-350 | GameDataBridge (Registry 기반) | 1 |

## 6. File Structure

```
Assets/Scripts/UI/Game/
├── GameUIRoot.cs                   ~100  composition root + EventSystem + Audio
├── Bridge/
│   ├── IGameDataBridge.cs          ~70   read model interface
│   ├── ILocalPlayerCommands.cs     ~15   command interface
│   ├── GameDataBridge.cs           ~300  NV aggregation + batching
│   ├── LocalPlayerCommandAdapter.cs ~50  PlayerState RPC routing
│   ├── SeatSnapshot.cs             ~25   per-seat struct
│   └── MatchSnapshot.cs            ~25   match-level struct
├── Build/
│   ├── GameHudBuilder.cs           ~480  runtime UGUI construction
│   └── GameHudRefs.cs              ~100  all UI references
├── Presenters/
│   ├── TemperaturePresenter.cs     ~180  local+opponent temp bars, override, color
│   ├── MatchHudPresenter.cs        ~250  phase+timer+alarm+score+ready+status+env+Tarot
│   ├── RoundResultPresenter.cs     ~80   game over panel + back-to-lobby (async)
│   └── OpponentBarPresenter.cs     ~80   single opponent bar (billboard)
├── FanSpawner.cs                   ~120  StayItem fan + 9 serialized fields
├── AZGameUI.cs                     (SHRINKS → DELETED)
└── GameUIManager.cs                ~180  owns presenters, mediates, routes
```

**~14 new files**. InventoryPresenter(694줄) — 수정 안 함 (Core assembly, TurnManager.Instance 유지). CombatVFXManager(865줄) — Phase 0B/0C 안전성 수정만.

## 7. Scope Declaration

### Will Modify
- `Assets/Scripts/UI/Game/AZGameUI.cs` — shrink per phase → delete (Phase 1~6)
- `Assets/Scripts/Core/Player/PlayerState.cs` — RPC InvokePermission (Phase 0A)
- `Assets/Scripts/Core/Combat/CombatVFXManager.cs` — ACK safety + ClientId fix (Phase 0B, 0C)
- `Assets/Scripts/Core/Combat/PresentationBarrier.cs` — BarrierState (Phase 0E)
- `Assets/Scripts/Core/Turn/RoundLifecycleService.cs` — DetermineDeathWinner 반환 타입 (Phase 0D)
- `Assets/Scripts/Core/Turn/TurnManager.cs` — BarrierState caller + DetermineDeathWinner caller + ClientId==0 제거 (Phase 0C, 0D, 0E)
- `Assets/Scripts/Core/Common/FanBladeSpinner.cs` — OwnerClientId==playerIndex fix (Phase 0C)
- `Assets/Scenes/GameScene.unity` — Phase 1: GameUIRoot 추가 (AZGameUI 공존), Phase 5: FanSpawner 추가, Phase 6: AZGameUI 제거

### Will NOT Modify (명시적)
- **InventoryPresenter.cs** (694줄) — Core assembly, bridge 참조 불가 (asmdef 규칙). TurnManager.Instance 유지. GameUIManager가 생성+이벤트 연결만 담당
- **CombatVFXManager.cs 전면 재작성** — Phase 0B/0C 안전성 수정만
- **Core combat** (CombatResult, CombatEngine, CombatResolver) — PLAN_019
- **Core support** (BuffDebuffSystem, EnvironmentRuleService) — PLAN_019
- **MatchManager** (P1/P2RoundWins NV) — bridge가 읽기만
- Network layer, Player prefab

### Will Create
- `Assets/Scripts/UI/Game/GameUIRoot.cs`
- `Assets/Scripts/UI/Game/GameUIManager.cs`
- `Assets/Scripts/UI/Game/FanSpawner.cs`
- All files under `Bridge/`, `Build/`, `Presenters/`

## 8. Implementation Phases

---

### Phase 0: Critical Fixes (prerequisite)

#### Phase 0A: RPC Security
- [x] 6개 PlayerState `[Rpc(SendTo.Server)]`에 `InvokePermission = RpcInvokePermission.Owner` 추가
- [x] `RpcParams rpcParams` 추가 + `rpcParams.Receive.SenderClientId != OwnerClientId` 방어
- [x] Files: `PlayerState.cs`
- [ ] Test: Host/Client 1v1 — item selection, ready, emote

#### Phase 0B: VFX ACK Exception Safety
- [x] `OnTempTargetsOverride?.Invoke()` (line 100)를 try 블록 안으로
- [x] `CompletePresentationSequence` — 각 cleanup을 독립 try/catch로 격리
- [x] ACK를 finally에서 전송 보장
- [x] Files: `CombatVFXManager.cs`
- [ ] Test: Host/Client 1v1 — combat VFX + ACK

#### Phase 0C: ClientId-as-Seat Removal (범위 확대 — Review #3)
- [x] `CombatVFXManager:328` `(int)nm.LocalClientId` → `isLocalUser` (이미 line 193에서 계산)
- [x] `TurnManager:614` `LocalClientId == 0` → Registry-based isLocalP1 판정
- [x] `FanBladeSpinner:118` `OwnerClientId == (ulong)playerIndex` → `PlayerState.PlayerIndex` seat 비교
- [x] **Grep 패턴 확대**: 전체 검색 완료 — 추가 패턴 0건
- [x] Files: `CombatVFXManager.cs`, `TurnManager.cs`, `FanBladeSpinner.cs`
- [ ] Test: Host/Client 1v1 — VFX positions, ambulance staging, fan binding

#### Phase 0D: DetermineDeathWinner Contract Fix
- [x] return `int?` — null=nobody died, -1=draw, 0+=winner seat
- [x] Update TurnManager caller
- [x] Files: `RoundLifecycleService.cs`, `TurnManager.cs`
- [ ] Test: Host/Client 1v1 — round winner detection

#### Phase 0E: PresentationBarrier Termination States
- [x] Add `BarrierState { Idle, Waiting, Completed, Canceled, TimedOut }` enum
- [x] Add `BarrierState State { get; }` property
- [x] `Begin()`: if expected set empty → immediate `State = Completed` (Review #3 권고)
- [x] `WaitForCompletion(float timeout)` remains IEnumerator:
  - while loop checks `State == Waiting && elapsed < timeout`
  - All ACKs → `State = Completed`
  - Timeout → `State = TimedOut`
- [x] `Cancel()`: only transitions if `State == Waiting` → `State = Canceled`
- [x] TurnManager reads `_barrier.State` after `yield return WaitForCompletion()`
- [x] Files: `PresentationBarrier.cs`, `TurnManager.cs`
- [ ] Test scenarios:
  - Normal completion (all ACKs received)
  - Timeout (1+ client never responds)
  - Cancel (called mid-wait)
  - Empty pending set (immediate completion)
  - Duplicate ACK (ignored)
  - Late ACK after completion (ignored)
  - Sequence mismatch (ignored)
  - Last client disconnect during wait → HandleDisconnect → auto-complete

---

### Phase 1: GameUIRoot + GameDataBridge (GameScene 1차 수정) ✅

- [x] **GameScene.unity 1차 수정 (MCP)**:
  - [x] GameUI 오브젝트에 `GameUIRoot` component 추가 (AZGameUI와 공존)
  - [x] GameUIRoot.executionOrder를 AZGameUI보다 앞으로 (`[DefaultExecutionOrder(-100)]`)
- [x] Create `GameUIRoot.cs` (UI assembly):
  - [x] `[DefaultExecutionOrder(-100)]` MonoBehaviour on GameScene "GameUI" object (Review #4: 인스턴스 executionOrder 없음)
  - [x] **초기화**: Coroutine으로 `MatchCompositionRoot.Instance` 대기 + `TurnManager.Instance` NetworkSpawn 대기
  - [x] `EnsureEventSystem()` (from AZGameUI #1)
  - [x] `EnsureAudioManager()` (from AZGameUI #9)
  - [x] Creates `GameDataBridge`, `LocalPlayerCommandAdapter`
  - [x] (Phase 3에서 GameUIManager 생성 추가)
- [x] AZGameUI에서 #1, #9 (EventSystem, AudioManager) 제거 — GameUIRoot가 대체
- [x] Create `IGameDataBridge.cs`, `ILocalPlayerCommands.cs`
- [x] Create `SeatSnapshot.cs`, `MatchSnapshot.cs`
- [x] Create `LocalPlayerCommandAdapter.cs`:
  - [x] Finds local PlayerState via Registry + NetworkManager.LocalClientId
  - [x] `SelectItem` → pre-validation (IsReady, MiniGameHub.IsRunning, HasSelectedItem) → PlayerState.SelectItemServerRpc
  - [x] `PressReady` → PlayerState.PressReadyServerRpc
  - [x] `LeaveMatchAsync` → `async Task` → NetworkSessionCoordinator.LeaveAsync() with try/catch
- [x] Create `GameDataBridge.cs`:
  - [x] **초기 hydration 순서** (subscribe → enumerate → bind):
    1. Subscribe to PlayerRegistry.Registered/Unregistered
    2. Enumerate existing `Registry.Players` → `BindSeat()` for each (idempotent)
    3. Wait for TurnManager.Instance to be spawned → subscribe NV changes
    4. Wait for MatchManager → subscribe NV changes
    5. Read current NV values → initialize MatchSnapshot
  - [x] Per-seat NV subs: Temperature, FanSpeed, IsReady, IsFanActive, IsFanUpgraded, IsBasicBlocked, HasSelectedItem
  - [x] Match-level NV subs: CurrentPhase, TurnNumber, RemainingTime, PrepDuration, ActiveEnvironment, RoundNumber, P1/P2RoundWins, MatchState, LastRoundWinner
  - [x] Static event subs: TurnManager.OnEnvironmentAnnounced, TurnManager.OnOpponentRevealed, CombatVFXManager.OnTempOverridesClear/OnTempTargetsOverride/OnPlayerTempOverride
  - [x] Level state: dirty flag per seat + match → LateUpdate single flush
  - [x] Edge events: immediate (OnPhaseChanged, OnEnvironmentAnnounced, OnTempOverride, OnTempOverridesClear, OnOpponentRevealed, OnLocalHasSelectedItemChanged)
  - [x] **RoundResult 감지** (Review #4: 0.15초 settle 보존):
    - CurrentPhase→RoundOver → `_roundResultPending=true`, `_roundResultTimer=0`
    - LateUpdate에서 `_roundResultTimer += Time.unscaledDeltaTime`
    - 0.15초 경과 후 최신 MatchSnapshot으로 `OnRoundResult(snapshot)` 발화
    - 이유: 다른 NetworkObject(MatchManager)의 NV가 다음 네트워크 프레임에 도착할 수 있음
  - [x] **MatchEnd 감지**: MatchState→MatchComplete → `_matchEndPending=true` → 동일 settle 후 `OnMatchEnd(snapshot)` 발화
  - [x] **OnRoundResult + OnMatchEnd 동시 발생 시** (Review #4): OnRoundResult 먼저 → OnMatchEnd 후. 최종 표시는 MATCH WIN/LOSE
  - [x] **Reconnect**: Unregistered(seat) → cleanup NV subs → Registered(seat) → idempotent BindSeat → rebind
  - [x] **LocalSeatIndex 결정**: NetworkManager.LocalClientId → Registry lookup → SeatIndex
  - [x] **OnDestroy** (Review #4): Registry Registered/Unregistered, 모든 seat NV OnValueChanged, TurnManager/MatchManager NV, 모든 static events를 idempotent 해제
- [ ] Test: AZGameUI와 GameUIRoot 공존 상태에서 Host/Client 1v1 — bridge debug log로 모든 event/snapshot 확인

### Phase 2: GameHudBuilder + GameHudRefs

- [x] Extract `Build*()` from AZGameUI → `GameHudBuilder.cs`:
  - `BuildOverlayUI()`, `BuildOppBarWorldUI()`, `BuildReadyWorldUI()`
  - `BuildMyHpBar()`, `BuildClockTimer()`, `BuildEnvironmentPanel()`, `BuildGiftLines()`, `BuildOppDividerLines()`
  - Static helpers: `CreateText`, `CreatePanel`, `CreateButton`, `Anchor*`
- [x] Create `GameHudRefs.cs`:
  - Overlay: phaseText, timerText, statusText, scoreText, myTempText, myHpSlider, myHpFillImage, timerFillImage, timerSliderImage, clockHandRT, timeAlarmObj, timeAlarmImage, timerContainerRT, envPanel, envText
  - GameOver: gameOverPanel, gameOverText, lobbyButton
  - OppBar: oppBarCanvas, oppTempText, oppHpSlider, oppHpFillImage
  - Ready: readyCanvas, readyButton, readyButtonImage
- [x] AZGameUI.Start() calls `GameHudBuilder.Build()` → receives `GameHudRefs`
- [x] No behavior change — purely structural extraction
- [ ] Test: Host/Client 1v1 — UI identical

### Phase 3: GameUIManager + TemperaturePresenter

- [x] Create `GameUIManager.cs`:
  - [x] GameUIRoot가 Phase 1 bridge 생성 후 GameUIManager 생성 (Phase 3에서 코드 추가)
  - [x] Receives `IGameDataBridge` + `ILocalPlayerCommands` + `GameHudRefs`
  - [x] Creates and owns presenters (이 Phase에서는 TemperaturePresenter만)
  - [x] Mediates cross-presenter: env→timer, audio triggers
  - [x] Holds `ILocalPlayerCommands` — presenter command callback routing
- [x] Create `TemperaturePresenter.cs`:
  - [x] `OnSeatSnapshotChanged` → update temp bars (my + opponent)
  - [x] `OnTempOverride(seat, value)` / `OnTempOverridesClear` → seat-keyed override
  - [x] `Dictionary<byte, float?>` _tempOverrides
  - [x] `SnapTempDisplay()` — bridge current snapshot으로 초기화
  - [x] Temp→color mapping (`GetTempColor`), lerp, slider, text update
- [x] Move AZGameUI #17-20 → TemperaturePresenter. AZGameUI에서 해당 코드 제거
- [ ] Test: Host/Client 1v1 — temp bars, VFX overrides, colors

### Phase 4: MatchHudPresenter + RoundResultPresenter

- [x] Create `MatchHudPresenter.cs`:
  - [x] **Phase**: bridge `OnPhaseChanged` → phase text
  - [x] **Timer**: bridge `OnMatchSnapshotChanged` → RemainingTime/PrepDuration → text, fill, clock hand
  - [x] **Alarm**: remaining ≤ 5 → shake routine + audio trigger
  - [x] **Score**: bridge `OnMatchSnapshotChanged` → P1/P2RoundWins → text
  - [x] **Ready button**: OnPhaseChanged (PrepPhase=show) + OnLocalHasSelectedItemChanged
  - [x] Ready click → GameUIManager → `ILocalPlayerCommands.PressReady()` + button visual
  - [x] **Status text**: HasSelectedItem, Tarot reveal, mini-game result
  - [x] **Environment**: bridge `OnEnvironmentAnnounced` → env panel + SummerVac shake + audio
  - [x] SummerVacation timer effect: GameUIManager 중재
- [x] Create `RoundResultPresenter.cs`:
  - [x] bridge `OnRoundResult(MatchSnapshot)` → game over panel show
  - [x] Win/Lose/Draw text (MatchSnapshot.LastRoundWinner + LocalSeatIndex 비교)
  - [x] Match complete detection (MatchSnapshot.MatchState)
  - [x] Back-to-lobby button → `async void` handler → `ILocalPlayerCommands.LeaveMatchAsync()`:
    - 중복 클릭 방어 (button.interactable = false)
    - try/catch + error logging
  - [x] bridge `OnMatchEnd(MatchSnapshot)` → MATCH WIN/LOSE display
- [x] Move AZGameUI #12-16, #21, #25-28, #13, #26 → presenters. AZGameUI에서 해당 코드 제거
- [ ] Test: Host/Client 1v1 — phase, timer, alarm, score, ready, environment, round result, match end, back-to-lobby

### Phase 5: Inventory + OpponentBar + Fan + MiniGame (GameScene 2차 수정)

- [x] Create `OpponentBarPresenter.cs`:
  - [x] Single opponent bar (1v1 — PLAN_019에서 0..3 pool)
  - [x] PlayerIndex 기반 opponent 탐색 → attach
  - [x] Billboard + world camera tracking
  - [x] Temp data는 TemperaturePresenter가 계속 담당
- [x] Create `FanSpawner.cs`:
  - [x] MonoBehaviour with 9 `[SerializeField]` fan tuning fields
  - [x] `SpawnStayItemFans()` + `SpawnFanAt()`
  - [x] `OnValidate()` + `ReapplyFanTuning()` for Editor live tuning
- [ ] **GameScene.unity 2차 수정 (MCP)**:
  - [ ] GameUI 오브젝트에 `FanSpawner` component 추가
  - [ ] Fan tuning 값 설정
- [x] **InventoryPresenter — 수정 안 함** (Core assembly, asmdef 규칙):
  - [x] GameUIManager가 `new GameObject("InventoryPresenter").AddComponent<InventoryPresenter>()` 생성
  - [x] `OnWorldItemClicked` → GameUIManager → `ILocalPlayerCommands.TrySelectItem(slotIndex)`:
    - true → `_inventoryPresenter.NotifyItemConfirmed(slotIndex)` + audio + statusText (Review #4)
    - false → 무시 (pre-validation 실패)
  - [x] InventoryPresenter 내부의 TurnManager.Instance 참조는 유지
- [x] **MiniGameHub**:
  - [x] GameUIManager가 `new GameObject("MiniGameHub").AddComponent<MiniGameHub>()` 생성
  - [x] OnFinishedLocal → GameUIManager → MatchHudPresenter.SetStatus()
- [x] Move AZGameUI #5-8, #22-24 → presenters/components. AZGameUI에서 해당 코드 제거 (AZGameUI → 빈 stub)
- [ ] Test: Host/Client 1v1 — fans, item selection, opponent bar, mini-game status

### Phase 6: AZGameUI Elimination (GameScene 3차 수정)

- [x] **Responsibility matrix 최종 확인** — 모든 #1-32 항목에 owner 배정됨
- [x] AZGameUI.cs 삭제 완료
- [x] **GameScene.unity 3차 수정 (MCP)**:
  - [x] AZGameUI component 이미 부재 (런타임 생성이었음)
  - [x] FanSpawner component 추가 (Phase 5 scene task 합침)
- [x] AZGameUI.Instance 참조 grep → 코드 내 참조 0건 확인
- [ ] Test: Host/Client 1v1 — full match flow without AZGameUI

### Phase 7: Cleanup + Documentation

- [x] Remove dead code, unused static events — AZGameUI 삭제, MatchScoreView는 Core 소속으로 유지
- [x] Verify asmdef: Core→UI 단방향 — Core 내 AbsoluteZero.UI 참조 0건 확인
- [x] Update `Docs/RECENT_CHANGES.md`
- [x] Update `Docs/ACTIVE_CONTEXT.md`
- [x] Update `Docs/CHANGES.md`

---

## 9. Migration Safety Rules

1. **Each phase must pass Host/Client 1v1 test**
2. **AZGameUI shrinks monotonically** — code moves OUT, never IN
3. **CombatResultData (1v1 DTO) 유지** — N-player DTO는 PLAN_019
4. **Presenters are pure UI** — no NV write, no RPC send. Commands through ILocalPlayerCommands via GameUIManager
5. **Seat index, not ClientId** — presenter addressing uses byte seatIndex from bridge
6. **GameUIManager가 cross-presenter 중재** — presenter 간 직접 참조 금지
7. **Level state = LateUpdate flush, Edge event = immediate** — 혼용 금지
8. **초기 hydration 순서**: subscribe Registry events → enumerate existing Players → idempotent BindSeat → wait for TurnManager/MatchManager spawn → subscribe NVs → read current values
9. **Scene 수정은 단계적**: Phase 1 (GameUIRoot 추가) → Phase 5 (FanSpawner 추가) → Phase 6 (AZGameUI 제거)
10. **Core assembly 코드는 UI bridge를 참조하지 않음** — InventoryPresenter는 TurnManager.Instance 유지, bridge 미참조
11. **RoundResult는 0.15초 settle 후 MatchSnapshot에서 렌더링** — LastRoundWinner.OnValueChanged 단독 의존 금지, 다른 NetworkObject NV 도착 대기
12. **LeaveMatchAsync는 Task 반환** — 호출부에서 async void + try/catch + 중복 클릭 방어
13. **TrySelectItem 성공 시 NotifyItemConfirmed 필수** — InventoryPresenter의 slot identity 추적 보존
14. **GameDataBridge.OnDestroy에서 모든 구독 해제** — Registry, seat NV, manager NV, static events 전부 idempotent
15. **LocalPlayerCommandAdapter는 PlayerState를 영구 캐시 금지** — Registry rebind 또는 명령 시 재검색
16. **OnRoundResult + OnMatchEnd 동시 발생**: OnRoundResult 먼저, OnMatchEnd 후. 최종 UI는 MATCH WIN/LOSE

## 10. Discovered Issues

_(populated during implementation)_

## 11. Estimated Effort

| Phase | Scope | Effort | Risk |
|-------|-------|--------|------|
| 0A-0E | Critical fixes (5건, 0C 범위 확대) | 1-1.5 days | Low-Medium |
| 1 | GameUIRoot + Bridge + Command + hydration + scene 1차 | 1.5 days | Medium |
| 2 | HudBuilder + HudRefs extraction | 0.5 day | Low |
| 3 | UIManager + TemperaturePresenter | 1-1.5 days | High (override pipeline) |
| 4 | MatchHudPresenter + RoundResultPresenter | 1 day | Medium (timer + alarm + coalesced result) |
| 5 | Inventory + OpponentBar + Fan + MiniGame + scene 2차 | 1 day | Low-Medium |
| 6 | AZGameUI elimination + scene 3차 | 0.5 day | Medium |
| 7 | Cleanup + docs | 0.5 day | Low |
| **Total** | | **~7-8 days** | |

## 12. After This Plan → PLAN_019

PLAN_020 완료 후, GAME_DESIGN 12개 결정이 확정되면 PLAN_019에서:
- Core data structures N-player (CombatResult, Engine, Resolver → PlayerState[])
- BuffSystem, EnvironmentRule, RoundLifecycle → N-player
- MatchManager seat-indexed scores (P1/P2RoundWins → int[])
- MatchSnapshot.P1/P2RoundWins → seat-indexed array
- TurnManager loop-based phase transitions
- InventoryPresenter: ITurnPhaseSource 주입 또는 Core 인터페이스 도입 (bridge 대체)
- Target selection (PlayerMask, per-item TargetPolicy)
- Death cascade, victory rules
- Stable roster (PlayerSeat + MatchEpoch)
- EnemyPlayer → PlayerVisualRegistry (seat→visual mapping)
- Camera view-role contract
- CombatResultBatchData (N-player DTO)
- OpponentBarPresenter → OpponentBarPool (0..3)
- SeatSnapshot.IsAlive 필드 추가

UI presenters는 이미 seat-keyed이므로 PLAN_019에서 UI 변경 최소화.
