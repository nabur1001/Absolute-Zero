# PLAN_029 — Multi Rule Corrections and Synchronized Defense Presentation

Status: Implemented with focused static/EditMode validation. Live 3–4 player network validation remains open.

The user subsequently approved execution of this plan. The implementation keeps D4 unchanged and limits 1v1 gameplay changes to D2 cancellation plus synchronized defense presentation.
Date: 2026-09-15.

## Goal and scope contract

Implement the user's Multi decisions while preserving existing 1v1 behavior. The permitted 1v1 changes are impact-synchronized defense presentation and cancellation of a selected item action when that selected copy is removed by an earlier steal/reroll. All other 1v1 rules remain unchanged.

This plan is not authorization to refactor unrelated systems. Existing dirty files must be inspected and preserved before implementation.

| Change | Multi (3–4 players) | 1v1 |
|---|---|---|
| Five-kill victory at an action boundary, including ghosts | Implement | No change to scoring, victory or round progression |
| Random item sources exclude Tarot | Implement consistently | Preserve current tables and eligibility |
| Two-survivor reward fills empty random slots to four | Preserve existing items; verify exact fill behavior | No change |
| Defense reaction synchronized with incoming impact | Planned | Planned presentation change |
| Selected item removed by earlier steal/reroll | Cancel action without substitute or extra consumption | Apply the same cancellation rule |
| Minimum three seconds per played item | Implement | Preserve attack/special clip durations; defense-only segment exception below |
| Temperature display follows impact/healing | Correct Multi sequence | Preserve existing display behavior |
| Cat, Hug and Feed presentation | Connect appropriate Multi sequences | Preserve existing sequences |
| Ghost art | Keep current placeholder; final art undecided | No change |

Non-goals: balance changes, new items, action-order changes, new defense mechanics, extra actions per turn, ghost art production, scene layout changes, SO tuning shared with 1v1, package upgrades, harness changes, and broad architecture migration.

## Requirements and implementation evidence

The user's latest decisions override conflicting historical descriptions in [GAME_DESIGN](../GAME_DESIGN.md) and earlier Multi plans. The following are observations of the current working tree, not claims of runtime verification.

| ID / design source | Required behavior | Current evidence / owner | Planned verification |
|---|---|---|---|
| M1 — user decision; Multi victory | First action taking any score to five ends the match; same-action achievers share victory | `TurnManager.CheckMultiMatchEnd` runs from `HandleRoundEnd`; `FindMultiMatchWinner` chooses the highest score at that later time | First achiever wins with multiple survivors; later actions do not execute |
| M2 — user decision; ghost skills | Ghosts can win immediately under the same rule | `UseGhostSkillRpc`, `GhostSkillService`, `AuthoritativeDeathService` | Frost and attributed delayed damage can produce a ghost winner |
| M3 — Multi item restrictions | Exclude Tarot from every random generation source | Initial/deathmatch grants use a rule-aware table; `TurnManager.GetDropTable` exposes the raw table to other paths | Initial, thresholds, next round, deathmatch and reroll coverage |
| M4 — user decision; two survivors | Keep current items and fill only empty random slots up to four | `GrantDeathmatchItems` and `PlayerInventory.GrantRandomItems` already cap grants; duplicate stacking needs checking | Inventories with 0–4 occupied random slots, remaining uses preserved |
| P1 — user decision; defense | Applicable defense reacts during the actual incoming hit, in both modes | `ApplyDefenseMulti` applies mechanics without an event; Multi VFX skips actors without `MainEffect` | Full/partial block, nonmatching attack, repeated hits, no attack |
| P2 — Multi presentation | Each played item lasts at least three seconds; temperature follows impact/heal | `PlayMultiCombatVFXSequence` pads the whole sequence; `PlayMultiItemSequence` applies temperatures before animation | Multi timing trace and all-client HUD capture |
| P3 — item-specific staging | Multi uses appropriate Cat/Hug/Feed staging | Dedicated handlers currently sit in the 1v1 presentation path | Multi seat/target and local/remote views; 1v1 unchanged |
| A1 — user decision; ghost appearance | Retain placeholder; sprite remains future work | Existing ghost visual implementation is provisional | No visual asset or prefab replacement in this work |

Source files are under `Assets/Scripts/Core/` unless stated otherwise. Preserve the boundaries described in [AI_TARGET_ARCHITECTURE](../AI_TARGET_ARCHITECTURE.md); its historical implementation snapshot is not current runtime evidence.

## Approved decisions and planning boundary

The user approved all three proposed gameplay rules and explicitly extended D2 to 1v1. These approvals settle design; the subsequent user clarification restricts the present task to planning, not code implementation.

