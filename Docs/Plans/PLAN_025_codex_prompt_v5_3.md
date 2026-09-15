# PLAN_025 v5.3 Verification Prompt — Codex Review #8

You are reviewing a **Unity 6 (6000.3.11f1) multiplayer game implementation plan** for architecture correctness.

## Project Context

- **Engine:** Unity 6 (6000.3.11f1)
- **Network:** NGO (Netcode for GameObjects) 2.11.2, Unity Relay (DTLS), Host-authoritative
- **Game:** 2.5D turn-based temperature deathmatch. Current: 1v1 Bo3. Adding: 3~4 player multi mode, solo/bot, ghost system.
- **Authority model:** Server-authoritative — all game state mutations on Host/Server. Clients send action selection only via Rpc.
- **NetworkVariable:** Only server/host may write `.Value`. Client writes cause silent failure.

## Key Source Code Facts (verified by grep)

These are **verified facts from the actual codebase**, not assumptions:

1. **`AttackItemDataSO.cs:13`**: `public bool EqualizeToUserTemp;` — Equalize sets target.temp = user.temp (NOT average)
2. **`RecoveryItemDataSO.cs:9-14`**: `public float[] HealPerUse = { 7f };` — Recovery uses array lookup: `HealPerUse[Clamp(MaxUses - RemainingUses, 0, len-1)]` (NOT baseHeal * remaining)
3. **`SpecialItemDataSO.cs:17`**: `if (!ctx.Target.IsReady.Value)` — Tarot checks `IsReady.Value` state (NOT intent existence)
4. **`NetworkTime.cs:65`**: `public int Tick => m_CachedTick;` — Tick is `int`, not `uint`
5. **`NetworkConnectionManager.cs:1362`**: `if (!playerObject.DontDestroyWithOwner)` — NGO despawns PlayerObject BEFORE OnClientDisconnected when DontDestroyWithOwner=false
6. **`NetworkObject.cs:1334`**: `public bool DontDestroyWithOwner;` — boolean field
7. **`Player.prefab`**: Currently `DontDestroyWithOwner=0` (false) — must be changed to true
8. **`MatchCompositionRoot.cs:7`**: `public class MatchCompositionRoot : MonoBehaviour` — NOT NetworkBehaviour, cannot own NetworkVariables
9. **`PlayerSpawnManager.cs:223`**: Current `DespawnPlayerForClient` destroys PlayerState — NV state lost
10. **`ItemEffectApplicator.cs`**: Writes `IsBasicBlocked.Value`, `FanSpeed.Value`, `TargetFanSpeed.Value` — resolution delta must capture all
11. **`ItemEffectOutcome.cs`**: Full struct with all NV-write fields
12. **`BuffDebuffSystem.cs`**: `ScheduledEffect` has NO `SourceSeat` field, `ProcessTurnStart(PlayerState p1, PlayerState p2)` is 2-player only
13. **`TemperatureSystem.cs:37,46`**: No multiplier support in `ApplyFanTick`/`ApplyRecoveryTick`
14. **`PlayerInventory.cs:52`**: `if (slot.IsUnlimited) return;` — Multi windbreaker must be `IsUnlimited=false`
15. **Unity Lobby permissions**: Each player can only modify own PlayerData. Host can READ Private data but NOT write others'.

## Previous Review History

| Review | Score | Key Issues Fixed |
|--------|-------|-----------------|
| #1 | 4.0/10 | 11 blockers: Phase order, Roster, ActionIntent, DTO |
| #2 | 6.0/10 | 5 blockers: B0↔B cycle, domain violation, MatchConfig NV |
| #3 | 6.0/10 | 8B+6P: MCR NV→MatchNetworkState, SO→snapshot, scope |
| #4 | 6.0/10 | 7B+6P: PlayerState preservation, grace vs round end, delta |
| #5 | 6.0/10 | 5B+3P: SeatRuntimeState, Lobby permissions, ItemEffectRuleSnapshot |
| #6 | 7.3/10 | 3B+4P: DontDestroyWithOwner, ISeatStateAccessor, formula corrections |
| #7 | 7.9/10 | 3B+3P: ISeatStateAccessor full NV coverage, MatchCombatSnapshot.IsReady, 2-stage token gate |

## Review #7 Fixes Applied in v5.3

| # | Issue | Fix |
|---|-------|-----|
| B1 | ISeatStateAccessor missing IsFanActive/IsFanUpgraded/IsBasicBlocked/Inventory + offline TryKill/RoundReset + PendingEffects duplication | ISeatStateAccessor **fully expanded** (all NVs + Inventory). `TryKill(byte seat, DamageSource)` seat-based via accessor. `RoundResetAll` via accessor. PendingEffects **removed** from SeatRuntimeState |
| B2 | MatchCombatSnapshot missing `bool[] IsReady` → Tarot Resolver won't compile | Added `bool[] IsReady` seat-indexed defensive copy to B-4a |
| B3 | Single token gate → client connects before Host polls token → rejected | **2-stage gate**: (1) Lobby gate: LobbyPlayerCount + TokenReadyCount == Required → relay code shared. (2) NGO gate: ApprovedNgoClientCount == Required → scene transition |
| P1 | Reconnect hydrate: SyncedPlayerIndex set first → half-restored PlayerState visible to TurnManager | **Hydrate commit order**: all NVs first → SyncedPlayerIndex last. Optional IsHydrated latch |
| P2 | Prompt not truly verbatim (929 vs 941 lines) | Plan file mechanically inserted (not manual transcription) |
| P3 | Invariant title says "16개" but actually 21 (now 24) | Fixed to "24개" |

---

## YOUR TASK

Review the **FULL PLAN** below (mechanically copied from the plan file). Verify:

1. **NGO 2.11.2 API correctness**: NetworkVariable ownership/permissions, Rpc patterns, NetworkList<T> constraints, BufferSerializer, DontDestroyWithOwner behavior
2. **Authority model**: All state mutations server-side, no client NV writes, proper Rpc direction
3. **Data flow consistency**: Snapshot→Resolver→Resolution→Applicator Shell pipeline, no NV/MonoBehaviour references in Resolver
4. **Disconnect/reconnect correctness**: DontDestroyWithOwner=true timing, ISeatStateAccessor for ALL operations (TryKill, RoundReset, fan tick, etc.), hydrate commit order
5. **Formula accuracy**: Equalize (target.temp=user.temp, NOT average), Recovery (HealPerUse[] lookup), Tarot (IsReady state via MatchCombatSnapshot.IsReady[])
6. **DTO serialization**: Writer+reader validation, manual element serialization, MTU bounds
7. **Token security**: RandomNumberGenerator, 2-stage gate (Lobby gate → Relay code → NGO gate → scene), Lobby permission model
8. **Phase dependencies**: No circular deps, no forward references
9. **1v1 regression**: Existing Resolve(p1,p2) + CombatResultData preserved
10. **Completeness**: All 188 checkboxes are implementable, no missing steps
11. **ISeatStateAccessor completeness**: Covers ALL PlayerState NVs used by any system during disconnect grace
12. **ScheduledEffect non-duplication**: Only in BuffDebuffSystem central dict, not in SeatRuntimeState

## Scoring

- **BLOCKER (B)**: Will cause runtime failure, desync, or data loss. Must fix before implementation.
- **PARTIAL (P)**: Incomplete specification that could cause issues. Should fix.
- **APPROVED**: No blockers remain. Implementation-ready.

Score 0-10. Target: 9+ for APPROVED.

