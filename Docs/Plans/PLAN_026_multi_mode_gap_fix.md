# PLAN_026 — Multi-Mode 갭 수정 (Codex 교차 검증)

> **Status:** ✅ Codex APPROVED **10.0/10** (R1: 3.0 → R2: 6.5 → R3: 8.0 → R4: 9.0 → R5: 10.0) — 구현 대기
> **Context:** PLAN_025 Phase A~D 구현 완료 후 전체 교차 검증에서 11건 이슈 발견 → 2건 비이슈 재분류 → 9건 수정 대상. 1v1 회귀 안전 유지하며 Multi 3~4인 로직 갭 수정.
> **Will NOT touch:** 1v1 전투 경로(Resolve), GameScene(build index 1), Bot/Solo 코드
> **Issue count:** 원본 감사 11건 중 "match end UI"(RoundResultPresenter 정상 동작 확인) + "OpponentBar"(이미 동적 할당) 2건 재분류 → 실제 수정 9건

---

## 수정 대상 이슈 및 우선순위

### P0 — 서버 게임플레이 (권위적 로직 깨짐)

#### FIX-1: MultiCombatApplicator Sabotage MutationType 처리
- **파일:** `Assets/Scripts/Core/Combat/MultiCombatApplicator.cs`
- **문제:** `InventoryDelta.MutationType` (RerollTarget, StealFromTarget) 완전히 무시. Cat/ClawMachine Multi에서 효과 없음

##### 현재 코드 (문제)
```csharp
// MultiCombatApplicator.cs Apply() — inventory 처리
int invCount = Math.Min(resolution.InventoryChangeCount, resolution.InventoryChanges.Length);
for (int i = 0; i < invCount; i++)
{
    var inv = resolution.InventoryChanges[i];
    if (inv.SeatIndex >= seatCount) continue;
    if (inv.Consumed && _accessor.IsConnected(inv.SeatIndex))
    {
        // RemainingUses 감소, 빈 슬롯 비우기만 처리
        // MutationType 완전히 무시!
    }
}
```

##### 수정 방향 (Codex R1 피드백 반영)

**1) 추상화 유지 — ISeatInventoryMutator 인터페이스 도입**
```csharp
public interface ISeatInventoryMutator
{
    bool TryConsume(byte seat, byte slot, short itemId);
    bool TryRerollRandomItems(byte targetSeat);
    bool TryStealRandomItem(byte actorSeat, byte targetSeat);
}
```
- Applicator 생성자에 `ISeatInventoryMutator` 주입 (PlayerState[] 직접 전달 대신)
- 구현체: `SeatInventoryMutator` — 내부에 `PlayerInventory[]` seat 매핑 보유
- 생성 시 seat 인덱스 = 배열 인덱스 검증 (ClientId/발견 순서와 무관하게 논리적 seat)

**2) Consumed과 MutationType 독립 처리**
```csharp
for (int i = 0; i < invCount; i++)
{
    var inv = resolution.InventoryChanges[i];
    
    // 소비 처리 (독립)
    if (inv.Consumed)
        _inventoryMutator.TryConsume(inv.SeatIndex, inv.SlotIndex, inv.ItemId);
    
    // Mutation 처리 (독립 — 하나의 delta가 둘 다 가질 수 있음)
    switch (inv.MutationType)
    {
        case InventoryMutationType.RerollTarget:
            _inventoryMutator.TryRerollRandomItems(inv.TargetSeat);
            break;
        case InventoryMutationType.StealFromTarget:
            _inventoryMutator.TryStealRandomItem(inv.SeatIndex, inv.TargetSeat);
            break;
    }
}
```
- Cat이 소비 delta와 mutation delta를 따로 생성하더라도 applicator는 내부 구현에 의존하지 않음
- `AddInventoryChange` overflow 검증: `InventoryChanges` fixed capacity 확인 필요

**3) Disconnect 정책 (명시)**
- 모든 접근 전에 `SeatIndex`, `TargetSeat`, 배열 길이, null 검증
- `RerollTarget`: 대상 inventory가 없거나 disconnected면 **no-op** (스킵)
- `StealFromTarget`: actor와 target **둘 다 유효할 때만** 원자적 실행
  - actor disconnected → 피해자 아이템만 제거되는 부분 실행 **금지**
- Resolver는 원칙적으로 disconnected actor의 액션을 생성하지 않음
- Applicator는 resolve→apply 사이 상태 변화에 대비해 다시 방어
- 모든 mutation은 서버에서만 실행 (`if (!IsServer) return` guard 포함)

