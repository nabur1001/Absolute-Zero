당신은 Unity 6 멀티플레이어 게임 "Absolute Zero"의 시니어 아키텍트입니다.
아래 PLAN_025 v3 (Multi Mode Full Implementation)를 검증해 주세요.

## 프로젝트 핵심 사실

- Unity 6 (6000.3.11f1), NGO 2.11.2, Host-authoritative, DTLS Relay
- 현재 1v1 Bo3 (GameScene) 정상 동작 중, Multi(3~4인 5킬 Deathmatch)를 별도 씬(GameScene_Multi)으로 구축
- PLAN_019의 핵심 계약(stable roster, pure Domain combat, DTO, 10 invariants)을 PLAN_025가 흡수

## 현행 코드 핵심 제약 (변경 불가)

1. `CombatResolver.Resolve(p1Queue, p2Queue, ...)` — 1v1 전용, 보존 필수
2. `CombatResult.P1*/P2*` + `CombatResultData` Event0/Event1 (max 2) — 1v1 DTO, 보존 필수
3. `PlayerState.SelectItemServerRpc(byte slotIndex)` — 현재 targetSeat 없음
4. `ActionQueue.QueuedAction { SlotIndex, ItemData }` — targetSeat 없음
5. `PlayerModifiers` — FanSpeedMultiplier/RecoveryMultiplier 없음
6. `TurnManager._players[2], _modifiers[2]` — 고정 2인 배열
7. `ItemManager.InitializePlayerInventory()` — 4개 랜덤 지급 (1v1에서는 반드시 4개 유지)
8. `NetworkConstants.GameMode { None, TurnBattle }` — Multi/Solo 미존재
9. `NetworkSessionCoordinator.AllocateAsync(maxPlayers)` — 현재 total 전달 (Relay는 joining clients 수 필요)
10. `TemperatureSystem.ApplyDamage(target, rawDamage, DamageFilter, DefenseInfo?)` — Ghost용 DamageFilter.Ghost 미존재
11. `PresentationBarrier.Begin(sequence, expectedClientIds)` — 이미 N인 지원
12. ConnectionApproval 콜백 미등록 (현재 코드에 없음)
13. `PlayerState.BuildContext()` — 첫 비자기 상대 하드코딩 (타겟 보존 안됨)
14. `_pendingMiniGameSlot` — 슬롯만 보존, 타겟 미보존

## 이전 리뷰 이력

### Review #1 (4.0/10): 11 blockers → v2에서 전부 해결
### Review #2 (6.0/10): 5 new blockers

| # | 지적 | v3 반영 |
|---|------|---------|
| N1 | B0 DeathService가 B의 ActionIntent 선행 참조 | Phase A에 `IPlayerTurnCancellation` 인터페이스 선언, B0는 인터페이스만 호출, B가 구현 확장 |
| N2 | ResolveMulti()가 런타임 객체(PlayerState[], TemperatureSystem) 직접 받아 Domain 계약 위반 | `MatchCombatSnapshot + ActionIntent[] + IGameModeRule` → 순수 `MultiCombatResolution` → `CombatResultApplicator.Apply()` (Imperative Shell) |
| N3 | DTO truncate가 권위 이벤트 손실 ("8 max, truncate+warning") | 도메인 최대치 계산: 4sub+4main+4death+4defense=16 events DTO bound, truncate 금지, 초과 시 resolution 에러 |
| N4 | MatchConfig 초기화: Awake 타이밍, 클라이언트 HUD 필요, Solo 경로 | Coordinator → MatchConfig 생성 → `MatchConfigNetData` NV 복제 → 클라이언트 OnValueChanged에서 local SO 선택 |
| N5 | Roster reconnect 프로토콜 부재 | ConnectionApproval callback 6단계 (ParticipantId+token→검증→중복거절→seat rebinding→state hydration→barrier 갱신), disconnect-during-turn=auto-ready |

## v2→v3 추가 수정

- SeatSnapshot.killScore/lifeState: Phase A → Phase B0으로 이동
- `DamageSource { KillerSeat, DamageOrigin }` + `DamageOrigin.Natural` sentinel (자연사=킬없음)
- 바람막이 소비: `ItemEffectApplicator.Apply()` 후 (WindbreakerItem 클래스 미존재 — DefenseItemDataSO 기반)
- 아이템 21종 타겟매핑 명시 (타로 포함): SingleTarget 12, Self 8, Special(타로) 1
- 라운드 시작 시 Multi 2개 재지급 명시 (GAME_DESIGN "ROUND START: items reset")
- Task 수 재집계: 141개 (Phase별 표기)

## 검증 기준 (10점 만점)

### BLOCKER 판정 기준 (하나라도 있으면 NOT APPROVED)
1. **Phase 의존성 위반**: Phase X가 Phase Y의 타입/API를 선행 참조하는데 Y가 X보다 후순위
2. **Domain 순수성 위반**: CombatResolver가 NetworkVariable, MonoBehaviour, PlayerState 런타임 참조
3. **DTO 무결성**: 이벤트 truncate, bounded 미계산, 직렬화 실패 가능
4. **서버 권위 위반**: 클라이언트가 LifeState/KillScore/MatchConfig 직접 write
5. **1v1 회귀**: 기존 Resolve(p1,p2), CombatResultData, InitializePlayerInventory(4개) 변경
6. **초기화 순서**: NV read before server write, Awake에서 다른 씬 객체 참조, SO 런타임 수정
7. **Reconnect 불완전**: seat rebinding 없이 ClientId만 갱신, 상태 hydration 누락
8. **밸런스 경로 누락**: MaxRandomItems cap 미적용 경로 존재

