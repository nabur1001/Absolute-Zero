# Lobby UI Visual Spec

> PLAN_023 로비 UI 구현 가이드. 모든 색상/크기/배치를 Unity C# 코드 빌드 기준으로 기술.
> Reference: 1920×1080 / CanvasScaler ScaleWithScreenSize / match 0.5

---

## Color System

### Unity Color 값 (C# `new Color(r, g, b, a)` — 0~1 범위)

| 이름 | Hex | Unity Color | 용도 |
|------|-----|-------------|------|
| **Wood Dark** | `#2C1810` | `(0.17f, 0.09f, 0.06f)` | 텍스트, 헤더 |
| **Wood Mid** | `#8B7355` | `(0.55f, 0.45f, 0.33f)` | 테두리, 비활성 버튼 |
| **Wood Light** | `#B8A080` | `(0.72f, 0.63f, 0.50f)` | 배경 데코, 구분선 |
| **Cream** | `#F5EDE0` | `(0.96f, 0.93f, 0.88f)` | 버튼 배경, 패널 배경 |
| **Cream Dim** | `#EDE4D4` | `(0.93f, 0.89f, 0.83f)` | 비활성 탭, 빈 슬롯 |
| **단청 Red** | `#C4483C` | `(0.77f, 0.28f, 0.24f)` | 주요 버튼, 활성 탭, 게임 시작 |
| **Red Hover** | `#A83A30` | `(0.66f, 0.23f, 0.19f)` | Red 호버/프레스 |
| **Teal** | `#3D7B80` | `(0.24f, 0.48f, 0.50f)` | 보조 버튼 (로비 생성, 슬라이더) |
| **Teal Hover** | `#2E6266` | `(0.18f, 0.39f, 0.40f)` | Teal 호버/프레스 |
| **Gold** | `#D4A54A` | `(0.83f, 0.65f, 0.29f)` | 호스트 슬롯, 로비 코드, 경고 |
| **Success Green** | `#4A8B5C` | `(0.29f, 0.55f, 0.36f)` | 참가 버튼, 내 슬롯 |
| **Dim Overlay** | `#1E140C` | `(0.12f, 0.08f, 0.05f, 0.55f)` | 모달 배경 딤 |
| **Panel BG** | `#F5EDE0` | `(0.96f, 0.93f, 0.88f, 0.96f)` | 모달 패널 |
| **Slot Empty** | `#E8E0D4` | `(0.91f, 0.88f, 0.83f, 0.50f)` | 빈 플레이어 슬롯 |
| **Slot Host** | `#D4A54A` | `(0.83f, 0.65f, 0.29f, 0.80f)` | 호스트 슬롯 |
| **Slot Me** | `#4A8B5C` | `(0.29f, 0.55f, 0.36f, 0.80f)` | 내 슬롯 |
| **Slot Other** | `#5A7B9B` | `(0.35f, 0.48f, 0.61f, 0.80f)` | 상대 슬롯 |
| **Input BG** | `#FFFFFF` | `(1f, 1f, 1f)` | 입력 필드 배경 |
| **Input Border** | `#D4C8B8` | `(0.83f, 0.78f, 0.72f)` | 입력 필드 테두리 |
| **Text Primary** | `#2C1810` | `(0.17f, 0.09f, 0.06f)` | 본문 텍스트 |
| **Text Secondary** | `#6B5D4F` | `(0.42f, 0.36f, 0.31f)` | 부제, 설명 |
| **Text Muted** | `#9B8E7F` | `(0.61f, 0.56f, 0.50f)` | 상태 텍스트, placeholder |
| **Disabled BG** | `#C8C0B4` | `(0.78f, 0.75f, 0.71f)` | 비활성 버튼 배경 |
| **Log BG** | `#0D0D14` | `(0.05f, 0.05f, 0.08f, 0.80f)` | 로그 영역 배경 |

### Button ColorBlock 패턴