---

#### FIX-2: EnableGhostSystem SO flag 런타임 체크
- **파일:** `Assets/Scripts/Core/Turn/TurnManager.cs`
- **문제:** GhostSkillService가 IsMulti만으로 생성. SO enableGhostSystem=false여도 스킬 가능

##### 수정 방향 (Codex R1 피드백 반영)
```csharp
_ghostSkillService = null;  // 명시적 초기화

if (IsMulti && roster != null && mcr.NetworkState != null)
{
    _deathService = new AuthoritativeDeathService(...);
    
    if (_gameRule != null && _gameRule.EnableGhostSystem)
        _ghostSkillService = new GhostSkillService(...);
}
```

**UseGhostSkillRpc 방어 체크 (전체 목록 — 진입점에서 전부 처리):**
1. `if (!IsServer) return` — 서버 guard
2. `if (!IsMulti) return` — Multi 모드 전용
3. `if (_gameRule == null || !_gameRule.EnableGhostSystem) return` — SO flag
4. `if (_ghostSkillService == null) return` — 서비스 존재
5. **actorSeat 도출 (실패 처리 포함):**
   ```csharp
   if (!_roster.TryGetSeatByClientId(rpcParams.Receive.SenderClientId, out byte actorSeat))
       return;  // 미등록/disconnect 경합 sender → 거부
   if (actorSeat >= seatCount || !_accessor.IsParticipating(actorSeat))
       return;  // sentinel/범위 초과/비참여 → 거부
   ```
   클라이언트 payload에서 actorSeat를 **절대 받지 않음**. TryGet 실패 시 즉시 return (sentinel 방어). accessor 호출 전 범위 검증 선행
6. **actor Ghost 검증:** `if (_accessor.GetLifeState(actorSeat) != LifeState.Ghost) return` — actor가 실제 Ghost인지 확인
7. **actor 연결 상태:** `if (!_accessor.IsConnected(actorSeat)) return`
8. **skill 사용권/횟수:** 해당 skill type의 남은 사용 횟수 > 0 확인 (쿨타임과 별개로 skill-specific permission)
9. **현재 phase:** Ghost skill 사용 가능한 phase인지 확인 (e.g., PrepPhase/AttackPhase만 허용)

**Target/Payload 서버 검증 (Codex R2 피드백 추가):**
- target seat 범위 검증: `0 <= targetSeat < seatCount`
- target 연결/참여 상태 검증: connected && participating
- target LifeState 검증: skill에 따라 Alive 대상만 허용
- self-target 정책: **금지** (actor == target → reject)
- skill payload (skill type, slot index 등) 유효성 검증
- 검증 책임: UseGhostSkillRpc 진입점에서 전부 처리 (service 내부에 위임하지 않음)

**추가 확인:** DeathService가 사망자를 Ghost로 전환하는 경로에서도 `EnableGhostSystem` flag가 적용되는지 검증
- flag=false이면: 사망 → LifeState.Ghost가 아닌 별도 Dead 상태 또는 즉시 탈락 처리

---

### P1 — 4인 사용자 경험

#### FIX-3: Multi Death Sequence RPC
- **파일:** `Assets/Scripts/Core/Turn/TurnManager.cs`
- **문제:** 1v1은 PublishDeathSequence 호출하나 Multi HandleRoundEnd에서 누락. 사망 애니메이션 미재생

##### 수정 방향 (Codex R1 피드백 반영 — 확정)

**단일 호출 지점: FlushDeathQueue 직후 (HandleRoundEnd 아님)**
- HandleRoundEnd에서 보내면 중복 재생 위험 + round cleanup 후 AZPlayerVisual 부재 가능
- FlushDeathQueue가 사망 seat 확정 → 즉시 RPC 전송 → 이후 round 전환

**Batch RPC (byte deathMask) + PresentationBarrier 연동:**
```csharp
[Rpc(SendTo.Everyone)]
private void PresentMultiDeathsRpc(byte deathMask, bool endsRound, uint presentationId)
{
    // 클라이언트: deathMask 비트 순회 → 각 seat AZPlayerVisual.PlayDeathSequence
}
```
- `byte deathMask`: 최대 4석이므로 비트마스크로 충분 (bit 0 = seat 0, ...)
- `presentationId`: PresentationBarrier에서 발급한 ID → stale RPC 구분 + ACK 매칭

