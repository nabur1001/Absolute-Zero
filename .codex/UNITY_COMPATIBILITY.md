# Unity reference compatibility contract

Read this contract when using the imported Unity references. Root instructions, the current user request, project guide, executable state, and relevant Docs take precedence over illustrative examples. Each imported topic links here before its source material.

## Scope and installed specialists

References describe capabilities, not commands to migrate this game. Preserve NGO server authority, TurnManager phase ownership, read-only item SOs, explicit participant/seat mapping, and current 1v1/Multi behavior. Do not introduce a second local game loop, replace UI technology, or install example dependencies without a task requiring that change.

Use installed specialists where available: `unity-cli` for Editor operations; `generate-editor-search-query` for concrete asset/object lookup; `ui`, `ui-ugui`, `ui-uitk`, `ui-imgui` for implementation; `optimize-text-mesh-pro`, `initialize-ai-navigation`, `physics-3d-collision`, `optimize-audio`, `audio-setup-mixers`, `urp-postprocessing`, `validate-urp-render-graph-renderer-feature`, and `build-live-game` for their domains. Imported references retain complementary explanations and examples. If an installed skill is unavailable, inspect local code and official docs rather than assume its tools exist.

## Corrections and interpretation

- `fileID: 0` or a zero GUID alone normally indicates an unassigned reference, not proof of damage. Package and built-in resources may be absent from an Assets-only GUID index. Confirm missing scripts/references with the Editor and owning component requirements.
- `Awaitable` is pooled and must not be awaited repeatedly. `.AsTask()` is not a built-in method listed on Unity Awaitable; the scripting reference includes a custom bridge extension. Include and validate that helper if used, or use an explicit async Task wrapper that consumes the Awaitable once. Preserve cancellation and network-despawn lifetimes. An awaitable expression in an async method is not invalid merely because the method returns a different task-like type.
- `GetHashCode`, including ordinal string hashing, and `HashCode.Combine` do not define portable persisted seeds. The imported procedural examples use an explicit fixed hash algorithm instead; record algorithm/version and input encoding when seeds cross machines.
- `AnimationCurve.Evaluate(time)` evaluates the supplied time. It is not inherently restricted to 0..1; normalize only when the authored curve contract requires it.
- `GetComponent<T>` and value-type construction are not universally managed allocations. Measure hot paths before optimizing. Cache stable lookups for search cost; do not suppress correct value-type construction with blanket rules.
- Match subscription cleanup to actual ownership: OnEnable/OnDisable, Awake/OnDestroy, or OnNetworkSpawn/OnNetworkDespawn as appropriate. No single pair fits every object. A destroy token alone does not necessarily cover disable, network despawn, or a new round.
- Use existing Input System actions. Legacy Input examples are historical alternatives, not a reason to change Active Input Handling.
- uGUI is established production UI here. UI Toolkit examples supplement it when specifically relevant. SerializedObject binding is Editor-only; runtime UI Toolkit uses its runtime binding model.
- Use the installed URP/NGO/package source and exact API signatures. HDRP, Sentis/Inference Engine, VFX Graph, NavMesh, Addressables, cloud saves, UniTask, and third-party serializers are conditional references. Their presence in this library does not prove package availability or validate an upgrade.
- Scene loads in a network session must follow current NGO/session ownership. Generic SceneManager examples do not authorize bypassing network synchronization. Addressable scenes are not universally required in Build Settings.
- Generic save, SO event-channel, service-locator, FSM, NPC, and UI scaffolds must be adapted to existing state owners. Do not import complete competing managers by default. Retained RIGHT/WRONG labels are illustrative context, not unconditional project rules.

## Evidence and sources

The migration manifest records source hashes and retained files. Validation covers links/metadata and named operational checks; it does not certify all tutorial code. Before using a selected code example, inspect its complete context, installed API, authority/lifecycle behavior, and run a focused check appropriate to the actual change.

Official references consulted for corrections:

- [Unity 6.3 Awaitable](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Awaitable.html)
- [AnimationCurve.Evaluate](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/AnimationCurve.Evaluate.html)
- [GUIDToAssetPath](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/AssetDatabase.GUIDToAssetPath.html)
- [Object reference properties](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/SerializedProperty-objectReferenceValue.html)