| ID | Approved rule | Planned implementation constraint |
|---|---|---|
| D1 ? effect-group boundary | Each scheduled effect is evaluated separately. Fan/chill damage from one periodic tick is settled together; multiple threshold achievers in that tick share victory | Stop before the next effect/group after victory. Different RPC actions are not simultaneous solely because they arrive in one frame |
| D2 ? removed selected item | In Multi and 1v1, cancel the selected action if an earlier steal/reroll removes that selected copy | No automatic substitution, reselection or additional consumption. Preserve selection when only its slot position changes; replacing it with a newly rolled copy of the same type still removes the original copy |
| D3 ? duplicate top-up | Two-survivor top-up retains existing items and fills empty random slots to four; duplicates occupy separate empty slots, maximum three copies of the same item | Do not stack the top-up into existing uses. Preserve other grant semantics and 1v1 rewards |
| D4 ? delayed-effect defense | Existing Q23 remains unresolved and outside this change | Preserve current mechanics; defense synchronization does not add blocking to delayed damage |

Existing reverse-list processing of scheduled effects is the implementation baseline; retain it for this correction instead of introducing new item priority. Record it in deterministic tests. Only D1's per-effect winner boundary is new.

D2 concerns the identity of the selected copy, not merely its item type or array position. Plan tests for compaction, same-type reroll and another identical copy remaining in inventory. Do not change these cases into automatic substitution. Death-of-actor cancellation remains the existing C2 rule.

## 1. Capture the 1v1 baseline first

- Record current 1v1 attack order, defense amount/filter/consumption, damage and healing, inventory results, win/round transitions, item durations and special sequences.
- Record the existing Multi action order and death-credit rules. They must not change as a side effect of this work.
- Identify mode entry points in `TurnManager`, `CombatResolver` and `CombatVFXManager`. All behavior changes other than defense presentation and approved D2 cancellation must stay behind the Multi route/rule.
- Do not change shared item SO values to enforce Multi presentation duration. Do not filter the shared item registry or remove Tarot from 1v1 assets.

Acceptance: baseline scenarios and mode routing are recorded before modifying shared code. Existing failures are distinguished from new regressions.

## 2. Stop Multi resolution at the winning action

### Architecture choice

Currently `CombatResolver.ResolveMulti` computes an entire turn and `MultiCombatApplicator.Apply` applies its accumulated deltas. Checking victory only after application, or merely shortening VFX, would still let later actions alter the winning result.

| Option | Benefit | Cost / decision |
|---|---|---|
| Resolve a terminal prefix using projected scores in the pure Multi resolver | Preserves whole-turn application | Requires duplicating live inventory mutations, random outcomes and death eligibility in projection; not selected |
| Resolve, validate and apply one Multi action at a time | Uses actual committed state and scores; earlier inventory mutations are visible | Selected; introduce a bounded synchronous Multi loop while retaining pure effect calculation |

Use the second option. `MatchCombatSnapshot.CurrentKillScores` already exists; reuse it rather than adding another score owner. Extract a single-action pure calculation entry point from the Multi resolver and collect accepted action results into the existing presentation batch. Do not call the existing whole-turn resolver repeatedly with partial intents: that would reapply defense and rebuild action order. Keep `TurnManager` as the sole phase owner and `AuthoritativeDeathService` as the authoritative death/score mutation owner. The 1v1 resolver retains its existing flow; only D2 action validation/cancellation and defense presentation metadata may change.

### Atomic action rule

1. Freeze the existing action order once from the attack-start snapshot. Validate and activate defenses once, retaining active defense metadata through the entire action loop.
2. For each ordered action, capture current committed state, validate actor, target and modifiers, and validate item identity/consumption against the approved D2 selection policy; then calculate that action's immediate effects and attributable deaths.
3. Prepare exact inventory outcomes and consumption before writing gameplay state. Validate the complete action and its serializable presentation record; apply it synchronously without yielding.
4. Settle all death credits for that action together, using existing valid-source rules. Self/natural deaths must not gain new kill credit. Compare actual scores before/after the action for all seats together.
5. If one or more seats cross the threshold, latch that winner mask and stop. Otherwise continue to the next action. Collect only committed records for presentation; canceled later actions have no consumption, effects or events.

Use the mode rule's victory threshold (currently five), not a second hard-coded constant. No coroutine yield or gameplay callback may interleave with an action commit. Notifications that can request phase progression must be deferred until the action's complete death group has been settled. Dispatch score/death notifications under a reentrancy guard; evaluate terminal match outcome before ordinary round end or two-survivor grants.

Do not check after each victim in a multi-victim action: that would make winner selection depend on seat iteration order. Separate RPCs/actions are not one simultaneous batch merely because they arrive in the same frame. Under approved D1, each scheduled effect is a separate group and a fan/chill tick is one group. Preserve existing deterministic due-effect ordering.

