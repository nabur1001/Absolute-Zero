# PLAN 030 — Visible four-player debug run

## Goal

Run one reproducible local four-player Development Build match in four visible windows. Capture every player's choices, replicated state, presentation checkpoints, screenshots, and blocking errors without changing production game rules.

## Scenario

- Host and three clients connect directly over loopback using the existing NGO session path.
- One shared seed controls server-side initial random inventory and each seat's deterministic choice stream.
- Seat 0 uses an Aggressor preference, Seat 1 a Counter preference, Seat 2 a Support preference, and Seat 3 an Opportunist preference. Each preference falls back to any usable item in that player's actual inventory.
- Single-target actions select only living remote seats. Self-target actions use no target. Mini-games are reported as simulated successes because this run validates match flow and presentation rather than mini-game input UI.
- The scenario ends after the configured number of settled presentations or an authoritative terminal match result.

## Evidence

- Per-player Unity log with selected item, target, readiness, phase, temperature, life state, kills, terminal result, and presentation sequence.
- Per-player screenshots at Prep entry, every settled presentation, and final exit.
- `report.json` for automated PASS/FAIL classification.
- `gallery.html` for a visual timeline of all four players.

## Boundaries

- The probe is compiled only in the Editor or a Development Build and starts only with `--az-visual-flow 1`.
- It does not use Lobby or Relay and does not prove production internet connectivity.
- It does not judge art quality, audio quality, or player experience.
- Production gameplay code, balance assets, scenes, and the existing hidden validation matrix remain unchanged.

## Validation

1. Unity automatic compilation and console check.
2. Fresh Windows Development Build from the current worktree.
3. Visible Host plus three-client run with four successful exit codes.
4. Compare checkpoints and inspect screenshots/logs for divergence, stuck phases, missing presentation, exceptions, and rendering failures.

## Result — 2026-09-19

- Final run: **PASS** for process completion, four-seat state equality, and capture completeness.
- Four players completed four turns with seed `29031`; every process exited with code `0`.
- All four clients reported identical authoritative temperatures, life states, ready/selection states, kills, terminal state, and presentation sequences at all four checkpoints.
- Each player produced nine valid 960×540 screenshots: four Prep, four settled presentation, and one final capture.
- The run exposed one presentation barrier timeout and three presentation/wiring warnings. See `Docs/Validation/PLAN_030_visual_four_player_20260919.md`.
