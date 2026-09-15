# PLAN_025 Codex Review Prompt v5

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
{
    public static MatchCompositionRoot Instance { get; private set; }
    PlayerRegistry _registry;
    ItemManager _itemManager;
    MatchManager _matchManager;
}
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
            Destroy(networkObject.gameObject);  // ← NV state LOST on disconnect!
        }
        spawnedPlayers.Remove(clientId);
    }
}

// line 238 — spawn position uses clientId (not seat)
private Vector3 GetSpawnPosition(ulong clientId)
{
    int index = (int)(clientId % (ulong)resolvedSpawnPoints.Count);  // ← needs seat-based
}
```

### PlayerInventory — ConsumeItem unlimited check
```csharp
// Assets/Scripts/Core/Player/PlayerInventory.cs line 52
public void ConsumeItem(byte slotIndex)
{
    if (!IsServer) return;
    if (slotIndex >= SlotStates.Count) return;
    var slot = SlotStates[slotIndex];
    if (slot.IsUnlimited) return;  // ← Multi windbreaker must be IsUnlimited=false
    if (slot.RemainingUses == 0) return;
    slot.RemainingUses--;
}
```

### ItemEffectApplicator — NV writes that must appear in resolution delta
```csharp
// Assets/Scripts/Core/Item/ItemEffectApplicator.cs
if (outcome.BlockTargetBasics)
    ctx.Target.IsBasicBlocked.Value = true;     // ← MISSING from prior delta
if (outcome.WriteUserFanSpeed)
    ctx.User.FanSpeed.Value = outcome.UserFanSpeedValue;  // ← MISSING from prior delta
if (outcome.WriteTargetFanSpeed)
    ctx.Target.FanSpeed.Value = outcome.TargetFanSpeedValue;  // ← MISSING from prior delta
