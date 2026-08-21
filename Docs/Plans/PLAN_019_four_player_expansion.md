# PLAN 019 — 4인 플레이 확장 계획

> 상태: Future / Blocked by design decisions — PLAN_018 완료 전 구현하지 않음  
> 작성 기준: 2026-08-19 코드베이스와 `AI_TARGET_ARCHITECTURE.md` v2  
> 목적: 1대1 동작을 훼손하지 않고 설정 가능한 2~4인 match로 확장한다.

## 1. 이 계획의 위치

PLAN_018은 현재 1대1 gameplay를 보존하면서 identity, session, lifetime, turn, presentation 구조를 정리한다. PLAN_019는 그 구조 위에 실제 4인 gameplay를 추가하는 후속 feature plan이다.

PLAN_018에서 허용되는 일:

- player collection 기반 Registry와 iteration
- 명시적 PlayerIndex/ClientId 변환
- N-player를 막지 않는 Domain method signature
- pair assumption을 compatibility mapper 뒤로 격리

PLAN_019에서만 허용되는 일:

- Lobby/Relay/Spawn의 RequiredPlayerCount를 3~4로 활성화
- PlayerSeat와 stable participant mapping 도입
- 타겟 선택 UI와 서버 검증
- 다중 player combat, elimination, 승리 조건 변경
- 4인 HUD/VFX/camera/layout
- Host + remote client 3개 runtime gate

## 2. 구현 전 필수 게임 디자인 결정

다음 결정이 `Docs/GAME_DESIGN.md`에 확정되지 않으면 구현을 시작하지 않는다.

1. 지원 인원은 2와 4만인지, 2~4 모두인지.
2. 로비가 가득 차야 시작하는지, Host가 최소 인원 이상에서 시작할 수 있는지.
3. free-for-all인지 team mode도 고려하는지.
4. target 종류: self, single opponent, several players, all players.
5. 여러 player가 같은 target을 공격할 때 action order와 tie-break.
6. 먼저 사망한 player의 이미 제출된 action을 실행할지 취소할지.
7. round 승리: last survivor, score, temperature ranking, 공동 승리 중 무엇인지.
8. 동시 사망과 draw 처리.
9. disconnect player를 즉시 탈락, AI 대체, reconnect 대기 중 무엇으로 처리할지.
10. reconnect가 필요하다면 좌석 예약 시간과 복귀 가능한 상태.
11. 상대 inventory와 pending action/target을 어느 시점에 누구에게 공개할지.
12. 전투 presentation을 순차, 부분 병렬, 완전 병렬 중 어떻게 재생할지.

## 3. 채택할 설계 패턴

| 경계 | 패턴 | 적용 이유 |
|---|---|---|
| 1대1→4인 이전 | Strangler Fig | 기존 pair 경로를 collection seam 뒤에서 단계적으로 교체 |
| match seat | Registry + stable roster | ClientId와 match-stable PlayerSeat 분리 |
| player input | Command | 검증된 `ActionIntent`를 server-only 불변 값으로 수집 |
| target/victory/disconnect | Strategy/Policy | mode별 규칙을 phase driver에서 분리 |
| combat | Deterministic pipeline | 동일 snapshot과 command가 동일 ordered events를 생성 |
| item effect | Functional Core / Imperative Shell + Strategy | pure multi-target outcome을 coordinator가 authoritative state에 적용 |
| Domain↔NGO | Anti-Corruption Layer / Adapter | 순수 Domain 모델과 `INetworkSerializable` DTO 분리 |
| state notification | Observer/Presenter | 복제 상태 관찰과 local UI/VFX 반응 분리 |
| app/match lifetime | Composition Root | 생성·연결·폐기 소유권을 scope 하나에 제한 |

일반 DI container, static service locator, global EventBus는 도입하지 않는다. 수동 composition과 작은 명시적 factory/binder를 사용한다.

## 4. Identity와 Roster 계약

### 4.1 ID domain

```text
ParticipantId  : 인증/로비에서 얻는 안정 참가자 식별자
PlayerIndex    : match 동안 고정되는 0..RequiredPlayerCount-1 seat
ClientId       : 현재 NGO connection 식별자, reconnect 시 변경 가능
OwnerClientId  : 현재 NetworkObject owner
LocalViewRole  : 각 client의 local/remote presentation 관계
```

`PlayerIndex`는 match 시작 후 재정렬하지 않는다. active ClientId 오름차순은 PLAN_018의 2인 호환 정책일 뿐 PLAN_019의 seat 정책이 아니다.

### 4.2 Roster 책임

서버 소유 roster가 다음을 담당한다.

- match 시작 전에 PlayerSeat 할당
- stable ParticipantId → PlayerIndex mapping; the stable participant key stays server-side unless an explicit client use is approved
- current ClientId → PlayerIndex binding
- disconnect 시 seat 상태 전환
- 승인된 reconnect 시 새 ClientId 재바인딩
- active, disconnected, eliminated, spectator 상태 구분

