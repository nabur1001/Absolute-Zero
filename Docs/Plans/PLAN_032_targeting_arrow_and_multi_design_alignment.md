# PLAN 032 — Targeting Arrow and Multi Design Alignment

## Status

Implemented and runtime-validated on 2026-09-20. D1-D5 were approved by the user and are reflected in code and repository design documents. The Google Sheet remains an external source and was not edited by this repository run.

## Goal

Adopt the approved client-local targeting arrow interaction, align the Multi presentation layout with the current player-relative south/west/north/east view, and reconcile the remaining differences between the `4인용플레이 기획` Google Sheet, `GAME_DESIGN.md`, and the current implementation.

This plan must preserve server authority, stable seat identity, combat order, item balance, the three-second Multi action rule, and existing 1v1 behavior unless a shared presentation fix is explicitly listed.

## Source order for this plan

1. Explicit user decisions in the current session.
2. `Docs/GAME_DESIGN.md` and resolved entries in `Docs/DESIGN_QUESTIONS.md`.
3. Current runtime behavior and validation evidence.
4. The `4인용플레이 기획` Google Sheet where it has not been superseded by a later decision.

## Confirmed decisions

### Targeting interaction

- A SingleTarget item is clicked once to enter targeting mode.
- The arrow tail follows the selected local item view.
- The arrow head follows the mouse while it is not snapped to a valid target.
- Entering a valid character's snap radius attaches the head to that character.
- Leaving the wider release radius returns the head to the mouse.
- The north/front target uses a straight arrow.
- West and east targets use a strongly curved arrow whose bend direction follows the target side.
- The arrow is local presentation only. No network state is written until target confirmation.
- Self-target items continue to bypass target selection.

### Multi perspective

- Each client sees its own seat as south/local.
- The other seats are mapped to west, north, and east presentation slots.
- Remote inventory groups are placed and rotated relative to their presentation slots.
- Authoritative seat indices, participant identity, and action targets are not remapped.
- The current square ghost placeholder remains until dedicated art is supplied.
- Deathmatch inventory reward preserves current items and fills empty random capacity up to four.
- Five-kill victory is checked after each committed action/effect group; all threshold crossings in that group are joint winners.
- Applicable defense reacts at the incoming impact and does not consume a separate three-second action interval.

## Design trace and current drift

| Requirement | Current design/code | Alignment action |
|---|---|---|
| Target designation | `GAME_DESIGN.md` and code use click-to-target; the Sheet still says drag | Replace the Sheet wording with click item → aim arrow → click target |
| Target feedback | Current code shows a small arrow above the hovered character | Replace it with the selected-item-to-pointer/target arrow |
| Re-click cancellation | Server cancellation RPC exists, but normal UI cannot cancel an already confirmed selection | Implement the approved pre-Ready re-click cancellation through the command adapter |
| Multi layout | Client-relative west/north/east with local south | Updated and validated in four client perspectives |
| Icebox position | The Sheet requires center; `GameScene_Multi` still places `BoxSpawnPoint` on the left at local X `-9.145565` | Move only the Multi icebox presentation to the approved south-center location |
| Tarot in Multi | Rule-aware drop table excludes Tarot | Mark complete in the Sheet |
| Windbreaker in Multi | Multi rule configures one use | Mark complete in the Sheet |
| Random capacity | Multi rule caps random items at four | Mark complete in the Sheet |
| Initial grant | Multi rule grants two random items | Mark complete in the Sheet |
| Threshold grants | 30/20/10 degrees grant one item each in Multi | Mark complete in the Sheet |
| Two-survivor grant | Current code fills empty capacity to four without replacing existing items | Update the Sheet wording and mark complete |
| Five-kill outcome | Action/effect-group threshold crossing and joint victory are implemented | Mark complete after regression validation |
| Ghost scoring | Ghost skills can receive kill credit | Update the Sheet from generic post-death attack wording |
| Ghost action model | Current design/code use Frost Strike and Chill Aura; the Sheet says fixed attack item | Retain the two skills and synchronize the older source |
| Death visual | Freeze and immediate break followed by the approved square ghost placeholder | Implemented and captured from four client perspectives |
| Multi presentation timeout | A normal four-client visual run still produced a first-sequence timeout | Complete PLAN 031 timing policy before final acceptance |
| Loading UI | `SceneLoadSyncManager.overlayRoot` remains unassigned | Complete PLAN 031 loading ownership repair |
| Owner animation routing | Runtime evidence still includes an intentional-owner null Animator warning | Complete PLAN 031 local/remote routing repair |
| Initiative crown | TMP crown glyph remains unsupported | Complete PLAN 031 Image marker replacement |
| Target status text | Some current Korean literals are mojibake | Replace with valid UTF-8 text or localization keys |

## Approved decisions

The user approved D1-D5 on 2026-09-20. These choices are implementation constraints.

### D1 — Target confirmation input

- **Approved:** click an item once, aim without holding, snap the arrow to a valid character, then left-click to confirm.
- Hover never confirms automatically because incidental pointer movement must not commit an action.

### D2 — Cancellation after target confirmation

- **Approved before confirmation:** re-click the same item, press Escape, or right-click to leave aiming without sending gameplay state.
- **Approved after confirmation:** before Ready, re-clicking the confirmed item sends `CancelSelectionServerRpc`.
- **Approved after Ready:** cancellation is rejected.
- Clicking a different item while aiming must not silently confirm the pending item; switching or cancellation must remain an explicit local UI transition.

### D3 — Multi icebox placement