For each issue found, cite:
- The specific plan section (e.g., "A-3d", "B-3b")
- The NGO/Unity API or codebase fact that contradicts it
- The concrete failure scenario

---

## FULL PLAN TEXT (188 checkboxes — mechanically copied from plan file)

# PLAN_025 — Multi Mode Full Implementation (v5.3 — Codex Review #7 반영)

> **Status:** 📋 Planning (v5.2 — Review #1~#7 전체 반영, 재검증 대기)
> **Created:** 2026-09-02 | **Revised:** 2026-09-02
> **Dependencies:** GAME_DESIGN.md, PLAN_018, PLAN_020
> **Absorbs:** PLAN_019 → 역사 문서 전환
> **Scope:** GameScene_Multi 씬 + GameScene wiring 변경(MatchNetworkState 추가) → **1v1 동작 보존**
> **Will NOT touch:** 1v1 Resolve()/CombatResultData/전투 로직, PLAN_024 Rematch (1v1 전용)

---

## Codex Review #5 핵심 수정 (v5 → v5.1)

| # | 지적 | v5.1 반영 |
|---|------|---------|
| B1 | DontDestroyWithOwner=false + ChangeOwnership PlayerObject 미등록 | **SeatRuntimeState 방식**: disconnect 시 서버가 NV 스냅샷→struct 보존, Despawn+Destroy OK. reconnect 시 새 SpawnAsPlayerObject + hydrate. PlayerObject lookup 자연 해결 |
| B2 | Host가 타인 PlayerData 수정 불가 (Unity Lobby 권한) | **클라이언트 self-write**: 각 클라이언트가 token 생성 → 자기 Private PlayerData에 기록 → Host가 Private 읽기 → SessionParticipantTable 저장 |
| B3 | ItemRuleSnapshot.From() 런타임 의존 효과 캡처 불가 | **ItemEffectRuleSnapshot**: 결과 아닌 계산 규칙 캡처. ItemEffectKind discriminator + 파라미터. Resolver가 runtime snapshot으로 결과 계산 |
| B4 | DTO reader 배열 null 접근 | reader-side 배열 초기화 `??= new` + SeatCount≤4, EventCount≤16 양측 검증 |
| B5 | GhostCooldownNetData: unmanaged+IEquatable 계약 없음 | 완전한 struct 계약 + MatchNetworkState 내 생성/구독/해제/Dispose/Clear 수명주기 명시 |
| P1 | auto-ready vs pending intent 충돌 | disconnect 시 **pending intent 즉시 취소** (auto-ready = 무행동) |
| P2 | ReadyTimestamp float, RPC FixedUpdate 보장 없음 | **ReadyServerTick** (int, NetworkManager.ServerTime.Tick) 사용 — v5.2: uint→int 수정 |
| P3 | IsFanUpgraded Applicator 누락 | Applicator Shell step 2에 IsFanUpgraded NV write 명시 |

## Codex Review #7 핵심 수정 (v5.2 → v5.3)

| # | 지적 | v5.3 반영 |
|---|------|---------|
| B1 | ISeatStateAccessor에 IsFanActive/IsFanUpgraded/IsBasicBlocked 누락 + offline TryKill/RoundReset 미대응 + PendingEffects 중복 | ISeatStateAccessor **완전 확장** (전체 NV 커버). `TryKill(byte seat, DamageSource)` seat 기반으로 변경. `RoundResetAll(ISeatStateAccessor)` 경유. SeatRuntimeState에서 PendingEffects 필드+reconnect 재등록 **제거** |
| B2 | MatchCombatSnapshot에 IsReady[] 미정의 → Tarot Resolver 컴파일 실패 | `bool[] IsReady` 추가. Factory: `PlayerState.IsReady.Value` seat-indexed copy |
| B3 | Token gate가 단일 시점 → Relay 코드 수신한 클라이언트가 token polling 전에 연결하여 거절 | **2단계 gate**: (1) Lobby gate: `LobbyPlayerCount==Required && TokenReadyCount==Required` → Relay 코드 공유. (2) NGO gate: `ApprovedNgoClientCount==Required` → 씬 전환 |
| P1 | reconnect hydrate 중 SyncedPlayerIndex 먼저 설정 시 반쯤 복원된 PlayerState 노출 | **hydrate commit 순서**: Temperature→Inventory→Modifier→... → 마지막에 SyncedPlayerIndex 설정. 또는 `IsHydrated` latch |
| P2 | 프롬프트 "원문 그대로" 표현이지만 929줄 vs 941줄 | 프롬프트 생성 시 **plan 파일을 기계적 삽입** |
| P3 | 불변식 제목 "16개" → 실제 21개 | 제목 수정 |

## Codex Review #6 핵심 수정 (v5.1 → v5.2)

| # | 지적 | v5.2 반영 |
|---|------|---------|
| B1 | DontDestroyWithOwner=false → OnClientDisconnected 전에 NGO가 PlayerObject despawn | **DontDestroyWithOwner=true** Player.prefab 설정. disconnect 시 NGO가 서버 소유로 전환 → OnClientDisconnected에서 **라이브 PlayerState**로 SeatRuntimeState 추출 → 명시적 Despawn+Destroy |
| B2 | disconnect grace 동안 ScheduledEffect/환경/Ghost가 빈 seat 타겟 불가 | **ISeatStateAccessor** 인터페이스: PlayerState 존재→NV 읽기/쓰기, 미존재→SeatRuntimeState 읽기/쓰기. BuffDebuffSystem seat-keyed 중앙 관리 |
| B3 | ItemEffectRuleSnapshot 수식 오류: Equalize≠평균, Recovery≠baseHeal*remaining, Tarot≠intent | **Equalize**: `EqualizeToUserTemp` flag→Resolver가 target.temp=user.temp. **Recovery**: `HealPerUse[]`+`MaxUses`→Resolver가 `HealPerUse[MaxUses-RemainingUses]`. **Tarot**: `RequiresTargetReady`→Resolver가 `IsReady` 상태 확인 |
| P1 | ReadyServerTick uint→int | `NetworkTime.Tick`은 `int` (NGO 소스 확인). `ReadyServerTick` 타입 `int`로 수정 |
| P2 | DTO writer-side 검증 누락 | Writer: `Events!=null && EventCount<=Events.Length`, 모든 배열 null 체크 |
| P3 | Token ready gate + Guid 대신 암호학적 랜덤 | `ConnectedCount==RequiredPlayerCount && TokenReadyCount==RequiredPlayerCount`. `RandomNumberGenerator.GetBytes(32)` 사용 |
| P4 | Codex prompt에 축약 69개 → 실제 180개 | prompt 파일에 전체 plan 원문 포함 |

## 이전 리뷰 수정 이력 (v1~v5 → v5.1)

<details>
<summary>Review #1~#5 수정 사항 (접기)</summary>

| Review | Score | 주요 수정 |
|--------|-------|-----------|
| #1 (4.0) | 11 blockers | Phase 순서, Roster, ActionIntent, DTO 설계 |
| #2 (6.0) | 5 blockers | B0↔B 순환, domain violation, MatchConfig NV, Roster reconnect |
| #3 (6.0) | 8B+6P | MCR NV불가→MatchNetworkState, SO→value snapshot, Session/Match scope, seat spawn |
| #4 (6.0) | 7B+6P | PlayerState 보존, MatchNetworkState lifecycle, grace vs round end, PlayerStateDelta, 수동 직렬화, Solo port, token, ChillAura epoch |

</details>

---

## Overview

1v1과 Multi는 **분리 씬**. `GameScene` (1v1 Bo3)의 전투 로직은 보존하고, 양쪽 씬에 `MatchNetworkState` NetworkBehaviour를 추가한다. `GameScene_Multi` (킬 기반 5킬 승리)를 신규 구축. PLAN_019 핵심 계약 흡수.

### 씬 구조

| Build Index | 씬 | 용도 |
|---|---|---|
| 0 | `LobbyScene` | 로비 (공통) |
| 1 | `GameScene` | 1v1 Bo3 (현행 + MatchNetworkState 추가) |
| 2 | `GameScene_Multi` | 3~4인 다인전 (5킬) |
| 3 | `GameScene_Solo` (또는 GameScene 공유) | 솔로 봇전 (TBD) |

### 디자인 패턴

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

### Phase 의존성 (v5 확정)

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

## Phase A — MatchConfig + MatchNetworkState + Roster + Spawn + 씬 + HUD

### A-1. GameMode + MatchConfig

- [ ] A-1a. `NetworkConstants.GameMode` enum: `None=0, OneVsOne=1, Multi=2, Solo=3`
- [ ] A-1b. `IGameModeRule` interface (12개 필드)
- [ ] A-1c. `GameModeRuleSO : ScriptableObject, IGameModeRule` — 배열 Awake copy
- [ ] A-1d. `MatchConfig` 서버 런타임: `{ IGameModeRule Rule, int RequiredPlayerCount, GameMode Mode }`
- [ ] A-1e. `MatchConfigNetData : INetworkSerializable` — `{ byte Mode, byte RequiredPlayerCount }`

### A-2. MatchNetworkState — 완전한 NGO 수명주기 (B2 해결)

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

### A-3. Session Scope vs Match Scope Roster

- [ ] A-3a. **SessionParticipantTable** (DDOL, Coordinator 소유):
  - Host 시작 시 생성
  - Lobby participant: `{ ParticipantId, SessionToken, IsConnected }`
  - **Token 전달 경로** (B2 해결 — Unity Lobby 권한 모델 준수):
    ```
    Unity Lobby 권한: 각 플레이어는 자기 PlayerData만 수정 가능.
    Host도 타인의 PlayerData 수정 불가. 단, Host는 Private 데이터를 읽을 수 있음.
    
    1. 각 클라이언트가 Lobby Join 시 암호학적 random token 생성:
       `System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)` → Base64 문자열
       (P3 v5.2: Guid 대신 암호학적 랜덤 — 예측 불가능한 32바이트)
    2. 자신의 PlayerData에 기록 (key: "SessionToken", visibility: Private)
       → Private: 본인 + Host만 읽기 가능, 다른 멤버에게 노출 안 됨
    3. Host: LobbyManager에서 Lobby.Players[].Data["SessionToken"] 읽기
       → Host는 Private visibility 데이터 접근 가능 (Lobby API 권한)
       → 읽은 token을 SessionParticipantTable에 저장
    4. NGO 연결 시: 클라이언트가 자기가 생성한 token을 ConnectionData payload에 포함
       payload = { ParticipantId(UTF8 bytes), token(UTF8 bytes) }
    5. Reconnect 시: 동일 token 재사용 (token은 session 수명 동안 유효)
    ```
  - ConnectionApprovalCallback:
    1. payload 파싱: ParticipantId + token
    2. SessionParticipantTable 조회 + token 검증
    3. 중복 ParticipantId (이미 connected) → 거절
    4. 테이블에 없음 (late join) → 거절
