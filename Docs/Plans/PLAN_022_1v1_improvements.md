# PLAN_022 — Sector 9: 1v1 Improvements

> **Status:** ✅ APPROVED for Implementation (v2.2 — all blockers resolved)
> **Created:** 2026-08-27
> **v2 Validated:** 2026-08-27 (Codex cross-validation, 3 prompts — 5 blockers remaining)
> **v2.1 Updated:** 2026-08-27 (B7~B11 + HIGH items addressed)
> **v2.2 Updated:** 2026-08-27 (B12~B13 + HIGH 정합성 3건 해결 — Codex 최종 검증 완료)
> **Depends on:** None (all specs confirmed in GAME_DESIGN.md)
> **Scope:** 9 tasks, 1v1 mode only — no Multi/Ghost/Solo changes

---

## Codex Validation Summary

| Blocker | Issue | Resolution |
|---------|-------|------------|
| B1 | 9-2: 3s envelope는 minimum이며 Cat은 ~3.5s. 최악 ~9.8s → 8s rebuild lock / 10s barrier 충돌 | "3s minimum envelope"로 명칭 변경. REBUILD_LOCK_TIMEOUT 15s, barrier timeout 런타임 floor `Mathf.Max(presentationTimeoutSeconds, 15f)` |
| B2 | 9-3: Cat reroll visual delay 이미 구현됨 (UnlockRebuild at line 553) | 신규 구현 제거 → 검증+튜닝 only로 축소 |
| B3 | 9-4: 시네마틱 ~4.3s > RoundOver `_waitThree` (3s) | `_waitThree` → `_waitSix` (6s)로 서버 대기 상향 (CC-8) |
| B4 | 9-6: Core에서 GameDataBridge (UI) 접근은 asmdef 위반 | `CombatResultData.EndsMatch` 필드 추가, 서버가 계산하여 RPC DTO에 포함 |
| B5 | 9-7: MatchSnapshot은 로컬 UI DTO, 네트워크 직렬화 없음 | `TurnManager.NetworkVariable<byte> FirstReadySeat` 추가, bridge가 구독 |
| B6 | Presenter는 MonoBehaviour (plain class 아님) | InventoryPresenter.Update() 직접 사용, 코루틴 호스팅 불필요 |

### v2.1 Blocker Resolutions

| Blocker | Issue | Resolution |
|---------|-------|------------|
| B7 | 9-6: `MatchManager.WouldEndMatch()` 미존재 — `EndsMatch` 계산 불가 | `WouldEndMatch(int winnerIndex)` 메서드 추가 + MatchManager.cs 파일 목록 추가 |
| B8 | 9-6: PrepPhase 사망 경로 `PublishDeathSequence(i)` → `CombatResultData.EndsMatch` 우회 | `PublishDeathSequence(int loser, bool endsMatch)` 시그니처 확장 + RPC 파라미터 추가 |
| B9 | 9-7: `FirstReadySeat` 기록 시점/로직 미정의 | PrepPhase 종료 후 `ActionQueue.readyTimestamp` 비교, 전체 NV lifecycle 명세 추가 |
| B10 | 9-8: `_bridge`/`_hasSelectedItem` Core asmdef 접근 불가 | `_localPlayer.HasSelectedItem.Value` + `TurnManager.Instance.CurrentPhase.Value` 사용, arrow 활성화 경로 추가 |
| B11 | 9-6: death timing "권장"이 아닌 "계약" 필요 | `PlayDeathSequenceAndWait(bool endsMatch)` Coroutine 반환, death check가 3s padding 선행 |

### v2.1 HIGH Item Resolutions

| Item | Issue | Resolution |
|------|-------|------------|
| H1 | `presentationTimeoutSeconds` GameScene 직렬화 값 10s < 최악 VFX 10.3s | 런타임 floor: `Mathf.Max(presentationTimeoutSeconds, 15f)` |
| H2 | RoundOver 5s → 시네마틱 4.3s + 네트워크 지연 여유 부족 | `_waitFive` → `_waitSix` (6s, 1.7s 여유) |
| H3 | 파일 목록 누락: MatchManager.cs, GameUIManager.cs, GameHudRefs.cs | 파일 목록에 추가 |
| H4 | B1 표 REBUILD_LOCK_TIMEOUT "12s" vs 본문 "15s" 불일치 | 15s로 통일 |

### v2.2 Blocker Resolutions (최종)

| Blocker | Issue | Resolution |
|---------|-------|------------|
| B12 | GameDataBridge lifecycle pseudocode가 `_snapshot`/`OnMatchChanged`/Flush `byte.MaxValue` 사용 — 실제 클래스는 `_currentMatch`/`OnMatchSnapshotChanged`/`_matchDirty` batching | 기존 batching 패턴 준수: `OnMatchNVChanged_Byte` handler, `FlushMatch`에서 `_tm.FirstReadySeat.Value` 읽기, byte.MaxValue 덮어쓰기 금지 |
| B13 | 9-8 arrow 숨김에 `TurnManager.OnPhaseChanged` static event 참조 — 미존재 | Update()에서 매 프레임 phase 확인으로 교체 |

### v2.2 HIGH 정합성 수정

| Item | Issue | Resolution |
|------|-------|------------|
| H5 | GameHudRefs.cs 파일 목록 누락 | Work Scope에 추가 (9-4, 9-7) |
| H6 | RoundOver 시간 문서 내 충돌 (5s/6s 혼재), CC-2/CC-8 중복 | CC-2→CC-8로 통합, B3 표 6s로 통일 |
| H7 | `_iceBreakParticle` 명칭 오류 | `_iceBreakEffectPrefab` + `PlayIceBreakAt(pos)` 사용, emission 런타임 수정 금지 |

---

