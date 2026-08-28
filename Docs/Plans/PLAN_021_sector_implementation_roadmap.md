# PLAN_021 — Sector Implementation Roadmap

> **Status:** 📋 Planning — 기획 답변 대기
> **Created:** 2026-08-27
> **Dependencies:** `Docs/DESIGN_QUESTIONS.md` (v3, 55건 미해결)
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
| 1-1 | 기획 확인: M1~M4 답변 수령 | 카메라 시점(1인칭/3인칭/탑다운), 캐릭터 배치(4방향/일렬/원형), HUD 위치, 아이템 배치 | ⏳ 대기 |
| 1-2 | GameScene_Multi 씬 생성 | 정자 배경 복사 + 3~4인 좌석 위치 오브젝트 배치 + EnemyPlayer 오브젝트 2~3개 추가 (기존 GameScene의 EnemyPlayer 구조 복제) | - [ ] |
| 1-3 | Multi 전용 카메라 구현 | 확정안에 따라 CameraController 분기 또는 신규 생성. 1인칭이면 기존 유지+상대 N명 표시, 3인칭/탑다운이면 새 카메라 시스템 | - [ ] |
| 1-4 | N인 온도 바 + 닉네임 HUD | 확정안에 따라 Screen-space(상단 나열) 또는 World-space(머리 위) 온도 바 + 닉네임 라벨 구현. TemperaturePresenter N인 확장 | - [ ] |
| 1-5 | Multi 아이템 배치 UI | 내 아이템(하단 반원형 유지 여부) + 상대 N명 아이템 표시 영역 + 스크롤/축소 처리 | - [ ] |
| 1-6 | PlayerSpawnManager 확장 | Multi 씬에서 3~4명 spawn 위치 할당 + PlayerRegistry 연동 + seat index 매핑 | - [ ] |

---

## 2. Multi 아이템 재정의

> **기획 의존:** I1~I10 | **선행:** 섹터 1 (씬/레이아웃) | **후행:** 섹터 3 (전투 해결)

| # | Task | 상세 | 상태 |
|---|------|------|------|
| 2-1 | 기획 확인: I1~I10 답변 수령 | 각 아이템 다인전 타겟 규칙 — 단일 타겟/전체/자기 | ⏳ 대기 |
| 2-2 | ItemDataSO에 TargetMode 추가 | `enum TargetMode { Self, SingleTarget, AllEnemies, AllPlayers }` 필드 추가 + 아이템 21종 값 설정 | - [ ] |
| 2-3 | 드래그-타겟 선택 시스템 | 아이템 터치 → 드래그 → 캐릭터 위 릴리즈로 타겟 지정. 재클릭 시 취소. (Slay the Spire 참조 UX). SingleTarget 아이템에만 활성화 | - [ ] |
| 2-4 | SelectItemRpc 확장 | 기존 `(slot)` → `(slot, targetClientId)` 페이로드 추가. 서버 검증: TargetMode 일치, 유효 타겟, Ghost 타겟 불가 등 | - [ ] |
| 2-5 | Multi 밸런스 패치 SO 분리 | 바람막이 `MaxUses` 1→Consumable, threshold 지급량 각 1개, 랜덤 최대 4개 cap. GameMode별 SO config 또는 runtime override | - [ ] |
| 2-6 | Multi 전용 드롭 테이블 | 타로카드 제거 + 확률 재분배 + 초기 2아이템 지급 로직. `DropTableSO` 분리 또는 GameMode 분기 | - [ ] |
| 2-7 | Deathmatch Grant 로직 | 생존자 2명 남으면 양쪽에 랜덤 4아이템 즉시 지급 트리거. 서버에서 생존자 수 체크 → 조건 충족 시 아이템 할당 + ClientRpc 알림 | - [ ] |

---

## 3. Multi 전투 해결

> **기획 의존:** C1~C5 | **선행:** 섹터 2 (아이템 타겟) | **후행:** 섹터 5 (Ghost)

