# Absolute Zero — Game Design Document

> **Genre**: 2.5D Turn-Based Deathmatch | **Modes**: 1v1 / Multi (3~4) / Solo (vs Bot)
> **Visual**: 3D Korean pavilion (정자) + 2D hand-drawn characters/items
> **Network**: Host-authoritative, Unity Relay (1v1/Multi) / Local (Solo)

---

## Overview

Players sit across a wooden floor (마루). Each has a **fan blowing cold air**, lowering their body temperature. Use items to attack, defend, or sabotage. **First to reach 0° is frozen.**

```
Starting Temp: 37°  |  Fan: -1°/sec  |  Prep Time: 20s  |  Defeat: 0°  |  Recovery: 1°/sec (after Ready)
```

---

## Game Modes

| Mode | Players | Win Condition | Scene | Death Rule |
|------|---------|--------------|-------|------------|
| **1v1** | 2 (online) | Bo3 — first to 2 round wins | `GameScene` | 0° = round ends |
| **Multi** | 3~4 (online) | First to **5 kills** (cumulative across rounds) | `GameScene_Multi` (NEW) | 0° = ghost, round continues |
| **Solo** | 1 + BT Bot | Same as 1v1 (Bo3) | `GameScene` or `GameScene_Solo` | Same as 1v1 |

### Mode Details

**1v1 (현행 + 개선):**
- Bo3 format, Ready-press order attack, defense always first
- Improvements: 1s prep immunity, 3s animation timing, UI/visual enhancements
- No post-death attacks

**Multi (3~4인 전용):**
- Kill-based scoring (5 kills to win, co-victory possible)
- Death → Ghost state (debuff trolling, ghost kills count as score)
- Round ends when ≤1 player alive (or all dead simultaneously)
- Round reset: all revive at 37°, items/thresholds fully reset, kill score persists
- Drag-to-target item selection
- Separate balance: Windbreaker 1-use, max 4 random items, threshold grants all 1 each
- Initial random item grant at game start (2 items)

**Solo (1인 + 봇):**
- Same rules as 1v1
- BT AI bot with 2~3 difficulty levels
- No Relay needed, local host execution
- Bot execution: TBD (server virtual bot vs client process)

---

## Turn Cycle

```
PREP PHASE (20s)          ATTACK PHASE              RESOLUTION
─────────────────    →    ────────────────    →    ──────────────
Fan ON: -1°/sec          Actions execute           Apply results
Select item              in Ready-press order      Check 0° death
Press "Ready"            Defense always active     Threshold items
Fan OFF: recovery        First kill = win          → Next turn
```

### Prep Phase
- Fan decreases temperature at **1°/sec** while active
- **1 item per turn** — select one item, confirm with "사용하기", then press "준비 끝"
  - Exception: certain instant-use items (e.g. Tarot Card) activate immediately on confirm, then allow selecting **1 additional item**
- Alternative: press **"준비 끝"** without selecting any item → no action this turn
- Ready / Main select → fan stops → temperature **recovers at 1°/sec**
- Timer expires with no selection → **idle** (no action taken, fully vulnerable)
- Temperature hits 0° during prep → **instant loss**

### Attack Phase
- Execute in **Ready-press order** (who pressed first goes first)
- **Defense exception**: always activates regardless of order
- If first action kills (0°), **second action is cancelled** — round ends immediately (Q10 confirmed)
- Simultaneous Ready → **lower temperature acts first** (comeback opportunity) (Q14 confirmed)

---

## Screen Layout

```
┌─────────────────────────────────────────────────┐
│  [Opponent Temp ██████████████████]              │
│  [My Temp ██████████████████████]          [20s] │
│                                                  │
│              ┌───────────┐                       │
│              │ OPPONENT  │  [선풍기]              │
│              │ (front)   │                       │
│              └───────────┘                       │
│         ···opponent items on floor···            │
│                                                  │
│  ─ ─ ─ ─ ─ ─ ─ 마루 ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─  │
│                                                  │
│    [기본1][기본2]            [랜덤1][랜덤2]        │
│      [기본3][기본4] [준비끝] [랜덤3][랜덤4]        │
│                 (semicircle arc)                  │
└─────────────────────────────────────────────────┘

Camera: Fixed 3rd-person behind player, slight overhead angle
Rendering: 3D background + 2D sprites on floor
Both clients see mirrored view (my items = bottom, opponent = top)
```

### Player Visibility

| Element | Visible | Description |
|---------|---------|-------------|
| **Opponent character** | Full 2D sprite | Front view, all animations (attack, damage, freeze, idle, etc.) |
| **Self character** | Hand only (TBD) | Full body hidden — first-person hand sprite for item-use animations |
| **Network object** | Both exist | Both Player objects spawned and synced; self player's visual (body/head/hair) is hidden client-side, system logic unchanged |

