# Codex 교차검증 프롬프트 — PLAN_022 (Sector 9: 1v1 Improvements)

---

## Prompt 1: 구조/로직 검증 (9-1, 9-2, 9-3)

```
You are reviewing an implementation plan for a Unity 6 (6000.3.11f1) multiplayer turn-based game using NGO 2.11.2 (Netcode for GameObjects), host-authoritative architecture.

Review the following 3 changes for correctness, edge cases, and risks. For each, answer:
1. Is the approach correct for the architecture? (server-authoritative, NetworkVariable only written by host)
2. Are there edge cases or race conditions missed?
3. Is there a simpler or more robust alternative?
4. Any Unity/NGO version-specific concerns?

---

### Change 9-1: PrepPhase 1-Second Fan Immunity

**Requirement:** First 1 second of PrepPhase, fan natural temperature decrease is disabled. Item effects and recovery apply normally.

**Current code (TurnManager.cs, PrepPhaseRoutine):**
```csharp
float elapsed = 0f;
while (elapsed < currentPrepDuration)
{
    float dt = Time.deltaTime;
    elapsed += dt;
    _tempSystem.Accumulate(dt);
    // ...
    while (_tempSystem.ConsumeTick())
    {
        for (int i = 0; i < _players.Length; i++)
        {
            _tempSystem.ApplyFanTick(_players[i]);
            _tempSystem.ApplyRecoveryTick(_players[i], recoveryRate);
            _tempSystem.CheckThresholds(...);
        }
    }
}
```

**Proposed change:** Add `const float PREP_IMMUNITY_DURATION = 1f;` and guard the ApplyFanTick call with `if (elapsed >= PREP_IMMUNITY_DURATION)`.

**TemperatureSystem.ApplyFanTick:**
```csharp
public void ApplyFanTick(PlayerState player)
{
    if (!player.IsFanActive.Value) return;
    float newTemp = Mathf.Max(MIN_TEMP, before - player.FanSpeed.Value);
    player.Temperature.Value = newTemp;
}
```

**Questions:**
- The tick system accumulates time and fires every TICK_INTERVAL (1.0s). The first tick fires at elapsed ~1.0s. Does the immunity guard `elapsed >= 1.0f` actually skip anything, or does the first tick coincidentally align with the immunity end? Should it be `elapsed > PREP_IMMUNITY_DURATION` or use a separate boolean flag that flips after the first tick?
- Recovery applies normally during immunity — is there any interaction where recovery + immunity creates an unintended temperature gain?
- 0° death check runs during immunity. If a player enters PrepPhase at very low temp with a debuff active, can they die during the immunity window? Is that intended?

---

### Change 9-2: 3-Second Unified Animation Timing

**Requirement:** All item animations unified to 3s total duration during Attack Phase.

**Current flow (CombatVFXManager.PlayActionSequence):**
- 0.5s intro → Player1 action (variable duration) → 0.3s pause → Player2 action (variable) → end
- Per-item duration comes from ItemDataSO.AnimDuration (fallback 0.8s) plus EffectDelay/EffectInterval timing

**Proposed approach:** Wrap each player's action in a 3s envelope:
```csharp
float t0 = Time.time;
yield return PlaySingleAction(player1);
float pad = 3.0f - (Time.time - t0);
if (pad > 0.05f) yield return new WaitForSeconds(pad);
```

**Questions:**
- `Time.time` can drift with frame timing. Should we use `Time.unscaledTime` or accumulate delta time instead for more precise padding?
- Death sequence (2.5s freeze + shatter) is a terminal event that ends the round. If death happens mid-action, does the 3s envelope need to handle early exit? Currently `PlayActionSequence` checks death after each action.
- The brief pause (0.3s) between actions — should it be inside or outside the 3s envelope? Current plan: outside. Total = 0.5s intro + 3.0s + 0.3s + 3.0s = 6.8s. Is this acceptable?
- For very short animations (~0.8s), there's a 2.2s idle gap. Should there be a visual indicator (e.g., brief pause pose) or is dead time acceptable?
- `new WaitForSeconds(pad)` — this should be cached per RULE-020 (WaitForSeconds must be cached). But pad is variable. Is `yield return null` loop with elapsed tracking better, or is a one-shot WaitForSeconds acceptable here?

---

### Change 9-3: Cat Reroll Visual Delay

**Requirement:** Cat reroll triggers at cat walk-animation timing (~1s), not instantly.

**Current architecture:**
- Server: CombatResolver → ItemEffectApplicator → RerollAllRandom() (instant, at resolution time)
- Client: CombatVFXManager plays cat animation (SFX_cat, 0.4s + 0.3s = 0.7s total)

**Proposed approach:** Server reroll stays instant (game state is correct). Client-side, delay the inventory UI refresh to match cat animation completion (~1.0s). Fire a static event `OnCatRerollVisual` after the animation to trigger UI update.

**Questions:**
- If the server rerolls instantly but the client delays UI update, what happens if the player opens inventory or interacts during the delay? Could they see stale data?
- The reroll changes the opponent's items, not the local player's. During Attack Phase, the opponent's items should already be hidden/revealed. Is there actually a visible inventory change to delay?
- Would it be simpler to just extend the cat animation duration in CombatVFXManager without adding a new static event?
- If the client disconnects and reconnects during the delay, NetworkVariable already has the rerolled state. Is the static event approach robust to reconnection?
```