- [ ] A-3b. **MatchRoster** (서버 전용, MCR 소유):
  - ConnectionState: `Connected, Disconnected, TimedOut` (연결만)
  - seat 0~3 순차 할당, PlayerIndex = seat 불변
- [ ] A-3c. **SeatRuntimeState** 서버 전용 struct (B1 해결):
  ```csharp
  // 서버 전용 — disconnect 시 PlayerState NV 스냅샷 보존용
  struct SeatRuntimeState
  {
      public byte SeatIndex;
      public float Temperature;
      public LifeState CurrentLifeState;
      public float FanSpeed;
      public bool IsFanActive;
      public bool IsFanUpgraded;
      public bool IsBasicBlocked;
      public PlayerModifiers Modifiers;         // value struct copy
      public SlotSnapshot[] InventorySlots;      // defensive copy
      // ⚠ PendingEffects 제거 (B1 v5.3): ScheduledEffect는 BuffDebuffSystem에서 seat-keyed 중앙 관리
      //   SeatRuntimeState에 중복 보관 시 reconnect 재등록으로 이중 발동 위험
      public DamageSource LastDamageSource;
      // KillScore는 MatchNetworkState 소유 → 별도 보존 불필요
  }
  ```
  - MatchRoster가 `Dictionary<byte seat, SeatRuntimeState>` 보유
- [ ] A-3d. **Player.prefab 설정 변경 (B1 v5.2)**:
  ```
  ⚠ Player.prefab NetworkObject.DontDestroyWithOwner = true (현재 false=0 → true로 변경)
  
  이유: DontDestroyWithOwner=false일 때 NGO는 OnClientDisconnected 콜백 **이전에**
  PlayerObject를 자동 despawn (NetworkConnectionManager.cs:1362 참조).
  → NV를 읽을 수 없어 SeatRuntimeState 추출 불가.
  
  DontDestroyWithOwner=true:
  - NGO가 owner disconnect 시 PlayerObject 소유권을 **서버로 자동 전환**
  - PlayerState가 살아있는 상태에서 OnClientDisconnected 콜백 실행
  - 서버가 라이브 NV를 직접 읽어 SeatRuntimeState 추출 가능
  - 추출 완료 후 서버가 명시적 Despawn + Destroy
  ```
- [ ] A-3e. **Disconnect 처리** (B1 v5.2 개선):
  ```
  1. NGO: owner disconnect → DontDestroyWithOwner=true이므로 소유권 서버 전환
  2. OnClientDisconnected 콜백 실행
  3. 서버: PlayerRegistry에서 clientId → seat → **라이브 PlayerState** 참조
  4. 라이브 PlayerState의 모든 NV를 직접 읽어 SeatRuntimeState 추출
     (Temperature.Value, CurrentLifeState.Value, FanSpeed.Value 등)
  5. MatchRoster에 SeatRuntimeState 저장
  6. PlayerState NetworkObject: 명시적 Despawn() + Destroy() (이제 서버 소유)
  7. MatchRoster seat.State → Disconnected + 30초 타이머 시작
  8. 진행 중 Barrier에서 이전 ClientId 즉시 제거
  9. **pending intent 즉시 취소** — auto-ready (무행동으로 턴 진행) [P1 해결]
  10. PlayerRegistry: seat unbind
  ```
