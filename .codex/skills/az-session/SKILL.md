---
name: az-session
description: Start, resume, or hand off repository work in Absolute Zero using shared Docs and the current dirty worktree.
---

# Session continuity

Read [PROJECT_GUIDE.md](../../PROJECT_GUIDE.md) once per session. At the start of repository work, inspect `git status --short`, the top current section of `Docs/ACTIVE_CONTEXT.md` when present, and the exact plan relevant to the user's request. Read recent history only when it resolves a question about that work.

User steering takes precedence over a stale status block. Verify paths and symbols with `rg`; do not assume old architecture names still exist. Missing ignored status files are not a blocker: use the user's request, tracked plans, and current code, and state the gap if it matters.

For substantial work, use a scoped plan in `Docs/Plans/` containing the design reference, changed files/symbols, non-goals, validation, and remaining work. Do not create a plan for a trivial self-contained edit.

Before updating shared documents, reread the affected section. Preserve concurrent work; record the current task without replacing an unrelated active gameplay task. Record meaningful results once in the plan and link to them from continuity/history when useful. No mandatory four-document rewrite on every turn.

For a handoff, record the task/plan, affected files, decisions, checks actually run, remaining checks, and next concrete action. Keep gameplay status in Docs; do not create a separate Codex game-state memory. Use the plan or final response when local status files are unavailable.

Do not mark runtime tests complete from code inspection or an old review score. If interrupted before writing a handoff, the next session must inspect the current worktree again rather than assume prior edits completed the task.