```csharp
// Primary (단청 Red)
colors.normalColor     = new Color(0.77f, 0.28f, 0.24f);       // #C4483C
colors.highlightedColor = new Color(0.66f, 0.23f, 0.19f);      // #A83A30
colors.pressedColor    = new Color(0.55f, 0.18f, 0.15f);       // 더 어둡게
colors.disabledColor   = new Color(0.78f, 0.75f, 0.71f, 0.60f); // #C8C0B4

// Secondary (Teal)
colors.normalColor     = new Color(0.24f, 0.48f, 0.50f);       // #3D7B80
colors.highlightedColor = new Color(0.18f, 0.39f, 0.40f);      // #2E6266
colors.pressedColor    = new Color(0.14f, 0.30f, 0.32f);
colors.disabledColor   = new Color(0.78f, 0.75f, 0.71f, 0.60f);

// Sprite Button (결투장/옷장 — Image 기반)
colors.normalColor     = Color.white;
colors.highlightedColor = new Color(0.90f, 0.95f, 1.00f);
colors.pressedColor    = new Color(0.75f, 0.75f, 0.75f);
colors.disabledColor   = new Color(0.70f, 0.70f, 0.70f, 0.80f);
```

---

## Typography

| 역할 | 폰트 | 크기 (pt) | 스타일 | 색상 | 정렬 |
|------|------|----------|--------|------|------|
| 화면 타이틀 | TMP Default (굵게) | 42~48 | Bold | Text Primary | Center |
| 버튼 라벨 (대) | TMP Default (굵게) | 28~32 | Bold | White | Center |
| 버튼 라벨 (소) | TMP Default (굵게) | 20~24 | Bold | White | Center |
| 본문 텍스트 | TMP Default | 20~22 | Normal | Text Primary | Left |
| 부제/설명 | TMP Default | 16~18 | Normal | Text Secondary | Center |
| 상태 메시지 | TMP Default | 18 | Normal | Text Muted | Left |
| 플레이어 슬롯 | TMP Default | 22 | Normal | White | Left |
| 로비 코드 | TMP Default (굵게) | 42~48 | Bold | Gold | Center |
| 입력 placeholder | TMP Default | 20 | Italic | Text Muted | Left |
| 로그 텍스트 | TMP Default | 14 | Normal | `(0.7f, 0.8f, 0.7f)` | TopLeft |

---

## Screen 1 — Main (메인 화면)

### 레이아웃

```
┌─────────────────────────────────────────────────────────────┐
│  1920 × 1080                                                │
│                                                             │
│  ┌──────────────────┐                                       │
│  │ 닉네임 바 (sprite)│  ← anchorMin(0,1) anchorMax(0,1)     │
│  │ 340×85           │     pivot(0,1) pos(30,-25)            │
│  │  "Player_a3f2" ✏️│                                       │
│  └──────────────────┘                                       │
│                                                             │
│                                          ┌───────────────┐  │
│                                          │               │  │
│                                          │  캐릭터 프리뷰  │  │
│  ┌──────────────────┐                    │  (향후)        │  │
│  │ 결투장 (sprite)   │  ← pos(30,185)    │  dashed border │  │
│  │ 340×183          │     anchor(0,0)    │  40% opacity   │  │
│  │ "결투장" 🏛️      │     pivot(0,0)     │               │  │
│  └──────────────────┘                    │               │  │
│  ┌──────────────────┐                    │               │  │
│  │ 옷장 (sprite)     │  ← pos(30,15)     └───────────────┘  │
│  │ 340×160          │     disabled 시                       │
│  │ "옷장" 👕        │     opacity 0.5                       │
│  └──────────────────┘                                       │
│  ┌────────┐┌────────┐                                       │
│  │ Solo   ││ ⚙️설정  │  ← 코드 생성 버튼 (155×80 each)      │
│  │disabled││        │     Solo: pos(30,-160) disabled       │
│  └────────┘└────────┘     설정: pos(195,-160)               │
│  "준비 완료"            ← 상태 텍스트 pos(45,10)              │
└─────────────────────────────────────────────────────────────┘
```

### 각 요소 상세

**닉네임 바** — 기존 `lobby_nickname` 스프라이트 사용
```
Anchor: Min(0,1) Max(0,1) Pivot(0,1)
Position: (30, -25)
Size: 340 × 85
내부 TMP_InputField:
  - anchorMin(0,0) anchorMax(1,1)
  - offsetMin(15,0) offsetMax(-55,0)
  - fontSize: 22, alignment: MidlineLeft
  - color: Text Primary (#2C1810)
  - characterLimit: 8
  - contentType: Standard (한글 허용)
  - placeholder: "닉네임" (Text Muted, Italic)
```

**결투장 버튼** — 기존 `lobby_arena` 스프라이트 사용
```
Anchor: Min(0,0) Max(0,0) Pivot(0,0)
Position: (30, 185)
Size: 340 × 183
Image: lobby_arena sprite, preserveAspect=true
Button: Sprite 기반 ColorBlock
onClick → LobbyViewState.ModeSelect
```

