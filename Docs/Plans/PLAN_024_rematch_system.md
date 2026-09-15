# PLAN_024 — Rematch System (하이브리드 C: 재대결 + 로비 귀환) v7

> **Status:** ✅ Complete (v7 구현 완료)
> **Created:** 2026-08-31
> **Dependencies:** None (현재 코드 기반 확장)
> **Scope:** 매치 종료 후 재대결 투표 → 수락 시 동일 방에서 즉시 재시작, 거부/타임아웃 시 로비 귀환
> **Will NOT Touch:** PrepPhase/AttackPhase/ResolutionPhase 전투 로직, 아이템 시스템, Lobby UI, Cosmetic 시스템

---

## Codex Review 반영 이력

<details>
<summary>Review #1 (v1→v2): BLOCKER 5건 + FAIL 8건 — 전부 v2에서 해결</summary>

| # | BLOCKER | v2 수정 |
|---|---------|---------|
| B1 | MatchComplete→RematchVote 같은 프레임 NV 변경 | 서버 6초 대기 후 RematchVote 진입 |
| B2 | MatchManager 서버 소유 RPC 거부 | RequireOwnership=false + SenderClientId 검증 |
| B3 | RematchAccepted 상태 클라 관측 불가 | RematchAccepted 상태 제거, PrepPhase가 클라 트리거 |
| B4 | ResetForRematch 불완전 | 매치 리셋 + 라운드 리셋 분리 |
| B5 | 호스트 종료 시 NV 미도달 | transport disconnect fallback |

| # | FAIL | v2 수정 |
|---|------|---------|
| F1-F8 | 레이스, 상태 전환, 하드코딩, 타이머, snapshot 등 | 전부 v2에서 해결 |

</details>

<details>
<summary>Review #2 (v2→v3): BLOCKER 5건 — 전부 v3에서 해결</summary>

| # | BLOCKER | v3 수정 |
|---|---------|---------|
| R2-1 | Accept후 disconnect | disconnectedMask 분리 |
| R2-2 | 6초 중 disconnect → 혼자 재대결 | roster 고정 + disconnect 추적 |
| R2-3 | StartRematch 소유권 모호 | TryBeginRematchCommit 패턴 |
| R2-4 | deadline+epoch 부재 | RematchVoteEpoch NV, epoch in RPC |
| R2-5 | reset postconditions 불완전 | BootstrapNewMatch 공통 함수 |

</details>

### Review #3 (v3→v4): BLOCKER 3건 + FAIL 5건

| # | 유형 | 문제 | v4 수정 |
|---|------|------|---------|
| R3-1 | **BLOCKER** | 6초 대기 중 disconnect → yield break 하지만 MatchComplete 상태 유지 → 남은 클라가 영구 정지 | `MatchComplete → RematchDeclined` 전이 추가. disconnect 감지 시 `TryTransition(MatchComplete → RematchDeclined)` 후 yield break |
| R3-2 | **BLOCKER** | roster를 MatchComplete 시점에 만들면 직전 disconnect된 seat가 빠질 수 있음. seat mask만으로는 ClientId→seat 역변환 불가 | 매치 시작 시 `_matchRoster = {ClientId → seat}` Dictionary 고정. MatchComplete 진입 시 connected clients와 비교하여 `_disconnectedMask` 초기화 |
| R3-3 | **BLOCKER** | accept가 deadline 직전에 처리되어도 코루틴이 다음 프레임에서 deadline 체크에 걸림. TryBeginRematchCommit → StartRematch 사이 yield에서 disconnect 가능 | RPC 처리 시점(SubmitRematchDecisionRpc 내부)에서 AllAccepted 감지 즉시 `TryBeginRematchCommit` 호출 + latch. 코루틴은 latch된 결과를 yield 없이 실행. 상태 전환(`RematchVote → RoundInProgress`)을 yield 전에 완료 |
| R3-4 | FAIL | inventory 갯수 불일치 (Codex가 8개 지적) | **거짓 양성**: 실제 코드 확인 결과 `GrantRandomItems(4, dropTable)` — 4개가 정확. 현재 코드의 canonical routine 그대로 사용 |
| R3-5 | FAIL | NV callback 순서 미보장 → epoch/deadline 전에 RematchVote 감지 가능 | UI 활성화를 `state == RematchVote && epoch > lastSeenEpoch && deadline > 0` 3조건 gate. `_lastProcessedEpoch` 도입 |
| R3-6 | FAIL | coroutine cleanup 경로 미명시 | try/finally + `_rematchWaitHandle` null 처리. `OnNetworkDespawn`에서 `StopCoroutine(_rematchWaitHandle)` + callback 해제 명시 |
| R3-7 | FAIL | decline도 RematchAcceptMask bit 세움 → 상대가 "accepted" 오해. cinematic 중 declined 시 취소 없음 | NV를 `RematchDecisionMask`로 rename. bit가 "결정 완료"를 의미 (accept/decline 미구분). `RematchDeclined` handler에서 cinematic 즉시 취소 |
| R3-8 | FAIL | EnterRematchVote 중복 호출 시 진행 중 vote의 epoch/decisions 초기화. TryBeginRematchCommit이 latch 없는 단순 검사 | EnterRematchVote 진입 시 expected-state guard 먼저 수행 (MatchComplete가 아니면 return). TryBeginRematchCommit은 `_rematchCommitted` bool latch — 성공 시 true, 이후 호출은 즉시 false 반환 |

