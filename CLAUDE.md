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
- Run tests over the bridge: `refresh_unity` → `run_tests {mode:"editmode"}` → poll `get_test_job` (jobs are SessionState-persisted, so they survive the domain reloads a run triggers). `run_tests` also takes `testFilter` (regex over full test names, e.g. `"GeneratedAssetWriter"`) + `category` to run a subset.
- **Dev loop — recompiling package edits in Leaf (SOLVED 2026-05-31 via junction embed).** The package is mounted into Leaf as an **embedded** package via a Windows directory **junction** `Leaf\Packages\com.autonomous.unity.mcp` → the repo's `com.autonomous-unity.mcp` folder (the old `file:` manifest entry was removed). Workflow: **edit in the repo → click into Unity → it recompiles from live source** (Auto Refresh is on in Leaf, so a focus alone compiles; `Ctrl+R` is the manual equivalent). Errors surface against the `Packages\com.autonomous.unity.mcp\…` path.
  - **Why the old `file:` mount failed:** Unity never *imported* changes to an external local-package folder — `AssetDatabase.Refresh` AND `CompilationPipeline.RequestScriptCompilation()` recompiled from Unity's **cached** copy, so only a full package re-resolution (PM op / restart) picked up edits.
  - **Still true:** Unity **defers compilation while unfocused**, so driving `refresh_unity` purely over the bridge (editor in background) will NOT compile — the user must focus Unity to apply edits, *then* verify over the bridge.
  - **Verify compiles with `read_console {level:"error"}`, NOT `get_compilation_errors`.** The latter reads a stale last-good assembly and reports false-clean; the error-level console reliably shows real CS errors (e.g. it caught a `CS0136` the other tool missed). A failed compile keeps the old assembly live, so an edit silently "doesn't take" — confirm an edit is live before trusting verification (deliberate-error probe, or report actual state like a window-capture's real dimensions).
  - **Rollback:** delete the junction (`Leaf\Packages\com.autonomous.unity.mcp`) and re-add the `file:` line to `Leaf/Packages/manifest.json`.
- **Advisor HUD** (in-Unity two-way advisor surface, `Window/Autonomous MCP/Advisor`): AI→HUD via `hud_post {text,level}` (advice) and `hud_post_card {title,body,id,actions}` (action card with Approve/Why/Dismiss). HUD→AI via an outbox the user fills (note/selection/console/screenshot, card Approve, quick-ask) — drained with `hud_poll`, and every response is piggybacked with `hudOutbox:{pending:N}`. State is `AdvisorStore` (`Editor/Advisor/`), SessionState-persisted. The HUD never executes fixes — Approve relays consent; the AI acts with its own permission-gated tools.
- **`manage_project_template`** (`inspect`/`list`/`apply`/`notes`): sets a VRChat avatar project to a pro baseline — `inspect` reports each scene avatar's state (PC/Quest, the PC↔Quest twin, per-step done flags), `apply` idempotently adds only what's missing (VRC descriptor + viewpoint, Expression Menu/Parameters, `Assets/_Project/<Avatar>/` folders; scaffolds a starter avatar if the scene is empty), `notes` returns the package/prefab interaction knowledge layer (`Editor/Templates/InteractionNotes.json` — VRCFury/Modular Avatar/Poiyomi/Quest "what messes with what"), `settings` reports current vs VRChat-recommended project settings (color space = Linear) read-only, writing them only with `apply:true` (impactful — reimports assets; gets the 75s dispatch budget). Non-destructive; VRCSDK components via reflection (`Editor/Templates/VrcReflection.cs`), skipped with a note if the SDK is absent. Pure diff/pairing engine in `Editor/Templates/ProjectTemplateEngine.cs`. The Advisor HUD's "Set up my project" quick-ask drives it.

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
- **OpenAI BYOK provider** (`provider:"openai"`, `GENERATOR_OPENAI_API_KEY`): real texture/sprite/material via OpenAI images API; split into `OpenAiImageSource` (network, key-gated) + `GeneratedAssetWriter` (key-free, unit-tested) under `Editor/Generators/`. Coexists with `free-tier` (registry last-write-wins per Kind+ProviderId). gpt-image-1 default (dall-e-* adds `response_format:b64_json`).
- **Reliable generation = BYOK HuggingFace** (`GENERATOR_HF_TOKEN`): account-based free quota with rotation/backoff already built. The keyless Pollinations path works **single-shot only** — it throttles rapid repeats per-IP (2nd request hangs, then HTTP 402). See `docs/superpowers/findings/2026-05-29-keyless-generation-throttle.md`. Single-shot gen works for Texture/Sprite/Material/Cubemap; Audio/Model3D/Animation/TerrainLayer were parallel-added and are unreviewed/unverified.

## Branch / remote model
- `feat/unified-mcp` is the v2 mainline; public `origin` = `KinofSin/UnityAutonomousMCP`, private `private` = `KinofSin/UnityAutonomousMCP-v2` (pushed as `main`).
- Commit and push freely to the **private** remote (`private` = `KinofSin/UnityAutonomousMCP-v2`) when work is verified — the user authorized this once off the public repo (2026-05-31). The manual-only rule applied to the public `origin`; keep that care for any public push.
- Live Unity test project: `C:\VRChatProjectsAlcom\Leaf`.