**옷장 버튼** — 기존 `lobby_closet` 스프라이트 사용
```
Anchor: Min(0,0) Max(0,0) Pivot(0,0)
Position: (30, 15)
Size: 340 × 160
Image: lobby_closet sprite, preserveAspect=true
Button: interactable = false (Phase B 완료 전)
  disabledColor: (0.70, 0.70, 0.70, 0.80)
완료 후 → LobbyViewState.Closet
```

**Solo 버튼** — 코드 생성
```
Anchor: Min(0,0) Max(0,0) Pivot(0,0)
Position: (30, -160)   ← 옷장 아래 여백
Size: 155 × 80
Image: color = Cream (#F5EDE0)
  outline 효과: 별도 child Image or UI.Outline, color = Wood Mid, 2px
  border radius: RoundedRect 없으므로 9-slice sprite 또는 Sprite 에셋 필요
Button: interactable = false
Label: "Solo", fontSize 20, Bold, color = Text Primary
Badge: 우상단에 "Coming Soon" 텍스트
  - child GameObject, anchorMin(1,1), pivot(1,1)
  - fontSize 11, Bold, color = White
  - bg Image: Gold (#D4A54A), padding(3,6)
```

**설정 버튼** — 코드 생성
```
Anchor: Min(0,0) Max(0,0) Pivot(0,0)
Position: (195, -160)  ← Solo 우측
Size: 155 × 80
Image: color = Cream (#F5EDE0), 동일 테두리
Button: interactable = true
Label: "⚙️" (TextMeshPro emoji 또는 기어 아이콘 sprite), fontSize 28
onClick → LobbyViewState.Settings
```

**상태 텍스트**
```
Anchor: Min(0,0) Max(0,0) Pivot(0,0)
Position: (45, 10)
Size: 400 × 30
fontSize: 18, color: Text Muted (#9B8E7F)
alignment: BottomLeft
텍스트: "준비 완료" / "초기화 중..." / 에러 메시지
```

**캐릭터 프리뷰 영역** (향후, 현재는 빈 영역)
```
Anchor: Min(0.55, 0.15) Max(0.95, 0.85)
Image: 없음 (또는 dashed border를 LineRenderer/UI.Outline으로)
현재는 비표시 — Closet 구현 후 코스메틱 적용 캐릭터 표시
```

---

## Screen 2 — Mode Select (모드 선택 모달)

### 레이아웃

```
┌─────────────────────────────────────────────┐
│  Dim Overlay (전체화면, rgba 0.12,0.08,0.05,0.55) │
│                                             │
│     ┌─────────────────────────────┐         │
│     │       "결투장"               │  600×500 │
│     │       (title, 42pt Bold)    │  중앙정렬 │
│     │                             │         │
│     │  ┌───────────────────────┐  │         │
│     │  │ ⚔️   1 vs 1           │  │  Red    │
│     │  │      "친구와 1:1 대결"  │  │  #C4483C│
│     │  │      height: 90px     │  │         │
│     │  └───────────────────────┘  │         │
│     │                             │         │
│     │  ┌───────────────────────┐  │         │
│     │  │ 👥   Multi (3~4인)    │  │ Disabled│
│     │  │      "다인전 — 준비 중" │  │ #C8C0B4│
│     │  │      opacity: 0.5     │  │         │
│     │  └───────────────────────┘  │         │
│     │                             │         │
│     │      "← 뒤로"              │         │
│     │      (16pt, 밑줄, Muted)   │         │
│     └─────────────────────────────┘         │
└─────────────────────────────────────────────┘
```

### 요소 상세

**Dim 배경**
```
GameObject: Dim
RectTransform: stretch 전체 (anchorMin 0,0 ~ anchorMax 1,1)
Image: color = (0.12f, 0.08f, 0.05f, 0.55f)
raycastTarget = true (뒤 클릭 차단)
Button 컴포넌트 추가: onClick → 뒤로 (Main)
```

**패널**
```
RectTransform: 중앙 정렬
  anchorMin(0.5, 0.5), anchorMax(0.5, 0.5), pivot(0.5, 0.5)
  sizeDelta: (600, 500)
Image: color = Panel BG (0.96f, 0.93f, 0.88f, 0.96f)
  (둥근 모서리: Unity 기본 Image에는 없음 → 9-slice rounded sprite 사용)
```

