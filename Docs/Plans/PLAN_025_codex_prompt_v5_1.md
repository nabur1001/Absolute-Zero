# PLAN_025 Codex Review Prompt v5.1

> **Instructions:** Copy the entire content below (from `---START---` to `---END---`) and paste into Codex for review.

---START---

## Role

You are a senior Unity multiplayer architect reviewing an implementation plan for a 3~4 player deathmatch mode in an existing Unity 6 (6000.3.11f1) + NGO 2.11.2 (Netcode for GameObjects) host-authoritative game.

## Project Context

- **Engine:** Unity 6 (6000.3.11f1), NGO 2.11.2, Unity Relay (DTLS), host-authoritative
- **Current state:** Working 1v1 (Best-of-3) turn-based temperature deathmatch
- **Goal:** Add Multi Mode (3~4 player, 5-kill win), Solo/Bot, Ghost System
- **Namespace:** `AbsoluteZero`
- **Scenes:** LobbyScene(0), GameScene(1, 1v1), GameScene_Multi(2, 3~4p), GameScene_Solo(3)
- **Architecture:** Singleton managers, MatchCompositionRoot (MonoBehaviour, NOT NetworkBehaviour), PlayerState (NetworkBehaviour on spawned prefab), TurnManager (NetworkBehaviour)
- **Key constraint:** 1v1 existing code (Resolve(), CombatResultData) must NOT be modified

## Verified Code References (from actual codebase)

### MatchCompositionRoot — MonoBehaviour (cannot own NetworkVariables)
```csharp
// Assets/Scripts/Core/Match/MatchCompositionRoot.cs line 7
public class MatchCompositionRoot : MonoBehaviour  // NOT NetworkBehaviour!
```

### PlayerSpawnManager — Current disconnect handling (DESTROYS PlayerState)
```csharp
// Assets/Scripts/Core/Network/PlayerSpawnManager.cs line 223
private void DespawnPlayerForClient(ulong clientId)
{
    if (spawnedPlayers.TryGetValue(clientId, out NetworkObject networkObject))
    {
        if (networkObject != null && networkObject.IsSpawned)
        {
            networkObject.Despawn();
            Destroy(networkObject.gameObject);  // ← NV state LOST
        }
        spawnedPlayers.Remove(clientId);
    }
}
```

### Player.prefab — DontDestroyWithOwner = false (0)
```
// Assets/Prefabs/Player.prefab NetworkObject component
// DontDestroyWithOwner: 0  ← NGO may auto-cleanup on owner disconnect
```

### PlayerInventory — ConsumeItem unlimited check
```csharp
// Assets/Scripts/Core/Player/PlayerInventory.cs line 52
if (slot.IsUnlimited) return;  // ← Multi windbreaker must be IsUnlimited=false
```

### ItemEffectApplicator — NV writes that must appear in resolution delta
```csharp
// Assets/Scripts/Core/Item/ItemEffectApplicator.cs
if (outcome.BlockTargetBasics)
    ctx.Target.IsBasicBlocked.Value = true;
if (outcome.WriteUserFanSpeed)
    ctx.User.FanSpeed.Value = outcome.UserFanSpeedValue;
if (outcome.WriteTargetFanSpeed)
    ctx.Target.FanSpeed.Value = outcome.TargetFanSpeedValue;
```

### ItemEffect computation is RUNTIME-DEPENDENT
```csharp
// AttackItemDataSO.cs — Equalize needs user+target temperatures at resolution time
// RecoveryItemDataSO.cs — heal scales with remaining uses
// SpecialItemDataSO.cs — Tarot reveal checks target Ready state
```

### ScheduledEffect — NO SourceSeat field currently
```csharp
struct ScheduledEffect
{
    public int TargetPlayerIndex;
    // NO SourceSeat field!
    public EffectType Type;
    public float Value;
    public int TurnsRemaining;
}
```

### TemperatureSystem — No multiplier support
```csharp
public void ApplyFanTick(PlayerState player)
{
    float newTemp = Mathf.Max(MIN_TEMP, before - player.FanSpeed.Value);  // no multiplier
}
```

### UnityTransport — GetLocalEndpoint confirmed at line 394
```csharp
public NetworkEndpoint GetLocalEndpoint()
// Usage: ((UnityTransport)NetworkManager.Singleton.NetworkConfig.NetworkTransport).GetLocalEndpoint().Port
```

### Unity Lobby — PlayerData visibility & permissions
```
- Each player can ONLY modify their own PlayerData
- Host CANNOT write other players' PlayerData
- Visibility levels: Public(0), Member(1), Private(2)
- Private: visible to owner + lobby host only
- Host CAN read Private data of all members
```

### GAME_DESIGN.md — ChillAura duration
```
| Chill Aura | fan decrease ×2, recovery ×0.5 | 1 turn (until next PrepPhase end) |
```