Defense preactivation remains mechanically before attacks. Already committed defense usage stays committed; canceled later main actions do not consume items. Preserve current targeting and cancellation rules, including no automatic retargeting after target death.

### Calculation/application consistency gate

`MultiCombatApplicator` currently ignores the boolean results from `TryConsume`, `TryRerollRandomItems` and `TryStealRandomItem`. A later score comparison cannot repair an already partially applied action.

- Give the Multi action application path an explicit success/failure result. Resolve random inventory choices once on the server into exact staged slot changes; application must not reroll those choices.
- Preflight inventory identities, remaining uses, seat eligibility and result/event capacity before any temperature, inventory, modifier or score write. Commit those prepared changes together without yielding; buffer outward notifications until completion.
- Preserve selected item identity when compaction moves a slot; never silently substitute another item. A slot lookup failure must be distinguished from actual removal of the selected item. Under approved D2, removal of the selected copy cancels its action in both modes before effect execution and additional consumption. Track copy removal explicitly; another copy of the same item type cannot satisfy the original selection. Do not bypass failed consumption or introduce double consumption.
- An internal preflight/capacity failure aborts further resolution with diagnostics; do not send a success result, grant rewards, retry random choices or start another round. An unexpected exception during mutation terminates normal match progression; do not pretend partial writes were rolled back. Provide a recoverable session exit and record the failing action/sequence.
- Test this contract before wiring the new winner check. No new whole-turn inventory simulator or generic transaction framework is required.

Carry turn-local defense activation, neutralization and damage attribution forward between actions. A fresh snapshot must not lose an earlier defense or reactivate a consumed one. Do not recompute ready/heatwave ordering from changed temperatures.

### Every authoritative death path

- Main actions: `ResolveMulti` / `MultiCombatResolution` / `MultiCombatApplicator`.
- Ghost Frost: `UseGhostSkillRpc` / `GhostSkillService`.
- Delayed effects before main actions: `BuffDebuffSystem` and the attack-phase entry path.
- Prep fan/chill effects: the prep tick and the existing ghost damage attribution.
- Disconnect/death cleanup: preserve existing no-extra-kill-credit behavior; do not allow another round transition after a terminal result.

### Delayed and periodic effects: approved boundaries

The current `BuffDebuffSystem.ProcessTurnStart(PlayerState[])` writes every due effect before `TurnManager` processes `LastAppliedEffects`. Adding a victory check to that later loop is insufficient.

- Add a Multi-only due-effect processing path that can stage/apply a defined group, settle its death credits, check victory and stop before the next group. Determine due entries once. Preserve current reverse-list ordering as the baseline; each due effect is one group under approved D1.
- Decrement countdowns once per turn, not once per yielded effect. A winner stops all subsequent due effects and the main-action stage. Do not resume pending effects in a completed match; clear them on new-match teardown/reset.
- Keep the `ProcessTurnStart(p1, p2)` implementation and 1v1 call unchanged.
- Under approved D1, settle a prep fan/chill tick's eligible seats as one atomic group and aggregate all attributable deaths before checking joint winners. In every case, do not grant threshold/deathmatch items before the corresponding winner check.
- For Frost, settle the single accepted RPC action and deaths, latch victory, then present its result. Subsequent queued ghost RPCs fail the terminal-state guard.
- Prepare two-source/four-kill fixtures for due effects and periodic damage. For two separate due effects, the first threshold achiever wins and the second effect is canceled. For one periodic tick, all threshold achievers share victory. These are planned expected results, not executed test evidence.

A server-owned, idempotent terminal result must immediately reject further gameplay input and prevent later ticks, queued actions, rewards and next-round starts. Audit coroutine continuations as well as RPC entry guards. A winning ghost does not need to become alive, and victory does not require one survivor.

Use an explicit match outcome/winner mask; do not reuse the batch's round-survivor `WinnerMask` as evidence of the five-kill winner. Remove the late highest-score calculation from the Multi victory decision; preserve 1v1 `MatchManager` behavior.

## 3. Finish the deciding presentation, then show results

Logical victory is immediate at the accepted action boundary. Clients finish the deciding action's hit/defense/death presentation, then display the latched result. They must not play later canceled actions or enter the next prep phase.

- Carry terminal match information alongside the accepted Multi sequence using explicit seat identities.
- Reuse sequence acknowledgements and inventory presentation cleanup; make completion idempotent on timeout, disconnect, despawn and scene exit.
- Compute a bounded presentation timeout from the accepted schedule, including per-item duration, special staging and death animation. Do not assume the existing 15-second minimum covers every sequence.
- Ensure ghost/prep/delayed-effect victories also enter this final-presentation path even when normal round-end conditions are false.
- Clear terminal state only on an explicit new match. Round reset cannot clear a match victory and resume play.
- Keep the terminal outcome and sequence available as replicated current match state for a late UI subscription; an event-only result RPC is insufficient. Use the existing legal match-state transitions and a Multi terminal flag to suppress round rewards/reset while final presentation completes. Failed transitions must not fall through to the next round.