**타이틀**
```
"결투장", fontSize: 42, Bold
color: Text Primary
anchoredPosition: (0, 200)
sizeDelta: (500, 60)
alignment: Center
```

**1v1 버튼**
```
anchoredPosition: (0, 80)
sizeDelta: (500, 90)
Image: color = 단청 Red (#C4483C → 0.77f, 0.28f, 0.24f)
  (9-slice rounded sprite, radius ~12px)
Button: ColorBlock Red 패턴
내부 레이아웃 (HorizontalLayoutGroup or 수동 배치):
  좌측:
    Label1: "1 vs 1", fontSize 32, Bold, White
    Label2: "친구와 1:1 대결", fontSize 14, White alpha 0.8
  우측:
    Icon: "⚔️", fontSize 36
onClick → LobbyViewState.Room (maxPlayers=2 고정, LobbyManager.MaxPlayers setter 미호출)
```

**Multi 버튼 (disabled)**
```
anchoredPosition: (0, -30)
sizeDelta: (500, 90)
Image: color = Disabled BG (#C8C0B4)
Button: interactable = false
CanvasGroup: alpha = 0.5
내부:
  Label1: "Multi (3~4인)", fontSize 32, Bold, Text Primary
  Label2: "다인전 — 준비 중", fontSize 14, Text Muted
  Icon: "👥", fontSize 36
```

**뒤로 링크**
```
anchoredPosition: (0, -180)
sizeDelta: (200, 30)
TextMeshProUGUI: "← 뒤로", fontSize 16, Text Muted
  fontStyle: Underline
Button: Transition.None, onClick → Main
```

---

## Screen 3 — Room (대기실 모달)

### 레이아웃

```
┌──────────────────────────────────────────────────┐
│  Dim Overlay                                      │
│                                                   │
│  ┌────────────────────────────────────────┐       │
│  │ "1 vs 1 대결"  (14pt, Muted, 좌상단)    │ 700×780│
│  │                                        │       │
│  │            "대기실"                     │       │
│  │         (42pt, Bold, Center)           │       │
│  │                                        │       │
│  │    "로비 코드 (클릭하여 복사)" (18pt)     │       │
│  │         A B C 1 2 3                    │       │
│  │    (42pt, Bold, Gold, letterSpacing)   │       │
│  │                                        │       │
│  │    "플레이어" (22pt, Muted)             │       │
│  │    ┌──────────────────────────┐        │       │
│  │    │👑 Player_a3f2 [호스트](나)│ Gold   │       │
│  │    └──────────────────────────┘        │       │
│  │    ┌──────────────────────────┐        │       │
│  │    │   대기 중...              │ Empty  │       │
│  │    └──────────────────────────┘        │       │
│  │                                        │       │
│  │    ┌──────────────────────────┐        │       │
│  │    │      로비 생성            │ Teal   │       │
│  │    └──────────────────────────┘        │       │
│  │            — 또는 —                    │       │
│  │    ┌──────────────────┐┌──────┐        │       │
│  │    │ 코드 입력...      ││ 참가 │ Green  │       │
│  │    └──────────────────┘└──────┘        │       │
│  │                                        │       │
│  │    ┌────────────┐┌────────────┐        │       │
│  │    │  게임 시작   ││   나가기   │        │       │
│  │    │  (Red)      ││  (Mid)    │        │       │
│  │    └────────────┘└────────────┘        │       │
│  │                                        │       │
│  │    "로비 참가 (호스트) — 코드: ABC123"   │       │
│  │                                        │       │
│  │    ┌──────────────────────────┐        │       │
│  │    │ [HH:mm:ss] 로그 메시지    │ Log BG │       │
│  │    │ 스크롤 영역 130px         │        │       │
│  │    └──────────────────────────┘        │       │
│  │                                        │       │
│  │        "← 모드 선택으로"                │       │
│  └────────────────────────────────────────┘       │
└──────────────────────────────────────────────────┘
```

### 요소 상세

**패널**
```
sizeDelta: (700, 780)
Image: Panel BG, 동일 rounded sprite
```

**모드 라벨**
```
anchoredPosition: (-300, 350)  (좌상단)
"1 vs 1 대결" / "3인 Multi" / "4인 Multi"
fontSize: 14, color: Text Muted, alignment: Left
```

