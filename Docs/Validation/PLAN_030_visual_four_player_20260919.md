# PLAN 030 four-player visual debug result — 2026-09-19

## Verdict

The visible loopback smoke match completed successfully, and all four players agreed on authoritative state at every completed presentation checkpoint. The first four-action presentation exceeded the server barrier estimate, so the overall gameplay run passed while presentation timing has a high-priority defect.

## Reproduction

- Development Build: `C:/Users/paek6/AppData/Local/Temp/AZVisual4P_Current/Player/AbsoluteZeroVisual4P.exe`
- Final evidence: `C:/Users/paek6/AppData/Local/Temp/AZVisual4P_20260919/Run4/`
- Seed: `29031`
- Topology: direct loopback Host plus three clients, four visible 960×540 windows
- Result: four exit codes `0`; `checkpoints_synchronized=true`; `capture_complete=true`
- Artifacts: `report.json`, `gallery.html`, four logs, and 36 screenshots

## Player flow

| Turn | Seat 0 — Aggressor | Seat 1 — Counter | Seat 2 — Support | Seat 3 — Opportunist |
|---|---|---|---|---|
| 1 | Windbreaker → self | Ice Cream → 0 | Cat → 0 | Cat → 0 |
| 2 | Fan → 2 | Fan → 0 | Warm Tea → self | Hand Fan → 0 |
| 3 | Fan → 3 | Fan → 0 | Windbreaker → self | Fan → 0 |
| 4 | Fan → 3 | Windbreaker → self | Fan → 3 | Fan → 0 |

The run used the generated initial inventory. The seed makes the inventory and role-weighted choices reproducible. Mini-game items were excluded when a normal item was available because mini-game input has separate focused tests.

## Synchronized checkpoints

| Sequence | Temperatures P0/P1/P2/P3 | Life state | Kills | Result |
|---|---|---|---|---|
| 1 | 31 / 36 / 34 / 34 | all Alive | 0 / 0 / 0 / 0 | identical on 4 clients |
| 2 | 25 / 35 / 37 / 31 | all Alive | 0 / 0 / 0 / 0 | identical on 4 clients |
| 3 | 18 / 34 / 36 / 25 | all Alive | 0 / 0 / 0 / 0 | identical on 4 clients |
| 4 | 14 / 33 / 33 / 16 | all Alive | 0 / 0 / 0 / 0 | identical on 4 clients |

All four Presentation Settled callbacks occurred on all four players. No process exception, null reference, identity binding failure, or transport disconnect occurred during the active scenario.

## Findings

### F-030-01 — High: first four-action presentation exceeded the barrier timeout

- Trigger: Turn 1 contained four actions, including self defense and three target actions.
- Expected: the server waits for all four presentation ACKs before continuing.
- Observed: sequence 1 timed out after 11 seconds with four pending clients. All four ACKs then arrived and were ignored because the barrier was already `TimedOut`.
- Code: `TurnManager` estimates `2 + EventCount × 3 + deathCount × 2`. The observed visual sequence took longer than the estimate before any client acknowledged.
- Risk: the server may start the next phase while one or more clients are still presenting the previous turn. Later sequences in this run completed normally, so this run demonstrates the race rather than a permanent desync.
- Evidence: `Run4/host.log`, markers `[PresentationBarrier] Timeout after 11s`, followed by four `ACK ignored: not waiting` lines.

### F-030-02 — Medium: scene loading overlay references are not wired

- `SceneLoadSyncManager` logged `overlayRoot is not assigned` on all four players.
- Both `GameScene.unity` and `GameScene_Multi.unity` serialize `overlayRoot` as file ID `0`.
- Network loading still completed in this run, but this component cannot show its own progress/fade overlay.

### F-030-03 — Low: crown glyph is missing from the active TMP font/fallbacks

- The `♛` character used by `GameHudBuilder` was replaced with a square in every turn on every player.
- The warning names `LiberationSans SDF`; the current fallback chain does not contain U+265B.
- Gameplay and synchronization are unaffected.

### F-030-04 — Low diagnostic noise: owner world animator is null

- Each player's own action emitted `_animator is NULL` on that owning client.
- The three remote visual slots bound successfully to `playerA` Animator controllers, and the screenshots show synchronized sprite/effect state.
- This does not prove remote motion is missing. It shows that the owner/FPS route still calls the world-character animation method and emits a warning when its local `AZPlayerVisual` has no remote-slot Animator.

## Visual inspection

- Captures are valid rendered Game Views, not black frames.
- The four-seat table, three remote characters, local inventory, phase label, temperature bars, clock, items, and debug overlay are visible at 960×540.
- Each client shows its own local inventory while the authoritative world state remains equal.
- Windbreaker selection and defensive character sprites are visible in the settled captures.
- Static settled screenshots cannot prove animation smoothness, exact hit timing, audio timing, or transient camera shake. Those require live observation or video capture.

## Evidence limits

This run bypasses Lobby and Relay and therefore does not validate Unity Services, NAT traversal, or the production room-to-game transition. It is a local Development Build smoke match for NGO authority, replication, presentation completion, camera framing, and capture tooling.
