# PLAN_029 AI validation matrix

## Mask blocked-food presentation repair — 2026-09-15

- Duel now suppresses the later feed/ingestion sequence when server impact flags report defense with no damage or recovery. Duel and Multi share the same full-block predicate; ordinary and partial-hit behavior remains eligible for its existing presentation.
- Blocked debuffs no longer trigger the pre-action hit-screen effect. Missing attacker animation / zero visual hit count still permits a defense reaction and cleanup.
- Mask consumption, Food filter, turn lifetime, item balance and existing remote `mask` -> `defence` animation fallback are unchanged. No dedicated art or animation asset was created.
- Validation: solution build zero errors/four existing warnings; newly compiled Unity EditMode suite **39/39 passed**. Added five network-impact classification cases and three configured asset checks (Ice Cream, Iced Americano, Samgyetang), including no new delayed effects when blocked. XML: `C:/Users/paek6/AppData/Local/Temp/az-mask-final.xml`. Initial run before automatic assembly reload contained only the prior 31 tests and is not the final evidence. Test wrapper warnings are intentional existing timeout/forced-settlement fixtures; saved final test result is Passed.
- Remaining: actual Host/Client visual replay for each food, blocked/unblocked, both initiative orders. These tests validate flags and resolver outputs, not rendered animation timing or real Relay playback.

## Counter-defense presentation repair — 2026-09-15

- User-observed: local attack did not visibly show the remote defender; local defense could restart three times against Fan.
- Fixed shared reaction preparation: the remote target now receives its defense item sprite and activates the previously hidden item object before the defense trigger. Owner FPS presentation remains routed only to the owning player.
- Fixed repeated reactions: Fan's three visual impacts no longer restart defense or its sound on every hit; defense starts on the first impact in both Duel and Multi.
- Fixed partial-defense animation interruption: damage flash can preserve the current combat animation, suppressing both `damage` and the delayed `end` trigger while retaining damage feedback. Defense cleanup remains owned by the attack sequence.
- Duel defense selections no longer receive a standalone item sequence, attacker-camera notification or between-item pause. Multi already omits standalone defense intents without a main-effect event. Attack duration remains unchanged; no extra defense wait is added.
- Authority: existing server result flags, defense item IDs and seat lookup remain the source of presentation. No new client-side defense decision or RPC was introduced. Fan damage 3 / Windbreaker block 2 are unchanged.
- Validation: solution build zero errors/four existing warnings. Unity EditMode **31/31 passed**, including two Duel metadata serialization directions and remote hidden-item preparation/cleanup. XML: `C:/Users/paek6/AppData/Local/Temp/az-defense-regression.xml`. The synchronous tool wrapper reported warnings from existing intentional timeout/forced-settlement tests; the saved NUnit result is Passed with zero failures.
- Pending visual validation: Host attacks/Client defends and reverse, each initiative order, first and repeated attacks, partial/full block, no attack, and Multi remote targets. EditMode results do not prove final rendered motion or real-time client synchronization.

## Production lobby entry repair — 2026-09-15

- Observed in the real Editor/MPPM lobby path: Host loaded GameScene with only one connected client; NGO rejected the remote request as `Incomplete connection request message given config`. The client returned from InGame to Ready without loading GameScene.
- Cause: LobbyScene defaults ConnectionApproval to false. Host registration sets it true, but NgoNetworkRuntime.StartClient previously left it false, omitting the approval payload expected by the host.
- Repair: enable approval before starting the client. Both entry paths now wait for local NGO connection and the expected loaded active scene before reporting InGame. A bounded 60-second entry timeout or disconnection invokes session cleanup and returns a failure. Superseded entry waits do not shut down newer sessions. Lobby Ready/Failed states restore status and join controls.
- Validation: generated solution build passed (zero errors, four existing warnings); changed-source whitespace check passed. Installed NGO source confirms client IsConnectedClient is set after initial scene synchronization. Console inspection retained existing AI bridge and Relay allocation/inactivity errors during Play Mode exit; no clean runtime-console claim is made.
- **Pending:** repeat a real 1v1 Lobby/Relay join with both editors using the changed code, then check refusal/timeout/cancellation and Multi entry. The earlier 42 selected cases bypassed this production startup path and do not certify this repair.