### Review #4 (v4→v5): BLOCKER 3건 + FAIL 2건

| # | 유형 | 문제 | v5 수정 |
|---|------|------|---------|
| R4-1 | **BLOCKER** | 코루틴 우선순위 `deadline > committed` → deadline 직전 accept가 latch되어도 다음 프레임에서 deadline 만료로 Declined | **코루틴 우선순위를 `disconnect/decline → _rematchCommitted → deadline`로 변경**. latch가 deadline보다 우선 |
| R4-2 | **BLOCKER** | 매치 점수 리셋 계약 누락 | **거짓 양성**: v4의 CommitRematch에 이미 `P1/P2 RoundWins=0, RoundNumber=0` 명시. v5에서 더 강조 표기 |
| R4-3 | **BLOCKER** | _seatDecisions 재초기화 누락 | **거짓 양성**: v4의 EnterRematchVote에 이미 `_seatDecisions[0] = _seatDecisions[1] = Pending` 명시. v5에서 더 강조 표기 |
| R4-4 | FAIL | NV gate의 `deadline > 0` 조건이 이전 투표의 양수 deadline을 통과시킴 | **`deadline > 0` → `deadline > ServerTime`으로 변경** (과거 deadline은 자연 필터) |
| R4-5 | FAIL | decision mask에서 자기 bit 변경도 "상대 선택 완료"로 표시 가능 | **상대 seat bit만 검사 + epoch 일치 조건 추가. bit==0이면 메시지 클리어** |

### Review #5 (v5→v6): BLOCKER 1건

| # | 유형 | 문제 | v6 수정 |
|---|------|------|---------|
| R5-1 | **BLOCKER** | NV callback이 선언 순서로 실행 → MatchState가 epoch/deadline보다 먼저 도착하면 3조건 gate 실패 → 이후 epoch/deadline 도착 시 gate를 재평가하지 않으면 버튼이 영구 미표시 | **멱등 `ReconcileRematchVoteUI()` 도입**: MatchState, RematchVoteEpoch, RematchDeadlineServerTime의 모든 OnValueChanged에서 호출. `_lastProcessedEpoch`는 UI가 실제로 열린 뒤에만 갱신. 같은 reconciliation에서 RematchDecisionMask도 재판독 |

### Review #6 (v6→v7): FAIL 1건

| # | 유형 | 문제 | v7 수정 |
|---|------|------|---------|
| R6-1 | FAIL | ReconcileRematchVoteUI()의 3조건 gate가 시네마틱 완료 여부를 포함하지 않음 → 시네마틱 중에 NV 도착하면 버튼이 시네마틱 위에 표시 | **4조건 gate로 확장**: `state==RematchVote && _matchCompleteCinematicFinished && epoch>_lastProcessedEpoch && deadline>ServerTime`. `_matchCompleteCinematicFinished`는 MatchComplete 진입 시 false, 시네마틱 정상 완료 시 true + ReconcileRematchVoteUI() 호출 |

---

## Design Pattern Analysis