### 3A. Separate replicated death from presentation completion

Observed source: `AZPlayerVisual.SyncGhostFromLifeState` sets `_isDead` on replicated Ghost state, while `PlayDeathSequence` returns when `_isDead` is already true. State arriving before its presentation can therefore suppress the intended death animation.

- Keep authoritative life state immediate. Add a separate Multi presentation record keyed by match epoch, sequence and victim seat, with pending/playing/completed states. Replicated Ghost state must not mark the death animation completed.
- Include the accepted action's deaths in its presentation record. At that action's impact/death stage, play each victim's death once regardless of whether life-state replication or the event arrived first. Duplicate records must not replay it.
- Defer the existing ghost visual transition until that death presentation completes for an already observing client. Preserve the current placeholder art; this is timing, not an art replacement or a delay to server death/kill state.
- A client newly observing a seat that was already a ghost uses the current ghost appearance without replaying historical deaths. Distinguish this initial snapshot from a pending death in the client's current sequence.
- Round revival/new match resets presentation records at the appropriate epoch. Despawn or scene exit cancels local animation and releases its resources without changing authoritative life state.
- Keep the existing 1v1 death path unchanged. Any shared helper extraction must preserve it under the regression gate.

### 3B. Gate the result UI on matching data and presentation

Observed source: `GameDataBridge.ProcessMatchEnd` waits a fixed settle time after `MatchComplete`; `HandleMultiMatchOutcome` separately receives the winner data. A timer is not proof that these refer to the same complete result.

- Publish a coherent, server-owned Multi terminal record containing match epoch, deciding sequence, outcome and winner mask. Integrate it with `MatchNetworkState`, the terminal path in `TurnManager`, `GameDataBridge` and the result presenter. Do not infer the winner from current score sorting on the client.
- Retain logical victory separately from the server's authorization to display the result after the presentation barrier finishes or expires. Both refer to the same terminal record.
- Open the Multi result screen once only when (a) the terminal record is valid for the current match, (b) the server has released that result for display, and (c) the local deciding presentation completed or was explicitly settled by timeout reconciliation. A fixed UI delay alone cannot satisfy any of these conditions.
- Support both arrival orders: terminal record before presentation and presentation before terminal record. State-change handlers and presentation completion both re-evaluate the gate; a late UI subscription reads the retained record and local presentation status instead of depending on a past RPC.
- After a server presentation timeout, explicitly cancel/settle the outstanding local sequence, restore authoritative final displays, release its inventory lock and then open the matching result. Do not leave an animation behind the result screen or falsely report successful playback.
- A late subscriber with no pending historical playback can display the released current result. Do not create a historical replay requirement. If terminal data is missing, wait for authoritative synchronization or use the existing session-exit path; never guess a winner or start a round.
- Preserve 1v1 result/rematch semantics; apply this new result gate to Multi only.

### 3C. Bind completion acknowledgements to sequence ownership

Observed source: `CombatVFXManager` starts presentation coroutines directly and completes through shared `_activeSequence`; `PresentationBarrier` already validates the sequence and sender. Overlapping local coroutines must not acknowledge each other's work.

- Give each accepted presentation an immutable context containing match epoch and sequence, its coroutine handle, completion state and ownership of temporary temperature/inventory display overrides. Pass that context to completion; do not read a mutable global sequence when acknowledging.
- Admit at most one active Multi presentation per client. Queue a subsequent valid sequence until the active one completes; ignore duplicate starts, and discard records from an old match or already settled sequence. Bound the queue; a gap/overflow requires synchronization with authoritative current state, not silent event dropping or invented playback.
- A newer record does not implicitly complete or cancel an older one. Only explicit timeout reconciliation, teardown or an authoritative epoch change cancels outstanding work. Cancellation cleanup may release only the canceled context's resources.
- Keep a bounded completion record so a duplicate delivery of a completed sequence can resend the same acknowledgement without replaying VFX. Server pending-client removal remains idempotent and validates actual RPC sender identity.
- Prevent overlapping `PresentationBarrier.Begin` calls from replacing a live barrier. Serialize ordinary combat, ghost death and periodic-death presentation through the existing TurnManager owner. Queue accepted nonterminal presentation groups when needed; do not grant clients authority to advance phases.
- Include the match epoch in completion validation, or guarantee nonreused sequence IDs over the entire network session. Select one explicit representation during implementation; stale acknowledgements from an earlier match must never satisfy a current barrier.
- Disconnect removes only that client from the current pending set. Late acknowledgement after timeout is ignored. Neither a timeout nor a disconnect may clear the latched winner or resume a completed match.
- Existing 1v1 presentation behavior remains the baseline. Use a Multi-specific admission path; if immutable acknowledgement context is shared with 1v1, preserve its order/timing and validate identical externally visible behavior.

