---
name: az-design-trace
description: Trace Absolute Zero gameplay changes or implementation plans from design decisions to code, assets, and validation; identify design drift.
---

# Design trace

Use existing identifiers and sections in `Docs/GAME_DESIGN.md`, `Docs/DESIGN_QUESTIONS.md`, and the relevant plan. Add subsystem documents such as `Docs/LOBBY_UI_SPEC.md` or `Docs/MINIGAME_SYSTEM.md` only when applicable. For architecture changes, also read `Docs/AI_TARGET_ARCHITECTURE.md` and the referenced migration plan.

For each material requirement, establish:

| Design section or decision ID | Plan task | Code/SO/scene owner | Observed behavior | Validation evidence or gap |
|---|---|---|---|---|

Use this mapping in the task plan or review; do not create a duplicate game design document. Inspect relevant ScriptableObject values as well as effect code. Distinguish intended rules, current implementation, unresolved decisions, and historical descriptions.

Review 1v1 and Multi differences explicitly when a shared path changes. Current code contains Multi paths; old statements that four-player work has not started are historical, not a reason to remove those paths. Implementation presence does not prove complete runtime correctness.

If code and design disagree on requested behavior, first check the user's current decisions. Ask for a decision only if that conflict remains unresolved and affects implementation; continue independent inspection. Do not silently change balance or mark a pending design question resolved.

Report changes to gameplay documents only when warranted by the authorized task. A review finding alone does not authorize rewriting the design or implementation.
