# Codex Skill Catalog

All 30 procedures are routed by root AGENTS.md. Keep this index small and read only the matching SKILL.md. All 30 are exposed in the current session's available-skills catalog. Root path routing remains a fallback; no separate picker registration was installed or UI picker tested. Game decisions remain in Docs.

## Project operations

| Skill | When to use |
|---|---|
| [az-session](skills/az-session/SKILL.md) | Start, resume, handoff |
| [az-design-trace](skills/az-design-trace/SKILL.md) | Design-to-code traceability |
| [az-network-review](skills/az-network-review/SKILL.md) | Authority, identity and network lifecycle |
| [az-unity-validation](skills/az-unity-validation/SKILL.md) | Validation selection and reporting |
| [az-cross-review](skills/az-cross-review/SKILL.md) | Independent evidence-based review |
| [console-check](skills/console-check/SKILL.md) | Live console classification and issue matching |
| [prefab-audit](skills/prefab-audit/SKILL.md) | Prefab/scene serialized references and network wiring |
| [scene-snapshot](skills/scene-snapshot/SKILL.md) | Read-only scene/component/transform capture and comparison |

## Unity reference library

| Skill | Coverage | Existing specialist overlap |
|---|---|---|
| [unity-ai-navigation](skills/unity-ai-navigation/SKILL.md) | NavMesh surfaces, agents, obstacles, links, patrols and inference references | initialize-ai-navigation |
| [unity-animation](skills/unity-animation/SKILL.md) | Animator controllers, clips, blend trees, root motion, events and Timeline | unity-cli |
| [unity-async-patterns](skills/unity-async-patterns/SKILL.md) | Awaitable cancellation, thread context, coroutine lifetime and asset handles | unity-cli |
| [unity-audio](skills/unity-audio/SKILL.md) | AudioSource, clips, mixers, spatial sound and playback patterns | optimize-audio, audio-setup-mixers |
| [unity-data-driven](skills/unity-data-driven/SKILL.md) | ScriptableObject configuration, JSON pipelines, data migration and designer handoff | unity-package-management |
| [unity-foundations](skills/unity-foundations/SKILL.md) | GameObjects, components, transforms, prefabs and scene fundamentals | unity-cli |
| [unity-game-architecture](skills/unity-game-architecture/SKILL.md) | Service ownership, composition, events and bootstrap patterns | unity-cli |
| [unity-game-loop](skills/unity-game-loop/SKILL.md) | Session loops, conditions, progression hooks, difficulty and pacing | az-design-trace |
| [unity-graphics](skills/unity-graphics/SKILL.md) | URP, HDRP reference, shaders, materials, cameras and Render Graph | validate-urp-render-graph-renderer-feature, shader-graph-create-custom-node |
| [unity-input](skills/unity-input/SKILL.md) | Input actions, maps, rebinding, devices, interactions and processors | unity-cli |
| [unity-lifecycle](skills/unity-lifecycle/SKILL.md) | Unity null semantics, initialization, event cleanup and destruction | unity-cli |
| [unity-lighting-vfx](skills/unity-lighting-vfx/SKILL.md) | Lighting, probes, particles, VFX Graph and effect configuration | urp-postprocessing |
| [unity-npc-behavior](skills/unity-npc-behavior/SKILL.md) | Perception, decisions, actions, factions, memory and squad coordination | initialize-ai-navigation |
| [unity-performance](skills/unity-performance/SKILL.md) | Profiler, memory, CPU, rendering and allocation investigation | optimize-audio, optimize-text-mesh-pro, optimize-web |
| [unity-physics](skills/unity-physics/SKILL.md) | 3D and 2D bodies, colliders, triggers, raycasts, joints and controllers | physics-3d-collision |
| [unity-procedural-gen](skills/unity-procedural-gen/SKILL.md) | Noise, grids, rooms, seeded generation and content budgets | tilemap-palette-create, tilemap-ruletile-createfromsegment |
| [unity-save-system](skills/unity-save-system/SKILL.md) | Save DTOs, locations, migrations, atomic writes and cloud synchronization | build-live-game |
| [unity-scene-assets](skills/unity-scene-assets/SKILL.md) | Scene ownership, loading screens, Resources and Addressables lifetimes | unity-cli, unity-package-management |
| [unity-scripting](skills/unity-scripting/SKILL.md) | MonoBehaviour, serialization, coroutines, events, pooling and C# idioms | unity-cli |
| [unity-state-machines](skills/unity-state-machines/SKILL.md) | FSM, hierarchical states, behavior trees, stacks and state testing | az-design-trace |
| [unity-ui](skills/unity-ui/SKILL.md) | uGUI, UI Toolkit, TextMeshPro and data binding references | ui, ui-ugui, ui-uitk, optimize-text-mesh-pro |
| [unity-ui-patterns](skills/unity-ui-patterns/SKILL.md) | Screen flow, presenters, HUD, feedback, lists and UI transitions | ui, ui-ugui, ui-uitk |

All 25 source topics are included: three rewritten operational skills and 22 adapted reference skills with all 41 supporting documents retained. Read [compatibility corrections](UNITY_COMPATIBILITY.md) before using reference code. File provenance is recorded in [the manifest](skill-import-manifest.json). Validation evidence and limitations live in [PLAN_028](../Docs/Plans/PLAN_028_codex_skill_library.md).
