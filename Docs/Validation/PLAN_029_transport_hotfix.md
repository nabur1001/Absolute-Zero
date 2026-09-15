# Transport 2.7.2 receive-buffer hotfix

## Scope and reason

KI-007 was reproduced on Windows with four independent development players: abruptly closing one UDP client eventually stopped reception from the remaining clients. A no-exit control survived 70 seconds. A loopback proxy that kept the remote socket open after process termination also survived, narrowing the failure to the UDP receive path rather than the match disconnect callback.

The installed `UDPNetworkInterface.ReceiveJob` acquired a packet buffer when scheduling a receive, but did not return that buffer when a completed receive failed or contained zero/invalid payload bytes. Those completions were never enqueued, so the normal queue cleanup could not release them. Repeated failed completions could exhaust the receive pool shared by all connections.

## Local change

- Unity Package Manager `Client.Embed("com.unity.transport")` embedded the already installed **2.7.2**, retaining its version, dependency requirements, license, and source files.
- `Packages/com.unity.transport/Runtime/UDPNetworkInterface.cs` returns the acquired buffer on both discarded-completion paths. Successful packets retain their existing ownership and cleanup.
- The exact source delta is [PLAN_029_transport_receive.patch](PLAN_029_transport_receive.patch): five added lines, including two `ReleaseBuffer` calls.
- No heartbeat, timeout, packet capacity, game rule, scene, or asset value was changed to mask the problem.
- `Packages/packages-lock.json` now resolves the same package from the embedded directory. The manifest's version remains 2.7.2.

## Provenance

Compared the embedded directory with the official Unity registry 2.7.2 tarball (`https://download.packages.unity.com/com.unity.transport/-/com.unity.transport-2.7.2.tgz`). Of 309 upstream files, the only source difference is the UDP file above. Package Manager adds `_fingerprint` to `package.json` and omits the registry `.signature` and `.attestation.p7m` when embedding. These metadata differences are not additional gameplay or package API changes.

## Regression evidence

`TransportReceiveRecoveryTests` creates real loopback NetworkDrivers with a four-buffer receive queue. The clean control connects; after eight empty UDP datagrams, the unpatched implementation fails to accept a new connection. The patched implementation accepts it. This test isolates lost receive capacity without running game code.

- Before: **1 passed / 1 failed** (`transport-before.xml`).
- After: **28 / 28** full EditMode tests passed (`transport-after.xml`).
- Rebuilt Windows players pass direct UDP abrupt-exit checks during preparation and attack, final presentation, and the 70-second connection-health scenario. See [the matrix](PLAN_029_ai_matrix.md) for selected evidence and all-seat follow-up results.

Raw evidence is under `C:/Users/paek6/AppData/Local/Temp/AZMatrix_20260915/`. Internet Lobby/Relay, other operating systems and separate PCs are not certified by these loopback runs. ICMP/native error details are inferred from the source and controlled socket comparison; no native packet capture was performed.

## Maintenance

Commit the embedded package alongside the lock file and regression tests; a cache-only patch would disappear on another machine. For a future approved Transport upgrade, compare the upstream receive paths and run these regressions before removing or porting the local patch. Do not automatically reapply a patch to a different version or alter `Library/PackageCache` on startup. Removing the embedded package restores the manifest's registry resolution, but also restores this defect unless the replacement passes the regression.