## Design Pattern Selection

### Selected: Incremental Enhancement + Observer
- **Why:** All 9 tasks are enhancements to existing systems, not new architecture. Each change is localized to 1-2 files. Observer pattern already established for Core→UI communication (RULE-018).
- **Why not Strategy:** A unified timing minimum is simpler than pluggable strategies.
- **Why not Decorator:** Wrapping coroutines complicates Unity's yield-based flow.

### Version Compatibility Check
- Unity 6 (6000.3.11f1): `CanvasGroup.alpha` ✅, `Mathf.SmoothStep` for easing ✅
- NGO 2.11.2: `CombatResultData.NetworkSerialize` 필드 추가 시 양쪽 순서 동일해야 함
- TMP: `TextMeshProUGUI` for nickname/score ✅

---

## Work Scope Declaration

### Files TO MODIFY

| File | Tasks |
|------|-------|
| `Assets/Scripts/Core/Turn/TurnManager.cs` | 9-1, 9-4 (RoundOver wait), 9-7 (NV FirstReadySeat) |
| `Assets/Scripts/Core/Combat/TemperatureSystem.cs` | (no change — immunity in TurnManager) |
| `Assets/Scripts/Core/Combat/CombatVFXManager.cs` | 9-2, 9-5, 9-7 (OnAttackerChanged event) |
| `Assets/Scripts/Core/Combat/CombatResult.cs` | 9-6 (EndsMatch field on CombatResultData) |
| `Assets/Scripts/Core/Combat/ScreenVFXManager.cs` | 9-5 (vignette) |
| `Assets/Scripts/Core/Common/CameraShake.cs` | (no change — existing API sufficient) |
| `Assets/Scripts/Core/Player/AZPlayerVisual.cs` | 9-5, 9-6 |
| `Assets/Scripts/Core/Match/MatchManager.cs` | CC-5 (WouldEndMatch 추가) |
| `Assets/Scripts/Core/Inventory/InventoryPresenter.cs` | 9-8 (arrow in Update) |
| `Assets/Scripts/UI/Game/GameUIManager.cs` | 9-4 (coroutine host) |
| `Assets/Scripts/UI/Game/Presenters/RoundResultPresenter.cs` | 9-4 |
| `Assets/Scripts/UI/Game/Presenters/MatchHudPresenter.cs` | 9-7 |
| `Assets/Scripts/UI/Game/Build/GameHudBuilder.cs` | 9-7 (progress HUD elements) |
| `Assets/Scripts/UI/Game/Build/GameHudRefs.cs` | 9-4, 9-7 (cinematic overlay/score + progress HUD refs) |
| `Assets/Scripts/UI/Game/Bridge/GameDataBridge.cs` | 9-7 (subscribe NV FirstReadySeat) |
| `Assets/Scripts/UI/Game/Bridge/MatchSnapshot.cs` | 9-7 (add FirstReadySeat field) |

### Will NOT Touch
- `ItemDataSO` / `*ItemDataSO` ComputeEffect logic
- `ItemEffectApplicator` NV write pipeline
- `PlayerSpawnManager`, `LobbyManager`, `RelayManager`
- NetworkVariable permissions
- Multi/Ghost/Solo systems
- Scene hierarchy (all UI runtime-built)
- Package versions

---

## Implementation Phases

### Phase A: Core System (9-1, 9-2, 9-3) — Timing Changes
### Phase B: VFX & Animation (9-5, 9-6, 9-9) — Client Presentation
### Phase C: UI Cinematic (9-4) — Round End Sequence + Server Wait
### Phase D: HUD & Feedback (9-7, 9-8) — UI Polish

---

## Task Details

---

### 9-1. PrepPhase 1-Second Immunity

**Design Spec:** First 1 second of PrepPhase — fan natural decrease disabled. Item/recovery normal.

**Current Code:**
- `TurnManager.cs:276-298` — PrepPhaseRoutine: `while (elapsed < duration)` calls `_tempSystem.ApplyFanTick()` every tick
- `TemperatureSystem.cs:37-44` — `ApplyFanTick()` fires per `TICK_INTERVAL = 1.0s`

**Problem with elapsed guard (Codex feedback):**
The tick system fires every 1.0s. `elapsed >= 1.0f` 검사 시 첫 tick이 정확히 ~1.0s에 발생하므로 프레임 overshoot에 따라 면역이 적용되지 않을 수 있음.

**Corrected Implementation — Boolean Flag:**
```csharp
TurnManager.PrepPhaseRoutine:
+ bool skipFirstFanTick = true;
  while (elapsed < currentPrepDuration)
  {
      ...
      while (_tempSystem.ConsumeTick())
      {
          for (int i = 0; i < _players.Length; i++)
          {
-             _tempSystem.ApplyFanTick(_players[i]);
+             if (skipFirstFanTick)
+                 ; // 첫 tick fan만 건너뜀
+             else
+                 _tempSystem.ApplyFanTick(_players[i]);
              _tempSystem.ApplyRecoveryTick(_players[i], recoveryRate);
              _tempSystem.CheckThresholds(...);
          }
+         skipFirstFanTick = false;
      }
  }
```

**Why boolean flag:** 프레임 hitch로 한 프레임에 tick이 여러 번 소비돼도 첫 tick만 정확히 제외. elapsed 비교보다 결정론적.

**Clarifications (Codex):**
- "Recovery 정상 적용" = 기존 규칙 유지. `IsFanActive == true`인 플레이어는 원래 recovery가 안 됨 → 면역 window에서도 동일
- 0° death check는 면역과 무관하게 매 프레임 실행 (기존 규칙)

**Validation:**
- [ ] 첫 tick에서 fan decrease 건너뜀
- [ ] 두 번째 tick부터 정상 적용
- [ ] Recovery 기존 규칙대로 동작
- [ ] 0° death check 매 프레임 실행

