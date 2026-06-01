# Project-setup templates — design

## Context & north star

Same mission as the rest of Autonomous Unity MCP: make a **brand-new VRChat dev**
operate like a pro **with as little knowledge as possible**. A novice usually
**imports a ready-made avatar** (mesh/armature/accessories already there — like the
project's `LEAF` / `LEAF QUEST`) but skips the **foundational setup a pro does
without thinking** — the VRC Avatar Descriptor, viewpoint, Expression Menu/Parameters
assets, sane folder organization, and the project settings that other tools/packages
(VRCFury, Modular Avatar, Poiyomi) and the upload flow silently assume. This feature
scaffolds that foundation deterministically.

**Scope (chosen):** *Scene + project organization* — the in-scene avatar foundation
**plus** folder structure, naming, and key project settings. **Deferred (notes only):**
package/SDK auto-install (that's the VCC's job; harder/riskier over the bridge) — but
the *knowledge* of "what messes with what" (package/prefab interactions, install order,
conflicts) is captured as a notes layer.

**Project types:** Avatar (PC) + Avatar (Quest/Android). Worlds deferred. PC and Quest
share the same hierarchy; they differ in constraints/defaults. The common real-world
shape is a PC avatar + its Quest twin in one project (`LEAF` + `LEAF QUEST`).

## Behavior: detect, then act

`apply` is **idempotent and non-destructive** — it inspects state and adds *only what
is missing*, safe to re-run:
- **Avatar present** (object with a humanoid Animator, or a VRC Avatar Descriptor):
  set it up — add/fix descriptor + viewpoint, create + link Expression Menu/Parameters,
  organize folders, apply scoped project settings, note the PC↔Quest pairing.
- **Empty scene**: create a minimal starter avatar scaffold (root + descriptor +
  placeholder hierarchy) to build from.

## Architecture

Determinism + testability drove the choice of **a tool applying declarative template
data** (over an AI-orchestrated skill): the foundational setup must be correct every
time and unit-testable. The AI customizes on top with its existing fine-grained tools
and the existing `vrchat-avatar` / `vrcfury-toggles` / `modular-avatar` skills.

- **`manage_project_template`** — MCP tool (legacy-switch, like the other `manage_*`),
  actions:
  - `inspect` (read-only) — detect project/avatar state; report present vs missing.
  - `list` — available templates (`avatar-pc`, `avatar-quest`).
  - `apply` — idempotently apply a named template; report exactly what changed.
- **`ProjectTemplate` definitions** — declarative data (JSON in the package, mirroring
  `Skills/index.json`): per-type target structure + required setup steps. Versionable,
  editable without code changes.
- **`ProjectTemplateEngine`** (`Editor/Templates/`) — pure-ish C# that, given a template
  + a scene state, computes the **diff** (what's missing) and applies it. The diff is
  the unit-testable core.
- **VRChat SDK components via reflection** — Avatar Descriptor / Expression Menu /
  Parameters require VRCSDK types our package cannot hard-reference (same constraint as
  Cinemachine, handled via reflection elsewhere). If the SDK isn't present, skip those
  steps and say so clearly in the report.

## What the avatar template scaffolds (the pro setup)

1. **Detect** — avatar present? PC vs Quest (heuristics: name/twin, platform, poly/mat
   counts)? what's already set up?
2. **VRC Avatar Descriptor** — add if missing; set viewpoint to a sane default (head
   bone position + small forward/up offset).
3. **Expression assets** — create `VRCExpressionsMenu` + `VRCExpressionParameters` if
   missing, in the avatar's Expressions folder, and link them to the descriptor.
4. **Folder organization** — `Assets/_Project/<Avatar>/{Animations,Materials,Expressions,Textures}`.
5. **Project settings** — the short list novices get wrong (e.g. color space = Linear),
   each applied only if it differs, each reported.
6. **PC↔Quest pairing** — detect the twin (e.g. `LEAF` ↔ `LEAF QUEST`); for the Quest
   variant apply Quest-aware defaults + notes (material/poly/shader limits, fallback).
7. **Report** — structured: `{ changed:[...], skipped:[...], notes:[...] }`, including
   which steps were skipped (e.g. "VRChat SDK not detected") and the interaction notes.

## Knowledge layer — "what messes with what"

A small declarative **package/prefab interaction notes** data file: what VRCFury vs
Modular Avatar vs Poiyomi each control, their install-order dependencies, and common
conflicts. Surfaced in the `apply`/`inspect` report and available to the Advisor HUD.
**No auto-install in v1** — this is knowledge, not action.

## Safety & edge cases

- `inspect` never mutates. `apply` is idempotent (re-running is a no-op once set up) and
  **never destructive** (only adds/links; never deletes user content).
- Missing VRCSDK → VRC-specific steps skipped with a clear note (the folder/settings
  parts still run).
- Multiple avatars in the scene (PC + Quest) → operate per-avatar; the report lists each.
- Unknown/ambiguous state → report findings and do nothing risky; let the AI/user decide.
- A pre-apply **plan** is returned by `inspect` so the Advisor HUD can show it before the
  user approves `apply`.

## Testing

- **Key-free EditMode tests** for `ProjectTemplateEngine`: template JSON parsing; the
  **diff** (given a constructed GameObject with/without an Animator, descriptor, expression
  assets, folders → assert the computed missing-steps); idempotency (apply twice → second
  run reports zero changes). VRC-reflection steps are guarded/mocked so tests run without
  the SDK.
- **Bridge round-trip:** `manage_project_template {action:"inspect"}` on the live Leaf
  scene → report names `LEAF`/`LEAF QUEST`, lists what's present vs missing.
- **Visual confirmation:** after `apply`, screenshot the Hierarchy/Inspector to confirm
  the descriptor + assets exist (per the mandatory-screenshot rule).

## Advisor HUD integration

The Advisor's **"Set up my project"** quick-ask (or a new card) → AI calls
`manage_project_template inspect` → posts an action card summarizing findings +
an **"Apply setup"** action → user Approve → AI calls `apply` → posts the result.
Reuses the Phase-1/2 HUD plumbing; no new HUD work required.

## Phasing

- **Phase 1:** `inspect` + `apply` + `list` for `avatar-pc` — descriptor + viewpoint +
  expression assets + folder organization, idempotent + non-destructive, with the diff
  engine + key-free tests. Usable end-to-end on a PC avatar.
- **Phase 2:** `avatar-quest` variant + PC↔Quest pairing detection + the scoped
  project-settings step + the package/prefab interaction-notes knowledge layer.

## Out of scope (for now)

Package/SDK auto-install (VCC's domain; notes only). World templates. Avatar *creation*
from a mesh (we set up imported/existing avatars or scaffold an empty starter, not model
an avatar). Destructive cleanup/reorganization of an existing hierarchy (we add, we don't
restructure what the user has).

## Assembly placement

`manage_project_template` handler lives with the dispatcher (Core). `ProjectTemplateEngine`
+ definitions under `Editor/Templates/` (Core). Reflection helpers for VRCSDK types are
self-contained. No new assembly.
