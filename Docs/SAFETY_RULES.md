# Safety Rules — Absolute Zero

> Living document. Update immediately when errors are found or lessons learned.
> Last updated: 2026-08-20 (Phase 7 + recompile rule)

---

## Rule Verification System

> Safety rules **constrain** Claude's actions. Incorrect rules can block valid work,
> so **only rules with verified code evidence are binding.**

### Verification States

| State | Meaning | Claude Behavior |
|-------|---------|-----------------|
| `✅ Verified` | Code evidence confirmed | **Must comply** — stop immediately on violation |
| `⏳ Unverified` | Needs further confirmation | **Caution level** — reference but may override if needed, mention to user |
| `❌ Disproven` | Confirmed mismatch with code | **Ignore** — delete or revise on next review |

### Rule Lifecycle

```
New rule (⏳ Unverified)
    ↓ Code evidence confirmed (file:line specified)
Verified (✅ Verified)
    ↓ Code changes invalidate rule
Re-verification needed (⏳ Unverified)
    ↓ Confirmed mismatch with code
Retired (❌ Disproven) → Delete or revise and re-register
```

### Verification Procedure (when adding/reviewing rules)

1. **Confirm code evidence:** Verify the code pattern actually exists via `Grep`/`Read`
2. **Record evidence:** Specify confirmed file:line numbers in the rule
3. **Search for counterexamples:** Check if code violating the rule already exists
4. **Assess impact scope:** Determine if the restricted behavior is actually dangerous
5. **Assign state:** Pass steps 1-4 → `✅ Verified`, insufficient → `⏳ Unverified`

### Periodic Review Triggers

- **Before starting code modification:** Verify related rule evidence is still valid
- **On new session start:** Re-verify rules related to changed files

---

## NEVER DO (Critical)

### RULE-001: ScriptableObject runtime modification forbidden
- **Status:** ✅ Verified (Unity engine behavior)
- **Category:** Architecture
- **Reason:** ScriptableObject field changes persist in Editor. Runtime modification corrupts source asset data permanently
- **Correct pattern:** Read from SO at initialization, copy values to runtime plain class or struct. Never write back to SO fields at runtime
- **Evidence:** Unity engine documented behavior — SO assets are shared instances in Editor, changes write through to disk
- **Ported from:** AbyssNode RULE-001 (universal Unity constraint)
- **Added:** 2026-07-13

### RULE-003: recompile_scripts MCP call forbidden
- **Status:** ✅ Verified
- **Category:** Tooling
- **Reason:** Breaks MCP WebSocket connection, requires manual Unity Editor restart to reconnect
- **Correct pattern:** Rely on Unity Editor auto-recompile on file save. Never call `mcp__unityMCP__recompile_scripts`
- **Evidence:** `.claude/settings.json` deny list. AbyssNode에서 확인된 동작
- **Ported from:** AbyssNode RULE-002 (universal MCP constraint)
- **Added:** 2026-07-13

### RULE-004: MCP HTTP direct connection required on Windows (not stdio)
- **Status:** ✅ Verified
- **Category:** Tooling
- **Reason:** stdio transport (uvx Python server) fails to discover Unity Editor instances on Windows — `No Unity Editor instances found` error. HTTP direct connection works reliably
- **Correct pattern:** Use `"type": "http", "url": "http://127.0.0.1:8080/mcp"` in `.mcp.json`
- **Evidence:** `.mcp.json` HTTP config. AbyssNode에서 3회 이상 stdio 실패 후 HTTP로 전환하여 해결
- **Ported from:** AbyssNode RULE-034 (universal Windows + Unity MCP constraint)
- **Added:** 2026-07-13