- [ ] A-3f. **Reconnect** (B1 해결):
  ```
  1. ConnectionApproval 통과 (A-3a 토큰 검증)
  2. MatchRoster에서 ParticipantId → seat 조회
  3. seat.State == Disconnected 확인
  4. 새 Player prefab SpawnAsPlayerObject(newClientId) — 정상적 PlayerObject 등록
     → LocalClient.PlayerObject, NetworkManager 내부 lookup 자동 작동
     → InventoryPresenter, CombatVFXManager, MiniGameHub 등 기존 코드 호환
  5. SeatRuntimeState → 새 PlayerState에 hydrate (서버가 모든 NV write):
     **hydrate commit 순서 (P1 v5.3 — 반쯤 복원된 PlayerState 노출 방지)**:
     ① Temperature.Value, CurrentLifeState.Value, FanSpeed.Value,
        IsFanActive.Value, IsFanUpgraded.Value, IsBasicBlocked.Value
     ② Modifiers 복원, Inventory 복원
     ③ **SyncedPlayerIndex 마지막에 설정** → 이 시점까지 TurnManager에 미노출
     ④ IsHydrated latch (선택): Rpc 수신 전 guard
     // ⚠ PendingEffects 재등록 없음 (B1 v5.3): ScheduledEffect는 BuffDebuffSystem 중앙 보관,
     //   reconnect 시 이미 seat-keyed로 존재하므로 재등록 불필요 + 이중 발동 방지
  6. seat.ClientId = newClientId, seat.State = Connected
  7. PlayerRegistry: seat → 새 PlayerState rebind
  8. MatchRoster에서 SeatRuntimeState 제거 (hydrate 완료)
  9. ⚠ 진행 중 Barrier: 추가하지 않음 → 다음 sequence부터 참여
  ```
- [ ] A-3g. **ISeatStateAccessor 인터페이스 (B1 v5.3 — 전체 NV 커버)**:
  ```csharp
  // disconnect grace 동안 모든 시스템이 offline seat 상태에 접근 가능해야 함
  // PlayerState(NV) 또는 SeatRuntimeState(struct) 중 존재하는 쪽에 읽기/쓰기
  interface ISeatStateAccessor
  {
      // ── Temperature ──
      float GetTemperature(byte seat);
      void SetTemperature(byte seat, float value);
      
      // ── LifeState ──
      LifeState GetLifeState(byte seat);
      void SetLifeState(byte seat, LifeState value);
      
      // ── Fan ──
      float GetFanSpeed(byte seat);
      void SetFanSpeed(byte seat, float value);
      bool GetIsFanActive(byte seat);
      void SetIsFanActive(byte seat, bool value);
      bool GetIsFanUpgraded(byte seat);
      void SetIsFanUpgraded(byte seat, bool value);
      
      // ── Status ──
      bool GetIsBasicBlocked(byte seat);
      void SetIsBasicBlocked(byte seat, bool value);
      
      // ── Modifiers ──
      PlayerModifiers GetModifiers(byte seat);
      void SetModifiers(byte seat, PlayerModifiers value);
      
      // ── Inventory (라운드 리셋 시 재구성 필요) ──
      SlotSnapshot[] GetInventorySlots(byte seat);
      void SetInventorySlots(byte seat, SlotSnapshot[] slots);
      void ClearInventory(byte seat);  // 사망 시
      
      // ── Connection 상태 ──
      bool IsConnected(byte seat);   // PlayerState 존재 여부
      bool IsActive(byte seat);      // Connected || Disconnected (TimedOut 제외)
  }
  
  // 구현: MatchRoster가 ISeatStateAccessor 구현
  // - IsConnected(seat)==true → PlayerRegistry에서 PlayerState 찾아 NV 읽기/쓰기
  // - IsConnected(seat)==false → SeatRuntimeState dict에서 읽기/쓰기
  // 
  // 사용처 (B1 v5.3 확장):
  // - BuffDebuffSystem: ScheduledEffect 적용 시 ISeatStateAccessor로 온도 변경
  // - TemperatureSystem: ApplyFanTick/RecoveryTick 시 ISeatStateAccessor 경유
  // - GhostSkillService: ChillAura/FrostStrike 적용 시 ISeatStateAccessor 경유
  // - **AuthoritativeDeathService**: TryKill(byte seat, DamageSource) — ISeatStateAccessor로
  //   GetLifeState → SetLifeState(Ghost) + ClearInventory. offline seat도 사망 처리 가능
  // - **RoundResetAll**: 전 seat 순회하며 ISeatStateAccessor로 Temperature=37, LifeState=Alive,
  //   FanSpeed/IsFanUpgraded/IsBasicBlocked 초기화, Inventory 기본4종+랜덤 재구성
  // - Reconnect hydrate: SeatRuntimeState의 최신 값(grace 중 변경된 값 포함)으로 NV 복원
  //
  // ⚠ ScheduledEffect는 중앙 시스템에 seat-keyed로 보관 (BuffDebuffSystem의 dict)
  //   PlayerState에도 SeatRuntimeState에도 중복 보관하지 않음
  ```
- [ ] A-3h. **30초 timeout**: TimedOut → SeatRuntimeState 폐기 → seat permanently empty → 생존자 재평가
- [ ] A-3i. **2단계 Start gate (B3 v5.3)**:
  ```
  ⚠ 단일 gate 문제: Relay 코드 수신한 클라이언트가 Host의 token polling 완료 전에
  NGO 연결을 시도하면 ConnectionApproval에서 token 미등록으로 거절됨.
  
  Gate 1 — Lobby/Relay gate (Relay 코드 공유 전):
    LobbyPlayerCount == RequiredPlayerCount
    && TokenReadyCount == RequiredPlayerCount
    → 모든 token이 SessionParticipantTable에 등록된 후에만 Relay 코드 배포
    → 이후 클라이언트가 NGO 연결 시도해도 token 검증 통과 보장
  
  Gate 2 — NGO/Scene gate (씬 전환 전):
    ApprovedNgoClientCount == RequiredPlayerCount
    → 모든 클라이언트가 ConnectionApproval 통과 후 씬 전환
  ```
  - Token 생성: `System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)` (Guid 대신 암호학적 랜덤)
  - 각 클라이언트가 token 생성 → Private PlayerData 기록 → Host 읽기 → SessionParticipantTable 저장
  - TokenReadyCount: SessionParticipantTable에서 token 수신 완료된 참가자 수
- [ ] A-3j. Late join 거절

### A-4. Spawn + Registry

- [ ] A-4a. `PlayerSpawnManager.GetSpawnPosition(byte seatIndex)`: markers[seat].position
- [ ] A-4b. 서버 스폰: `MatchRoster.GetSeat(clientId) → seat → GetSpawnPosition(seat)`
- [ ] A-4c. `PlayerState.Initialize(byte seat)` — TurnManager ClientId 정렬 경로 제거
- [ ] A-4d. PlayerRegistry: `Register(seat, playerState)` seat 기반 lookup

### A-5. 로비 → 다인전 진입

- [ ] A-5a. LobbyModeSelectView "다인전" 버튼 → 인원 선택
- [ ] A-5b. LobbyPresenter.HandleMultiClicked(int count)
- [ ] A-5c. LobbyRoomView 3~4인 슬롯
- [ ] A-5d. NetworkSessionCoordinator: CreateLobbyAsync(mode, playerCount), Relay AllocateAsync(playerCount - 1), 씬 분기
- [ ] A-5e. 기존 1v1: OneVsOne+2 (기존 시그니처 보존)
- [ ] A-5f. ConnectionApproval 등록

### A-6. GameScene_Multi 씬

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

### A-7. N인 HUD

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

### A-8. 통합 테스트

