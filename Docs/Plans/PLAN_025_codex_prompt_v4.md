당신은 Unity 6 멀티플레이어 게임 "Absolute Zero"의 시니어 아키텍트입니다.
아래 PLAN_025 v4 (Multi Mode Full Implementation)를 검증해 주세요.

## 프로젝트 핵심 사실

- Unity 6 (6000.3.11f1), NGO 2.11.2, Host-authoritative, DTLS Relay
- 현재 1v1 Bo3 (GameScene) 정상 동작 중, Multi(3~4인 5킬 Deathmatch)를 별도 씬(GameScene_Multi)으로 구축
- PLAN_019의 핵심 계약(stable roster, pure Domain combat, DTO, 10→14 invariants)을 PLAN_025가 흡수

## 현행 코드 핵심 제약 (변경 불가)

1. `CombatResolver.Resolve(p1Queue, p2Queue, ...)` — 1v1 전용, 보존 필수
2. `CombatResult.P1*/P2*` + `CombatResultData` Event0/Event1 (max 2) — 1v1 DTO, 보존 필수
3. `PlayerState.SelectItemServerRpc(byte slotIndex)` — 현재 targetSeat 없음
4. `ActionQueue.QueuedAction { SlotIndex, ItemData }` — targetSeat 없음
5. `PlayerModifiers` — FanSpeedMultiplier/RecoveryMultiplier 없음
6. `TurnManager._players[2], _modifiers[2]` — 고정 2인 배열
7. `ItemManager.InitializePlayerInventory()` — 4개 랜덤 지급 (1v1에서는 반드시 4개 유지)
8. `NetworkConstants.GameMode { None, TurnBattle }` — Multi/Solo 미존재
9. `NetworkSessionCoordinator.AllocateAsync(maxPlayers)` — total 전달 (Relay는 joining clients 수 필요)
10. `TemperatureSystem.ApplyDamage(target, rawDamage, DamageFilter, DefenseInfo?)` — Ghost용 DamageFilter.Ghost 미존재
11. `PresentationBarrier.Begin(sequence, expectedClientIds)` — 이미 N인 지원
12. ConnectionApproval 콜백 미등록
13. `PlayerState.BuildContext()` — 첫 비자기 상대 하드코딩
14. `_pendingMiniGameSlot` — 슬롯만 보존, 타겟 미보존
15. `MatchCompositionRoot` — **MonoBehaviour** (NetworkBehaviour 아님, NV 소유 불가)
16. `PlayerSpawnManager.GetSpawnPosition(clientId)` — `clientId % spawnPoints.Count` (seat 기반 아님)
17. `PlayerInventory.ConsumeItem()` — `IsUnlimited=true`면 즉시 반환 (소비 안 됨)
18. `ScheduledEffect` — SourceSeat 필드 없음 (DelayedEffect 킬 귀속 불가)
19. `TemperatureSystem.ApplyFanTick/ApplyRecoveryTick` — multiplier 미참조
20. Tarot — `ctx.Target.IsReady` 상대 조회 (Self로 지정 시 자기 조회 → 1v1 회귀)

## 리뷰 이력

### Review #1 (4.0/10): 11 blockers → v2 전부 해결
### Review #2 (6.0/10): 5 blockers → v3 전부 해결
### Review #3 (6.0/10): 8 blockers + 6 partial