### RULE-002: .meta file path renaming requires planned migration
- **Status:** ✅ Verified (Unity engine behavior)
- **Category:** Git/Unity
- **Reason:** Unity `.meta` files bind GUIDs to paths. Renaming folders/files without preserving `.meta` GUIDs breaks all serialized references in scenes, prefabs, and ScriptableObjects
- **Correct pattern:** Use Unity Editor to rename (preserves GUID), or plan batch rename that preserves `.meta` file pairings. Never rename via filesystem directly
- **Evidence:** Unity engine `.meta` binding architecture (official docs)
- **Ported from:** AbyssNode RULE-003 (universal Unity constraint)
- **Added:** 2026-07-13

### RULE-005: Namespace final segment must not collide with imported type names
- **Status:** ✅ Verified
- **Category:** Code
- **Reason:** `AbsoluteZero.UI.Lobby` resolved to the namespace instead of `Unity.Services.Lobbies.Models.Lobby`, causing CS0118 compilation error
- **Correct pattern:** Suffix namespace with category (e.g., `LobbyUI`, `PlayerVisuals`). Check if final segment matches any imported type name before creating
- **Evidence:** `Assets/Scripts/UI/Lobby/AZLobbyUI.cs` — originally used `AbsoluteZero.UI.Lobby`, had to rename to `AbsoluteZero.UI.LobbyUI`
- **Added:** 2026-07-13

---

## ALWAYS DO

### RULE-010: Event handler cleanup required on coroutine/callback exit
- **Status:** ✅ Verified (universal Unity pattern)
- **Category:** Code
- **Reason:** Event handler (`+=`) without corresponding `-=` on exit causes duplicate calls and NullReferenceException on destroyed objects
- **Correct pattern:** Every `+= handler` must have a corresponding `-= handler` on coroutine exit, OnDisable, or OnDestroy
- **Evidence:** Unity engine lifecycle — destroyed MonoBehaviours with lingering event subscriptions cause MissingReferenceException
- **Ported from:** AbyssNode RULE-014 (universal Unity constraint)
- **Added:** 2026-07-13

### RULE-012: Design spec verification required before gameplay code changes
- **Status:** ✅ Verified
- **Category:** Workflow
- **Reason:** Item SO field values (damage, heal, drop weight, minigame settings) and system behavior (turn flow, temperature, combat) drifted from design spec without detection. Discovered 3 SO mismatches in 2026-07-18 audit
- **Correct pattern:** Before modifying gameplay code or SO assets:
  1. Read `Docs/GAME_DESIGN.md` item tables and system rules
  2. Verify target values match design spec
  3. If design and code diverge, ask user which is correct before changing either
- **Evidence:** Soda had RequiresMiniGame=True (design: False), TarotCard had RequiresMiniGame=True (design: False), Screwdriver had MiniGameTimeLimit=5 (design: 7)
- **Added:** 2026-07-18

### RULE-013: Item effects must not directly write NetworkVariable/NetworkList
- **Status:** ✅ Verified
- **Category:** Architecture
- **Reason:** 7개 아이템 카테고리가 각각 NV에 직접 write하면 mutation 지점이 분산되어 desync 원인 추적 불가. Phase 5C에서 모든 아이템이 Temperature.Value, FanSpeed.Value 등을 직접 쓰던 패턴을 발견하고 전면 전환
- **Correct pattern:** `ItemDataSO.ComputeEffect()` → `ItemEffectOutcome` 반환 → `ItemEffectApplicator.Apply()`에서만 NV write
- **Evidence:** `Assets/Scripts/Core/Item/ItemEffectApplicator.cs` — 유일한 item effect NV write 지점. 7개 `*ItemDataSO.ComputeEffect()`는 순수 계산만 수행
- **Added:** 2026-08-20

