---
name: unity-compile-fix
description: "Semi-automatic Unity 2022.3 compile/console error fix loop. Uses unity-verify.mjs (refresh_unity + read_console level=error — never get_compilation_errors) to measure, then fixes one root cause per pass. Use when asked to fix compile errors, CS errors, or clear the Unity console. Not for avatar/world optimization or MCP build/smoke."
argument-hint: "[optional error filter]"
user-invocable: true
allowed-tools: Read, Glob, Grep, Bash, Edit, Write, AskUserQuestion
model: sonnet
---

# Skill: Unity Compile Fix Loop

Fitness function: **console error count → 0**, measured by `.claude/tools/unity-verify.mjs`.

## Critical constraint — Unity focus

Unity **defers compilation while unfocused**. After every C# edit you must pause and ask the user to **click into the Unity editor** (or `Ctrl+R`) before re-verifying. This loop is **semi-automatic**, not unattended. Do not pretend otherwise.

Also: verify with `read_console {level:"error"}` via the harness — **never** `get_compilation_errors` (stale last-good assembly → false-clean; see `CLAUDE.md`).

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
3. Ask the user: **"Focus Unity so it recompiles, then tell me when ready."** Wait.
4. Re-measure:

```bash
node .claude/tools/unity-verify.mjs
```

5. Act on the exit code:
   - **0** — done. Summarize what changed.
   - **1** — still errors. If the same error persists, the edit did not take (stale assembly / Unity not focused) — re-check with the user before another edit.
   - **2** — bridge died. Stop.

Stop early when clean, when two passes yield no change, or after 5 passes.

## Rules

- One root cause per pass so the delta is interpretable.
- Prefer package edits under `com.autonomous-unity.mcp/` (junction-mounted into Leaf).
- Do not run avatar/world optimization here — that is `vrchat-avatar-audit` / `vrchat-world-audit`.
- Build/smoke of the Node relay stays with `run-autonomous-unity-mcp`.