- System logic (NetworkObject, PlayerState, temperature, combat) runs identically for both players — visibility is purely a client-side rendering concern
- Self player's hand animation asset is not yet created; will be added as a separate sprite/animator later
- During Attack Phase, only the opponent character plays full-body animations (item use, damage reaction, freeze/death)

### Character Architecture

| Layer | Source | Description |
|-------|--------|-------------|
| **Visual (opponent)** | Scene-placed object | `EnemyPlayer` is pre-placed in GameScene — NOT spawned from prefab. Body, head, hair, arms, animator all included |
| **System logic** | NetworkObject spawn | PlayerState, temperature, combat data — spawned separately as NetworkObject for sync |
| **Linking** | Runtime reference | System logic references the scene-placed visual object; minimal binding (e.g. HP bar repositioning) |

- Character visuals must NOT be instantiated from Player.prefab — use the existing scene object
- Player.prefab is for system logic (NetworkObject + PlayerState) only, not for visual representation
- Visual updates (animations, damage flash, freeze overlay) are driven by the system logic referencing the scene-placed character

---

## Temperature System

```
37° ████████████████████████████ RED        (start)
30° ██████████████████████       PINK       → +1 random item
20° ██████████████               SKY BLUE   → +2 random items, frost edge
10° ████████                     BLUE       → +3 random items, heavy frost
 0° ░░░░░░░░░░░░░░░░░░░░░░░░░░░ FROZEN     → DEFEAT
```