| # | Task | 상세 | 상태 |
|---|------|------|------|
| 3-1 | 기획 확인: C1~C5 답변 수령 | 행동 순서(순차/동시), 중간사망 처리, 복수공격, 바람막이 범위, 동시 Ready 규칙 | ⏳ 대기 |
| 3-2 | CombatResolver N인 확장 | Ready 타임스탬프 큐(N명) + 타겟별 데미지 배치 + 방어 우선 해결 순서. `CombatSnapshot` → `MultiCombatSnapshot` 확장 | - [ ] |
| 3-3 | 중간 사망 → Ghost 전환 | Attack Phase 중 0° 도달 시 즉시 Ghost 상태 전환. 해당 플레이어 잔여 행동 처리(실행/취소 — 확정안에 따름) | - [ ] |
| 3-4 | 킬 스코어 시스템 | MatchScoreView 확장: 라운드 누적 킬 카운트 + 5킬 승리 판정 + 동시 5킬 공동승리. NetworkVariable `kills[]` 추가 | - [ ] |
| 3-5 | 라운드 리셋 로직 | 라운드 종료 시: 전원 37° 부활, 아이템 전부 삭제, 버프/디버프 클리어, threshold 지급 이력 초기화. 킬 스코어만 유지 | - [ ] |
| 3-6 | 라운드 종료 조건 | ≤1명 생존 or 전원 사망 감지 → 라운드 종료 트리거 + 준비 텍스트 표시 → 다음 라운드 시작. `RoundLifecycleService` 확장 | - [ ] |

---

## 4. Multi 연출/애니메이션

> **기획 의존:** S1~S3 | **선행:** 섹터 3 (전투 해결) | **후행:** 섹터 5 (Ghost 연출)

| # | Task | 상세 | 상태 |
|---|------|------|------|
| 4-1 | 기획 확인: S1~S3 답변 수령 | 연출 방식(순차 3초×N / 동시 3초 / 하이브리드 1.5초×N), 사망 연출 타이밍, 환경변수 다인 규칙 | ⏳ 대기 |
| 4-2 | N인 Attack Phase 시퀀서 | 확정안에 따라 순차/동시/하이브리드 연출 파이프라인. `PresentationBarrier` N인 확장(모든 클라이언트 ACK 대기) | - [ ] |
| 4-3 | CombatVFXManager N인 확장 | 복수 타겟 히트 이펙트 + 동시 다발 파티클 풀 확장. 동시 연출 시 이펙트 겹침 방지 오프셋 | - [ ] |
| 4-4 | Ghost 전환 연출 | 프리즈 애니메이션 → 파티클 버스트 → 캐릭터 사라짐 → 반투명 고스트 등장 시퀀스. 중간 사망 즉시 or 일괄(확정안) | - [ ] |
| 4-5 | Multi용 환경 변수 효과 조정 | 잼민이: 전원 1개씩 스틸. 앰뷸런스: 최저 온도 1명만 +10°. 폭염경보: 최저 온도 선공. `EnvironmentRuleService` 분기 추가 | - [ ] |

---

## 5. Ghost System

> **기획 의존:** G1~G6 | **선행:** 섹터 3 (사망→Ghost 전환) | **후행:** 없음