---

## Prompt 2: VFX/애니메이션 검증 (9-5, 9-6, 9-9)

```
You are reviewing VFX and animation changes for a Unity 6 (6000.3.11f1) 2.5D game (3D backgrounds, 2D sprite characters). The game uses URP, runtime-built UI (no Inspector wiring), SpriteRenderers for characters, and an ObjectPool pattern for particles.

Review these 3 changes for visual correctness, performance, and Unity best practices:

---

### Change 9-5: Damage Effect Upgrade

**Requirement:** Add vignette edge effect + camera shake + ice particle burst on normal hits (currently only death has camera shake).

**Current hit effects:**
- AZPlayerVisual.PlayDamageFlash(): white flash on sprite renderers via _FlashAmount shader property (0.15s flash, 0.5s hold)
- CombatVFXManager.PlayHitAt(): spawns pooled particle at world position, auto-returns after 3s
- ScreenVFXManager.PlayHitVFX(): full-screen frost overlay (RawImage), fade in 0.2s, hold 0.16s, fade out 0.7s

**Proposed additions:**

**A. Vignette:** New RawImage in ScreenVFXManager with runtime-generated radial gradient Texture2D (transparent center, dark edges). Flash simultaneously with frost overlay.

**B. Camera Shake:** Call existing CameraShake.Instance.Shake(0.15f, 0.1f) on hit. Current death shake is (0.5f, 0.3f).

**C. Ice Particle Burst:** Additional particle system on hit, pooled alongside existing _hitEffectPrefab.

**Questions:**
- Runtime Texture2D generation for vignette: what's the recommended resolution? 256x256 with bilinear filtering? Or would a shader-based approach (single quad with radial gradient in fragment shader) be more performant?
- The vignette + frost overlay + damage flash all fire simultaneously. Is there a z-order / layering concern? Current frost overlay is on Canvas sortingOrder=200.
- CameraShake uses transform.localPosition manipulation. Is this safe to call during an ongoing particle animation? Could the camera position shift cause particles to appear misaligned?
- For ice particle burst: should this use the same particle pool as _hitEffectPrefab, or a separate pool? Concern: if both fire simultaneously, the pool might not have enough instances.
- Rapid multi-hit items (e.g., Fan has 3 hits at 0.3s intervals): camera shake (0.15s) is called 3 times in quick succession. CameraShake.Shake() calls StopAllCoroutines() first. Is the rapid stop-restart pattern smooth, or will it cause jitter?

---

### Change 9-6: Defeat Animation Fix

**Requirement:** 
- Non-final round: Freeze → particle burst → smooth return to Idle (not off-screen teleport)
- Final round: Freeze → heavy particle burst → stays frozen (cinematic takes over)

**Current DeathRoutine (AZPlayerVisual):**
1. Play freeze animation state
2. Show freeze overlay sprites (freeze1→freeze2→freeze3 over 0.33s)
3. Hold 1.5s
4. PlayIceBreak SFX + CameraShake(0.5f, 0.3f) + PlayBreakParticles()
5. Hide freeze overlay
6. Move visual root to (0, -100, 0) — off-screen

**Current ReviveVisual:**
- Stops death coroutine, hides freeze, stops particles
- Moves root back to saved position
- Resets animator to "Idle_Tree"

**Proposed change:** 
- Step 6: Instead of off-screen move, wait 0.5s for particles to clear, then fade freeze overlay alpha to 0, reset animator to Idle
- Add `bool isFinalRound` parameter: if true, play heavier particles and skip Idle return
- ReviveVisual still works as safety net for next round

**Questions:**
- The freeze overlay uses SpriteRenderer (not UI). Fading requires either: (a) changing SpriteRenderer.color.a, (b) material alpha, or (c) CanvasGroup. Which is correct for a SpriteRenderer-based overlay?
- If DeathRoutine now returns to Idle internally (non-final), and ReviveVisual is also called at round start, is there a conflict? Could Idle be set twice?
- The `isFinalRound` flag: how should this be passed? Options: (a) parameter on PlayDeathSequence(), (b) static field set by TurnManager, (c) query MatchSnapshot. Which is cleanest?
- PlayBreakParticles() currently plays _iceBreakParticle and _finalBreakParticle. For the "heavier" final round burst, should we: (a) increase particle count on existing systems, (b) play them multiple times, (c) create a new heavy particle system?
- After the non-final death routine ends with Idle return, the character is visible and in Idle. But the round result cinematic (9-4) does a 1s fade-out. Will the player see the character pop back to Idle before the fade? Timing coordination needed?

---

### Change 9-9: Feed/Eat Sprite Fix

**Requirement:** Use actual item sprite during feed/eat animations instead of generic.

**Current code (CombatVFXManager.PlayFeedReaction):**
```csharp
var sprite = GameSprites.GetItemSprite(itemName);
if (sprite != null)
{
    feedSpriteGO = new GameObject("FeedSprite");
    var sr = feedSpriteGO.AddComponent<SpriteRenderer>();
    sr.sprite = sprite;
    sr.sortingOrder = 90;
    feedSpriteGO.transform.position = GetPlayerWorldPos(targetIdx) + new Vector3(-0.05f, 0.5f, 0f);
    feedSpriteGO.transform.localScale = Vector3.one * 0.8f;
}
```

**Analysis:** The code already calls `GameSprites.GetItemSprite(itemName)` with the specific item name. This might already be correct.

**Questions:**
- Is the issue that `GameSprites.GetItemSprite()` returns null for some items, causing no sprite to appear? We need to verify which items return null.
- The sprite position offset (+0.5f Y) — is this aligned with the character's mouth area? The plan suggests adjusting to +0.55f. How should we determine the correct value without runtime testing?
- `new GameObject("FeedSprite")` is created and destroyed each feed animation. Should this be pooled instead for GC reduction?
- The feed sprite uses SpriteRenderer with sortingOrder=90. The character sprites are at various sorting orders. Is 90 guaranteed to render above the character?
- If this is already working correctly, should the task be reduced to just position tuning + null-sprite verification?
```

