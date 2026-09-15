# PLAN_021 — Sector Implementation Roadmap

> **Status:** ✅ 55/64 Complete — Bot/Solo 보류, 질문 잔여 2건
> **Created:** 2026-08-27 | **Updated:** 2026-09-11
> **Dependencies:** `Docs/DESIGN_QUESTIONS.md` (v3, 대부분 해결)
> **Purpose:** 각 섹터별 구현 플로우 + 세부 TODO. 기획 답변 도착 순서대로 착수.

---

## 섹터 의존성 맵

```
                    ┌─────────────┐
                    │  8. Lobby   │ ← 독립 (먼저 착수 가능)
                    └──────┬──────┘
                           │ mode select → scene load
        ┌──────────────────┼──────────────────┐
        ▼                  ▼                  ▼
  ┌──────────┐     ┌──────────────┐    ┌──────────┐
  │ 9. 1v1   │     │ 1. Multi     │    │ 6. Solo  │
  │ 개선     │     │ 화면/레이아웃 │    │ Bot AI   │
  └──────────┘     └──────┬───────┘    └──────────┘
                          │ scene ready
              ┌───────────┼───────────┐
              ▼           ▼           ▼
        ┌──────────┐ ┌──────────┐ ┌──────────┐
        │ 2. Multi │ │ 3. Multi │ │ 4. Multi │
        │ 아이템   │ │ 전투해결 │ │ 연출     │
        └──────────┘ └─────┬────┘ └──────────┘
                           │ death → ghost
                     ┌─────▼─────┐
                     │ 5. Ghost  │
                     │ System    │
                     └───────────┘

  ┌──────────────┐              ┌──────────────┐
  │ 7. Custom    │ ← 독립      │ 10. 기존Q    │ ← 독립
  └──────────────┘              └──────────────┘
```

**착수 우선순위:** 9(1v1 개선, 기획 확정) → 8(Lobby) & 7(Customization) → 1(Multi 화면) → 2+3+4(병렬) → 5(Ghost) → 6(Solo/Bot)

---

## 1. Multi 화면/레이아웃

> **기획 의존:** M1~M4 | **선행:** 없음 | **후행:** 섹터 2, 3, 4, 5 전부

| # | Task | 상세 | 상태 |
|---|------|------|------|
| 1-1 | 기획 확인: M1~M4 답변 수령 | 카메라 시점(1인칭/3인칭/탑다운), 캐릭터 배치(4방향/일렬/원형), HUD 위치, 아이템 배치 | ✅ Sheet 반영 |
| 1-2 | GameScene_Multi 씬 생성 | 정자 배경 복사 + 3~4인 좌석 위치 오브젝트 배치 + EnemyPlayer 오브젝트 2~3개 추가 | ✅ 2×2 grid SP1~SP4 |
| 1-3 | Multi 전용 카메라 구현 | 3인칭 탑다운: pos(0,5,-5) rot(22,0,0) FOV=65 | ✅ |
| 1-4 | N인 온도 바 + 닉네임 HUD | Screen-space 상단 나열, MatchHudPresenter N-player nameBoxes + crown | ✅ PLAN_026 FIX-4 |
| 1-5 | Multi 아이템 배치 UI | 하단 반원형 유지 + InventoryPresenter seat-keyed | ✅ |
| 1-6 | PlayerSpawnManager 확장 | Multi 씬 3~4명 spawn + PlayerRegistry + seat index 매핑 | ✅ |

---

## 2. Multi 아이템 재정의

> **기획 의존:** I1~I10 | **선행:** 섹터 1 (씬/레이아웃) | **후행:** 섹터 3 (전투 해결)

| # | Task | 상세 | 상태 |
|---|------|------|------|
| 2-1 | 기획 확인: I1~I10 답변 수령 | 각 아이템 다인전 타겟 규칙 — 단일 타겟/전체/자기 | ✅ Sheet 반영 |
| 2-2 | ItemDataSO에 TargetMode 추가 | `enum TargetMode { Self, SingleTarget }` + 모든 SO 서브클래스 `GetTargetMode()` | ✅ |
| 2-3 | 클릭-타겟 선택 시스템 | `GameUIManager.HandleTargetSelection()` click-to-target (SingleTarget 아이템) | ✅ |
| 2-4 | SelectItemRpc 확장 | `(slot, targetSeat)` 페이로드 + 서버 검증 | ✅ |
| 2-5 | Multi 밸런스 패치 SO 분리 | `GameModeRuleSO` — isWindbreakerUnlimited/isTarotAllowed/maxRandomItems 분리 | ✅ |
| 2-6 | Multi 전용 드롭 테이블 | 타로카드 제거 + 초기 2아이템 지급 + GameMode 분기 | ✅ |
| 2-7 | Deathmatch Grant 로직 | `TryGrantDeathmatchItems()` — 5경로 호출, 생존자 2명 트리거, rule.DeathmatchGrantCount | ✅ |

