# VRChat SDK3 Reference (Unity 2022.3.22f1)

Always-loaded stance for this repo: **Unity 2022.3.22f1 + VRChat SDK3**. Do not recommend Unity 6 APIs, UI Toolkit runtime source-gen, or SDK2 patterns unless the user is explicitly migrating legacy content.

Live test project: `C:\VRChatProjectsAlcom\Leaf`. Package tools reach VRCSDK through reflection (`Editor/Templates/VrcReflection.cs`) and skip cleanly when the SDK is absent.

## Look it up, do not guess

This file holds only the **stable** stance. Anything version-specific or tool-specific is served live by the bridge and is more current than this document:

| Need | Use |
|---|---|
| Conventions/best practices across 150+ VRChat tools, 21 categories | `get_vrc_knowledge` |
| Install/setup/test steps, VPM repo URLs, common errors (60+ tools) | `get_install_guide` |
| What is actually installed in this project | `get_installed_packages` |
| Shader families present (Poiyomi, lilToon, SCSS, ORL) | `list_shaders` |
| Measured avatar stats + budgets | `scan_avatar`, `scan_armature` |
| Measured scene stats | `unity_optimization` |
| Prefab/tooling interaction traps | `manage_project_template` `notes`, or `Editor/Templates/InteractionNotes.json` |

Exact numeric performance-rank thresholds move between SDK releases. Report **measured** numbers from `scan_avatar` and send the user to the SDK control panel for the official rank rather than quoting caps from memory.

## SDK3 vs SDK2

| Topic | SDK3 (current) | SDK2 (legacy) |
|---|---|---|
| Avatars | Avatars 3.0 (playable layers, Expression Parameters/Menus) | SDK2 gestures / Animation Controllers only |
| Physics jiggle | **PhysBones** | DynamicBones (do not add new) |
| Worlds | Udon / UdonSharp | obsolete world scripting |

## Avatars 3.0

- **VRC Avatar Descriptor** — viewpoint, lip sync, eye look, playable layers, expressions.
- **Playable layers** — Base, Additive, Gesture, Action, FX. Unused layers still cost; keep controllers lean.
- **Expression Parameters** — synced params share a bit budget (`scan_avatar` reports cost against it). Bools cost 1, Int and Float cost 8 each, so prefer bools/triggers and reserve Int/Float for genuinely continuous state.
- **PhysBones count against performance rank** (components and affected transforms). Keep chains short; avoid overlapping PhysBone roots on one bone. Migrate any leftover DynamicBones.
- **PC ↔ Quest twins** — ship both (e.g. `LEAF` / `LEAF QUEST`). Quest gets a stripped material set, lower poly, and Quest-safe shaders.

Helper: `manage_project_template` (`inspect` / `apply` / `notes` / `settings`) idempotently scaffolds descriptor, viewpoint, Expression Menu/Parameters, and `Assets/_Project/<Avatar>/` folders.

## Quest constraints (the ones that do not move)

- Only Quest-compatible shaders. **No Poiyomi on Quest** — separate Quest material set plus fallback.
- Lock Poiyomi materials on PC **before upload**; unlocked shaders blow past keyword limits and bloat the avatar. Unlock to edit, re-lock to ship.
- Quest caps on materials, polys, and texture memory are enforced harder than PC. Texture max size and crunch compression are the cheapest wins.

## Tooling coexistence

- **Modular Avatar vs VRCFury** — both are non-destructive build plugins and can coexist, but use one system per feature or you get duplicate menu controls.
- **Install order** — avatar base prefab first, then accessories parented **under** the avatar so Bone Proxy / Merge Armature can find the armature. Scene-root accessories will not merge.

## Worlds (Udon / UdonSharp)

- Keep `Update` cheap: no per-frame allocation or `GetComponent`. Prefer event-driven sync over continuous.
- Lightmapping, occlusion culling, static batching, and draw-call reduction dominate world cost; Quest worlds also need aggressive LOD and texture budgets.
- Audio: too many concurrent spatialized sources hurts Quest.
- `unity_optimization`'s `draw_call_estimate` is explicitly rough (enabled renderers times non-null shared materials). Use it as a relative signal across passes, not an absolute.

## Project settings / upload

- **Color space = Linear** (VRChat-recommended). `manage_project_template settings` reports current vs recommended read-only; writing needs `apply:true` and reimports assets (75s dispatch budget).
- Upload through Creator Companion / ALCOM with the SDK3 builders. Validate rank in the SDK control panel before uploading.

## Routing

- General SDK3 questions, avatar-vs-world routing → `vrchat-specialist` agent
- Avatar rank strategy → `vrchat-avatar-optimizer` agent; measured audit + optimization loop → skill `vrchat-avatar-audit`
- World perf strategy → `vrchat-world-optimizer` agent; measured audit + loop → skill `vrchat-world-audit`
- FBX/GLB importer settings → skill `3d-model-import-review`
- Build / smoke / bridge / EditMode tests → skill `run-autonomous-unity-mcp`