These three contracts implement already approved gameplay and require no additional gameplay decision. They are planned work, not evidence that synchronization is implemented or verified.

## 4. Synchronize defense with the attack impact

### Shared presentation contract — one of the two permitted 1v1 changes

Keep defense activation order, filters, block amounts, remaining uses and damage results unchanged. Add or expose authoritative presentation metadata identifying the defender, defense item, applicability and block outcome; clients must not recalculate combat from current mutable state.

An applicable defense means one that actually affected the authoritative attack under existing rules. Do not add blocking to ghost skills, debuffs, sabotage or delayed damage to make a visual reaction possible. In particular, leave the unresolved delayed-defense rule Q23 untouched (D4).

At the incoming attack's impact callback:

- Full block: play the matching defense reaction; no damage/temperature-loss reaction.
- Partial block: play defense and the actual damage/temperature response together.
- Nonmatching attack: no successful-defense reaction.
- Repeated applicable impacts: show the appropriate reaction for each impact without extra item consumption.
- No incoming attack: no hit-triggered defense reaction. No standalone defense action slot or added three-second wait.

Use a small presentation helper called from each mode's existing impact path. Preserve 1v1 attack/special clip durations, gameplay attack order, and camera/temperature timing relative to each retained attack clip. Remove the standalone defense clip and only its associated attacker-focus switch/inter-item wait. This permitted defense synchronization can shorten total 1v1 presentation time and move subsequent clips earlier; it must not change combat results or unrelated pauses. Preserve the existing overall minimum-duration policy rather than adding the Multi per-item minimum to 1v1. Two defense selections produce no attack/impact reaction and complete the sequence with normal cleanup.

Current 1v1 `targetDefending` is inferred from item category alone. Replace that presentation inference with authoritative applicability/block metadata; a selected but nonmatching defense must not suppress actual damage feedback. Cover fallback/no-Animator paths and multi-hit callbacks, not just the standard animation path.

Inspect `CombatResult.cs` (including `CombatResultData`), `CombatEventNetData`, `CombatResolutionBatchNetData` and both VFX paths before selecting the smallest metadata extension. The current 1v1 event cap and Multi event capacity must not silently discard added defense information. Prefer enriching the corresponding hit record over adding unrelated standalone actions. Review serialization round trips, enum stability and payload bounds.

## 5. Complete the Multi presentation corrections

- In `PlayMultiItemSequence`, apply intermediate temperature display values at impact/heal callbacks, not item start. Preserve server authority; client overrides only control presentation.
- Distinguish cosmetic multi-hit animation from multiple authoritative damage steps. Do not split gameplay damage or add victory checks per cosmetic hit. For one authoritative effect, update the HUD at its designated effect impact; only use intermediate values when the server actually supplies multiple effect results. Do not show the final temperature prematurely through normal HUD replication.
- Give each actually played item a minimum three-second interval, including attack and its concurrent defense. Longer existing sequences are allowed to finish. Canceled actions and defense-only selections do not create empty intervals.
- Adapt existing Cat/Hug/Feed presentation primitives to explicit actor/target seats for Multi. Avoid copying 1v1 assumptions such as `1 - index` or interpreting ClientId as seat.
- Keep 1v1 callers and their timings unchanged when extracting helpers. Use existing placeholders when art/audio are missing; do not produce assets in this task.

## 6. Make Multi grants consistently rule-aware

- Route every random generation path through the active mode's eligible table: initial setup, 30/20/10 thresholds, subsequent round grants, deathmatch top-up, reroll and any environment grant found by call-site audit.
- Preserve the unfiltered/1v1 behavior for the 1v1 rule. Filter generation eligibility, not the global item-ID registry required for decoding inventory.
- At exactly two survivors, grant once for that round and fill only empty random capacity to four. Preserve existing item identities and remaining uses.
- Existing design permits duplicate drops with a maximum of three copies; do not restrict candidates to previously unowned IDs. Under approved D3, top-up duplicates occupy separate empty slots. Existing items and uses must remain intact, and successful top-up must fill available random capacity to four without changing other grant/stacking semantics or 1v1 behavior.
- Under approved D3, construct weighted candidates that can legally occupy the remaining capacity and update eligibility after each grant. Avoid unbounded random retries. Detect an unsatisfiable pool/capacity and report it explicitly; do not silently replace items, increase their uses or describe a short grant as a successful fill. If the eligible pool cannot fill capacity, preserve existing items, grant only legal available copies and report the shortfall; do not loop indefinitely or exceed the copy cap.