- [ ] A-8a. 로비 → 3인 방 → GameScene_Multi 로드
- [ ] A-8b. seat 안정성 (disconnect → reconnect → 같은 seat, **새 PlayerState + hydrate된 NV 동일값**)
- [ ] A-8c. MatchNetworkState: 초기값 + OnValueChanged (Host + Client 모두)
- [ ] A-8d. Relay allocation: playerCount - 1
- [ ] A-8e. 1v1 회귀 (GameScene + MatchNetworkState 정상)
- [ ] A-8f. ConnectionApproval 거절 (중복 ParticipantId, late join, 잘못된 token)
- [ ] A-8g. spawn = SpawnPoint[seat]
- [ ] A-8h. 2단계 start gate: Lobby gate(token ready→relay 배포) + NGO gate(approved→씬 전환) (B3 v5.3)
- [ ] A-8i. disconnect → **라이브 PlayerState에서 NV 읽기** → SeatRuntimeState 추출 → 명시적 Despawn+Destroy (B1 v5.2)
- [ ] A-8j. reconnect → 새 SpawnAsPlayerObject + hydrate된 NV 값 일치 확인 (grace 중 변경 값 포함)
- [ ] A-8k. token 전달: RandomNumberGenerator.GetBytes(32) → Private PlayerData → Host 읽기 → ConnectionData (P3 v5.2)
- [ ] A-8l. **(v5.2)** ISeatStateAccessor: disconnect grace 중 ScheduledEffect가 disconnected seat 온도 변경 → reconnect 시 해당 변경 반영
- [ ] A-8m. **(v5.2)** DontDestroyWithOwner=true 확인: Player.prefab NetworkObject inspector 값
- [ ] A-8n. **(v5.3)** 2단계 gate: Lobby gate 통과 전 NGO 연결 시도 → token 미등록 거절 안 됨
- [ ] A-8o. **(v5.3)** offline seat 라운드 리셋: disconnect grace 중 라운드 종료 → offline seat도 37° + Alive + 기본4종
- [ ] A-8p. **(v5.3)** offline seat 사망: ScheduledEffect로 offline seat 0° → Ghost 전환 + 킬 귀속
- [ ] A-8q. **(v5.3)** reconnect hydrate 순서: SyncedPlayerIndex 마지막 → TurnManager 미노출 확인

---

## Phase B0 — LifeState + DeathService + DamageSource

### B0-1. LifeState + Roster 분리

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

### B0-2. AuthoritativeDeathService

- [ ] B0-2a. **TryKill(byte victimSeat, DamageSource) — seat 기반 (B1 v5.3)**:
  - ISeatStateAccessor 경유: `GetLifeState(seat)` guard Alive → `SetLifeState(seat, Ghost)`
  - CancelTurnParticipation(seat) → `ClearInventory(seat)` (ISeatStateAccessor)
  - guard `Origin!=Natural && KillerSeat<count` → KillScores++
  - ⚠ **offline seat도 사망 처리 가능**: disconnect 유예 중 ScheduledEffect/환경으로 0°도달 시
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

### B0-3. 킬 스코어 + 승리 + 라운드 리셋

- [ ] B0-3a. KillScores (MatchNetworkState 소유)
- [ ] B0-3b. SeatSnapshot +killScore, +lifeState
- [ ] B0-3c. 공동 승리 WinnerMask (Shell 계산)
- [ ] B0-3d. 라운드 종료: **CountsAsAliveForRoundEnd** ≤ 1 or 전원 Ghost (B3)
- [ ] B0-3e. **라운드 리셋 — ISeatStateAccessor 경유 (B1 v5.3)**:
  ```
  ⚠ 모든 active seat(Connected + Disconnected)을 ISeatStateAccessor로 순회.
  offline seat도 리셋 적용 → reconnect 시 리셋된 상태로 hydrate.

  1. 전원 SetTemperature(seat, 37°) + SetLifeState(seat, Alive)
  2. 기본 4종 재구성: SetInventorySlots(seat, ...) (Multi 바람막이: IsUnlimited=false, RemainingUses=1)
  3. 랜덤 Rule.InitialRandomItems (1v1:4, Multi:2)
  4. Threshold 초기화
  5. 킬 스코어 누적 (리셋 안 함)
  6. Modifier 전체 초기화
  7. Ghost cooldown 전체 클리어
  8. Active debuff dictionary 클리어
  9. ScheduledEffect 전체 클리어
  10. SetFanSpeed(seat,0)/SetIsFanUpgraded(seat,false)/SetIsBasicBlocked(seat,false) — ISeatStateAccessor 경유
  ```

### B0-4. 통합 테스트

- [ ] B0-4a. 0° → Ghost 전환 (online + **offline seat 모두**)
- [ ] B0-4b. 중복 TryKill 방지
- [ ] B0-4c. 킬 스코어 + batch 공동 승리
- [ ] B0-4d. 라운드 리셋 전체 (기본4종+랜덤+modifier+cooldown+fanspeed)
- [ ] B0-4e. Natural source → 킬 없음 (OOB 방지)
- [ ] B0-4f. 1v1 회귀: DeathRule.RoundEnd 분기
- [ ] B0-4g. ScheduledEffect.SourceSeat → DelayedEffect 킬 귀속
- [ ] B0-4h. **Disconnect 유예 중 라운드 미종료** (CountsAsAliveForRoundEnd)
- [ ] B0-4i. **Timeout 후 즉시 생존자 재평가**

---

## Phase B — ActionIntent + Value Snapshots + N인 Resolver + DTO + Balance

### B-1. ActionIntent

- [ ] B-1a. `ActionIntent` readonly struct: { SourceSeat, SlotIndex, ItemId, TargetSeat(255=NoTarget), ReadyServerTick(**int**) }
  - ⚠ **ReadyServerTick** = `NetworkManager.Singleton.ServerTime.Tick` at Rpc receive (P2 해결)
  - **`int` 타입** — `NetworkTime.Tick`은 `int` (NGO NetworkTime.cs:65 확인, P1 v5.2)
  - RPC 도착은 FixedUpdate 기준이 아니므로 float timestamp 대신 서버 NetworkTick 사용
  - 결정적 행동 순서 보장: 방어 우선 → ReadyServerTick 오름차순 → 온도 낮은 순 → seat index
- [ ] B-1b. QueuedAction.TargetSeat 추가
- [ ] B-1c. PlayerState._pendingIntent 보존 (미니게임 경유, 재검증)
- [ ] B-1d. IPlayerTurnCancellation 확장
- [ ] B-1e. ILocalPlayerCommands + LocalPlayerCommandAdapter 확장

### B-2. 아이템 타겟

- [ ] B-2a. TargetMode enum: Self, SingleTarget
- [ ] B-2b. 21종: SingleTarget **13종** (타로 포함), Self 8종
- [ ] B-2c. 드래그-타겟 UI (1v1 자동 타겟)
- [ ] B-2d. 서버 검증

### B-3. Value-Type Snapshots (P1 해결)

> **계약**: factory가 모든 배열을 **defensive copy**로 생성. Resolver는 입력 배열을 **변경하지 않음** (readonly 참조 + 문서화된 불변식).

- [ ] B-3a. `GameModeRuleSnapshot` readonly struct — IGameModeRule 전체 value copy
  - Factory: `GameModeRuleSnapshot.From(IGameModeRule rule)` — 배열 필드는 `.ToArray()` copy