| Pattern | 적용 | 이유 |
|---------|------|------|
| **State Machine + 전이 함수** | MatchState | TryTransitionMatchState(expected, next) 중앙 보호 |
| **Observer (NV)** | RematchDecisionMask, Deadline, Epoch | 서버 기록 → 클라 OnValueChanged |
| **Command** | SubmitRematchDecisionRpc(accept, epoch) | 단일 RPC, seat별 최초 결정, epoch 검증 |
| **Separated concerns** | MatchManager=투표/score, TurnManager=리셋/phase | 소유권 명확화 |
| **Atomic commit latch** | TryBeginRematchCommit | 한 번만 성공, 이후 false — idempotent |

### API/Package Audit

- **NGO 2.11.2:** `NetworkVariable<byte>`, `NetworkVariable<double>`, `NetworkVariable<uint>`. `RequireOwnership = false` RPC. `NetworkManager.ServerTime.Time` (double). `rpcParams.Receive.SenderClientId`. 전부 확인됨.
- **No package changes required**

---

## 허용 상태 전이 (전체)

```
WaitingToStart  → RoundInProgress
RoundInProgress → RoundEnd
RoundEnd        → RoundInProgress   (다음 라운드)
RoundEnd        → MatchComplete     (Bo3 종료)
MatchComplete   → RematchVote       (투표 시작)
MatchComplete   → RematchDeclined   (6초 대기 중 disconnect) ← NEW in v4
RematchVote     → RematchDeclined   (거부/타임아웃/disconnect)
RematchVote     → RoundInProgress   (재대결 수락 → 새 매치)
```

`CurrentMatchState`는 MatchManager 내부에서만 `.Value` 변경. 외부 코드는 `TryTransitionMatchState` 또는 전용 메서드만 호출.

---

## 변경 후 매치 종료 플로우 (v4)

### Server Flow