### 점수 기준
- 9~10: APPROVED — 즉시 구현 가능
- 7~8: MINOR REVISION — 비차단 개선사항만
- 5~6: NEEDS REVISION — 차단 이슈 있음
- 1~4: NOT APPROVED — 구조적 결함

### 출력 형식

```
## 점수: X/10 — [APPROVED / MINOR REVISION / NEEDS REVISION / NOT APPROVED]

### BLOCKER (차단)
B1. [제목] — 구체적 문제 + 어느 Phase/Task + 수정 방향
B2. ...

### PARTIAL (부분 수정)
P1. [제목] — 구체적 문제 + 수정 방향
P2. ...

### GOOD (잘된 점)
G1. ...

### 총평
한 문단 요약
```

---

## PLAN_025 v3 전문

# PLAN_025 — Multi Mode Full Implementation (v3 — Codex Review #2 반영)

> **Status:** 📋 Planning (v3 — Codex Review #1 + #2 반영, 재검증 대기)
> **Created:** 2026-09-02 | **Revised:** 2026-09-02
> **Dependencies:** GAME_DESIGN.md, PLAN_018, PLAN_020
> **Absorbs:** PLAN_019 → 역사 문서 전환
> **Scope:** GameScene_Multi 씬 → 3~4인 전투 → Ghost → Solo/Bot → 미해결 질문
> **Will NOT touch:** GameScene (1v1), PLAN_024 Rematch (1v1 전용)

---

## Codex Review #2 핵심 수정 (v2 → v3)

| # | 지적 | 반영 |
|---|------|------|
| N1 | B0 DeathService가 B의 ActionIntent를 선행 참조 | `IPlayerTurnCancellation` 인터페이스를 **Phase A**(기반)에 도입, B0는 인터페이스만 호출, B가 구체 구현 |
| N2 | ResolveMulti()가 런타임 객체를 직접 받아 Domain 계약 위반 | `MatchCombatSnapshot` + `ActionIntent[]` + 불변 `RuleSnapshot` → 순수 `MultiCombatResolution` → applicator가 PlayerState에 반영 |
| N3 | DTO truncate가 권위 이벤트 손실 | 도메인 규칙 기반 **최대 이벤트 수 계산** (max 12: 4sub + 4main + 4death), 초과 시 resolution 실패, truncate 금지 |
| N4 | MatchConfig 초기화: Awake 타이밍, 클라이언트 필요, Solo 경로 | Coordinator → 서버 MatchConfig 생성 → `MatchConfigNetData` NV 복제 → 각 씬 MCR이 local SO 선택 |
| N5 | Roster reconnect 프로토콜 부재 | NGO ConnectionApproval + ParticipantId/session token 검증 + seat rebinding 상세화 |
| P1 | SeatSnapshot.killScore/lifeState가 A에 있으나 B0 상태 | B0으로 이동 |
| P7 | DamageSource 모델, 자연사 sentinel 부재 | `DamageSource { byte seat, DamageOrigin origin }` + `DamageOrigin.Natural` sentinel |
| P9 | 바람막이 소비 위치, 21개 매핑 수, 라운드 지급 | `ItemEffectApplicator.Apply()` 후 소비, 타로카드 포함 21개 명시, 라운드 시작 시 2개(Multi) 명시 |
| P11 | 태스크 수 오류 | 재집계 후 정확한 수 기재 |

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
| 전투 파이프라인 | **Functional Core / Imperative Shell** — 순수 snapshot→resolution→applicator |
| Domain↔Network | **Anti-Corruption Layer** — bounded `CombatResolutionBatchNetData` |
| Ghost | **State Pattern** — `LifeState { Alive, Ghost }` |
| Bot AI | **Behavior Tree** |
| N인 연출 | **Sequencer** — PresentationBarrier + CombatVFXManager |

### Phase 의존성 (v3 확정)

```
Phase A (MatchConfig + Roster + 씬 + 기반 인터페이스)
    ↓
Phase B0 (LifeState + DeathService — 인터페이스 경유)
    ↓
Phase B (ActionIntent 구현 + Combat Snapshot + N인 해결 + DTO + Balance)
    ↓
Phase C (VFX + 환경)
    ↓
Phase D (Ghost Skill + UI)

Phase A ──→ Phase E (Solo/Bot — 별도 프로세스 고유 인프라)

Phase F ← 기획 답변 시
```

### 현행 코드 수정 포인트

| 현행 코드 | 위치 | 해법 |
|---|---|---|
| `_players[2]`, `_modifiers[2]` | TurnManager.cs:50-51 | Roster 기반 동적 |
| `Resolve(p1, p2)` | CombatResolver.cs:11 | 보존 + `ResolveMulti(snapshot, intents, rules)` 신규 |
| `CombatResult.P1*/P2*` | CombatResult.cs | 1v1 보존, Multi용 `MultiCombatResolution` 신규 |
| `CombatResultData` Event0/Event1 (max 2) | CombatResult.cs:32 | 1v1 보존, Multi용 `CombatResolutionBatchNetData` 신규 |
| `SelectItemServerRpc(byte slotIndex)` | PlayerState.cs:181 | `(slot, targetSeat)` 확장 |
| `BuildContext()` — 첫 비자기 선택 | PlayerState.cs:146 | `_pendingIntent.TargetSeat` 기반 |
| `ActionQueue` — no target | ActionQueue.cs:7 | `QueuedAction.TargetSeat` 추가 |
| `PlayerModifiers` — no fan/recovery mult | PlayerModifiers.cs:5 | `FanSpeedMultiplier`, `RecoveryMultiplier` 추가 |
| `GameMode { None, TurnBattle }` | NetworkConstants.cs:23 | `OneVsOne, Multi, Solo` |
| `gameSceneName = "GameScene"` | PlayerSpawnManager.cs:25 | GameMode 분기 |
| `maxPlayers = 2` | NetworkSessionCoordinator.cs:44 | MatchConfig 기반 |
| `ApplyDamage(target, raw, filter, defense)` | TemperatureSystem.cs:56 | Ghost용 `DamageFilter.Ghost` 추가 |
| `InitializePlayerInventory()` — 4 random | ItemManager.cs:54 | `Rule.InitialRandomItems` 참조 (1v1: 4 보존) |
| `AllocateAsync(maxPlayers)` | NetworkSessionCoordinator.cs:285 | `RequiredPlayerCount - 1` |
| `Find("EnemyPlayer")` | AZPlayerVisual.cs | seat→visual slot 매핑 |

---

## Phase A — MatchConfig + Roster + 씬 + 기반 인터페이스

> 목표: 기반 인프라 전부. 이후 Phase가 의존하는 인터페이스도 여기서 선언.

### A-1. GameMode + MatchConfig

- [ ] A-1a. `NetworkConstants.GameMode` enum: `None=0, OneVsOne=1, Multi=2, Solo=3` — `TurnBattle` 제거 + 참조 마이그레이션
- [ ] A-1b. `IGameModeRule` interface:
  ```
  int ModeCapacity             // SO 불변 최대: 1v1=2, Multi=4
  int WinKills                 // 1v1: -1, Multi: 5
  int WinRounds                // 1v1: 2, Multi: -1
  int MaxRandomItems           // 1v1: 8, Multi: 4
  int[] ThresholdGrants        // 1v1: [1,2,3], Multi: [1,1,1]
  float InitialTemperature     // 37
  bool WindbreakerConsumable   // 1v1: false, Multi: true
  bool TarotEnabled            // 1v1: true, Multi: false
  int InitialRandomItems       // 1v1: 4(현행), Multi: 2
  DeathRule DeathRule           // RoundEnd / Ghost
  bool DeathmatchGrantEnabled  // Multi only
  int DeathmatchGrantCount     // 4
  ```
- [ ] A-1c. `GameModeRuleSO : ScriptableObject, IGameModeRule` — 1v1/Multi 각 1개 에셋
  - RULE-001: **ThresholdGrants 등 배열은 Awake에서 runtime immutable copy 생성** (SO 원본 보호)
- [ ] A-1d. `MatchConfig` 서버 소유 구조체 + 복제 경로:
  ```
  서버: NetworkSessionCoordinator가 Lobby metadata에서 GameMode + PlayerCount 읽기
       → MatchConfig { Rule(SO ref), RequiredPlayerCount, Mode } 생성
       → MatchConfigNetData NV로 클라이언트에 복제 (mode byte + count byte)
  클라이언트: MatchConfigNetData 수신 → local GameModeRuleSO 선택
  Solo: Lobby 미경유 → Coordinator가 직접 MatchConfig 생성 (GameMode.Solo, count=2)
  ```
- [ ] A-1e. `MatchConfigNetData : INetworkSerializable` — `{ byte Mode, byte RequiredPlayerCount }`
  - `MatchCompositionRoot`에 `NetworkVariable<MatchConfigNetData>` 서버 write
  - 클라이언트는 OnValueChanged에서 local SO 선택 + HUD 초기화

### A-2. Stable Roster (PLAN_019 §4)

- [ ] A-2a. `MatchRoster` 서버 전용:
  ```csharp
  class MatchRoster
  {
      Dictionary<byte, ParticipantEntry> _seats;
      // playerIndex(0~3) → { ParticipantId, ClientId, SeatState, DisconnectTime }
      enum SeatState { WaitingSpawn, Active, Disconnected, Ghost, Eliminated }
  }
  ```
- [ ] A-2b. Seat 할당: match 시작 시 접속 순서대로 seat 0,1,2,3
  - **PlayerIndex는 match 동안 불변** — reconnect 시에도 변경 없음
- [ ] A-2c. **Reconnect 프로토콜** — NGO ConnectionApproval 활용:
  ```
  1. NetworkManager.ConnectionApprovalCallback 등록
  2. Client 연결 시 approval payload에 ParticipantId(=Unity Auth PlayerId) + session token 포함
  3. 서버 검증:
     - ParticipantId가 Lobby roster에 존재하는지
     - session token 유효한지
     - 해당 seat이 Disconnected 상태인지
     - 동일 ParticipantId의 중복 연결 거절
  4. 승인 시: seat의 ClientId 재바인딩, SeatState → Active
  5. PlayerState 재스폰 + 전체 상태 hydration (온도, 킬스코어, LifeState)
  6. Barrier pending에서 이전 ClientId 제거 + 새 ClientId 추가
  ```
- [ ] A-2d. Disconnect 처리:
  - ClientDisconnect → seat `Disconnected`, 타이머 30초 시작
  - Disconnected seat의 turn eligible 처리: **auto-ready** (행동 없이 자동 준비 완료)
  - 30초 timeout → seat `Eliminated`
  - AI 대체는 Phase E 이후에만 가능, 당분간 Eliminated 처리
- [ ] A-2e. `MatchCompositionRoot`에 `MatchRoster` + `MatchConfig` 소유권

### A-3. 기반 인터페이스 (B0/B 선행 참조 해소)

> N1 해결: DeathService가 B의 ActionIntent를 직접 참조하지 않도록, 취소 계약을 인터페이스로 분리

- [ ] A-3a. `IPlayerTurnCancellation` interface:
  ```csharp
  interface IPlayerTurnCancellation
  {
      void CancelTurnParticipation(PlayerState player);
      // 현재 pending intent / mini-game / ready / fan → 전부 취소
  }
  ```
  - Phase A에서 **선언만**, Phase B에서 ActionIntent 기반 **구현 확장**
  - 기존 PlayerState에 이미 있는 `ResetForNewTurn()` (line 139)과 `_pendingMiniGameSlot=-1` (line 143)을 기반으로 함
- [ ] A-3b. `DamageSource` 값 타입:
  ```csharp
  struct DamageSource
  {
      byte KillerSeat;            // 킬 귀속 seat (Natural이면 255)
      DamageOrigin Origin;        // Item, GhostFrostStrike, GhostChillAura, Fan, DelayedEffect, Natural
  }
  enum DamageOrigin : byte { Item, GhostFrost, GhostChill, Fan, DelayedEffect, Natural }
  ```
  - 자연사(선풍기만으로 0°, Ghost 디버프 없이): `KillerSeat=255, Origin=Natural` → 킬 없음

### A-4. 로비 → 다인전 진입

- [ ] A-4a. `LobbyModeSelectView` "다인전" 버튼 → 인원 선택 (3인/4인)
- [ ] A-4b. `LobbyPresenter.HandleMultiClicked(int count)`
- [ ] A-4c. `LobbyRoomView` 3~4인 슬롯
- [ ] A-4d. `NetworkSessionCoordinator` 수정:
  - `CreateLobbyAsync(GameMode mode, int playerCount)` 오버로드
  - Lobby metadata: `"GameMode"`, `"PlayerCount"` (PascalCase 계약)
  - `_relayGateway.AllocateAsync(playerCount - 1)` — Relay maxConnections = joining clients
  - 씬 분기: `mode == Multi ? "GameScene_Multi" : "GameScene"`
- [ ] A-4e. 기존 1v1: 내부에서 `OneVsOne, 2` 호출 (기존 시그니처 보존)
- [ ] A-4f. ConnectionApproval 등록 (A-2c 연계)

### A-5. GameScene_Multi 씬 생성

- [ ] A-5a. `GameScene` 복사 → `GameScene_Multi`, Build Settings index 2
- [ ] A-5b. SpawnPoint3D 4개 배치 (Order 0~3)
- [ ] A-5c. EnemyPlayer 확장: `EnemyPlayer_0/1/2` (최대 3상대, 초과분 비활성)
- [ ] A-5d. 카메라: M1 확정 (1인칭), FOV 조정
- [ ] A-5e. `MatchCompositionRoot` + Multi용 `GameModeRuleSO`
- [ ] A-5f. Build Settings 갱신

### A-6. N인 HUD

- [ ] A-6a. `TemperaturePresenter` N인 확장
- [ ] A-6b. 닉네임 라벨 N인
- [ ] A-6c. 아이템 배치 UI: 하단 내 아이템 + 상대 N명 영역
- [ ] A-6d. `AZPlayerVisual` seat→visual slot 매핑:
  - 각 클라이언트 기준 localSeat 제외한 remoteSlot 매핑
  - `Find("EnemyPlayer")` → `EnemyPlayer_{remoteSeatIndex}` 바인딩

### A-7. 통합 테스트

- [ ] A-7a. 로비 → 3인 방 → GameScene_Multi 로드
- [ ] A-7b. seat 안정성 확인 (disconnect → reconnect → 같은 seat)
- [ ] A-7c. MatchConfigNetData 클라이언트 수신 확인
- [ ] A-7d. Relay allocation: `playerCount - 1` 확인
- [ ] A-7e. 1v1 회귀 테스트
- [ ] A-7f. ConnectionApproval 거절 케이스 (중복 ParticipantId)

---

## Phase B0 — LifeState + AuthoritativeDeathService

> LifeState와 중앙 사망 처리. `IPlayerTurnCancellation`을 통해 Phase B 구현에 의존하지 않음.

### B0-1. LifeState

- [ ] B0-1a. `PlayerState.CurrentLifeState` NetworkVariable:
  ```csharp
  public enum LifeState : byte { Alive = 0, Ghost = 1 }
  public NetworkVariable<LifeState> CurrentLifeState = new(LifeState.Alive,
      NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
  ```
- [ ] B0-1b. `MatchRoster.SeatState` ↔ `PlayerState.LifeState` 동기화

### B0-2. AuthoritativeDeathService

- [ ] B0-2a. 서버 전용 `AuthoritativeDeathService`:
  ```
  TryKill(PlayerState victim, DamageSource source) → bool
  
  1. guard: victim.LifeState != Alive → return false (중복 방지)
  2. victim.CurrentLifeState.Value = Ghost
  3. _turnCancellation.CancelTurnParticipation(victim)  // ← IPlayerTurnCancellation
  4. victim.GetInventory().ClearAll()
  5. Reset transient modifiers
  6. Record kill: if source.Origin != Natural → KillScores[source.KillerSeat]++
  7. Update MatchRoster seat → Ghost
  ```
- [ ] B0-2b. `IPlayerTurnCancellation` 기본 구현 (Phase A에서 선언):
  - A 단계 기본: `ResetForNewTurn()` + `IsReady=true` + `IsFanActive=false`
  - Phase B에서 ActionIntent 취소로 **확장** (기존 구현 교체)
- [ ] B0-2c. **온도 변경 → 사망 감지 통합**:
  - 모든 온도 변경 후: `if (IsDead(player)) deathQueue.Enqueue(player, source)`
  - **batch 전투 해결 중**: 킬 큐에 쌓음
  - **batch 완료 후**: `FlushDeathQueue()` → 각 TryKill → 그 후 1회 `EvaluateRoundMatch()`
- [ ] B0-2d. `DamageSource` 추적:
  - 각 온도 변경 경로에서 `lastDamageSource` 기록
  - 아이템 공격: `{ attackerSeat, DamageOrigin.Item }`
  - 선풍기 틱: `{ 255, DamageOrigin.Fan }` → Ghost ChillAura 적용 시 `{ ghostSeat, DamageOrigin.GhostChill }`로 갱신
  - Ghost FrostStrike: `{ ghostSeat, DamageOrigin.GhostFrost }`
  - 지연 효과: `{ originalAttackerSeat, DamageOrigin.DelayedEffect }`
  - 자연사 (디버프 없는 선풍기): `{ 255, DamageOrigin.Natural }` → 킬 없음

### B0-3. 킬 스코어 + 승리 판정

- [ ] B0-3a. `NetworkList<int> KillScores` in MatchManager (seat indexed, 서버 write)
  - 초기화: Roster.RequiredPlayerCount 크기, 0 fill
  - late hydration 대응: OnListChanged 구독
- [ ] B0-3b. `SeatSnapshot` 확장 (v2에서 A에 있던 것 → B0으로 이동):
  - `killScore`, `lifeState` 필드 추가
- [ ] B0-3c. 공동 승리: batch `FlushDeathQueue()` 후 1회 `EvaluateMatchEnd()`:
  - winner mask = `KillScores[seat] >= WinKills` 인 모든 seat
  - 2명 이상이면 공동 승리
- [ ] B0-3d. 라운드 종료: 생존자(Alive) ≤ 1 or 전원 Ghost → 라운드 종료
- [ ] B0-3e. 라운드 리셋 (Q20):
  - 전원 37° + LifeState → Alive + 아이템 삭제 + threshold 초기화
  - 킬 스코어 누적
  - Multi: InitialRandomItems(2) 재지급 (매 라운드 시작 시 — GAME_DESIGN §Round Flow: "ROUND START: all at 37°, items reset")
  - 1v1: InitialRandomItems(4) (현행 `RoundLifecycleService.GrantStartingItems` 유지)

### B0-4. 통합 테스트

- [ ] B0-4a. 0° → LifeState.Ghost 전환
- [ ] B0-4b. 중복 TryKill 방지
- [ ] B0-4c. 킬 스코어 + batch 공동 승리
- [ ] B0-4d. 라운드 리셋 후 Alive + 37°
- [ ] B0-4e. 자연사 (Natural) → 킬 없음
- [ ] B0-4f. 1v1 회귀: DeathRule.RoundEnd 분기

---

## Phase B — ActionIntent + Combat Snapshot + N인 해결 + DTO + Balance

### B-1. ActionIntent + Pending Intent

- [ ] B-1a. `ActionIntent` 서버 전용 불변 구조체:
  ```csharp
  readonly struct ActionIntent
  {
      byte SourceSeat;
      byte SlotIndex;
      short ItemId;
      byte TargetSeat;       // 255 = NoTarget (Self 아이템 전용)
      float ReadyTimestamp;
  }
  ```
  - `TargetSeat=255`는 **NoTarget만** 의미, Self 변환은 서버가 canonical seat로 처리
  - PLAN_019 §5: secret intent는 reveal 전까지 서버 전용
- [ ] B-1b. `QueuedAction.TargetSeat` 추가
- [ ] B-1c. `PlayerState._pendingIntent` 보존:
  - `SelectItemServerRpc(byte slot, byte targetSeat)` → 서버 검증 → `_pendingIntent` 저장
  - 미니게임 필요 시: `_pendingMiniGameSlot` + `_pendingIntent` **함께 보존**
  - 미니게임 완료 시: `_pendingIntent`에서 복원 + **아이템 존재·타겟 Alive·phase·ownership 재검증**
  - `BuildContext()` → `_pendingIntent.TargetSeat` 기반 타겟 해결
- [ ] B-1d. `IPlayerTurnCancellation` 확장 구현:
  - Phase A 기본: `ResetForNewTurn()` + IsReady + IsFanActive
  - Phase B 확장: + `_pendingIntent` 클리어 + `_pendingMiniGameSlot=-1` + `HasSelectedItem=false`
- [ ] B-1e. `ILocalPlayerCommands` + `LocalPlayerCommandAdapter` 확장:
  - `SubmitItemSelection(byte slot, byte targetSeat)`

### B-2. 아이템 타겟 시스템

- [ ] B-2a. `TargetMode` enum: `Self`, `SingleTarget` → `ItemDataSO`에 필드
- [ ] B-2b. 21종 아이템 TargetMode 설정:
  ```
  SingleTarget (12종):
    ATK: 부채, 손풍기, 아이스크림, 아.아, 물총, 안아줘요 티셔츠
    DBF: 삼계탕
    SAB: 고양이, 집게손, 청테이프, 레드카드
    SPC: 십자드라이버
  Self (8종):
    REC: 따뜻한 차, 뜨.아, 핫팩, 스마트폰
    BUF: 불닭볶음면, 탄산음료
    DEF: 바람막이, 마스크
  Special (1종):
    타로카드: Self (1v1에서만 사용, Multi 드롭 제거)
  합계: 12 + 8 + 1 = 21
  ```
- [ ] B-2c. 드래그-타겟 UI:
  - SingleTarget → 드래그 → 캐릭터 릴리즈 (Slay the Spire 참조)
  - Self → 기존 클릭
  - **1v1: SingleTarget도 자동 타겟** (상대 1명)
  - 재클릭 취소
- [ ] B-2d. 서버 검증 (PLAN_019 §7):
  - sender owns SourceSeat
  - PrepPhase
  - targetSeat Alive in Roster
  - TargetMode match
  - self/enemy 제한
  - 중복 intent 아님

### B-3. Functional Core: MatchCombatSnapshot → MultiCombatResolution

> N2 해결: 순수 Domain 파이프라인

- [ ] B-3a. `MatchCombatSnapshot` 불변 Domain 입력:
  ```
  float[] TemperaturesAtTurnStart    // seat-indexed
  float[] CurrentTemperatures        // seat-indexed
  PlayerModifiers[] Modifiers        // seat-indexed (value copy)
  LifeState[] LifeStates             // seat-indexed
  EnvironmentType Environment
  IGameModeRule Rule                 // immutable snapshot
  ```
- [ ] B-3b. `MultiCombatResolution` 순수 Domain 출력:
  ```
  CombatEvent[] OrderedEvents        // 최대 MAX_EVENTS
  float[] FinalTemperatures          // seat-indexed
  DamageSource[] LastDamageSources   // seat-indexed
  byte DeadMask                      // 신규 사망자 bits
  byte WinnerMask                    // 5킬 달성자 bits
  int[] ActionOrder                  // 실행 순서
  short[] MainItemIds                // seat-indexed
  short[] SubItemIds                 // seat-indexed
  uint ResultSequence
  ```
- [ ] B-3c. `CombatResolver.ResolveMulti(MatchCombatSnapshot snap, ActionIntent[] intents, ItemDataSO[] registry)` → `MultiCombatResolution`:
  - 순수 함수: PlayerState/NV/MonoBehaviour 참조 없음
  - 행동 순서: 방어 우선 → Ready 타임스탬프 → 동시 시 온도 낮은 순 (C5)
  - 중간 사망: 매 행동 후 0° 체크, 사망자 미실행 행동 취소 (C2)
  - 기존 `Resolve(p1, p2)` **완전 보존**
- [ ] B-3d. **Imperative Shell: Authoritative Applicator**:
  - `CombatResultApplicator.Apply(MultiCombatResolution res, PlayerState[] players, ...)`
  - resolution의 temperature deltas를 PlayerState NV에 반영
  - 사망자 → `deathQueue.Enqueue()` → `FlushDeathQueue()` (B0-2c)
  - **NV write는 applicator에서만** — CombatResolver 내부 금지

### B-4. Network DTO: CombatResolutionBatchNetData

> N3 해결: truncate 금지, 도메인 최대치 계산

- [ ] B-4a. **최대 이벤트 수 계산**:
  - 4인 매치: sub 4 + main 4 + death 4 + defense activation 4 = **최대 16**
  - 실제: 사망 시 행동 취소되므로 현실적 최대 ~12
  - **DTO bound: 16 events** (절대 truncate 없음)
  - resolution이 16 초과 생성 시 → **resolution 자체 에러** (프로그래밍 버그)
- [ ] B-4b. `CombatResolutionBatchNetData : INetworkSerializable`:
  ```
  byte SeatCount
  byte EventCount (≤16)
  FixedArray: CombatEventNetData[16]  // inline, no heap
  FixedArray: float[4] TempBefore
  FixedArray: float[4] TempAfter
  FixedArray: short[4] MainItemIds
  FixedArray: short[4] SubItemIds
  byte DeadMask
  byte WinnerMask
  byte FirstActionSeat
  uint ResultSequence
  ```
  - MTU 크기 테스트: 예상 ~400 bytes (1200 MTU 이내)
- [ ] B-4c. 1v1 호환: 1v1은 기존 `Resolve()` + `CombatResultData` 유지 (변경 없음)

### B-5. Multi 밸런스

- [ ] B-5a. 모든 랜덤 아이템 획득 경로에 `MaxRandomItems` cap:
  | 경로 | 현행 | Multi 변경 |
  |------|------|-----------|
  | `ItemManager.InitializePlayerInventory()` | 4개 | `Rule.InitialRandomItems` (1v1: 4, Multi: 2) |
  | `TemperatureSystem.CheckThresholds()` | [1,2,3] | `Rule.ThresholdGrants` — 지급 전 현재 random count 체크 |
  | Deathmatch Grant | N/A | 4개 (cap 이내) |
  | `GrantStartingItems()` (라운드 시작) | 4개 | `Rule.InitialRandomItems` |
  | Cat reroll 결과 | 무제한 | reroll 후 random count ≤ cap |
  | Claw steal 결과 | +1 | steal 후 random count ≤ cap |
  - `PlayerInventory.GetRandomSlotCount()` 헬퍼 추가
- [ ] B-5b. 바람막이 Multi 1회용:
  - `ItemEffectApplicator.Apply()` 후: `if (Rule.WindbreakerConsumable) inventory.ConsumeSlot(slot)`
  - `WindbreakerItem` 클래스는 존재하지 않음 — 현재 `DefenseItemDataSO` + inventory slot 기반
  - I2: 도구 공격만 방어, I4: 복수 공격 시 첫 1건만
- [ ] B-5c. 타로카드: `TarotEnabled=false` → `ItemDropTable` 생성 시 가중치 0
- [ ] B-5d. 초기/라운드 아이템 지급 정리:
  - **1v1**: 게임 시작 시 4개 (현행 보존) + 매 라운드 시작 시 4개 (현행 보존)
  - **Multi**: 게임 시작 시 2개 + **매 라운드 시작 시 2개** (GAME_DESIGN: "ROUND START: items reset" → 초기화 후 재지급)
- [ ] B-5e. Deathmatch Grant: Alive 2명 남는 순간 양쪽 4아이템 즉시 지급 (라운드당 1회)

### B-6. TurnManager N인

- [ ] B-6a. `_players[]` / `_modifiers[]` / `_tempsAtTurnStart[]` 동적 배열
- [ ] B-6b. PrepPhase N명 루프
- [ ] B-6c. AttackPhase: snapshot 생성 → `ResolveMulti()` → `Applicator.Apply()` (Multi) vs 기존 경로 (1v1)

### B-7. 통합 테스트

- [ ] B-7a. 3인 드래그-타겟 → 검증 → 전투
- [ ] B-7b. 미니게임 경유 후 타겟 보존
- [ ] B-7c. 중간 사망 → 행동 취소
- [ ] B-7d. 5킬 + 공동 승리 (batch)
- [ ] B-7e. 밸런스: 바람막이 1회용, 타로 제거, 초기 2개, Deathmatch 4개
- [ ] B-7f. DTO: 16 event 내 정상 직렬화 + MTU 체크
- [ ] B-7g. 1v1 회귀 (기존 Resolve + CombatResultData)
- [ ] B-7h. Host + remote 3 + 모든 seat 관점

---

## Phase C — N인 VFX + 환경

### C-1. N인 연출 시퀀서

- [ ] C-1a. PresentationBarrier: 이미 N인 지원 — Begin 시점 expected snapshot 사용, disconnect 즉시 제거, ACK = ClientId + Sequence
- [ ] C-1b. CombatVFXManager N인: seat→visual 매핑 (A-6d), 파티클 풀 확장
- [ ] C-1c. Attack Phase 순차 연출 (S1): ActionOrder 순서, 각 3초
- [ ] C-1d. `CombatResolutionBatchNetData` 소비: 새 DTO에서 event 읽기

### C-2. 사망/Ghost 전환 연출

- [ ] C-2a. 0° → 프리즈 → 파티클 → 페이드아웃 → Ghost (LifeState는 B0에서 이미 존재)
- [ ] C-2b. Ghost alpha 0.4 (G6), SortingOrder 조정
- [ ] C-2c. 라운드 종료 연출 + 킬 스코어 표시

### C-3. 환경 효과 N인

- [ ] C-3a. 잼민이: N인 각 1개 스틸
- [ ] C-3b. 앰뷸런스: N인 최저 1명 +10°
- [ ] C-3c. 폭염경보: 온도 오름차순
- [ ] C-3d. 기타: 전원 동일
- [ ] C-3e. EnvironmentRuleService + IGameModeRule 주입

### C-4. 통합 테스트

- [ ] C-4a. 3인 연출 순차
- [ ] C-4b. Ghost 전환 연출
- [ ] C-4c. 환경 N인
- [ ] C-4d. disconnect 중 barrier 처리

---

## Phase D — Ghost Skill + UI

> LifeState + DeathService는 B0 완료. 여기서는 스킬 + UI만.
> Q24 (FrostStrike 데미지), Q26 (Ghost 위치), Q27 (Ghost 스킬 UI)

### D-1. PlayerModifiers 확장

- [ ] D-1a. `FanSpeedMultiplier = 1f`, `RecoveryMultiplier = 1f` 추가
- [ ] D-1b. PrepPhase 끝 `ResetForNewTurn()`에서 1f로 reset

### D-2. Ghost 스킬

- [ ] D-2a. GhostSkillData: FrostStrike (−3~5°, CD 1턴), ChillAura (fan×2, recovery×0.5, 1턴)
- [ ] D-2b. `GhostSkillServerRpc(byte skillIndex, byte targetSeat, RpcParams)`:
  검증 6단계: IsServer, sender==Owner, LifeState==Ghost, PrepPhase, target Alive in Roster, cooldown 만료
- [ ] D-2c. 디버프 **덮어쓰기** (G3, N8 수정):
  ```
  기존 효과 있으면 → modifier 원복 (FanSpeedMult=1, RecoveryMult=1)
  새 효과 원자적 설치
  killerSeat 갱신
  ```
- [ ] D-2d. FrostStrike 적용:
  - `DamageFilter.Ghost` 추가 (바람막이로 방어 불가)
  - `TemperatureSystem.ApplyDamage(target, value, DamageFilter.Ghost, defense: null)`
  - → `AuthoritativeDeathService` 체크 (DamageSource = GhostFrost)
- [ ] D-2e. ChillAura: `Modifiers[target].FanSpeedMultiplier=2, RecoveryMultiplier=0.5`

### D-3. Ghost 킬 귀속

- [ ] D-3a. DamageSource 경로별 (B0-2d에서 정의):
  - FrostStrike → `{ ghostSeat, GhostFrost }`
  - ChillAura 간접 → `{ ghostSeat, GhostChill }` (선풍기 틱 시 lastDamageSource 갱신)
  - 자연사 → Natural (킬 없음)
- [ ] D-3b. Ghost 킬도 KillScores++ 동일

### D-4. Ghost UI

- [ ] D-4a. Ghost PrepPhase UI: 아이템 패널 숨김 → 스킬 버튼 2개 (Q27 TBD — 우선 버튼)
- [ ] D-4b. 타겟 선택: 스킬 버튼 → 생존자 클릭
- [ ] D-4c. 쿨다운 오버레이
- [ ] D-4d. G1: PrepPhase 자유 사용, Ready 없음
- [ ] D-4e. G2: Ghost 시야에 전원 상태 표시

### D-5. 통합 테스트

- [ ] D-5a. FrostStrike → 사망 → 킬 귀속
- [ ] D-5b. ChillAura → 간접 킬 → 킬 귀속
- [ ] D-5c. 디버프 덮어쓰기: 2 Ghost 동일 타겟
- [ ] D-5d. 라운드 종료 → 전원 Alive
- [ ] D-5e. Ghost 5킬 승리

---

## Phase E — Solo / Bot AI (별도 클라이언트)

> B5 확정: 별도 클라이언트 프로세스. BOT_AI_HANDOVER.md 미존재 확인됨.

### E-1. Solo 인프라

- [ ] E-1a. Solo 버튼 → 난이도 선택 (B1)
- [ ] E-1b. localhost Transport 설정:
  - Host: UnityTransport, `127.0.0.1`, **자동 포트 할당** (OS ephemeral port)
  - 포트를 Bot 프로세스에 command-line arg로 전달
- [ ] E-1c. Bot headless build:
  - Unity `-batchmode -nographics` build
  - entry: `BotBootstrap.cs` — command-line 파싱 → Transport 설정 → Client 연결
  - 인자: `--host 127.0.0.1 --port <port> --difficulty <easy|normal|hard> --session-token <token>`
- [ ] E-1d. **Connection approval (Solo)**:
  - Bot이 approval payload에 `session-token` 포함
  - Host가 token 검증 (Coordinator가 생성한 일회용 토큰)
  - 유효하면 seat 1 할당
- [ ] E-1e. 프로세스 라이프사이클:
  - Host: `Process.Start(botExePath, args)` → 10초 연결 타임아웃 → 실패 시 에러 UI
  - 매치 종료: Bot에 `ShutdownRpc` → 프로세스 종료 대기 3초 → 미종료 시 `Process.Kill()`
  - Bot 크래시: Host `OnClientDisconnected` → "봇 연결 끊김" UI
- [ ] E-1f. Auth 미사용: Bot은 Unity Auth 건너뛰기 (로컬 전용)
- [ ] E-1g. Lobby 미경유: Solo는 Lobby 건너뛰기, Coordinator.StartSoloAsync() 직접 Host 시작
- [ ] E-1h. 씬: GameScene 공유 (1v1 규칙, Bo3)

### E-2. Bot AI (BT)

- [ ] E-2a. `IBotBrain`: `SelectItem(ctx) → (slot, target)`, `GetReadyDelay(ctx) → float`
- [ ] E-2b. `BotPlayer`: PrepPhase 감지 → BT → SelectItemServerRpc → delay → ReadyServerRpc
- [ ] E-2c. 난이도: Easy(랜덤) / Normal(가중치) / Hard(최적)
- [ ] E-2d. Ready 타이밍: Easy(5~15s) / Normal(8~12s) / Hard(최적화)
- [ ] E-2e. 미니게임 (B3): `IsBot=true` → 자동 성공 (1~2s 대기)

### E-3. 외형/이름

- [ ] E-3a. Bot 이름 (B4 확정안)
- [ ] E-3b. 기본 캐릭터

### E-4. 통합 테스트

- [ ] E-4a. Solo → 난이도 → Host → Bot 연결
- [ ] E-4b. Bot 아이템+Ready (난이도별)
- [ ] E-4c. Bo3 정상
- [ ] E-4d. Bot 크래시 → Host 감지
- [ ] E-4e. 매치 종료 → Bot 프로세스 정리
- [ ] E-4f. approval token 거절 (잘못된 토큰)

---

## Phase F — 미해결 질문 + 정리

### F-1. 미해결 질문 (8건)

- [ ] F-1a. Q10 (미니게임 상대 화면)
- [ ] F-1b. Q16 (아이템 슬롯 UI)
- [ ] F-1c. Q21 (버프/디버프 중첩)
- [ ] F-1d. Q22 (선택 아이템 공개)
- [ ] F-1e. Q23 (지연 효과 방어)
- [ ] F-1f. Q24 (FrostStrike 수치)
- [ ] F-1g. Q26 (Ghost 위치)
- [ ] F-1h. Q27 (Ghost 스킬 UI)

### F-2. 문서 정리

- [ ] F-2a. PLAN_019 → 역사 문서 표기
- [ ] F-2b. PLAN_021 트리 갱신
- [ ] F-2c. GAME_DESIGN Open Questions 정리
- [ ] F-2d. ACTIVE_CONTEXT 갱신
- [ ] F-2e. CHANGES 기록

---

## 완료 불변식 (PLAN_019 §12)

1. PlayerIndex는 match 동안 고정, reconnect가 seat를 바꾸지 않는다
2. ClientId/OwnerClientId/PlayerIndex 암묵적 변환 금지
3. eligible actor마다 authoritative intent는 turn당 최대 하나
4. secret intent가 비인가 client에 복제되지 않는다
5. 동일 snapshot+rules → 동일 ordered resolution
6. Barrier는 Begin 시점 expected clients만 기다린다
7. phase driver와 NV writer는 각각 하나
8. 2인 기존 gameplay golden result 유지
9. 모든 collection/DTO는 4인/16 event bound 검증
10. disconnect/despawn/reconnect cleanup은 idempotent

## 검증 게이트

각 Phase 완료 시:
1. Host + remote 3 클라이언트
2. 3인/4인 모두
3. 2번째 라운드 이상 (리셋)
4. Prep/Attack/VFX 중 disconnect
5. 모든 local seat 관점 UI
6. 1v1 회귀

## 위험 요소

| 리스크 | 완화 |
|--------|------|
| TurnManager 배열 변경 → 1v1 회귀 | 기존 Resolve() 보존, Multi 전용 경로 |
| CombatResultData 2인 DTO | 1v1 그대로, Multi용 BatchNetData 신규 |
| Ghost Rpc + PrepPhase | 서버 즉시 처리, 턴 구조 독립 |
| Solo 별도 프로세스 | localhost + token + 프로세스 관리 |
| Relay AllocateAsync(N-1) | joining clients 수 전달 |
| AZPlayerVisual 단일 바인딩 | seat→slot 매핑 |
| DTO 16 events | 도메인 최대치 계산, truncate 금지 |
| GrantRandomItems cap 누락 | 모든 경로에 random count 체크 |
| reconnect state hydration | ConnectionApproval + seat rebinding |
| MatchConfig Awake 타이밍 | NV 복제 + OnValueChanged 초기화 |

## Task 통계

| Phase | Tasks |
|-------|-------|
| A — Config + Roster + 씬 + 기반 | 33 |
| B0 — LifeState + Death | 14 |
| B — Intent + Combat + DTO + Balance | 31 |
| C — VFX + 환경 | 14 |
| D — Ghost Skill + UI | 17 |
| E — Solo/Bot | 19 |
| F — 미해결 + 정리 | 13 |
| **합계** | **141** |