Scope: executable local host/client scenarios, focused deterministic tests, and static tracing of the previously listed AI-verifiable TODOs. Preserve existing dirty files, rules, scene layouts, and art. Development-only fixtures may seed state, but must identify seeded values and drive player actions through the owning client's RPCs. No internet-service or visual-appearance claims from loopback logs.

Execution plan:
1. Expand opt-in probes for 3/4-seat winners, ghost win, disconnect at phase boundaries, startup failure, special items, and 1v1 rounds/rematch.
2. Run focused tests for simultaneous crossings, stale/absent acknowledgements, inventory identity, filtering/top-up, and invalid targets.
3. Compare stable per-sequence states across clients; verify terminal sequence, result completion, errors, and exit codes.
4. Trace uncovered grant, RPC, lifecycle, and presentation paths. Record observed failures and unexecuted cases explicitly.

Results are recorded below as runs finish. Existing 16-test and Run5 results are historical baselines, not new evidence for this matrix.

## Current follow-up — 2026-09-15

The user authorized available automated testing and repairs. KI-007 and KI-008 now have implemented corrections and passing focused regressions. Expanded results are selected in [PLAN_029_followup_results.json](PLAN_029_followup_results.json); the original 21-case results below remain historical evidence, not the current failure count.

**Final selected follow-up results: 42 passed / 0 failed / 0 pending. EditMode: 28/28 passed.** These are the selected bounded scenario configurations below, not a percentage of total project completion or certification of all remaining TODOs. The earlier production Host-exit/lobby-return result remains inconclusive and is not counted as a new pass. All validation player processes have exited.

| Selected scenario group | Count | Result |
|---|---:|---|
| Normal winner seats, 3/4 players | 7 | Pass |
| Ghost winner, all four seats | 4 | Pass |
| Special actions, inventory in both modes, RPC guards, services, Multi reset | 6 | Pass |
| 1v1 rematch and three consecutive rematches in the same processes | 2 | Pass |
| Initialization failure UI creation | 1 | Pass |
| Joint periodic winner and first individual delayed-effect winner | 2 | Pass |
| Prep/Attack abrupt exit, each remote seat 1/2/3 | 6 | Pass |
| Final presentation exit, post-exit 70-second health, no-exit 70-second control | 3 | Pass |
| Minigame actor/target invalidation and failure after invalidation | 3 | Pass |
| All winning seats under 100 ms one-way delay / 3% configured random loss | 7 | Pass |
| 20-second upstream hold, timeout and late ACK rejection | 1 | Pass |

### Implemented corrections

1. **KI-007: UDP receive-buffer exhaustion.** Embedded the existing Unity Transport **2.7.2**, without a version/dependency upgrade, and release buffers for completed failed or empty/invalid receives. A plain NetworkDriver regression fails before and passes after the repair. Direct four-process preparation/attack/final-presentation abrupt-exit checks now pass. [Source delta, provenance and maintenance](PLAN_029_transport_hotfix.md).
2. **KI-008: ordinary item actor/target eligibility.** `PlayerState` requires a spawned, connected, alive actor and target. This also applies before accepting a minigame result; invalidated games clear pending state and cannot queue or consume another action. The explicit ghost skill path and 1v1 target default remain intact. Remote tests cover ghost attack/self-defense rejection, invalid/self targets, valid selection/cancellation, actor/target death during minigames, duplicate result rejection, and valid retry.

### Follow-up checklist

