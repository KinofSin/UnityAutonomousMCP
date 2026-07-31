---
name: unity-compile-fix
description: "Unattended Unity 2022.3 compile/console error fix loop. Uses unity-verify.mjs (refresh_unity + read_console level=error — never get_compilation_errors) to measure, then fixes one root cause per pass. Use when asked to fix compile errors, CS errors, or clear the Unity console. Not for avatar/world optimization or MCP build/smoke."
argument-hint: "[optional error filter]"
user-invocable: true
allowed-tools: Read, Glob, Grep, Bash, Edit, Write, AskUserQuestion
model: sonnet
---

# Skill: Unity Compile Fix Loop

Fitness function: **console error count → 0**, measured by `.claude/tools/unity-verify.mjs`.

## This loop runs unattended

**Unity does not need focus to compile.** Measured 2026-07-31: with another app holding the
foreground for the whole test, writing a package `.cs` and calling `refresh_unity` recompiled in
23s (`buildStamp c169b785 → ba1225bd`). Do **not** ask the user to click into Unity — just call
the harness and wait for `isCompiling` to clear.

Verify with `read_console {level:"error"}` via the harness — **never** `get_compilation_errors`
(stale last-good assembly → false-clean; see `CLAUDE.md`).

## Phase 1 — Baseline

```bash
node .claude/tools/unity-verify.mjs
```

- Exit **0** — clean. Report and stop.
- Exit **1** — errors present. Continue.
- Exit **2** — bridge down. Stop and say so.

Read `.claude/docs/unity-2022-reference.md` for the `??` pitfall, asmdef/testables, and 2022.3 API stance before editing.

## Phase 2 — Loop (max 5 passes)

Each pass:

1. Pick **one** root cause from the error list (prefer the first unique file/line cluster).
2. Fix it with the smallest additive change. Avoid `??` on `UnityEngine.Object`.
3. Re-measure (no user interaction needed — the harness triggers the recompile itself):

```bash
node .claude/tools/unity-verify.mjs
```

4. Act on the exit code:
   - **0** — done. Summarize what changed.
   - **1** — still errors. If the identical error persists, the edit did not take (stale assembly) — confirm the file on disk changed before editing again, rather than stacking a second fix on top.
   - **2** — bridge died. Stop.

Stop early when clean, when two passes yield no change, or after 5 passes.

## Rules

- One root cause per pass so the delta is interpretable.
- Prefer package edits under `com.autonomous-unity.mcp/` (junction-mounted into Leaf).
- Do not run avatar/world optimization here — that is `vrchat-avatar-audit` / `vrchat-world-audit`.
- Build/smoke of the Node relay stays with `run-autonomous-unity-mcp`.