**Risk:** Very Low

---

### 9-2. 3-Second Minimum Animation Envelope

**Design Spec:** All item animations unified to **minimum 3s** duration during Attack Phase.

**⚠ Codex Correction:** "정확히 3초"가 아니라 "최소 3초". Cat sequence는 이미 ~3.5s (`PlayCatSpriteSequence` minDuration=1.5s + walk + rumble). 3초 미만 연출은 padding, 초과 연출은 자연 완료.

**Current Code:**
- `CombatVFXManager.cs:428` — `PlayCatSpriteSequence(minDuration=1.5f)` already ~3.5s total
- Per-item timing varies: Fan ~0.9s, Feed 1.0s, Hug ~1.1s
- `InventoryPresenter` rebuild lock timeout: `REBUILD_LOCK_TIMEOUT = 8f` (line 37)
- `PresentationBarrier.WaitForCompletion(timeoutSeconds)` — server-side barrier

**Worst-case total VFX timing (Codex analysis):**
```
0.5s intro + 3.5s action1 (Cat) + 0.3s pause + 3.5s action2 (Cat) + death(2.5s) = ~10.3s
```

**Implementation:**

```csharp
// CombatVFXManager — new constant:
const float MIN_ACTION_DURATION = 3.0f;

// In PlayCombatVFXSequence, wrap each action:
float t0 = Time.time;
yield return StartCoroutine(PlayItemSequence(firstIdx, ...));
float elapsed = Time.time - t0;
if (elapsed < MIN_ACTION_DURATION)
    yield return new WaitForSeconds(MIN_ACTION_DURATION - elapsed);
// (동적 pad이므로 캐시 불필요 — one-shot WaitForSeconds 허용, Codex 확인)

yield return _waitBriefPause;  // 0.3s between actions (envelope 밖)

float t1 = Time.time;
yield return StartCoroutine(PlayItemSequence(secondIdx, ...));
float elapsed2 = Time.time - t1;
if (elapsed2 < MIN_ACTION_DURATION)
    yield return new WaitForSeconds(MIN_ACTION_DURATION - elapsed2);
```

**Time.time 사용 (Codex):** `Time.time`은 현재 모든 VFX coroutine과 일관됨. `Time.unscaledTime`은 pause 시 어긋남.

**Timeout 상향 (blocker B1 + H1 해결):**
```csharp
// InventoryPresenter.cs line 37:
- const float REBUILD_LOCK_TIMEOUT = 8f;
+ const float REBUILD_LOCK_TIMEOUT = 15f;

// TurnManager.cs — barrier timeout 호출 부분 (line 391):
- yield return StartCoroutine(_barrier.WaitForCompletion(presentationTimeoutSeconds));
+ yield return StartCoroutine(_barrier.WaitForCompletion(Mathf.Max(presentationTimeoutSeconds, 15f)));
// GameScene 직렬화 값 10 → 런타임 floor 15s. 씬 값 수정 불필요, 코드에서 보장.
```

**Death-before-padding (blocker B11):** padding 적용 전에 death 여부 확인. 사망 시 즉시 death sequence 진입, 3s padding 적용 안함.
```csharp
// CombatVFXManager — PlayCombatVFXSequence 각 action 후:
float t0 = Time.time;
yield return StartCoroutine(PlayItemSequence(firstIdx, ...));

// ★ death check FIRST — padding 전에
if (firstActionKilled && deadIdx >= 0)
{
    var deadVisual = GetPlayerVisual(deadIdx, nm);
    if (deadVisual != null)
        yield return deadVisual.PlayDeathSequenceAndWait(result.EndsMatch);
    yield break;  // padding 없이 즉시 종료
}

// padding은 death가 아닐 때만
float elapsed = Time.time - t0;
if (elapsed < MIN_ACTION_DURATION)
    yield return new WaitForSeconds(MIN_ACTION_DURATION - elapsed);
```

**Validation:**
- [ ] 3s 미만 연출: padding으로 최소 3s
- [ ] Cat (~3.5s): padding 없이 자연 완료
- [ ] 최악 case ~10.3s: rebuild lock 15s, barrier 15s 이내
- [ ] Death 시 envelope 무시, 즉시 death sequence

**Risk:** Medium — timeout 상향 후 regression test 필요

---

### 9-3. Cat Reroll Timing — ~~신규 구현~~ → 검증+튜닝 Only

**⚠ Codex Correction:** 이미 구현되어 있음.

**현재 구현 (CombatVFXManager.cs:553):**
- Cat이 목적지에 도달한 뒤 `InventoryPresenter.Instance?.UnlockRebuild()` 호출
- 서버는 즉시 reroll하지만, `LockRebuild()`로 클라이언트 UI 갱신이 보류됨
- Cat animation ~1.5s 후 UnlockRebuild → 인벤토리 시각적 갱신

**OnCatRerollVisual 제거 (Codex):** 새 static event는 기존 lock 흐름과 충돌 가능. 불필요.

**Task 축소:**
- [ ] Play Mode에서 Cat 사용 시 reroll 시각 타이밍 확인 (현재 ~1.5s)
- [ ] 디자인 의도가 "~1초"라면 UnlockRebuild 호출 위치를 walk 완료 직후로 조정 검토
- [ ] AttackPhase 중 입력 불가 + rebuild lock으로 stale data 노출 없음 확인

**Risk:** Very Low — 코드 변경 거의 없음

---

### 9-4. Round End Cinematic

**Design Spec:** 1s fade-out → winner name + score rise → 1s fade-in. Final match: extended hold.