```
[서버 — HandleRoundEnd (match complete branch)]
  MatchManager.EndRound(winner) → MatchState = MatchComplete
  // _matchRoster는 매치 시작 시(WaitingToStart→RoundInProgress) 이미 고정됨
  _disconnectedMask = BuildInitialDisconnectMask()
    → _matchRoster의 각 ClientId가 현재 connected인지 확인
    → 이미 끊긴 seat는 mask에 포함
  CurrentPhase = RoundOver
  yield return _waitSix                           (6초 — 시네마틱 보장)
  if (!IsSpawned) yield break
  
  // 6초 중 disconnect 발생 체크
  if (_disconnectedMask != 0):
    MatchManager.TryTransition(MatchComplete → RematchDeclined)  ← v4 NEW
    yield break                                   (RematchVote 진입 안 함)
  
  MatchManager.EnterRematchVote():
    // expected-state guard — 중복 호출 방지
    if (CurrentMatchState.Value != MatchComplete) return false
    _epoch++
    _rematchCommitted = false                     ← latch 초기화
    _seatDecisions[0] = Pending                   ← v5 강조: 명시적 재초기화
    _seatDecisions[1] = Pending                   ← v5 강조: 명시적 재초기화
    RematchVoteEpoch.Value = _epoch
    RematchDecisionMask.Value = 0                 ← v4 RENAMED
    RematchDeadlineServerTime.Value = ServerTime + 15.0
    TryTransition(MatchComplete → RematchVote)
    return true
  
  _rematchWaitHandle = StartCoroutine(WaitForRematchDecision())

[서버 — WaitForRematchDecision]
  try:
    while (true):
      // v5 판정 우선순위: disconnect/decline → committed(latch) → deadline
      if (_disconnectedMask != 0 || AnyExplicitDecline()):
        MatchManager.TryTransition(RematchVote → RematchDeclined)
        yield break
      if (_rematchCommitted):                     ← latch가 deadline보다 우선! (v5 FIX)
        // 상태 전환을 yield 전에 완료
        MatchManager.CommitRematch(_epoch)
        BootstrapNewMatch()
        MatchManager.StartRound()                 → RoundInProgress (yield 전!)
        PublishReviveVisuals()
        yield return PrepPhaseRoutine()
        yield break
      if (ServerTime >= deadline):
        MatchManager.TryTransition(RematchVote → RematchDeclined)
        yield break
      yield return null
  finally:
    _rematchWaitHandle = null

[서버 — SubmitRematchDecisionRpc(bool accept, uint voteEpoch, RpcParams)]
  Guards (전부 → 무시):
    MatchState != RematchVote
    voteEpoch != _epoch
    ServerTime >= deadline
    _matchRoster.TryGetSeat(SenderClientId) 실패   ← v4: 매치 시작 시 고정된 roster 사용
    _seatDecisions[seat] != Pending (최초 결정만)
    _rematchCommitted == true (이미 commit됨)       ← v4 NEW
  Action:
    _seatDecisions[seat] = accept ? Accepted : Declined
    RematchDecisionMask.Value |= (byte)(1 << seat) ← v4 RENAMED
    
    // v4 NEW: accept인 경우 즉시 all-accepted 검사
    if (accept && AllAccepted() && _disconnectedMask == 0):
      if (TryBeginRematchCommit(_epoch)):
        _rematchCommitted = true                   ← latch! 코루틴이 다음 프레임에 감지

[서버 — TryBeginRematchCommit(epoch)]
  // atomic latch — 한 번만 true 반환
  if (_rematchCommitted) return false              ← v4 NEW: 중복 방지
  6조건 재검증:
    1. MatchState == RematchVote
    2. epoch == _epoch
    3. ServerTime < deadline
    4. AnyExplicitDecline() == false
    5. _disconnectedMask == 0
    6. All required seats Accepted
  → 전부 true → return true
  → 하나라도 false → return false

[서버 — CommitRematch(epoch)]  ← 매치 점수 완전 초기화 (v5 강조)
  P1RoundWins.Value = 0          ← Bo3 승수 리셋
  P2RoundWins.Value = 0          ← Bo3 승수 리셋
  RoundNumber.Value = 0          ← 라운드 인덱스 리셋
  RematchDecisionMask.Value = 0  ← 투표 마스크 리셋

[서버 — BootstrapNewMatch()]  ← 공통 함수
  ResetPlayersForNewRound(p1, p2):
    온도 → 37°, IsReady=false, IsFanActive=false
    FanSpeed → DEFAULT, IsFanUpgraded=false
    Inventory.ResetForNewRound()
  GrantStartingItems(p1, p2, dropTable):
    각 플레이어 GrantRandomItems(4, dropTable)     — 4개 정확 (코드 확인 완료)
  _buffSystem.ClearAll()                           — active + delayed 전부
  ActiveEnvironment.Value = None
  TurnNumber.Value = 0
  _modifiers[0].Reset(); _modifiers[1].Reset()
  각 플레이어:
    GetActionQueue().Clear()
    HasSelectedItem.Value = false
    _pendingMiniGameSlot = -1
  LastRoundWinner.Value = -1
  _emoteWindowClosed = false
  // Cosmetic 상태 유지 (동일 세션)

[서버 — StartNextRound 리팩터]
  기존 코드가 수행하는 것과 동일 → BootstrapNewMatch() 호출로 교체
  (MatchManager.StartRound() 호출 유지)

[서버 — FixMatchRoster(players)]  ← v4 NEW
  매치 최초 시작 시 (WaitingToStart → RoundInProgress):
    _matchRoster = new Dictionary<ulong, byte>()
    foreach player in _players:
      _matchRoster[player.OwnerClientId] = player.PlayerIndex

[서버 — BuildInitialDisconnectMask()]  ← v4 NEW
  byte mask = 0
  foreach (clientId, seat) in _matchRoster:
    if (!NetworkManager.ConnectedClientsIds.Contains(clientId)):
      mask |= (byte)(1 << seat)
  return mask

[서버 — OnClientDisconnect(ulong clientId)]
  // 기존 barrier 처리 유지
  if (MatchState == MatchComplete || MatchState == RematchVote):
    if (_matchRoster.TryGetValue(clientId, out byte seat)):
      _disconnectedMask |= (byte)(1 << seat)

[서버 — OnNetworkDespawn]  ← v4 보강
  if (_rematchWaitHandle != null):
    StopCoroutine(_rematchWaitHandle)
    _rematchWaitHandle = null
  StopAllCoroutines()                              — 기존 패턴 유지
  if (IsServer && NetworkManager != null):
    NetworkManager.OnClientDisconnectCallback -= OnClientDisconnectForBarrier
    NetworkManager.OnClientDisconnectCallback -= OnClientDisconnectForRematch  ← v4 NEW
  _barrier?.Reset()
  OnCombatResult = null
  OnEnvironmentAnnounced = null
  if (Instance == this) Instance = null
```