| # | v3 지적 | v4 반영 |
|---|---------|---------|
| B1 | MCR MonoBehaviour → NV 불가 | `MatchNetworkState : NetworkBehaviour` 신규. OnNetworkSpawn에서 초기값 즉시 읽기 + OnValueChanged. MCR은 composition만 |
| B2 | ResolveMulti가 SO runtime instance 참조 | 모든 입력 value-type snapshot: `GameModeRuleSnapshot`, `ItemRuleSnapshot[]`, `InventorySnapshot[]`, `ScheduledEffectSnapshot[]`. Resolution에 전체 state delta (InventoryDelta, ModifierDelta, ScheduledEffectDelta). WinnerMask/ResultSequence → Shell 결정 |
| B3 | Roster 수명 ↔ ConnectionApproval 시점 충돌 | Session scope(`SessionParticipantTable`, DDOL) vs Match scope(`MatchRoster`, GameScene). Barrier reconnect: 진행 중 추가 안 함 → 다음 sequence부터 |
| B4 | Spawn clientId % count, TurnManager ClientId 정렬 | `MatchRoster.GetSeat(clientId) → seat → SpawnPoint[seat]`. TurnManager ClientId 정렬 경로 제거. Start gate + late join 거절 + hydration 상세 |
| B5 | FixedArray C# 타입 아님 | `FixedList512Bytes<CombatEventNetData>`. 이벤트 생성 불변식 명시 (1 action → 1 event). CombatEventNetData = 10 bytes. Overflow → state 적용 전 abort + match 강제 종료 |
| B6 | 바람막이 unlimited 즉시 반환 | Multi 지급 시 runtime slot `IsUnlimited=false, RemainingUses=1`. 기존 ConsumeItem 경로 정상 작동. SO 변경 없음 |
| B7 | Tarot Self → 1v1 자기 조회 회귀 | Tarot = **SingleTarget** (1v1 서버 자동 타겟, Multi 드롭 제외) |
| B8 | {255,Fan} → KillScores[255] OOB | Fan(디버프 없음) = **Natural 정규화**. guard: `Origin!=Natural && KillerSeat<count`. ScheduledEffect.SourceSeat 추가. Ghost Frost PrepPhase → 즉시 FlushDeathQueue |
| P1 | ChillAura multiplier 미적용 | ApplyFanTick/ApplyRecoveryTick 코드 수정 명시. epoch: "적용 Prep~다음 Prep 시작 해제". 쿨다운 storage + cleanup 태스크 |
| P2 | Roster/LifeState 중복 | Roster=연결(Connected/Disconnected/TimedOut), LifeState=게임(Alive/Ghost), Eligible=파생 |
| P3 | Tie-break 없음 | 최종 키: seat index. 동시 Ready = 같은 FixedUpdate. Target Ghost → 취소(재지정 안 함). Ambulance 동률 → seat index 최소 |
| P4 | Multi HUD 파일 매핑 부재 | 9개 UI 파일 수정 목록. visual slot 0~2 ≠ match seat. 공동 승리 → Lobby 복귀 |
| P5 | 라운드 리셋 기본 아이템/Deathmatch cap | 기본 4종 재구성 (Multi 바람막이 1-use). Deathmatch: `min(4, cap-current)`. Tarot 필터 = drop table 생성 시 제외 |
| P6 | Solo 포트/빌드 분리 | bind(0) → 실제 endpoint → Bot arg. Standalone build vs -batchmode 실행 분리 |

## 검증 기준 (10점 만점)

### BLOCKER 판정 기준
1. **Phase 의존성 위반**: Phase X가 Phase Y 타입을 선행 참조
2. **Domain 순수성 위반**: CombatResolver가 NV/MonoBehaviour/SO runtime instance 참조
3. **DTO 무결성**: truncate, bounded 미계산, C# 타입 미존재
4. **서버 권위 위반**: 클라이언트 NV direct write
5. **1v1 회귀**: 기존 Resolve/CombatResultData/InitializePlayerInventory(4개) 변경
6. **초기화 순서**: NV read before server write, 누락된 초기 동기화
7. **Reconnect 불완전**: seat rebinding 없이 ClientId만 갱신, hydration 누락
8. **밸런스 경로 누락**: MaxRandomItems cap 미적용 경로
9. **(신규)** NV 소유자가 NetworkBehaviour가 아닌 MonoBehaviour
10. **(신규)** Spawn이 seat 기반이 아닌 clientId 기반
11. **(신규)** KillerSeat OOB 가능성
12. **(신규)** Resolution 출력에 applicator가 적용할 수 없는 누락 delta

### 점수 기준
- 9~10: APPROVED
- 7~8: MINOR REVISION
- 5~6: NEEDS REVISION
- 1~4: NOT APPROVED

### 출력 형식

```
## 점수: X/10 — [APPROVED / MINOR REVISION / NEEDS REVISION / NOT APPROVED]

### BLOCKER (차단)
B1. [제목] — 구체적 문제 + Phase/Task + 수정 방향

### PARTIAL (부분 수정)
P1. [제목] — 구체적 문제 + 수정 방향

### GOOD (잘된 점)
G1. ...

### 총평
한 문단 요약
```

---

## PLAN_025 v4 전문