| # | Task | 상세 | 상태 |
|---|------|------|------|
| 5-1 | 기획 확인: G1~G6 답변 수령 | 행동 타이밍, 시야/UI, 복수 고스트, 고스트→고스트, 라스트 킬, 시각 표현 | ⏳ 대기 |
| 5-2 | PlayerState 상태머신 확장 | `enum LifeState { Alive, Ghost, Dead }` 추가. Ghost 진입 시 아이템 전부 제거 + PrepPhase 참여 비활성화 + Ready 버튼 숨김 | - [ ] |
| 5-3 | GhostSkillSO 데이터 | ScriptableObject 생성: FrostStrike(즉시 데미지 −3~5°, 쿨다운 1턴), ChillAura(감소×2 + 회복×0.5, 쿨다운 1턴, 지속 1턴). 값은 플레이테스트 조정 | - [ ] |
| 5-4 | Ghost 스킬 실행 파이프라인 | 고스트 스킬 선택 → 타겟 클릭 → `GhostSkillRpc(SendTo.Server)` → 서버 검증(쿨다운/유효타겟/Ghost상태) → 효과 적용 → `GhostSkillResultRpc` 연출 | - [ ] |
| 5-5 | Ghost 전용 UI | PrepPhase 아이템 UI 대신 스킬 버튼 2개(FrostStrike/ChillAura) + 타겟 선택 + 쿨다운 표시 + Ready 버튼 비활성화 | - [ ] |
| 5-6 | Ghost 비주얼 | 확정안에 따라 반투명(alpha 0.4) 캐릭터 or 전용 고스트 스프라이트 + 위치/이동 처리. SortingOrder 조정으로 생존자 뒤에 표시 | - [ ] |
| 5-7 | Ghost 킬 스코어 연동 | ChillAura/FrostStrike로 타겟 0° 시 고스트 플레이어에게 킬 귀속. 5킬 승리 판정에 포함. 킬 피드 UI 표시 | - [ ] |

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
| 7-1 | 기획 확인: CU1~CU6 답변 수령 | 초기 수량, Closet UI, 색상, 저장 방식, 멀티 동기화, 해금 조건 | ⏳ 대기 |
| 7-2 | CosmeticItemSO 설계 | ScriptableObject: `part`(Head/Top/Back/Bottom/Tail), `type`(Overlay/Swap/Tint), `sprite`, `sortOrderOffset`, `unlockCondition`. 파트당 N개 에셋 생성 | - [ ] |
| 7-3 | CosmeticEquipState 데이터 모델 | 5파트 장착 슬롯 관리 클래스. equip/unequip 로직 + 같은 파트 자동 교체 + 이벤트 발행(OnEquipChanged) | - [ ] |
| 7-4 | Closet UI Canvas | 캐릭터 프리뷰(전신 스프라이트 조립) + 파트별 탭/그리드 + 장착/해제 버튼 + 닫기. LobbyScene에 오버레이 Canvas로 구현 | - [ ] |
| 7-5 | 캐릭터 비주얼 적용 | AZPlayerVisual에 코스메틱 반영 시스템: Overlay(child SpriteRenderer 추가/제거), Swap(기존 스프라이트 교체), Tint(Material 색상 변경) | - [ ] |
| 7-6 | 로컬 저장 | PlayerPrefs or JSON 파일로 장착 상태 영속화 + 게임 시작 시 로드 복원. 키: `cosmetic_head`, `cosmetic_top` 등 | - [ ] |
| 7-7 | 멀티 동기화 | 게임 시작 시 장착 데이터를 `CosmeticSyncRpc(SendTo.Server)` → 서버가 전체 클라에 `CosmeticBroadcastRpc` → 상대 캐릭터에 적용 | - [ ] |

---

## 8. Lobby / 매칭

> **기획 의존:** L1~L6 | **선행:** 없음 (독립) | **후행:** 섹터 1 (Multi 씬 진입), 섹터 6 (Solo 진입)

| # | Task | 상세 | 상태 |
|---|------|------|------|
| 8-1 | 기획 확인: L1~L6 답변 수령 | 진입 방식, 모드 선택, 인원 설정, Settings 항목, 닉네임 규칙, 비주얼 | ⏳ 대기 |
| 8-2 | 로비 UI 리디자인 | 기존 AZLobbyUI 리팩터: 메인 화면에 Solo/Multi/Closet/Settings 4버튼 + 닉네임 입력 필드 배치. 캐주얼 스타일 비주얼 | - [ ] |
| 8-3 | Multi Play 모드 선택 | Multi 버튼 → 서브 화면: 1v1 / 3~4인 선택 → 방 만들기(코드 생성) / 참가(코드 입력). 기존 LobbyManager 연동 유지 | - [ ] |
| 8-4 | 다인 방 설정 | 호스트가 인원 수(3 or 4) 선택 + 대기실 UI(참가자 목록, 슬롯 표시) + 인원 충족 시 시작 버튼 활성화. LobbyManager 확장 | - [ ] |
| 8-5 | 닉네임 시스템 | 입력 필드 + 길이/문자 제한(확정안) + 로컬 저장(PlayerPrefs) + 로비/게임 내 표시. HUD 닉네임 연동 | - [ ] |
| 8-6 | Settings 캔버스 | 사운드(BGM/SFX 볼륨 슬라이더) + 확정된 추가 항목. PlayerPrefs 저장 + GameAudioManager 볼륨 연동 | - [ ] |

---

## 9. 1v1 개선 (기획 확정 완료 — 즉시 착수 가능)

> **기획 의존:** 없음 (전부 확정) | **선행:** 없음 | **후행:** 없음