## 7. Validation and delivery gates

| Gate | Required cases |
|---|---|
| Multi action calculation and commit | A reaches five before B acts; canceled B consumes nothing and creates no scheduled effects; multi-victim action; joint threshold crossing; no threshold crossing; fixed original ordering; defense applied/consumed once |
| Inventory consistency | Earlier steal/reroll changes a selected item: expected execution/consumption follows approved D2; slot compaction preserves identity; failed internal preflight produces zero writes/events; prepared random result equals applied result |
| Effect boundaries | Expected grouping/winners follows approved D1; countdown decreases once; no later group applies after victory; Frost followed by queued RPC; winning group grants no subsequent rewards |
| Authority | Host-only mutation; validated sender/seat/phase; ghost winner; delayed/chill credit; self/natural death exclusion; repeated terminal callbacks are harmless |
| Rewards | Every generation source excludes Tarot in Multi; 0–4 occupied-slot top-ups preserve existing uses; duplicate eligibility/copy cap and capacity follow D3; reward triggers once; 1v1 table unchanged |
| Event transport | Defense/terminal metadata round-trip; maximum event count; full and partial block; no silent event truncation |
| Death presentation ordering (3A) | Ghost state before event and event before Ghost state; duplicate death; two victims; new observer of an existing ghost; round revival; exactly one death animation for each currently observed accepted death |
| Result readiness (3B) | Result before VFX and VFX before result; late UI subscription; missing winner data; server timeout with local animation pending; result opens once for matching epoch/sequence, never from settle time alone |
| Sequence ownership (3C) | Overlapping ghost/combat presentations; duplicate/new/stale records; wrong sender; old-match ACK; late ACK after timeout; cancellation cannot release a newer context's lock or acknowledge its sequence |
| Multi live play | Three players and Host plus three remote clients; every seat as actor/target/ghost winner; correct intermediate HUD; sequential item intervals; no canceled VFX |
| Lifecycle | Disconnect during deciding hit/ack, late subscription/spawn, timeout, scene exit, second round and a fresh match; no stuck inventory locks or duplicate result screen |
| 1v1 regression | Without D2 removal, gameplay order/state/outcome remain identical; only defense synchronization changes presentation. With D2 removal, the canceled action has no effect, added consumption or VFX, and resulting temperatures/winner follow the remaining valid actions. Attack/special clip lengths and unrelated mechanics remain unchanged |

Use focused deterministic tests for resolver, credit boundaries and serialization, then Unity auto-compilation and console inspection. Never call forced `recompile_scripts`. If an API change is required, verify against installed Unity/NGO sources and official documentation before implementing it. Generated project compilation alone is not Unity runtime verification.

Do not claim full completion without live host/client checks. Report static, compile and runtime evidence separately, including unavailable checks and pre-existing asset limitations.

### Fixed-input comparison protocol

Capture an immutable baseline before gameplay edits: repository revision plus relevant dirty-file copies/hashes, mode/rule values, starting temperatures, inventory identities/uses, ready order, selected targets, scheduled effects and deterministic random draws. Preserve the user's working files; do not reset the tree to obtain a baseline.

For each scenario, compare an ordered trace of action acceptance, state deltas, consumption, deaths/credits, terminal winner mask and presentation events. Identify players by logical seat and separately record network client IDs.

| Scenario | Required comparison |
|---|---|
| 1v1 attack/defense, recovery and special items | When no selected copy is removed, gameplay trace is identical. Presentation differs only by synchronized applicable defense and its removed standalone segment/wait |
| Ordinary Multi action reaches five with multiple survivors | Winner is that action's achiever; all subsequent action deltas, consumption and events absent |
| One action yields multiple threshold achievers | Same winner mask when victim iteration order is varied; do not vary actual action order |
| Ghost Frost wins while other players survive | Correct ghost seat wins; queued later intents rejected; no next prep/round |
| Full/partial/nonmatching defense, repeated cosmetic hits | Feedback matches authoritative block outcome; unchanged total damage and consumption |
| 3-player and Host plus 3 clients | Same accepted action order, final state and winner mask on every participant; each seat exercised |
| Disconnect during final presentation | Latched winner unchanged, surviving clients finish or time out cleanly, no next round |
| D1/D2/D3 interactions | Separate due effects: first achiever wins. Same tick: joint achievers. Removed selected copy: cancel in both modes, including same-type reroll; compaction alone preserves selection. Top-up: separate duplicate slots, max three copies, existing uses unchanged |

