# PLAN_029: four-process scenario validation — 2026-09-15

## Latest result: Run5 after corrections

**Network scenario PASS; rendered visual verification OPEN.** One real host plus three independent loopback clients completed two combat sequences, the five-kill decision, presentation settlement, and result-presenter completion. All four exited with code 0. Both sequence checkpoints and the first complete released terminal snapshots match across all clients. This is a seeded late-match scenario, not a full match from zero kills or a Relay/internet test.

### Corrections

- Wait for MatchNetworkState to be spawned with server authority before bootstrap; retry pending initialization and report actual completion.
- Make ServerInitialize idempotent: repeated matching configuration preserves scores and the terminal record. Live probe assertions passed before combat and after final release.
- Bound configuration/participant discovery to 30 seconds. Report configuration/roster failures through a replicated initialization error and offer a lobby-return UI. Failure-button interaction is not yet exercised live.
- Separate identity readiness from the five-second visual-object binding window. Run5 assigned seats 0–3 and bound all 12 remote visuals without timeout.
- Use actual NGO connected-client count for scene-load bookkeeping; the local probe now reports 4 clients.
- Safely cancel result UI after its Canvas or coroutine host is destroyed. Run4 reproduced NullReferenceException in RoundResultPresenter.CancelCinematic on all four processes; Run5 does not reproduce it.
- Add bounded subprocess orchestration, unique run token, exit codes, boot logs, sequence checkpoints, result completion checks, screenshots, and automatic comparison under `.codex/scripts/`.

### Run history

| Run | Outcome |
|---|---|
| Run1 | Original startup stall, detailed below |
| Run2 | Startup fixed; original 8-degree fixture died to natural fan damage before the finishing attack |
| Run3 | Harness failed because Run2 still held the port; those exact processes were subsequently stopped |
| Run4 | Fixture changed to 12 degrees; five-kill result completed; UI teardown exception exposed |
| Run5 | Same fixture plus teardown fix; protocol comparison passes; no managed exception or Debug.LogError entries |

Run5 begins with seat 0 at four kills and seat 3 at 12 degrees. First combat executes Fan/Windbreaker actions. In the second combat, seat 0's Fan kills seat 3, immediately fixes score 5 and winner mask 1 with deciding sequence 2, and cancels later actions. Cat was selected but does **not** execute after the terminal action; this run does not validate Cat presentation. Each client settles sequences 1 and 2 exactly once and reports final result presentation once.

Focused EditMode regression: 16 passed / 0 failed. Final solution build: zero errors, four existing warnings. Unity development build: succeeded with zero errors despite wrapper warnings.

Evidence: `C:/Users/paek6/AppData/Local/Temp/AZScenario_20260915/Run5/` contains four logs, `exit_codes.json`, and `comparison.json`. No scenario process remains running.

### Limits

- Captures are uniform black in both batch and ordinary hidden-window execution. `visual_capture_valid=false` is separate from `protocol_passed=true`. Presenter signals/binding logs do not prove rendered camera framing, animation appearance, or HUD placement.
- Three-player mode, 1v1 live regression, every ghost/item path, simultaneous winners, network impairment, mid-game disconnect/rejoin, second-round/rematch, and failure-UI interaction remain separate checks.
- The existing child-object DontDestroyOnLoad warning and optional resource warnings remain separate from the repaired exceptions.
- These corrections changed no gameplay decision, balance asset, scene position, or art asset.

## Original Run1 result (historical)

**FAIL / blocked before gameplay.** A Windows development player was built and run as one host plus three independent clients over loopback Unity Transport. All four connected and every process registered four pending player objects. No process reached an assigned seat or PrepPhase. This is a real network-process test, not an EditMode simulation. It is not an internet/Relay test.

## Planned scenario

