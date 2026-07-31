# Blender → Unity 2022.3 / VRChat Export Contract

> **Stopgap.** This repo has no Blender bridge yet (`bpy` side-channel is planned later).
> Until then, treat this file as the export contract the agent must assume — do **not**
> invent axis/scale/naming rules. If a `.blend` must be inspected or edited, ask the user
> (or wait for the bpy bridge).

Target stack: **Blender 3.x/4.x → FBX (or GLB via glTFast/UnityGLTF) → Unity 2022.3.22f1 → VRChat SDK3**.

## Axis, units, scale

- Blender is **Z-up**; Unity is **Y-up**. Prefer the FBX exporter's "Apply Transform" / experimental "Apply Transform" so the armature arrives upright without a 90° root rotation.
- Work in **meters** (Blender Scene Unit Scale = 1.0). A 1.6–1.8 m character should land near that height in Unity.
- **Apply** Location / Rotation / Scale on meshes and the armature before export (`Ctrl+A`). Unapplied scale is the #1 cause of stretched PhysBones and wrong Humanoid mapping.
- **Never ship negative scale.** Mirroring via `Scale X = -1` breaks normals, PhysBones, and Quest. Mirror with modifiers or edit-mode tools, then apply.

## Mesh hygiene

- Apply modifiers on export (or apply them in the stack before exporting). Subdivision / mirror / armature left live in the file will surprise Unity.
- **Triangulate** (or let the exporter triangulate). Ngons and concave quads create bad silhouettes and lightmap UVs.
- Keep material **slot order** stable — Unity's `Renderer.sharedMaterials` indices match Blender slot order. Renaming slots mid-project remaps materials silently.
- One UV set for albedo; a second for lightmaps only if the asset is for a world. Avatars usually need one clean UV0.
- Avoid overlapping islands on UV0 when the mesh will be atlased later.

## Armature & Humanoid

- Single armature root. Bone names should match Unity Humanoid expectations (or have a clear mapping): `Hips`, `Spine`, `Chest`, `Neck`, `Head`, `LeftUpperArm` / `RightUpperArm`, etc.
- Keep a clean hierarchy; do not parent mesh objects under random empties that won't survive FBX.
- **Bone weights:** ≤4 influences per vertex for Quest (Unity Skin Weights → 4 Bones). Clean weights before export; leftover tiny weights inflate bones and kill mobile rank.
- Rest pose should be a sensible T or A pose. Extreme rest poses confuse Humanoid auto-mapping.

## Shape keys / visemes

- Shape key names survive FBX → Unity as blendshape names. Name them for VRChat visemes when shipping lip sync (`vrc.v_sil`, `vrc.v_pp`, `vrc.v_ff`, …) or keep a clear custom set and wire it on the descriptor.
- Basis shape must be index 0. Do not delete/reorder shape keys after Unity materials or anims already reference them by name.
- Keep shape-key ranges 0–1. Values outside that range fight the VRChat lip sync driver.

## Materials

- Blender Principled BSDF does **not** become Poiyomi. Export creates slots; assign Poiyomi / lilToon / Quest materials in Unity.
- Pack only the textures you need (albedo, normal, mask). Giant unused UDIMs waste VRAM and Quest rank.
- Prefer PNG/TGA for masks with hard alpha; avoid leaving Blender packing absolute paths that break on another machine.

## FBX exporter checklist (Blender)

- Selected Objects only (armature + meshes).
- Apply Scaling: **FBX Units Scale** (or FBX All) — be consistent across the project.
- Forward `-Z`, Up `Y` (Blender FBX defaults for Unity).
- Armature → Add Leaf Bones: **off**.
- Bake Animation only when intentionally exporting clips; avatar base meshes usually leave this off.
- For GLB/glTF: confirm the Unity project has glTFast or UnityGLTF before relying on that path (`3d-model-import-review` skill).

## After import in Unity (do not guess — measure)

- Run `node .claude/tools/scene-dossier.mjs avatar <Name>` and read the artifact before changing materials or meshes.
- Check Humanoid mapping, normals, and blendshape names with `scan_avatar` / `scan_armature`.
- Texture max size + Android override belong in Unity importers (`manage_texture`), not Blender.

## Out of scope until the bpy bridge exists

- Reading or writing `.blend` files from this agent.
- Driving Blender over a socket / `bpy.ops` remote.
- Auto-retargeting or auto-weighting inside Blender.