**⚠ Codex Correction:** 시네마틱 ~4.3s인데 서버 RoundOver `_waitThree` (3s, line 453) 후 다음 PrepPhase 진입 → 매 라운드 시네마틱이 잘림.

**해결: 서버 RoundOver 대기 상향 (H2 반영: 6s)**
```csharp
// TurnManager.cs HandleRoundEnd (line 453):
- yield return _waitThree;       // 3s
+ yield return _waitSix;         // 6s (시네마틱 4.3s + 1.7s 네트워크 지연 여유)
// + static readonly WaitForSeconds _waitSix = new(6f); 추가 (line 67~74 영역)
// 기존 _waitFive (line 72) 존재 — _waitSix 새로 추가
```

**⚠ Codex Correction:** `RoundResultPresenter`는 plain class이나 `MatchHudPresenter`는 MonoBehaviour. `GameUIManager`를 coroutine host로 전달 (GameUIRoot도 가능).

**Cinematic Flow (수정):**
```
HandleRoundResult/HandleMatchEnd:
  1. Cancel any previous cinematic coroutine
  2. _host.StartCoroutine(CinematicRoutine())

CinematicRoutine (non-final):
  // Phase 1: Fade out (1s)
  overlay alpha 0→1 over 1.0s (Mathf.SmoothStep)
  
  // Phase 2: Show result + rise (0.8s)
  winner text, score text
  anchoredPosition Y: -50→0 over 0.8s (SmoothStep)
  
  // Phase 3: Hold (1.5s)
  
  // Phase 4: Fade in (1s)
  overlay alpha 1→0 over 1.0s (SmoothStep)
  Total: 4.3s → server waits 6s (1.7s 네트워크 여유)

CinematicRoutine (match end):
  Phase 1~3 동일
  Phase 4: hold indefinitely, lobby button visible + interactable
  캐릭터 센터 표시: 아트 의존 → 이번 플랜에서 미구현 (9-4 partial)
```

**Coroutine 관리 (Codex):**
- `Coroutine _activeCinematic` 저장
- 다음 result/PrepPhase/Dispose에서 `StopCoroutine(_activeCinematic)`
- RoundResult와 MatchEnd 연속 호출 시 이전 coroutine 취소

**Lobby 버튼:** 처음 숨김, cinematic 완료 후 활성화 (별도 CanvasGroup 불필요)

**Easing:** `Mathf.SmoothStep` (DOTween 불필요, Codex 확인)

**Validation:**
- [ ] Round result: 1s fade → text rise → 1.5s hold → 1s fade = 4.3s, server 6s 안에 완료
- [ ] Match end: fade + hold + lobby button
- [ ] PrepPhase 진입 시 cinematic 강제 중단
- [ ] Draw: "DRAW!" 텍스트

**Risk:** Medium — server timing 변경은 네트워크 동기화에 영향

---

### 9-5. Damage Effect Upgrade

**Design Spec:** Vignette + camera shake + ice particle burst on hit.

**⚠ Codex Corrections:**
- `!isLocalUser` 조건은 "상대가 나를 공격하는 **로컬 피격 화면**" (plan의 주석 반대)
- 현재 코드에서 `h == 0`에서만 `ScreenVFXManager.PlayHitVFX()` 호출 → shake도 첫 hit에서만 1회
- Fan 3연타에서 shake 3회 호출 문제 → 해당 없음 (h==0 guard)

#### A. Vignette (128×128 Texture2D)
```csharp
// ScreenVFXManager.BuildOverlay():
_vignette = CreateFullScreen(canvasGO.transform, "Vignette", null);
_vignette.texture = GenerateVignetteTexture(128);
// Bilinear filtering, Clamp wrap mode
// OnDestroy: Destroy(_vignette.texture) 정리 필수

// Sibling order: frost 먼저, vignette 다음 (둘 다 같은 Flash에서 alpha 조정)
```

#### B. Camera Shake on Hit (first hit only)
```csharp
// CombatVFXManager hit section (h == 0 block):
ScreenVFXManager.Instance.PlayHitVFX();
CameraShake.Instance?.Shake(0.15f, 0.1f);
```
- CameraShake.OnEnable 위치 저장 → 다른 카메라 이동과 충돌 가능성 주의 (Codex)

#### C. Ice Particle Burst
- 기존 `_iceBreakEffectPrefab` (line 23) + `PlayIceBreakAt(pos)` (line 642) 재사용
- `PlayHitAt()` 호출 시 기존 hit effect + `PlayIceBreakAt(targetPos)` 동시 호출
- emission count 런타임 수정 금지 — 복원 계약 없으므로 기존 ice+final 시스템 동시 재생만 사용

**Validation:**
- [ ] 피격 시: vignette + frost overlay + camera shake + ice particles (첫 hit에서만)
- [ ] Recovery: warm overlay only (vignette/shake 없음)
- [ ] Vignette Texture2D OnDestroy 정리
- [ ] Camera 원위치 복귀

**Risk:** Low-Medium

---

### 9-6. Defeat Animation Fix

**Design Spec:** Non-final: freeze→shatter→Idle return. Final: heavy particles + stay frozen.

**⚠ Codex Corrections:**
- `GameDataBridge.CurrentMatch` 접근은 Core→UI asmdef 위반 (RULE-018)
- VFX 시작 시점에 아직 `MatchManager.EndRound()` 전이라 최종 매치 여부 미확정
- → 서버가 `CombatResultData.EndsMatch` 계산하여 RPC DTO에 포함해야 함

**CC-5: MatchManager.WouldEndMatch() 추가 (blocker B7):**
```csharp
// MatchManager.cs — IsMatchComplete() 아래 추가:
public bool WouldEndMatch(int winnerIndex)
{
    if (winnerIndex == 0)
        return P1RoundWins.Value + 1 >= WINS_TO_MATCH;
    if (winnerIndex == 1)
        return P2RoundWins.Value + 1 >= WINS_TO_MATCH;
    return false; // draw → match 종료 아님
}
// EndRound() 호출 전에 사용 — 현재 wins + 1이 WINS_TO_MATCH 이상인지 예측
```