- **Approved:** present the local icebox at south-center, behind or beside the local item row, while keeping it clear of the Ready control and arrow tail.
- Apply this to Multi only. The decision is separate from the already confirmed remote-player and remote-inventory layout.

### D4 — Death-to-ghost visual transition

- **Approved:** freeze briefly, break immediately, then show the temporary square ghost placeholder so the playable ghost state is visible.
- Keep the square placeholder until a dedicated ghost sprite is supplied.
- The behavior must match both ordinary and ghost-caused deaths.

### D5 — Post-death gameplay representation

- **Approved:** retain Frost Strike and Chill Aura as ghost skills and update the Sheet's older “fixed attack item” description.
- Ghost skills remain separate from item inventory state.

## Implementation plan

### Phase 1 — Targeting arrow presentation

1. Replace the generated hover-arrow sprite in `GameUIManager` with a dedicated client-local targeting arrow presenter.
2. Read the tail position from `InventoryPresenter.GetLocalView(pendingSlot)` every frame so distribution animation or a view rebuild cannot detach the arrow.
3. Convert the item, pointer, and character target anchors into one screen-space coordinate system.
4. Generate an outlined ribbon and arrow head:
   - straight segment for a snapped north target;
   - strong left/right cubic curve for west/east and free pointer tracking;
   - clamped body width and arrow-head size independent of total length.
5. Use separate enter and release radii to prevent snap-edge flicker.
6. Hide and dispose the arrow on cancellation, phase change, item invalidation, target death, target disconnect, scene exit, or successful confirmation.
7. Keep the arrow below persistent HUD controls and above the world presentation.

### Phase 2 — Selection and cancellation contract

1. Add a cancellation operation to `ILocalPlayerCommands` and `LocalPlayerCommandAdapter` that calls the existing owner-only server RPC.
2. Preserve server checks for owner, PrepPhase, Ready state, and existing selection.
3. Distinguish unconfirmed aiming cancellation from confirmed selection cancellation.
4. Re-resolve the selected item by item identity after inventory rebuild or compaction.
5. Keep this targeting/cancellation interaction Multi-only; preserve the existing 1v1 item-selection flow.

### Phase 3 — Multi layout and design synchronization

1. Keep seat identity separate from client-local presentation slots.
2. Tune west/north/east target anchors and inventory spacing against the arrow path.
3. Apply the approved D3 icebox result without moving unrelated scene objects.
4. Update `GAME_DESIGN.md`, `DESIGN_QUESTIONS.md`, and the Google Sheet only after runtime evidence matches the approved behavior.
5. Correct the Sheet completion states for implemented Multi balance and outcome rules.

### Phase 4 — Existing presentation stability work

Complete the remaining implementation in `PLAN_031_multiplayer_presentation_stability.md` in this order:

1. Action-aware presentation timeout policy and forced-settlement reconciliation.
2. Explicit local FPS versus remote world-Animator routing.
3. Loading state/UI ownership separation for Duel and Multi.
4. Image-based initiative marker.
5. Repair mojibake status strings without changing gameplay behavior.

### Phase 5 — Death and ghost alignment

1. Preserve the current immediate freeze/break timing for normal item deaths, delayed effects, disconnect-forced ghost transitions, and ghost-caused deaths.
2. Replace the current 40%-alpha character result with the approved temporary square ghost placeholder, without changing authoritative life state or timing.
3. Retain Frost Strike and Chill Aura without mixing item inventory state with ghost cooldown state.
4. Preserve living-only ghost targets, one active debuff per target, last-source kill credit, and round-end evaluation.

## Validation plan

### Deterministic tests

- North snapped target produces a straight path.
- West and east targets produce opposite, strongly curved paths.
- Free pointer tracking follows the pointer without committing an action.
- Enter radius snaps and wider release radius unsnaps without flicker.
- Invalid/self/dead/ghost targets cannot snap or confirm.
- Phase change, item removal, target disconnect, and local destruction clear the arrow.
- Confirm sends exactly one target intent; cancellation sends exactly one cancellation request.
- Ready prevents cancellation.
- Stable seats map to unique presentation slots for three and four players.

### Unity/runtime validation

1. Compile with zero new errors and run all EditMode tests.
2. Run a visible Host plus three-client Development Build.
3. Capture each client aiming at west, north, and east targets.
4. Confirm north is straight and west/east curves reverse direction.
5. Confirm snap, unsnap, target click, re-click cancellation, death, and disconnect cleanup.
6. Confirm every client agrees on selected actor seat, target seat, action sequence, temperatures, life states, and kill scores.
7. Run a four-action turn and require no normal presentation timeout or late ACK.
8. Run 1v1 Lobby → GameScene and a defense-counter turn to prove the Multi-only target presentation did not change 1v1 behavior.
9. Validate the approved D3 and D4 visually from all affected client perspectives.

## Acceptance criteria

- The targeting interaction matches the approved web prototype behavior.
- The arrow never changes authoritative state before explicit confirmation.
- Front targeting is straight; side targeting is visibly curved and directionally correct.
- Snap state is stable at the boundary and clears on all lifecycle exits.
- Re-click cancellation follows D2 and remains server validated.
- Multi layout, icebox placement, death visual, and ghost action model match the recorded decisions.
- The Google Sheet and repository design documents no longer contradict the verified runtime.
- Normal four-client turns do not time out during presentation.
- Existing 1v1 behavior remains unchanged outside the shared fixes already authorized by PLAN 031.

## Out of scope

- New ghost artwork.
- Combat balance changes.
- Item effect or target-policy changes.
- Server seat remapping.
- New 1v1 targeting interaction.
- Scene-object movement unrelated to the approved D3 icebox result.