### RULE-014: FindObjectsByType for player iteration forbidden — use PlayerRegistry
- **Status:** ✅ Verified
- **Category:** Code
- **Reason:** `FindObjectsByType<AZPlayerVisual>()`은 despawn 중이거나 파괴된 객체를 반환할 수 있고 match seat 순서를 보장하지 않음
- **Correct pattern:** `MatchCompositionRoot.Instance.Registry.Players`로 순회. Registry는 seat index별 정렬 보장
- **Evidence:** `Assets/Scripts/Core/Turn/TurnManager.cs` `ReviveVisualsClientRpc` — FindObjectsByType에서 Registry 경로로 수정 완료
- **Added:** 2026-08-20

### RULE-015: _p1/_p2 paired field naming forbidden — use indexed collection
- **Status:** ✅ Verified
- **Category:** Architecture
- **Reason:** `_p1`/`_p2` paired fields는 `1 - index` 반전 패턴을 강제하고 3인 이상 확장 시 전면 재작성 필요. Phase 5D에서 TurnManager의 40+ 참조를 `_players[]`로 전환
- **Correct pattern:** `PlayerState[] _players`, `float[] _tempsAtTurnStart` 등 collection + index 접근. 대칭 연산은 for loop 사용
- **Evidence:** `Assets/Scripts/Core/Turn/TurnManager.cs` — `_players[0]`/`_players[1]` + loop. `Assets/Scripts/Core/Combat/TwoPlayerCombatMapper.cs` — 기존 P1/P2 wire를 indexed API 뒤에 격리
- **Added:** 2026-08-20

### RULE-011: Null check required in entity iteration loops
- **Status:** ✅ Verified (universal Unity pattern)
- **Category:** Code
- **Reason:** Entities (enemies, players, network objects) can be destroyed mid-loop. `foreach` over entity lists without null check causes NullReferenceException
- **Correct pattern:** `foreach (var entity in entities) { if (entity == null) continue; ... }`
- **Evidence:** Unity engine — `Destroy()` sets reference to null but doesn't remove from List. Network objects can despawn mid-frame
- **Ported from:** AbyssNode RULE-012 (universal Unity constraint)
- **Added:** 2026-07-13

---

## WARNINGS

### RULE-016: Shared enums in Common namespace — interface/service 파일에서 using 필수
- **Status:** ✅ Verified
- **Category:** Code
- **Reason:** `TurnPhase`, `EnvironmentType` 등 공용 enum은 `AbsoluteZero.Core.Common`에 있음. `AbsoluteZero.Core.Turn` 등 다른 네임스페이스의 interface/service 파일에서 `using AbsoluteZero.Core.Common;`을 빠뜨리면 CS0246 발생
- **Correct pattern:** enum을 사용하는 모든 파일에 해당 네임스페이스 using 명시 확인. 같은 최상위 네임스페이스라도 하위 segment가 다르면 자동 resolve 안 됨
- **Evidence:** `Assets/Scripts/Core/Turn/ITurnContext.cs` — `TurnPhase` 사용 시 `using AbsoluteZero.Core.Common;` 누락으로 CS0246 발생 (2026-08-20 수정)
- **Added:** 2026-08-20

### RULE-017: ObjectPool release 시 event/coroutine/state 정리 필수
- **Status:** ✅ Verified
- **Category:** Code
- **Reason:** pool에서 get한 오브젝트에 이전 사용의 event subscription, coroutine, Animator state가 남아 있으면 두 번째 사용에서 중복 이벤트·잘못된 애니메이션·stale target 참조 발생
- **Correct pattern:** `actionOnRelease`에서 `StopAllCoroutines()`, event unsubscribe, target reference null 처리. `actionOnGet`에서 초기 상태 확인
- **Evidence:** `Assets/Scripts/Core/Emote/EmoteBubble.cs` — release 시 StopAllCoroutines + anchor null. `Assets/Scripts/Core/Combat/CombatVFXManager.cs` — release 시 ParticleSystem.Stop(StopEmittingAndClear)
- **Added:** 2026-08-20