**타이틀**
```
"대기실", anchoredPosition: (0, 310)
fontSize: 42, Bold, Text Primary, Center
```

**로비 코드**
```
라벨: "로비 코드 (클릭하여 복사)"
  anchoredPosition: (0, 265), fontSize: 18, Text Muted
코드: anchoredPosition: (0, 220)
  fontSize: 42, Bold, color: Gold (#D4A54A)
  characterSpacing: 15 (TMP)
  기본값: "------"
  Button 컴포넌트: onClick → GUIUtility.systemCopyBuffer
```

**플레이어 슬롯 목록**
```
컨테이너: VerticalLayoutGroup
  anchoredPosition: (0, 100)
  sizeDelta: (500, 슬롯수×63)
  spacing: 8, childForceExpandWidth: true
  padding: (10, 10, 5, 5)

슬롯 1개:
  sizeDelta: (480, 55), LayoutElement.preferredHeight: 55
  Image: 슬롯 색상 (Host/Me/Other/Empty)
  Label: padding-left 20px
    fontSize: 22, White, MidlineLeft
    호스트: "👑 {name} [호스트]"
    나: "{name} (나)"
    상대: "{name}"
    빈: "대기 중..." (Text Muted)

슬롯 색상:
  Host+Me: Slot Host (0.83f, 0.65f, 0.29f, 0.80f)
  Me only: Slot Me (0.29f, 0.55f, 0.36f, 0.80f)
  Other:   Slot Other (0.35f, 0.48f, 0.61f, 0.80f)
  Empty:   Slot Empty (0.91f, 0.88f, 0.83f, 0.50f)
```

**로비 생성 버튼**
```
anchoredPosition: (0, 0)
sizeDelta: (400, 55)
Image: Teal (0.24f, 0.48f, 0.50f)
Label: "로비 생성", fontSize 24, White, Center
onClick → coordinator.CreateLobbyAsync()
```

**"또는" 텍스트**
```
"— 또는 —", anchoredPosition: (0, -40)
fontSize: 16, Text Muted, Center
```

**코드 입력 + 참가**
```
InputField:
  anchoredPosition: (-70, -85)
  sizeDelta: (260, 55)
  bg Image: (0.15f, 0.15f, 0.20f, 0.90f)  ← 기존 어두운 스타일 유지
  TMP: fontSize 22, White, MidlineLeft
  placeholder: "코드 입력..." (Text Muted, Italic, 20pt)
  characterLimit: 8, Alphanumeric

참가 버튼:
  anchoredPosition: (145, -85)
  sizeDelta: (130, 55)
  Image: Success Green (0.29f, 0.55f, 0.36f)
  Label: "참가", fontSize 22, White
  onClick → coordinator.JoinGameAsync(code.ToUpper().Trim())
```

**시작 / 나가기 버튼**
```
시작:
  anchoredPosition: (-100, -165)
  sizeDelta: (190, 55)
  Image: 단청 Red (0.77f, 0.28f, 0.24f)
  Label: "게임 시작", fontSize 22, White
  호스트만 표시 (SetActive)
  인원 미충족 시 interactable = false
  onClick → coordinator.StartMatchAsHostAsync()

나가기:
  anchoredPosition: (100, -165)
  sizeDelta: (190, 55)
  Image: Wood Mid (0.55f, 0.45f, 0.33f)  ← 기존 (0.6f, 0.2f, 0.2f)에서 변경
  Label: "나가기", fontSize 22, White
  onClick → coordinator.LeaveAsync()
```

**로비 참가 후 UI 변화 (ApplyLobbyMembershipUI)**
```
로비 참가 시:
  - 로비 생성 버튼: SetActive(false)
  - "또는" 텍스트: SetActive(false)
  - 코드 입력 필드: SetActive(false)
  - 참가 버튼: SetActive(false)
  → 슬롯 + 시작/나가기만 표시

로비 미참가 시:
  - 위 4개 전부 SetActive(true)
  - 로비 코드: "------"
```

**상태 텍스트**
```
anchoredPosition: (0, -230)
sizeDelta: (600, 30)
fontSize: 18, Text Muted, Center
```

**로그 스크롤 영역**
```
anchoredPosition: (0, -310)
sizeDelta: (600, 130)
bg Image: Log BG (0.05f, 0.05f, 0.08f, 0.80f)
ScrollRect: vertical only, Clamped
  Viewport: Mask, padding (8,5)
  Content: ContentSizeFitter.verticalFit = PreferredSize
    TextMeshProUGUI: fontSize 14
      color: (0.70f, 0.80f, 0.70f)
      alignment: TopLeft
      max 30 lines
```