---

## 3. Multi 전투 해결

> **기획 의존:** C1~C5 | **선행:** 섹터 2 (아이템 타겟) | **후행:** 섹터 5 (Ghost)

| # | Task | 상세 | 상태 |
|---|------|------|------|
| 3-1 | 기획 확인: C1~C5 답변 수령 | 행동 순서(순차), 중간사망 취소, 바람막이 범위, 동시 Ready 규칙 | ✅ Sheet 반영 |
| 3-2 | CombatResolver N인 확장 | `ResolveMulti()` + `MultiCombatResolution` + `CombatResolutionBatchNetData` | ✅ |
| 3-3 | 중간 사망 → Ghost 전환 | `AuthoritativeDeathService.TryKill()` → Ghost + `CancelTurnParticipation` + `ClearInventory` | ✅ |
| 3-4 | 킬 스코어 시스템 | `MatchNetworkState.KillScores` + `CheckMultiMatchEnd()` 5킬 + `MultiMatchOutcome.JointVictory` | ✅ |
| 3-5 | 라운드 리셋 로직 | `RoundLifecycleService.ResetPlayersForNewRound(players[])` — 37° 부활, 아이템/버프/threshold 리셋 | ✅ |
| 3-6 | 라운드 종료 조건 | `EvaluateRoundEnd()` ≤1생존 감지 + deathMask batch presentation | ✅ |

---

## 4. Multi 연출/애니메이션

> **기획 의존:** S1~S3 | **선행:** 섹터 3 (전투 해결) | **후행:** 섹터 5 (Ghost 연출)

| # | Task | 상세 | 상태 |
|---|------|------|------|
| 4-1 | 기획 확인: S1~S3 답변 수령 | 연출 방식(순차), 사망 연출 즉시, 환경변수 다인 규칙 | ✅ Sheet 반영 |
| 4-2 | N인 Attack Phase 시퀀서 | `PlayMultiCombatVFXSequence()` 순차 + `PresentationBarrier` ACK 동기화 | ✅ |
| 4-3 | CombatVFXManager N인 확장 | N-player hit effects + `PlayMultiDeathSequence(deathMask)` batch death | ✅ |
| 4-4 | Ghost 전환 연출 | 즉시: alpha 0.4 반투명 전환 (`AZPlayerVisual`) + deathCoroutine guard | ✅ |
| 4-5 | Multi용 환경 변수 효과 조정 | `EnvironmentRuleService` Multi 분기: 잼민이 전원 스틸, 앰뷸런스 최저 1명, 폭염경보 최저 선공 | ✅ |

---

## 5. Ghost System

> **기획 의존:** G1~G6 | **선행:** 섹터 3 (사망→Ghost 전환) | **후행:** 없음

| # | Task | 상세 | 상태 |
|---|------|------|------|
| 5-1 | 기획 확인: G1~G6 답변 수령 | 행동 타이밍, 시야/UI, 복수 고스트, 고스트→고스트 불가, 라스트 킬, 시각 표현 | ✅ Sheet 반영 |
| 5-2 | PlayerState 상태머신 확장 | `LifeState { Alive, Ghost, Dead }` + Ghost 진입 시 아이템 제거/Ready 숨김 | ✅ |
| 5-3 | GhostSkillService 데이터 | FrostStrike(−15°, CD 3), ChillAura(fan×2, recovery×0.5, CD 2, 1턴) | ✅ |
| 5-4 | Ghost 스킬 실행 파이프라인 | `UseGhostSkillRpc` 6단계 서버 검증 + 쿨다운 + `GhostCooldownNetData` NetworkList | ✅ |
| 5-5 | Ghost 전용 UI | `GhostSkillPresenter` — 스킬 버튼 2개 + 클릭-타겟 선택 + 쿨다운 오버레이 | ✅ |
| 5-6 | Ghost 비주얼 | alpha 0.4 반투명 + `PlayerSeatMarker` + BoxCollider(trigger) | ✅ |
| 5-7 | Ghost 킬 스코어 연동 | `TryKill(GhostFrost)` 킬 귀속 + `CheckMultiMatchEnd` 5킬 승리 포함 | ✅ |

---

## 6. Solo / Bot AI

> **기획 의존:** B1~B5 | **선행:** 섹터 8 (Lobby 진입) | **후행:** 없음