# PLAN_025 — Multi Mode Full Implementation (v4 — Codex Review #3 반영)

> **Status:** 📋 Planning (v4 — Review #1/#2/#3 전체 반영, 재검증 대기)
> **Created:** 2026-09-02 | **Revised:** 2026-09-02
> **Dependencies:** GAME_DESIGN.md, PLAN_018, PLAN_020
> **Absorbs:** PLAN_019 → 역사 문서 전환
> **Scope:** GameScene_Multi 씬 → 3~4인 전투 → Ghost → Solo/Bot → 미해결 질문
> **Will NOT touch:** GameScene (1v1), PLAN_024 Rematch (1v1 전용)

---

## Overview

1v1과 Multi는 **완전 분리 씬**. `GameScene` (1v1 Bo3)은 그대로 두고 `GameScene_Multi` (킬 기반 5킬 승리)를 구축한다. PLAN_019의 핵심 계약(roster, DTO, combat pipeline, 완료 불변식)을 흡수한다.

### 씬 구조

| Build Index | 씬 | 용도 |
|---|---|---|
| 0 | `LobbyScene` | 로비 (공통) |
| 1 | `GameScene` | 1v1 Bo3 (현행) |
| 2 | `GameScene_Multi` | 3~4인 다인전 (5킬) |
| 3 | `GameScene_Solo` (또는 GameScene 공유) | 솔로 봇전 (TBD) |

### 디자인 패턴

| 시스템 | 패턴 |
|---|---|
| 규칙 분기 | **Strategy** — `IGameModeRule` |
| 참가자 관리 | **Registry + Roster** (PLAN_019 §4) |
| 아이템 선택 | **Command** — `ActionIntent` (불변, 미니게임 경유 보존) |
| 사망/킬 귀속 | **Centralized** — `AuthoritativeDeathService` |
| 전투 파이프라인 | **Functional Core / Imperative Shell** — 순수 value snapshot→resolution→authoritative applicator |
| Domain↔Network | **Anti-Corruption Layer** — bounded `CombatResolutionBatchNetData` (`FixedList512Bytes`) |
| Ghost | **State Pattern** — `LifeState { Alive, Ghost }` |
| Bot AI | **Behavior Tree** |
| N인 연출 | **Sequencer** — PresentationBarrier + CombatVFXManager |

### Phase 의존성 (v4 확정)

```
Phase A (MatchConfig + MatchNetworkState + Session/Match Roster + Spawn + 씬 + 기반 인터페이스 + HUD)
    ↓
Phase B0 (LifeState + DeathService + DamageSource 정규화)
    ↓
Phase B (ActionIntent + Value Snapshots + N인 Resolver + DTO + Balance)
    ↓
Phase C (VFX + 환경)
    ↓
Phase D (Ghost Skill + Multiplier 적용 + UI)

Phase A ──→ Phase E (Solo/Bot — 별도 빌드/프로세스)

Phase F ← 기획 답변 시
```

### 현행 코드 수정 포인트

| 현행 코드 | 위치 | 해법 |
|---|---|---|
| `_players[2]`, `_modifiers[2]` | TurnManager.cs:50-51 | Roster 기반 동적 |
| `Resolve(p1, p2)` | CombatResolver.cs:11 | 보존 + `ResolveMulti(snapshots)` 신규 |
| `CombatResult.P1*/P2*` | CombatResult.cs | 1v1 보존, Multi용 `MultiCombatResolution` 신규 |
| `CombatResultData` Event0/Event1 | CombatResult.cs:32 | 1v1 보존, Multi용 `CombatResolutionBatchNetData` 신규 |
| `SelectItemServerRpc(byte slotIndex)` | PlayerState.cs:181 | `(slot, targetSeat)` 확장 |
| `BuildContext()` | PlayerState.cs:146 | `_pendingIntent.TargetSeat` 기반 |
| `ActionQueue` — no target | ActionQueue.cs:7 | `QueuedAction.TargetSeat` 추가 |
| `PlayerModifiers` | PlayerModifiers.cs:5 | `FanSpeedMultiplier`, `RecoveryMultiplier` 추가 |
| `GameMode { None, TurnBattle }` | NetworkConstants.cs:23 | `OneVsOne, Multi, Solo` |
| `gameSceneName = "GameScene"` | PlayerSpawnManager.cs:25 | GameMode 분기 |
| `maxPlayers = 2` | NetworkSessionCoordinator.cs:44 | MatchConfig 기반 |
| `ApplyDamage` | TemperatureSystem.cs:56 | `DamageFilter.Ghost` 추가 |
| `ApplyFanTick` | TemperatureSystem.cs:37 | `* FanSpeedMultiplier` |
| `ApplyRecoveryTick` | TemperatureSystem.cs:46 | `* RecoveryMultiplier` |
| `InitializePlayerInventory()` | ItemManager.cs:54 | `Rule.InitialRandomItems` (1v1:4) |
| `AllocateAsync(maxPlayers)` | NetworkSessionCoordinator.cs:285 | `RequiredPlayerCount - 1` |
| `Find("EnemyPlayer")` | AZPlayerVisual.cs | visual slot 매핑 |
| `GetSpawnPosition(clientId)` | PlayerSpawnManager.cs:238 | `SpawnPoint[seat]` |
| `MatchCompositionRoot` | MatchCompositionRoot.cs:7 | composition만, NV → MatchNetworkState |
| `ScheduledEffect` | BuffDebuffSystem.cs:10 | SourceSeat 필드 추가 |
| `PlayerInventory.ConsumeItem` | PlayerInventory.cs:57 | Multi 바람막이: `IsUnlimited=false` |
| Tarot ctx.Target | PlayerState.cs:315 | **SingleTarget** (Self 아님) |

---

## Phase A — MatchConfig + MatchNetworkState + Roster + Spawn + 씬 + HUD

### A-1. GameMode + MatchConfig

- [ ] A-1a. `NetworkConstants.GameMode` enum: `None=0, OneVsOne=1, Multi=2, Solo=3`
- [ ] A-1b. `IGameModeRule` interface (12개 필드): ModeCapacity, WinKills, WinRounds, MaxRandomItems, ThresholdGrants, InitialTemperature, WindbreakerConsumable, TarotEnabled, InitialRandomItems, DeathRule, DeathmatchGrantEnabled, DeathmatchGrantCount
- [ ] A-1c. `GameModeRuleSO : ScriptableObject, IGameModeRule` — RULE-001: 배열 Awake copy
- [ ] A-1d. `MatchConfig` 서버 런타임: `{ IGameModeRule Rule, int RequiredPlayerCount, GameMode Mode }`
- [ ] A-1e. `MatchConfigNetData : INetworkSerializable` — `{ byte Mode, byte RequiredPlayerCount }`

### A-2. MatchNetworkState (NV 소유자)

- [ ] A-2a. `MatchNetworkState : NetworkBehaviour` 신규 (GameScene/GameScene_Multi 씬 배치)
  - `NetworkVariable<MatchConfigNetData> Config` (서버 write, Everyone read)
  - `NetworkList<int> KillScores` (seat indexed, 서버 write)
- [ ] A-2b. 서버: scene load 완료 후 `Config.Value = data` 1회 기록
- [ ] A-2c. 클라이언트: OnNetworkSpawn에서 `Config.Value` 즉시 읽기 (Mode!=None 확인) + OnValueChanged 구독
  - Mode==None || RequiredPlayerCount==0 → TurnManager/HUD 초기화 대기
- [ ] A-2d. MCR: MatchNetworkState 참조 → 복제 상태 읽어 local MatchConfig 조립 (composition 역할만)

### A-3. Session Scope vs Match Scope Roster

- [ ] A-3a. **SessionParticipantTable** (DDOL, Coordinator 소유):
  - Host 시작 시 생성, Lobby participant (ParticipantId → approved, token)
  - ConnectionApprovalCallback: payload ParticipantId+token → 검증 → 중복 거절 → late join 차단
- [ ] A-3b. **MatchRoster** (서버 전용, MCR 소유, GameScene에서 생성):
  - ConnectionState: Connected, Disconnected, TimedOut (연결 상태만, P2)
  - seat 0~3 순차 할당, PlayerIndex = seat 불변
- [ ] A-3c. **Reconnect (Match scope)**: approval 통과 → seat 조회 → Disconnected 확인 → ClientId 재바인딩 → PlayerState 재스폰 + hydration → ⚠ 진행 중 Barrier 추가 안 함 → 다음 sequence부터
- [ ] A-3d. Disconnect: seat Disconnected + 30초 타이머, Barrier 즉시 이전 ClientId 제거, auto-ready, pending intent 보존(timeout 시 취소)
- [ ] A-3e. Start gate: `SessionParticipantTable.ConnectedCount == RequiredPlayerCount` → 씬 전환
- [ ] A-3f. Late join 거절: 씬 전환 이후 미등록 ParticipantId → 거절

### A-4. Spawn + Registry 연결

- [ ] A-4a. `PlayerSpawnManager.GetSpawnPosition(byte seatIndex)` — `markers[seatIndex].position`
- [ ] A-4b. 서버 스폰: `MatchRoster.GetSeat(clientId) → seat → GetSpawnPosition(seat)`
- [ ] A-4c. `PlayerState.Initialize(byte seat)` — `SyncedPlayerIndex.Value = seat`
  - TurnManager ClientId 정렬→PlayerIndex 경로 **제거** → roster seat 사용
- [ ] A-4d. Reconnect hydration:
  - NV 자동 동기화: Temperature, LifeState, IsFanActive, SyncedPlayerIndex, IsReady
  - NetworkList 자동: PlayerInventory.SlotStates, KillScores
  - 서버 수동: pending intent, pendingMiniGame, modifier 상태
  - PlayerRegistry: old remove → new register
- [ ] A-4e. PlayerRegistry: `Register(seat, playerState)` seat 기반 lookup

### A-5. 로비 → 다인전 진입

- [ ] A-5a~f. LobbyModeSelectView/Presenter/RoomView, Coordinator 수정, Relay N-1, 씬 분기, 1v1 보존, ConnectionApproval 등록

### A-6. GameScene_Multi 씬

- [ ] A-6a. GameScene 복사 → index 2
- [ ] A-6b. SpawnPoint3D 4개 (Order 0~3)
- [ ] A-6c. EnemyPlayer_0/1/2
- [ ] A-6d. **Visual slot ↔ Match seat 분리**:
  ```
  localSeat = myPlayerIndex
  remoteSeatList = allSeats.Where(s != localSeat).OrderBy(s)
  remoteSeatList[0] → EnemyPlayer_0
  remoteSeatList[1] → EnemyPlayer_1
  remoteSeatList[2] → EnemyPlayer_2
  ```
- [ ] A-6e. MatchCompositionRoot + MatchNetworkState + Multi GameModeRuleSO
- [ ] A-6f. Build Settings

### A-7. N인 HUD (파일 매핑 명시)

| UI 파일 | 변경 |
|---------|------|
| MatchSnapshot | +KillScores[], +LifeStates[], +RequiredPlayerCount |
| GameDataBridge | +MatchNetworkState.Config/KillScores 구독, seat-keyed N인 |
| MatchHudPresenter | 킬 스코어 N인, 라운드(1v1) vs 킬(Multi) |
| GameHudRefs | +N인 상대 온도 바, 킬 스코어 텍스트 |
| GameHudBuilder | N인 상대 bar 동적 생성 |
| OpponentBarPresenter | seat-keyed N인 |
| InventoryPresenter | 드래그-타겟 (B-2c 연계) |
| RoundResultPresenter | Multi 공동 승리 → Lobby 복귀 (Rematch = 1v1 전용) |
| TemperaturePresenter | N인 |

### A-8. 통합 테스트

- [ ] A-8a~h. 로비→3인→씬, seat 안정, MatchNetworkState 초기값+변경, Relay N-1, 1v1 회귀, approval 거절, spawn=SpawnPoint[seat], start gate

---

## Phase B0 — LifeState + DeathService + DamageSource

### B0-1. LifeState + Roster 분리

- [ ] B0-1a. `LifeState { Alive, Ghost }` NV (PlayerState)
- [ ] B0-1b. Roster = 연결만 (Connected/Disconnected/TimedOut), LifeState = 게임, Eligible = 파생

### B0-2. AuthoritativeDeathService

- [ ] B0-2a. TryKill: guard Alive → Ghost → CancelTurnParticipation → ClearAll → guard **두 조건**: `Origin!=Natural && KillerSeat<count` → KillScores++
- [ ] B0-2b. IPlayerTurnCancellation 기본: ResetForNewTurn + IsReady=true + IsFanActive=false
- [ ] B0-2c. 사망 감지: deathQueue → batch 후 FlushDeathQueue → EvaluateRoundMatch
  - **Ghost Frost (PrepPhase 즉시)**: combat batch 밖 → mutation 직후 즉시 FlushDeathQueue + round check
- [ ] B0-2d. DamageSource 정규화:
  - Fan(ChillAura 없음) = `{255, Natural}` (Fan이 아닌 Natural!)
  - Fan(ChillAura 적용 중) = `{ghostSeat, GhostChill}`
  - 규칙: **KillerSeat=255 → 반드시 Natural**
- [ ] B0-2e. ScheduledEffect.SourceSeat 추가, Schedule() 시그니처 확장

### B0-3. 킬 스코어 + 승리 + 라운드 리셋

- [ ] B0-3a. KillScores (MatchNetworkState 소유)
- [ ] B0-3b. SeatSnapshot +killScore, +lifeState
- [ ] B0-3c. 공동 승리 WinnerMask
- [ ] B0-3d. 라운드 종료: Eligible ≤1 or 전원 Ghost
- [ ] B0-3e. 라운드 리셋: 37° + Alive + 기본4종 재구성(Multi 바람막이 1-use) + random N개 + threshold reset + 킬 누적 + modifier reset + cooldown reset + scheduled clear

### B0-4. 테스트 (7건)

---

## Phase B — ActionIntent + Value Snapshots + N인 Resolver + DTO + Balance

### B-1. ActionIntent

- [ ] B-1a~e. readonly struct, QueuedAction.TargetSeat, pendingIntent 보존, IPlayerTurnCancellation 확장, ILocalPlayerCommands

### B-2. 아이템 타겟 (Tarot = **SingleTarget**)

- [ ] B-2a. TargetMode enum
- [ ] B-2b. 21종: SingleTarget **13종** (타로 포함), Self 8종
- [ ] B-2c. 드래그-타겟 UI (1v1 자동 타겟)
- [ ] B-2d. 서버 검증 6단계

### B-3. Value-Type Snapshots (순수 Domain 보장)

- [ ] B-3a. `GameModeRuleSnapshot` — IGameModeRule 전체 value copy
- [ ] B-3b. `ItemRuleSnapshot` — ItemDataSO effect 계산 필요값
- [ ] B-3c. `InventorySnapshot` — seat별 slot 상태
- [ ] B-3d. `ScheduledEffectSnapshot` — +SourceSeat

### B-4. Functional Core

- [ ] B-4a. `MatchCombatSnapshot` — 순수 value-type만: temps, modifiers, lifeStates, inventories, scheduled, **currentKillScores**, environment, `GameModeRuleSnapshot`, `ItemRuleSnapshot[]`
- [ ] B-4b. `MultiCombatResolution` — events + deltas: `InventoryDelta[]`, `ModifierDelta[]`, `ScheduledEffectDelta[]`. **WinnerMask/ResultSequence 미포함** (Shell 결정)
- [ ] B-4c. `ResolveMulti(MatchCombatSnapshot, ActionIntent[])` — NV/MonoBehaviour/SO 참조 없음. Tie-break: 방어우선→Ready→온도→**seat index**. Target Ghost → 취소(재지정 안 함)
- [ ] B-4d. Applicator Shell: deltas 적용 → FlushDeathQueue → KillScores 반영 → WinnerMask 계산 → ResultSequence++ → DTO 생성 + Rpc

### B-5. Network DTO

- [ ] B-5a. CombatEventNetData = 10 bytes. 이벤트 불변식: 1 action → 1 event
- [ ] B-5b. `FixedList512Bytes<CombatEventNetData>` (160 bytes ≤ 512)
- [ ] B-5c. Overflow → state 적용 전 abort → match 강제 종료 (재시도 안 함)
- [ ] B-5d. 1v1: 기존 경로 보존

### B-6. Balance

- [ ] B-6a. 모든 경로 MaxRandomItems cap + GetRandomSlotCount() 헬퍼
- [ ] B-6b. 바람막이 Multi: InitializeBasicItems에 IGameModeRule → IsUnlimited=false, RemainingUses=1. CombatResolver.ApplyDefense → ConsumeItem 정상 소비
- [ ] B-6c. 타로: Multi ItemDropTable 생성 시 필터링 제외 (SO weight 변경 안 함)
- [ ] B-6d. Deathmatch: `min(Rule.DeathmatchGrantCount, cap - currentRandom)` (라운드 1회)
- [ ] B-6e. 동시 Ready = 같은 FixedUpdate frame

### B-7. TurnManager N인

- [ ] B-7a~d. 동적 배열, N루프, Multi/1v1 경로 분기, BuffDebuffSystem N인

### B-8. 테스트 (9건)

---

## Phase C — N인 VFX + 환경

- [ ] C-1a~d. Barrier(이미 N인), VFX visual slot, 순차 연출, FixedList 소비
- [ ] C-2a~c. 사망/Ghost 연출 (alpha 0.4)
- [ ] C-3a~e. 잼민이 N스틸, 앰뷸런스 최저1명 (+동률→seat index 최소), 폭염 오름차순
- [ ] C-4a~d. 테스트

---

## Phase D — Ghost Skill + Multiplier 적용 + UI

### D-1. PlayerModifiers + 실제 적용

- [ ] D-1a. FanSpeedMultiplier/RecoveryMultiplier 추가
- [ ] D-1b. ApplyFanTick: `speed = FanSpeed.Value * modifiers.FanSpeedMultiplier`
- [ ] D-1c. ApplyRecoveryTick: `rate = recoveryRate * modifiers.RecoveryMultiplier`
- [ ] D-1d. **Epoch**: 적용 Prep → 다음 Prep 시작 시 ResetModifiers() 해제 = 1턴
- [ ] D-1e. 쿨다운 서버 저장소 `Dictionary<byte,int>`, PrepPhase 시작 1 감소, 라운드 리셋 클리어

### D-2. Ghost 스킬

- [ ] D-2a~f. FrostStrike/ChillAura, GhostSkillServerRpc 6단계 검증, 덮어쓰기(원복+재설치+killerSeat), FrostStrike 즉시 FlushDeathQueue, active debuff dictionary + 라운드 클리어

### D-3. Ghost UI

- [ ] D-3a~e. 스킬 버튼, 타겟, 쿨다운, PrepPhase 자유, 전원 상태

### D-4. 테스트 (6건)

---

## Phase E — Solo/Bot (별도 빌드/프로세스)

### E-1. Solo 인프라

- [ ] E-1a. Solo 버튼 → 난이도
- [ ] E-1b. **Bot Standalone build** 생성 (build ≠ 실행, P6)
- [ ] E-1c. 실행: `-batchmode -nographics`
- [ ] E-1d. 포트: bind(0) → 실제 endpoint 조회 → Bot arg `--port <actual>` (P6)
- [ ] E-1e~h. BotBootstrap, approval token, 프로세스 관리, Auth 미사용/Lobby 미경유/GameScene

### E-2. Bot AI

- [ ] E-2a~e. IBotBrain BT, BotPlayer, 난이도 3종, 미니게임 자동

### E-3. 외형 + E-4. 테스트

---

## Phase F — 미해결 질문 (8건) + 문서 정리 (5건)

---

## 완료 불변식 (14개)

1. PlayerIndex = seat, match 동안 고정
2. ClientId/OwnerClientId/PlayerIndex 암묵적 변환 금지
3. eligible actor당 intent turn당 최대 1
4. secret intent 비인가 복제 금지
5. 동일 snapshot+intents → 동일 ordered resolution (tie-break: seat)
6. Barrier는 Begin 시점 snapshot만 기다림 — reconnect 추가 금지
7. phase driver와 NV writer 각 하나
8. 2인 기존 golden result 유지
9. collection/DTO 4인/16 event bound, 위반=abort
10. disconnect/despawn/reconnect idempotent
11. CombatResolver는 value-type snapshot만 — NV/MB/SO 금지
12. WinnerMask/ResultSequence는 Shell(Applicator) 결정
13. KillerSeat=255 → Origin=Natural
14. 진행 중 Barrier에 reconnect client 추가 금지

## Task 통계: 165개

| Phase | Tasks |
|-------|-------|
| A | 42 |
| B0 | 17 |
| B | 37 |
| C | 14 |
| D | 22 |
| E | 20 |
| F | 13 |
| **합계** | **165** |