### Client Flow (v4)

```
[MatchComplete 관측 — 6초간 안정]
  HandleMatchEnd() → immutable snapshot 저장 (winner, score)
  시네마틱: fade + "MATCH WIN/LOSE" (snapshot 기반, NV 미참조)
  버튼 표시 안 함 (RematchVote 대기)

[RematchVote 관측 — ReconcileRematchVoteUI() 패턴]  ← v7 FINAL
  멱등 ReconcileRematchVoteUI():
    MatchState, Epoch, Deadline의 모든 OnValueChanged에서 호출
    시네마틱 완료 콜백에서도 호출
    v7 4조건 gate:
      state == RematchVote
      AND _matchCompleteCinematicFinished == true   ← v7 NEW
      AND epoch > _lastProcessedEpoch
      AND deadline > ServerTime
    → 4조건 모두 충족 시에만 UI 활성화
    → _lastProcessedEpoch는 UI가 실제 열린 뒤에만 갱신
    → 같은 호출에서 RematchDecisionMask도 재판독 (상대 결정 표시)
  
  _matchCompleteCinematicFinished 관리:
    MatchComplete 진입 → false
    시네마틱 정상 완료 → true + ReconcileRematchVoteUI() 호출
    CancelCinematic() → true (강제 완료 처리)
  
  표시: [재대결] + [로비로] 버튼 + 타이머 + 상태 텍스트

[재대결 클릭]
  SubmitRematchDecisionRpc(true, epoch)
  두 버튼 모두 비활성화 (interactable=false)
  "수락함 — 상대 대기 중..."

[로비로 클릭]
  SubmitRematchDecisionRpc(false, epoch)
  두 버튼 모두 비활성화
  "로비로 돌아갑니다..."
  RematchDeclined 관측 대기 (2초 fallback timeout) → LeaveAsync

[RematchDecisionMask NV 변경]  ← v4 RENAMED
  v5: epoch 일치 확인 + 상대 seat bit만 검사 (자기 bit 무시)
  상대 bit 켜짐 → "상대가 선택 완료" (accept/decline 미구분)
  상대 bit 0 → 메시지 클리어

[PrepPhase 관측 (재대결 성공)]
  HandlePhaseChanged(PrepPhase) → CancelCinematic() → 전체 UI 초기화
  SnapTempDisplay() → 온도 즉시 37°
  local decision/countdown/snapshot 표시 상태 초기화
  _lastProcessedEpoch 유지

[RematchDeclined 관측]
  CancelCinematic()                                ← v4 NEW: 시네마틱 중이면 즉시 취소
  "재대결이 성사되지 않았습니다" (중립 문구)
  3초 후 자동 LeaveAsync (idempotent _leaveInProgress 게이트)

[Transport disconnect (호스트 나감)]
  기존 NetworkSessionCoordinator.OnClientDisconnect → LobbyScene 복귀
```

---

## Phase A: Server-Side

### A-1: MatchState + 전이 함수

- [x] **A-1a** `NetworkConstants.cs` — MatchState 추가:
  ```csharp
  RematchVote = 4,
  RematchDeclined = 5
  ```

- [x] **A-1b** `MatchManager.TryTransitionMatchState(MatchState expected, MatchState next)`:
  - `CurrentMatchState.Value != expected` → false + 로그
  - 허용 전이 테이블 검증 (**v4: `MatchComplete → RematchDeclined` 포함**)
  - `CurrentMatchState.Value = next; return true`
  - 기존 `.Value =` 직접 대입을 전부 이 메서드로 교체

### A-2: MatchManager 투표 시스템

- [x] **A-2a** NV 추가:
  ```csharp
  public readonly NetworkVariable<byte> RematchDecisionMask = new(0, Everyone, Server);  // v4 RENAMED
  public readonly NetworkVariable<double> RematchDeadlineServerTime = new(0, Everyone, Server);
  public readonly NetworkVariable<uint> RematchVoteEpoch = new(0, Everyone, Server);
  ```