| # | Task | 상세 | 상태 |
|---|------|------|------|
| 6-1 | 기획 확인: B1~B5 답변 수령 | 난이도 선택 UI, 봇 외형, 미니게임 처리, 이름, 실행 위치 | ⏳ 대기 |
| 6-2 | Solo 진입 플로우 | Lobby에서 Solo 버튼 → 난이도 선택(확정 시) → Relay 없이 로컬 Host 시작 → GameScene 로드. `SessionManager` 분기 추가 | - [ ] |
| 6-3 | BotPlayer 기본 구조 | Host에서 가상 PlayerState 생성 + BT 루트 노드 + 턴 페이즈 이벤트 수신 hookup. `IBotBrain` 인터페이스 설계 | - [ ] |
| 6-4 | BT 아이템 선택 로직 | 난이도별 평가 함수 — Easy: 랜덤 선택, Normal: 상황 가중치(온도 차이/아이템 효과), Hard: 최적 계산(상대 온도/방어 예측) | - [ ] |
| 6-5 | BT Ready 타이밍 | 난이도별 PrepPhase 내 Ready 누르는 시점 — Easy: 랜덤 지연(5~15초), Normal: 적절한 타이밍(8~12초), Hard: 최적 타이밍(회복 최대화) | - [ ] |
| 6-6 | Bot 미니게임 처리 | 확정안에 따라: A) 자동 성공/실패 확률(Easy 40%/Normal 70%/Hard 95%) 서버 직접 주입, B) 미니게임 스킵(항상 성공) | - [ ] |
| 6-7 | Bot 외형/이름 설정 | 확정안에 따라 기본 캐릭터 or 전용 외형 + 이름(고정 "Bot" / 랜덤 풀). AZPlayerVisual에 봇 표시 분기 | - [ ] |

---

## 7. Customization

> **기획 의존:** CU1~CU6 | **선행:** 없음 (독립) | **후행:** 섹터 8 (Lobby Closet 버튼)

| # | Task | 상세 | 상태 |
|---|------|------|------|
| 7-1 | 기획 확인: CU1~CU6 답변 수령 | 초기 수량, Closet UI, 색상, 저장 방식, 멀티 동기화, 해금 조건 | ✅ PLAN_023 |
| 7-2 | CosmeticItemSO 설계 | ScriptableObject: `part`(Head/Top/Back/Bottom/Tail), `type`(Overlay/Swap), `sprite`, `sortOrderOffset`. 파트당 N개 에셋 생성 | ✅ PLAN_023 B-1a |
| 7-3 | CosmeticEquipState 데이터 모델 | 5파트 장착 슬롯 관리 클래스. equip/unequip 로직 + 같은 파트 자동 교체 + 이벤트 발행(OnEquipChanged) | ✅ PLAN_023 B-3 |
| 7-4 | Closet UI Canvas | 파트별 탭 + 리스트 + 장착/해제 버튼 + 닫기. LobbyScene에 오버레이로 구현 | ✅ PLAN_023 B-5 |
| 7-5 | 캐릭터 비주얼 적용 | AZPlayerVisual에 코스메틱 반영: Overlay(child SR 추가), Swap(스프라이트 교체) | ✅ PLAN_023 B-4+B-7 |
| 7-6 | 로컬 저장 | PlayerPrefs JSON으로 장착 상태 영속화 + 게임 시작 시 로드 복원 | ✅ PLAN_023 B-3b |
| 7-7 | 멀티 동기화 | Owner→SubmitCosmeticRpc→Server TryValidate→CosmeticDataNV→상대 적용 | ✅ PLAN_023 B-6 |

---

## 8. Lobby / 매칭

> **기획 의존:** L1~L6 | **선행:** 없음 (독립) | **후행:** 섹터 1 (Multi 씬 진입), 섹터 6 (Solo 진입)

| # | Task | 상세 | 상태 |
|---|------|------|------|
| 8-1 | 기획 확인: L1~L6 답변 수령 | 진입 방식, 모드 선택, 인원 설정, Settings 항목, 닉네임 규칙, 비주얼 | ✅ PLAN_023 |
| 8-2 | 로비 UI 리디자인 | AZLobbyUI MVP 분해 → LobbyPresenter + 5 Views + UIHelper. Arena/Closet/Solo/Settings 버튼 + 닉네임 + 배경 이미지 | ✅ PLAN_023 A |
| 8-3 | Multi Play 모드 선택 | ModeSelectView: 1v1 / 다인전(disabled). 방 만들기/참가 → RoomView | ✅ PLAN_023 A |
| 8-4 | 다인 방 설정 | 다인전 "준비 중" disabled (PLAN_019 대기). 1v1 대기실 + 슬롯 표시 + 시작 버튼 | ✅ PLAN_023 A (1v1 only) |
| 8-5 | 닉네임 시스템 | TMP_InputField 한/영 2~8자 + PlayerPrefs + CosmeticProfileService.TryValidateNickname + 로비 동기화 | ✅ PLAN_023 A |
| 8-6 | Settings 캔버스 | BGM/SFX 슬라이더 + base×master 볼륨 + PlayerPrefs 저장 + GameAudioManager 연동 | ✅ PLAN_023 A |