**CombatResultData 수정:**
```csharp
// CombatResult.cs — CombatResultData struct:
+ public bool EndsMatch;

// NetworkSerialize (기존 필드 뒤, ResultSequence 앞에 추가):
+ serializer.SerializeValue(ref EndsMatch);

// CombatResult.ToNetData():
+ EndsMatch = false  // 기본값, TurnManager에서 설정
```

```csharp
// TurnManager — AttackPhaseRoutine, combat result 생성 시 (line ~389):
var netData = result.ToNetData();
int winnerIndex = result.WinnerIndex;
netData.EndsMatch = winnerIndex >= 0
    && _matchManager != null
    && _matchManager.WouldEndMatch(winnerIndex);
PublishCombatResult(netData);
```

**PlayDeathSequence → PlayDeathSequenceAndWait (blocker B11 — timing contract):**
```csharp
// AZPlayerVisual:
- public void PlayDeathSequence()
+ public void PlayDeathSequence(bool endsMatch = false)

// ★ 신규: coroutine 반환 메서드 — 외부에서 yield 가능
+ public Coroutine PlayDeathSequenceAndWait(bool endsMatch)
+ {
+     PlayDeathSequence(endsMatch);
+     return _deathCoroutine;  // DeathRoutine coroutine ref
+ }
// _deathCoroutine은 이미 line 293에서 저장됨
// 호출부: yield return visual.PlayDeathSequenceAndWait(endsMatch)
```

```csharp
// CombatVFXManager — AttackPhase 사망 경로 (line 146~152, 164~170):
- if (deadVisual != null) deadVisual.PlayDeathSequence();
- yield return _waitDeathSequence;
+ if (deadVisual != null)
+     yield return deadVisual.PlayDeathSequenceAndWait(result.EndsMatch);
// _waitDeathSequence (2.5f) 제거 → 실제 coroutine 완료까지 대기
```

**PrepPhase 사망 경로 확장 (blocker B8):**
```csharp
// TurnManager.cs — PublishDeathSequence (line 104~105):
- void PublishDeathSequence(int loserIndex)
-     => TriggerDeathSequenceRpc(loserIndex);
+ void PublishDeathSequence(int loserIndex, bool endsMatch)
+     => TriggerDeathSequenceRpc(loserIndex, endsMatch);

// TriggerDeathSequenceRpc (line 571~588):
- void TriggerDeathSequenceRpc(int loserIndex)
+ void TriggerDeathSequenceRpc(int loserIndex, bool endsMatch)
  {
      ...
-     if (visual != null) visual.PlayDeathSequence();
+     if (visual != null) visual.PlayDeathSequence(endsMatch);
  }

// HandleRoundEnd (line 441) — 호출부 수정:
- PublishDeathSequence(i);
+ bool endsMatch = _matchManager != null && _matchManager.WouldEndMatch(winnerIndex);
+ PublishDeathSequence(i, endsMatch);
```

**DeathRoutine 수정:**

Non-final (endsMatch == false):
```
freeze sprites → hold 1.5s → ice break SFX + shake + PlayBreakParticles(iceOnly: true)
→ wait 0.5s → SpriteRenderer.color.a fade (freeze overlay) → ResetToIdle
// 캐릭터 제자리 유지, off-screen 이동 제거
// _isDead는 ReviveVisual까지 유지 (Codex)
```

Final (endsMatch == true):
```
freeze sprites → hold 1.5s → ice break SFX + shake + PlayBreakParticles(iceOnly: false, heavy: true)
→ wait 1.0s → 캐릭터 frozen 상태 유지 (Idle 복귀 안함)
```

**Freeze overlay fade:** `SpriteRenderer.color.a` 사용 (CanvasGroup은 SpriteRenderer에 적용 불가, Codex 확인)

**PlayBreakParticles 분리 (Codex + v2.2 수정):**
- 기존: `PlayBreakParticles()` — ice break + final break 파티클 동시 재생
- 수정: non-final은 ice break만, final은 기존 ice+final 동시 재생 (emission 런타임 수정 금지)

**Non-final Idle 복귀 + ReviveVisual 중복 (Codex):** Idle 두 번 설정은 무해. `_isDead`는 ReviveVisual에서만 false로 전환.

**Validation:**
- [ ] Non-final: freeze → shatter → ice particles → fade → Idle (제자리)
- [ ] Final: freeze → shatter → heavy particles → frozen 유지
- [ ] `CombatResultData.EndsMatch` 직렬화 정상 (필드 순서 양쪽 동일)
- [ ] `WouldEndMatch()` EndRound() 호출 전 정확히 예측
- [ ] PrepPhase 사망 경로: `TriggerDeathSequenceRpc(loser, endsMatch)` 정상 전달
- [ ] CombatVFXManager: `PlayDeathSequenceAndWait` yield — `_waitDeathSequence` 미사용
- [ ] Core에서 UI 참조 없음 (asmdef clean)
- [ ] ReviveVisual 기존 동작 유지

**Risk:** Medium — CombatResultData 직렬화 변경 + RPC 시그니처 변경, 양쪽 동일 필수

---

### 9-7. Progress HUD

**Design Spec:** Top-center: nicknames + crown on first-Ready + attacker box highlight.

