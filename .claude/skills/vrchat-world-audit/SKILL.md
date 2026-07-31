---
name: vrchat-world-audit
description: "Audit and iteratively optimize a VRChat SDK3 world scene using the live Unity bridge (unity_optimization, manage_texture, manage_checkpoint). Measures triangles, unique materials/meshes, renderers, draw-call estimate, and oversized textures, then applies bounded optimization passes. Also reviews lightmapping, occlusion, static flags, and Udon hot paths. Use for VRChat world scenes — not for avatar performance ranks or MCP build/smoke tests."
argument-hint: "[scene-path-or-name]"
user-invocable: true
allowed-tools: Read, Glob, Grep, Bash, AskUserQuestion
model: sonnet
---

# Skill: VRChat World Audit & Optimization Loop

Audits a VRChat **world scene** (not an avatar) on Unity 2022.3.22f1 + SDK3, then optionally runs bounded optimization passes.

You **decide** which lever to pull. `.claude/tools/vrc-loop.mjs` owns the numbers.

## Phase 1 — Load context

1. Read `.claude/docs/vrchat-reference.md` (worlds / Udon) and `.claude/docs/unity-2022-reference.md`.
2. Query `get_vrc_knowledge` for world-tool specifics instead of guessing.
3. Confirm the target: `unity_optimization` reports on the **active scene**, so open the right one first with `manage_scene { action: "open_scene" }` (it auto-saves a dirty scene). Confirm PC-only vs PC+Quest.

## Phase 2 — Measure (bridge required)

**Step 0 — dossier (scene lighting/meshes/materials, do not guess):**

```bash
node .claude/tools/scene-dossier.mjs scene
```

Read the summary and Grep `.claude/.vrc-state/dossier-<slug>.md` for offenders. Do not paste the full JSON into chat. Re-run `verify <slug>` after edits if freshness matters.

Then record the optimization baseline:

```bash
node .claude/tools/vrc-loop.mjs world baseline
```

This combines `scene_summary`, `draw_call_estimate`, and `oversized_textures` into one snapshot: triangles, vertices, unique materials, unique meshes, renderers, GameObjects, draw-call estimate, oversized texture count.

Then gather what the harness does not track:

- `unity_optimization` with `mesh_audit` (per-mesh triangle offenders) and `texture_audit`
- `search_hierarchy` — find non-static geometry, realtime lights, and Udon behaviours
- `list_shaders` — Quest compatibility
- `read_script` on the noisiest UdonSharp behaviours — look for per-frame `GetComponent`, allocation in `Update`, continuous sync

Note the draw-call number is explicitly a **rough estimate** (enabled renderers times non-null shared materials); it ignores batching, shadows, and extra cameras. Treat it as a relative signal across passes, not an absolute.

**Fallback:** if the harness exits 2, report that the bridge is down and do a read-only YAML pass with every number labelled unverified. No fixes in fallback mode.

## Phase 3 — Report

1. **Summary** — PC vs Quest readiness, biggest single cost driver.
2. **Findings** — severity, area, measured evidence, suggested lever.
3. **Lighting / occlusion** — missing bakes, static flag inconsistencies, absent occlusion setup.
4. **Script hotspots** — Udon/UdonSharp suspects for `vrchat-world-optimizer` follow-up.
5. **Non-goals** — not an avatar audit; not `run-autonomous-unity-mcp` build/smoke; Editor stats are not a substitute for in-client testing.

Stop here if only an audit was requested.

## Phase 4 — Optimization loop (only if asked to optimize)

**Checkpoint first:**

```
manage_checkpoint { action: "create", label: "pre-world-opt <scene>" }
```

Then repeat, at most **5 passes** — one lever per pass:

1. Apply the single highest-yield lever.
2. Measure:

```bash
node .claude/tools/vrc-loop.mjs world measure
```

3. Act on the exit code: **0** improved/unchanged, **1** regressed (restore the checkpoint or justify), **2** bridge died (stop and report).

Stop when the target is met, two passes yield nothing, or 5 passes are spent.

### Lever tiers

**Tier 1 — apply autonomously.** Reversible import settings:
- `manage_texture` — downsize oversized textures, crunch compression, Android/Quest overrides
- Mesh compression on importers

**Tier 2 — checkpoint required.** Component and flag level:
- Set static flags on genuinely static geometry (enables batching)
- Disable shadow casting on geometry that does not need it
- Remove dead/duplicate renderers and empty GameObjects

**Tier 3 — always ask first.**
- Rebaking lightmaps (slow, and changes the visual result)
- Deleting or decimating geometry, merging materials
- Removing or rewriting gameplay Udon behaviours

Never strip gameplay Udon to win a draw-call number.

## Rules

- One change per pass, so the delta has a single cause.
- Baking lightmaps and reimporting textures can be slow; warn before starting and expect long dispatches.
- Editor-side numbers are directional. Final validation is the VRChat client and SDK world validators.
- Hand deeper strategy to the `vrchat-world-optimizer` agent; avatar work goes to `vrchat-avatar-audit`.