Before comparing VFX timing, distinguish absolute sequence time from timing relative to an attack clip. Removal of a standalone defense segment may shift later clips; it must not alter their own impact offsets; authoritative 1v1 results may differ only when approved D2 cancellation removes an otherwise executed action. Record missing assets/pre-existing failures separately.

## Execution order and completion checklist

- [x] Capture baseline and confirm mode isolation.
- [ ] Define/test the Multi presentation context and terminal record contracts in 3A/3B/3C, including arrival-order and epoch cases, before connecting final-result display.
- [x] Implement the approved D1/D2/D3 rules after implementation authorization; keep D4 mechanics unchanged.
- [x] Add D2 selected-copy cancellation in both modes, preserving compaction identity and suppressing canceled-action VFX.
- [x] Establish single-action preflight/commit, inventory identity and fixed-order/defense context; focused deterministic tests pass.
- [x] Implement Multi action-boundary resolution and terminal outcome; focused threshold tests pass.
- [x] Connect ghost, delayed and prep death paths plus terminal presentation guards; live multiplayer cases remain below.
- [ ] Connect 3A death playback tracking, 3B GameDataBridge/result readiness and 3C serialized presentation/ACK ownership; pass the added synchronization cases.
- [ ] Add defense metadata and synchronized impact reaction in both modes; pass 1v1 regression gate.
- [x] Correct Multi-only timing, temperature display and special sequence code paths.
- [ ] Complete rule-aware Multi grants and verify top-up behavior.
- [ ] Run compile, host/client and lifecycle validation; record actual evidence.
- [x] Sync relevant sections of `GAME_DESIGN.md` / `DESIGN_QUESTIONS.md` with the user's decisions and record implementation status in continuity documents, preserving existing edits.

Keep changes reviewable by phase. An issue in a Multi correction must be fixed in its Multi route; the sole gameplay exception for 1v1 is the explicitly approved D2 cancellation rule.

## Implementation status — 2026-09-15

- Baseline revision: `02049cd56217`. Twenty-two pre-implementation working-tree files were copied to `C:\Users\paek6\AppData\Local\Temp\az-plan029-impl-e0dfc5040c4b4a23bc578c147e5c977c` before edits; unrelated dirty files were not reset.
- Multi resolution now commits one ordered action at a time and checks new five-kill threshold crossings immediately after each committed action. Ghost Frost, individual due effects, and grouped periodic fan/chill ticks use the same latch-and-stop rule.
- A retained terminal record binds winner data, deciding presentation sequence, and server release. The client serializes Multi presentations, acknowledges the immutable sequence, settles timeout state, supports late result subscribers, and suppresses the ordinary round result for a terminal Multi action.
- D2 cancellation tracks the selected copy through compaction and invalidates it on reroll/steal replacement in both modes. Multi sabotage chooses and validates its random inventory mutation before committing it, then applies that exact prepared result once.
- Applicable defense metadata is serialized with the hit. Multi and 1v1 show full/partial defense at the attack impact, including fallback and repeated cosmetic-hit paths, without a standalone defense interval.
- Multi item playback uses a minimum three-second interval for each executed item, applies temperature overrides at its configured impact/midpoint, and routes Cat, Hug, and Feed through explicit actor/target seats.
- All audited Multi random paths use the rule-aware table. Two-survivor grants keep existing slots/uses and fill random capacity to four with separate copies and a three-copy cap.
- Validation completed: generated solution build succeeded with zero errors and four existing warnings; Unity command compilation succeeded; EditMode suite `AbsoluteZero.EditorTests` passed 11/11; Unity Console reported zero current errors. The deterministic suite covers full/partial/nonmatching defense metadata, D2 identity behavior, four-seat ordering, single/joint threshold masks, overflow rejection, NGO serialization, and Tarot filtering.
- Still required for runtime certification: a three-player session and Host plus three remote clients; all-seat actor/target/ghost winner runs; disconnect during the deciding presentation; timeout/late-ACK behavior; second-round/rematch reuse; visual timing and one-time result/death playback from every client view. These are not claimed as passed.

## Plan review record

### Authorized AI follow-up repair — 2026-09-15

Scope: fix ordinary actor/target RPC eligibility (including minigame completion), isolate and correct KI-007 transport receive exhaustion, expand real-process regression and delayed/lossy loopback checks. Non-goals: gameplay/balance decisions, scene/art layout changes, package upgrades, or claiming internet/visual certification.

The installed Transport 2.7.2 is embedded through Unity Package Manager for a narrowly scoped UDP receive-buffer fix. Completed failed/empty receives previously retained acquired buffers without enqueueing or releasing them. A standalone driver regression reproduces failed acceptance after eight empty datagrams with a four-buffer queue, while the clean control succeeds. The source version and dependencies remain unchanged; retain the upstream license and review this local patch when changing Transport later. Full evidence and remaining work belong to [the validation matrix](../Validation/PLAN_029_ai_matrix.md).

