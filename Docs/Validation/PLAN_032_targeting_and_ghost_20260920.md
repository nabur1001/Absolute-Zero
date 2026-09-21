# PLAN 032 targeting and ghost validation — 2026-09-20

## Verdict

The approved D1-D5 behavior is implemented. A visible loopback Host plus three-client Development Build completed both the ghost showcase and a normal one-turn match with synchronized authoritative checkpoints, complete screenshot sets, zero process errors, and zero presentation timeouts.

## Implementation result

- Multi single-target items use click-to-aim, pointer tracking, target snapping, and explicit click confirmation.
- North uses a straight arrow. West/east use opposite strong curves in the presenter implementation.
- Local aiming can be cancelled by same-item click, Escape, or right-click. A confirmed choice can be cancelled by re-click before Ready through the existing owner-only server RPC; Ready rejects cancellation.
- Multi presents the local icebox at south-center behind the item row and moves both its spawn anchor and an already-created instance.
- Death presentation freezes, breaks immediately, hides the former character, and displays the temporary cyan square placeholder.
- Frost Strike and Chill Aura remain authoritative ghost skills. Their replicated event now drives a client-side projectile and impact presentation.

## Validation runs

### Ghost showcase

- Evidence: `Docs/Validation/Artifacts/PLAN_032_ghost_showcase_final_20260920/`
- Seed: `32032`
- Topology: direct loopback Host plus three clients, four visible 960×540 windows
- Result: `passed=true`, `checkpoints_synchronized=true`, `capture_complete=true`
- Every process exited with code `0` and received the same two ghost-skill events.
- Every perspective captured freeze, placeholder, Frost motion/impact, Chill motion/impact, settled state, and final state.
- Authoritative checkpoint on all clients: seat 1 Ghost at 0°, seats 0/2/3 Alive at 37°, kills `0/0/0/0`.

### Normal one-turn targeting flow

- Evidence: `Docs/Validation/Artifacts/PLAN_032_targeting_directions_final_20260920/`
- Seed: `32033`
- Result: `passed=true`, `checkpoints_synchronized=true`, `capture_complete=true`
- All four players selected, readied, presented, and settled sequence 1.
- Each client captured west, north, and east targeting from its own perspective. West/east bend in opposite directions and north remains straight.
- All four clients agreed on temperatures `31/35/37/34`, life states, Ready state, selected state, and kill scores.
- No presentation timeout or gameplay exception occurred.

## Build and tests

- Development Build `build_3921be89d809`: succeeded, 0 errors, 3 existing compiler warnings.
- Unity EditMode: 39 total, 39 passed, 0 failed, 0 skipped.
- Unity console ground truth after compilation: `compilationFailed=false`, `compiling=false`, `consoleErrors=0`.

## Remaining observations

- The normal run still records the existing owner-world-Animator warning, unsupported crown glyph warning, and unassigned loading overlay warning tracked by PLAN 031.
- Automated evidence exercised all three snapped arrow directions and cancelled each local debug aim before continuing the authoritative turn. Physical Escape/right-click/re-click gestures remain suitable for a short hands-on UX check because the probe invokes the same cancellation endpoint directly.
- This is a local loopback test and does not cover Relay, NAT, or separate machines.