---

## 9. 1v1 개선 (기획 확정 완료 — 즉시 착수 가능)

> **기획 의존:** 없음 (전부 확정) | **선행:** 없음 | **후행:** 없음

| # | Task | 상세 | 상태 |
|---|------|------|------|
| 9-1 | PrepPhase 1초 면역 | `skipFirstFanTick = true` — 첫 fan tick 스킵으로 1초 면역 구현 | ✅ PLAN_022 |
| 9-2 | 애니메이션 타이밍 통일 | per-item `AnimDuration` SO 필드 기반 개별 지정 시스템 | ✅ PLAN_022 |
| 9-3 | 고양이 리롤 타이밍 | 고양이 걸어가는 애니메이션 타이밍에 맞춰 리롤 | ✅ PLAN_022 |
| 9-4 | 라운드 종료 시네마틱 | `CinematicOverlay` + `RoundResultPresenter` fade/text-rise 코루틴 | ✅ PLAN_022 |
| 9-5 | 데미지 이펙트 강화 | `ScreenVFXManager` Vignette + `CameraShake` + `PlayIceBreakAt()` | ✅ PLAN_022 |
| 9-6 | 패배 애니메이션 수정 | freeze → particle burst → Idle 복귀 시퀀스 | ✅ PLAN_022 |
| 9-7 | Progress HUD | `MatchHudPresenter` nameBoxes + crown + attacker box highlight | ✅ PLAN_022 |
| 9-8 | 아이템 선택 화살표 | `InventoryPresenter._selectionArrow` procedural triangle + bounce | ✅ PLAN_022 |
| 9-9 | feed/eat 스프라이트 수정 | `SetItemSprite(itemData.ItemName)` 실제 SO 스프라이트 사용 | ✅ PLAN_022 |

---

## 10. 기존 미해결 질문 해소

> **기획 의존:** Q1~Q23 잔여 10건 | **선행:** 없음 (독립) | **후행:** 해당 섹터 코드 수정

| # | Task | 상세 | 상태 |
|---|------|------|------|
| 10-1 | 기획 확인: 아이템/미니게임 질문 | Q1(핫팩 수치), Q6(음식 태그), Q8(Main/Sub 구분), Q9(타로카드 타이밍), Q10(미니게임 상대 화면) | ✅ Sheet 답변 수령 |
| 10-2 | 기획 확인: 시스템/UI 질문 | Q16(슬롯 레이아웃), Q19(티셔츠 역효과), Q21(중첩 허용), Q22(선택 공개), Q23(지연 방어) | ✅ Sheet 답변 수령 |
| 10-3 | GAME_DESIGN.md 반영 | 답변 기반 Resolved 이동 + 타겟 선택/Ghost 시스템/Multi 레이아웃 반영 | ✅ 2026-09-11 |
| 10-4 | 코드 수정 | SO 수치 반영 완료 (2026-08-30). 잔여: Q10/Q16/Q21/Q22/Q23 Pending (기획 미확정) | ⏳ 5건 Pending |

---

## 통계

| 섹터 | 항목 수 | 완료 | 상태 |
|------|---------|------|------|
| 1. Multi 화면 | 6 | 6 | ✅ 100% |
| 2. Multi 아이템 | 7 | 7 | ✅ 100% |
| 3. Multi 전투 | 6 | 6 | ✅ 100% |
| 4. Multi 연출 | 5 | 5 | ✅ 100% |
| 5. Ghost | 7 | 7 | ✅ 100% |
| 6. Solo/Bot | 7 | 0 | ⏸ 보류 (유저 지시) |
| 7. Customization | 7 | 7 | ✅ 100% (PLAN_023) |
| 8. Lobby | 6 | 6 | ✅ 100% (PLAN_023) |
| 9. 1v1 개선 | 9 | 9 | ✅ 100% (PLAN_022) |
| 10. 기존 질문 | 4 | 3 | ⏳ 75% (Q Pending 5건) |
| **합계** | **64** | **56** | **87.5%** |
