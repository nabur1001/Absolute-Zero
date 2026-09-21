# PLAN 033 — Live Unity Relay Four-Player Validation

## Result

**PASS** on 2026-09-20 (Asia/Seoul).

The development-player automation used the Unity Cloud project embedded in the build and completed a real four-player session through Unity Lobby and Relay:

1. Four player processes initialized Unity Services with separate authentication profiles.
2. Unity Authentication returned four distinct anonymous Player IDs.
3. The host created a real Lobby for four players.
4. Three clients joined the Lobby.
5. The host created one Relay allocation for three joining clients.
6. The host published the Relay join code through Lobby member data.
7. All three clients joined the Relay allocation using DTLS and started NGO clients.
8. NGO synchronized `GameScene_Multi`, assigned seats 0–3, and completed one automated turn.
9. Every peer reported the same authoritative turn checkpoint.

## Evidence

- Machine-readable result: [report.json](Artifacts/PLAN_033_relay_four_player_20260920_final/report.json)
- Visual gallery: [gallery.html](Artifacts/PLAN_033_relay_four_player_20260920_final/gallery.html)
- Host log: [host.log](Artifacts/PLAN_033_relay_four_player_20260920_final/host.log)
- Client logs: [client1.log](Artifacts/PLAN_033_relay_four_player_20260920_final/client1.log), [client2.log](Artifacts/PLAN_033_relay_four_player_20260920_final/client2.log), [client3.log](Artifacts/PLAN_033_relay_four_player_20260920_final/client3.log)
- Captures: 24 PNG files across host and three client perspectives.

The report's `relay_verified` check requires four unique Player IDs, one host allocation marker, three client Relay join markers, and a successful Relay connection marker from every process. The final result was:

| Check | Result |
|---|---:|
| Development build | Succeeded, 0 errors, 4 warnings |
| Distinct anonymous identities | 4 / 4 |
| Relay allocation | 1 / 1 |
| Relay client joins | 3 / 3 |
| NGO seats assigned | 4 / 4 |
| Synchronized turn checkpoints | 4 / 4 identical |
| Scenario process exits | 4 / 4 code 0 |
| Unity EditMode tests | 39 / 39 passed |
| Captures | 24 / 24 expected |

## Automation changes

- `NetworkSessionCoordinator` accepts `--az-services-profile` in Editor or Development builds so parallel players do not share one cached anonymous identity.
- `VisualFourPlayerProbe` supports the production Lobby and Relay session path with bounded operation timeouts.
- `run_visual_four_player.py --relay` launches four distinct authentication profiles, validates Relay evidence, and removes its temporary Lobby-code coordination file.
- `RelayGateway` no longer prints short-lived Relay join codes to logs.

The local coordination file is used only to tell clients which public Lobby to join. Relay allocation details still travel through the production Lobby member-data path, and all NGO gameplay traffic uses Unity Relay.

## Non-blocking visual findings

The session completed without exceptions, connection denial, or timeout. Existing presentation warnings remain visible in the logs:

- One missing Animator warning per perspective.
- Missing chess-crown glyph warnings from the current TMP fallback fonts.
- The host reports an unassigned loading overlay reference.

These warnings did not affect Relay, Lobby, seat assignment, scene synchronization, turn resolution, or screenshot capture.