- [x] **A-2b** 서버 전용 상태:
  ```csharp
  enum SeatDecision : byte { Pending, Accepted, Declined }
  SeatDecision[] _seatDecisions = new SeatDecision[2];
  byte _disconnectedMask;
  Dictionary<ulong, byte> _matchRoster;  // v4: 매치 시작 시 고정
  uint _epoch;
  bool _rematchCommitted;                // v4 NEW: atomic latch
  ```

- [x] **A-2c** `bool EnterRematchVote()` (**v4: expected-state guard + latch 초기화**):
  - `if (CurrentMatchState.Value != MatchComplete) return false`
  - `_epoch++; _rematchCommitted = false`
  - `RematchVoteEpoch.Value = _epoch`
  - `_seatDecisions[0] = _seatDecisions[1] = Pending`
  - `RematchDecisionMask.Value = 0`
  - `RematchDeadlineServerTime.Value = NetworkManager.ServerTime.Time + 15.0`
  - `TryTransitionMatchState(MatchComplete, RematchVote)`
  - `return true`

- [x] **A-2d** `[Rpc(SendTo.Server, RequireOwnership = false)] SubmitRematchDecisionRpc(bool accept, uint voteEpoch, RpcParams rpcParams = default)`:
  - Guards: MatchState != RematchVote, voteEpoch != _epoch, ServerTime >= deadline, `_matchRoster` seat 변환 실패, `_seatDecisions[seat] != Pending`, `_rematchCommitted == true` → 전부 무시
  - `_seatDecisions[seat] = accept ? Accepted : Declined`
  - `RematchDecisionMask.Value |= (byte)(1 << seat)`
  - **v4 NEW: accept && AllAccepted() && disconnectedMask==0 → TryBeginRematchCommit → _rematchCommitted = true**

- [x] **A-2e** `bool AllAccepted()` — requiredMask의 모든 seat가 Accepted
- [x] **A-2f** `bool AnyExplicitDecline()` — 어느 seat라도 Declined
- [x] **A-2g** `bool TryBeginRematchCommit(uint epoch)` — **v4: `_rematchCommitted` guard + 6조건 재검증**

- [x] **A-2h** `CommitRematch(uint epoch)`:
  - `P1/P2 RoundWins.Value = 0; RoundNumber.Value = 0`
  - `RematchDecisionMask.Value = 0`

- [x] **A-2i** `ForceDecline()`:
  - `TryTransitionMatchState(RematchVote, RematchDeclined)`

- [x] **A-2j** `FixMatchRoster(PlayerState[] players)` — **v4 NEW**: 매치 시작 시 호출

- [x] **A-2k** `byte BuildInitialDisconnectMask()` — **v4 NEW**: _matchRoster vs ConnectedClientsIds

### A-3: TurnManager 재대결 흐름

- [x] **A-3a** `HandleRoundEnd` 수정 — match complete 분기:
  ```csharp
  _disconnectedMask = _matchManager.BuildInitialDisconnectMask();
  yield return _waitSix;
  if (!IsSpawned) yield break;
  if (_disconnectedMask != 0) {
      _matchManager.TryTransitionMatchState(MatchComplete, RematchDeclined);
      yield break;
  }
  if (!_matchManager.EnterRematchVote()) yield break;
  _rematchWaitHandle = StartCoroutine(WaitForRematchDecision());
  yield return _rematchWaitHandle;
  yield break;
  ```

- [x] **A-3b** `WaitForRematchDecision()` 코루틴 — **v5: try/finally + 우선순위 disconnect/decline → committed → deadline + yield 전 상태 전환**

- [x] **A-3c** `BootstrapNewMatch()` — 공통 리셋 함수:
  - `ResetPlayersForNewRound` + `GrantStartingItems(4)` + `ClearAll` + env/turn reset
  - ActionQueue.Clear + HasSelectedItem=false + _pendingMiniGameSlot=-1 per player
  - _modifiers[0].Reset() + _modifiers[1].Reset()
  - LastRoundWinner = -1, _emoteWindowClosed = false

- [x] **A-3d** `StartNextRound` 리팩터 → `BootstrapNewMatch()` 호출 (중복 제거)

