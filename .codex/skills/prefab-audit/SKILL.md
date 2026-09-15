---
name: prefab-audit
description: Audit Absolute Zero prefab or scene serialized references, missing scripts, package GUIDs, and network wiring without changing assets; distinguish intentional nulls from broken references.
---

# Prefab and scene audit

Read [compatibility](../../UNITY_COMPATIBILITY.md) and [az-unity-validation](../az-unity-validation/SKILL.md). Audit a user-specified path first; otherwise start with `Assets/Prefabs/Player.prefab` and relevant prefabs in `Assets/Prefabs`. Include scenes only when requested or relevant. Use the available asset-search skill for discovery if needed.

## Live prefab inspection

After confirming the Editor project, read [AuditPrefab.cs](scripts/AuditPrefab.cs). Execute its complete text using the discovered `Unity_RunCommand` schema (`Code`, `Title`). Its default target is Player.prefab. For another target substitute only the `AssetPath` C# string with a properly escaped, verified Assets-relative .prefab path; do not interpolate untrusted code. This command loads the persistent prefab asset and observes components; it does not instantiate, save, dirty, or repair it.

Report missing component slots separately from nonzero-instance-ID unresolved object references. Empty references are a count for follow-up, not errors. SerializedProperty traversal is evidence for inspected objects, not a guarantee about every imported subasset. The result includes `isDirty` before/after so unintended changes can be detected. Read owning component declarations to decide which references are actually required.

## GUID and scene checks

- Static YAML inspection may collect `m_Script`, external GUID/fileID pairs, and field context. Do not rewrite YAML. Skip binary serialization explicitly.
- Resolve GUIDs using AssetDatabase in the connected Editor, including Packages. An Assets-only `.meta` scan cannot conclusively diagnose package scripts or built-in GUIDs. A resolved GUID alone does not establish that a subasset fileID resolves.
- Inspect scenes in their existing loaded state through [scene-snapshot](../scene-snapshot/SKILL.md), or read the scene file. Do not open/replace a dirty scene solely for this audit.
- For Player/network wiring, inspect current PlayerSpawnManager and prefab lists. A null NetworkManager.PlayerPrefab can be intentional because server spawning is custom. Check NetworkObject/NetworkBehaviour relationships against the actual NGO source, without changing them.
- Variant and nested-prefab overrides need instance/asset distinction; don't label a valid inherited reference missing just because it is absent from a YAML fragment.

Output file, hierarchy path, component, property, category, evidence, and required next check. `--fix-hint` means explain a candidate correction; never guess a replacement GUID from string similarity or apply changes as part of an audit.
