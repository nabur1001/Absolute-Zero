---
name: console-check
description: Read and classify Absolute Zero Unity console errors and warnings, trace source locations, and optionally match current known issues without modifying code or clearing logs.
---

# Console check

Use [az-unity-validation](../az-unity-validation/SKILL.md) for project identity and evidence limits. This replaces the Claude source's fixed port, old tool names, and unrelated issue IDs.

1. Discover current tools and call `Unity_ManageEditor` with `Action: GetProjectRoot`. Require the current repository before reading project data. Query `GetState` to record compile/play state.
2. Call `Unity_ReadConsole` with `Action: Get`, `Types: [Error, Warning]`, `Format: Json`, `IncludeStacktrace: true`, and an explicit count (default 100). Errors-only uses `[Error]`. Use a supported timestamp or filter for a narrowed request; never clear logs to obtain a clean result.
3. Group identical type/message/first relevant source location. Report retrieved entries, grouped entries, query bounds, and possible truncation; do not infer total editor counts from a capped response. Request more when the cap is reached and supported by the tool.
4. Trace project-owned stack frames to existing files/lines. Separate compilation, null/destroyed-reference, missing-component, network-authority, package/tooling, and asset failures. Package/internal frames do not prove a gameplay defect.
5. When issue matching is requested, read current `Docs/KNOWN_ISSUES.md` and relevant plan entries. Match actual evidence, not copied KI/RULE numbers. Unmatched failures are candidates, not automatically created or resolved issues.
6. Return severity, group/count, source, likely cause with confidence, and next focused check. Use [prefab-audit](../prefab-audit/SKILL.md) for reference failures when relevant. No code changes are implied by inspection.

If MCP is unavailable, analyze a user-provided/exported log or the relevant Editor log and label it as file-based historical evidence. Do not claim an empty live console or invoke forced recompilation. Flags in a user request such as `--errors-only` and `--match-issues` are procedure options, not registered slash commands.