**전체 흐름:**
1. 서버: FlushDeathQueue → **이번 flush에서 새로 확정된** 사망 seat 집합 산출
2. PresentationBarrier에서 `presentationId` 발급 + expected-client snapshot 등록
3. `PresentMultiDeathsRpc(deathMask, endsRound, presentationId)` 1회 전송
4. 각 클라이언트: 모든 death sequence **애니메이션 완료 후** ACK 전송 (RPC 수신 시점이 아님)
5. 서버: ACK 수신 시 sender 검증 + presentationId 매칭 → 중복/stale ACK 무시
6. 대기 중 disconnect 발생 시 해당 클라이언트를 expected set에서 제거
7. 전원 ACK 수신 또는 서버 timeout (scene teardown 시 취소)
8. phase 재개는 barrier callback이 아닌 **TurnManager coroutine만** 수행
9. barrier 종료 후 서버가 **terminal outcome 재평가**: terminal(전원 사망 또는 매치 종료 조건) → RoundOver 전환, **아니면**(Ghost 존재하여 경기 계속) → TurnManager가 다음 phase/turn으로 진행

**deathMask 의미:** 전체 사망자가 아닌 **이번 flush에서 새로 확정된 사망자**만 포함

**deathMask == 0 경로 (Codex R4 피드백):**
- `deathMask == 0`이면 (이번 flush에서 신규 사망자 없음): presentationId 발급, barrier 등록, PresentMultiDeathsRpc 전송 및 ACK 대기를 **모두 생략**하고 기존 phase 진행 경로를 계속한다
- 이를 통해 사망 없는 일반 턴에서 불필요한 barrier/timeout 대기 방지

---

#### FIX-4: Progress HUD N인 확장 (Name Box + Crown + Attacker Highlight)
- **파일:** `Assets/Scripts/UI/Game/Build/GameHudBuilder.cs`, `GameHudRefs.cs`, `MatchHudPresenter.cs`
- **문제:** P1/P2 NameBox 2개 고정, Crown binary, Attacker highlight 2인만

##### 수정 방향 (Codex R1 피드백 반영)

**공유 API 변경 범위 (Multi 전용이 아닌 공유 코드 변경):**
- `GameHudRefs`: `P1NameBox/P2NameBox` → `RectTransform[] NameBoxes` 배열화
- `GameHudBuilder.BuildProgressHud`: seat count 파라미터 기반 동적 name box 생성
- `MatchHudPresenter`: Crown/Highlight 로직 배열 기반으로 변경

**1v1 호환 보장:**
- `seatCount == 2`일 때 명시적 legacy branch 유지
  - 기존 x 좌표 동일: `[-145, +145]`
  - anchor, pivot, sizeDelta 동일
  - sibling order 보존
  - 폰트/색상 동일
- 기존 `P1NameBox`/`P2NameBox` 참조의 **전체 call site 정적 검사** 필수
  - Presenter, Builder, HudRefs 이외에도 참조하는 곳이 있는지 Grep 확인
  - 없을 경우 getter 호환 레이어 불필요, 있을 경우 `NameBoxes[0]`/`NameBoxes[1]` 매핑

**Crown 좌표계 안전:**
- 같은 parent → `anchoredPosition` 직접 사용
- 다른 parent → `TransformPoint`/`InverseTransformPoint` 변환
- 단순 좌표 더하기 금지

**N인 레이아웃:**
- 3인: x = `[-180, 0, +180]`
- 4인: x = `[-210, -70, +70, +210]`

---

#### FIX-5: SetTurnCancellation 연결
- **파일:** `Assets/Scripts/Core/Turn/TurnManager.cs`, `Assets/Scripts/Core/Match/AuthoritativeDeathService.cs`
- **문제:** _turnCancellation 항상 null → 사망 시 HasSelectedItem/IsReady stale

##### 수정 방향 (Codex R1 피드백 반영)

**TurnManager가 IPlayerTurnCancellation 구현:**
```csharp
public void CancelTurnParticipation(byte seat)
{
    if (!IsServer) return;                    // 서버 guard
    if (_players == null || seat >= _players.Length) return;
    
    PlayerState player = _players[seat];
    if (player == null) return;
    
    player.ClearPendingIntent();
    player.IsReady.Value = false;
    player.HasSelectedItem.Value = false;
}
```

**WaitForPlayersRoutine에서 연결:**
```csharp
_deathService.SetTurnCancellation(this);
```

