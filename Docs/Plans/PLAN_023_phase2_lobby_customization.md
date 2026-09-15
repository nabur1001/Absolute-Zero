# PLAN_023 — PHASE 2: Lobby Redesign + Customization System (v3.2)

> **Status:** ✅ Complete — Phase A+B+C 전체 구현 완료 (컴파일 에러 0건, C-2 런타임 테스트 대기)
> **Created:** 2026-08-30
> **Dependencies:** GAME_DESIGN.md (L1~L6 ✅, CU1/CU4/CU5/CU6 ✅, CU2/CU3 ⏸ 보류)
> **Scope:** Sector 8 (Lobby) + Sector 7 (Customization) from PLAN_021. **1v1 전용** — 3~4인 Multi는 비활성 버튼만.
> **Will NOT Touch:** GameScene.unity, TurnManager, CombatEngine, ItemEffectApplicator, PresentationBarrier, CombatVFXManager, EnvironmentRuleService, `Assets/Scripts/UI/Game/*`, Network authority model
> **Will Touch:** `LobbyScene.unity` (Managers에 CosmeticProfileService + GameAudioManager 추가), `PlayerState.cs` (CosmeticDataNV 추가), `LobbyManager.cs` (SetPlayerCosmeticDataAsync 추가)

---

## Codex Review #4 필수 수정 (v3.1→v3.2)

| # | 지적 | v3.2 수정 |
|---|------|-----------|
| F1 | FromJson()이 예외 없이 null 반환 가능 (e.g. `"null"`) | TryValidateAndCanonicalizeDto 3단계 후 `dto == null` 검사 추가 (4단계로 분리). 변수명: `canonicalJson`(string) vs `canonical`(FixedString) |
| F2 | Overlay Apply 시 비활성 child를 SetActive(true) 안 함 + base renderer 재복사 누락 | Apply에서 재사용 시 `SetActive(true)` → base renderer 속성 매번 재복사 → cosmetic sprite/sortOrder 적용 |
| F3 | `DataObject.VisibilityOptions.Member` 오타 | `PlayerDataObject.VisibilityOptions.Member`로 수정 |

## Codex Review #4 추가 반영