### RULE-020: WaitForSeconds must be cached as static readonly
- **Status:** ✅ Verified (universal Unity performance)
- **Category:** Performance
- **Reason:** `new WaitForSeconds()` per coroutine iteration creates GC pressure. Cached instances eliminate allocation
- **Correct pattern:**
```csharp
private static readonly WaitForSeconds Wait1Sec = new WaitForSeconds(1f);
private static readonly WaitForSeconds Wait01Sec = new WaitForSeconds(0.1f);
```
- **Evidence:** Unity performance best practices (official docs). Coroutines in networked game run frequently
- **Ported from:** AbyssNode RULE-005 (universal Unity constraint)
- **Added:** 2026-07-13

### RULE-018: Core→UI 직접 참조 금지 — static event로 디커플링
- **Status:** ✅ Verified (asmdef 컴파일러 강제)
- **Category:** Architecture
- **Reason:** Core(Domain) 어셈블리에서 UI(Presentation) 타입을 직접 참조하면 역방향 의존성 발생. asmdef 분리 시 컴파일 실패
- **Correct pattern:** Core에서 static event 정의 → UI에서 구독. 참조 방향: UI→Core만 허용
- **Impact:** Core asmdef이 UI asmdef 참조 없이 컴파일 가능해야 함. 역참조 시 순환 의존성으로 빌드 불가
- **검증:** `grep -r 'using AbsoluteZero.UI' Assets/Scripts/Core/` 결과 0건
- **Added:** 2026-08-20

### RULE-019: RuntimeInitializeOnLoadMethod(SubsystemRegistration)에서 static 필드 반드시 초기화
- **Status:** ✅ Verified (Unity domain reload 요구사항)
- **Category:** Lifecycle
- **Reason:** Unity domain reload 시 static 필드는 자동 초기화되지 않음. SubsystemRegistration 콜백에서 명시적 null/초기값 할당 필수
- **Correct pattern:** `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void Init() { _pool = null; }`
- **Impact:** 미초기화 시 Editor Play Mode 재진입 시 이전 세션의 stale 참조로 NullReferenceException 또는 MissingReferenceException 발생
- **Added:** 2026-08-20

### RULE-020b: 에러 확인 전 반드시 refresh_unity + compile 후 콘솔 확인
- **Status:** ✅ Verified (user feedback)
- **Category:** Workflow
- **Reason:** Unity Editor는 파일 변경 후 자동 리컴파일이 지연될 수 있음. 리컴파일 없이 콘솔 확인 시 이전 상태의 에러/성공이 표시되어 오류 누락
- **Correct pattern:** `mcp__unityMCP__refresh_unity(mode=force, compile=request, wait_for_ready=true)` → 대기 → `mcp__unityMCP__read_console(types=["error"])`
- **Impact:** 리컴파일 없이 콘솔만 확인하면 실제 컴파일 에러를 놓칠 수 있음
- **Added:** 2026-08-20

### RULE-021: Never mix PowerShell and Bash syntax in a single pipeline
- **Status:** ✅ Verified (environment constraint)
- **Category:** Tooling
- **Reason:** PowerShell uses `$env:VAR`, backtick escaping, object pipeline; Bash uses `$VAR`, backslash escaping, text pipeline. Mixing causes silent failures or parse errors on Windows
- **Correct pattern:** Use pure PowerShell OR pure Bash per command — never chain `cmd /c` inside PowerShell or vice versa
- **Impact:** Mixed syntax causes unpredictable failures — wrong variable expansion, broken pipelines, or commands that silently do nothing
- **Ported from:** AbyssNode RULE-035 (universal tooling constraint)
- **Added:** 2026-07-13

---

## Adding New Rules

1. Add to appropriate section (NEVER DO / ALWAYS DO / WARNINGS)
2. Number convention: 001-009 = NEVER DO, 010-019 = ALWAYS DO, 020+ = WARNINGS
3. Required fields: Category, Reason, Added date
4. Recommended fields: Correct pattern, File path, Impact
5. Update `Last updated` date
