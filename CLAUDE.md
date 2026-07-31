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

CI (`.github/workflows/`): `ci.yml` runs on every push — Node relay build + smoke + `node --check`
on every `.claude/**/*.mjs` (a hook syntax error otherwise breaks every session in this repo, and
nothing else would catch it). `npm install`, not `npm ci`: `package-lock.json` is gitignored, so
there is no lockfile to install from. `unity-tests.yml` is `workflow_dispatch`-only and needs
`UNITY_LICENSE`/`UNITY_EMAIL`/`UNITY_PASSWORD` secrets; expect VRC-dependent tests to skip there
because the VRChat SDK does not resolve from a bare CI checkout.

## Bridge
- Transport host binds **HTTP 127.0.0.1:8080** (`POST /mcp/tool` body `{"tool","params"}`) and TCP 8081, only when `AutoConnect` is on.
- **Registry tools** go through the permission gate; **legacy switch tools** bypass it.
- Run tests over the bridge: `refresh_unity` → `run_tests {mode:"editmode"}` → poll `get_test_job` (jobs are SessionState-persisted, so they survive the domain reloads a run triggers). `run_tests` also takes `testFilter` (regex over full test names, e.g. `"GeneratedAssetWriter"`) + `category` to run a subset.
- **Dev loop — recompiling package edits in Leaf (SOLVED 2026-05-31 via junction embed).** The package is mounted into Leaf as an **embedded** package via a Windows directory **junction** `Leaf\Packages\com.autonomous.unity.mcp` → the repo's `com.autonomous-unity.mcp` folder (the old `file:` manifest entry was removed). Workflow: **edit in the repo → click into Unity → it recompiles from live source** (Auto Refresh is on in Leaf, so a focus alone compiles; `Ctrl+R` is the manual equivalent). Errors surface against the `Packages\com.autonomous.unity.mcp\…` path.
  - **Why the old `file:` mount failed:** Unity never *imported* changes to an external local-package folder — `AssetDatabase.Refresh` AND `CompilationPipeline.RequestScriptCompilation()` recompiled from Unity's **cached** copy, so only a full package re-resolution (PM op / restart) picked up edits.
  - **Focus is NOT required (measured 2026-07-31, corrects the earlier claim).** With Spotify holding foreground the whole time (verified before *and* after via `GetForegroundWindow`), adding a package `.cs` file and calling `refresh_unity` recompiled in **23s** — `buildStamp c169b785 → ba1225bd`. `refresh_unity` returns `recompileRequested:true, isCompiling:true` and the editor compiles in the background. So `unity-compile-fix` and any code-edit loop can run unattended; do not ask the user to alt-tab. The old "defers while unfocused" note predates the junction embed, when the `file:` mount meant nothing recompiled at all.
  - **Verify compiles with `read_console {level:"error"}`, NOT `get_compilation_errors`.** The latter reads a stale last-good assembly and reports false-clean; the error-level console reliably shows real CS errors (e.g. it caught a `CS0136` the other tool missed). A failed compile keeps the old assembly live, so an edit silently "doesn't take" — confirm an edit is live before trusting verification (deliberate-error probe, or report actual state like a window-capture's real dimensions).
  - **Rollback:** delete the junction (`Leaf\Packages\com.autonomous.unity.mcp`) and re-add the `file:` line to `Leaf/Packages/manifest.json`.
- **Advisor HUD** (in-Unity two-way advisor surface, `Window/Autonomous MCP/Advisor`): AI→HUD via `hud_post {text,level}` (advice) and `hud_post_card {title,body,id,actions}` (action card with Approve/Why/Dismiss). HUD→AI via an outbox the user fills (note/selection/console/screenshot, card Approve, quick-ask) — drained with `hud_poll`, and every response is piggybacked with `hudOutbox:{pending:N}`. State is `AdvisorStore` (`Editor/Advisor/`), SessionState-persisted. The HUD never executes fixes — Approve relays consent; the AI acts with its own permission-gated tools. **Round trip verified end to end with a human (2026-07-31)** — card buttons, notes, and attached selections all arrive via `hud_poll` intact. An attached selection is a cheap way to learn what the user is looking at, components included. Card titles wrap, so they can be full questions.
- **`capture_screenshot {source:"editor"}`** composites every dock-area `GUIView` into one image of the whole main window — the fastest way to see layout, which windows are open, and console state at a glance. Sources are `scene` / `game` / `editor` / `window` (`editor` and `window` are undocumented in `supportedActions`). Pass an **absolute** `save_path`; the AI's cwd is this repo, not the Unity project. Orientation is guarded by `SystemInfo.graphicsUVStartsAtTop` — the composite must not flip tiles where the UV origin is already at the top (D3D), or panels land correctly with their contents upside down. Note: the Transform inspector renders in Russian because `Assets/MyScripts/Editor/EasyTransforn.cs` registers `[CustomEditor(typeof(Transform), true)]`; the editor itself is English (Unity 2022.3 does not even ship a Russian language pack), so menu paths are unaffected.
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

## Agents & Skills index (domain layer)

Engine stance remains **Unity 2022.3.22f1 + VRChat SDK3**. Always-loaded references:

- `.claude/docs/vrchat-reference.md` — SDK3, Avatars 3.0, PhysBones, PC/Quest ranks, Udon/worlds
- `.claude/docs/unity-2022-reference.md` — 2022.3 APIs, uGUI, asmdef/testables, `??` pitfall, import/domain-reload
- `.claude/docs/blender-reference.md` — Blender→Unity/VRChat export contract (docs only; no bpy bridge yet)

**Agents** (`.claude/agents/`):

| Agent | Use for |
|---|---|
| `vrchat-specialist` | SDK3 routing, Expressions, PhysBones, upload/project gotchas |
| `vrchat-avatar-optimizer` | Hit Good/Excellent avatar rank; Quest twins |
| `vrchat-world-optimizer` | Lightmaps, occlusion, Udon hot paths, draw calls |
| `unity-2022-specialist` | 2022.3 API correctness, editor scripting, asmdef/tests |

**Skills** (`.claude/skills/`):

| Skill | Use for |
|---|---|
| `run-autonomous-unity-mcp` | Build Node relay, offline smoke, live bridge drive, EditMode tests |
| `vrchat-avatar-audit` | Measured avatar audit + bounded optimization loop |
| `vrchat-world-audit` | Measured world-scene audit + bounded optimization loop |
| `unity-compile-fix` | Semi-automatic console/compile error loop (`unity-verify.mjs`) |
| `3d-model-import-review` | FBX/GLB ModelImporter + glTFast/UnityGLTF soft-detect review |

**Optimization loop** — the audit skills measure over the bridge, they do not parse prefab YAML:

- **Step 0 — scene/avatar dossier** (before guessing inspector/material state): `node .claude/tools/scene-dossier.mjs avatar <goName>` or `… scene`. Pulls sectioned `unity_perception {action:"dossier"}` calls, writes `.claude/.vrc-state/dossier-<slug>.md` + `.json`, prints a ~40-line summary. Grep the artifact for a mesh/material; do not dump full Poiyomi property lists into chat (~87k tokens for a 28-mat avatar). `verify <slug>` rechecks `stateHash` (exit `1` = stale). Locked Poiyomi (`Hidden/Locked/…`) is flagged — per-property values are not meaningfully readable until unlocked.
- **`execute_csharp` is fully usable — the BCL restriction is gone** (2026-07-31). Two separate
  bugs stacked on it. References go in an mcs **response file**, not on the command line: Leaf
  loads ~325 assemblies and `/r:` per reference blew the ~32 KB Windows limit, failing every
  snippet with "The filename or extension is too long" regardless of length. Then naming any of
  `List<T>`, `Dictionary<,>`, `StringBuilder`, `System.IO.Path` — most of `System.*` — failed with
  "defined multiple times". That was **not** the netstandard facades, the obvious suspect: dropping
  netstandard (321 refs) or the whole `Facades/` directory (313 refs) reproduces the error exactly.
  mcs *implicitly* references its own mscorlib, so passing the loaded copy with `-r:` too gives it
  two physically different files defining the same types. Fix is one exclusion — never reference
  the loaded `mscorlib`; keep every facade, since they forward to the implicit core and LINQ's
  signatures need `netstandard`. Verified: `List`/`Dictionary`/`StringBuilder`/`Path`/LINQ/
  `AssetDatabase`/`Selection` in one snippet. `all-loaded` remains a fallback, and a genuine
  snippet error still reports plainly after a single compile rather than a strategy chain.
- **Generators**: `Animation` is procedural/offline and always available (`spin`/`bob`/`pulse`/
  `blink`, verified to emit real curves). `TerrainLayer` and the image kinds work keyless through
  Pollinations but throttle hard on repeats ("Queue full for IP") — set `GENERATOR_HF_TOKEN` for
  reliability. `Audio` requires `GENERATOR_HF_TOKEN`, `Model` requires `GENERATOR_MESHY_API_KEY`;
  unconfigured they report a clear status and fall back to the stub. `Model`'s Meshy poll blocks
  the **editor main thread**, so its wait is capped below the 75s dispatch budget — a long
  generation fails cleanly and returns the taskId rather than freezing the editor for minutes.
- **`unity_optimization oversized_textures` is project-wide and dimension-based** — it judges by
  the larger dimension, so it is not a scene metric and never moves when you optimize a scene.
  Degenerate data textures (Poiyomi TPS baked mesh strips, 8190×2) are excluded via
  `min_dimension` and reported separately as `skippedDataTextures`; **never shrink those**, they
  encode mesh data. For a real scene/avatar texture figure use the dossier.
- **All harnesses share `.claude/tools/bridge.mjs`** — one request path with reconnect. Any AssetDatabase write can trigger a domain reload that tears down the HTTP listener for a few seconds, and a one-shot `fetch` turns that routine blip into a dead run. Retries are classified by whether the editor can possibly have executed the tool. **Only `refused` (nothing listening, so never delivered) is provably safe.** `busy` is *not*: the HTTP 503 comes from `AutonomousMcpMainThread` timing out the **wait**, and the queued action is never cancelled, so the tool may still be running — it is ambiguous exactly like `timeout` and `network`. All three are retried only when the caller passes `idempotent:true` (see `READ_ONLY_TOOLS`). Never mark a mutating call idempotent; it could apply twice.
- `.claude/tools/vrc-loop.mjs` — records a baseline via `scan_avatar` / `unity_optimization` into `.claude/.vrc-state/` (gitignored) and prints a delta table each pass. **Avatar passes also pull `unity_perception dossier` textures**, because `scan_avatar` reports no texture data at all: without it the loop could not see its own primary Tier-1 lever, so a working `manage_texture` pass measured as an all-zero delta and the "stop after two unchanged passes" rule ended the run. Tracked as `Texture VRAM (MB)` and `Textures > 1024`. **`Texture VRAM (MB)` reads ~2× real GPU VRAM** — it comes from `Profiler.GetRuntimeMemorySizeLong`, which in the Editor counts the CPU-side copy as well as the GPU one (verified: a DXT5 2048² with mips reports 11.19 MB where the GPU cost is 5.59 MB). It is exact for **deltas**, so use it to compare passes; halve it before comparing against VRChat's texture-memory ranks. Exit codes drive the loop: `0` improved/unchanged, `1` regressed, `2` bridge unreachable. Avatar resolve uses `search_hierarchy {include_inactive:true}` → `instanceId` because VRChat twins are normally inactive and `GameObject.Find` skips them.
- One change per pass, 5 passes max. Tier 1 (AAO TraceAndOptimize if installed/off, then `manage_texture` `set_import_settings`, importer settings) is autonomous; Tier 2 (component removal) needs a `manage_checkpoint`; Tier 3 (geometry, bones, material merges, lightmap rebake) always asks.
- **Mesh memory cannot be measured in the Editor — the two Profiler caveats cut opposite ways.** Toggling a model's Read/Write off left `Profiler.GetRuntimeMemorySizeLong(mesh)` at *exactly* 50.45 MB before and after on LEAF, because the Editor keeps mesh data resident whatever the flag says; the CPU copy is only stripped in a player build (the uploaded avatar). So texture VRAM over-reports ~2× but is exact per-delta, while mesh memory does not move at all and is not even usable as a delta. Never quote a mesh-memory saving from an Editor measurement — say the flag changed and that only an upload confirms it.
- **Avatar Cleanup window** (`Window/Autonomous MCP/Avatar Cleanup`, or the Advisor's **Cleanup** button) — the same `AvatarCost` model the `cost` section reads, rendered as a sortable table with tick-boxes and a live "leaves N polys (rank)" footer. **This is the one place the MCP UI edits the scene itself rather than queuing consent**, and only because deleting a scene object is natively undoable: every removal is a checkpoint *plus* one collapsed `Undo` step. Asset edits stay with the agent — they are not undoable. It refuses to delete the avatar root, warns when a selected object is **enabled**, warns that a prefab-instance child delete is an override, and **requires a second confirmation for FX/VRCFury/MA-driven objects**. Bulk select is **Select undriven** — never "all disabled". Columns include exclusive VRAM, PhysBones, and Driven by; a dimmed `~ …` in Driven by means property animation only and is not a reason to keep the object.
- **`dossier` section `cost` — per-object cost attribution.** Answers "what should I delete?" without hand-writing a query: polygons, material slots, exclusive/shared VRAM, PhysBones, `drivenBy` (animation FX / VRCFury / MA), `ifRemoved`, inactive roll-up split into **driven vs undriven**, and `twins` (e.g. LEAF QUEST). **`driven` means *controlled*, not merely animated**: an `m_IsActive`/`m_Enabled` curve or a direct VRCFury/MA reference, both of which break on delete. Blendshape and material curves set `animatedOnly` instead and are safe to remove — counting them was enough to mark every renderer on LEAF driven, because one Hue Shift slider targets every material. Each `drivenBy` entry carries `controls` so the split is inspectable. **Rank thresholds live only in `StateDossier.PolygonTier`/…; `PcBudgets` is projected from them.** `"Over"` = past the highest published tier. Bones are **not** reclaimed by deleting a renderer.
- **VRChat's stats count *disabled* renderers**, but **disabled ≠ unused.** On LEAF all 5 disabled objects (86,619 polys) are genuinely controlled wardrobe (`Dog`, `Rabbit`, `Hair Swap`, and `FT_Debug` via VRCFury `BlendShapeLink`) — deleting them breaks the menu, and the avatar has **zero** free objects. Its FX controller carries 15 activeness toggles; only `Body` and `TOP Bodysuit` are animated without ever being switched off. Quantify in **polygons**, not verts. Check `drivenBy` / `inactive.undriven` before recommending removal.
- **`unity_ndmf { action: "bake_cost", target }`** — NDMF `ManualProcessAvatar` on a clone, `AvatarCost` diff, destroy clone. Converts "editor hierarchy ≠ uploaded avatar" into a number. Dispatch timeout is 75s (same as generators). Side effect: may leave `Assets/ZZZ_GeneratedAssets`.
- **`get_installed_packages` reads VPM.** Merges `manifest.json` + `vpm-manifest.json` + embedded `Packages/*/package.json`, detects ALCOM/VCC/VPM, and returns `recommendations` (AAO / d4rk / TexTransTool) with install steps — **never auto-installs**.
- **Checkpoints cover assets copy-on-first-touch.** `CheckpointStore.CaptureAsset(path, tool)` runs *before* each asset write (`manage_texture set_import_settings`, `unity_importer set_property`, `manage_material` `set_property`/`set_shader`/`assign_texture`, `manage_scriptable_object set_property`), storing the asset **and its `.meta`** — importer settings live in `.meta` and `SaveAndReimport` is not undoable. First capture per path per checkpoint wins, so stored bytes are the checkpoint-time state. If no checkpoint exists one is auto-created (`auto-before-<tool>`), so an autonomous edit is never unrecoverable. `restore` takes `include_scene:false` to revert asset edits without reopening the scene. Still not a full project rollback: assets no tool touched are not stored.
- `manage_checkpoint create` no longer force-saves a dirty scene. A dirty scene is snapshotted via `SaveScene(saveAsCopy:true)` through a temp asset, so unsaved work is captured *and* left unsaved.
- `.claude/hooks/require-checkpoint.mjs` enforces the Tier-2 checkpoint. It is **inert unless `.claude/.vrc-state/` holds a baseline**. Manual HUD/menu checkpoints satisfy it via a bridge `manage_checkpoint list` query. Fail open if Unity is closed.
- `.claude/tools/unity-verify.mjs` — `refresh_unity`, **wait for the compile to finish**, then `read_console {level:"error"}` (never `get_compilation_errors`). Exit `0` clean / `1` errors / `2` bridge down. Runs unattended: the editor recompiles in the background, no focus needed. It polls `health_check` until `isCompiling`/`isUpdating` are clear for three consecutive samples — reading the console straight after `refresh_unity` returns the *previous* compile's errors and prints `clean` over a fresh syntax error. Trust `clean` only when it also printed `compiled in Ns` or made no change.
- `.claude/hooks/hud-drain.mjs` — `UserPromptSubmit` injects a `hud_poll` nudge when the Advisor outbox has pending items; `Stop` blocks once per ~60s (and respects `stop_hook_active`) so queued HUD items are not abandoned. Manual checkpoint: Advisor toolbar **Checkpoint** or `Window/Autonomous MCP/Create Checkpoint`.
- `.claude/tools/unity-supervisor.mjs` — `status` / `ensure` / `enable-autoconnect`. Turns "Unity is closed" from a dead end into a recoverable state: `ensure` fast-paths when the bridge answers, refuses to launch over a live editor (Unity rejects a second instance, and the real fault is almost always AutoConnect), otherwise launches with its own `-logFile` and waits. Exit `0` ready / `1` not ready / `2` misconfigured. Project comes from `--project`, `UNITY_PROJECT_PATH`, or the gitignored `.claude/unity-project.json`; the editor is **derived from the project's `ProjectVersion.txt`** rather than hardcoded, because opening a VRChat project with the wrong editor silently upgrades it.
  - **Bridge identity:** `health_check.projectPath` is `Application.dataPath`; its parent must match the configured project. A mismatch is a hard error (wrong editor answering on port 8080).
  - **`MCP_AUTO_LAUNCH=1` or `--ensure`:** `unity-verify` / `vrc-loop` / `scene-dossier` call `unity-supervisor ensure` on a `refused` bridge. Off by default so it never launches Unity under someone at the keyboard. SessionStart (`vrc-session-note.mjs`) reports bridge up/down.
  - **`AutoConnect` (EditorPrefs `AutonomousMcp.AutoConnect`, default `false`) is the thing that makes unattended runs possible at all.** With it off, a launched editor is running and permanently unreachable — indistinguishable from a hang. It cannot be set from outside the editor, hence `AutonomousMcpBootstrap.EnableAutoConnect` for `-batchmode -quit -executeMethod`. **When the bridge is already up, set it through `execute_csharp` instead** — far cheaper than the batch round trip, which needs the editor closed. `AutonomousMcpSettings` is `internal`, so go via `EditorPrefs` directly.
  - On timeout it greps the launch log for the failure modes that all look like a hang from outside: the **Safe Mode prompt** (compile errors block startup behind a dialog), a licence/sign-in dialog, and a stale `Temp/UnityLockfile`. A bare timeout tells you nothing; the log almost always does.

Studio org layer (technical-director / producer hierarchy) is **not** installed — optional later if structured design gates are wanted. Do not add a local `code-review` skill; the global `code-review@claude-plugins-official` plugin already covers that.
