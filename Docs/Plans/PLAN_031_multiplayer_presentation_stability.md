# PLAN 031 — Multiplayer presentation stability repairs

## Goal

Repair the four findings from the visible four-player run without changing combat balance, authority, action order, the three-second Multi action rule, scene object placement, or match outcomes. Shared 1v1 code may change only where local/remote animation routing and concurrent defense presentation require the same behavior.

## Evidence and confirmed causes

| Finding | Confirmed cause | Risk |
|---|---|---|
| F-030-01: first four-action presentation timed out | `TurnManager` budgets `aggregate.EventCount * 3`, while `CombatVFXManager` presents each executed actor action for at least three seconds, plus intro, pauses, configured animation time, and deaths. One event with four actor actions therefore received only 11 seconds. | The server can advance while clients still present the previous sequence. |
| F-030-02: loading overlay references are null | `SceneLoadSyncManager` owns optional UI fields that are unassigned in both game scenes, while the persistent `LoadingScreenManager` already creates and owns the actual transition UI. The latter only recognizes `GameScene` in its NGO load callback. | Duplicate ownership creates warnings and leaves Multi/client loading presentation inconsistent. |
| F-030-03: crown glyph is missing | `GameHudBuilder` creates U+265B through TMP, but the active font and fallbacks do not contain it. No dedicated crown asset exists in the project. | Repeated font warnings and a square placeholder. |
| F-030-04: owner Animator warning | Regular actor and defense paths call `AZPlayerVisual.PlayCombatAnimation` before or alongside the owner-only FPS path. The local player's `AZPlayerVisual` intentionally has no remote-slot Animator. | Expected owner behavior is reported as a fault and can start duplicate owner presentation if a world visual is later added. |

## Fixed contracts

1. The server remains the only authority for combat results, temperatures, deaths, scores, and sequence numbers.
2. Clients acknowledge a presentation sequence only after their serialized local presentation completes.
3. A defense reaction starts at the incoming attack impact. It never receives a separate three-second action interval.
4. Every executed Multi main action keeps a minimum three-second presentation. Configured or special animation time may make it longer.
5. The local player uses the FPS presentation route; remote players use scene-slot world Animators. A missing remote Animator remains a warning.
6. Loading synchronization and loading UI have separate owners: `SceneLoadSyncManager` owns readiness state; `LoadingScreenManager` owns visuals.
7. No font atlas or fallback asset is mutated at runtime to display the initiative marker.

## Implementation plan

### Phase 1 — Make presentation timing a shared policy

1. Add a small pure timing policy in the combat layer. It accepts the serialized batch and item metadata and returns a breakdown containing:
   - intro duration;
   - executed action count;
   - per-action budget `max(3 seconds, configured/special sequence duration)`;
   - pauses only between actions that will actually play;
   - death presentation budget;
   - a bounded transport/frame grace period.
2. Count executed actions from the batch action order plus `MainEffect`/`DefenseActivated` events. Do not use total event count as an action count.
3. Keep defense events inside their incoming action budget. Do not add a defense action or defense delay.
4. Use the policy at all barrier call sites:
   - Multi combat batch;
   - 1v1 combat result, preserving current visible timing;
   - standalone Multi death presentation.
5. Keep a hard upper timeout so a disconnected or broken client cannot deadlock the match. Before clearing the barrier, snapshot its pending client IDs and send an idempotent settlement command for that sequence. A client still presenting that sequence restores camera/visual state, emits its local settled event once, and clears it before accepting later presentation work; a client that already completed it ignores the command.
6. After reconciliation, let the server continue. The existing client queue remains the ordering guard for results received around the timeout boundary.
7. Extend timeout diagnostics with sequence, calculated timing breakdown, and pending client IDs. Late ACKs remain harmless and must identify the timed-out sequence.

This phase changes waiting policy only. It does not change resolution, item effects, action order, winner detection, or RPC ownership.

### Phase 2 — Route local and remote animations explicitly

1. Introduce one internal presentation helper used by both Duel and Multi regular-item paths:
   - owner actor/defender: call `FPSVisualController` only;
   - remote actor/defender: prepare its item sprite and call `AZPlayerVisual` only.
2. Apply the same routing to `PlayDefenseReaction`. Keep defense activation on the first visual hit only and clean it up when the attack sequence ends.
3. Do not suppress all null-Animator warnings. Suppress the call for the intentional local-owner case; retain a warning containing seat, item, and sequence when an expected remote slot lacks an Animator.
4. Preserve existing Cat, Feed, Hug, damage flash, camera shake, and temperature impact points. Any special sequence that intentionally moves a world transform remains on its existing path and receives an explicit owner/remote check.

