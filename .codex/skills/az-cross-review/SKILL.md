---
name: az-cross-review
description: Perform a bounded evidence-based cross-review of an Absolute Zero plan or another agent's changes, or prepare a portable review handoff.
---

# Cross-review

Establish whether the request concerns a proposal, committed diff, or working-tree implementation. Capture the relevant plan/decision, files, baseline, non-goals, and claimed tests. Inspect current files independently; pasted summaries and previous scores are claims to verify.

Use [az-design-trace](../az-design-trace/SKILL.md) for design consistency and [az-network-review](../az-network-review/SKILL.md) when network paths are involved. Do not run every procedure for an unrelated review.

Return findings ordered by impact, with source locations, trigger/reproduction, expected behavior, observed evidence, and a focused correction. Explicitly separate implementation findings from missing runtime evidence. If there are no findings, state the inspected scope and remaining limits.

Review mode produces findings without edits unless the user also requests fixes. This procedure does not itself authorize delegation, external messages, recursive reviewer calls, or concurrent edits to the same files.

When an MCP-only bridge cannot connect, produce this portable request for the user or calling agent:

```text
Review target and type:
Repository and baseline/diff:
Design references and accepted decisions:
Relevant files/symbols:
Questions and non-goals:
Tests already run, with evidence:
Requested output: prioritized findings, source locations, reproduction, and verification gaps.
```

Do not report the review as performed, or promise a shell fallback from a tool-restricted bridge. A standalone Codex session can continue from this request.