```

### ItemEffectOutcome — Complete field list
```csharp
public struct ItemEffectOutcome
{
    public float UserHeal, UserDamage, TargetHeal, TargetDamage;
    public DamageFilter UserDamageFilter, TargetDamageFilter;
    public DefenseInfo? TargetDefenseCheck, SetUserDefense;
    public bool NeutralizeTarget, GrantExtraAction, RevealOpponent;
    public bool BlockTargetBasics;
    public bool WriteUserFanSpeed; public float UserFanSpeedValue;
    public bool WriteTargetFanSpeed; public float TargetFanSpeedValue;
    public bool HasScheduledEffect;
    public int ScheduledTargetIndex; public EffectType ScheduledType;
    public float ScheduledValue; public int ScheduledDelayTurns;
    public InventoryMutationType InventoryAction;
    public bool Blocked;
}
```

### ScheduledEffect — NO SourceSeat field currently
```csharp
// Assets/Scripts/Core/Buff/BuffDebuffSystem.cs
struct ScheduledEffect
{
    public int TargetPlayerIndex;
    // NO SourceSeat field! Plan adds it.
    public EffectType Type;
    public float Value;
    public int TurnsRemaining;
}
public void ProcessTurnStart(PlayerState p1, PlayerState p2)  // 2-player only
```

### TemperatureSystem — No multiplier support
```csharp
// Assets/Scripts/Core/Combat/TemperatureSystem.cs line 37
public void ApplyFanTick(PlayerState player)
{
    float newTemp = Mathf.Max(MIN_TEMP, before - player.FanSpeed.Value);  // no multiplier!
}
// line 46
public void ApplyRecoveryTick(PlayerState player, float recoveryRate)
{
    float newTemp = Mathf.Min(MAX_TEMP, before + recoveryRate);  // no multiplier!
}
```

### PlayerState — Tarot OpponentRevealed uses ctx.Target
```csharp
// Assets/Scripts/Core/Player/PlayerState.cs line 315-329
if (ctx.UserModifiers.OpponentRevealed)
{
    var opponent = ctx.Target;  // ← must be opponent, not self
    var oppQueue = opponent.GetActionQueue();
}
```

### UnityTransport — GetLocalEndpoint confirmed
```csharp
// com.unity.transport UnityTransport.cs line 394
public NetworkEndpoint GetLocalEndpoint()  // returns actual bound endpoint
// Correct usage: ((UnityTransport)NetworkManager.Singleton.NetworkConfig.NetworkTransport).GetLocalEndpoint().Port
```

### GAME_DESIGN.md — ChillAura duration
```
| Chill Aura | Target fan decrease rate ×2, recovery effectiveness ×0.5 | 1 turn | 1 turn (until next PrepPhase end) |
```

## Previous Reviews Summary

| Review | Score | Fixed |
|--------|-------|-------|
| v1 (Review #1) | 4.0/10 | 11 blockers → all fixed in v2 |
| v2 (Review #2) | 6.0/10 | 5 blockers → all fixed in v3 |
| v3 (Review #3) | 6.0/10 | 8 blockers + 6 partial → all fixed in v4 |
| v4 (Review #4) | 6.0/10 | 7 blockers + 6 partial → all fixed in v5 (this version) |

### Review #4 Fixes Applied in v5

| # | Issue | v5 Fix |
|---|-------|--------|
| B1 | PlayerState Destroyed on disconnect → NV lost | Keep server-owned: RemoveOwnership() on disconnect, ChangeOwnership(newClientId) on reconnect. No Despawn/Destroy. |
| B2 | MatchNetworkState lifecycle incomplete | Full spec: NetworkObject required, Host reads Config too, stored delegate (not lambda), OnNetworkDespawn cleanup, NetworkList Awake init + OnDestroy Dispose |
| B3 | Disconnect grace vs round end conflict | TurnEligible (Connected+Alive) vs CountsAsAliveForRoundEnd (Connected∥Disconnected + Alive). Round end checks CountsAsAliveForRoundEnd ≤ 1. TimedOut excluded from all. |
| B4 | Resolution delta missing FanSpeed/IsBasicBlocked | Added PlayerStateDelta struct: NewFanSpeed?, IsFanUpgraded?, IsBasicBlocked? — covers all ItemEffectOutcome NV writes |
| B5 | FixedList512Bytes not directly serializable | Manual element-by-element: EventCount prefix → validate 0..16 → serialize each CombatEventNetData. No FixedList. |
| B6 | ServerClientId is 0, not port | `((UnityTransport)NetworkManager.Singleton.NetworkConfig.NetworkTransport).GetLocalEndpoint().Port` confirmed at line 394 |
| B7 | Token delivery path missing | Host writes token to Lobby PlayerData → client reads from poll → includes in ConnectionData payload. Reconnect reuses same token. |
| P1 | readonly struct arrays are references | Factory creates defensive copies (.ToArray()). Resolver contract: must not mutate input arrays. |
| P2 | "Will NOT touch GameScene" conflicts | Changed to "1v1 동작 보존" — GameScene wiring changes (MatchNetworkState addition) are in scope |
| P3 | Ghost cooldown Dict<byte,int> ambiguous | Changed to Dictionary<(byte seat, byte skillIndex), int>. UI via NetworkList<GhostCooldownNetData>. ChillAura expiry removes debuff entry. |
| P4 | ChillAura duration mismatch | Aligned with GAME_DESIGN: "until next PrepPhase **end**". ResetChillAura() called at Prep→Attack transition. |
| P5 | Task count wrong | v5 accurate: 179 checkboxes |
| P6 | Codex prompt abbreviated | v5 prompt includes full plan text below |

---

## FULL PLAN TEXT (v5)

# PLAN_025 — Multi Mode Full Implementation (v5 — Codex Review #4 반영)

> **Status:** 📋 Planning (v5 — Review #1~#4 전체 반영, 재검증 대기)
> **Created:** 2026-09-02 | **Revised:** 2026-09-02
> **Dependencies:** GAME_DESIGN.md, PLAN_018, PLAN_020
> **Absorbs:** PLAN_019 → 역사 문서 전환
> **Scope:** GameScene_Multi 씬 + GameScene wiring 변경(MatchNetworkState 추가) → **1v1 동작 보존**
> **Will NOT touch:** 1v1 Resolve()/CombatResultData/전투 로직, PLAN_024 Rematch (1v1 전용)

---

### Codex Review #4 핵심 수정 (v4 → v5)

| # | 지적 | v5 반영 |
|---|------|---------|
| B1 | Reconnect: Despawn+Destroy → NV 소실 | disconnect 시 PlayerState **서버 소유 유지** (Despawn 안 함). ownership만 제거, reconnect 시 `ChangeOwnership(newClientId)` |
| B2 | MatchNetworkState NGO 수명주기 불완전 | NetworkObject 필수, Host 경로, 저장 delegate, OnNetworkDespawn 해제, NetworkList 초기화/정리 전부 명시 |
| B3 | Disconnect grace vs 라운드 종료 충돌 | `CountsAsAliveForRoundEnd` (Connected∥Disconnected + Alive) vs `TurnEligible` (Connected + Alive) 분리 |
| B4 | Resolution delta 불완전: FanSpeed/IsFanUpgraded/IsBasicBlocked NV 누락 | `PlayerStateDelta` 추가 — ItemEffectOutcome의 모든 NV write 포함 |
| B5 | FixedList512Bytes NGO 직접 직렬화 불가 | 수동 직렬화: EventCount prefix + 0~16 검증 + 요소별 Serialize |
| B6 | ServerClientId는 포트가 아님 | `((UnityTransport)...NetworkTransport).GetLocalEndpoint().Port` 확정 |
| B7 | Token 전달 경로 누락 | Lobby PlayerData에 Host가 token 기록 → 클라이언트가 Lobby poll에서 읽기 → ConnectionData에 포함. reconnect 시 동일 token 재사용 |
| P1 | readonly struct 내 배열은 참조형 | factory defensive copy + Resolver 배열 미변경 계약 명시 |
| P2 | "Will NOT touch GameScene" 충돌 | scope 문구 변경: "1v1 동작 보존" (GameScene wiring 변경 포함) |
| P3 | Ghost cooldown (seat,skill) 키 필요 | `Dictionary<(byte seat, byte skill), int>` + owner-visible NV 복제 + ChillAura 만료 시 debuff entry 제거 |
| P4 | ChillAura 지속: PLAN vs GAME_DESIGN | GAME_DESIGN 기준: "until next PrepPhase **end**" → Prep 종료 시 해제 |
| P5 | Task 수 오류 | v5 정확 재집계 |
| P6 | Codex 프롬프트 축약 | v5 프롬프트: PLAN 전문 그대로 포함 |

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

#### Phase 의존성 (v5 확정)

```
Phase A (MatchConfig + MatchNetworkState + Session/Match Roster + Spawn + 씬 + HUD)
    ↓
Phase B0 (LifeState + DeathService + DamageSource)
    ↓
Phase B (ActionIntent + Value Snapshots + N인 Resolver + DTO + Balance)
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

#### A-2. MatchNetworkState — 완전한 NGO 수명주기 (B2 해결)