This is the only shared 1v1 behavior change in this plan. Both initiative directions must continue to show attack and defense concurrently, with no separate defense wait.

### Phase 3 — Give loading state and loading UI one owner each

1. Remove the unused overlay fields and visual control methods from `SceneLoadSyncManager`. Keep its server-owned loaded count, total count, timeout, and `AllPlayersLoaded` state.
2. Keep `LoadingScreenManager` on the persistent session object as the sole transition presenter.
3. Replace the hard-coded `GameScene` check with the configured playable-scene set so both `GameScene` and `GameScene_Multi` show loading UI on every local client when NGO begins the load.
4. Dismiss only after local scene arrival and the game has left `WaitingForPlayers`; use `AllPlayersLoaded` as additional progress/readiness input when available. Preserve immediate cleanup on load failure, disconnect, and return to lobby.
5. Make `BuildUI` idempotent so duplicate host event and NGO load callbacks cannot create a second canvas.
6. Keep scene positions and existing game-scene objects unchanged. The two game scenes no longer require serialized loading-overlay references.

### Phase 4 — Replace the unsupported crown glyph

1. Replace the TMP U+265B object with a dedicated `Image` initiative marker and update `GameHudRefs`/`MatchHudPresenter` to position and toggle that image.
2. Use a small project-local crown sprite when art is available. Until then, use a deterministic UI-primitive placeholder that does not depend on a font glyph; do not alter the current TMP font assets.
3. Preserve the current initiative source (`FirstReadySeat`), color, visibility phase, and relative name-box placement.

## Validation plan

### Deterministic Editor tests

- Timing policy: zero through four executed actions, multiple effects from one action, configured duration above three seconds, defense-without-damage, repeated-hit defense, one or more deaths, and a special sequence.
- Barrier: all ACKs, duplicate/unknown/stale ACK, disconnect removal, late ACK after timeout, targeted idempotent settlement, and timeout diagnostics containing pending IDs.
- Perspective routing: local actor uses FPS only, remote actor uses world Animator only, local defense uses FPS only, remote defense prepares its item sprite and starts once.
- Loading presenter: Duel and Multi scene names, duplicate show callbacks, failure/disconnect cleanup, and one canvas maximum.
- Initiative marker: visibility and seat-relative position without unsupported TMP characters.

### Unity validation

1. Compile the current Editor state and require zero new errors.
2. Run the focused EditMode suite, then the existing combat/network regression suite.
3. Run the reproducible visible Host plus three-client scenario with:
   - four executed actions in one turn;
   - local attack against remote Windbreaker/Mask defense;
   - remote attack against local Windbreaker/Mask defense;
   - a repeated-hit attack;
   - at least one Cat, Feed, or Hug special sequence;
   - a death presentation.
4. Require every client to ACK each sequence before the calculated normal deadline, with no normal-run timeout or late-ACK message.
5. Compare authoritative temperatures, life states, kills, selected actions, and sequence IDs at every checkpoint across all four processes.
6. Capture all four views at attack start, defense impact, sequence settlement, and final state. Confirm local FPS and remote world presentations from both attacker and defender viewpoints.
7. Run production Lobby/Relay entry for 1v1 and Multi. Confirm one loading canvas per process, visible progress, transition completion, and cleanup on disconnect/timeout.
8. Run a forced stalled-client fixture. Confirm the hard timeout logs the pending client, settles that sequence once on the stalled client, releases the server, and prevents a later sequence from overlapping it.

## Acceptance criteria

- A four-action Multi turn does not time out under normal local or production-latency conditions.
- The server never waits on event count as a proxy for visible action count.
- Attack and full defense start together at impact in both initiative directions and both game modes; defense adds no standalone three-second interval.
- No owning client emits the expected `_animator is NULL` warning; a genuinely missing remote Animator is still diagnosable.
- Duel and Multi loading show exactly one overlay per client and leave no unassigned-overlay warning.
- The initiative marker renders without missing-glyph warnings and without dirtying font assets.
- Four-client state checkpoints remain identical, and all existing authority/RPC guard tests remain green.

## Change order and rollback boundaries

1. Implement and test the timing policy first; it addresses the only high-severity race.
2. Implement perspective routing and run Duel plus Multi defense regressions before touching loading/UI.
3. Consolidate loading ownership and validate real Lobby/Relay transitions.
4. Replace the crown marker last as an isolated UI change.

Each phase is independently reviewable and revertible. Scene YAML, item balance assets, and authoritative combat resolution are outside the change set.

## Readiness

The plan is implementation-ready. No gameplay decision is still open. The final crown artwork can be supplied later because the implementation uses a non-font placeholder until that asset exists.