---

## Prompt 3: UI/HUD/시네마틱 검증 (9-4, 9-7, 9-8)

```
You are reviewing UI and HUD changes for a Unity 6 (6000.3.11f1) game. All UI is runtime-built (no Inspector wiring) using Canvas + TextMeshPro. The game uses a presenter pattern: GameDataBridge (data) → Presenters (logic) → GameHudBuilder (construction). Presenters are plain C# classes, not MonoBehaviours.

Review these 3 changes for UI architecture correctness, Unity best practices, and edge cases:

---

### Change 9-4: Round End Cinematic

**Requirement:** 1s fade-out → winner name + score rise animation → 1s fade-in. Final match: character center + extended hold.

**Current RoundResultPresenter:**
- Plain C# class (not MonoBehaviour) — cannot StartCoroutine
- HandleRoundResult: instantly SetActive(true) on gameOverPanel, sets text/color
- HandleMatchEnd: same pattern, different text
- Panel hidden when PrepPhase begins

**Proposed approach:**
- Pass MonoBehaviour host (GameUIRoot) to RoundResultPresenter constructor for coroutine hosting
- Add CanvasGroup to gameOverPanel for alpha fade
- Create full-screen black overlay RawImage for scene fade
- Cinematic coroutine: fade overlay 0→1 (1s) → show text with Y rise animation (-50→0, 0.8s) → hold 1.5s → fade overlay 1→0 (1s)
- Match end: same fade but hold indefinitely, show lobby button

**Questions:**
- Passing MonoBehaviour host for coroutine: is this a code smell? Should RoundResultPresenter be converted to MonoBehaviour instead? Or should we use async/Awaitable (Unity 6 supports it)?
- CanvasGroup.alpha on gameOverPanel — this affects all children. The lobby button should only be interactable after cinematic completes. How to handle: (a) separate CanvasGroup for button, (b) enable button.interactable after delay, (c) button on a different panel?
- The full-screen black overlay: should this be a shared resource (e.g., on ScreenVFXManager) or owned by RoundResultPresenter? Other systems might need fade-to-black too.
- If PrepPhase starts before cinematic finishes (e.g., server advances quickly), HandlePhaseChanged hides the panel. Should the cinematic coroutine be stopped? Who cancels it?
- The Y rise animation: for smooth easing, should we use AnimationCurve.EaseInOut, DOTween, or manual Mathf.SmoothStep? The project doesn't currently use DOTween.
- For final match "character displayed center" — this requires moving AZPlayerVisual or changing camera. Is this feasible without art assets? Should it be deferred?

---

### Change 9-7: Progress HUD

**Requirement:** Top-center: nicknames + crown icon on first-Ready player + active attacker box color highlight.

**Current MatchHudPresenter:**
- Score display: `"P1  {wins} : {wins}  P2"` — generic labels, no nicknames
- No crown icon, no attacker highlight
- GameHudBuilder constructs all elements at runtime

**Current data available in MatchSnapshot:**
- P1RoundWins, P2RoundWins, LastRoundWinner, IsFinalRound
- Missing: FirstReadySeat (who pressed Ready first)

**Proposed layout:**
```
[P1 Name Box] 👑  {score} : {score}  [P2 Name Box]
```
- Crown icon next to first-Ready player during Attack Phase
- Active attacker box highlighted with color

**Proposed data flow:**
- Add `byte FirstReadySeat` to MatchSnapshot, populated from TurnManager ready tracking
- Add static event `CombatVFXManager.OnAttackerChanged(int seatIndex)` for highlight

**Questions:**
- Adding FirstReadySeat to MatchSnapshot: MatchSnapshot is likely a struct sent via ClientRpc. Adding a field changes the serialization. Is this safe with NGO 2.11.2? Any versioning concerns?
- The crown icon: should it be a TMP inline sprite (`<sprite=0>`), a separate UI Image, or a world-space SpriteRenderer? Which is most maintainable for runtime-built UI?
- Nicknames are TBD (depends on Sector 8). Using "Player 1"/"Player 2" as placeholder — should the presenter accept a Func<int, string> delegate for name resolution, so it's easy to swap later?
- `OnAttackerChanged` static event: during Attack Phase, this fires twice (once per player). The highlight should switch between boxes. What happens if the round ends mid-attack (death)? Does the highlight get stuck?
- Crown visibility: shown only during Attack Phase. What about Resolution Phase? RoundOver? Should it persist until next PrepPhase or hide immediately after Attack?
- The box highlight: solid color background, border color change, or glow effect? For runtime-built UI without custom shaders, what's the simplest approach?

---

### Change 9-8: Item Selection Arrow

**Requirement:** Arrow above selected item with bounce animation. Ready button lit sprite on press.

**Current selection feedback:**
- InventoryPresenter.UpdateSelectionVisuals: selected item gets green outline (HoverEffect), others dimmed to alpha 0.35
- Ready button: already swaps to BTN_PRESSED sprite (MatchHudPresenter line 229-231)

**Proposed approach:**
- Create arrow GameObject with SpriteRenderer, sortingOrder=95
- Position above selected item (+0.6f Y)
- Bounce: sinusoidal Y oscillation (sin(Time.time * 4f) * 0.05f) in Update()
- Arrow created once per InventoryPresenter, shown/hidden on selection

**Questions:**
- InventoryPresenter is a plain C# class (not MonoBehaviour). It can't have Update(). How to drive the bounce animation? Options: (a) separate MonoBehaviour component on arrow GO, (b) coroutine on a host, (c) subscribe to a static Update event. Which is cleanest?
- Arrow sprite: runtime-generated or loaded from Resources? If runtime: how to generate a triangle SpriteRenderer without a sprite asset? Options: (a) create a Texture2D programmatically, (b) use a LineRenderer, (c) use UI Image with a built-in sprite.
- sortingOrder=95: the selected item's HoverEffect outline is at some order, feed sprites at 90, character sprites at various. Is 95 correct? Could the arrow render behind something?
- When the phase changes from Prep to Attack, the arrow should disappear. Who hides it? InventoryPresenter already handles phase changes?
- Items are world-space objects (SpriteRenderer), not UI. The arrow is also world-space. But could the arrow be occluded by 3D background geometry? Should it use the same _ZTest=Always trick as hit particles?
- The ready button sprite swap is already implemented. Should this task verify it works or just mark it as done?
```

---

## 사용법

1. **Prompt 1** → Codex에 붙여넣기 → 구조/로직 답변 확인
2. **Prompt 2** → Codex에 붙여넣기 → VFX/애니메이션 답변 확인
3. **Prompt 3** → Codex에 붙여넣기 → UI/HUD 답변 확인
4. 답변에서 나온 이슈 → PLAN_022에 반영 후 구현 착수