- [x] **A-3e** `OnClientDisconnect` 확장 — **v4: _matchRoster 기반 seat 역변환**:
  - MatchState == MatchComplete || RematchVote → `_disconnectedMask |= (1 << seat)`

- [x] **A-3f** `OnNetworkDespawn` 보강 — **v4: _rematchWaitHandle 정리 + disconnect callback 해제**

- [x] **A-3g** `OnNetworkSpawn` 수정:
  - `FixMatchRoster` 호출 시점 = WaitForPlayersRoutine 완료 후, 첫 StartRound 전
  - `NetworkManager.OnClientDisconnectCallback += OnClientDisconnectForRematch`

---

## Phase B: Client-Side UI

### B-1: GameHudRefs + GameHudBuilder

- [x] **B-1a** `GameHudRefs.cs` 추가: `Button RematchButton`, `TextMeshProUGUI RematchStatusText`

- [x] **B-1b** `BuildCinematicOverlay()` 수정:
  - LobbyBtn → (110, -60), "로비로"
  - RematchBtn → (-110, -60), 200x45, "재대결", 초록 `(0.3f, 0.5f, 0.3f)`
  - RematchStatusText → (0, -110), 400x30
  - 전부 SetActive(false) 초기

### B-2: GameDataBridge + IGameDataBridge

- [x] **B-2a** `IGameDataBridge` 이벤트 추가: `event Action<byte> OnRematchDecisionChanged`

- [x] **B-2b** `GameDataBridge`:
  - `_mm.RematchDecisionMask.OnValueChanged` 구독/해제
  - `MatchSnapshot`에 `RematchDecisionMask`, `RematchDeadlineServerTime`, `RematchVoteEpoch` 필드 추가 + FlushMatch 갱신

### B-3: RoundResultPresenter

- [x] **B-3a** 생성자: RematchButton.onClick + bridge 이벤트 구독

- [x] **B-3b** `HandleMatchEnd` 수정:
  - immutable winner/score snapshot 저장
  - **v7**: `_matchCompleteCinematicFinished = false`
  - 시네마틱만 실행, 버튼 표시 안 함

- [x] **B-3c** **v7: 멱등 `ReconcileRematchVoteUI()` 도입**:
  - MatchState, RematchVoteEpoch, RematchDeadlineServerTime의 모든 OnValueChanged + 시네마틱 완료 콜백에서 호출
  - **v7 4조건 gate**: `state==RematchVote && _matchCompleteCinematicFinished && epoch>_lastProcessedEpoch && deadline>ServerTime`
  - UI 열릴 때만 `_lastProcessedEpoch = epoch` 갱신
  - 같은 호출에서 RematchDecisionMask 재판독 (상대 결정 표시)
  - RematchDeclined → CancelCinematic() 먼저 → "재대결이 성사되지 않았습니다" → 3초 후 LeaveAsync
  - RoundInProgress → CancelCinematic() (이중 안전장치)

- [x] **B-3d** `CinematicRoutine` 수정 — isMatchEnd 분기:
  - **v7**: 시네마틱 완료 시 `_matchCompleteCinematicFinished = true` + `ReconcileRematchVoteUI()` 호출
  - ReconcileRematchVoteUI가 4조건 gate를 통해 적절한 시점에 버튼 표시

- [x] **B-3e** `OnRematchClicked()`:
  - DecisionRpc(true, epoch) + 두 버튼 interactable=false + "수락함 — 상대 대기 중..."

- [x] **B-3f** `OnBackToLobbyClicked()` 수정:
  - RematchVote 상태 → DecisionRpc(false, epoch) + interactable=false + "로비로..." + Declined 대기 (2초 fallback)
  - 그 외 → 기존 즉시 Leave

- [x] **B-3g** `OnRematchDecisionChanged(byte mask)` — **v5: epoch 일치 확인 + 상대 seat bit만 검사** (자기 bit 무시). bit 0 → 메시지 클리어

- [x] **B-3h** 타이머 Tick: `deadline - ServerTime` → 상태 텍스트

- [x] **B-3i** `CancelCinematic()` 수정: RematchButton/StatusText SetActive(false) + local 상태 초기화

- [x] **B-3j** `Dispose()`: 전체 해제 + idempotent `_leaveInProgress` 게이트

---

## Phase C: Integration & Edge Cases

### C-1: Edge Cases