| # | 권고 | v3.2 수정 |
|---|------|-----------|
| X1 | Registry OnEnable도 null/empty/포맷/Part-Type 검증 | OnEnable()에서 전체 검증 + 위반 항목 런타임 lookup 제외 |
| X2 | FromDto()에서도 DTO 필드 Part vs Registry Part 일치 확인 | FromDto에 Part 교차 검증 추가 |
| X3 | CosmeticProfileService.Instance null guard | OnNetworkSpawn/Rpc에서 Instance null 체크 + 로그 |
| X4 | 로비 중 연속 equip → Lobby API 다중 호출 | Closet 닫을 때 한 번만 PlayerData 갱신 (debounce) |
| X5 | Player_{shortId} 자동 생성값 UX | 자동 폴백은 PlayerPrefs에 저장하지 않음 → Main 입력 필드는 빈 상태 유지 → 유저가 입력하면 그때 저장 |
| X6 | 파일 수 17 new → 18 new (C# 15 + asset 3) | Summary 파일 수 수정: 18 new + 7 modify |

## Codex Review #3 반영 이력 (v3→v3.1)

<details>
<summary>M1~M4 필수 + R1~R6 HIGH (v3.1에서 해결)</summary>

| # | 수정 |
|---|------|
| M1 | TryValidateAndCanonicalizeDto 8단계 canonical 검증 |
| M2 | 파트별 overlay 이름 `_cosmetic_overlay_{Part}` |
| M3 | LobbyManager cosmetic API + Will Touch |
| M4 | OnNetworkSpawn cosmetic → MatchCompositionRoot 앞 |
| R1 | _hasAcceptedCosmetic 잠금 |
| R2 | 닉네임 Authentication 독립 |
| R3 | overlay base renderer 속성 복사 |
| R4 | malformed → NV 미변경 |
| R5 | LOBBY_UI_SPEC A-0 이동 |
| R6 | backing field + getter |

</details>

---

## Codex Review #1~#2 반영 이력

<details>
<summary>v1→v2 수정 (15건), v2→v3 수정 (BLOCKER 3건 + HIGH 10건)</summary>

### v2 BLOCKER 해결
| # | BLOCKER | 해결 |
|---|---------|------|
| B1 | Lobby→NV 복사 불가 | Owner→ServerRpc→NV 흐름으로 전환 |
| B2 | Registry 소유자 미정 | CosmeticProfileService (Core, DDOL) 도입 |
| B3 | GameAudioManager LobbyScene 부재 | Managers에 추가 |

### v3 HIGH 해결 (H1~H10)
Id 1~8자 ASCII, 고정필드 DTO, overlay 재사용, OnNetworkDespawn 해제, 닉네임 Regex, CreatePlayerData cosmetic 포함, LOBBY_UI_SPEC 동기화, disposed guard, OnValidate, versioned JSON 폴백 — 전부 반영

</details>

---

## Design Pattern Analysis

### Pattern Selection

| Pattern | System | Selected |
|---------|--------|----------|
| **MVP** | Lobby UI | LobbyPresenter (service events + commands) + passive Views |
| **State Machine** | Navigation | LobbyViewState enum, Presenter.SetState() |
| **Strategy** | Cosmetic apply | ICosmeticApplier: Overlay, Swap (Tint 제외) |
| **Observer** | Equip→visual | CosmeticEquipState.OnEquipChanged |
| **Service Locator** | CosmeticProfileService | DDOL singleton on Managers, Core assembly |

### API/Package Audit

- **Unity 6 (6000.3.11f1):** `async Task` 유지
- **NGO 2.11.2:** `NetworkVariable<FixedString128Bytes>` (125 UTF-8 bytes). WritePerm=Server. Owner가 `[Rpc(SendTo.Server)]`로 DTO 전송 → 서버 검증/canonical화 후 NV 기록
- **Unity Lobby PlayerData:** 10 attrs, 2KB/player. 현재 3개 + CosmeticData = 4개. Visibility=Member. 로비 참가 시점에 포함
- **JsonUtility:** Dictionary 불가 → **고정 필드 DTO class** (`CosmeticDto`)
- **No package changes**

### Class Mapping (v3.2)

```
Assets/Scripts/Core/Cosmetic/
├── CosmeticItemSO.cs          ← NEW: SO (Id 1~8 ASCII, Part, Type, Sprite, SortOrderOffset)
├── CosmeticRegistrySO.cs      ← NEW: SO list + GetById/GetByPart + OnValidate 검증
├── CosmeticDto.cs             ← NEW: 고정 필드 DTO (v, head, top, back, bottom, tail) + JsonUtility
├── CosmeticEquipState.cs      ← NEW: 5-part equip + versioned JSON save/load + events
├── CosmeticProfileService.cs  ← NEW: DDOL singleton (Registry, EquipState, save/load, canonical DTO, 닉네임)
├── ICosmeticApplier.cs         ← NEW: Apply/Remove (idempotent, partName 파라미터)
├── OverlayApplier.cs           ← NEW: 파트별 "_cosmetic_overlay_{Part}" 이름 + base renderer 속성 복사
├── SwapApplier.cs              ← NEW: sprite swap + original backup
├── CosmeticVisualController.cs ← NEW: equip state → applier dispatch

Assets/Scripts/UI/Lobby/
├── AZLobbyUI.cs               ← REWRITE: composition root (View factory + Presenter 생성)
├── LobbyPresenter.cs          ← NEW: service events + async commands + state machine + _disposed guard
├── LobbyMainView.cs           ← NEW: passive View (buttons → Action)
├── LobbyModeSelectView.cs     ← NEW: 1v1 active, Multi disabled
├── LobbyRoomView.cs           ← NEW: passive View (lobby render)
├── LobbySettingsView.cs       ← NEW: volume sliders
├── ClosetView.cs              ← NEW: minimal placeholder (tab + list)

Assets/Scripts/Core/Player/
├── PlayerState.cs             ← MODIFY: CosmeticDataNV + SubmitCosmeticRpc (CosmeticProfileService.TryValidateAndCanonicalizeDto 호출) + _hasAcceptedCosmetic + OnNetworkDespawn
├── AZPlayerVisual.cs          ← MODIFY: 5-part Transform refs + bind 후 cosmetic 적용

Assets/Scripts/Core/Audio/
├── GameAudioManager.cs        ← MODIFY: base gain × master multiplier + PlayerPrefs 복원

Assets/Scripts/Core/Network/
├── LobbyManager.cs            ← MODIFY: SetPlayerCosmeticDataAsync(string dto) 추가

Assets/Scripts/Core/Session/
├── NetworkSessionCoordinator.cs ← MODIFY: CreatePlayerData()에 닉네임 + cosmeticDto 포함

Scene:
├── LobbyScene.unity           ← MODIFY: Managers에 CosmeticProfileService + GameAudioManager 추가
```

### Data Flow (v3.2)

```
[Lobby — 프리뷰용]
  CosmeticProfileService (DDOL)
    - owns CosmeticRegistrySO (SerializeField)
    - owns CosmeticEquipState
    - owns saved nickname (PlayerPrefs, 빈 경우 CreatePlayerData에서 폴백)
    
  Coordinator.CreatePlayerData():
    - "PlayerName" = CosmeticProfileService.Nickname (빈 경우 "Player_{shortId}" 폴백)
    - "CosmeticData" = CosmeticProfileService.GetCompactDto()
    → Lobby 참가/생성 시 포함 → 상대방 poll에서 프리뷰 확인 가능

  Closet equip 변경 + 로비 중:
    → CosmeticProfileService.Equip/Unequip
    → LobbyManager.SetPlayerCosmeticDataAsync(dto) — PlayerData 갱신 (Visibility=Member)
    → 실패 시 로컬 장착 상태 유지, 프리뷰만 미갱신

  LobbyPresenter → CosmeticProfileService 참조
    - Closet equip/unequip → CosmeticProfileService.Equip/Unequip
    - Nickname 변경 → CosmeticProfileService.SetNickname + LobbyManager.SetPlayerNameAsync

[GameScene — 실제 표현 (owner→ServerRpc→canonical NV)]
  각 Owner PlayerState.OnNetworkSpawn():
    — CosmeticDataNV.OnValueChanged 구독 (MatchCompositionRoot 체크보다 앞)
    → if (IsOwner) SubmitCosmeticRpc(CosmeticProfileService.Instance.GetCompactDto())
    
  서버 SubmitCosmeticRpc 수신:
    1. _hasAcceptedCosmetic == true → 거부 (게임 중 변경 방지)
    2. SenderClientId == OwnerClientId 검증
    3. TryValidateAndCanonicalizeDto(raw, out canonical):
       a. null/empty → fail
       b. Encoding.UTF8.GetByteCount(raw) > 125 → fail
       c. try { dto = JsonUtility.FromJson<CosmeticDto>(raw); } catch → fail
       d. dto == null → fail (FromJson은 "null" 등에서 예외 없이 null 반환 가능)
       e. dto.v != 1 → fail
       f. 각 파트 Id: 빈 문자열이면 OK, 아니면 Registry.GetById→null이면 fail, Part 불일치면 fail
       g. string canonicalJson = JsonUtility.ToJson(dto) 재직렬화
       h. Encoding.UTF8.GetByteCount(canonicalJson) > 125 → fail
       i. out canonical = new FixedString128Bytes(canonicalJson) → true
    4. CosmeticDataNV.Value = canonical
    5. _hasAcceptedCosmetic = true
    — 실패 시 Debug.LogWarning + NV 미변경 (마지막 valid 값 유지)
    
  모든 클라이언트:
    → CosmeticDataNV.OnValueChanged → OnCosmeticNVChanged
    → remote: TryBindEnemyVisual 완료 여부
      - bind 완료 → 즉시 CosmeticVisualController.ApplyFromDto(newValue)
      - bind 미완료 → 플래그, bind 성공 콜백에서 현재 NV 값 적용
    → overlay 재사용: 파트별 "_cosmetic_overlay_{Part}" child
      SetActive(true) → base renderer 속성 재복사 → cosmetic sprite/sortOrder 적용
    → idempotent: swap은 원복 후 재적용
    
  OnNetworkDespawn():
    → CosmeticDataNV.OnValueChanged -= OnCosmeticNVChanged (반드시 해제)
    
  재입장 (disconnect→rejoin):
    → 새 PlayerState → _hasAcceptedCosmetic=false → SubmitCosmeticRpc 재전송 → NV 재기록
```

---

## Phase A: Lobby UI Restructure (Sector 8)

### A-0: Pre-implementation Spec Sync

- [x] **A-0a** `Docs/LOBBY_UI_SPEC.md` — v3.2 동기화: base gain×master multiplier 패턴, CosmeticDto 고정필드 구조, Tint 제거, Closet 최소 placeholder, maxPlayers setter 미호출, canonical DTO, 파트별 overlay 이름
- [x] **A-0b** LOBBY_UI_SPEC.md에서 직접 volume 설정 → master multiplier 방식으로 변경

### A-1: MVP Skeleton + Navigation

- [x] **A-1a** `LobbyViewState` enum: `Main, ModeSelect, Room, Settings, Closet`
- [x] **A-1b** `LobbyPresenter.cs`:
  - Constructor: all Views + CosmeticProfileService ref
  - `Initialize()`: Coordinator/LobbyManager 이벤트 구독 + View Action 구독
  - `Dispose()`: 전체 이벤트 해제 + View Action -= 해제 + `_disposed = true`
  - `SetState(LobbyViewState)`: Hide current → Show target
  - 서비스 이벤트 핸들러 → View render 호출 (disposed guard)
  - Async 커맨드 → Coordinator 호출 + disposed 후 접근 방지
- [x] **A-1c** `AZLobbyUI.cs` 리팩터 → composition root:
  - Canvas/EventSystem 생성
  - 각 View.BuildUI(root)
  - LobbyPresenter 생성 + Initialize()
  - OnDestroy: Presenter.Dispose()
- [x] **A-1d** MainPanel 빌드 로직 → `LobbyMainView.BuildUI()` 추출
- [x] **A-1e** LobbyPanel 빌드 로직 → `LobbyRoomView.BuildUI()` 추출
- [x] **A-1f** View button events → Action 노출:
  - LobbyMainView: `Action OnArenaClicked, OnClosetClicked, OnSettingsClicked`
  - LobbyRoomView: `Action OnCreateClicked, Action<string> OnJoinClicked, Action OnStartClicked, Action OnLeaveClicked, Action OnBackClicked`
  - LobbyModeSelectView: `Action OnOneVsOneClicked, Action OnBackClicked`
  - LobbySettingsView: `Action OnCloseClicked`
  - ClosetView: `Action OnCloseClicked` (Phase B)
- [x] **A-1g** Presenter에서 View Action 구독 → service 호출 라우팅
- [x] **A-1h** 컴파일 확인 + 기존 Create/Join/Start/Leave 플로우 동작 보존

### A-2: Main Screen

- [x] **A-2a** `LobbyMainView.BuildUI()`: 기존 스프라이트 (lobby_nickname, lobby_arena, lobby_closet) 유지 + Solo/Settings 추가
  - Solo: `interactable=false` + "Coming Soon"
  - Closet: Phase B 완료 전 `interactable=false`
- [x] **A-2b** 로비 배경 이미지 (Resources/로비화면)

### A-3: Mode Select (1v1 전용)

- [x] **A-3a** `LobbyModeSelectView.BuildUI()`: dim + 패널
  - 1v1 버튼 활성 → Presenter.SetState(Room)
  - Multi 3~4인: `interactable=false` + "Coming Soon"
  - 뒤로 / dim 클릭 → Main
- [x] **A-3b** maxPlayers=2 고정 (LobbyManager.MaxPlayers setter 호출 안 함)

### A-4: Room View (1v1 전용)

- [x] **A-4a** `LobbyRoomView.BuildUI()`: 기존 LobbyPanel 로직 + 모드 라벨 "1 vs 1 대결" 고정
- [x] **A-4b** `RenderLobby(Lobby)`: 슬롯 2개 고정, 코드, 시작 버튼
- [x] **A-4c** `ApplyMembership(bool)`: 생성/참가 UI 토글
- [x] **A-4d** 뒤로 → LeaveAsync + ModeSelect
- [x] **A-4e** 로그 스크롤 영역 유지

### A-5: Nickname System

- [x] **A-5a** `CosmeticProfileService`에 닉네임 저장: PlayerPrefs `"player_nickname"`. Nickname getter: PlayerPrefs 없으면 빈 문자열 반환 (Authentication 타이밍 독립). **자동 생성된 `Player_{shortId}` 폴백은 PlayerPrefs에 저장하지 않음** → 유저가 직접 입력해야 저장
- [x] **A-5b** 앱 시작 시 복원 → LobbyMainView 입력 필드 초기값. 빈 경우 필드 text="" + placeholder "닉네임 입력" (자동 폴백값은 표시하지 않음)
- [x] **A-5c** 검증: `Trim()` → Unicode FormC → Regex `^[가-힣a-zA-Z0-9]+$` → `StringInfo.LengthInTextElements` 2~8. TMP characterLimit은 보조, **onEndEdit 검증이 최종**
- [x] **A-5d** 저장: **onEndEdit** → CosmeticProfileService.SetNickname() → PlayerPrefs + 로비 중이면 LobbyManager.SetPlayerNameAsync()
- [x] **A-5e** `Coordinator.CreatePlayerData()` 수정: `"PlayerName"` 값을 `CosmeticProfileService.Instance`에서 읽기. 빈 문자열이면 `"Player_{shortId}"` 폴백 생성
- [x] **A-5f** 금칙어/중복: 이번 범위 밖 (Discovered Issues)

### A-6: Settings Canvas (L4 기반)

- [x] **A-6a** `LobbySettingsView.BuildUI()`: dim + 패널
- [x] **A-6b** BGM 슬라이더 (0~1) → `GameAudioManager.SetBGMVolume(val)` + PlayerPrefs `"bgm_volume"`
- [x] **A-6c** SFX 슬라이더 (0~1) → `GameAudioManager.SetSFXVolume(val)` + PlayerPrefs `"sfx_volume"`
- [x] **A-6d** 닫기 / dim → Main
- [x] **A-6e** `GameAudioManager` 수정:
  - base gain 상수: `_bgmBase=0.35f, _sfxBase=0.7f, _uiBase=0.5f, _envBase=0.6f, _fanBase=0.15f, _clockBase=0.5f`
  - `SetBGMVolume(float m)`: `_bgmSource.volume = _bgmBase * m`
  - `SetSFXVolume(float m)`: `_sfxSource.volume = _sfxBase * m; _uiSource.volume = _uiBase * m; _envSource.volume = _envBase * m; _fanLoopSource.volume = _fanBase * m; _clockSource.volume = _clockBase * m`
  - `Awake()`: PlayerPrefs 읽기 (기본 1.0f) → Set 호출. master=1.0 → 기존 gain 보존
- [x] **A-6f** `LobbyScene.unity` Managers에 **GameAudioManager + CosmeticProfileService 컴포넌트 추가** (MCP 사용)
  - GameUIRoot.EnsureAudioManager(): `Instance != null` 이면 스킵 — 기존 가드 있음 (line 68)

### A-7: Coordinator PlayerData 통합

- [x] **A-7a** `NetworkSessionCoordinator.CreatePlayerData()` 수정:
  - `"PlayerName"` → `CosmeticProfileService.Instance`에서 읽기. 빈 경우 `"Player_{shortId}"` 폴백
  - `"CosmeticData"` 키 추가 → `CosmeticProfileService.Instance?.GetCompactDto() ?? ""`
  - Closet 장착 후 로비 재참가 없이도 로비 생성/참가 시점에 최신 데이터 포함

---

## Phase B: Customization System (Sector 7)

### B-1: Data Model

- [x] **B-1a** `CosmeticItemSO.cs`:
  ```
  enum CosmeticPart { Head, Top, Back, Bottom, Tail }
  enum CosmeticType { Overlay, Swap }  // Tint 제외
  
  [SerializeField] private string _id;    // ASCII [a-z0-9_], 1~8자, 불변 (FixedString 125byte cap)
  public string Id => _id;
  [SerializeField] private CosmeticPart _part;
  public CosmeticPart Part => _part;
  [SerializeField] private CosmeticType _type;
  public CosmeticType Type => _type;
  [SerializeField] private Sprite _sprite;
  public Sprite Sprite => _sprite;
  [SerializeField] private int _sortOrderOffset;
  public int SortOrderOffset => _sortOrderOffset;
  [SerializeField] private string _displayName;
  public string DisplayName => _displayName;
  ```
- [x] **B-1b** `CosmeticRegistrySO.cs`:
  - `List<CosmeticItemSO> AllItems`
  - `CosmeticItemSO GetById(string id)` → null if not found
  - `List<CosmeticItemSO> GetByPart(CosmeticPart part)`
  - `OnValidate()`: null/empty Id 검사, 중복 Id 검사, Id 포맷 검증 (`^[a-z0-9_]{1,8}$`), Part별 허용 Type 검증 (Debug.LogError)
  - `OnEnable()`: 런타임 전체 검증 (null/empty Id, 중복, 포맷, Part-Type 규칙) + 위반 항목은 `_validItems` 리스트에서 제외하여 런타임 lookup 불가. **중복 Id 정책: 동일 Id를 가진 항목이 2개 이상이면 해당 Id의 모든 항목을 제외** (어느 것이 정본인지 판단 불가)
  - 파트별 허용 Type: Head=Overlay, Top=Overlay|Swap, Back=Overlay, Bottom=Overlay|Swap, Tail=Overlay
- [x] **B-1c** `CosmeticDto.cs` — 고정 필드 DTO:
  ```csharp
  [System.Serializable]
  public class CosmeticDto
  {
      public int v = 1;
      public string head = "";
      public string top = "";
      public string back = "";
      public string bottom = "";
      public string tail = "";
  }
  // JsonUtility.ToJson / FromJson 사용
  // canonical 직렬화 후 UTF-8 125 byte 이하 검증
  ```
- [x] **B-1d** Registry asset: `Assets/Data/Cosmetics/CosmeticRegistry.asset`
- [x] **B-1e** 샘플 SO: `Assets/Data/Cosmetics/Items/hat_01.asset`, `cape_01.asset`

### B-2: CosmeticProfileService (DDOL)

- [x] **B-2a** `CosmeticProfileService.cs` — Managers에 배치, DDOL (Managers root가 LobbyManager.Awake()에서 DontDestroyOnLoad 처리 → 별도 호출 불필요):
  - `[SerializeField] CosmeticRegistrySO _registry`
  - `CosmeticEquipState EquipState { get; }`
  - `string Nickname { get; }` — PlayerPrefs `"player_nickname"`, 없으면 빈 문자열 (Authentication 독립)
  - `void SetNickname(string n)` — 검증 + 저장
  - `string GetCompactDto()` — EquipState → CosmeticDto → JsonUtility.ToJson → UTF-8 125byte 검증
  - `bool TryValidateAndCanonicalizeDto(string raw, out FixedString128Bytes canonical)`:
    1. null/empty → false
    2. `Encoding.UTF8.GetByteCount(raw) > 125` → false
    3. `try { dto = JsonUtility.FromJson<CosmeticDto>(raw); } catch → false`
    4. `dto == null` → false (FromJson은 `"null"` 등에서 예외 없이 null 반환)
    5. `dto.v != 1` → false
    6. 각 파트 Id: 빈 문자열이면 통과, 아니면 `Registry.GetById(id)` → null이면 false, Part 불일치면 false
    7. `string canonicalJson = JsonUtility.ToJson(dto)` 재직렬화
    8. `Encoding.UTF8.GetByteCount(canonicalJson) > 125` → false
    9. `canonical = new FixedString128Bytes(canonicalJson)` → true
  - `CosmeticRegistrySO Registry` getter (GameScene에서 접근용)
  - `Awake()`: singleton + Load
- [x] **B-2b** `LobbyScene.unity` Managers에 CosmeticProfileService 컴포넌트 추가 + Registry SO 연결 (MCP)

### B-3: Equipment Logic

- [x] **B-3a** `CosmeticEquipState.cs`:
  - `Dictionary<CosmeticPart, CosmeticItemSO> _equipped`
  - `Equip(CosmeticItemSO item)` + `Unequip(CosmeticPart)` + `OnEquipChanged` event
  - `CosmeticDto ToDto()` — 각 파트 equipped Id 또는 ""
  - `void FromDto(CosmeticDto dto, CosmeticRegistrySO registry)` — Id → GetById, 미존재 시 무시 (로그). **DTO 필드명(head/top/...)과 item.Part가 일치하는지 교차 검증** → 불일치 시 해당 파트 무시
- [x] **B-3b** Save/Load (CosmeticProfileService에서 호출):
  - `Save()`: ToDto() → JsonUtility.ToJson → PlayerPrefs `"cosmetic_equip_v1"`
  - `Load(CosmeticRegistrySO)`: PlayerPrefs → try JsonUtility.FromJson<CosmeticDto> → catch → 빈 상태 폴백. v!=1 → 빈 상태 폴백. null/missing 필드 → 해당 파트 빈. 존재하지 않는 Id → Registry.GetById null → 해당 파트 무시 (로그)

### B-4: Visual Apply System

- [x] **B-4a** `ICosmeticApplier.cs`: `Apply(Transform partRoot, CosmeticItemSO item, CosmeticPart part)`, `Remove(Transform partRoot, CosmeticPart part)` — idempotent, part 파라미터로 이름 분리
- [x] **B-4b** `OverlayApplier.cs`:
  - Apply: partRoot에서 `$"_cosmetic_overlay_{part}"` 이름 child 검색
    - 재사용 (있으면): `gameObject.SetActive(true)` → base SpriteRenderer에서 `sortingLayerID`, `material`, `flipX`, `flipY` **매번 재복사** (방향/material 변경 대응) → cosmetic sprite/sortOrder 적용
    - 신규 (없으면): 새 child 생성 → base 속성 복사 → cosmetic sprite/sortOrder 적용
  - Remove: child 있으면 `gameObject.SetActive(false)` (Destroy 대신 비활성화로 재사용 보장)
  - Top과 Back이 body를 공유해도 `_cosmetic_overlay_Top`과 `_cosmetic_overlay_Back`으로 독립 → 충돌 없음
- [x] **B-4c** `SwapApplier.cs`:
  - `Dictionary<(Transform, CosmeticPart), Sprite> _originals` — 원본 백업
  - Apply: 원본 저장 → sprite 교체
  - Remove: 원본 복원 + _originals에서 제거
- [x] **B-4d** `CosmeticVisualController.cs`:
  - `Dictionary<CosmeticPart, Transform> _partRoots`
  - `ApplyAll(CosmeticEquipState)` — 5파트 적용
  - `ApplyFromDto(string json, CosmeticRegistrySO registry)` — parse → 적용
  - `Clear()` — 전체 제거 (idempotent)
  - 내부: clear → apply 순서 (idempotent)

### B-5: Closet UI (최소 Placeholder — CU2 보류)

- [x] **B-5a** `ClosetView.BuildUI()`: dim + 패널
  - 파트 탭 5개: 클릭 → 리스트 필터
  - VerticalLayoutGroup 리스트 (아이템 DisplayName + equip/unequip 토글)
  - 닫기 버튼
  - **프리뷰, 좌우분할, 그리드는 CU2 확정 전 구현하지 않음**
- [x] **B-5b** 리스트: CosmeticProfileService.Registry.GetByPart(tab) → 버튼 동적 생성
- [x] **B-5c** 클릭 → Presenter.EquipCosmetic / UnequipCosmetic → CosmeticProfileService
- [x] **B-5d** 닫기 → Save + Presenter.SetState(Main)

### B-6: Multiplayer Cosmetic Sync (Owner→ServerRpc→Canonical NV)

**Lobby (프리뷰 전용)**
- [x] **B-6a** Coordinator.CreatePlayerData()에서 `"CosmeticData"` 포함 (A-7a에서 처리)
- [x] **B-6b** **Closet 닫을 때** 한 번만 `LobbyManager.SetPlayerCosmeticDataAsync(dto)` 호출 (로비 참가 중일 때만). 연속 equip 클릭은 로컬만 변경하고 Lobby API는 닫기 시점에 debounce. 실패 시 로컬 장착 유지, 프리뷰만 미갱신
- [x] **B-6c** `LobbyManager.cs` 수정:
  - `SetPlayerCosmeticDataAsync(string dto)`: PlayerData `"CosmeticData"` 갱신 (기존 SetPlayerNameAsync 패턴 복제)
  - Visibility = `PlayerDataObject.VisibilityOptions.Member`

**GameScene (서버 canonical 기록)**
- [x] **B-6d** `PlayerState.cs` 수정:
  - `NetworkVariable<FixedString128Bytes> CosmeticDataNV` — ReadPerm=Everyone, WritePerm=Server
  - `private bool _hasAcceptedCosmetic = false`
  - `[Rpc(SendTo.Server)] void SubmitCosmeticRpc(string dto, RpcParams rpcParams = default)`:
    - `_hasAcceptedCosmetic == true` → Debug.LogWarning + 거부 (게임 중 변경 방지)
    - `rpcParams.Receive.SenderClientId == OwnerClientId` 검증
    - `CosmeticProfileService.Instance` null 체크 (null → LogWarning + 거부)
    - `CosmeticProfileService.Instance.TryValidateAndCanonicalizeDto(dto, out var canonical)` 호출
    - 통과 시 `CosmeticDataNV.Value = canonical; _hasAcceptedCosmetic = true`
    - 실패 시 Debug.LogWarning + NV 미변경 (마지막 valid 값 유지)
  - `OnNetworkSpawn()` 추가 — **MatchCompositionRoot null 체크보다 앞에 배치**:
    - `CosmeticDataNV.OnValueChanged += OnCosmeticNVChanged`
    - `if (IsOwner && CosmeticProfileService.Instance != null) SubmitCosmeticRpc(CosmeticProfileService.Instance.GetCompactDto())`
    - Instance null 시 Debug.LogWarning (DDOL 미로드 → cosmetic 없이 진행)
  - `OnNetworkDespawn()` 추가: `CosmeticDataNV.OnValueChanged -= OnCosmeticNVChanged` (**반드시 해제**)
- [x] **B-6e** Remote cosmetic 적용:
  - `OnCosmeticNVChanged`: remote인 경우 → TryBindEnemyVisual 완료 여부 확인
    - bind 완료 → 즉시 `CosmeticVisualController.ApplyFromDto(newValue)`
    - bind 미완료 → 플래그 설정, bind 성공 콜백에서 현재 NV 값 적용
- [x] **B-6f** 재입장: 새 PlayerState → `_hasAcceptedCosmetic=false` → OnNetworkSpawn → SubmitCosmeticRpc 재전송 → NV 재기록 (자동 해결)
- [x] **B-6g** Owner(FPS) 모드: cosmetic visual 비적용 (1인칭)

### B-7: AZPlayerVisual Hookup

- [x] **B-7a** 5파트 Transform 참조: `_headPart`, `_topPart`, `_backPart`, `_bottomPart`, `_tailPart`
  - Top/Back 모두 body 하위에서 Find하되 CosmeticVisualController에는 별도 Transform으로 전달 → 파트별 overlay 이름으로 충돌 방지
- [x] **B-7b** TryBindEnemyVisual() 성공 시: 파트 Find + CosmeticVisualController 초기화 + CosmeticDataNV 현재 값 적용
- [x] **B-7c** Tail 파트: 기존 child 없으면 새 GameObject 생성 (lowerbody 하위, sortOrder 조정)

---

## Phase C: Integration & Validation

### C-1: Cross-cutting

- [x] **C-1a** Main Closet 버튼 활성화 (Phase B 완료 후)
- [x] **C-1b** Solo 버튼 "Coming Soon" + disabled
- [x] **C-1c** Multi 3~4인 "Coming Soon" + disabled

### C-2: Test Scenarios

- [ ] **C-2a** Main → Multi → 1v1 → Room → Create/Join → Game (기존 보존)
- [ ] **C-2b** Main → Closet → equip → 닫기 → Multi → Game (NV cosmetic 확인)
- [ ] **C-2c** Host cosmetic ≠ Remote cosmetic 양쪽 정상 (Top+Back 동시 overlay 포함)
- [ ] **C-2d** 앱 재시작 → PlayerPrefs 복원 → 게임 진입 → 상대에게 NV로 반영
- [ ] **C-2e** 미등록 Id (SO 삭제 후) → 정상 폴백 (해당 파트 빈)
- [ ] **C-2f** 2라운드 이후 cosmetic 유지
- [ ] **C-2g** 재입장 (disconnect→rejoin) → _hasAcceptedCosmetic=false → SubmitCosmeticRpc 재전송 → 정상
- [ ] **C-2h** malformed DTO 전송 (변조 클라) → 서버 TryValidateAndCanonicalizeDto 거부 + **NV 미변경 (마지막 valid 값 유지)**
- [ ] **C-2i** DTO 125byte 초과 → 서버 byte 선검사 거부 + NV 미변경
- [ ] **C-2j** 게임 중 SubmitCosmeticRpc 재호출 → _hasAcceptedCosmetic=true → 거부

### C-3: Documentation Sync

- [x] **C-3a** `Docs/RECENT_CHANGES.md` 갱신
- [x] **C-3b** `Docs/ACTIVE_CONTEXT.md` 갱신
- [x] **C-3c** `Docs/CHANGES.md` 엔트리
- [x] **C-3d** PLAN_021 섹터 7, 8 상태 업데이트

---

## Scope Guard

### Will NOT Touch
- `Assets/Scripts/Core/Game/` — TurnManager, CombatEngine
- `Assets/Scripts/Core/Combat/` — VFX, 환경
- `Assets/Scripts/Core/Item/` — 아이템 파이프라인
- `Assets/Scripts/UI/Game/` — MatchHudPresenter, GameDataBridge 등 게임 내 UI
- `GameScene.unity`
- Network authority model
- `LobbyManager.MaxPlayers` — 2 고정

### Will Touch (명시)
- `LobbyScene.unity` — Managers에 CosmeticProfileService + GameAudioManager 추가
- `PlayerState.cs` — CosmeticDataNV + SubmitCosmeticRpc (CosmeticProfileService.TryValidateAndCanonicalizeDto 호출) + _hasAcceptedCosmetic
- `AZPlayerVisual.cs` — 5파트 refs + bind 후 cosmetic
- `GameAudioManager.cs` — base gain × master multiplier
- `LobbyManager.cs` — SetPlayerCosmeticDataAsync 추가
- `NetworkSessionCoordinator.cs` — CreatePlayerData 닉네임/cosmetic

### Blocked (추후)
- CU2 (Closet 레이아웃) — 보류
- CU3 (색상/Tint) — 보류, TintApplier 미생성
- Solo 진입 — PHASE 6
- 3~4인 Multi — PHASE 3~4
- 금칙어/중복 닉네임 — 서버사이드
- MatchHudPresenter 닉네임 — 별도 플랜

### Discovered Issues
(작업 중 기록)

---

## Summary

| Phase | Tasks | Files | Core |
|-------|-------|-------|------|
| A (Lobby) | 32 | 8 new, 3 modify | Spec sync, MVP, 1v1 모드선택, 닉네임(Authentication 독립), Settings, Coordinator PlayerData |
| B (Customization) | 27 | 9 new, 4 modify | ProfileService, canonical DTO, Overlay(파트별 이름)/Swap, Closet placeholder, Owner→Rpc→canonical NV, _hasAcceptedCosmetic |
| C (Integration) | 17 | docs sync | 10 테스트 시나리오 (byte/lock/overlay 포함) + 문서 동기화 |
| **Total** | **76** | **18 new (C# 15 + asset 3), 7 modify (C# 5 + scene 1 + rewrite 1)** | |