**PrepPhaseRoutine ready predicate 수정 (논리 race 방지):**
- 매 반복마다 `Alive && connected && participating` 좌석만 검사
- 초기 participant count **캐싱 금지** — 사망/disconnect로 active count가 변동
- 사망자를 `IsReady=false`로 만들면서 동시에 해당 좌석을 active participant에서 제외해야 함
  - 그렇지 않으면 PrepPhase가 timeout까지 멈춤

**1v1 공유 경로 안전 (Codex R2 피드백):**
- `SetTurnCancellation` 호출은 `IsMulti` guard 내부의 `_deathService` 생성 블록에서만 수행
- 1v1에서는 `_deathService` 자체가 생성되지 않으므로 SetTurnCancellation 미호출
- PrepPhaseRoutine의 ready predicate 변경은 **Multi 전용 분기에만 적용**
  - 1v1 PrepPhaseRoutine은 기존 `!IsMulti` 분기 유지 → 기존 로직 그대로
  - 구현 시 `if (IsMulti) { /* 동적 predicate */ } else { /* 기존 2인 로직 */ }` 명시적 분기

**ClearPendingIntent 검증:**
- queued action, target, mini-game submission 모두 정리하는지 확인
- 부족하면 추가 clear 로직 필요

**Cancellation은 idempotent:**
- 이미 cancelled 된 seat에 다시 호출해도 안전
- pending mini-game 결과와 deadline도 무효화
- cancellation 자체가 phase progression을 직접 호출하지 않음

---

### P2 — 일관성/정리

#### FIX-6: BuffDebuffSystem Ghost LifeState 체크
- **파일:** `Assets/Scripts/Core/Buff/BuffDebuffSystem.cs`
- **문제:** ProcessTurnStart array overload에서 Ghost에게도 지연 효과 발동

##### 수정 방향 (Codex R1 피드백 반영)
- 단순 early return만으로는 ghost/dead 좌석의 debuff가 영구 동결될 수 있음
- **정책 결정 필요:**
  - Option A: 사망 전환 시 해당 seat의 모든 debuff **즉시 clear** (깨끗한 상태로 ghost 전환)
  - Option B: duration만 진행하되 효과 적용은 skip (자연 소멸)
  - **권장: Option A** — Ghost는 새로운 상태이므로 이전 buff/debuff를 가져가지 않음
- 구현: `AuthoritativeDeathService.TryKill` 성공 시 `_buffDebuffSystem.ClearAll(seat)` 호출
- ProcessTurnStart에서도 `LifeState.Alive` guard 추가 (이중 안전)

**1v1 공유 경로 안전 (Codex R2 피드백):**
- `TryKill → ClearAll` 경로: TryKill은 Multi 전용 (`AuthoritativeDeathService`는 `IsMulti` 블록에서만 생성)
  - 1v1에서는 AuthoritativeDeathService 인스턴스 없음 → ClearAll 호출 경로 자체가 존재하지 않음
- `ProcessTurnStart LifeState guard`: 1v1에서는 Ghost LifeState가 존재하지 않으므로 모든 player가 항상 Alive
  - guard 조건 `!= LifeState.Alive` → 1v1에서 항상 false → 기존 로직과 동일하게 전부 통과
  - **확정: Multi overload(`ProcessTurnStart(PlayerState[])`)에만 guard 추가** — 공유 overload 미수정으로 1v1 경로 완전 격리. 1v1은 기존 단일 PlayerState overload 사용하므로 배열 overload 변경의 영향 없음

---

#### FIX-7: FindMultiMatchWinner 동점 규칙 → 공동 승리
- **파일:** `Assets/Scripts/Core/Turn/TurnManager.cs`
- **문제:** 동시 KillsToWin 도달 시 lower seat 묵시적 승리

##### 수정 방향 (Codex R1 피드백 반영)

**byte.MaxValue 대신 명시적 결과 타입:**
```csharp
public enum MultiMatchOutcome : byte
{
    InProgress = 0,
    SingleWinner = 1,
    JointVictory = 2     // 공동 승리
}
```

**FindMultiMatchWinner 반환값 변경 (Codex R2: winnerMask로 확정):**
```csharp
(MultiMatchOutcome outcome, byte winnerMask)
```
- `byte winnerMask`: 비트마스크 (bit 0 = seat 0, bit 1 = seat 1, ...)
- `SingleWinner`: outcome=SingleWinner, winnerMask = `1 << winnerSeat` (1비트만 설정)
- `JointVictory` (동점 2명+): outcome=JointVictory, winnerMask = 동점자 seat 비트 OR (복수 비트)
- `InProgress`: outcome=InProgress, winnerMask=0

