# Known Issues — Absolute Zero

> Track bugs and issues discovered during development.
> Format: [Status] Issue description — impact — discovered date
> Last updated: 2026-09-15

---

## Active Issues

### KI-007: Multi 클라이언트 강제 종료 후 남은 연결 timeout
- **Status:** 수정 및 Windows 로컬 재검증 통과 — 실제 Lobby/Relay·다른 PC 검증은 남음 (2026-09-15)
- **Impact:** 준비/공격 단계에서 한 명을 강제 종료하면 남은 원격 클라이언트도 ProtocolTimeout/ClosedByRemote로 연결 종료. Host만 남으면 라운드 종료·재시작 반복 관찰.
- **Fix / evidence:** Transport 2.7.2의 실패/빈 UDP 수신 완료에서 버퍼 미반환을 수정. 버전 변경 없이 패키지를 프로젝트 내부에 고정. 독립 UDP 회귀 검사와 준비/공격/최종 연출 이탈 및 남은 연결 유지 검사를 통과. [수정 근거 및 유지보수](Validation/PLAN_029_transport_hotfix.md), [현재 검증 현황](Validation/PLAN_029_ai_matrix.md).

### KI-008: 일반 아이템 선택 RPC가 고스트 대상을 허용
- **Status:** 수정 및 실제 원격 RPC 재검증 통과 (2026-09-15)
- **Impact:** PlayerState.TryResolveTargetSeat에서 등록 여부만 검사하여 고스트를 공격 대상으로 선택 가능. 전투 계산에서는 거부되므로 피해 적용 증거는 없지만 선택이 잠기거나 행동이 낭비됨.
- **Fix / evidence:** 일반 행동의 행동자·대상이 접속 중인 생존자인지 서버에서 확인. 미니게임 결과 수락 시에도 재확인. 고스트 공격/자기 방어 요청 거절, 정상 선택, 미니게임 중 사망·중복 응답·재시도 검사를 통과. [현재 검증 현황](Validation/PLAN_029_ai_matrix.md).

### KI-001: EnemyPlayer 렌더러 미표시
- **Status:** ✅ Resolved — 2인 런타임 테스트 완료 (2026-08-30)
- **Category:** Visual / Network
- **Description:** 상대 플레이어 캐릭터 스프라이트가 화면에 표시되지 않는 현상. 근본 원인: 클라이언트에서 Player prefab의 `OnNetworkSpawn`이 GameScene 로드 완료 전에 실행 → `GameObject.Find("EnemyPlayer")` null 반환 후 즉시 return (재시도 없음).
- **Fix:** `AZPlayerVisual.TryBindEnemyVisual()` 추출 + `RetryBindEnemyVisual()` 코루틴 (3초 timeout, 매 프레임 재시도). EnemyPlayer 씬 오브젝트 정상 확인 (body/arm1/arm2/head/lowerbody/item/freezeice/particles, Animator with playerA.controller, SpriteRenderers enabled).
- **Impact:** 상대 캐릭터가 보이지 않음
- **Discovered:** 2026-07-20
- **Fixed:** 2026-08-27

### KI-002: SFX_wind 오디오 파일 미존재
- **Status:** Open — 아트/오디오 에셋 필요
- **Category:** Audio
- **Description:** CoolBreeze 환경 효과음용 wind 오디오 클립 미존재. GameAudioManager에서 참조하지만 실제 파일 없음.
- **Impact:** CoolBreeze 환경일 때 효과음 무음
- **Discovered:** 2026-07-22

### KI-003: 아이템 스프라이트 4종 미존재
- **Status:** Open — 아트 에셋 필요
- **Category:** Visual
- **Description:** 다음 아이템의 스프라이트가 존재하지 않아 GameSprites.GetItemSprite() null 반환:
  - Samgyetang (삼계탕)
  - Mask (마스크)
  - Screwdriver (십자드라이버)
  - ClawMachine (집게손)
- **Impact:** 해당 아이템 사용 시 아이콘 없이 빈 이미지 표시
- **Discovered:** 2026-07-22

### KI-004: CoolBreeze 바람 파티클 미존재
- **Status:** Open — 아트 에셋 필요
- **Category:** Visual
- **Description:** CoolBreeze 환경 효과용 바람 파티클 에셋 미존재.
- **Impact:** CoolBreeze 환경에서 시각 효과 없음
- **Discovered:** 2026-07-22

### KI-005: swing1 SpriteHash 커브 런타임 충돌 확인 필요
- **Status:** ✅ Resolved — 2인 런타임 테스트 완료 (2026-08-30)
- **Category:** Animation
- **Description:** Fan/HandFan 아이템의 swing1 애니메이션 SpriteHash 커브가 런타임에서 정상 작동하는지 미확인.
- **Impact:** 부채/손풍기 공격 애니메이션 오류 가능
- **Discovered:** 2026-07-22

### KI-006: 런타임 플레이테스트 미진행 (PLAN_018 Phase 1~7)
- **Status:** ✅ Resolved — 2인 매치 테스트 완료 (2026-08-30)
- **Category:** Integration
- **Description:** PLAN_018 아키텍처 마이그레이션 Phase 1~7 (Identity/Registry, PresentationBarrier, Gateway/Coordinator, Bootstrap, Class Extraction/ItemPipeline/ArrayPattern, ObjectPool, asmdef) 적용 후 전체 2인 매치 플로우 런타임 검증 미진행.
- **Impact:** 행동 보존(behavior-preserving) 보장 미확인
- **Discovered:** 2026-08-20

---

## Resolved Issues

(none)