Final selected follow-up evidence: **42 runtime configurations passed, 0 failed, 0 pending; EditMode 28/28; builds have zero errors**. Coverage includes every remote-seat Prep/Attack exit, all ghost winners, minigame invalidation/retry, same-tick joint winners, individual delayed-effect cutoff, real delay/loss, late ACKs, and three consecutive same-process 1v1 rematches. Fixture corrections and excluded attempts are retained in the matrix; production Lobby/Relay, other PCs, rendered presentation and long-duration profiling remain open.

### Expanded AI validation — 2026-09-15

The user requested execution of AI-verifiable checks. Added opt-in development matrix probes, a bounded Windows process runner, and ten further deterministic cases (26 total passing tests). Selected evidence covers 21 distinct runtime cases: 17 pass, 3 fail, 1 production-session-inconclusive. Failures identify remaining-client timeouts after abrupt exit and ordinary RPC acceptance of ghost targets; they are recorded as KI-007/008 rather than silently declared fixed. No production gameplay code or assets were changed in this validation task. See [full matrix and remaining work](../Validation/PLAN_029_ai_matrix.md).

### Authorized startup and scenario corrections — 2026-09-15

Scope: network-ready/idempotent bootstrap, bounded initialization, identity binding, failure UI, local scene-load counts, result-UI teardown, and four-process validation. Non-goals: balance/gameplay changes, scene/art layout edits, architecture migration, and certification of the entire lifecycle matrix.

Implemented in MatchNetworkState, MatchCompositionRoot, TurnManager, AZPlayerVisual, SceneLoadSyncManager, GameUIRoot, RoundResultPresenter, MultiScenarioProbe, and `.codex/scripts/*multiplayer_scenario.py`. Run5 completes the seeded two-combat/five-kill flow with matching state on all four processes, one result completion per client, and four zero exit codes. Bootstrap retries preserve scores and terminal state. The Run4 teardown exception is fixed. Captures remain black; rendered framing is unverified. Evidence and remaining checks: [scenario report](../Validation/PLAN_029_scenario_20260915.md).

### Follow-up implementation review corrections — 2026-09-15

- Preserved the approved kill boundary, shared-defense/D2 scope, item balance, three-second Multi minimum, and existing scene positions/art. No scene or item asset was edited for these corrections.
- Remote visual binding now searches inactive objects in the player's scene, activates the claimed slot at runtime, checks its owner, and uses stable seat/local-seat mapping rather than currently discovered players. Opponent bar positions and temperature values use the same mapping, including holes left by disconnects.
- HUD creation waits for the assembled match configuration instead of permanently falling back to two players before network configuration arrives.
- Disconnect callbacks update authoritative state without starting a concurrent round-ending coroutine. The active phase re-evaluates round end at its boundaries and after presentation waits; a round-end latch rejects duplicate entry and resets for the next round/rematch.
- Presentation settlement/destruction restores the saved camera position. Multi camera hit feedback is restricted to the actual local target.
- Multi Hug/Feed/Cat invoke the effect-display callback at contact, ingestion, or destination arrival. Fully blocked feeding skips the ingestion sequence; blocked hugs show defense at contact. Multi Cat uses a transient visual aimed at the explicit target and keeps the inventory rebuild lock until settlement. Shared 1v1 special sequences retain their existing timing through optional callbacks.
- Validation: 16 EditMode tests passed, including new all-local-seat mappings for 2/3/4 players, disconnect plus stale-ACK barrier behavior, and idempotent forced camera settlement. Generated solution build has zero errors and the four previously observed warnings. An attempted live preview binding probe was rejected by the Unity command tool's reflection namespace restriction; it is not counted as a successful binding or Play Mode test. Host plus three-client arrival-order, disconnect, second-round, and visual checks remain open.

This revision records the user's D1/D2/D3 approvals, expanded 1v1 D2 scope, and the later implementation authorization. Contracts 3A/3B/3C are implemented, while their full host/client arrival-order and lifecycle matrix remains runtime validation debt.

Historical plan score: 92/100, from an earlier static review. It is not a runtime test result. The focused deterministic checks above now cover core calculation and transport contracts; three/four-player host/client behavior still requires live execution.

During this session, code edits were mistakenly started before the plan-only clarification. All agent edits to 11 C# files were restored byte-for-byte from the pre-edit working-tree backup, which included the user's existing changes. Comparison of backed-up `Assets/Scripts` files found no remaining differences. Unity project identity was checked read-only; no gameplay or asset operation was executed. This record does not claim that Unity auto-compilation was unaffected by the temporary edits.