- [x] Reproduce and repair ordinary ghost-target/actor selection.
- [x] Reproduce UDP receive exhaustion independently of gameplay; verify **28/28 EditMode tests** after repair.
- [x] Build development Windows players with zero errors. Generated solution build: zero errors, four existing warnings. One full rebuild also emitted warnings from unchanged inference shaders; these are not runtime test failures.
- [x] Re-run normal winner seats in 3/4 players, inventory/threshold rules, special actions, service checks and Multi round reset.
- [x] Exercise all four ghost winner seats with real owner RPCs.
- [x] Verify minigame actor/target invalidation, failed result after target invalidation, duplicate results and normal retry.
- [x] Verify direct UDP abrupt exit during Prep/Attack/final presentation and 70-second remaining-client connection health for seat 3.
- [x] Verify real same-periodic-tick **joint winner mask 3**, including one final result per client.
- [x] Hold remote upstream traffic for **20 seconds**: server presentation timeout, late ACK rejection, subsequent synchronized combat and final result all pass.
- [x] Re-run 1v1 two-round/rematch and startup-failure UI creation using the transport-fixed player.
- [x] Verify all 3/4-player winning seats under **100 ms one-way delay and 3% packet loss** using an explicitly isolated delivery fixture.
- [x] Verify individual delayed-effect victory and cancellation of the next due lethal effect across all clients (also under delay/loss).
- [x] Verify abrupt exit of each remote seat **1, 2 and 3** during both Prep and Attack; remaining clients finish combat and one final result.
- [x] Complete three rematches in the same two processes, checking reset scores and one TurnManager/MatchCompositionRoot with exactly two spawned players at each new match.

### Evidence boundaries and test corrections

- `FollowupRegression` uses the corrected PlayerState with the original transport; `Fixed*` players include the embedded transport repair. The latter additionally exercise transport-sensitive lifecycle, minigame, ghost, and terminal paths.
- The normal no-exit control and proxy-isolated abrupt-exit control both survive 70 seconds. Native ICMP error details were not captured; the source-level buffer leak and before/after driver test are direct evidence.
- Minigame clients initially exited before observers could finish. The actor now stays until observers record results; superseded runs are excluded, not counted as gameplay failures.
- The first delayed-effect test incorrectly assumed FIFO registration order. The existing scheduler in both modes processes the pending list in reverse. The test now expects the first **processed** effect's owner to win and verifies the later pending lethal effect does not run. Gameplay scheduling order was not changed.
- An earlier loss run did not guarantee the seeded winner survived competing actions/natural deaths. The isolated delivery fixture uses defending peers and a one-degree non-fanning victim. Its previous six-degree per-turn reset prevented a three-damage Fan from finishing and was corrected. These fixture changes do not modify item SOs or production rules. All superseded attempts remain listed in the follow-up JSON.
- The first seat-1 attack-disconnect fixture kept connections and 19 combat checkpoints synchronized but its seeded winner died before five kills. Final seat-1/2 repetitions set the finishing victim to one degree with its fan off at Prep after disconnect recovery. This isolates continued combat/result delivery; it is not evidence that the original competing-action scenario must produce the seeded winner. The superseded seat-2 runner was also stopped and repeated with the same corrected fixture.
- The UDP proxy is test-only, localhost-bound, records actual forwarded/dropped packets, and never substitutes for production Lobby/Relay verification.
- Camera/HUD/animation/audio appearance, full player input, production Lobby/Relay, reconnect and long-duration leak behavior remain unverified. No scene, prefab, art, or balance asset was edited in this follow-up.

## Original results — 2026-09-15 (before follow-up repairs)

**21 distinct runtime cases: 17 pass, 3 fail, 1 inconclusive for the production session path.** The three failed cases represent two findings (disconnect reliability and ghost target validation), not three unrelated bugs. Focused EditMode tests: **26 passed / 0 failed**. Development player and generated-solution builds completed with zero errors; four existing solution warnings remain. No production gameplay source, balance asset, or scene layout was changed in this validation task.

Machine-readable selected evidence: [PLAN_029_ai_matrix_results.json](PLAN_029_ai_matrix_results.json). Raw logs and per-process exit codes are under `C:/Users/paek6/AppData/Local/Temp/AZMatrix_20260915/`.