PlayerRegistry는 현재 spawn된 Unity 객체 binding만 소유한다. Roster는 match participant 정책을 소유한다. Registry가 승리 조건이나 reconnect 시간을 결정하지 않는다.

## 5. 동기화와 정보 공개 계약

### Public replicated state

모든 참가자가 알아야 하고 late join/reconnect가 복원해야 하는 값만 Everyone-readable 상태로 둔다.

- PlayerIndex/seat state
- temperature와 공개 fan 상태
- ready 여부가 공개 규칙일 때 ready state
- life/elimination state
- match-owned roster-indexed round score

### Server-only intent

다음 값은 combat reveal 전까지 서버 전용이다.

- selected item/slot
- sub-action
- target selection
- ready timestamp/order seed
- hidden modifier와 미공개 reveal 정보

이 값은 Everyone-readable `PlayerSnapshot`에 넣지 않는다. 클라이언트는 `[Rpc(SendTo.Server)]` intent를 보내고 서버가 sender, phase, ownership, slot, item use, target policy를 검증한 뒤 server-only `ActionIntent`를 생성한다.

### Snapshot grouping rule

NetworkVariable 수를 줄이기 위해 무조건 하나의 struct로 합치지 않는다. authority, read permission, lifetime, update cadence가 같은 값만 묶고 profiler/payload 측정 후 결정한다. Identity, inventory, secret intent는 각각 별도 계약을 유지한다.

현재 ItemId는 `short`와 `-1` sentinel을 사용하므로 wire DTO도 별도 schema migration 전까지 `short`를 유지한다.

## 6. Domain과 Network DTO 분리

Domain 예시:

```text
ActionIntent
TargetSelection
ItemEffectSpec
PlayerCombatState
MatchCombatSnapshot
CombatRuleContext
CombatResolution
CombatEvent
PlayerStateDelta
```

Networking 예시:

```text
ActionInputNetData : INetworkSerializable
CombatEventNetData : INetworkSerializable
PlayerStateDeltaNetData : INetworkSerializable
CombatResolutionBatchNetData : INetworkSerializable
```

Domain 타입은 NGO, MonoBehaviour, PlayerState, ScriptableObject runtime instance를 참조하지 않는다. Networking mapper가 ItemId와 pure config snapshot을 Domain 입력으로 변환하고 결과를 bounded wire DTO로 변환한다.

## 7. TargetSelection 계약

`TargetIndex = 1 - SourceIndex` 규칙을 제거한다. 최종 표현은 GAME_DESIGN 결정 후 확정하지만 최소한 다음 의미를 구분할 수 있어야 한다.

```text
None
Self
SinglePlayer(PlayerIndex)
PlayerMask(bit 0..3)
AllEligible
```

서버 검증:

- sender가 SourceIndex를 소유하는지
- 현재 phase에서 제출 가능한지
- target이 roster에 있고 해당 action에 eligible한지
- self/enemy/team 제한을 만족하는지
- eliminated/disconnected target 정책을 만족하는지
- 같은 intent를 중복 제출하지 않았는지

## 8. Deterministic Combat Pipeline

```text
collect validated ActionIntent for eligible actors
-> freeze MatchCombatSnapshot
-> build deterministic order
-> validate/normalize target set
-> execute commands against pure runtime state
-> append ordered CombatEvent values
-> compute final PlayerStateDelta values
-> evaluate elimination/victory policy
-> publish one bounded CombatResolutionBatchNetData
```

Order policy는 `GAME_DESIGN.md`에서 먼저 확정한다. ready timestamp, item priority, temperature, stable PlayerIndex는 후보 key일 뿐이며, 현재 P0 우선 tie-break를 그대로 일반화하지 않는다. 승인된 정책이 random을 요구하면 match seed와 deterministic `IRandomSource`를 사용하고 재현에 필요한 선택을 결과에 기록한다.

Turn intent collection은 configured seat 전체가 아니라 turn 시작 시 snapshot한 eligible actor set을 기다린다. elimination/disconnect/reconnect가 이 set에 미치는 영향은 승인된 match policy 하나가 결정하며, UI와 phase driver가 각자 다른 수를 계산하지 않는다.

결과는 player당 정확히 하나라고 가정하지 않는다. 한 action이 여러 target에 여러 event를 만들 수 있다. payload에는 최대 event 수, overflow 정책, serialization test를 둔다.

## 9. PresentationBarrier와 Presentation

Barrier pending set은 결과 `Begin` 시점의 active expected presentation client snapshot이다.

- spectator와 이후 연결자는 현재 barrier에 추가하지 않음
- disconnect는 pending에서 즉시 제거
- eliminated 상태여도 결과를 표현하는 연결 client라면 GAME_DESIGN 정책에 따라 포함 가능
- ACK는 sender ClientId + ResultSequence로 검증
- 같은 client의 중복/late/future ACK 무시

각 client presentation 구성:

- `MatchPresenter` 1개: phase, timer, round, roster summary
- `PlayerPresenter` N개: seat별 공개 상태
- local interactive InventoryPresenter 1개
- remote read-only inventory presenter 0..N-1개, 공개 정책 적용
- `CombatSequencePlayer` 1개: ordered/parallel presentation policy

