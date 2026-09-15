---
name: scene-snapshot
description: Capture Absolute Zero loaded scene hierarchy, component types, active states and local transforms for read-only inspection and before/after comparison, with optional report saving.
---

# Scene snapshot

Confirm project identity using [az-unity-validation](../az-unity-validation/SKILL.md). Record editor play/compile state and call `Unity_ManageScene` with `GetActive` and `GetBuildSettings`. For a quick hierarchy use `GetHierarchy` with an explicit depth, and label limited-depth results.

For component/transform detail, read [CaptureScene.cs](scripts/CaptureScene.cs) and execute the complete command via `Unity_RunCommand`. It captures loaded normal scenes with a 500-object cap, includes inactive objects, and reports truncation and scene dirty flags. It does not include the special DontDestroyOnLoad scene or unloaded assets; disclose that scope. Local transform values are captured for comparison only, never automatically restored.

Derive required managers/components from the current scene and owning source. LobbyScene, GameScene, and GameScene_Multi have different owners. A runtime-spawned Player or runtime-built UI missing in Edit Mode is not automatically a defect. Do not copy the source project's @MapManagers/CombatManager expectations. `--check-only` means summarize relevant component checks without printing the whole hierarchy.

For a comparison, match scene path plus hierarchy names/sibling indices and component types; instance IDs are transient, and reordering may change paths. Report additions/removals, active-state, component and transform changes separately. Do not conflate an Edit Mode snapshot with a runtime one.

Default output is a response. If saving is requested, write a timestamped report under `Docs/Validation/` with project, scene paths, mode, limits, captured data and evidence; use seconds or a unique suffix to avoid overwriting. Never save the Unity scene, start Play Mode, clear logs, reposition objects, or force recompilation as part of capture. If disconnected, describe any file-based scene inspection as static rather than a live snapshot.