- [ ] A-2a. `MatchNetworkState : NetworkBehaviour` 씬 오브젝트로 배치:
  - **GameObject에 NetworkObject 컴포넌트 필수** (씬 내 NetworkBehaviour는 NetworkObject가 같은 GO에 있어야 함)
  - GameScene **및** GameScene_Multi 양쪽에 배치 (1v1도 MatchConfig 필요)
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
      // Host/Client 모두 구독 (IsClient && !IsServer 아님!)
      Config.OnValueChanged += OnConfigChanged;
      KillScores.OnListChanged += OnKillScoresChanged;
      
      // 초기값 읽기 (이미 서버가 기록했을 수 있음)
      if (Config.Value.Mode != 0)
          OnConfigReceived(Config.Value);
  }
  ```
  - ⚠ **람다 아닌 저장 delegate** (named method)
- [ ] A-2e. OnNetworkDespawn — 이벤트 해제:
  ```csharp
  public override void OnNetworkDespawn()
  {
      Config.OnValueChanged -= OnConfigChanged;
      KillScores.OnListChanged -= OnKillScoresChanged;
  }
  ```
- [ ] A-2f. OnDestroy — NetworkList Dispose:
  ```csharp
  void OnDestroy() { KillScores?.Dispose(); }
  ```
- [ ] A-2g. 서버: scene load 완료 후 `Config.Value = data` 1회 기록 + `KillScores` 초기화 (RequiredPlayerCount 크기, 0 fill)
- [ ] A-2h. MCR: `MatchNetworkState` FindAnyObjectByType → 복제 상태 읽어 local MatchConfig 조립

#### A-3. Session Scope vs Match Scope Roster

- [ ] A-3a. **SessionParticipantTable** (DDOL, Coordinator 소유):
  - Host 시작 시 생성
  - Lobby participant: `{ ParticipantId, SessionToken, IsConnected }`
  - **Token 전달 경로** (B7 해결):
    ```
    1. Host가 CreateLobby 시 participant별 session token 생성
    2. Host → Lobby PlayerData에 token 기록 (key: "SessionToken")
    3. 클라이언트: Lobby poll에서 자기 PlayerData 읽기 → token 저장
    4. NGO 연결 시: ConnectionData payload에 { ParticipantId(UTF8), token(UTF8) } 포함
    5. Reconnect 시: 동일 token 재사용 (token은 match 수명 동안 유효)
    ```
  - ConnectionApprovalCallback:
    1. payload 파싱: ParticipantId + token
    2. SessionParticipantTable 조회 + token 검증
    3. 중복 ParticipantId (이미 connected) → 거절
    4. 테이블에 없음 (late join) → 거절
- [ ] A-3b. **MatchRoster** (서버 전용, MCR 소유):
  - ConnectionState: `Connected, Disconnected, TimedOut` (연결만)
  - seat 0~3 순차 할당, PlayerIndex = seat 불변
- [ ] A-3c. **Disconnect 시 PlayerState 보존** (B1 해결):
  ```
  1. OnClientDisconnected 콜백
  2. PlayerState의 NetworkObject: Despawn/Destroy하지 않음!
  3. 대신: networkObject.RemoveOwnership() → 서버 소유로 전환
  4. MatchRoster seat → Disconnected + 30초 타이머
  5. 진행 중 Barrier에서 이전 ClientId 즉시 제거
  6. auto-ready 설정 (행동 없이 턴 진행)
  7. pending intent 보존 (timeout 시 취소)
  ```
- [ ] A-3d. **Reconnect** (B1 해결):
  ```
  1. ConnectionApproval 통과 (A-3a)
  2. MatchRoster에서 ParticipantId → seat 조회
  3. seat.State == Disconnected 확인
  4. 기존 PlayerState의 NetworkObject.ChangeOwnership(newClientId)
  5. seat.ClientId = newClientId, State = Connected
  6. PlayerRegistry: old ClientId → new ClientId rebinding
  7. ⚠ NV/NetworkList는 서버에 살아 있으므로 자동 동기화됨
  8. ⚠ 진행 중 Barrier: 추가하지 않음 → 다음 sequence부터 새 ClientId
  ```
- [ ] A-3e. **30초 timeout**: TimedOut → seat의 PlayerState Despawn+Destroy → 생존자 재평가
- [ ] A-3f. Start gate: `ConnectedCount == RequiredPlayerCount` → 씬 전환
- [ ] A-3g. Late join 거절

#### A-4. Spawn + Registry

- [ ] A-4a. `PlayerSpawnManager.GetSpawnPosition(byte seatIndex)`: markers[seat].position
- [ ] A-4b. 서버 스폰: `MatchRoster.GetSeat(clientId) → seat → GetSpawnPosition(seat)`
- [ ] A-4c. `PlayerState.Initialize(byte seat)` — TurnManager ClientId 정렬 경로 제거
- [ ] A-4d. PlayerRegistry: `Register(seat, playerState)` seat 기반 lookup

#### A-5. 로비 → 다인전 진입

- [ ] A-5a. LobbyModeSelectView "다인전" 버튼 → 인원 선택
- [ ] A-5b. LobbyPresenter.HandleMultiClicked(int count)
- [ ] A-5c. LobbyRoomView 3~4인 슬롯
- [ ] A-5d. NetworkSessionCoordinator: CreateLobbyAsync(mode, playerCount), Relay AllocateAsync(playerCount - 1), 씬 분기
- [ ] A-5e. 기존 1v1: OneVsOne+2 (기존 시그니처 보존)
- [ ] A-5f. ConnectionApproval 등록

#### A-6. GameScene_Multi 씬

- [ ] A-6a. GameScene 복사 → index 2
- [ ] A-6b. SpawnPoint3D 4개 (Order 0~3)
- [ ] A-6c. EnemyPlayer_0/1/2
- [ ] A-6d. Visual slot ↔ Match seat 분리:
  ```
  localSeat = myPlayerIndex
  remoteSeatList = allSeats.Where(s != localSeat).OrderBy(s)
  remoteSeatList[i] → EnemyPlayer_{i}
  ```
- [ ] A-6e. MatchCompositionRoot + MatchNetworkState(+NetworkObject) + Multi GameModeRuleSO
- [ ] A-6f. GameScene에도 MatchNetworkState(+NetworkObject) 추가 (1v1용)
- [ ] A-6g. Build Settings 갱신

#### A-7. N인 HUD

| UI 파일 | 변경 |
|---------|------|
| MatchSnapshot | +KillScores[], +LifeStates[], +RequiredPlayerCount |
| GameDataBridge | +MatchNetworkState.Config/KillScores 구독, seat-keyed N인 |
| MatchHudPresenter | 킬 스코어 N인, 라운드(1v1) vs 킬(Multi) |
| GameHudRefs | +N인 상대 온도 바, 킬 스코어 텍스트 |
| GameHudBuilder | N인 상대 bar 동적 생성 |
| OpponentBarPresenter | seat-keyed N인 |
| InventoryPresenter | 드래그-타겟 (B-2c 연계) |
| RoundResultPresenter | Multi 공동 승리 → Lobby 복귀 |
| TemperaturePresenter | N인 |

- [ ] A-7a. MatchSnapshot 확장
- [ ] A-7b. GameDataBridge 확장
- [ ] A-7c. MatchHudPresenter 확장
- [ ] A-7d. GameHudRefs 확장
- [ ] A-7e. GameHudBuilder 확장
- [ ] A-7f. OpponentBarPresenter 확장
- [ ] A-7g. InventoryPresenter 확장
- [ ] A-7h. RoundResultPresenter 확장
- [ ] A-7i. TemperaturePresenter 확장

#### A-8. 통합 테스트

- [ ] A-8a. 로비 → 3인 방 → GameScene_Multi 로드
- [ ] A-8b. seat 안정성 (disconnect → reconnect → 같은 seat, **같은 PlayerState 오브젝트**)
- [ ] A-8c. MatchNetworkState: 초기값 + OnValueChanged (Host + Client 모두)
- [ ] A-8d. Relay allocation: playerCount - 1
- [ ] A-8e. 1v1 회귀 (GameScene + MatchNetworkState 정상)
- [ ] A-8f. ConnectionApproval 거절 (중복 ParticipantId, late join, 잘못된 token)
- [ ] A-8g. spawn = SpawnPoint[seat]
- [ ] A-8h. start gate
- [ ] A-8i. disconnect → PlayerState 서버 유지 + RemoveOwnership 확인
- [ ] A-8j. reconnect → ChangeOwnership + NV 자동 동기화 확인
- [ ] A-8k. token 전달: Lobby PlayerData → ConnectionData

---

### Phase B0 — LifeState + DeathService + DamageSource

#### B0-1. LifeState + Roster 분리

- [ ] B0-1a. `LifeState { Alive, Ghost }` NV (PlayerState)
- [ ] B0-1b. Roster = 연결 (Connected/Disconnected/TimedOut), LifeState = 게임 (Alive/Ghost)
- [ ] B0-1c. **상태 조합 정의** (B3 해결):
  ```
  TurnEligible:         Connected && Alive → 턴 참가 (아이템 선택, Ready)
  GhostEligible:        Connected && Ghost → Ghost 스킬 사용
  CountsAsAliveForRoundEnd: (Connected || Disconnected) && Alive
      → disconnect 유예 중에도 "살아있는 것으로 카운트"
      → 라운드 종료 판정: CountsAsAliveForRoundEnd ≤ 1 or 전원 Ghost
  AutoReady:            Disconnected && Alive → 행동 없이 자동 준비
  ```
  - ⚠ **TimedOut은 어느 카운트에도 포함 안 됨** → timeout 시 즉시 생존자 재평가

#### B0-2. AuthoritativeDeathService

- [ ] B0-2a. TryKill: guard Alive → Ghost → CancelTurnParticipation → ClearAll → guard `Origin!=Natural && KillerSeat<count` → KillScores++
- [ ] B0-2b. IPlayerTurnCancellation 기본 구현
- [ ] B0-2c. 사망 감지: deathQueue → batch 후 FlushDeathQueue → EvaluateRoundMatch
  - Ghost Frost (PrepPhase 즉시): mutation 직후 즉시 FlushDeathQueue + round check
- [ ] B0-2d. DamageSource 정규화:
  ```
  아이템 공격: { attackerSeat, Item }
  선풍기 (ChillAura 없음): { 255, Natural }
  선풍기 (ChillAura 적용 중): { ghostSeat, GhostChill }
  Ghost FrostStrike: { ghostSeat, GhostFrost }
  지연 효과: { sourceSeat, DelayedEffect }
  규칙: KillerSeat=255 → 반드시 Natural
  ```
- [ ] B0-2e. ScheduledEffect.SourceSeat 추가, Schedule() 시그니처 확장

#### B0-3. 킬 스코어 + 승리 + 라운드 리셋

- [ ] B0-3a. KillScores (MatchNetworkState 소유)
- [ ] B0-3b. SeatSnapshot +killScore, +lifeState
- [ ] B0-3c. 공동 승리 WinnerMask (Shell 계산)
- [ ] B0-3d. 라운드 종료: **CountsAsAliveForRoundEnd** ≤ 1 or 전원 Ghost (B3)
- [ ] B0-3e. 라운드 리셋:
  ```
  1. 전원 Temperature=37° + CurrentLifeState=Alive
  2. 기본 4종 재구성 (Multi 바람막이: IsUnlimited=false, RemainingUses=1)
  3. 랜덤 Rule.InitialRandomItems (1v1:4, Multi:2)
  4. Threshold 초기화
  5. 킬 스코어 누적 (리셋 안 함)
  6. Modifier 전체 초기화
  7. Ghost cooldown 전체 클리어
  8. Active debuff dictionary 클리어
  9. ScheduledEffect 전체 클리어
  10. FanSpeed/IsFanUpgraded/IsBasicBlocked 초기화
  ```

#### B0-4. 통합 테스트

- [ ] B0-4a. 0° → Ghost 전환
- [ ] B0-4b. 중복 TryKill 방지
- [ ] B0-4c. 킬 스코어 + batch 공동 승리
- [ ] B0-4d. 라운드 리셋 전체 (기본4종+랜덤+modifier+cooldown+fanspeed)
- [ ] B0-4e. Natural source → 킬 없음 (OOB 방지)
- [ ] B0-4f. 1v1 회귀: DeathRule.RoundEnd 분기
- [ ] B0-4g. ScheduledEffect.SourceSeat → DelayedEffect 킬 귀속
- [ ] B0-4h. **Disconnect 유예 중 라운드 미종료** (CountsAsAliveForRoundEnd)
- [ ] B0-4i. **Timeout 후 즉시 생존자 재평가**

---

### Phase B — ActionIntent + Value Snapshots + N인 Resolver + DTO + Balance

#### B-1. ActionIntent

- [ ] B-1a. `ActionIntent` readonly struct: { SourceSeat, SlotIndex, ItemId, TargetSeat(255=NoTarget), ReadyTimestamp }
- [ ] B-1b. QueuedAction.TargetSeat 추가
- [ ] B-1c. PlayerState._pendingIntent 보존 (미니게임 경유, 재검증)
- [ ] B-1d. IPlayerTurnCancellation 확장
- [ ] B-1e. ILocalPlayerCommands + LocalPlayerCommandAdapter 확장

#### B-2. 아이템 타겟

- [ ] B-2a. TargetMode enum: Self, SingleTarget
- [ ] B-2b. 21종: SingleTarget **13종** (타로 포함), Self 8종
- [ ] B-2c. 드래그-타겟 UI (1v1 자동 타겟)
- [ ] B-2d. 서버 검증

#### B-3. Value-Type Snapshots (P1 해결)

> **계약**: factory가 모든 배열을 **defensive copy**로 생성. Resolver는 입력 배열을 **변경하지 않음** (readonly 참조 + 문서화된 불변식).

- [ ] B-3a. `GameModeRuleSnapshot` readonly struct — IGameModeRule 전체 value copy
  - Factory: `GameModeRuleSnapshot.From(IGameModeRule rule)` — 배열 필드는 `.ToArray()` copy
- [ ] B-3b. `ItemRuleSnapshot` readonly struct:
  ```
  { short ItemId, ItemCategory Category, TargetMode TargetMode, DamageFilter AttackFilter,
    float BaseDamage, float DefenseReduction, bool IsDefense,
    bool WritesFanSpeed, float FanSpeedValue, bool WritesTargetFanSpeed, float TargetFanSpeedValue,
    bool BlocksTargetBasics, bool GrantsExtraAction, bool RevealsOpponent, bool NeutralizesTarget,
    InventoryMutationType InventoryAction,
    bool HasScheduledEffect, EffectType ScheduledType, float ScheduledValue, int ScheduledDelay }
  ```
  - Factory: `ItemRuleSnapshot.From(ItemDataSO so)` — SO의 ComputeEffect 결과를 정적 값으로 캡처
- [ ] B-3c. `InventorySnapshot` readonly struct (seat별): `{ byte SeatIndex, SlotSnapshot[] Slots }`
  - `SlotSnapshot { short ItemId, bool IsUnlimited, byte RemainingUses }`
  - Factory: defensive `.ToArray()` copy
- [ ] B-3d. `ScheduledEffectSnapshot` readonly struct: `{ byte TargetSeat, byte SourceSeat, EffectType Type, float Value, int TurnsRemaining }`

#### B-4. Functional Core (B4 해결 — 완전한 delta)

- [ ] B-4a. `MatchCombatSnapshot` — **순수 value-type만, defensive copy**:
  ```
  float[] TemperaturesAtTurnStart      // seat-indexed, copy
  float[] CurrentTemperatures          // seat-indexed, copy
  PlayerModifiers[] Modifiers          // value struct copy
  LifeState[] LifeStates               // copy
  InventorySnapshot[] Inventories
  ScheduledEffectSnapshot[] ScheduledEffects
  int[] CurrentKillScores              // copy (WinnerMask는 Shell이지만 Deathmatch grant 판단용)
  EnvironmentType Environment
  GameModeRuleSnapshot Rule
  ItemRuleSnapshot[] ItemRules         // copy
  ```
- [ ] B-4b. `MultiCombatResolution` — **전체 state delta** (B4):
  ```
  CombatEvent[] OrderedEvents          // ≤ MAX_EVENTS(16)
  float[] TemperatureDeltas            // seat-indexed
  DamageSource[] LastDamageSources     // seat-indexed
  byte DeadMask
  int[] ActionOrder
  short[] MainItemIds                  // seat-indexed
  short[] SubItemIds                   // seat-indexed
  
  // 완전한 state deltas:
  InventoryDelta[] InventoryChanges    // 아이템 소비, Cat reroll, Claw steal
  PlayerStateDelta[] PlayerStateChanges // ← 신규: FanSpeed, IsFanUpgraded, IsBasicBlocked 등 NV write
  ModifierDelta[] ModifierChanges      // ActiveDefense, ActionNeutralized, HasExtraAction, OpponentRevealed
  ScheduledEffectDelta[] NewScheduled  // 지연 효과 예약 (with SourceSeat)
  ```
  - `PlayerStateDelta`:
    ```
    struct PlayerStateDelta
    {
        byte SeatIndex;
        float? NewFanSpeed;            // WriteUserFanSpeed/WriteTargetFanSpeed
        bool? IsFanUpgraded;           // 현행 코드에 존재 시
        bool? IsBasicBlocked;          // BlockTargetBasics
    }
    ```
  - ⚠ **WinnerMask, ResultSequence 미포함** → Shell 결정
- [ ] B-4c. `CombatResolver.ResolveMulti(MatchCombatSnapshot, ActionIntent[])` → `MultiCombatResolution`:
  - **NV/MonoBehaviour/SO runtime instance 참조 없음** — ItemRuleSnapshot으로 효과 계산
  - 행동 순서: 방어 우선 → Ready 타임스탬프 → 온도 낮은 순 → **seat index** (최종 tie-break)
  - 중간 사망: 매 행동 후 0° 체크, 사망자 행동 취소
  - Target이 실행 전 Ghost → 행동 **취소** (재지정 안 함)
  - 기존 `Resolve(p1, p2)` 완전 보존
- [ ] B-4d. **Applicator Shell**:
  ```
  1. resolution.TemperatureDeltas → PlayerState.Temperature.Value write
  2. resolution.PlayerStateChanges → FanSpeed/IsBasicBlocked NV write
  3. resolution.InventoryChanges → PlayerInventory 반영
  4. resolution.ModifierChanges → PlayerModifiers 반영
  5. resolution.NewScheduled → BuffDebuffSystem 등록 (with SourceSeat)
  6. FlushDeathQueue() → TryKill
  7. KillScores 반영 → WinnerMask 계산 (Shell)
  8. ResultSequence = ++_serverSequenceCounter (Shell)
  9. CombatResolutionBatchNetData 생성 + ClientRpc
  ```

#### B-5. Network DTO — 수동 직렬화 (B5 해결)

- [ ] B-5a. `CombatEventNetData`:
  ```csharp
  struct CombatEventNetData : INetworkSerializable
  {
      public byte ActorSeat;     // 1
      public byte TargetSeat;    // 1
      public short ItemId;       // 2
      public byte EventType;     // 1
      public float Value;        // 4
      public byte Flags;         // 1
      // = 10 bytes per event
      
      public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
      {
          s.SerializeValue(ref ActorSeat);
          s.SerializeValue(ref TargetSeat);
          s.SerializeValue(ref ItemId);
          s.SerializeValue(ref EventType);
          s.SerializeValue(ref Value);
          s.SerializeValue(ref Flags);
      }
  }
  ```
- [ ] B-5b. **이벤트 생성 불변식**: 1 action → 최대 1 event. 4인 최대: 4 main + 4 sub + 4 defense + 4 death = 16
- [ ] B-5c. `CombatResolutionBatchNetData : INetworkSerializable` — **수동 요소별 직렬화**:
  ```csharp
  public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
  {
      s.SerializeValue(ref SeatCount);
      s.SerializeValue(ref EventCount);
      
      // EventCount 검증 (읽기 시)
      if (s.IsReader && EventCount > 16)
          throw new System.InvalidOperationException("EventCount exceeds 16");
      
      // 요소별 수동 직렬화 (FixedList512Bytes 직접 직렬화 불가)
      for (int i = 0; i < EventCount; i++)
          Events[i].NetworkSerialize(s);
      
      // 나머지 고정 필드
      for (int i = 0; i < 4; i++) s.SerializeValue(ref TempBefore[i]);
      for (int i = 0; i < 4; i++) s.SerializeValue(ref TempAfter[i]);
      // ... 등
      s.SerializeValue(ref DeadMask);
      s.SerializeValue(ref WinnerMask);
      s.SerializeValue(ref FirstActionSeat);
      s.SerializeValue(ref ResultSequence);
  }
  ```
  - Events 저장: `CombatEventNetData[] Events = new CombatEventNetData[16]` (고정 배열, EventCount로 유효 범위)
  - **FixedList512Bytes 사용하지 않음** — NGO 2.11.2 BufferSerializer에 직접 오버로드 없으므로
  - 총 크기: 2(header) + 10×16(worst) + 4×8(temps) + 2×8(items) + 4(masks) + 4(seq) = ~218 bytes (1200 MTU 이내)
- [ ] B-5d. Overflow → state 적용 전 abort → match 강제 종료 (재시도 안 함)
- [ ] B-5e. 1v1: 기존 Resolve() + CombatResultData 보존

#### B-6. Balance

- [ ] B-6a. 모든 랜덤 경로에 MaxRandomItems cap + GetRandomSlotCount() 헬퍼
- [ ] B-6b. 바람막이 Multi 1-use: InitializeBasicItems에 IGameModeRule → `IsUnlimited=false, RemainingUses=1`
- [ ] B-6c. 타로: Multi ItemDropTable 생성 시 필터링 (SO weight 변경 안 함)
- [ ] B-6d. Deathmatch: `min(Rule.DeathmatchGrantCount, cap - currentRandom)` (라운드 1회)
- [ ] B-6e. 동시 Ready = 같은 FixedUpdate frame

#### B-7. TurnManager N인

- [ ] B-7a. 동적 배열
- [ ] B-7b. PrepPhase N명 루프
- [ ] B-7c. AttackPhase: Multi → snapshot + ResolveMulti + Applicator, 1v1 → 기존
- [ ] B-7d. BuffDebuffSystem N인 확장

#### B-8. 통합 테스트

- [ ] B-8a. 3인 드래그-타겟 → 전투
- [ ] B-8b. 미니게임 타겟 보존
- [ ] B-8c. 중간 사망 → 행동 취소
- [ ] B-8d. Target Ghost → 취소
- [ ] B-8e. 5킬 + 공동 승리 (WinnerMask Shell)
- [ ] B-8f. 밸런스: 바람막이 1-use, 타로 필터, 초기 2개, Deathmatch cap
- [ ] B-8g. DTO: 수동 직렬화 정상 + MTU
- [ ] B-8h. 1v1 회귀
- [ ] B-8i. Host + remote 3 + 모든 seat 관점
- [ ] B-8j. PlayerStateDelta: FanSpeed/IsBasicBlocked 정상 적용

---

### Phase C — N인 VFX + 환경

- [ ] C-1a. PresentationBarrier (이미 N인)
- [ ] C-1b. CombatVFXManager visual slot 매핑
- [ ] C-1c. 순차 연출 ActionOrder
- [ ] C-1d. 수동 직렬화 DTO 소비
- [ ] C-2a. 사망/Ghost 연출 (alpha 0.4)
- [ ] C-2b. 라운드 종료 연출 + 킬 스코어
- [ ] C-3a. 잼민이 N스틸
- [ ] C-3b. 앰뷸런스: 최저 1명 (동률 → seat index 최소)
- [ ] C-3c. 폭염경보: 오름차순
- [ ] C-3d. 기타: 전원 동일
- [ ] C-3e. EnvironmentRuleService + IGameModeRule
- [ ] C-4a~d. 통합 테스트

---

### Phase D — Ghost Skill + Multiplier + UI

#### D-1. PlayerModifiers + 실제 적용

- [ ] D-1a. FanSpeedMultiplier=1f, RecoveryMultiplier=1f 추가
- [ ] D-1b. ApplyFanTick: `speed = FanSpeed.Value * modifiers.FanSpeedMultiplier`
- [ ] D-1c. ApplyRecoveryTick: `rate = recoveryRate * modifiers.RecoveryMultiplier`
- [ ] D-1d. **ChillAura epoch** (P4 해결 — GAME_DESIGN 기준):
  ```
  적용: Ghost가 PrepPhase 중 사용 → 즉시 modifier 적용
  유효: 해당 Prep + Attack + Resolution 동안
  해제: **다음 PrepPhase 종료 시** (GAME_DESIGN: "until next PrepPhase end")
  ∴ 실제 지속 = 현재 턴 나머지 + 다음 턴 Prep 전체
  해제 시점: TurnManager가 다음 PrepPhase → AttackPhase 전환 직전에 ResetChillAura() 호출
  ```

#### D-2. Ghost 스킬 + 쿨다운 (P3 해결)

- [ ] D-2a. **쿨다운 저장소**: `Dictionary<(byte seat, byte skillIndex), int>` (스킬별 개별 쿨다운)
  - skillIndex: 0=FrostStrike, 1=ChillAura
  - PrepPhase 시작 시 전원 쿨다운 1 감소
  - 라운드 리셋 시 전체 클리어
- [ ] D-2b. **쿨다운 UI 복제**: `NetworkList<GhostCooldownNetData>` in MatchNetworkState
  - `{ byte Seat, byte Skill, byte RemainingTurns }`
  - owner 클라이언트가 읽어 UI 표시 (Everyone readable)
- [ ] D-2c. GhostSkillServerRpc 6단계 검증: IsServer, sender==Owner, Ghost, PrepPhase, target Eligible(Connected+Alive), cooldown==0
- [ ] D-2d. 디버프 덮어쓰기: modifier 원복 → **active debuff entry 제거** → 새 설치 + 새 entry → killerSeat 갱신
- [ ] D-2e. **Active debuff dictionary**: `Dictionary<byte targetSeat, GhostDebuffEntry>`
  - `GhostDebuffEntry { byte GhostSeat, byte SkillIndex, int AppliedTurn }`
  - **ChillAura 만료 시**: modifier 원복 **+ debuff entry 제거**
  - 라운드 리셋 시 전체 클리어
- [ ] D-2f. FrostStrike: DamageFilter.Ghost, 즉시 FlushDeathQueue, DamageSource={ghostSeat, GhostFrost}
- [ ] D-2g. ChillAura: FanSpeedMultiplier=2, RecoveryMultiplier=0.5, DamageSource 갱신

#### D-3. Ghost UI

- [ ] D-3a. Ghost PrepPhase UI: 스킬 버튼 2개
- [ ] D-3b. 타겟 선택: Eligible 생존자 클릭
- [ ] D-3c. 쿨다운 오버레이 (NetworkList 읽기)
- [ ] D-3d. PrepPhase 자유, Ready 불필요
- [ ] D-3e. Ghost 시야에 전원 상태

#### D-4. 통합 테스트

- [ ] D-4a. FrostStrike → PrepPhase 즉시 사망 → FlushDeathQueue → 킬 귀속
- [ ] D-4b. ChillAura → fan ×2 확인 → 간접 킬
- [ ] D-4c. Recovery ×0.5 확인
- [ ] D-4d. 덮어쓰기: 2 Ghost 동일 타겟
- [ ] D-4e. 라운드 종료 → 전원 Alive + 전체 초기화
- [ ] D-4f. Ghost 5킬 승리
- [ ] D-4g. **ChillAura 만료**: 다음 PrepPhase 종료 시 해제 + debuff entry 제거
- [ ] D-4h. **쿨다운 UI**: FrostStrike/ChillAura 개별 표시

---

### Phase E — Solo/Bot

#### E-1. Solo 인프라

- [ ] E-1a. Solo 버튼 → 난이도
- [ ] E-1b. Bot Standalone build 생성
- [ ] E-1c. 실행: `-batchmode -nographics`
- [ ] E-1d. **포트 조회** (B6 해결):
  ```csharp
  var transport = (UnityTransport)NetworkManager.Singleton.NetworkConfig.NetworkTransport;
  transport.SetConnectionData("127.0.0.1", 0);  // port 0 = OS ephemeral
  NetworkManager.Singleton.StartHost();
  ushort actualPort = transport.GetLocalEndpoint().Port;  // 실제 할당 포트
  // Bot 프로세스에 --port actualPort 전달
  ```
- [ ] E-1e. BotBootstrap.cs: command-line → Transport → Client 연결
- [ ] E-1f. Connection approval: session-token, seat 1
- [ ] E-1g. 프로세스 관리: Process.Start → 10초 timeout, ShutdownRpc → 3초 → Kill
- [ ] E-1h. Auth 미사용, Lobby 미경유, GameScene 공유

#### E-2. Bot AI

- [ ] E-2a. IBotBrain BT
- [ ] E-2b. BotPlayer: PrepPhase → BT → SelectItemServerRpc → delay → ReadyServerRpc
- [ ] E-2c. 난이도 3종
- [ ] E-2d. Ready 타이밍
- [ ] E-2e. 미니게임 IsBot 자동 성공

#### E-3. 외형/이름

- [ ] E-3a. Bot 이름
- [ ] E-3b. 기본 캐릭터

#### E-4. 통합 테스트

- [ ] E-4a. Solo → Host → Bot 연결
- [ ] E-4b. Bot 아이템+Ready
- [ ] E-4c. Bo3 정상
- [ ] E-4d. Bot 크래시 감지
- [ ] E-4e. 매치 종료 → 프로세스 정리
- [ ] E-4f. token 거절

---

### Phase F — 미해결 질문 + 정리

- [ ] F-1a~h. Q10, Q16, Q21~Q23, Q24, Q26, Q27
- [ ] F-2a~e. PLAN_019 역사화, PLAN_021 트리, GAME_DESIGN, ACTIVE_CONTEXT, CHANGES

---

### 완료 불변식 (14개)

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

### 검증 게이트

1. Host + remote 3
2. 3인/4인 모두
3. 2번째 라운드 이상 (리셋)
4. Prep/Attack/VFX 중 disconnect
5. 모든 local seat 관점 UI
6. 1v1 회귀
7. spawn = SpawnPoint[seat]
8. reconnect → 같은 seat + 같은 PlayerState 오브젝트 + NV 자동 동기화
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
| SO in Resolver | value-type snapshot + defensive copy |
| DTO 직렬화 | 수동 요소별 (FixedList 미사용) |
| {255,Fan} OOB | Natural 정규화 + guard |
| Roster/LifeState 중복 | 연결=Roster, 게임=LifeState |
| Windbreaker unlimited | Multi: IsUnlimited=false |
| Tarot Self 회귀 | SingleTarget |
| Multiplier 미적용 | ApplyFanTick/RecoveryTick 수정 |
| Reconnect NV 소실 | PlayerState 서버 유지 + ChangeOwnership |
| Disconnect grace vs round end | CountsAsAliveForRoundEnd 분리 |
| Resolution delta 불완전 | PlayerStateDelta (FanSpeed/IsBasicBlocked) |
| Token 전달 | Lobby PlayerData → ConnectionData |
| ChillAura epoch | GAME_DESIGN 기준: 다음 PrepPhase 종료 시 |

---

## END OF PLAN TEXT

---

## Review Criteria

Rate this plan on a 1–10 scale. For each item below, flag BLOCKER (prevents implementation), PARTIAL (works but has a gap), or PASS.

### Category Checklist

1. **NGO 2.11.2 API correctness**
   - NetworkVariable/NetworkList lifecycle (init, subscribe, unsubscribe, dispose)
   - INetworkSerializable — manual serialization (no FixedList direct serialize)
   - Rpc direction (SendTo.Server for client→host, appropriate server→client target)
   - NetworkObject on same GO as NetworkBehaviour
   - Host reads NV too (not excluded by IsClient && !IsServer guard)
   - ChangeOwnership / RemoveOwnership for reconnect

2. **Authority model**
   - All game state mutations server-only
   - Client sends intent only via Rpc
   - No client-side NV writes

3. **Reconnect / Disconnect**
   - PlayerState NOT destroyed on disconnect (server retains ownership)
   - NV state preserved across disconnect/reconnect
   - Session token delivery path (Lobby PlayerData → ConnectionData)
   - Grace period vs round end (CountsAsAliveForRoundEnd vs TurnEligible)
   - Timeout → cleanup → survivor re-evaluation

4. **Combat resolution (Functional Core / Imperative Shell)**
   - Value-type snapshots (no SO/NV/MonoBehaviour in resolver)
   - Defensive copy contract for arrays
   - Complete delta output (Temperature, FanSpeed, IsBasicBlocked, IsFanUpgraded, Inventory, Modifiers, ScheduledEffects)
   - Shell applies deltas → NV writes
   - WinnerMask/ResultSequence computed by Shell only

5. **DTO / Serialization**
   - Manual element-by-element (no FixedList512Bytes direct)
   - EventCount validation 0..16
   - Size within MTU (1200 bytes)
   - 1v1 CombatResultData untouched

6. **Ghost system**
   - LifeState separate from Roster ConnectionState
   - FrostStrike: immediate death check in PrepPhase
   - ChillAura: multipliers (FanSpeed×2, Recovery×0.5), epoch = "until next PrepPhase end"
   - Cooldown: Dictionary<(seat, skill), int> — per-skill individual
   - Cooldown UI: NetworkList<GhostCooldownNetData>
   - DamageSource normalization for ghost kills
   - Active debuff dictionary: cleaned on ChillAura expiry and round reset

7. **Balance / Item correctness**
   - Windbreaker: Multi IsUnlimited=false, RemainingUses=1
   - Tarot: SingleTarget (not Self), Multi filter from drop table
   - InitialRandomItems: 1v1=4, Multi=2
   - Deathmatch grant cap

8. **Scene / Spawn / Roster**
   - Separate scenes (GameScene 1v1, GameScene_Multi)
   - Seat-based spawn (not clientId-based)
   - MatchNetworkState on both scenes
   - SessionParticipantTable (DDOL) vs MatchRoster (match scope)

9. **1v1 regression safety**
   - Existing Resolve(p1,p2) untouched
   - CombatResultData untouched
   - GameScene only adds MatchNetworkState

10. **Solo / Bot**
    - Port discovery: GetLocalEndpoint().Port (not ServerClientId)
    - localhost transport, no Auth/Lobby
    - Process lifecycle

11. **Completeness**
    - All 179 tasks cover the full feature set
    - No orphaned references or missing definitions
    - Phase dependencies are acyclic
    - Invariants are testable

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