### NGO NetworkList<T> constraint
```csharp
// T must be: unmanaged, IEquatable<T>, INetworkSerializable
// NetworkList<T>.cs in com.unity.netcode.gameobjects
```

## Previous Reviews Summary

| Review | Score | Fixed |
|--------|-------|-------|
| v1 (#1) | 4.0/10 | 11 blockers → all fixed in v2 |
| v2 (#2) | 6.0/10 | 5 blockers → all fixed in v3 |
| v3 (#3) | 6.0/10 | 8B+6P → all fixed in v4 |
| v4 (#4) | 6.0/10 | 7B+6P → all fixed in v5 |
| v5 (#5) | 6.0/10 | 5B+3P → all fixed in v5.1 (this version) |

### Review #5 Fixes Applied in v5.1

| # | Issue | v5.1 Fix |
|---|-------|----------|
| B1 | DontDestroyWithOwner=false + ChangeOwnership doesn't re-register PlayerObject | **SeatRuntimeState approach**: On disconnect, server extracts all NV state → struct. Despawn+Destroy OK. On reconnect, new SpawnAsPlayerObject(newClientId) + hydrate from struct. PlayerObject lookup works naturally. |
| B2 | Host can't write other players' Lobby PlayerData | **Client self-write**: Each client generates token → writes to own Private PlayerData → Host reads Private (permitted) → stores in SessionParticipantTable |
| B3 | ItemRuleSnapshot.From() can't capture runtime-dependent effects (Equalize, ScalesWithUses, Tarot) | **ItemEffectRuleSnapshot**: Captures calculation RULES, not results. ItemEffectKind discriminator. Resolver computes actual effects from rules + MatchCombatSnapshot runtime state. |
| B4 | DTO reader starts with null arrays (struct default) | Reader-side `??= new` initialization for all arrays + SeatCount≤4, EventCount≤16 validation on both reader and writer |
| B5 | GhostCooldownNetData missing unmanaged+IEquatable contract | Full struct: `INetworkSerializable, IEquatable<T>`, manual NetworkSerialize, Equals+GetHashCode. Full lifecycle in MatchNetworkState: Awake init, OnNetworkSpawn subscribe, OnNetworkDespawn unsubscribe, OnDestroy Dispose, round reset Clear. |
| P1 | auto-ready vs pending intent conflict | Disconnect = **immediate pending intent cancellation** (auto-ready = no action) |
| P2 | ReadyTimestamp float, RPC doesn't arrive on FixedUpdate boundary | **ReadyServerTick** (uint) = `NetworkManager.Singleton.ServerTime.Tick` at Rpc receive. Deterministic ordering. |
| P3 | IsFanUpgraded in PlayerStateDelta but not in Applicator Shell | Applicator Shell step 2 now explicitly includes IsFanUpgraded NV write |

---

## FULL PLAN TEXT (v5.1)

# PLAN_025 — Multi Mode Full Implementation (v5.1 — Codex Review #5 반영)

> **Status:** 📋 Planning (v5.1 — Review #1~#5 전체 반영, 재검증 대기)
> **Created:** 2026-09-02 | **Revised:** 2026-09-02
> **Dependencies:** GAME_DESIGN.md, PLAN_018, PLAN_020
> **Absorbs:** PLAN_019 → 역사 문서 전환
> **Scope:** GameScene_Multi 씬 + GameScene wiring 변경(MatchNetworkState 추가) → **1v1 동작 보존**
> **Will NOT touch:** 1v1 Resolve()/CombatResultData/전투 로직, PLAN_024 Rematch (1v1 전용)

---

### Codex Review #5 핵심 수정 (v5 → v5.1)

| # | 지적 | v5.1 반영 |
|---|------|---------|
| B1 | DontDestroyWithOwner=false + ChangeOwnership PlayerObject 미등록 | **SeatRuntimeState 방식**: disconnect 시 서버가 NV 스냅샷→struct 보존, Despawn+Destroy OK. reconnect 시 새 SpawnAsPlayerObject + hydrate. PlayerObject lookup 자연 해결 |
| B2 | Host가 타인 PlayerData 수정 불가 (Unity Lobby 권한) | **클라이언트 self-write**: 각 클라이언트가 token 생성 → 자기 Private PlayerData에 기록 → Host가 Private 읽기 → SessionParticipantTable 저장 |
| B3 | ItemRuleSnapshot.From() 런타임 의존 효과 캡처 불가 | **ItemEffectRuleSnapshot**: 결과 아닌 계산 규칙 캡처. ItemEffectKind discriminator + 파라미터. Resolver가 runtime snapshot으로 결과 계산 |
| B4 | DTO reader 배열 null 접근 | reader-side 배열 초기화 `??= new` + SeatCount≤4, EventCount≤16 양측 검증 |
| B5 | GhostCooldownNetData: unmanaged+IEquatable 계약 없음 | 완전한 struct 계약 + MatchNetworkState 내 생성/구독/해제/Dispose/Clear 수명주기 명시 |
| P1 | auto-ready vs pending intent 충돌 | disconnect 시 **pending intent 즉시 취소** (auto-ready = 무행동) |
| P2 | ReadyTimestamp float, RPC FixedUpdate 보장 없음 | **ReadyServerTick** (uint, NetworkManager.ServerTime.Tick) 사용 |
| P3 | IsFanUpgraded Applicator 누락 | Applicator Shell step 2에 IsFanUpgraded NV write 명시 |

### 이전 리뷰 수정 이력 (v1~v4 → v5)

| Review | Score | 주요 수정 |
|--------|-------|-----------|
| #1 (4.0) | 11 blockers | Phase 순서, Roster, ActionIntent, DTO 설계 |
| #2 (6.0) | 5 blockers | B0↔B 순환, domain violation, MatchConfig NV, Roster reconnect |
| #3 (6.0) | 8B+6P | MCR NV불가→MatchNetworkState, SO→value snapshot, Session/Match scope, seat spawn |
| #4 (6.0) | 7B+6P | PlayerState 보존, MatchNetworkState lifecycle, grace vs round end, PlayerStateDelta, 수동 직렬화, Solo port, token, ChillAura epoch |

---

### Overview

1v1과 Multi는 **분리 씬**. `GameScene` (1v1 Bo3)의 전투 로직은 보존하고, 양쪽 씬에 `MatchNetworkState` NetworkBehaviour를 추가한다. `GameScene_Multi` (킬 기반 5킬 승리)를 신규 구축. PLAN_019 핵심 계약 흡수.

#### 씬 구조

| Build Index | 씬 | 용도 |
|---|---|---|
| 0 | `LobbyScene` | 로비 (공통) |
| 1 | `GameScene` | 1v1 Bo3 (현행 + MatchNetworkState 추가) |
| 2 | `GameScene_Multi` | 3~4인 다인전 (5킬) |
| 3 | `GameScene_Solo` (또는 GameScene 공유) | 솔로 봇전 (TBD) |

#### 디자인 패턴

| 시스템 | 패턴 |
|---|---|
| 규칙 분기 | **Strategy** — `IGameModeRule` |
| 참가자 관리 | **Registry + Roster** (PLAN_019 §4) |
| 아이템 선택 | **Command** — `ActionIntent` |
| 사망/킬 귀속 | **Centralized** — `AuthoritativeDeathService` |
| 전투 파이프라인 | **Functional Core / Imperative Shell** — value snapshot→resolution→applicator |
| Domain↔Network | **Anti-Corruption Layer** — `CombatResolutionBatchNetData` (수동 직렬화) |
| Ghost | **State Pattern** — `LifeState { Alive, Ghost }` |
| Bot AI | **Behavior Tree** |
| N인 연출 | **Sequencer** — PresentationBarrier + CombatVFXManager |
| 상태 보존 | **Memento** — `SeatRuntimeState` (disconnect→reconnect) |

#### Phase 의존성 (v5.1 확정)

```
Phase A (MatchConfig + MatchNetworkState + SeatRuntimeState + Session/Match Roster + Spawn + 씬 + HUD)
    ↓
Phase B0 (LifeState + DeathService + DamageSource)
    ↓
Phase B (ActionIntent + ItemEffectRuleSnapshot + N인 Resolver + DTO + Balance)
    ↓
Phase C (VFX + 환경)
    ↓
Phase D (Ghost Skill + Multiplier + UI)

Phase A ──→ Phase E (Solo/Bot)

Phase F ← 기획 답변 시
```

---

### Phase A — MatchConfig + MatchNetworkState + Roster + Spawn + 씬 + HUD

#### A-1. GameMode + MatchConfig

- [ ] A-1a. `NetworkConstants.GameMode` enum: `None=0, OneVsOne=1, Multi=2, Solo=3`
- [ ] A-1b. `IGameModeRule` interface (12개 필드)
- [ ] A-1c. `GameModeRuleSO : ScriptableObject, IGameModeRule` — 배열 Awake copy
- [ ] A-1d. `MatchConfig` 서버 런타임: `{ IGameModeRule Rule, int RequiredPlayerCount, GameMode Mode }`
- [ ] A-1e. `MatchConfigNetData : INetworkSerializable` — `{ byte Mode, byte RequiredPlayerCount }`

#### A-2. MatchNetworkState — 완전한 NGO 수명주기

- [ ] A-2a. `MatchNetworkState : NetworkBehaviour` 씬 오브젝트로 배치:
  - **GameObject에 NetworkObject 컴포넌트 필수**
  - GameScene **및** GameScene_Multi 양쪽에 배치
- [ ] A-2b. 필드:
  ```csharp
  public NetworkVariable<MatchConfigNetData> Config = new(
      default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
  public NetworkList<int> KillScores;  // Awake에서 생성
  ```
- [ ] A-2c. Awake:
  ```csharp
  void Awake() { KillScores = new NetworkList<int>(); }
  ```
- [ ] A-2d. OnNetworkSpawn — **Host 포함**:
  ```csharp
  public override void OnNetworkSpawn()
  {
      Config.OnValueChanged += OnConfigChanged;
      KillScores.OnListChanged += OnKillScoresChanged;
      if (Config.Value.Mode != 0) OnConfigReceived(Config.Value);
  }
  ```
  - ⚠ **람다 아닌 저장 delegate** (named method)
- [ ] A-2e. OnNetworkDespawn — 이벤트 해제
- [ ] A-2f. OnDestroy — NetworkList Dispose
- [ ] A-2g. 서버: Config.Value 1회 기록 + KillScores 초기화
- [ ] A-2h. MCR: FindAnyObjectByType → local MatchConfig 조립

#### A-3. Session Scope vs Match Scope Roster

- [ ] A-3a. **SessionParticipantTable** (DDOL, Coordinator 소유):
  - **Token 전달 경로** (B2 해결 — Unity Lobby 권한 모델 준수):
    ```
    Unity Lobby 권한: 각 플레이어는 자기 PlayerData만 수정 가능.
    Host도 타인의 PlayerData 수정 불가. 단, Host는 Private 데이터를 읽을 수 있음.
    
    1. 각 클라이언트가 Lobby Join 시 암호학적 random token 생성 (System.Guid.NewGuid())
    2. 자신의 PlayerData에 기록 (key: "SessionToken", visibility: Private)
       → Private: 본인 + Host만 읽기 가능, 다른 멤버에게 노출 안 됨
    3. Host: LobbyManager에서 Lobby.Players[].Data["SessionToken"] 읽기
       → Host는 Private visibility 데이터 접근 가능 (Lobby API 권한)
       → 읽은 token을 SessionParticipantTable에 저장
    4. NGO 연결 시: 클라이언트가 자기가 생성한 token을 ConnectionData payload에 포함
    5. Reconnect 시: 동일 token 재사용
    ```
  - ConnectionApprovalCallback: ParticipantId + token 검증
- [ ] A-3b. **MatchRoster** (서버 전용, MCR 소유):
  - ConnectionState: `Connected, Disconnected, TimedOut`
  - seat 0~3 순차 할당, PlayerIndex = seat 불변
- [ ] A-3c. **SeatRuntimeState** 서버 전용 struct (B1 해결):
  ```csharp
  struct SeatRuntimeState
  {
      public byte SeatIndex;
      public float Temperature;
      public LifeState CurrentLifeState;
      public float FanSpeed;
      public bool IsFanActive;
      public bool IsFanUpgraded;
      public bool IsBasicBlocked;
      public PlayerModifiers Modifiers;
      public SlotSnapshot[] InventorySlots;
      public ScheduledEffectSnapshot[] PendingEffects;
      public DamageSource LastDamageSource;
  }
  ```
  - MatchRoster가 `Dictionary<byte seat, SeatRuntimeState>` 보유
- [ ] A-3d. **Disconnect 처리** (B1 해결):
  ```
  ⚠ Player.prefab DontDestroyWithOwner=false → NGO가 owner disconnect 시
    PlayerObject 자동 정리 가능. ChangeOwnership은 PlayerObject lookup 미등록.
  ∴ SeatRuntimeState 방식:

  1. OnClientDisconnected 콜백
  2. 서버: PlayerState에서 SeatRuntimeState 추출 (모든 NV 읽기)
  3. MatchRoster에 SeatRuntimeState 저장
  4. PlayerState NetworkObject: Despawn + Destroy (state는 struct에 보존)
  5. MatchRoster seat.State → Disconnected + 30초 타이머
  6. 진행 중 Barrier에서 이전 ClientId 즉시 제거
  7. pending intent 즉시 취소 — auto-ready (무행동) [P1]
  8. PlayerRegistry: seat unbind
  ```
- [ ] A-3e. **Reconnect** (B1 해결):
  ```
  1. ConnectionApproval 통과 (토큰 검증)
  2. MatchRoster: ParticipantId → seat 조회, Disconnected 확인
  3. 새 Player prefab SpawnAsPlayerObject(newClientId)
     → PlayerObject lookup 자동 작동 (InventoryPresenter, CombatVFXManager, MiniGameHub 호환)
  4. SeatRuntimeState → 새 PlayerState hydrate (서버 NV write)
  5. seat.ClientId = newClientId, State = Connected
  6. PlayerRegistry: seat → 새 PlayerState rebind
  7. SeatRuntimeState 제거 (hydrate 완료)
  8. 진행 중 Barrier: 추가하지 않음
  ```
- [ ] A-3f. **30초 timeout**: TimedOut → SeatRuntimeState 폐기 → seat permanently empty → 생존자 재평가
- [ ] A-3g. Start gate: `ConnectedCount == RequiredPlayerCount`
- [ ] A-3h. Late join 거절

#### A-4. Spawn + Registry

- [ ] A-4a. `PlayerSpawnManager.GetSpawnPosition(byte seatIndex)`: markers[seat].position
- [ ] A-4b. 서버 스폰: `MatchRoster.GetSeat(clientId) → seat → GetSpawnPosition(seat)`
- [ ] A-4c. `PlayerState.Initialize(byte seat)`
- [ ] A-4d. PlayerRegistry: `Register(seat, playerState)` seat 기반 lookup

#### A-5. 로비 → 다인전 진입

- [ ] A-5a~f. LobbyModeSelectView, LobbyPresenter, LobbyRoomView, NetworkSessionCoordinator, 1v1 보존, ConnectionApproval

#### A-6. GameScene_Multi 씬

- [ ] A-6a~g. 씬 복사, SpawnPoint 4개, EnemyPlayer 3개, Visual slot 매핑, MatchNetworkState 배치, Build Settings

#### A-7. N인 HUD

- [ ] A-7a~i. MatchSnapshot, GameDataBridge, MatchHudPresenter, GameHudRefs, GameHudBuilder, OpponentBarPresenter, InventoryPresenter, RoundResultPresenter, TemperaturePresenter

#### A-8. 통합 테스트

- [ ] A-8a~k. 로비→Multi, seat 안정성(SeatRuntimeState hydrate), MatchNetworkState Host 포함, Relay, 1v1 회귀, ConnectionApproval, spawn, start gate, disconnect→SeatRuntimeState 추출, reconnect→새 spawn+hydrate, token Private

---

### Phase B0 — LifeState + DeathService + DamageSource

#### B0-1. LifeState + Roster 분리

- [ ] B0-1a. `LifeState { Alive, Ghost }` NV
- [ ] B0-1b. Roster = 연결, LifeState = 게임
- [ ] B0-1c. **상태 조합**:
  ```
  TurnEligible:         Connected && Alive
  GhostEligible:        Connected && Ghost
  CountsAsAliveForRoundEnd: (Connected || Disconnected) && Alive
  AutoReady:            Disconnected && Alive → 무행동 (pending intent 이미 취소됨)
  ```
  - TimedOut은 어느 카운트에도 미포함

#### B0-2. AuthoritativeDeathService

- [ ] B0-2a~e. TryKill, IPlayerTurnCancellation, FlushDeathQueue, DamageSource 정규화, ScheduledEffect.SourceSeat

#### B0-3. 킬 스코어 + 승리 + 라운드 리셋

- [ ] B0-3a~e. KillScores, SeatSnapshot, WinnerMask, 라운드 종료(CountsAsAliveForRoundEnd ≤ 1), 라운드 리셋(10항목)

#### B0-4. 통합 테스트

- [ ] B0-4a~i. Ghost 전환, 중복 TryKill, 킬 스코어, 리셋, Natural source, 1v1 회귀, SourceSeat, disconnect 유예, timeout 재평가

---

### Phase B — ActionIntent + ItemEffectRuleSnapshot + N인 Resolver + DTO + Balance

#### B-1. ActionIntent

- [ ] B-1a. `ActionIntent` readonly struct: { SourceSeat, SlotIndex, ItemId, TargetSeat(255=NoTarget), **ReadyServerTick(uint)** }
  - ReadyServerTick = `NetworkManager.Singleton.ServerTime.Tick` at Rpc receive [P2]
  - 행동 순서: 방어 우선 → ReadyServerTick 오름차순 → 온도 낮은 순 → seat index
- [ ] B-1b~e. QueuedAction.TargetSeat, _pendingIntent, IPlayerTurnCancellation, ILocalPlayerCommands

#### B-2. 아이템 타겟

- [ ] B-2a~d. TargetMode, 21종 분류, 드래그-타겟 UI, 서버 검증

#### B-3. Value-Type Snapshots

> **계약**: factory defensive copy. Resolver 배열 미변경.

- [ ] B-3a. `GameModeRuleSnapshot` readonly struct — `.ToArray()` copy
- [ ] B-3b. **`ItemEffectRuleSnapshot`** readonly struct (B3 해결 — **계산 규칙 캡처, 결과 아님**):
  ```csharp
  readonly struct ItemEffectRuleSnapshot
  {
      public readonly short ItemId;
      public readonly ItemCategory Category;
      public readonly TargetMode TargetMode;
      public readonly DamageFilter AttackFilter;
      public readonly ItemEffectKind EffectKind;  // discriminator
      
      // Attack
      public readonly float BaseDamage;
      public readonly bool IsEqualize;  // Resolver computes from temperatures
      
      // Defense
      public readonly bool IsDefense;
      public readonly float DefenseReduction;
      
      // Recovery
      public readonly float BaseHeal;
      public readonly bool ScalesWithUses;  // Resolver checks InventorySnapshot
      public readonly float HealPerUseMultiplier;
      
      // Fan/Status
      public readonly bool WritesFanSpeed; public readonly float FanSpeedValue;
      public readonly bool WritesTargetFanSpeed; public readonly float TargetFanSpeedValue;
      public readonly bool BlocksTargetBasics;
      
      // Special
      public readonly bool GrantsExtraAction;
      public readonly bool RevealsOpponent;  // Resolver checks target intent existence
      public readonly bool NeutralizesTarget;
      public readonly SpecialEffectType SpecialKind;
      
      // Inventory & Scheduled
      public readonly InventoryMutationType InventoryAction;
      public readonly bool HasScheduledEffect;
      public readonly EffectType ScheduledType;
      public readonly float ScheduledValue;
      public readonly int ScheduledDelay;
  }
  
  enum ItemEffectKind : byte
  {
      DirectDamage, Equalize, Recovery, Defense,
      FanControl, Sabotage, Special, Buff, Debuff, Scheduled
  }
  ```
  - Factory: `ItemEffectRuleSnapshot.From(ItemDataSO so)` — SO의 정적 파라미터 복사 (ComputeEffect 결과 아님!)
  - Resolver 책임: ItemEffectKind별 분기 + MatchCombatSnapshot 런타임 상태로 최종 효과 계산
- [ ] B-3c. `InventorySnapshot` — SlotSnapshot[] defensive copy
- [ ] B-3d. `ScheduledEffectSnapshot` — +SourceSeat

#### B-4. Functional Core

- [ ] B-4a. `MatchCombatSnapshot` — 순수 value-type, defensive copy, ItemEffectRuleSnapshot[] ItemRules
- [ ] B-4b. `MultiCombatResolution` — 전체 state delta:
  - `PlayerStateDelta { SeatIndex, NewFanSpeed?, IsFanUpgraded?, IsBasicBlocked? }`
  - CombatEvent[], TemperatureDeltas[], DamageSource[], DeadMask, ActionOrder, ItemIds
  - InventoryDelta[], ModifierDelta[], ScheduledEffectDelta[]
  - WinnerMask/ResultSequence 미포함 (Shell 결정)
- [ ] B-4c. `CombatResolver.ResolveMulti` — ItemEffectKind별 분기:
  - DirectDamage: rule.BaseDamage
  - Equalize: `(snap.Temps[user] + snap.Temps[target]) / 2`
  - Recovery+ScalesWithUses: `baseHeal * remaining * multiplier`
  - RevealsOpponent: target intent 존재 여부
  - 행동 순서: 방어 우선 → ReadyServerTick → 온도 → seat
  - 기존 `Resolve(p1, p2)` 보존
- [ ] B-4d. Applicator Shell: step 2 = FanSpeed/**IsFanUpgraded**/IsBasicBlocked NV write [P3]

#### B-5. Network DTO — 수동 직렬화

- [ ] B-5a. `CombatEventNetData` (10 bytes)
- [ ] B-5b. 이벤트 불변식: 최대 16
- [ ] B-5c. `CombatResolutionBatchNetData` — **reader-side 배열 초기화** (B4):
  ```csharp
  if (s.IsReader)
  {
      Events ??= new CombatEventNetData[16];
      TempBefore ??= new float[4];
      TempAfter ??= new float[4];
      MainItemIds ??= new short[4];
      SubItemIds ??= new short[4];
      ActionOrder ??= new int[4];
  }
  // 범위 검증 (양측)
  if (SeatCount > 4) throw ...;
  if (EventCount > 16) throw ...;
  if (!s.IsReader && EventCount > Events.Length) throw ...;
  ```
  - ~246 bytes (1200 MTU 이내)
- [ ] B-5d~e. Overflow abort, 1v1 보존

#### B-6. Balance

- [ ] B-6a~e. MaxRandomItems cap, 바람막이 1-use, 타로 필터, Deathmatch cap, ReadyServerTick 동시 판정

#### B-7. TurnManager N인

- [ ] B-7a~d. 동적 배열, N명 루프, Multi→ResolveMulti/1v1→기존, BuffDebuffSystem N인

#### B-8. 통합 테스트

- [ ] B-8a~j. 드래그-타겟, 미니게임 보존, 중간 사망, Target Ghost, 5킬, 밸런스, DTO, 1v1 회귀, 전 seat 관점, PlayerStateDelta

---

### Phase C — N인 VFX + 환경

- [ ] C-1a~d. PresentationBarrier, CombatVFXManager, ActionOrder, DTO 소비
- [ ] C-2a~b. Ghost 연출, 라운드 종료
- [ ] C-3a~e. 잼민이, 앰뷸런스, 폭염경보, 기타, EnvironmentRuleService
- [ ] C-4a~d. 통합 테스트

---

### Phase D — Ghost Skill + Multiplier + UI

#### D-1. PlayerModifiers

- [ ] D-1a~d. FanSpeedMultiplier, ApplyFanTick×multiplier, ApplyRecoveryTick×multiplier, ChillAura epoch (until next PrepPhase end)

#### D-2. Ghost 스킬 + 쿨다운

- [ ] D-2a. 쿨다운: `Dictionary<(byte seat, byte skill), int>`
- [ ] D-2b. **GhostCooldownNetData** (B5 해결):
  ```csharp
  public struct GhostCooldownNetData : INetworkSerializable, IEquatable<GhostCooldownNetData>
  {
      public byte Seat, Skill, RemainingTurns;
      public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter { ... }
      public bool Equals(GhostCooldownNetData other) => ...;
      public override int GetHashCode() => ...;
  }
  ```
  - MatchNetworkState lifecycle: Awake init, OnNetworkSpawn subscribe, OnNetworkDespawn unsubscribe, OnDestroy Dispose, round Clear
- [ ] D-2c~g. GhostSkillServerRpc 6단계, 덮어쓰기+debuff entry, Active debuff dictionary, FrostStrike, ChillAura

#### D-3. Ghost UI

- [ ] D-3a~e. 스킬 버튼, 타겟 선택, 쿨다운 오버레이, PrepPhase 자유, Ghost 시야

#### D-4. 통합 테스트

- [ ] D-4a~h. FrostStrike, ChillAura fan×2, Recovery×0.5, 덮어쓰기, 라운드 리셋, Ghost 5킬, ChillAura 만료, 쿨다운 UI

---

### Phase E — Solo/Bot

#### E-1~E-4.
- [ ] E-1a~h. Solo 인프라 (GetLocalEndpoint().Port, BotBootstrap, Process 관리)
- [ ] E-2a~e. Bot AI (IBotBrain, BotPlayer, 난이도, Ready, 미니게임)
- [ ] E-3a~b. 외형/이름
- [ ] E-4a~f. 통합 테스트

---

### Phase F — 미해결 질문 + 정리

- [ ] F-1a~h. Q10, Q16, Q21~Q23, Q24, Q26, Q27
- [ ] F-2a~e. PLAN_019 역사화, PLAN_021, GAME_DESIGN, ACTIVE_CONTEXT, CHANGES

---

### 완료 불변식 (16개)

1. PlayerIndex = seat, match 동안 고정
2. ClientId/OwnerClientId/PlayerIndex 암묵적 변환 금지
3. eligible actor당 intent turn당 최대 1
4. secret intent 비인가 복제 금지
5. 동일 snapshot+intents → 동일 ordered resolution (tie-break: seat)
6. Barrier는 Begin 시점 snapshot만 기다림 — reconnect 추가 금지
7. phase driver와 NV writer 각 하나
8. 2인 기존 golden result 유지
9. collection/DTO 4인/16 event bound, 위반=abort
10. disconnect/despawn/reconnect cleanup idempotent
11. CombatResolver는 value-type snapshot만 (defensive copy, 배열 미변경)
12. WinnerMask/ResultSequence는 Shell 결정
13. KillerSeat=255 → Origin=Natural
14. 진행 중 Barrier reconnect 추가 금지
15. ItemEffectRuleSnapshot은 계산 규칙만 (런타임 결과 아님). Resolver가 snapshot 상태로 효과 계산
16. disconnect 시 pending intent 즉시 취소 (auto-ready = 무행동)

### 검증 게이트

1. Host + remote 3
2. 3인/4인 모두
3. 2번째 라운드 이상 (리셋)
4. Prep/Attack/VFX 중 disconnect
5. 모든 local seat 관점 UI
6. 1v1 회귀
7. spawn = SpawnPoint[seat]
8. reconnect → 같은 seat + 새 PlayerState + SeatRuntimeState hydrate NV 값 일치
9. disconnect 유예 중 라운드 미종료
10. MatchNetworkState Host 포함 초기화

### 위험 요소

| 리스크 | 완화 |
|--------|------|
| TurnManager 배열 → 1v1 회귀 | 기존 Resolve() 보존 |
| CombatResultData 2인 DTO | 1v1 그대로 |
| Ghost Frost PrepPhase | 즉시 FlushDeathQueue |
| Solo 별도 프로세스 | GetLocalEndpoint().Port |
| MCR NV 불가 | MatchNetworkState 분리 |
| SO in Resolver | ItemEffectRuleSnapshot 규칙 캡처 + Resolver 계산 |
| DTO 직렬화 | 수동 요소별 + reader-side ??= new |
| {255,Fan} OOB | Natural 정규화 + guard |
| Reconnect NV 소실 | SeatRuntimeState + 새 SpawnAsPlayerObject + hydrate |
| PlayerObject lookup 미등록 | SeatRuntimeState: 새 spawn → 자연 해결 |
| DontDestroyWithOwner=false | SeatRuntimeState: Despawn+Destroy OK |
| Disconnect grace vs round end | CountsAsAliveForRoundEnd 분리 |
| Resolution delta 불완전 | PlayerStateDelta (FanSpeed/IsFanUpgraded/IsBasicBlocked) |
| Token Lobby 권한 | 클라이언트 self-write Private → Host 읽기 |
| ItemRule 런타임 의존 | ItemEffectRuleSnapshot 규칙 + Resolver 계산 |
| DTO reader 배열 null | reader-side ??= new + 양측 검증 |
| GhostCooldownNetData | unmanaged+IEquatable+INetworkSerializable+lifecycle |
| ReadyTimestamp 비결정적 | ReadyServerTick (NetworkTime.Tick) |
| ChillAura epoch | GAME_DESIGN: 다음 PrepPhase 종료 시 |

---

## END OF PLAN TEXT

---

## Review Criteria

Rate this plan on a 1–10 scale. For each item below, flag BLOCKER (prevents implementation), PARTIAL (works but has a gap), or PASS.

### Category Checklist

1. **NGO 2.11.2 API correctness**
   - NetworkVariable/NetworkList lifecycle (init, subscribe, unsubscribe, dispose)
   - INetworkSerializable — manual serialization (no FixedList)
   - Rpc direction
   - NetworkObject on same GO as NetworkBehaviour
   - Host reads NV too (not excluded by IsClient && !IsServer)
   - GhostCooldownNetData: unmanaged + IEquatable<T> + INetworkSerializable + full lifecycle

2. **Authority model**
   - All game state mutations server-only
   - Client sends intent only via Rpc
   - No client-side NV writes

3. **Reconnect / Disconnect — SeatRuntimeState approach**
   - SeatRuntimeState extracts ALL relevant NV state on disconnect
   - Despawn+Destroy is safe (DontDestroyWithOwner irrelevant — state in struct)
   - Reconnect = new SpawnAsPlayerObject + hydrate (PlayerObject lookup works)
   - Pending intent cancelled on disconnect (auto-ready = no action)
   - Grace period vs round end (CountsAsAliveForRoundEnd vs TurnEligible)
   - Timeout → SeatRuntimeState discarded → seat empty → survivor re-eval

4. **Session token delivery (Unity Lobby permissions)**
   - Client self-writes token to Private PlayerData (not Host writing others')
   - Host reads Private data (permitted by Lobby API)
   - Token NOT exposed to other members (Private visibility)

5. **Combat resolution (Functional Core / Imperative Shell)**
   - ItemEffectRuleSnapshot captures RULES, not pre-computed results
   - ItemEffectKind discriminator for runtime-dependent effects
   - Resolver computes Equalize from snapshot temperatures
   - Resolver computes Recovery scaling from InventorySnapshot remaining uses
   - Resolver checks target intent for Tarot reveal
   - Complete delta: Temperature, FanSpeed, IsFanUpgraded, IsBasicBlocked, Inventory, Modifiers, ScheduledEffects
   - Shell applies deltas → NV writes, WinnerMask/ResultSequence Shell-only

6. **DTO / Serialization**
   - Reader-side array initialization (??= new) before access
   - SeatCount ≤ 4, EventCount ≤ 16 validation (both sides)
   - Writer-side EventCount ≤ Events.Length
   - Size within MTU
   - 1v1 CombatResultData untouched

7. **Ghost system**
   - GhostCooldownNetData full contract (INetworkSerializable + IEquatable + lifecycle)
   - FrostStrike / ChillAura with multipliers
   - ChillAura epoch = "until next PrepPhase end"
   - Cooldown per-(seat, skill) key
   - Active debuff dictionary cleaned on expiry and round reset
   - DamageSource normalization

8. **Balance / Item correctness**
   - Windbreaker Multi 1-use
   - Tarot SingleTarget
   - InitialRandomItems 1v1=4, Multi=2
   - ReadyServerTick for deterministic ordering

9. **Scene / Spawn / Roster**
   - Separate scenes
   - Seat-based spawn
   - MatchNetworkState on both scenes
   - Session vs Match scope

10. **1v1 regression safety**

11. **Solo / Bot**
    - GetLocalEndpoint().Port

12. **Completeness**
    - 180 tasks cover full feature set
    - No orphaned references
    - Phase dependencies acyclic
    - 16 invariants testable

## Output Format

```
SCORE: X/10

### BLOCKER (prevents implementation)
B1: [description] — Task [ID] — [what's wrong] — [suggested fix]

### PARTIAL (works but has a gap)
P1: [description] — Task [ID] — [what's missing] — [suggested fix]

### PASS (verified correct)
[category]: [brief note]

### Summary
[2-3 sentences on overall readiness]
```

If score ≥ 9: reply "APPROVED" at the top.
If score < 9: list all blockers and partials with specific task IDs and suggested fixes.

---END---