**⚠ Codex Corrections:**
- MatchSnapshot은 로컬 UI DTO (RPC 아님). 필드 추가해도 네트워크 직렬화 없음
- `ActionQueue.readyTimestamp`는 서버 로컬 → 클라이언트 bridge가 직접 접근 불가
- → `NetworkVariable<byte> FirstReadySeat` 필요 (서버 write, 클라이언트 read)
- HeatWave에서 first-ready ≠ first-attacker → crown과 attacker highlight를 다른 의미로 처리

**Data Flow (수정 + blocker B9 해결 — 전체 NV lifecycle):**

**서버 (TurnManager):**
```csharp
// 선언 (line ~55 영역):
public readonly NetworkVariable<byte> FirstReadySeat = new(
    byte.MaxValue, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

// PrepPhase 시작 시 초기화 (PrepPhaseRoutine 첫 줄):
FirstReadySeat.Value = byte.MaxValue;

// ★ 기록 시점: PrepPhase 종료 후, AttackPhase 진입 전 (line ~317 이후)
// 모든 플레이어 ready 후 (자발적 + ForceReady 포함):
byte firstSeat = byte.MaxValue;
float earliestTimestamp = float.MaxValue;
for (int i = 0; i < _players.Length; i++)
{
    var q = _players[i].GetActionQueue();
    if (q.isReady && q.readyTimestamp < earliestTimestamp)
    {
        earliestTimestamp = q.readyTimestamp;
        firstSeat = (byte)i;
    }
}
FirstReadySeat.Value = firstSeat;
```

**클라이언트 (GameDataBridge) — 기존 batching 패턴 준수:**
```csharp
// SubscribeTurnManager() (line 108~122 영역):
+ _tm.FirstReadySeat.OnValueChanged += OnMatchNVChanged_Byte;

// 신규 handler (line 268~270 영역):
+ void OnMatchNVChanged_Byte(byte _, byte __) => _matchDirty = true;

// ReadCurrentMatchValues() (line 132~147):
// 기존 MatchSnapshot 초기화 블록 맨 뒤에 추가:
+   FirstReadySeat = _tm.FirstReadySeat.Value,

// FlushMatch() (line 322~345):
// _tm != null 블록 안에 추가:
+   _currentMatch.FirstReadySeat = _tm.FirstReadySeat.Value;
// ⚠ byte.MaxValue로 덮어쓰기 금지 — 서버가 reset하면 NV 자체가 byte.MaxValue가 됨

// OnDestroy unsubscribe (line 382~386 영역):
+   _tm.FirstReadySeat.OnValueChanged -= OnMatchNVChanged_Byte;
```

**MatchSnapshot (Bridge/MatchSnapshot.cs):**
```csharp
+ public byte FirstReadySeat;  // byte.MaxValue = 미확정
```

**UI (MatchHudPresenter):**
```
OnMatchChanged → snapshot.FirstReadySeat != byte.MaxValue 이면 crown 표시
CombatVFXManager.OnAttackerChanged → box highlight 전환
```

**OnAttackerChanged event 관리 (Codex):**
- `PlayCombatVFXSequence`에서 각 action 시작 시 fire
- try/finally에서 `-1` (clear) event 전송 → 라운드 중단 시 highlight 해제
- MatchHudPresenter는 phase 이탈 시에도 highlight 숨김

**Crown vs Attacker (Codex):**
- Crown = "누가 먼저 Ready를 눌렀는가" (first-ready) → AttackPhase 동안 고정 표시
- Highlight = "현재 누구의 연출이 재생 중인가" (attacker) → 연출마다 전환
- HeatWave(폭염경보: 최저온도 선공)에서 first-ready ≠ first-attacker 가능 → 별도 처리

**Nickname placeholder:** `"Player 1"` / `"Player 2"` (Sector 8 닉네임 시스템까지 placeholder. Func delegate 과하다는 Codex 의견 반영 — 향후 `SeatSnapshot.DisplayName`으로 확장)

**Crown/Highlight 가시성:**
- Crown: AttackPhase 동안만 표시 → PrepPhase/RoundOver에서 숨김
- Highlight: 연출 중에만 → OnAttackerChanged(-1) or phase 이탈 시 clear

**Box highlight 구현:** `Image.color` 변경 (solid background) — 가장 단순, custom shader 불필요

**Crown asset:** 별도 UI `Image` (emoji 텍스트 or 런타임 Texture2D). TMP inline sprite보다 Image가 관리 용이 (Codex)

**Validation:**
- [ ] Top-center: 두 플레이어 이름 + 중앙 스코어
- [ ] Crown: first-ready player 옆에 AttackPhase 동안 표시
- [ ] Highlight: 현재 attacker box 색상 변경
- [ ] Phase 이탈 시 crown/highlight 숨김
- [ ] HeatWave에서 crown ≠ highlight 정상 분리

**Risk:** Medium — NetworkVariable 추가 + static event 관리

---

### 9-8. Item Selection Arrow

**Design Spec:** Arrow above selected item with bounce. Ready button lit sprite.

**⚠ Codex Correction:** `InventoryPresenter`는 MonoBehaviour (`line 14: public class InventoryPresenter : MonoBehaviour`). Update() 직접 사용 가능.

**Implementation (blocker B10 수정 — Core asmdef 호환):**

**⚠ v2.1 수정:** `_bridge`와 `_hasSelectedItem`은 Core asmdef에서 접근 불가.
- Phase 확인: `TurnManager.Instance.CurrentPhase.Value` (Core 내부 — 접근 가능)
- 선택 확인: `_localPlayer.HasSelectedItem.Value` (PlayerState NV — Core 내부)