- Max capacity: **8 random items** (basic items don't count)
- Threshold grants are **one-time only** — re-crossing doesn't re-grant
- Visual: segment markers disappear after grant

---

## Item System

### Types

| Type | Persistence | Example |
|------|-------------|---------|
| Basic/Permanent | Never consumed | Fan, Windbreaker |
| Basic/Consumable | Refreshes each game | Warm Tea, Cat |
| Random/Consumable | One-time use | All random items |

### Categories

| Category | Target | Timing | Examples |
|----------|--------|--------|----------|
| **Attack** | Opponent temp ↓ | Immediate | Fan, Ice Cream, Water Gun |
| **Defense** | Block incoming | Immediate | Windbreaker, Mask |
| **Recovery** | Self temp ↑ | Immediate | Warm Tea, Hot Pack |
| **Buff** | Self benefit | Next turn | Buldak Noodles, Soda |
| **Debuff** | Opponent penalty | Next turn | Samgyetang |
| **Sabotage** | Disrupt opponent | Varies | Cat, Red Card, Blue Tape |
| **Special** | Unique mechanic | Varies | Screwdriver, Tarot Card |

### Basic Items (Always Available)

| Item | Cat | Persistence | Value | Effect |
|------|-----|-------------|-------|--------|
| Fan (부채) | ATK | Permanent | 3° | Opponent temp −3° |
| Windbreaker (바람막이) | DEF | Permanent | 4° | Partial block: absorbs up to 4° of temp attack damage |
| Warm Tea (따뜻한 차) | REC | Consumable | 7° | Self temp +7° |
| Cat (고양이) | SAB | Consumable | — | Reroll ALL opponent random items |

### Random Items (Threshold Drops)

| Item | Cat | Value | Drop% | Mini-game |
|------|-----|-------|-------|-----------|
| Hand Fan (손풍기) | ATK | 4° | 12% | — |
| Ice Cream (아이스크림) | ATK | 5° | 10% | — |
| Iced Americano (아.아) | ATK | 5° | 10% | — |
| Water Gun (물총) | ATK | 7° | 6% | 5s: hit 3 moving targets |
| Hug T-shirt (안아줘요 티셔츠) | ATK | =my temp | 4% | 10s: hug approaching character |
| Hot Americano (뜨.아) | REC | 5° | 10% | — |
| Smartphone (스마트폰) | REC | 3→5→7° | 6% | 5s: pattern unlock |
| Hot Pack (핫팩) | REC | 10° | 4% | **TBD: 아이템표 10s/30tap vs 미니게임표 7s/15tap** |
| Mask (마스크) | DEF | 100% food block | 8% | — |
| Samgyetang (삼계탕) | DBF | opp +3 now / opp −7 next | 8% | — |
| Soda (탄산음료) | BUF | self −5 now / self +15 next | 6% | — |
| Buldak Noodles (불닭볶음면) | BUF | +17° | 2% | 10s: tap to boil water |
| Screwdriver (십자드라이버) | SPC | 2×fan | 4% | 7s: tighten 3 screws |
| Tarot Card (속마음 타로카드) | SPC | — | 2% | — |
| Claw Machine (집게손) | SAB | — | 4% | 7s: timing claw grab |
| Blue Tape (청테이프) | SAB | — | 4% | 5s: timing tape cut |
| Red Card (레드카드) | SAB | — | 2% | 5s: tap red card among yellows |

> **Drop rates sum to 102%** — treated as **weighted pool** (e.g. 손풍기 12/102 ≈ 11.8%). Duplicate drops allowed, but **max 3 copies** of same item.
> **9 items require mini-games**, 8 do not.

```
Category key: ATK=Attack  DEF=Defense  REC=Recovery
              BUF=Buff    DBF=Debuff   SAB=Sabotage  SPC=Special
```

---

## Mini-Game System

Mini-games trigger during PrepPhase when selecting certain items. The PrepPhase timer keeps running.

### Flow

```
Item click → Mini-game starts (PrepPhase timer continues)
  ├─ SUCCESS → item queued for use
  ├─ FAIL    → selection cancelled, return to item selection
  └─ PREP TIME EXPIRES → mini-game force-cancelled, no effect
```

- Failure = immediate selection cancel (one chance only)
- To retry, must re-select the item and play the mini-game again
- Success = item enters the action queue

### Mini-Game Details

| Item | Time | Input | Description | Success | Fail |
|------|------|-------|-------------|---------|------|
| Screwdriver (십자드라이버) | 7s | Circular drag | 3 screws appear. Drag clockwise (3 rotations each) to tighten | All 3 screws tightened in time | Time out or incomplete |
| Claw Machine (집게손) | 7s | Timing tap | Claw moves left-right above target item. Tap when aligned | Claw grabs item at correct timing | Missed timing or time out |
| Water Gun (물총) | 5s | Tap | 3 targets move irregularly. Tap to shoot | Hit all 3 targets in time | Missed targets or time out |
| Hot Pack (핫팩) | **TBD (7s or 10s)** | Rapid tap | Hot Pack turns red as tapped, gauge rises. **TBD: 아이템표 10s/30tap vs 미니게임표 7s/15tap** | Reach tap count in time | Count not reached |
| Blue Tape (청테이프) | 5s | Timing tap | Tape stretches with timing bar. Tap in green zone | Tap in green zone | Tap outside green or time out |
| Smartphone (스마트폰) | 5s | Drag | 3×3 dot grid, trace shown pattern (ㄱ, ㄴ, Z, etc.) | Pattern matched | Wrong pattern or time out |
| Buldak Noodles (불닭볶음면) | 10s | Rapid tap | Pot + gauge. Tapping boils water, gauge rises | Gauge reaches 100% | Gauge incomplete |
| Red Card (레드카드) | 5s | Tap | Yellow + red cards appear mixed. Find and tap red card | Tap red card correctly | Tap yellow or time out |
| Hug T-shirt (안아줘요 티셔츠) | 10s | Timing tap (both sides) | Character approaches center. Tap left+right (arms) simultaneously when in hug zone | Timed correctly | Mistimed or time out |

---

## Environment Variables

Appears at **2nd prep phase** of each round. Random. Resets per round.

| Environment | Effect | Impact |
|-------------|--------|--------|
| **Sunny Day** (햇살쨍쨍) | Fan-off recovery: 2°/sec | Recovery items less valuable |
| **Cool Breeze** (바람선선) | Fan-off recovery: 0°/sec | Recovery items critical |
| **Cicada Song** (매미울음) | Audio/visual distraction | Pure chaos |
| **Kids** (잼민이들) | Steal 1 unused random item | Can't hoard items |
| **Ambulance** (앰뷸런스) | Turn 3: lower-temp player +10° | Comeback mechanic |
| **Summer Vacation** (여름방학) | Prep time: 20s → 10s | Less decision time |
| **Heat Wave** (폭염경보) | Lower-temp player acts first | Overrides Ready-order |

### Environment Variable Staging Detail

> All environment variables follow the same base staging:
> 1. Camera rotates LEFT to show surrounding environment
> 2. Staging text appears center-screen (fade in / fade out) during camera rotation
> 3. Each environment's unique staging plays
> 4. Camera returns to default position

| Env | Staging Text | Background Staging | Sound |
|-----|-------------|-------------------|-------|
| **Sunny Day** | "햇살이 더 쨍쨍해집니다." | All Light color → yellow tint, intensity UP | SFX_cicada |
| **Cool Breeze** | "시원한 바람이 불어옵니다." | Wind particle effect appears around scene | SFX_wind |
| **Kids** | "근처에 어린 친구들이 서성거립니다." | Kid sprite rises from below on LEFT side of pavilion | SFX_kidWhistle |
| **Ambulance** | "근처에 응급구조원이 대기중입니다." | Ambulance sprite enters from off-screen LEFT → position (2.47, 1.4, 14) behind pavilion (정자 뒷편) | SFX_siren |
| **Summer Vacation** | "여름방학이 얼마 남지 않았습니다." | Timer shakes on X/Y, clock-inner SpriteRenderer color → RED | SFX_clock |
| **Heat Wave** | "폭염경보가 발생했습니다." | All Light color → red tint, intensity UP | SFX_cicada |

#### Kids — Turn 3 Special Staging
1. Kid sprite on LEFT sinks back down (disappears)
2. Kid sprite rises behind opponent (random item area), animation state: `ready`
3. Animation → `steal`, play SFX_kidSteal, 1 random item consumed from BOTH players
4. Kid sprite sinks down and disappears

#### Ambulance — Turn 3 Special Staging

> Ambulance spawn position: (2.47, 1.4, 14) — z=14 is behind the pavilion (정자 뒷편). After effect triggers, the rescue worker sinks down and is removed.

**If MY temperature is lower:**
1. Full-screen blanket drops from top → covers view (SFX_wear)
2. Temperature recovers, blanket alpha fades to 0

**If OPPONENT temperature is lower:**
1. Rescue worker sprite rises behind opponent (next to opponent)
2. Blanket drops from above → covers opponent (SFX_wear)
3. Rescue worker animation `rescueA_complete` plays simultaneously
4. Opponent temperature recovers, blanket alpha fades to 0
5. Rescue worker sprite sinks down and disappears

---

## Item Selection Flow

```
[1] Click item on floor
        │
  (mini-game required?)
  YES → mini-game starts (PrepPhase timer keeps running)
        ├─ SUCCESS → item queued
        ├─ FAIL    → selection cancelled, back to [1]
        └─ PREP TIME OUT → mini-game cancelled, no effect
  NO  → item queued immediately
        │
  (Sub item?)
  YES → queued, turn continues (can still pick Main)
        → executes at Attack Phase START
  NO  → Main item queued for Attack Phase
        │
[2] Press "준비 끝" → fan OFF, recovery starts → TURN END
    (action cannot be changed after "준비 끝")

Alternative: Press "준비 끝" without selecting → no action this turn
```

> **Main/Sub system:** 1 Main + 1 Sub per turn. Sub items execute at Attack Phase start before Main resolution.
> **TBD:** Which items are Sub? (See Open Questions)

---

## Attack Sequence (Visuals)

```
"납량 시작" (Cooling Begins) → center screen text
     ↓
1st player item animation → temp bar reacts (shake + flash)
     ↓
Check: opponent 0°? → YES: freeze + shatter → round end
                       NO: continue
     ↓
2nd player item animation → temp bar reacts
     ↓
Check: opponent 0°? → round end or next turn
```

### Defeat Animations
| Scenario | Visual |
|----------|--------|
| Opponent reaches 0° | Opponent freezes solid → ice shatters → next round |
| Opponent 0° (final round) | Freeze → character shatters completely |
| Self reaches 0° | Screen edges freeze → fade out → next round |
| Self 0° (final round) | Camera rotates to opponent POV → own character shatters |

### Item Staging Detail

> **Global rules:**
> - Damage/Heal → always play SFX_damaged / SFX_heal + hit/heal visual, in addition to item-specific SFX
> - Item sprite source: (1P) FPS hand sprite, (3P) opponent-use sprite. If no 1P/3P note → use the floor item sprite
> - All item animations play sequentially during Attack Phase (fixed rule — exceptions only when explicitly stated)

| # | Item | Self (1P) | Opponent (3P) | Extra Staging |
|---|------|-----------|---------------|---------------|
| 1 | **부채** (Fan) | anim: `swing`, sprite: fan, dmg@0.5s/0.7s/0.9s (3 hits), SFX_swing | anim: `swing`, sprite: fan, dmg@0.3s, SFX_swing | — |
| 2 | **바람막이** (Windbreaker) | anim: `defence`, sprite: windbreaker(1P), SFX_clothZiper | anim: `defence`, no item sprite, SFX_clothZiper | — |
| 3 | **따뜻한 차 / 뜨.아** (WarmTea / HotAmericano) | anim: `use`, sprite: drink, heal@0.5s, SFX_drink | anim: `drink`, sprite: drink, heal@0.5s, SFX_drink | — |
| 4 | **고양이** (Cat) | no anim, no sprite, SFX_cat (on cat movement start) | same | — |
| 5 | **십자드라이버** (Screwdriver) | anim: `use`, sprite: screwdriver, exec@0.5s, SFX_driver | anim: `attack`, sprite: screwdriver, exec@0.5s, SFX_driver | Target fan SpriteRenderer color → blue. (Optional) Fan shakes on X/Y continuously |
| 6 | **삼계탕** (Samgyetang) | anim: `feed`, sprite: samgyetang, dmg@0.5s, SFX_feed | anim: `attack`, sprite: samgyetang, dmg@0.5s, SFX_feed | If I→opponent: opponent plays `feed` anim + item sprite@0.5s. If opponent→me: simple heal staging only |
| 7 | **집게손** (ClawMachine) | anim: `use`, sprite: claw, SFX_steal | anim: `attack`, sprite: claw, SFX_steal | — |
| 8 | **아이스크림 / 아.아** (IceCream / IcedAmericano) | anim: `feed`, sprite: item, dmg@0.5s, SFX_feed | anim: `feed`, sprite: item, dmg@0.5s, SFX_feed | If I→opponent: opponent plays `feed` anim + item sprite@0.5s. If opponent→me: simple damage staging only |
| 9 | **물총** (WaterGun) | anim: `gun`, sprite: watergun(1P), dmg@0.5s~1s, SFX_watergun | anim: `attack`, sprite: watergun(3P), dmg@0.5s~1s, SFX_watergun | (Optional) Water splash particle on hit |
| 10 | **핫팩 / 스마트폰** (HotPack / Smartphone) | anim: `use`, sprite: item, heal@0.5s | anim: `heal`, sprite: item, heal@0.5s | No SFX |
| 11 | **타로카드** (TarotCard) | anim: `card`, sprite: tarot, dmg@0.5s, SFX_heal | anim: `card`, sprite: tarot, dmg@0.5s, SFX_heal | Turn end blocked for 1s during animation |
| 12 | **청테이프** (BlueTape) | anim: `tape`, no item sprite, SFX_boxtape | anim: `attack`, sprite: bluetape, SFX_boxtape | Basic items covered by "청테이프" sprite for 1 turn |
| 13 | **마스크** (Mask) | anim: `mask`, sprite: mask(1P), SFX_wear | anim: `mask`, sprite: mask(3P), SFX_wear | Mask anim TBD — create from `playerA_idle` clip after 3P art received |
| 14 | **손풍기** (HandFan) | anim: `fan`, sprite: handfan(1P), dmg@0.5s~1s, SFX_miniFan | anim: `attack`, sprite: handfan, dmg@0.5s~1s, SFX_miniFan | — |
| 15 | **불닭볶음면** (BuldakNoodles) | anim: `eat`, sprite: buldak, heal@0.7s, SFX_eat (0.7s/0.9s/1.1s x3) | anim: `eat`, sprite: buldak, heal@0.7s, SFX_eat (0.7s x1) | — |
| 16 | **탄산음료** (Soda) | anim: `use`, sprite: soda, dmg@0.5s, SFX_drink | anim: `drink`, sprite: soda, dmg@0.5s, SFX_drink | Soda damage does NOT trigger damage staging |
| 17 | **레드카드** (RedCard) | anim: `card`, sprite: redcard, SFX_redcard | anim: `card`, sprite: redcard, SFX_redcard | If I→opponent: opponent plays `disappoint` immediately. If opponent→me: no FPS anim |
| 18 | **안아줘요 티셔츠** (HugTshirt) | anim: `hug`, no item sprite, dmg@0.5s, SFX_hug | anim: `hug`, no item sprite, dmg@0.5s, SFX_hug | If I→opponent: camera approaches opponent until 0.5s, returns@1s. If opponent→me: opponent plays `jump` → moves toward me → plays `hug` → returns to position |

---

## 1v1 Improvements (2026-08 additions)

### System Changes

| Change | Detail |
|--------|--------|
| **Prep Phase 1s immunity** | First 1 second of PrepPhase: fan natural temperature decrease disabled. Item effects and recovery apply normally |
| **3s animation timing** | All item animations unified to 3s total duration (animation + wait) during Attack Phase |
| **Defense always first** | Windbreaker activates before any attack regardless of Ready order (existing behavior reinforced) |
| **Cat reroll timing** | Cat item reroll triggers at cat walk-animation timing, not instantly |

### UI Enhancements

| Change | Detail |
|--------|--------|
| **Lobby UI redesign** | Full visual redesign to casual style matching game concept |
| **Progress HUD** | Top-center: nicknames, crown icon on first-Ready player, active attacker box color highlight |
| **Item selection arrow** | Arrow object above selected item for clear visual feedback |
| **Ready button sprite** | Pressed state changes to "lit" sprite (art asset needed) |
| **Nickname system** | TBD — lobby input / Auth ID / separate account system |

### Visual Enhancements

| Change | Detail |
|--------|--------|
| **Round end cinematic** | 1s fade-out → winner name + score rise animation → 1s fade-in. Final winner: character displayed center |
| **Damage effect upgrade** | Vignette edge effect + camera shake + ice particle burst on hit |
| **Feed animation fix** | Item position aligned to mouth + item sprite matches used item |
| **Eat/drink sprite fix** | Use actual item sprite instead of generic |
| **Defeat animation fix** | Freeze → particle burst → Idle return (next round start via ReviveVisual) |
| **Final defeat particle** | Additional ice shard particle on final round defeat |
| **Hover tuning** | HoverEffect scale/outline parameters increased (no code change, value tuning) |

---

## Win Conditions

### 1v1 Mode

| Condition | Result |
|-----------|--------|
| Opponent temp = 0° | Win the round |
| Self temp = 0° | Lose the round |
| Both reach 0° simultaneously | Draw — round voided, replay (no score change) |
| First to **2 round wins** | Match victory |

### Multi Mode (3~4)

| Condition | Result |
|-----------|--------|
| Any player reaches 0° | That player enters **Ghost state** — round continues |
| ≤1 player alive (or all dead) | Round ends → all revive at 37° → next round |
| First to **5 kills** (cumulative) | Match victory |
| Multiple players reach 5 kills simultaneously | Co-victory |
| Ghost kill via debuff | Counts toward ghost player's kill score |

---

## Network Model

```
CLIENT                          SERVER (Host)
──────                          ─────────────
SelectItemRpc(slot) ──────→     Validate + store
ReadyRpc() ───────────────→     Record timestamp, stop fan
MiniGameResultRpc() ──────→     Validate result
                                    ↓
                         CombatResolver.Resolve()
                         TemperatureSystem.Apply()
                         BuffDebuffSystem.Tick()
                                    ↓
         ←──────────────── ResultRpc(outcomes)
         ←──────────────── NetworkVariable updates
UI updates from callbacks          (temp, phase, timer)
```

All mutations server-side: damage, healing, time, win/loss, item use, buff/debuff, mini-game validation.

---

## Multi-Player System (3~4인 전용)

> Completely separate from 1v1. Different scene, different balance, different win condition.

### Balance Differences from 1v1

| Parameter | 1v1 | Multi |
|-----------|-----|-------|
| Win condition | Bo3 round wins | 5 kills (cumulative) |
| Max random items | 8 | **4** |
| Initial random grant | None | **2 at game start** |
| 30° threshold | +1 item | **+1 item** |
| 20° threshold | +2 items | **+1 item** |
| 10° threshold | +3 items | **+1 item** |
| Windbreaker | Permanent | **1-use (Consumable)** |
| Tarot Card | Available | **Removed from drop table** |
| Icebox position | Left-center | **Center** |
| Death rule | Round ends | Ghost state, round continues |
| Deathmatch grant | N/A | **2 players left → instant 4 random items** |

### Target Selection

- Item use requires **drag-to-target** on the target character
- Re-clicking a targeted item cancels the selection
- Reference: Slay the Spire targeting UX

### Round Flow (Multi)

```
ROUND START (all at 37°, items reset, threshold reset)
     ↓
PREP PHASE (20s, 1s immunity)
  - Select item + drag-to-target
  - Press Ready → fan off, recovery
     ↓
ATTACK PHASE (3s per item, defense first)
  - Execute in Ready-press order
  - If player reaches 0° → Ghost state (round continues!)
  - Remaining players keep fighting
     ↓
CHECK: ≤1 alive or all dead?
  YES → Round over, prep text, next round
  NO  → Next turn (PrepPhase again)
     ↓
KILL SCORE: cumulative across rounds
FIRST TO 5 KILLS → MATCH VICTORY
```

---

## Ghost System (다인전 전용)

> Players who reach 0° become ghosts. They can't use items or participate in turns, but can use debuff skills to troll surviving players. Ghost kills count toward score.

### Ghost State Flow

```
Player reaches 0°
  ↓
Freeze animation → ice particle burst → character disappears
  ↓
Ghost prefab replaces character (semi-transparent, ~0.4 alpha)
  ↓
Ghost cannot: use items, press Ready, participate in PrepPhase turn flow
Ghost can: use debuff skills on surviving players (free-form, outside turn structure)
  ↓
If ghost's debuff causes a kill → ghost gets the kill score
  ↓
Round ends (≤1 alive) → all revive at 37° for next round
```

### Ghost Debuff Skills

> Values are initial estimates — balance via playtesting.

| Skill | Effect | Cooldown | Duration |
|-------|--------|----------|----------|
| **Frost Strike** | Target temp instantly −3~5° | 1 turn | Instant |
| **Chill Aura** | Target fan decrease rate ×2, recovery effectiveness ×0.5 | 1 turn | 1 turn (until next PrepPhase end) |

- Target selection: click on surviving player (same as item targeting)
- Ghost visual: semi-transparent character near the play area (TBD — exact visual/movement)
- Skill cooldown prevents infinite debuff spam

### TBD — Ghost System

| Item | Status |
|------|--------|
| Exact damage value for Frost Strike | Playtest |
| Chill Aura stacking with other debuffs | Playtest |
| Ghost movement/positioning on screen | TBD |
| Ghost skill UI (buttons? click target?) | TBD |

---

## Post-Death Attack System (다인전 전용)

When a player dies:
1. Existing items are removed
2. Ghost-exclusive **skills** are granted (not items)
3. Skills operate outside the normal turn structure — ghost acts freely while alive players take turns
4. Ghost's PrepPhase/Ready mechanics are completely disabled

---

## Solo Play / Bot AI

| Parameter | Value |
|-----------|-------|
| Rules | Same as 1v1 (Bo3) |
| Network | Local host, no Relay |
| Bot execution | TBD (A: server virtual bot, B: client process) |
| Difficulty | 2~3 levels |
| Logic | Separate from network multiplayer logic |

### Bot AI Requirements

- BT (Behavior Tree) based decision making
- Must handle: item selection, Ready timing, mini-game participation
- Difficulty affects: item choice quality, Ready timing optimization, mini-game success rate
- Existing handover doc: `Docs/BOT_AI_HANDOVER.md` (server-side virtual bot structure)

---

## Customization System

### Lobby Integration

Lobby screen elements:
- **Solo Play** button
- **Multi Play** button
- **Closet (옷장)** button → opens customization canvas
- **Settings** button
- **Nickname** text input

### Character Customization

5-part equipment system. Reference: Among Us customization UI.

| Part | Sprite Target | Implementation |
|------|--------------|----------------|
| Head (머리) | head child | Overlay SpriteRenderer |
| Top (상의) | body child | Sprite swap or overlay |
| Back (등) | body child (behind) | Overlay (sortOrder adjusted) |
| Bottom (하의) | lowerbody child | Sprite swap or overlay |
| Tail (꼬리) | NEW child object | Overlay SpriteRenderer |

### Implementation Approach (Hybrid)

- **Color/tone changes** → Material tint
- **Part additions** (hat, tail, back decoration) → Overlay SpriteRenderer as child of target part
- **Part replacements** (top, bottom variant) → Sprite swap (same pivot/size variant sprites)

### Equipment Logic

- Click to equip/unequip
- Equipping a different item in the same part → auto-unequip previous
- Unlock: currently all available (A), future transition to play-based unlock (B)

---

## Scene Structure

```
LobbyScene (build 0)
  ├── Nickname input
  ├── Solo Play → GameScene (1v1 rules + Bot AI, no Relay)
  ├── Multi Play
  │     ├── 1v1 (2 players) → GameScene (Bo3, Relay)
  │     └── 3~4 players → GameScene_Multi (5-kill, Relay)
  ├── Closet → Customization Canvas (overlay)
  └── Settings → Settings Canvas (overlay)
```

---

## Open Questions (기획 확인 필요)

### Resolved

| # | Question | Answer |
|---|----------|--------|
| Q4 | **삼계탕 "+3, -7" 효과 방향** | ✅ 즉시 상대 온도 +3° (올림) → 다음 턴 상대 온도 -7° (내림). 둘 다 상대에게 적용 |
| Q5 | **탄산음료 "-5, +15" 효과 방향** | ✅ 즉시 내 온도 -5° (자해 비용) → 다음 턴 내 온도 +15° (이득). 둘 다 자신에게 적용 |
| Q11 | **미니게임 판정 권한** | ✅ 클라이언트 판정 + 서버 타임아웃 강제 실패. 상세 아래 참조 |
| Q12 | **미니게임 실패 시 아이템 소모** | ✅ 아이템 소멸. 실패 시 아이템 파괴되며 다른 아이템 재선택 필요 |
| Q13 | **공격턴 아이템 사용 연출** | ✅ 전용 애니메이션/이펙트 예정, 아트 확정 후 결정 |
| Q14 | **선풍기 월드 표시** | ✅ StayItem 위치에 해당 오브젝트 스폰만 하면 됨. 연출은 추후 추가 |
| Q2 | **따뜻한 차 사용 횟수** | ✅ 1회 (기획서 확인, SO MaxUses=1) |
| Q3 | **고양이 사용 횟수** | ✅ 1회 (기획서 확인, SO MaxUses=1) |
| Q7 | **드롭 확률 합계 102%** | ✅ 가중치 풀로 처리 (12/102 ≈ 11.8%). 기획서 최종 확인 |
| Q18 | **스마트폰 사용 횟수** | ✅ 3회 사용, 회복량 3→5→7° (기획서 확인, SO MaxUses=3, HealPerUse=[3,5,7]) |
| Q15 | **상대방 아이템 보유 목록** | ✅ EnemyItem 위치에 서버가 내려주는 목록 표시. 소모 시 시각적 제거, 재획득 시 다시 표시. 서버 권위 |
| Q17 | **환경 시스템** | ✅ 데모에는 미포함, 이후 추가 |
| Q20 | **라운드 간 리셋 범위** | ✅ 전체 초기화: 전원 37° 부활, 아이템/버프/threshold 지급 이력 모두 리셋. 킬 스코어만 라운드 누적 (다인전) |

### Resolved: Mini-Game Judgment Model (Q11)

```
미니게임 중 성공/실패 → 클라이언트에서 판정 → 결과만 ServerRpc로 전송
PrepPhase 타이머 만료 → 서버가 턴 종료 판정 → 클라이언트도 미니게임 강제 실패 처리

핵심: 서버 타이머가 마스터. 클라/서버 핑 차이로 꼬여도
      서버의 "턴 종료" 판정이 최종 → 클라는 무조건 실패로 전환.
```

### Pending — Item Details

| # | Question | Context |
|---|----------|---------|
| Q1 | **핫팩 미니게임 수치 불일치** — 아이템표 "10초/30번 연타" vs 미니게임표 "7초/15번 연타". 어느 쪽? | 2026-07-18 최종 기획에서도 여전히 불일치. SO 현재값: 10초 |

### Pending — Slot Type (Main/Sub)

| # | Question | Context |
|---|----------|---------|
| Q9 | **타로카드 "추가 사용" 타이밍** — Sub로 먼저 발동? 추가 선택은 언제? | 현재 코드: Sub |

### Resolved via Design Spec (질문지 제외 — 기획서에 직접 명시 예정)

| # | Item | Status |
|---|------|--------|
| Q6 | **마스크 "음식 아이템" 범위** — 아이템 기획서에 음식 태그 직접 명시 예정 | ⏳ 기획서 대기 |
| Q8 | **각 아이템 Main/Sub 구분** — 아이템 기획서에 슬롯 구분 직접 명시 예정 | ⏳ 기획서 대기 |

### Pending — Mini-Game

| # | Question | Context |
|---|----------|---------|
| Q10 | **미니게임 중 상대방 화면** — 상대에게 표시가 보이는지? | 각자 독립 PrepPhase 진행 |

### Pending — Visual / System

| # | Question | Context |
|---|----------|---------|
| Q16 | **아이템 슬롯 UI 레이아웃** — 기본 4 + 랜덤 8 배치 방식? 빈 슬롯 표시? | 현재: 4칸만 |
| Q19 | **안아줘요 티셔츠 역효과** — 내 온도 > 상대 온도면 상대를 회복시킴. 의도된 리스크? 효과 없음 처리? | 코드: diff ≤ 0이면 ApplyHeal(상대) |
| Q21 | **버프/디버프 중첩** — 같은 효과 다중 적용 가능? 삼계탕 2연속 = -14°? | 코드: 무제한 중첩 |
| Q22 | **이번 턴 선택 아이템 상대 공개** — 보유 목록은 공개(Q15)지만, 뭘 골랐는지는? | 코드: 비공개 |
| Q23 | **지연 효과 발동 시 방어 가능 여부** — 삼계탕 -7° 발동 턴에 방어 아이템으로 차단? | 코드: 방어 무시 |

### Pending — Ghost System (다인전)

| # | Question | Context |
|---|----------|---------|
| Q24 | **Frost Strike 데미지** — 즉시 −3°? −5°? | 플레이테스트 결정 |
| Q25 | **Chill Aura 디버프 중첩** — 복수 고스트 Aura 동시 적용 시 효과? | 플레이테스트 결정 |
| Q26 | **고스트 위치/이동** — 화면 어디에? 자유 이동? 고정 위치? | TBD |
| Q27 | **고스트 스킬 UI** — 버튼? 드래그? | TBD |