**뒤로 링크**
```
anchoredPosition: (0, -390)
"← 모드 선택으로", fontSize 16, Text Muted, Underline
onClick → LeaveAsync() + ModeSelect 전환
```

---

## Screen 4 — Settings (설정 오버레이)

### 레이아웃

```
┌────────────────────────────────────┐
│  Dim Overlay                       │
│                                    │
│    ┌────────────────────────┐      │
│    │       "설정"            │ 480× │
│    │    (36pt, Bold)        │ 340  │
│    │                        │      │
│    │  🎵 BGM 볼륨      75%  │      │
│    │  ████████████░░░░ ●    │      │
│    │                        │      │
│    │  🔊 효과음 볼륨   100% │      │
│    │  ██████████████████ ●  │      │
│    │                        │      │
│    │       [ 닫기 ]          │      │
│    └────────────────────────┘      │
└────────────────────────────────────┘
```

### 요소 상세

**패널**
```
sizeDelta: (480, 340)
중앙 정렬
```

**타이틀**
```
"설정", anchoredPosition: (0, 130)
fontSize: 36, Bold, Text Primary
```

**슬라이더 (BGM / SFX 동일 패턴)**
```
컨테이너 (각 슬라이더):
  sizeDelta: (380, 60)
  
  라벨 행 (HorizontalLayoutGroup or 수동):
    좌: "🎵 BGM 볼륨" / "🔊 효과음 볼륨"
      fontSize: 16, fontWeight: 500, Text Primary
    우: "75%" / "100%"
      fontSize: 16, Text Muted

  Unity UI Slider:
    sizeDelta: (380, 20)
    
    Background (track):
      Image: color = Input Border (0.83f, 0.78f, 0.72f)
      height: 8px
      
    Fill Area → Fill:
      Image: color = Teal (0.24f, 0.48f, 0.50f)
      
    Handle:
      sizeDelta: (20, 20)
      Image: White, circular sprite
      Outline or border: Teal, 2px

    value: 0~1
    wholeNumbers: false
    
    BGM 기본: 1.0f (master multiplier — base gain은 GameAudioManager 내부)
    SFX 기본: 1.0f

onChange:
  BGM → GameAudioManager.Instance.SetBGMVolume(val)  // val × _bgmBase
         PlayerPrefs.SetFloat("bgm_volume", val)
  SFX → GameAudioManager.Instance.SetSFXVolume(val)  // val × _sfx/_ui/_env/_fan/_clock base
         PlayerPrefs.SetFloat("sfx_volume", val)
```

**닫기 버튼**
```
anchoredPosition: (0, -120)
sizeDelta: (160, 45)
Image: Wood Mid (0.55f, 0.45f, 0.33f)
Label: "닫기", fontSize 20, White
onClick → Main 복귀
```

**GameAudioManager 추가 메서드 (master multiplier × base gain 패턴)**
```csharp
// base gain 상수 (기존 값 보존)
const float _bgmBase = 0.35f, _sfxBase = 0.7f, _uiBase = 0.5f;
const float _envBase = 0.6f, _fanBase = 0.15f, _clockBase = 0.5f;

public void SetBGMVolume(float master)
{
    _bgmSource.volume = _bgmBase * master;
}

public void SetSFXVolume(float master)
{
    _sfxSource.volume = _sfxBase * master;
    _uiSource.volume = _uiBase * master;
    _envSource.volume = _envBase * master;
    _fanLoopSource.volume = _fanBase * master;
    _clockSource.volume = _clockBase * master;
}

// Awake()에 추가:
float bgmMaster = PlayerPrefs.GetFloat("bgm_volume", 1.0f);
float sfxMaster = PlayerPrefs.GetFloat("sfx_volume", 1.0f);
SetBGMVolume(bgmMaster);
SetSFXVolume(sfxMaster);
// master=1.0 → volume = base × 1.0 = 기존 gain 그대로 보존
```

---

## Screen 5 — Closet (옷장 오버레이) — 최소 Placeholder

> **CU2 보류** — 좌우분할 프리뷰, 그리드 레이아웃은 CU2 확정 전 미구현.
> 현재는 탭 + 세로 리스트 + 닫기만 구현합니다.