```csharp
// InventoryPresenter — new fields:
GameObject _selectionArrow;
SpriteRenderer _arrowRenderer;

void EnsureArrow()
{
    if (_selectionArrow != null) return;
    _selectionArrow = new GameObject("SelectionArrow");
    _arrowRenderer = _selectionArrow.AddComponent<SpriteRenderer>();
    _arrowRenderer.sprite = GenerateArrowSprite(); // runtime Texture2D triangle
    _arrowRenderer.sortingLayerName = /* 아이템과 동일한 sorting layer */;
    _arrowRenderer.sortingOrder = 95;
    _selectionArrow.SetActive(false);
}
```

**Arrow 활성화 경로 (v2.1 추가):**
```csharp
// UpdateSelectionVisuals() — 아이템 선택/해제 시 호출:
void UpdateSelectionVisuals()
{
    EnsureArrow();
    bool visible = _localPlayer != null
        && _localPlayer.HasSelectedItem.Value                     // 서버 확인된 선택
        && _confirmedSlotIndex >= 0
        && _confirmedSlotIndex < _localViews.Length
        && _localViews[_confirmedSlotIndex] != null
        && TurnManager.Instance != null
        && TurnManager.Instance.CurrentPhase.Value == TurnPhase.PrepPhase;

    _selectionArrow.SetActive(visible);
    if (visible)
    {
        var basePos = _localViews[_confirmedSlotIndex].transform.position
                    + new Vector3(0f, 0.6f, 0f);
        _selectionArrow.transform.position = basePos;
    }
}
```

**Bounce 애니메이션 (Update):**
```csharp
// 기존 Update()에 추가:
void Update()
{
    // ... existing rebuild/hover logic ...

    // Arrow bounce (SetActive 여부는 UpdateSelectionVisuals에서 관리)
    if (_selectionArrow != null && _selectionArrow.activeSelf
        && _confirmedSlotIndex >= 0
        && _confirmedSlotIndex < _localViews.Length
        && _localViews[_confirmedSlotIndex] != null)
    {
        var basePos = _localViews[_confirmedSlotIndex].transform.position
                    + new Vector3(0f, 0.6f, 0f);
        float bounce = Mathf.Sin(Time.time * 4f) * 0.05f;
        _selectionArrow.transform.position = basePos + new Vector3(0f, bounce, 0f);
    }
}
```

**Arrow 숨김 경로 (blocker B12 수정 — OnPhaseChanged 미존재):**
- Phase 이탈 시: Update()에서 매 프레임 확인 (이벤트 없이 가장 단순):
```csharp
// Update() 상단에 추가 (bounce 로직 전):
if (_selectionArrow != null && _selectionArrow.activeSelf
    && (TurnManager.Instance == null
        || TurnManager.Instance.CurrentPhase.Value != TurnPhase.PrepPhase))
{
    _selectionArrow.SetActive(false);
}
```
- 아이템 선택 해제 시: `UpdateSelectionVisuals()` 재호출 → visible = false

**Arrow 표시 조건 정리:**
- `_localPlayer.HasSelectedItem.Value == true` (서버 확인 — Core NV)
- `_confirmedSlotIndex >= 0` (slot 선택됨 — Core 로컬)
- `TurnManager.Instance.CurrentPhase.Value == PrepPhase` (Core 내부)
- Phase → Attack 전환 시 즉시 숨김 (이벤트 기반)

**Arrow sprite:** Runtime Texture2D (삼각형) 1회 생성. 정식 sprite asset 추가 시 교체.

**Sorting:** sortingOrder 95 + 아이템과 동일한 sorting layer 사용 (Codex)

**Ready button:** 이미 구현됨 (`MatchHudPresenter:229-231`, `GameSprites.BTN_PRESSED`). `btn_pressed.png` 존재 확인 → 검증 항목만.

**Validation:**
- [ ] 아이템 선택 후 arrow 표시 + bounce
- [ ] 다른 아이템 선택 시 arrow 이동
- [ ] AttackPhase 진입 시 arrow 숨김
- [ ] Ready button sprite 변경 동작 확인

**Risk:** Low

---

### 9-9. Feed/Eat Sprite — 검증 + 포지션 튜닝

**⚠ Codex Confirmation:** 이미 올바른 item sprite 사용 중 (`GameSprites.GetItemSprite(itemName)`). Samgyetang, IceCream, IcedAmericano 매핑 + 리소스 존재.

**Task 축소:**
1. Runtime에서 null sprite 반환되는 아이템 확인 (fallback: `ItemDataSO.Icon`, not `.Sprite` — Codex)
2. Feed sprite position (+0.5f Y) → Play Mode에서 캐릭터 입 위치 기준 조정
3. sortingOrder 90이 캐릭터 위에 렌더링되는지 확인
4. 한 연출당 `new GameObject` 1회 → 현재 단계에서 pooling 과도 (Codex)

**Validation:**
- [ ] 모든 feed 아이템에서 올바른 sprite 표시
- [ ] null sprite 시 `ItemDataSO.Icon` fallback 로그
- [ ] Position Play Mode 튜닝 완료

**Risk:** Very Low

---

## Execution Order

```
Phase A (Logic):
  9-1  PrepPhase immunity         [~30 min]  ← boolean flag, 최소 변경
  9-2  3s minimum envelope        [~2 hours] ← timeout 상향 포함
  9-3  Cat reroll verification    [~20 min]  ← 코드 변경 거의 없음

Phase B (VFX):
  CC-5 WouldEndMatch() 추가       [~15 min]  ← MatchManager 메서드 1개
  CC-6 PrepPhase death RPC ext    [~30 min]  ← PublishDeathSequence + RPC 시그니처
  9-5  Damage effect upgrade      [~1.5 hours] ← vignette + shake + particles
  9-6  Defeat animation fix       [~2 hours] ← EndsMatch + PlayDeathSequenceAndWait + death rework
  9-9  Feed/eat verification      [~20 min]  ← 검증 + 포지션 튜닝

Phase C (Cinematic):
  CC-8 _waitSix 추가              [~10 min]  ← cached WaitForSeconds + HandleRoundEnd
  9-4  Round end cinematic        [~2.5 hours] ← fade + text anim + server wait 6s

Phase D (HUD):
  9-7  Progress HUD               [~2.5 hours] ← NV FirstReadySeat lifecycle + UI build
  9-8  Selection arrow            [~45 min]  ← Core asmdef 호환 + 활성화 경로

Total estimated: ~12.5 hours
```