| Runtime case | Count | Result | Evidence / limit |
|---|---:|---|---|
| 3-seat normal win, each winner seat | 3 | Pass | Two combat checkpoints agree across all three processes; one final presenter completion per client |
| 4-seat normal win, each winner seat | 4 | Pass | Same check on all four processes; ClientId arrival order varied independently of log filename |
| Ghost wins from seats 0 and 2 | 2 | Pass | Seeded ghost/four-kill state; owner sends Frost RPC; fifth kill and deciding death presentation complete |
| Cat, Hug, Ice Cream | 1 | Pass | Three actual main actions, synchronized checkpoints; Hug minigame success is simulated by the owning client's result RPC |
| 1v1 rounds and rematch | 1 | Pass | Two won rounds, both clients accept rematch, new round starts with zero wins |
| Disconnect during prep / attack | 2 | **Fail** | Remaining clients later disconnect; no intended terminal result. Prep case also reproduced with only one case running |
| Disconnect during final presentation | 1 | Pass | Remaining clients settle and complete the same fixed winner result |
| Host exit / return to lobby | 1 | **Inconclusive** | Local direct-transport fixture does not enter the production coordinator/Relay session state; clients stop networking but do not satisfy lobby-return assertion |
| Missing fourth participant | 1 | Pass | Three joined processes observe bounded initialization failure and the failure UI GameObject |
| Inventory assertions, Multi and 1v1 | 2 | Pass | Live server inventory APIs: preserved copies/uses, one-slot top-up, three-copy cap, full-capacity no-op, steal/reroll cancellation, compaction preservation |
| Invalid RPC targets | 1 | **Fail** | Out-of-range and self targets rejected; registered ghost target accepted |
| Ghost/death service assertions | 1 | Pass | Live server APIs: Frost damage/cooldown, duplicate rejection, Chill modifiers/expiry, natural/self no-score, ghost credit, death mask once, idempotent Dispose |
| Multi second-round reset | 1 | Pass | Seeded natural deaths; all four players return alive at 37 degrees, basic inventory restored, selections/readiness cleared, cooldowns empty, kills unchanged |

The inventory Multi case also tests the **actual configured** filtered drop table, 30/20/10 threshold grants, and repeated-threshold idempotence. The 1v1 case tests shared inventory behavior; applying the top-up API in a fixture is not evidence that 1v1 gameplay now calls that Multi-only rule.

## Original findings (repair status above)

### F1 — High: abrupt client exit can lead to remaining clients timing out

Reproduction: four loopback development players; terminate the process whose replicated local seat is 3 during PrepPhase or AttackPhase. Host later records `ProtocolTimeout` for the terminated client **and the two still-running remote clients**; those clients record `ClosedByRemote` and stop in GameScene_Multi. The original long run then repeatedly ends/restarts rounds on the remaining Host. No managed exception explains the initial disconnect chain.

- Reproduced in Batch1, Boundary2, and the isolated Final/disconnect-prep-4-w0 run.
- Configured heartbeat is 500 ms and disconnect timeout 30000 ms in LobbyScene. Do not silently change these values to hide the failure.
- The low-temperature finishing-attack fixture can die naturally while waiting for the missing client; therefore failing to reach the seeded five-kill result alone would not prove a defect. The unexpected loss of the **remaining** connections is the material observation.
- Exact ownership of the timeout cause is not established. Investigate transport send/receive activity and callbacks around the first disconnect, then review the single-survivor round loop. The affected inspection routes are `MatchRoster.OnPlayerDisconnected`, `TurnManager` phase/round boundaries, `SessionManager`, and UnityTransport lifecycle.
- This is loopback evidence. Reproduce through the real Lobby/Relay entry path before claiming identical production behavior or selecting a fix.

### F2 — Medium: general item selection accepts a ghost target

Reproduction: seat 3 is a replicated Ghost; living seat 1 submits normal Fan selection targeting seat 3. `HasSelectedItem` becomes true. The probe fails with `server accepted invalid target 3`.

`PlayerState.TryResolveTargetSeat` checks self-target and registry membership but does not require the target to be Alive/connected. `CombatResolver.ResolveMultiAction` rejects the ghost target later, so this result does **not** demonstrate damage being applied to a ghost; it demonstrates a wasted/locked selection until cancellation or phase reset.

