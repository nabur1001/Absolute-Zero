---
name: az-unity-validation
description: Choose and report focused static, Unity compile, console, and Play Mode checks for Absolute Zero changes without overstating runtime evidence.
---

# Unity validation

Read the installed version from `ProjectSettings/ProjectVersion.txt` and relevant packages from `Packages/manifest.json` or installed source. Use available Unity specialist skills for the actual UI, asset, CLI, or rendering operation rather than embedding their implementation here.

Select checks proportional to the change:

1. References, authority paths, cleanup, namespaces, serialized field compatibility, and GUID pairing.
2. Relevant existing tests or analyzers. A generated `.csproj` build is supporting evidence only.
3. Unity automatic compilation and console inspection after saving code when an editor connection is available. Distinguish new diagnostics from preexisting errors; do not clear the console just to produce a clean result.
4. Targeted Host/client Play Mode scenarios for network, timing, visuals, disconnect, and reset behavior.

Before any Editor operation, discover the current connection and verify that its project path is this repository. Do not reuse another project's connection or hardcode a stale instance ID/transport from an old setup guide. If discovery cannot establish the project, continue static work and report live checks unavailable.

Never invoke forced `recompile_scripts`. Do not move scene objects or alter assets just to make validation easier. Preserve `.meta` pairings and use a verified Editor automation path for serialized changes.

Report the command/tool, scope, observed result, and remaining gaps. Documentation/harness edits do not require gameplay tests. Do not label a planned test, tool availability check, or old validation report as a successful run.