---

## Art Dependencies

| Asset | Used By | Fallback |
|-------|---------|----------|
| Arrow sprite | 9-8 | Runtime Texture2D triangle |
| Vignette texture | 9-5 | Runtime 128×128 Texture2D |
| Crown icon | 9-7 | Emoji text or runtime Texture2D |
| btn_pressed.png | 9-8 | 이미 존재 (검증만) |
| Ice shard particles | 9-5, 9-6 | 기존 _iceBreakParticle pool 재사용 |

---

## Cross-Cutting Changes (여러 task에 걸침)

### CC-1: CombatResultData.EndsMatch 필드 추가
- **Affects:** 9-6 (death animation), 잠재적으로 9-4 (cinematic)
- **Files:** `CombatResult.cs` (struct + serialize), `TurnManager.cs` (populate)
- **NGO 주의:** `NetworkSerialize()` 필드 순서 양쪽 동일 필수

### ~~CC-2: (CC-8로 통합됨)~~

### CC-3: InventoryPresenter REBUILD_LOCK_TIMEOUT 15s 상향
- **Affects:** 9-2 (최악 VFX timing ~10s)
- **Files:** `InventoryPresenter.cs` (const 변경)

### CC-4: NetworkVariable<byte> FirstReadySeat + Full Lifecycle
- **Affects:** 9-7 (Progress HUD crown)
- **Files:** `TurnManager.cs` (NV 선언 + write + reset), `GameDataBridge.cs` (subscribe + init + flush + unsubscribe), `MatchSnapshot.cs` (field)
- **Lifecycle:** Reset at PrepPhase start → Record after all ready → Bridge subscribe/init/flush/dispose

### CC-5: MatchManager.WouldEndMatch() (v2.1 — blocker B7)
- **Affects:** 9-6 (EndsMatch 계산), PrepPhase death path
- **Files:** `MatchManager.cs` (메서드 추가)
- **Logic:** `winnerIndex == 0 ? P1RoundWins.Value + 1 >= WINS_TO_MATCH : P2RoundWins.Value + 1 >= WINS_TO_MATCH`
- **호출 시점:** `EndRound()` 호출 전 — 현재 wins 기준 예측

### CC-6: PrepPhase Death Path RPC Extension (v2.1 — blocker B8)
- **Affects:** 9-6 (PrepPhase 사망에서도 endsMatch 전달)
- **Files:** `TurnManager.cs` (PublishDeathSequence + TriggerDeathSequenceRpc 시그니처 확장)
- **변경:** `PublishDeathSequence(int, bool)` → `TriggerDeathSequenceRpc(int, bool)` → `PlayDeathSequence(bool)`
- **HandleRoundEnd:** `WouldEndMatch(winnerIndex)` 호출하여 endsMatch 계산 후 전달

### CC-7: Barrier Timeout Runtime Floor (v2.1 — HIGH H1)
- **Affects:** 9-2 (최악 VFX timing 보호)
- **Files:** `TurnManager.cs` (barrier 호출부)
- **변경:** `_barrier.WaitForCompletion(Mathf.Max(presentationTimeoutSeconds, 15f))`
- **GameScene 직렬화 값 10s 유지** — 코드 런타임 floor로 안전 보장, 씬 수정 불필요

### CC-8: RoundOver Wait 6s (v2.1 — HIGH H2)
- **Affects:** 9-4 (시네마틱 + 네트워크 지연 여유)
- **Files:** `TurnManager.cs` (`_waitSix` 추가 + HandleRoundEnd 대기 변경)
- **변경:** `_waitThree` → `_waitSix` (4.3s 시네마틱 + 1.7s 네트워크 여유)

---

## Checklist

- [x] **9-1** PrepPhase 1-second immunity (boolean flag)
- [x] **9-2** 3-second minimum animation envelope + timeout 상향
- [x] **9-3** Cat reroll timing verification (코드 변경 최소) — 기존 lock/unlock 흐름 코드 검증 완료
- [x] **9-4** Round end cinematic + server RoundOver 6s
- [x] **CC-1** CombatResultData.EndsMatch field + serialize
- [x] **9-5** Damage effect upgrade (vignette + shake + ice particles)
- [x] **9-6** Defeat animation fix (Idle return + final particles via EndsMatch)
- [x] **CC-4** NetworkVariable FirstReadySeat + bridge subscribe
- [x] **9-7** Progress HUD (names + crown + attacker highlight)
- [x] **9-8** Item selection arrow (InventoryPresenter.Update bounce)
- [x] **9-9** Feed/eat sprite verification + position tuning
- [x] ~~**CC-2**~~ (CC-8로 통합)
- [x] **CC-3** REBUILD_LOCK_TIMEOUT 15s
- [x] **CC-5** MatchManager.WouldEndMatch() 추가
- [x] **CC-6** PrepPhase death path RPC extension (endsMatch 파라미터)
- [x] **CC-7** Barrier timeout runtime floor (Mathf.Max 15f)
- [x] **CC-8** _waitSix 추가 + HandleRoundEnd 대기 변경
- [ ] Runtime playtest: 2-player match 전체 검증
- [x] `Docs/RECENT_CHANGES.md` updated
- [x] `Docs/ACTIVE_CONTEXT.md` updated
- [x] `Docs/CHANGES.md` entry added
