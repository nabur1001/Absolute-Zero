---
name: az-network-review
description: Review Absolute Zero authority, RPC validation, seat identity, and network lifecycle when auditing or changing shared multiplayer state.
---

# Multiplayer verification

Trace the changed intent from the client entrypoint through server validation, state mutation, replication, and presentation. Follow actual callers; absence of `IsServer` in a helper alone is not proof of an authority bug.

Check the paths relevant to the change:

- RPC sender, participant/seat mapping, phase, ownership, target eligibility, and repeated or late intents.
- Server ownership of NetworkVariable/NetworkList writes and immutable SO configuration.
- TurnManager's phase ownership; clients display authoritative outcomes rather than drive phases.
- ClientId, owner ID, logical seat, participant ID, and visual slot conversions. Check holes left by disconnects and every configured seat.
- Subscription timing, late spawn, despawn, disconnect, cancellation, disposal, and second-round reuse.
- 1v1/Multi differences, Ghost or eliminated-player eligibility, target visibility, and deterministic resolution when those systems are affected.
- Inventory compaction, queued selection identity, and VFX rebuild locks when item or presentation paths change.

For each finding provide severity, file/symbol, a concrete triggering sequence, expected versus observed behavior, and the evidence limit. Separate demonstrated defects from concerns needing Play Mode reproduction. Do not assign an overall numeric score as a substitute for evidence.

Use [az-unity-validation](../az-unity-validation/SKILL.md) for available compile/runtime checks. Relevant Multi validation includes Host plus three remote clients; label unexecuted scenarios explicitly. A review request does not authorize fixes or spawning another agent.