- [x] **C-1a** 호스트 나감 → transport disconnect → 클라 LobbyScene (기존)
- [x] **C-1b** 클라 로비로 → DecisionRpc(false) → Declined → 호스트도 Leave
- [x] **C-1c** Accept 후 disconnect → `_disconnectedMask` 기록 → AllAccepted 불가 → Declined
- [x] **C-1d** MatchComplete 6초 중 disconnect → `_disconnectedMask` 기록 → **v4: TryTransition(MatchComplete → RematchDeclined)** → RematchVote 스킵
- [x] **C-1e** deadline 직전 accept → **v4: RPC 내부에서 즉시 commit latch** → 코루틴이 다음 프레임에서 latch 감지 후 yield 전 상태 전환
- [x] **C-1f** 이전 epoch RPC → epoch 불일치 → 무시
- [x] **C-1g** **v4 NEW**: EnterRematchVote 중복 호출 → expected-state guard가 차단
- [x] **C-1h** **v4 NEW**: TryBeginRematchCommit 중복 호출 → _rematchCommitted latch가 차단

### C-2: 상태 검증

- [x] **C-2a** 재대결 시 양쪽 온도 37°, 아이템 4개, 버프 없음, 환경 없음
- [x] **C-2b** 라운드 승수 0-0, RoundNumber=0 리셋
- [x] **C-2c** Cosmetic NV 유지
- [x] **C-2d** 연속 재대결 3회 (코루틴/이벤트/UI 누적 없음)
- [x] **C-2e** SnapTempDisplay → 37° 즉시 (FlushSeats 선행)
- [x] **C-2f** 결과 화면 immutable snapshot → 리셋 후에도 문구 유지
- [x] **C-2g** ActionQueue/Modifiers/MiniGame/threshold 완전 리셋
- [x] **C-2h** **v5**: NV callback 순서 무관하게 UI 정상 (3조건 gate: state + epoch + deadline>ServerTime)
- [x] **C-2i** **v5 NEW**: decision mask에서 상대 bit만 표시, 자기 bit 무시, epoch 일치 검증

### C-3: Documentation

- [x] **C-3a** RECENT_CHANGES.md 갱신
- [x] **C-3b** ACTIVE_CONTEXT.md 갱신
- [x] **C-3c** CHANGES.md 엔트리

---

## Scope Guard

### Will NOT Touch
- PrepPhase / AttackPhase / ResolutionPhase 전투 로직
- CombatEngine / CombatResolver / ItemEffectApplicator
- Lobby UI
- Cosmetic 시스템 (_hasAcceptedCosmetic 유지)
- 아이템 데이터/드롭 테이블

### Will Touch (명시)
- `NetworkConstants.cs` — MatchState enum (2 values 추가)
- `MatchManager.cs` — NV 3개, TryTransition, 투표/epoch/commit/latch, FixMatchRoster, BuildInitialDisconnectMask
- `TurnManager.cs` — HandleRoundEnd, WaitForRematch, BootstrapNewMatch, OnClientDisconnect, OnNetworkDespawn
- `RoundLifecycleService.cs` — BootstrapNewMatch 연동 (기존 함수 재사용, 새 함수 없음)
- `MatchSnapshot.cs` — RematchDecisionMask, DeadlineServerTime, Epoch 필드
- `GameDataBridge.cs` / `IGameDataBridge.cs` — NV 구독 + 이벤트
- `GameHudRefs.cs` / `GameHudBuilder.cs` — RematchBtn, StatusText
- `RoundResultPresenter.cs` — 재대결 UI + snapshot + 타이머 + 3조건 gate

---

## Summary

| Phase | Tasks | Core Changes |
|-------|-------|--------------|
| A (Server) | 18 | MatchState enum, 전이함수(MatchComplete→Declined 포함), 투표(epoch/deadline/latch), roster Dictionary, disconnect mask, BootstrapNewMatch, OnNetworkDespawn 보강 |
| B (Client) | 13 | 버튼 빌드, Bridge 이벤트(DecisionMask), immutable snapshot, 3조건 gate, 타이머, cinematic 취소, Leave fallback |
| C (Integration) | 15 | Edge case 8건, 상태 검증 9건, 문서 3건 |
| **Total** | **46** | |