1. Load LobbyScene, connect four processes on loopback port 17849, then use NGO to load GameScene_Multi.
2. After all seats are assigned and PrepPhase starts, seed a reproducible late-match fixture once: seat 0 has four kills, seat 3 has eight degrees. All other state/rules remain unchanged.
3. Turn 1: submit an invalid slot/target request, then seat 0 attacks seat 3 with Fan; seat 1 uses Windbreaker; seats 2 and 3 attack seat 1 with Fan.
4. Following turns: seat 0 continues attacking seat 3; seat 1 uses Cat on seat 2 if available; other players continue using Fan. Submit ready through each owning client's normal RPC.
5. Compare per-client temperature/inventory/kill/phase snapshots and the retained terminal result. Expect winner mask 1, finish the deciding presentation, then exit each test process.

Steps 2–5 were **not reached**. In particular, the seeded fixture, invalid-request rejection, defense, Cat, five-kill decision, VFX, and result screen have not passed this scenario.

## Observations

| Process | Pending player registrations | Visual binding timeouts | Finished scenario |
|---|---:|---:|---|
| Host | 4 | 3 | No |
| Client 1 | 4 | 3 | No |
| Client 2 | 4 | 3 | No |
| Client 3 | 4 | 3 | No |

- Host logged `HOST_LISTENING` and `FOUR_CONNECTED`.
- Host logged `ServerBootstrapMatch — Mode=Multi, Players=4`, but never logged `MatchNetworkState` server initialization or assembled match configuration.
- Every remote visual timed out after five seconds with `seat=-1`: 12 timeout errors across four processes.
- No managed exception header was found in the four player logs. The test still failed because initialization did not progress.
- Scene-load overlay bookkeeping logged `Waiting for 1 clients to load` followed by `1/1` even with four connected processes. Its source defaults to one when no Lobby service room exists, as in this local probe. This is a local-test-path limitation, not evidence that three clients failed NGO scene loading or that the normal Lobby path uses the wrong count.

## Source-level diagnosis

The strongest explanation is an initialization-order race: `TurnManager.WaitForPlayersRoutine` invokes `ServerBootstrapMatch` once, then waits indefinitely for `ActiveConfig`. `MatchNetworkState.ServerInitialize` returns immediately if its NetworkBehaviour has not acquired server authority yet. Because it is a separate scene NetworkObject, it can spawn after TurnManager. The caller logs the attempt as if bootstrap succeeded and never retries. The Multi scene was inspected through an isolated Editor preview: MatchNetworkState is active and has a NetworkObject, so a missing component is not the explanation.

The logs demonstrate the stalled bootstrap and unassigned seats. The exact IsServer/IsSpawned values at the original bootstrap call were not instrumented, so the spawn-order cause remains a strongly supported diagnosis rather than a directly captured value.

Recommended correction: coordinate initialization with MatchNetworkState's network-spawn readiness, make bootstrap completion explicit/idempotent, and begin visual binding timeout only once seat identity is ready. Re-run this unchanged scenario after correction. Do not bypass the wait or force seat values in the test to claim success.

## Artifacts and scope

- Probe: `Assets/Scripts/Core/Solo/MultiScenarioProbe.cs`, compiled only for Editor/development builds and activated only with `--az-scenario host|client --az-run <run-id>`.
- Build: `C:/Users/paek6/AppData/Local/Temp/AZScenario_20260915/Player/AbsoluteZeroScenario.exe`.
- Raw logs: `C:/Users/paek6/AppData/Local/Temp/AZScenario_20260915/Run1/{host,client1,client2,client3}.log`.
- Per-process options: `-batchmode -force-d3d11 -screen-width 960 -screen-height 540 -logFile <unique-path>`. No `-nographics` option was used; no gameplay screenshots were captured because gameplay was never reached.
- Build completed with zero errors. Existing C# warnings and package shader warnings caused the MCP wrapper to report an error despite the underlying build reporting success. An initial probe-only NetworkList LINQ compilation mistake was fixed before this successful build/run.
- The four launched process IDs were recorded in Run1/pids.txt; they were stopped after reproducing the initialization stall. No unrelated Unity processes were stopped.
- No gameplay rule, scene asset, or production initialization code was changed during this validation. The earlier 16 passing EditMode cases do not cover this multi-process startup path.