- [ ] B-3b. `ItemEffectRuleSnapshot` readonly struct (B3 해결 — **계산 규칙 캡처, 결과 아님**):
  ```csharp
  readonly struct ItemEffectRuleSnapshot
  {
      // 공통 식별
      public readonly short ItemId;
      public readonly ItemCategory Category;
      public readonly TargetMode TargetMode;
      public readonly DamageFilter AttackFilter;
      public readonly ItemEffectKind EffectKind;  // discriminator
      
      // Attack 계열
      public readonly float BaseDamage;           // 고정 피해량 (WaterGun 8°, BuldakNoodles 20° 등)
      public readonly bool EqualizeToUserTemp;    // B3 v5.2: Equalize = target.temp을 user.temp으로 설정 (평균 아님!)
                                                  // (AttackItemDataSO.cs:13 확인)
      
      // Defense 계열
      public readonly bool IsDefense;
      public readonly float DefenseReduction;
      
      // Recovery 계열 (B3 v5.2: HealPerUse[] 배열 기반으로 교정)
      public readonly float[] HealPerUse;         // RecoveryItemDataSO.HealPerUse 배열 defensive copy
                                                  // (RecoveryItemDataSO.cs:9 확인)
      public readonly int MaxUses;                // RecoveryItemDataSO.MaxUses
                                                  // Resolver: HealPerUse[Clamp(MaxUses - RemainingUses, 0, len-1)]
      
      // Fan/Status 변경
      public readonly bool WritesFanSpeed;
      public readonly float FanSpeedValue;
      public readonly bool WritesTargetFanSpeed;
      public readonly float TargetFanSpeedValue;
      public readonly bool BlocksTargetBasics;
      
      // Special 효과
      public readonly bool GrantsExtraAction;
      public readonly bool RequiresTargetReady;   // B3 v5.2: Tarot은 target의 IsReady.Value 확인 (intent 아님!)
                                                  // (SpecialItemDataSO.cs:17 확인)
      public readonly bool NeutralizesTarget;
      public readonly SpecialEffectType SpecialKind; // Cat(reroll), Claw(steal) 등
      
      // Inventory mutation
      public readonly InventoryMutationType InventoryAction;
      
      // Scheduled (지연) 효과
      public readonly bool HasScheduledEffect;
      public readonly EffectType ScheduledType;
      public readonly float ScheduledValue;
      public readonly int ScheduledDelay;
  }
  
  enum ItemEffectKind : byte
  {
      DirectDamage,    // 고정 피해
      Equalize,        // target.temp = user.temp (평균 아님! B3 v5.2)
      Recovery,        // 회복 (HealPerUse[] 배열 lookup)
      Defense,         // 방어
      FanControl,      // 선풍기/업그레이드
      Sabotage,        // 방해 (BlockBasics 등)
      Special,         // 타로/고양이발톱/고양이장난감 등
      Buff,            // 버프
      Debuff,          // 디버프 (삼계탕 등)
      Scheduled        // 지연 효과만
  }
  ```
  - Factory: `ItemEffectRuleSnapshot.From(ItemDataSO so)` — SO의 **정적 파라미터** 복사 (ComputeEffect 결과 아님!)
    - Recovery: `HealPerUse = so.HealPerUse.ToArray()` (defensive copy), `MaxUses = so.MaxUses`
    - Attack: `EqualizeToUserTemp = so.EqualizeToUserTemp`
    - Special: `RequiresTargetReady = (so.SpecialEffect == RevealOpponent)` (Tarot)
  - ⚠ **Resolver 책임**: ItemEffectKind별 분기 + MatchCombatSnapshot 런타임 상태로 최종 효과 계산 (B3 v5.2 교정)
    - **Equalize**: `target.newTemp = snapshot.Temperatures[user]` (target 온도를 user 온도로 설정, 평균 아님)
    - **Recovery**: `heal = rule.HealPerUse[Clamp(rule.MaxUses - inventory.Slots[slot].RemainingUses, 0, rule.HealPerUse.Length-1)]`
      (RecoveryItemDataSO.cs:13-14 수식 그대로)
    - **RequiresTargetReady**: Resolver가 target의 `IsReady` **상태** 확인 (intent 존재 여부 아님, SpecialItemDataSO.cs:17)
- [ ] B-3c. `InventorySnapshot` readonly struct (seat별): `{ byte SeatIndex, SlotSnapshot[] Slots }`
  - `SlotSnapshot { short ItemId, bool IsUnlimited, byte RemainingUses }`
  - Factory: defensive `.ToArray()` copy
- [ ] B-3d. `ScheduledEffectSnapshot` readonly struct: `{ byte TargetSeat, byte SourceSeat, EffectType Type, float Value, int TurnsRemaining }`

### B-4. Functional Core (B4 해결 — 완전한 delta)