## 10. 구현 단계

### 19-0 — Four-player characterization

- PLAN_018 2인 regression suite 통과 확인
- current pair assumption scan을 새 baseline으로 저장
- Host + remote 3개 테스트 환경과 로그 수집 경로 준비

### 19-1 — MatchConfig와 stable roster

- `RequiredPlayerCount` config 도입
- PlayerSeat/ParticipantId/ClientId domain 분리
- disconnect/reconnect policy 구현
- Registry와 Roster binding

### 19-2 — Lobby, Relay, spawn

- Lobby max player와 start gate 연결
- Relay maxConnections semantics 확인
- PlayerSpawnManager를 roster seat 기반 spawn point로 변경
- 2, 3, 4인 spawn/despawn 검증

### 19-3 — Intent와 target selection

- pure `ActionIntent`와 `TargetSelection`
- target UI와 local validation
- sender/phase/ownership/server policy validation
- secret intent 비공개 검증

### 19-4 — N-player combat engine

- immutable combat snapshot
- deterministic order/target/victory policies
- activate the match-seat-indexed `PlayerModifiers` collection prepared in PLAN_018 with `RequiredPlayerCount` from MatchConfig (up to 4); eliminated/disconnected seats remain addressable without renumbering
- extend the pure ItemEffect pipeline from PLAN_018 to self/single/multi/all target sets without reintroducing PlayerState/NV mutation into Domain rules
- ordered event + state delta output
- 2인 결과 golden test로 기존 동작 보존

### 19-5 — Network DTO와 barrier

- bounded batch serializer
- payload size/overflow tests
- expected presentation participant snapshot
- disconnect/late ACK/timeout 검증

### 19-6 — 4인 presentation

- responsive PlayerPresenter layout
- replace `_opponentPlayer`/`_opponentInventory` single binding with one local interactive inventory role plus a keyed collection of remote read-only roles
- camera/VFX target mapping
- sequential/parallel presentation 정책 구현

### 19-7 — Match lifecycle

- elimination, draw, winner, score
- `P1RoundWins`/`P2RoundWins`를 roster-indexed match score collection으로 이전
- round reset과 rematch epoch
- disconnect/reconnect across Prep/Attack/Presentation

### 19-8 — Full multiplayer validation

- Host + remote client 3개
- 모든 PlayerIndex seat에서 local player 검증
- 2인 regression + 3인/4인 configured match
- 두 round 이상, scene 왕복, 한 명씩 disconnect

## 11. API와 패키지 검증 gate

구현 전 exact installed versions를 다시 읽는다. 2026-08-19 기준 baseline은 Unity `6000.3.11f1`, NGO `2.11.2`, Transport `2.7.2`, Lobby `1.3.0`, Relay `1.0.5`다.

- 신규 RPC는 installed NGO에서 지원하는 universal `[Rpc(...)]`를 우선한다.
- sender identity는 RPC receive params에서 얻고 payload ClientId를 신뢰하지 않는다.
- custom NetworkVariable/RPC DTO의 installed serialization·equality 요구사항을 package source와 공식 문서로 확인한다.
- UGS `Task` boundary와 Unity `Awaitable` lifecycle boundary를 구분한다.
- 최신 호환 package가 있어도 자동 upgrade하지 않는다. upgrade는 별도 승인, changelog, rollback, Host+3 client test plan을 요구한다.
- NetworkPrefab과 필수 registry asset은 첫 Addressables 대상에서 제외한다.

## 12. 완료 불변식

1. match 동안 PlayerIndex는 고정되고 reconnect가 seat를 바꾸지 않는다.
2. ClientId, OwnerClientId, PlayerIndex, ParticipantId를 암묵적으로 변환하지 않는다.
3. eligible actor마다 authoritative intent는 turn당 최대 하나다.
4. secret item/target intent가 비인가 client에 복제되지 않는다.
5. 동일 input snapshot, seed, rules는 동일 ordered resolution을 만든다.
6. Barrier는 Begin 시점 expected clients만 기다린다.
7. phase driver와 NetworkVariable writer는 각각 하나다.
8. 2인 기존 gameplay golden result가 유지된다.
9. 모든 collection과 DTO는 최대 4인/최대 event bound를 검증한다.
10. disconnect/despawn/reconnect cleanup은 idempotent하다.

## 13. 완료 조건

- 2인 mode가 PLAN_018 regression gate를 계속 통과한다.
- 설정된 2~4인 roster가 올바른 stable seat를 가진다.
- Host + remote client 3개에서 lobby→relay→scene→spawn→두 round가 통과한다.
- 각 seat의 local UI, target selection, inventory visibility가 동일 규칙을 따른다.
- multi-target combat가 deterministic Domain test를 통과한다.
- DTO payload bound와 malformed input validation이 통과한다.
- Prep, resolution, presentation 중 각 client disconnect가 match policy에 따라 안전하게 종료된다.
- scene re-entry와 rematch에서 stale binding, event, task, ACK가 남지 않는다.
