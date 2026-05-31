# Autonomous Unity MCP (v2)

VRChat-native **Model Context Protocol** server for **Unity 2022.3.22f1** — a superset of
Unity's AI Assistant (which needs Unity 6). Two halves:
- `com.autonomous-unity.mcp/` — the Unity Editor package (C#). Package name: `com.autonomous.unity.mcp`.
- `server/` — the Node/TypeScript MCP relay (npm workspace).

## Commands
```bash
npm install
npm --workspace server run build         # tsc; must exit 0
node server/dist/smokeTest.js            # planner/executor smoke (fake bridge, no Unity)
```
Unity-side has **no headless test harness** — verify by opening the project in 2022.3.22f1
(console must be error-free) and running EditMode tests via the live MCP bridge.

## Bridge
- Transport host binds **HTTP 127.0.0.1:8080** (`POST /mcp/tool` body `{"tool","params"}`) and TCP 8081, only when `AutoConnect` is on.
- **Registry tools** go through the permission gate; **legacy switch tools** bypass it.
- Run tests over the bridge: `refresh_unity` → `run_tests {mode:"editmode"}` → poll `get_test_job` (jobs are SessionState-persisted, so they survive the domain reloads a run triggers).
- **CRITICAL — editing this package over the bridge does NOT pick up source changes.** The package is `file:`-mounted into Leaf from the repo, and Unity never *imports* changes to an external local-package folder: `AssetDatabase.Refresh` AND `CompilationPipeline.RequestScriptCompilation()` both recompile from Unity's **cached** copy, so the bridge keeps serving the **stale last-good assembly** and `get_compilation_errors` reports false-clean. Only a **package re-resolution** re-reads the source from disk — triggered by a Package Manager op (add/remove/Refresh in the PM window), a manifest change on editor focus, or a **Unity restart**. Verified 2026-05-31 by injecting a deliberate compile error that stayed undetected through refresh+recompile until a package op forced re-resolve. **Prove an edit is live before trusting any verification** (e.g. a build-stamp in `health_check`, or a deliberate-error probe). A junction/symlink embed of the package under `Leaf/Packages/` (instead of `file:`) would restore normal live-iteration — open question.

## Architecture
- `Editor/Core/ToolRegistry.cs` — registry-first dispatch; legacy switch in `AutonomousMcpToolDispatcher.cs` is the fallback.
- `PermissionStore` (Ask/Agent + per-client + `autoApprove*`, in `ProjectSettings/`/`Library/`), `CheckpointStore`, `GeneratorRegistry`.
- `Editor/Tools/*` one tool family per file (auto-registered via `[InitializeOnLoadMethod]`).
- `Editor/Tests/*` — EditMode self-test suite (`AutonomousMcp.SelfTest`).

## Gotchas (each cost real debugging time)
- **Mount the package SUBFOLDER, not the repo root**, in a Unity project's `Packages/manifest.json`:
  `"com.autonomous.unity.mcp": "file:.../UnityAutonomousMCP/com.autonomous-unity.mcp"`. Mounting the
  repo root makes Unity import `node_modules/` + `server/` → ~40s domain reloads + ~23k GUID conflicts.
- Package must be in the project manifest's **`"testables"`** array or its test assembly never compiles/appears. Manifest changes only re-resolve on **editor focus**.
- Test asmdef: use `overrideReferences:true` + `precompiledReferences:["nunit.framework.dll","Newtonsoft.Json.dll"]` + explicit `UnityEngine/UnityEditor.TestRunner` refs. The `optionalUnityReferences:["TestAssemblies"]` style **strips Newtonsoft** from test asms.
- **Unity `??` pitfall**: `GetComponent<T>() ?? AddComponent<T>()` ignores Unity's overloaded `==` (fake-null). Use `var c = GetComponent<T>(); if (c == null) c = AddComponent<T>();`.
- Target **2022.3 APIs, not Unity 6**: uGUI (not UI Toolkit UXML source-gen), legacy `UnityEditor.AI.NavMeshBuilder` (not `com.unity.ai.navigation`), Cinemachine 2.x via reflection.
- **Animate Transform Euler rotation with `localEulerAnglesRaw.<axis>`, not `localEulerAngles.<axis>`**: the latter lands only in the editor-only `m_EulerEditorCurves` channel (no runtime rotation, `AnimationUtility.GetCurveBindings` returns it as 0); `*Raw` writes the real runtime `m_EulerCurves`. (Position/scale via `localPosition`/`localScale` are fine as-is.)
- **Generator dispatch budget:** `manage_generator` runs on the editor main thread and gets a **75s** `Invoke` timeout (all other tools keep **10s**), in `AutonomousMcpToolDispatcher.DispatchTimeoutMsFor`. Per-request timeouts are keyless **20s** / keyed **60s** (`FreeTierImageClient.RequestTimeoutMsFor`); request timeout always stays below the dispatch budget so the request, not the dispatcher, bounds the editor freeze. Keyless (Pollinations) throttles rapid repeats per-IP (402) — fails fast with a "set `GENERATOR_HF_TOKEN`" message.

## Policies
- **Generators are BYOK only.** Keys come from the user's own env vars (`GENERATOR_*`). HuggingFace = user's own tokens; Pollinations = a legitimate free *keyless public* API. **Never** harvest/scrape/rotate third-party API keys, and never drive the consumer ChatGPT/Claude web subscription via a browser session (it's not API-accessible; that's scraping) — refuse both outright.
- **Reliable generation = BYOK HuggingFace** (`GENERATOR_HF_TOKEN`): account-based free quota with rotation/backoff already built. The keyless Pollinations path works **single-shot only** — it throttles rapid repeats per-IP (2nd request hangs, then HTTP 402). See `docs/superpowers/findings/2026-05-29-keyless-generation-throttle.md`. Single-shot gen works for Texture/Sprite/Material/Cubemap; Audio/Model3D/Animation/TerrainLayer were parallel-added and are unreviewed/unverified.

## Branch / remote model
- `feat/unified-mcp` is the v2 mainline; public `origin` = `KinofSin/UnityAutonomousMCP`, private `private` = `KinofSin/UnityAutonomousMCP-v2` (pushed as `main`).
- Commit and push freely to the **private** remote (`private` = `KinofSin/UnityAutonomousMCP-v2`) when work is verified — the user authorized this once off the public repo (2026-05-31). The manual-only rule applied to the public `origin`; keep that care for any public push.
- Live Unity test project: `C:\VRChatProjectsAlcom\Leaf`.