### 레이아웃

```
┌────────────────────────────────────┐
│  Dim Overlay                       │
│                                    │
│    ┌────────────────────────┐      │
│    │      "옷장" (36pt Bold) │ 500× │
│    │                        │ 550  │
│    │  [머리][상의][등][하의][꼬리] │      │
│    │                        │      │
│    │  ┌──────────────────┐  │      │
│    │  │ hat_01 [장착]     │  │      │
│    │  │ cape_01           │  │      │
│    │  │ ...               │  │      │
│    │  │ (VerticalLayout)  │  │      │
│    │  └──────────────────┘  │      │
│    │                        │      │
│    │       [ 닫기 ]          │      │
│    └────────────────────────┘      │
└────────────────────────────────────┘
```

### 요소 상세

**패널**
```
sizeDelta: (500, 550)
중앙 정렬
Image: Panel BG
```

**타이틀**
```
"옷장", anchoredPosition: (0, 230)
fontSize: 36, Bold, Text Primary
```

**파트 탭 (HorizontalLayoutGroup)**
```
anchoredPosition: (0, 185)
spacing: 6

탭 1개:
  sizeDelta: (auto, 32) — ContentSizeFitter.horizontalFit
  padding: (10, 10, 4, 4)
  
  활성 탭:
    Image: 단청 Red (0.77f, 0.28f, 0.24f)
    Label: fontSize 14, Bold, White
  
  비활성 탭:
    Image: Cream Dim (0.93f, 0.89f, 0.83f)
    Label: fontSize 14, Text Muted
  
  탭 이름: "머리" / "상의" / "등" / "하의" / "꼬리"
  onClick → CosmeticProfileService.Registry.GetByPart(part) → 리스트 갱신
```

**아이템 리스트 (VerticalLayoutGroup + ScrollRect)**
```
ScrollRect: vertical
anchoredPosition: (0, -20)
sizeDelta: (440, 320)

아이템 행:
  sizeDelta: (420, 45), LayoutElement.preferredHeight: 45
  Image bg: Cream Dim
  
  Label (좌): CosmeticItemSO.DisplayName
    fontSize: 18, Text Primary, MidlineLeft, padding-left 15

  토글 버튼 (우):
    미장착: "장착" (fontSize 14, Teal)
    장착 중: "해제" (fontSize 14, 단청 Red)
    onClick:
      미장착 → Presenter.EquipCosmetic(item)
      장착 중 → Presenter.UnequipCosmetic(item.Part)
```

**닫기 버튼**
```
anchoredPosition: (0, -230)
sizeDelta: (160, 45)
Image: Wood Mid (0.55f, 0.45f, 0.33f)
Label: "닫기", fontSize 20, White
onClick → Save + LobbyViewState.Main
  로비 참가 중이면 LobbyManager.SetPlayerCosmeticDataAsync(dto) 1회 호출
```

---

## PlayerPrefs Keys

| 키 | 타입 | 기본값 | 저장 시점 |
|----|------|--------|----------|
| `"player_nickname"` | string | `""` (빈=미입력, `Player_{shortId}` 폴백은 저장 안 함) | 유저 직접 입력 시 (onEndEdit) |
| `"bgm_volume"` | float | `1.0f` (master multiplier) | 슬라이더 변경 시 |
| `"sfx_volume"` | float | `1.0f` (master multiplier) | 슬라이더 변경 시 |
| `"cosmetic_equip_v1"` | string (JSON) | `""` (빈=미장착) | Closet 닫기 시. 형식: `{"v":1,"head":"","top":"","back":"","bottom":"","tail":""}` |

---

## Navigation State 전환 요약

```
enum LobbyViewState { Main, ModeSelect, Room, Settings, Closet }

Main       →  ModeSelect  : 결투장 버튼
ModeSelect →  Main        : ← 뒤로 / dim 클릭
ModeSelect →  Room        : 1v1 버튼 (maxPlayers=2 고정)
Room       →  ModeSelect  : ← 뒤로 (LeaveAsync 호출)
Room       →  (GameScene) : StartMatchAsHostAsync 성공
Main       ↔  Settings    : ⚙️ / 닫기 (overlay)
Main       ↔  Closet      : 옷장 / 닫기 (overlay)

Settings, Closet은 Main 위에 overlay로 표시 (Main은 뒤에 유지)
ModeSelect, Room은 Main을 가림 (dim 배경)
```