- [ ] B-4a. `MatchCombatSnapshot` — **순수 value-type만, defensive copy**:
  ```
  float[] TemperaturesAtTurnStart      // seat-indexed, copy
  float[] CurrentTemperatures          // seat-indexed, copy
  PlayerModifiers[] Modifiers          // value struct copy
  LifeState[] LifeStates               // copy
  InventorySnapshot[] Inventories
  ScheduledEffectSnapshot[] ScheduledEffects
  int[] CurrentKillScores              // copy (WinnerMask는 Shell이지만 Deathmatch grant 판단용)
  bool[] IsReady                       // seat-indexed, Factory: PlayerState.IsReady.Value copy (B2 v5.3: Tarot Resolver용)
  EnvironmentType Environment
  GameModeRuleSnapshot Rule
  ItemEffectRuleSnapshot[] ItemRules   // copy
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
  - **NV/MonoBehaviour/SO runtime instance 참조 없음**
  - ItemEffectRuleSnapshot의 ItemEffectKind별 분기 + MatchCombatSnapshot 런타임 상태로 효과 계산 (B3 v5.2 교정):
    - DirectDamage: `delta = -rule.BaseDamage` 적용
    - **Equalize**: `target.newTemp = snap.Temps[user]` (target 온도를 user 온도로 설정, 평균 아님!)
      `delta = snap.Temps[user] - snap.Temps[target]`
    - **Recovery**: `useIndex = rule.MaxUses - snap.Inventories[seat].Slots[slot].RemainingUses`
      `heal = rule.HealPerUse[Clamp(useIndex, 0, rule.HealPerUse.Length-1)]`
      (RecoveryItemDataSO.cs:13-14 로직 그대로 복제)
    - **RequiresTargetReady (Tarot)**: target의 `IsReady` 상태 확인 (intent 존재 여부 아님!)
      `if (rule.RequiresTargetReady && !snap.IsReady[target]) → reveal 실패`
  - 행동 순서: 방어 우선 → **ReadyServerTick** 오름차순 → 온도 낮은 순 → **seat index** (최종 tie-break)
  - 중간 사망: 매 행동 후 0° 체크, 사망자 행동 취소
  - Target이 실행 전 Ghost → 행동 **취소** (재지정 안 함)
  - 기존 `Resolve(p1, p2)` 완전 보존
- [ ] B-4d. **Applicator Shell**:
  ```
  1. resolution.TemperatureDeltas → PlayerState.Temperature.Value write
  2. resolution.PlayerStateChanges → FanSpeed/IsFanUpgraded/IsBasicBlocked NV write [P3 반영]
  3. resolution.InventoryChanges → PlayerInventory 반영
  4. resolution.ModifierChanges → PlayerModifiers 반영
  5. resolution.NewScheduled → BuffDebuffSystem 등록 (with SourceSeat)
  6. FlushDeathQueue() → TryKill
  7. KillScores 반영 → WinnerMask 계산 (Shell)
  8. ResultSequence = ++_serverSequenceCounter (Shell)
  9. CombatResolutionBatchNetData 생성 + ClientRpc
  ```

### B-5. Network DTO — 수동 직렬화 (B5 해결)

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
- [ ] B-5c. `CombatResolutionBatchNetData : INetworkSerializable` — **수동 요소별 직렬화** (B4 해결):
  ```csharp
  public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
  {
      s.SerializeValue(ref SeatCount);
      s.SerializeValue(ref EventCount);
      
      // ── Writer-side null/length 검증 (P2 v5.2) ──
      if (!s.IsReader)
      {
          if (Events == null || TempBefore == null || TempAfter == null ||
              MainItemIds == null || SubItemIds == null || ActionOrder == null)
              throw new System.InvalidOperationException("Writer: null array detected");
          if (EventCount > Events.Length)
              throw new System.InvalidOperationException("EventCount exceeds Events array");
          if (SeatCount > TempBefore.Length || SeatCount > TempAfter.Length ||
              SeatCount > MainItemIds.Length || SeatCount > SubItemIds.Length ||
              SeatCount > ActionOrder.Length)
              throw new System.InvalidOperationException("SeatCount exceeds seat array");
      }
      
      // ── Reader-side 배열 초기화 (B4: struct default → 배열 null 방지) ──
      if (s.IsReader)
      {
          Events ??= new CombatEventNetData[16];
          TempBefore ??= new float[4];
          TempAfter ??= new float[4];
          MainItemIds ??= new short[4];
          SubItemIds ??= new short[4];
          ActionOrder ??= new int[4];
      }
      
      // ── 범위 검증 (양측) ──
      if (SeatCount > 4)
          throw new System.InvalidOperationException($"SeatCount {SeatCount} > 4");
      if (EventCount > 16)
          throw new System.InvalidOperationException($"EventCount {EventCount} > 16");
      
      // ── 요소별 수동 직렬화 (FixedList512Bytes 직접 직렬화 불가) ──
      for (int i = 0; i < EventCount; i++)
          Events[i].NetworkSerialize(s);
      
      // ── 고정 길이 배열 (SeatCount 기준) ──
      for (int i = 0; i < SeatCount; i++) s.SerializeValue(ref TempBefore[i]);
      for (int i = 0; i < SeatCount; i++) s.SerializeValue(ref TempAfter[i]);
      for (int i = 0; i < SeatCount; i++) s.SerializeValue(ref MainItemIds[i]);
      for (int i = 0; i < SeatCount; i++) s.SerializeValue(ref SubItemIds[i]);
      for (int i = 0; i < SeatCount; i++) s.SerializeValue(ref ActionOrder[i]);
      
      s.SerializeValue(ref DeadMask);
      s.SerializeValue(ref WinnerMask);
      s.SerializeValue(ref FirstActionSeat);
      s.SerializeValue(ref ResultSequence);
  }
  ```
  - Events 저장: `CombatEventNetData[] Events = new CombatEventNetData[16]` (고정 배열, EventCount로 유효 범위)
  - **FixedList512Bytes 사용하지 않음** — NGO 2.11.2 BufferSerializer에 직접 오버로드 없으므로
  - 총 크기: 2(header) + 10×16(worst) + 4×5×4(seat arrays) + 4(masks) + 4(seq) = ~246 bytes (1200 MTU 이내)
- [ ] B-5d. Overflow → state 적용 전 abort → match 강제 종료 (재시도 안 함)
- [ ] B-5e. 1v1: 기존 Resolve() + CombatResultData 보존

### B-6. Balance

- [ ] B-6a. 모든 랜덤 경로에 MaxRandomItems cap + GetRandomSlotCount() 헬퍼
- [ ] B-6b. 바람막이 Multi 1-use: InitializeBasicItems에 IGameModeRule → `IsUnlimited=false, RemainingUses=1`
- [ ] B-6c. 타로: Multi ItemDropTable 생성 시 필터링 (SO weight 변경 안 함)
- [ ] B-6d. Deathmatch: `min(Rule.DeathmatchGrantCount, cap - currentRandom)` (라운드 1회)
- [ ] B-6e. 동시 Ready 판정 = 동일 ReadyServerTick (NetworkManager.ServerTime.Tick) [P2]

### B-7. TurnManager N인

- [ ] B-7a. 동적 배열
- [ ] B-7b. PrepPhase N명 루프
- [ ] B-7c. AttackPhase: Multi → snapshot + ResolveMulti + Applicator, 1v1 → 기존
- [ ] B-7d. BuffDebuffSystem N인 확장

### B-8. 통합 테스트

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

## Phase C — N인 VFX + 환경

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

## Phase D — Ghost Skill + Multiplier + UI

### D-1. PlayerModifiers + 실제 적용

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

### D-2. Ghost 스킬 + 쿨다운 (P3 해결)

- [ ] D-2a. **쿨다운 저장소**: `Dictionary<(byte seat, byte skillIndex), int>` (스킬별 개별 쿨다운)
  - skillIndex: 0=FrostStrike, 1=ChillAura
  - PrepPhase 시작 시 전원 쿨다운 1 감소
  - 라운드 리셋 시 전체 클리어
- [ ] D-2b. **쿨다운 UI 복제**: `NetworkList<GhostCooldownNetData>` in MatchNetworkState (B5 해결):
  ```csharp
  // NetworkList<T>의 T 계약: unmanaged + IEquatable<T> + INetworkSerializable
  public struct GhostCooldownNetData : INetworkSerializable, System.IEquatable<GhostCooldownNetData>
  {
      public byte Seat;
      public byte Skill;           // 0=FrostStrike, 1=ChillAura
      public byte RemainingTurns;
      
      public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
      {
          s.SerializeValue(ref Seat);
          s.SerializeValue(ref Skill);
          s.SerializeValue(ref RemainingTurns);
      }
      
      public bool Equals(GhostCooldownNetData other)
          => Seat == other.Seat && Skill == other.Skill && RemainingTurns == other.RemainingTurns;
      public override int GetHashCode() => (Seat << 16) | (Skill << 8) | RemainingTurns;
  }
  ```
  - **MatchNetworkState 수명주기**:
    - `Awake`: `GhostCooldowns = new NetworkList<GhostCooldownNetData>();`
    - `OnNetworkSpawn`: `GhostCooldowns.OnListChanged += OnCooldownsChanged;`
    - `OnNetworkDespawn`: `GhostCooldowns.OnListChanged -= OnCooldownsChanged;`
    - `OnDestroy`: `GhostCooldowns?.Dispose();`
    - 서버: 쿨다운 변경 시 리스트 동기화 (Add/Remove/Update)
    - 서버: 라운드 리셋 시 `GhostCooldowns.Clear()`
  - 클라이언트: Everyone readable → Ghost UI에서 자기 seat의 쿨다운 표시
- [ ] D-2c. GhostSkillServerRpc 6단계 검증: IsServer, sender==Owner, Ghost, PrepPhase, target Eligible(Connected+Alive), cooldown==0
- [ ] D-2d. 디버프 덮어쓰기: modifier 원복 → **active debuff entry 제거** → 새 설치 + 새 entry → killerSeat 갱신
- [ ] D-2e. **Active debuff dictionary**: `Dictionary<byte targetSeat, GhostDebuffEntry>`
  - `GhostDebuffEntry { byte GhostSeat, byte SkillIndex, int AppliedTurn }`
  - **ChillAura 만료 시**: modifier 원복 **+ debuff entry 제거**
  - 라운드 리셋 시 전체 클리어
- [ ] D-2f. FrostStrike: DamageFilter.Ghost, 즉시 FlushDeathQueue, DamageSource={ghostSeat, GhostFrost}
- [ ] D-2g. ChillAura: FanSpeedMultiplier=2, RecoveryMultiplier=0.5, DamageSource 갱신

### D-3. Ghost UI

- [ ] D-3a. Ghost PrepPhase UI: 스킬 버튼 2개
- [ ] D-3b. 타겟 선택: Eligible 생존자 클릭
- [ ] D-3c. 쿨다운 오버레이 (NetworkList 읽기)
- [ ] D-3d. PrepPhase 자유, Ready 불필요
- [ ] D-3e. Ghost 시야에 전원 상태

### D-4. 통합 테스트

- [ ] D-4a. FrostStrike → PrepPhase 즉시 사망 → FlushDeathQueue → 킬 귀속
- [ ] D-4b. ChillAura → fan ×2 확인 → 간접 킬
- [ ] D-4c. Recovery ×0.5 확인
- [ ] D-4d. 덮어쓰기: 2 Ghost 동일 타겟
- [ ] D-4e. 라운드 종료 → 전원 Alive + 전체 초기화
- [ ] D-4f. Ghost 5킬 승리
- [ ] D-4g. **ChillAura 만료**: 다음 PrepPhase 종료 시 해제 + debuff entry 제거
- [ ] D-4h. **쿨다운 UI**: FrostStrike/ChillAura 개별 표시

---

## Phase E — Solo/Bot

### E-1. Solo 인프라

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

### E-2. Bot AI

- [ ] E-2a. IBotBrain BT
- [ ] E-2b. BotPlayer: PrepPhase → BT → SelectItemServerRpc → delay → ReadyServerRpc
- [ ] E-2c. 난이도 3종
- [ ] E-2d. Ready 타이밍
- [ ] E-2e. 미니게임 IsBot 자동 성공

### E-3. 외형/이름

- [ ] E-3a. Bot 이름
- [ ] E-3b. 기본 캐릭터

### E-4. 통합 테스트

- [ ] E-4a. Solo → Host → Bot 연결
- [ ] E-4b. Bot 아이템+Ready
- [ ] E-4c. Bo3 정상
- [ ] E-4d. Bot 크래시 감지
- [ ] E-4e. 매치 종료 → 프로세스 정리
- [ ] E-4f. token 거절

---

## Phase F — 미해결 질문 + 정리

- [ ] F-1a~h. Q10, Q16, Q21~Q23, Q24, Q26, Q27
- [ ] F-2a~e. PLAN_019 역사화, PLAN_021 트리, GAME_DESIGN, ACTIVE_CONTEXT, CHANGES

---

## 완료 불변식 (24개)

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
17. **(v5.2)** DontDestroyWithOwner=true: disconnect 시 NGO가 서버 소유 전환 → 라이브 PlayerState에서 NV 읽기 → SeatRuntimeState 추출 → 명시적 Despawn+Destroy
18. **(v5.2)** ISeatStateAccessor: disconnect grace 동안 모든 시스템이 seat 상태 접근 가능 (PlayerState 또는 SeatRuntimeState)
19. **(v5.2)** Equalize: target.temp = user.temp (평균 아님). Recovery: HealPerUse[MaxUses-RemainingUses]. Tarot: IsReady 상태 확인 (intent 아님)
20. **(v5.2)** ReadyServerTick은 int (NetworkTime.Tick 타입)
21. **(v5.2)** DTO writer-side: 모든 배열 null 체크 + count≤length 검증
22. **(v5.3)** ISeatStateAccessor는 PlayerState의 전체 NV를 커버 (Temperature~IsBasicBlocked + Inventory)
23. **(v5.3)** TryKill/RoundReset은 ISeatStateAccessor 경유 — offline seat도 처리 가능
24. **(v5.3)** SeatRuntimeState에 ScheduledEffect 중복 보관 금지 — BuffDebuffSystem 중앙만

## 검증 게이트

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
11. **(v5.2)** disconnect → 라이브 PlayerState에서 NV 읽기 성공 (DontDestroyWithOwner=true)
12. **(v5.2)** grace 중 ScheduledEffect/환경이 disconnected seat에 정상 적용 (ISeatStateAccessor)
13. **(v5.2→v5.3)** Token 2단계 gate: Lobby gate (token ready → relay 코드 배포) + NGO gate (approved count → 씬 전환)
14. **(v5.3)** TryKill(byte seat) — offline seat에서 ScheduledEffect/환경으로 0°도달 시 Ghost 전환
15. **(v5.3)** 라운드 리셋 — offline seat도 ISeatStateAccessor로 전체 상태 초기화
16. **(v5.3)** MatchCombatSnapshot.IsReady[] 존재 → Tarot Resolver 컴파일 성공
17. **(v5.3)** Reconnect hydrate: SyncedPlayerIndex 마지막 설정 → 반쯤 복원 방지

## 위험 요소

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
| Reconnect NV 소실 | SeatRuntimeState + 새 SpawnAsPlayerObject + hydrate |
| PlayerObject lookup 미등록 | SeatRuntimeState 방식: 새 spawn이므로 자연 해결 |
| DontDestroyWithOwner timing (v5.2) | =true 설정: NGO 서버 소유 전환 → 라이브 NV 읽기 가능 |
| Disconnect grace 중 seat 접근 (v5.2) | ISeatStateAccessor: PlayerState↔SeatRuntimeState 통합 |
| Disconnect grace vs round end | CountsAsAliveForRoundEnd 분리 |
| Resolution delta 불완전 | PlayerStateDelta (FanSpeed/IsFanUpgraded/IsBasicBlocked) |
| Token 전달 Lobby 권한 | 클라이언트 self-write Private → Host 읽기 |
| ItemRuleSnapshot 런타임 의존 | ItemEffectRuleSnapshot 규칙 캡처 + Resolver 계산 |
| DTO reader 배열 null | reader-side ??= new + 양측 범위 검증 |
| GhostCooldownNetData 계약 | unmanaged+IEquatable+INetworkSerializable+lifecycle |
| ReadyTimestamp 비결정적 | ReadyServerTick int (NetworkTime.Tick) |
| ChillAura epoch | GAME_DESIGN 기준: 다음 PrepPhase 종료 시 |
| Equalize 수식 오류 (v5.2) | target.temp=user.temp (평균 아님), EqualizeToUserTemp flag |
| Recovery 수식 오류 (v5.2) | HealPerUse[] 배열 lookup (baseHeal*remaining 아님) |
| Tarot 판정 오류 (v5.2) | IsReady 상태 확인 (intent 존재 아님), RequiresTargetReady flag |
| DTO writer null (v5.2) | writer-side 모든 배열 null+length 검증 |
| Token Guid 예측가능 (v5.2) | RandomNumberGenerator.GetBytes(32) 사용 |
| Token ready gate (v5.3) | 2단계: Lobby gate(token ready→relay) + NGO gate(approved→scene) |
| ISeatStateAccessor 불완전 (v5.3) | 전체 NV 커버 + Inventory + TryKill/RoundReset 경유 |
| PendingEffects 이중 발동 (v5.3) | SeatRuntimeState에서 제거, BuffDebuffSystem 중앙만 |
| Reconnect 반쯤 복원 (v5.3) | SyncedPlayerIndex 마지막 설정 + IsHydrated latch |
| MatchCombatSnapshot.IsReady 누락 (v5.3) | bool[] IsReady 추가 |


---

## END OF PLAN

Review the full plan above. Output your assessment as:

```
## Review #8 — PLAN_025 v5.3

**Score: X.X / 10**

### BLOCKER (must fix)
- **B1**: [section] — [issue] — [failure scenario]

### PARTIAL (should fix)
- **P1**: [section] — [issue] — [risk]

### APPROVED items (previously fixed, now correct)
- B1 v5.3 (ISeatStateAccessor full coverage): [assessment]
- B2 v5.3 (MatchCombatSnapshot.IsReady[]): [assessment]
- B3 v5.3 (2-stage token gate): [assessment]
- P1 v5.3 (hydrate commit order): [assessment]

### Summary
[Overall assessment. Is the plan implementation-ready?]
```