**클라이언트에서 동점자 식별:**
```csharp
bool IsWinner(byte mySeat, byte winnerMask) => (winnerMask & (1 << mySeat)) != 0;
```

**RPC + UI:**
- `OnMultiMatchEndClientRpc(MultiMatchOutcome outcome, byte winnerMask)`
- RoundResultPresenter: outcome 분기
  - `SingleWinner` → 기존 VICTORY/DEFEAT (winnerMask에서 seat 추출)
  - `JointVictory` → winnerMask 비트 순회 → 해당 seat 전원에게 "공동 승리!" VICTORY 표시, 나머지 DEFEAT

---

#### FIX-8: CheckMultiMatchEnd 중복 호출 제거
- **파일:** `Assets/Scripts/Core/Turn/TurnManager.cs`
- **문제:** HandleRoundEnd에서 두 번 호출, 첫 결과 폐기
- **수정:** 첫 호출 결과를 변수에 저장하여 재사용
  - 평가 함수가 순수하지 않을 경우 특히 필수 (side effect 중복 방지)

---

#### FIX-9: Ghost disconnect _activeDebuffs 정리
- **파일:** `Assets/Scripts/Core/Match/GhostSkillService.cs`
- **문제:** Dictionary serialize 안 됨. disconnect→reconnect 시 modifier 잔류 가능

##### 수정 방향 (조사 결론 + Codex R2 피드백 반영)
- GhostSkillService는 **서버에서만 유지**되는 서비스 (NetworkBehaviour 아님)
- `_activeDebuffs` Dictionary는 서버 메모리에만 존재 → 직렬화 필요 없음

**수명주기 관리 (subscribe/unsubscribe/Dispose):**
```csharp
public class GhostSkillService : IDisposable
{
    // 생성 시
    public GhostSkillService(..., MatchRoster roster)
    {
        _roster = roster;
        _roster.OnSeatDisconnected += OnSeatDisconnected;
    }
    
    // 정리 시
    public void Dispose()
    {
        if (_roster != null)
            _roster.OnSeatDisconnected -= OnSeatDisconnected;
        RemoveAllActiveDebuffs();  // PlayerModifiers에 적용된 효과 실제 해제
        _activeDebuffs.Clear();
    }
}
```
- TurnManager에서 서비스 파기 시 반드시 `Dispose()` 호출 (씬 범위 TurnManager + 더 긴 수명 roster → stale callback 방지)
- **Dispose() 호출 경로 (전부 보장):**
  - TurnManager.OnNetworkDespawn → `_ghostSkillService?.Dispose()`
  - TurnManager.OnDestroy → `_ghostSkillService?.Dispose()`
  - BootstrapNewMatch (재초기화) → 기존 서비스 `Dispose()` 후 새 인스턴스 생성
- **Dispose()는 멱등적:** 중복 호출 시 이미 해제된 구독/debuff에 대해 안전 (null 체크 + flag)

**disconnect 시 실제 효과 해제:**
```csharp
void OnSeatDisconnected(byte seat)
{
    RemoveDebuffsForSeat(seat);  // 해당 seat가 부여한 + 받은 debuff 모두
}

void RemoveDebuffsForSeat(byte seat)
{
    // 1. _activeDebuffs에서 해당 seat 관련 엔트리 검색
    // 2. 각 엔트리의 실제 PlayerModifiers 효과 rollback (fan multiplier, recovery multiplier 원복)
    // 3. Dictionary에서 제거
}
```
- `_activeDebuffs.Clear()`만으로는 PlayerModifiers에 적용된 효과가 되돌려지지 않음
- `RemoveDebuffsForSeat` → 각 debuff에 대해 PlayerModifiers의 해당 modifier를 원복한 뒤 Dictionary에서 제거

**라운드 reset 시:** `RemoveAllActiveDebuffs()` → 전체 debuff rollback + `_activeDebuffs.Clear()`

---

## 수정 순서

```
FIX-1 (Sabotage — ISeatInventoryMutator)
  → FIX-2 (GhostSystem flag + death-state 경로 검증)
  → FIX-3 (Death RPC — batch deathMask + PresentationBarrier)
  → FIX-5 (TurnCancellation — ready predicate 수정 포함)
  → FIX-6 (Buff ghost — clear 정책 + ProcessTurnStart guard)
  → FIX-4 (HUD N인 — 공유 API 변경 + 1v1 호환 테스트)
  → FIX-7 (Tie-break — MultiMatchOutcome enum)
  → FIX-8 (중복 호출 제거)
  → FIX-9 (Ghost disconnect cleanup)
```

