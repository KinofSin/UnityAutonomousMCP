---
name: vrchat-avatar-audit
description: "Audit and iteratively optimize a VRChat SDK3 avatar against PC/Quest performance ranks using the live Unity bridge (scan_avatar, manage_texture, manage_checkpoint). Measures polys, materials, skinned meshes, bones, PhysBones, and expression parameter cost, then applies bounded optimization passes. Use when asked to audit, score, or optimize an avatar's VRChat performance — not for world scenes, code review, or MCP build/smoke tests."
argument-hint: "[avatar-gameobject-name]"
user-invocable: true
allowed-tools: Read, Glob, Grep, Bash, AskUserQuestion
model: sonnet
---

# Skill: VRChat Avatar Audit & Optimization Loop

Audits a VRChat **avatar** (not a world) on Unity 2022.3.22f1, then optionally runs bounded optimization passes.

You **decide** which lever to pull. You do **not** grade your own work — `.claude/tools/vrc-loop.mjs` owns the numbers.

## Phase 1 — Load context

1. Read `.claude/docs/vrchat-reference.md`.
2. Query `get_vrc_knowledge` for anything tool-specific (Poiyomi, AAO, Modular Avatar conventions) rather than guessing.
3. Read `com.autonomous-unity.mcp/Editor/Templates/InteractionNotes.json` for prefab/tooling interaction traps.
4. Resolve the target avatar GameObject name from the argument, or ask. Note whether a Quest twin exists (e.g. `LEAF` / `LEAF QUEST`).

## Phase 2 — Measure (bridge required)

The live editor is the source of truth. Do not parse prefab YAML when the bridge is up.

**Step 0 — dossier (inspector/material state, do not guess):**

```bash
node .claude/tools/scene-dossier.mjs avatar <goName>
```

Read the printed summary and Grep `.claude/.vrc-state/dossier-<slug>.md` for the specific mesh/material/property you need. Do not paste the full JSON into chat. Locked Poiyomi materials are flagged — unlock before editing properties. Re-run `verify <slug>` after edits if you need to know whether the artifact went stale.

Then record the optimization baseline:

```bash
node .claude/tools/vrc-loop.mjs avatar baseline <goName>
```

This calls `scan_avatar` and records polygons, material slots, skinned meshes, mesh renderers, blendshapes, bone count, PhysBones, PhysBone colliders, contacts, and expression parameter cost against its 256 budget.

Then gather qualitative context the harness does not track:

- `scan_avatar` full output — `shaderUsage`, `installedFrameworks` (Modular Avatar / VRCFury / AAO / lilycalInventory), `hasAvatarDescriptor`, viseme mesh, view position
- `scan_armature` — PhysBone chains and per-SkinnedMeshRenderer vertex/material counts, to find *which* mesh is heavy
- `list_shaders` / `get_installed_packages` — Quest shader compatibility and available optimizer tooling
- `unity_optimization` with `texture_audit` / `oversized_textures` — texture memory pressure

**Fallback:** if the bridge is unreachable (harness exits 2), say so plainly, then do a read-only pass over the prefab `.meta`/YAML and label every number as unverified. Do not attempt fixes in fallback mode.

## Phase 3 — Report

1. **Summary** — target platform (PC / Quest / both), estimated rank pressure, and whether a Quest twin is present.
2. **Findings** — table of severity (High/Med/Low), area, measured evidence, suggested lever.
3. **Quest twin gaps** — shaders (no Poiyomi on Quest), material sets, texture sizes.
4. **Non-goals** — state that you did not run the SDK validator, build/smoke (`run-autonomous-unity-mcp`), or a world audit.

Stop here if the user only asked for an audit.

## Phase 4 — Optimization loop (only if asked to optimize)

**Checkpoint first, always:**

```
manage_checkpoint { action: "create", label: "pre-avatar-opt <goName>" }
```

Then repeat, at most **5 passes**:

1. Pick the **single** highest-yield lever from the tiers below. One change per pass — batching changes makes the delta uninterpretable.
2. Apply it.
3. Measure:

```bash
node .claude/tools/vrc-loop.mjs avatar measure <goName>
```

4. Read the exit code, not your own impression:
   - **0** — improved or unchanged. If unchanged, that lever was a dud; pick a different one.
   - **1** — a tracked metric regressed. Restore the checkpoint (`manage_checkpoint { action: "restore", id }`) or explain why the trade is worth it and get approval.
   - **2** — bridge died. Stop the loop and report.

**Stop the loop when** the target rank is met, two consecutive passes produce no improvement, or 5 passes are used. Never loop silently — print the delta table each pass.

### Lever tiers

**Tier 1 — apply autonomously.** Reversible / non-destructive, no geometry change:

1. **AAO TraceAndOptimize (first if installed and off).** `scan_avatar` reports `installedFrameworks` with `"AAO: Avatar Optimizer"` and `hasTraceAndOptimize`. If AAO is present and that flag is false, enable TraceAndOptimize on the avatar (via the AAO component / Modular Avatar path already in the project). Highest-yield non-destructive lever for poly/material rank pressure — do this before texture work.
2. `manage_texture` — real actions are `get_import_settings`, `set_import_settings`, `get_info`, `find_textures`. Use `set_import_settings` to reduce max size, enable crunch compression, set Android/Quest overrides, and fix mipmaps. There is no `set_max_size` action.
3. Mesh compression on `ModelImporter` (via `unity_importer` or the asset's import settings).

These usually move rank pressure a lot without touching the look.

**Tier 2 — checkpoint required (already created in this phase).** Component-level cleanup:
- Remove leftover **DynamicBones** (superseded by PhysBones)
- Prune empty or degenerate PhysBone components and unused PhysBone colliders
- Remove disabled components that still count toward rank

Verify each removal actually targets dead weight — a PhysBone with no child transforms is dead, one driving hair is not.

**Tier 3 — always ask first.** Cosmetically or structurally destructive:
- Deleting or decimating geometry
- Bone hierarchy changes / armature merges
- Material merges and atlasing
- Anything that changes the avatar's appearance

Rank can be "improved" by destroying the avatar, so Tier 3 stays gated on explicit approval even mid-loop.

## Rules

- One change per pass. The delta is only meaningful if it has a single cause.
- Never edit C# in this loop. Not because of compilation (the editor recompiles unfocused just fine) but because a script change triggers a domain reload mid-measurement, which invalidates the pass. Send compile work to `unity-compile-fix`.
- Do not invent exact rank thresholds. Report measured numbers and direct the user to the SDK control panel for the official rank.
- Hand off deeper strategy to the `vrchat-avatar-optimizer` agent; route world work to `vrchat-world-audit`.
