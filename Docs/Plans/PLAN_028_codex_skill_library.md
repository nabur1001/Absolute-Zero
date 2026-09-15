# PLAN_028 - Codex Unity Skill Library

## Scope

Import all 25 project Claude skill topics into the contained Codex harness: three operational audits and 22 Unity references. Keep existing five Codex procedures. Preserve useful supporting examples while correcting known incompatibilities and making project constraints explicit. Shared game facts stay in Docs.

## Non-goals

No Claude settings/hooks changes, gameplay/scene/prefab edits, package installations, global configuration changes, background agents, native skill-picker registration, or automatic hook installation. Importing NPC, HDRP, cloud-save, or procedural references does not activate those features in the game.

## Tasks

- [x] Inventory 25 source skills and 66 Markdown files; examine topics, project assumptions, tool names, and risky API claims.
- [x] Adapt 22 reference entrypoints and preserve supporting documents with provenance and corrections.
- [x] Rewrite console-check, prefab-audit, and scene-snapshot for Absolute Zero and current Unity MCP.
- [x] Add an indexed routing catalog, migration inventory, and validation coverage.
- [x] Run skill/link checks and live read-only operational examples; verify no source or game changes.

## Validation boundary

Structural validation covers every imported file. Topic review checks applicability, tool dependencies, overlap with installed specialists, and selected problematic claims. Large example libraries are reference material, not code compiled into the project. Do not describe every sample or optional package as runtime-verified. Record exact live checks and remaining limits at completion.

## Completion evidence (2026-09-15)

- All 25 source topics imported: 22 adapted Unity reference entrypoints plus three rewritten operational skills. Existing five az-* skills retained, for 30 total.
- All 41 supporting source documents retained alongside 22 topic guides. No source topic was excluded. Conditional subjects (NPCs, HDRP, inference, cloud saves) remain reference capabilities, not installed gameplay features.
- 98 Markdown instruction/reference documents passed local link and metadata validation, with zero errors/warnings. All 30 SKILL.md files separately passed skill-creator quick_validate.py.
- Source/target hashes in `.codex/skill-import-manifest.json` matched on resume. Protected source/config/scene/prefab files in `.codex/source-baseline.json` were unchanged from the captured baseline. No Claude or gameplay files were written by this task.
- Root AGENTS.md routes to SKILL_CATALOG.md; the resumed environment now explicitly exposes all 30 skills in its available-skills catalog. This supersedes earlier path-only assumptions for this client. No separate registration, global changes, or symlinks were added; no UI picker test or independent agent behavior evaluation was performed.
- Corrected the previously observed heading question marks and contradictory latest-snapshot wording.

### Operational smoke tests

| Procedure | Actual check | Result |
|---|---|---|
| console-check | Unity_ReadConsole, Error/Warning, count 100 | Three preexisting warnings, no errors returned: Input Manager deprecation and two executable signature warnings. Not a new game compilation test. |
| prefab-audit | Compile and execute bundled AuditPrefab.cs against Player.prefab | One object, five components, eleven empty references, zero reported missing/unresolved findings. dirtyBefore=false, dirtyAfter=false. Empty fields were not misclassified as errors. |
| scene-snapshot | Compile and execute bundled CaptureScene.cs | LobbyScene, 101 objects, local transforms/component lists present, truncated=false, scene dirty=false. |
| scene identity/build list | GetActive and GetBuildSettings | LobbyScene active, 11 roots; LobbyScene/GameScene/GameScene_Multi enabled at indices 0/1/2. |

Both final command payloads were parsed as JSON and inspected, not merely checked for execution success. Initial helper versions exposed MCP dynamic compiler behavior: private nested DTOs were duplicated at namespace scope, JsonUtility omitted dynamic collection fields, and an installed Newtonsoft package was not accessible in the dynamic command compilation context. Final helpers use internal DTOs and explicit JSON formatting with escaping/invariant float formatting, requiring no new dependency. Final versions compiled and returned full expected data. No forced project recompile was invoked.

### Corrections and remaining limits

The compatibility contract and imported copies address normal-null versus broken references, obsolete MCP names/ports and foreign manager/issue IDs, custom Awaitable-to-Task bridge assumptions, deterministic seed hashing, curve time semantics, allocation overclaims, runtime versus Editor binding, and inappropriate mandatory architecture migrations. Individual topic notes map overlap to existing specialist skills.

The 22 large tutorial libraries received structural/topic compatibility review and targeted corrections. Their many illustrative snippets were not all compiled, all remote documentation links were not checked, and optional-package workflows were not runtime-tested. The three operational smoke tests cover the current live LobbyScene and Player prefab, not every scene/prefab, missing-reference failure fixture, or multiplayer Play Mode scenario. API signatures and selected snippets still require focused verification when used for an implementation task.

## Maintenance and rollback

Use `.codex/SKILL_CATALOG.md` to select a topic, and `.codex/UNITY_COMPATIBILITY.md` for project-specific constraints. The manifest records imported-file hashes; refresh target hashes deliberately after future reference edits. The source baseline is an audit record, not a restore script.

To undo this expansion, review only PLAN_028-owned added files and routing edits; retain the earlier five-skill harness and unrelated user changes. No configuration, packages, hooks, or global installation needs undoing.