순서 변경 이유: FIX-5가 FIX-3(death 처리)와 FIX-6(buff 정리)의 전제이므로 앞으로 이동. FIX-4는 UI 전용이므로 서버 로직 안정화 후 진행.

---

## 1v1 회귀 안전 검증 (Acceptance Criteria)

> 모든 FIX 완료 후 1v1 회귀 테스트 필수. Multi 코드 변경이 1v1 경로에 영향을 주지 않는지 확인.

### 공유 코드 변경 영향 분석
| FIX | 공유 코드 변경 | 1v1 영향 |
|-----|--------------|---------|
| FIX-1 | MultiCombatApplicator (Multi 전용) | 없음 — 1v1은 별도 경로 |
| FIX-2 | TurnManager ghost service 생성 조건 | `IsMulti` guard 내부 → 1v1 무관 |
| FIX-3 | TurnManager RPC 추가 | `IsMulti` guard → 1v1에서 호출 안 됨 |
| FIX-4 | **GameHudBuilder, GameHudRefs, MatchHudPresenter** | **영향 있음** — seatCount==2 legacy branch 필수 |
| FIX-5 | TurnManager PrepPhaseRoutine + AuthoritativeDeathService | **공유 경로 주의** — SetTurnCancellation은 `IsMulti` 블록에서만 호출, ready predicate 변경은 Multi 분기에만 적용. 1v1 PrepPhaseRoutine은 기존 `!IsMulti` 분기 유지. |
| FIX-6 | BuffDebuffSystem ProcessTurnStart + TryKill→ClearAll | **공유 경로 주의** — TryKill은 Multi 전용(AuthoritativeDeathService 없음). ProcessTurnStart guard는 1v1에서 항상 통과(Ghost 없음). 단위 테스트로 1v1 무영향 확인 필수. |
| FIX-7 | TurnManager FindMultiMatchWinner | Multi 전용 함수 → 1v1 무관 |
| FIX-8 | TurnManager HandleRoundEnd | `IsMulti` 분기 내부 → 1v1 무관 |
| FIX-9 | GhostSkillService | Multi 전용 → 1v1 무관 |

### 1v1 필수 테스트 시나리오
1. **HUD 표시:** 2인 name box 위치/크기/색상 기존과 동일
2. **Crown 위치:** 승리자 표시 정상
3. **Attacker highlight:** 공격 시 name box 하이라이트 정상
4. **아이템 사용:** 모든 카테고리 (Attack/Recovery/Buff/Debuff/Defense/Special) 정상
5. **매치 종료:** VICTORY/DEFEAT 표시 + 리매치/로비 버튼 정상
6. **Disconnect → Reconnect:** 세션 복구 정상

---

## Multi 런타임 테스트 시나리오

1. **Sabotage (FIX-1):** 4인 매치에서 Cat 사용 → 타겟 인벤토리 RerollAllRandom 확인
2. **Sabotage (FIX-1):** ClawMachine 사용 → actor가 target에서 아이템 StealRandomItem 확인
3. **Disconnect Sabotage (FIX-1):** disconnect된 플레이어 대상 Sabotage → no-op 확인
4. **Ghost flag (FIX-2):** EnableGhostSystem=false → 사망 시 Ghost 스킬 사용 불가 확인
5. **Death animation (FIX-3):** 3~4인 동시 사망 → 모든 사망자 애니메이션 재생 확인
6. **Death + PresentationBarrier (FIX-3):** death RPC 후 barrier ACK → round 전환 타이밍 확인
7. **HUD N인 (FIX-4):** Seat 0-3 승리/공격 → name box/crown/highlight 정상 확인
8. **Turn cancellation (FIX-5):** 사망자 다음 턴 PrepPhase에서 제외 확인 (timeout 안 걸림)
9. **Buff ghost (FIX-6):** 사망 시 기존 debuff clear 확인 + Ghost에게 debuff 미적용 확인
10. **동점 (FIX-7):** 동시 KillsToWin 도달 → "공동 승리!" UI 확인
11. **중복 체크 (FIX-8):** HandleRoundEnd에서 CheckMultiMatchEnd 1회만 호출 확인
12. **Ghost disconnect (FIX-9):** Ghost 플레이어 disconnect → _activeDebuffs 정리 확인 + 라운드 리셋 시 전체 clear 확인
