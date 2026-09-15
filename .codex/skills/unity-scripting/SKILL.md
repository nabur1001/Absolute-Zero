---
name: unity-scripting
description: Reference monobehaviour, serialization, coroutines, events, pooling and c# idioms when that topic is requested in Absolute Zero; adapt examples to installed APIs and current project ownership.
---

# unity-scripting

Read [project compatibility](../../UNITY_COMPATIBILITY.md) before using the examples. Adapt examples to AbsoluteZero namespaces, immutable item configuration and network lifecycle. Custom AsTask bridges are not built-in APIs.

## How to use

Identify the existing code/assets and relevant design/plan first. Reuse available specialist skills (unity-cli) for their concrete operations; this topic supplies complementary reference depth. No package installation, scene change, gameplay expansion, or architectural migration follows merely from loading this skill.

Read only the relevant sections of [the topic guide](reference-guide.md), then the detailed reference needed for the task. Verify selected example signatures against the installed Unity/package version before adapting code. Preserve cancellation, authority and ownership. Report what was actually compiled or exercised.

## Detailed references

- [coroutines and async](references/coroutines-and-async.md)
- [monobehaviour lifecycle](references/monobehaviour-lifecycle.md)
- [object pool](references/object-pool.md)
- [scriptableobjects](references/scriptableobjects.md)

## Provenance

Adapted from the same-named project Claude reference. Source hashes and the complete migration inventory are in [skill-import-manifest.json](../../skill-import-manifest.json). Generic examples are retained as reference material, not proof of game implementation.