| # | Task | 상세 | 상태 |
|---|------|------|------|
| 9-1 | PrepPhase 1초 면역 | TurnManager PrepPhase 진입 후 1초간 fan 자연감소 비활성화. `EnvironmentRuleService`에 면역 타이머 추가. 아이템/회복은 정상 적용 | - [ ] |
| 9-2 | 3초 통합 애니메이션 타이밍 | CombatVFXManager 모든 아이템 연출을 3초(애니메이션+대기) 내 완료하도록 통일. 기존 아이템별 가변 시간 → 고정 3초 cap | - [ ] |
| 9-3 | 고양이 리롤 타이밍 | 즉시 리롤 → 고양이 걸어가는 애니메이션 타이밍(~1초)에 맞춰 리롤 실행으로 변경. SFX_cat 재생 시점 유지 | - [ ] |
| 9-4 | 라운드 종료 시네마틱 | 1초 fade-out → 승자 이름 + 점수 rise 애니메이션 → 1초 fade-in. 최종 승자: 캐릭터 센터 표시. `RoundResultPresenter` 확장 | - [ ] |
| 9-5 | 데미지 이펙트 강화 | 피격 시: 비네트 엣지 이펙트 + 카메라 셰이크(DOTween or 수동) + 얼음 파티클 버스트 추가. CombatVFXManager `PlayHitEffect()` 확장 | - [ ] |
| 9-6 | 패배 애니메이션 수정 | 프리즈 → 파티클 버스트 → Idle 복귀(ReviveVisual 연동). 최종 라운드: 추가 얼음 파편 파티클. 기존 freeze→shatter 시퀀스 보강 | - [ ] |
| 9-7 | Progress HUD | 상단 중앙: 닉네임 2개 + 선공자 왕관 아이콘 + 현재 공격자 박스 색상 하이라이트. `MatchHudPresenter` 확장 | - [ ] |
| 9-8 | 아이템 선택 화살표 | 선택된 아이템 위에 화살표 오브젝트 표시(bounce 애니메이션). Ready 버튼 pressed 상태 "lit" 스프라이트 교체 (아트 에셋 필요) | - [ ] |
| 9-9 | feed/eat 스프라이트 수정 | 먹이기/먹기 애니메이션에서 범용 스프라이트 → 실제 아이템 SO의 sprite 사용으로 교체. CombatVFXManager 아이템 스프라이트 주입 | - [ ] |

---

## 10. 기존 미해결 질문 해소

> **기획 의존:** Q1~Q23 잔여 10건 | **선행:** 없음 (독립) | **후행:** 해당 섹터 코드 수정

| # | Task | 상세 | 상태 |
|---|------|------|------|
| 10-1 | 기획 확인: 아이템/미니게임 질문 | Q1(핫팩 수치), Q6(음식 태그), Q8(Main/Sub 구분), Q9(타로카드 타이밍), Q10(미니게임 상대 화면) | ⏳ 대기 |
| 10-2 | 기획 확인: 시스템/UI 질문 | Q16(슬롯 레이아웃), Q19(티셔츠 역효과), Q21(중첩 허용), Q22(선택 공개), Q23(지연 방어) | ⏳ 대기 |
| 10-3 | GAME_DESIGN.md 반영 | 답변 기반 Open Questions → Resolved 이동 + 관련 섹션(아이템 테이블, 미니게임, 전투 규칙) 업데이트 | - [ ] |
| 10-4 | 코드 수정 | 핫팩 SO 수치 확정, 아이템 음식 태그 적용, Main/Sub 할당, 슬롯 UI 레이아웃 등 확정값 코드 반영 | - [ ] |

---

## 통계

| 섹터 | 항목 수 | 기획 의존 | 독립 착수 가능 |
|------|---------|----------|--------------|
| 1. Multi 화면 | 6 | M1~M4 | ❌ |
| 2. Multi 아이템 | 7 | I1~I10 | ❌ |
| 3. Multi 전투 | 6 | C1~C5 | ❌ |
| 4. Multi 연출 | 5 | S1~S3 | ❌ |
| 5. Ghost | 7 | G1~G6 | ❌ |
| 6. Solo/Bot | 7 | B1~B5 | ❌ |
| 7. Customization | 7 | CU1~CU6 | ❌ |
| 8. Lobby | 6 | L1~L6 | ❌ |
| **9. 1v1 개선** | **9** | **없음** | **✅ 즉시 가능** |
| 10. 기존 질문 | 4 | Q1~Q23 | ❌ |
| **합계** | **64** | | |
