# Unity 2022.3 LTS Reference (this repo)

Engine pin: **Unity 2022.3.22f1**. This MCP is a VRChat-native superset of Unity's AI Assistant (which needs Unity 6). Never suggest Unity 6-only APIs as the default path.

Package root to mount: `com.autonomous-unity.mcp/` (not the git repo root). Live project: `C:\VRChatProjectsAlcom\Leaf` (junction embed — see `CLAUDE.md`).

## API / stack stance (2022.3, not Unity 6)

| Prefer | Avoid (Unity 6 / wrong stack) |
|---|---|
| **uGUI** (`UnityEngine.UI`) for runtime UI | UI Toolkit UXML source-gen as the assumed runtime UI |
| Legacy `UnityEditor.AI.NavMeshBuilder` when needed | `com.unity.ai.navigation` as a hard dependency |
| Cinemachine **2.x** via reflection | Cinemachine 3.x APIs without a version gate |
| Built-in / URP as the project already uses | Assuming HDRP or Unity 6 render features |
| `AnimationUtility.SetAnimationClipSettings` for loop | Relying only on `clip.wrapMode` for modern clips |

## Unity `??` / fake-null pitfall

`UnityEngine.Object` overloads `==` so destroyed objects compare equal to `null`, but `??` and `?.` use raw C# reference equality and **miss** fake-null.

```csharp
// BAD — ignores Unity fake-null
var c = GetComponent<T>() ?? AddComponent<T>();

// GOOD
var c = GetComponent<T>();
if (c == null) c = AddComponent<T>();
```

Same rule for any `UnityEngine.Object` before coalescing or null-conditional chains.

## Animation curve bindings

Animate Transform Euler rotation with **`localEulerAnglesRaw.<axis>`**, not `localEulerAngles.<axis>`. The non-Raw path writes editor-only `m_EulerEditorCurves` (no runtime rotation; `GetCurveBindings` can show 0). Position/scale via `localPosition` / `localScale` are fine.

## Assemblies, tests, package resolution

- Mount the package **subfolder** in `Packages/manifest.json`:
  `"com.autonomous.unity.mcp": "file:.../UnityAutonomousMCP/com.autonomous-unity.mcp"`.
  Mounting the repo root imports `node_modules/` + `server/` → ~40s domain reloads + GUID storms.
- Package must be listed in the project's **`"testables"`** array or the test assembly never compiles/appears. Manifest changes re-resolve on **editor focus**.
- Test asmdef pattern for this package:
  - `overrideReferences: true`
  - `precompiledReferences: ["nunit.framework.dll", "Newtonsoft.Json.dll"]`
  - explicit refs to `UnityEngine.TestRunner` / `UnityEditor.TestRunner`
  - Do **not** use `optionalUnityReferences: ["TestAssemblies"]` — it strips Newtonsoft from test asms.
- Registry-first tools live in `Editor/Tools/*` (`[InitializeOnLoadMethod]`); legacy switch in `AutonomousMcpToolDispatcher` is fallback only.

## AssetDatabase / import timing

- After writing bytes to `Assets/`, call `AssetDatabase.ImportAsset` / `Refresh` before `LoadAssetAtPath`.
- Importers (TextureImporter, ModelImporter, glTF packages) may need a second import or settings apply before the typed asset is loadable.
- **Domain reload** wipes static/in-memory state. Persist cross-reload job state with `SessionState` (e.g. `run_tests` / `get_test_job`). Do not keep long-lived static caches of generation jobs without persistence.
- Unity **defers compilation while unfocused**. Bridge-only `refresh_unity` does not compile until the user focuses the editor. Verify compiles with `read_console {level:"error"}`, not `get_compilation_errors` (stale last-good assembly → false clean).

## Leaf junction mount (dev loop)

Leaf embeds the package via a Windows **junction** `Leaf\Packages\com.autonomous.unity.mcp` → repo `com.autonomous-unity.mcp`. Edit in repo → focus Unity → Auto Refresh recompiles from live source. The old `file:` external mount cached copies and ignored edits until PM re-resolve / restart.

## Generators / BYOK (editor main thread)

- Keys only from user env (`GENERATOR_*`). Never harvest third-party keys or scrape consumer ChatGPT/Claude web sessions.
- `manage_generator` dispatch budget **75s**; other tools **10s**. Keyless image requests ~20s, keyed ~60s — always below dispatch budget.
- Keyless Pollinations is **single-shot** friendly; rapid repeats throttle (HTTP 402). Reliable path = BYOK HuggingFace (`GENERATOR_HF_TOKEN`).
- Model generator writes GLB and soft-detects **glTFast** / **UnityGLTF** without compile-time references. Without an importer, the file lands on disk but mesh may not load as a Unity mesh asset.

## Routing for agents / skills

- 2022.3 API correctness, editor scripting, asmdef/tests → `unity-2022-specialist` agent.
- Build / smoke / bridge / EditMode tests → existing skill `run-autonomous-unity-mcp` (do not duplicate).
- FBX/GLB import settings → skill `3d-model-import-review`.
- VRChat-specific constraints → `.claude/docs/vrchat-reference.md` + VRChat agents.
