---
name: 3d-model-import-review
description: "Reviews FBX/GLB/glTF import settings for Unity 2022.3.22f1 VRChat projects — ModelImporter (scale, normals, tangents, blendshapes, animation, materials), humanoid/generic rig, and soft-detection of glTFast/UnityGLTF for GLB from manage_generator Model. Use when asked to review 3D model import settings — not for avatar rank audits, world lightmaps, or MCP build/smoke."
argument-hint: "[model-asset-path]"
user-invocable: true
allowed-tools: Read, Glob, Grep, Bash, AskUserQuestion
model: sonnet
---

# Skill: 3D Model Import Review

Review how a **3D model asset** (FBX / GLB / glTF) is imported into Unity 2022.3 for VRChat avatar or world use.

## Phase 1 — Load context

1. Read `.claude/docs/unity-2022-reference.md` (AssetDatabase / soft-detect).
2. Read `.claude/docs/vrchat-reference.md` if the model is for an avatar or Quest twin.
3. Skim `com.autonomous-unity.mcp/Editor/Generators/FreeTierModelGenerator.cs` when the asset came from `manage_generator` Model (GLB + glTFast/UnityGLTF soft-detect).

## Phase 2 — Locate importer state

- Prefer the `.meta` next to the model for `ModelImporter` YAML (scale factor, mesh compression, blendShapes, animation type, material import mode, avatar setup).
- For GLB: check whether the project has **glTFast** or **UnityGLTF** (package manifest / soft-detect). Without one, a generated GLB may sit on disk without a loadable mesh prefab.

## Phase 3 — Checklist

| Area | Review |
|---|---|
| Scale | Factor matches VRChat / source DCC (often 1.0 for meters); avatar vs prop |
| Normals / tangents | Import vs calculate; normal map readiness |
| Blendshapes | On if face tracking / visemes need them |
| Rig | Generic vs Humanoid; bone mapping sanity for avatars |
| Animation | Import anims only if needed; avoid unused clips bloating |
| Materials | Naming / location; Quest will need separate material set later |
| Read/Write | Enable only if runtime mesh mutate needed (rank/memory cost) |
| Mesh compression | Off vs Low/Med/High trade-off for VRChat |
| GLB path | Importer package present? Prefab/mesh loadable after import? |

## Phase 4 — Report

1. **Summary** — Ready / Needs importer package / Misconfigured.
2. **Findings** — severity, setting, current vs recommended, why (avatar vs world).
3. **Generator note** — if from free-tier Model gen, mention soft-detect message paths and that missing glTF packages are expected until installed.
4. **Non-goals** — does not run performance rank audit (`vrchat-avatar-audit`) or world bake audit.

Ask before rewriting `.meta` importer settings (reimport can be slow and may break material assignments).