Correction direction: validate ordinary actor/target eligibility on the server before accepting selection, and use the same eligibility check after minigame completion. Preserve explicit ghost-skill RPC eligibility and existing 1v1 target defaults. Re-run rpc-guards after the fix.

## Deterministic checks and static trace

- 26 EditMode tests include all distinct actor/target pairs for 3/4 seats, unchanged source snapshots, invalid actor/slot/target rejection, ordinary ghost actor/target rejection, all nonempty winner subsets for 3/4 players, no repeat threshold crossings, barrier timeout/unknown/duplicate/stale ACK behavior, camera restoration, defense metadata, inventory identity and NGO serialization.
- Multi initial grants call `ItemManager.GetRuleAwareDropTable`; thresholds use `TurnManager.GetDropTable`; next-round grants use that same rule-aware accessor; deathmatch top-up uses the rule-aware table. These call-site traces complement the live table/threshold checks. Every random outcome is not exhaustively enumerated.
- RPC ownership/phase guards are present for selection, ready, minigame result and presentation ACK. The target-life gap above remains; static guards are not a substitute for all malicious-client runtime cases.
- Existing unsubscribe/despawn paths were inspected. Multi second-round and 1v1 rematch pass, but reconnect/late-spawn and long-duration leak behavior are not certified.

## Test-harness corrections and evidence exclusions

- The first special-item run could finish three turns after Kids removed Ice Cream; it is excluded as a full special-item pass. Boundary2 replenishes only a missing required test item at PrepPhase and requires all three actual combat actions in the host log. No SO or production environment rule is modified.
- The first Boundary attempt raced log-file creation. The runner now waits safely for missing logs; that attempt is excluded from gameplay results.
- The runner selects an intentionally terminated client by replicated seat, not by process filename, records fault injection, stops only its own processes, and exits early on unexpected network stop/failure. No validation player remains running.
- Tests use independently running players, not fake in-memory network clients. Inventory/service fixtures are explicitly direct server API assertions; they are not full item-input or cinematic tests.

## Remaining TODOs and limitations

- [x] Resolve F1 in the tested Windows UDP path; confirm direct abrupt-exit recovery.
- [ ] Verify graceful leave, reconnect, and presentation interruption through the production session path.
- [x] Resolve F2 and exercise actor/target invalidation during minigames, duplicate results and valid retry.
- [ ] Extend stale inventory-intent and every malicious-client permutation beyond the covered RPC cases.
- [x] Exercise real cross-process missing-until-timeout/late ACK behavior with a 20-second upstream hold.
- [x] Finish selected real latency/loss cases with packet counters and per-client state comparisons.
- [x] Finish all remote-seat Prep/Attack disconnect repeats.
- [x] Exercise same-tick joint winners through the actual periodic-death and network presentation path.
- [x] Complete the corrected individual delayed-effect terminal scenario. Every other damage-route combination remains unverified.
- [ ] Test repeated production lobby-return/new-match sessions and profile long-duration object/event cleanup. Three same-process rematches pass, but this bounded check does not establish leak freedom or exercise Lobby/Relay transitions.
- [ ] Verify failure button interaction, full minigame input, all item/ghost combinations and all ghost winner seats. Current failure UI evidence proves creation, not clicks or rendered text.
- [ ] Capture visible rendering to verify camera framing, HUD layout, animation timing/appearance, and audio. Prior hidden-window captures were black; logs and camera-state tests do not replace this.
- [ ] Validate real Lobby/Relay and different PCs. The Host-exit lobby result remains inconclusive because the local fixture intentionally bypasses that session setup.

Windows reproduction example:

```powershell
python .codex/scripts/run_validation_matrix.py <development-player.exe> <new-output-directory> --filter disconnect-prep --workers 1 --port-base 18300
```

Probe activation is restricted to Editor/development builds and explicit `--az-matrix` arguments. Test-only startup and seeded state are not enabled in ordinary launches.
