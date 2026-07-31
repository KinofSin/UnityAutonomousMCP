# Pending checks

Work that is code-complete but **not yet verified in a running Unity Editor**, plus
open questions worth settling. `.claude/hooks/vrc-session-note.mjs` prints the
unchecked items at session start, so delete or tick a line once it is genuinely done.

Format: one `- [ ]` per item. Ticked (`- [x]`) items are ignored by the hook.

## Verified 2026-07-31 (Unity 2022.3.22f1 on Leaf)

- [x] Package compiles after the checkpoint change — clean, zero `error CS`, zero console errors.
- [x] `CheckpointStoreTests` — 7/7 pass, including the material round-trip flake risk.
- [x] Full EditMode suite — 260 tests: 235 pass, 18 fail, 7 skip. **No failure is ours.** 16 are
      the vendored YUCP package (several reference `C:\Users\svalp\...`, another dev's machine);
      2 were caused by our own bridge — see the open item below.
- [x] `AutonomousMcp.SelfTest` alone — 96 pass, 4 fail, 2 skip; all 4 failures are the keyless
      Pollinations tier returning HTTP 429 "Queue full for IP", not logic.
- [x] Live restore end-to-end: `set_import_settings` 2048→256 auto-captured the asset (+`.meta`),
      `restore {include_scene:false}` returned it to 2048 without reopening the scene.
- [x] **Unity compiles while unfocused.** Spotify held the foreground before *and* after
      (`GetForegroundWindow` checked both ends); `refresh_unity` recompiled in 23s,
      `buildStamp c169b785 → ba1225bd`. The old "focus required" claim was stale and is now
      corrected in `CLAUDE.md`, `unity-2022-reference.md`, `unity-compile-fix`, the
      `unity-2022-specialist` agent, and `unity-verify.mjs`.

## Verified 2026-07-31 (second pass — loop end to end on LEAF)

- [x] Shared `bridge.mjs` reconnect wired into all four harnesses. Proved itself immediately:
      a `driver.mjs health` hit a live domain reload and rode `network → refused → back` across
      five retries where it previously died with "bridge unreachable — open Unity".
- [x] Full Tier-1 optimization pass on LEAF, end to end: dossier → baseline → checkpoint →
      shrink one normal map (2048→1024) → delta → restore. Texture VRAM 187 → 179 MB, then
      187 again after restore. Avatar left byte-identical to how it was found and the
      checkpoint deleted.
- [x] Found and fixed while doing it: `AVATAR_METRICS` tracked no texture memory, so the
      loop was blind to its own main Tier-1 lever.

## Still needs Unity open

- [ ] Untested branch: `CaptureAssets` auto-creating a checkpoint when **zero** exist.
      Forcing it in a test would need `DeleteAll()`, which would wipe real checkpoints.

## Bugs found while verifying

- [x] **Polling the bridge during a test run failed unrelated tests.** A `get_test_job` poll hit
      the 10s main-thread budget while tests owned the main thread, the bridge logged
      `[Error] HTTP loop error: …`, and NUnit fails any test that emits an unexpected error log —
      so monitoring a run corrupted it. Fixed in `AutonomousMcpTransportHost`: a `TimeoutException`
      now answers **HTTP 503 `{busy:true,retryable:true}`** (previously the connection just dropped)
      and logs at Warning, not Error. Re-run confirms it: 18 → 17 failures, zero console errors
      across a full 260-test run.
- [x] `get_test_job` reported `totalTests` for the whole suite even under `testFilter`, so a
      filtered run read as a stalled `7/260`. Unity hands `RunStarted` the entire tree regardless
      of the filter, so `AutonomousMcpTestRunner` now counts the leaves the filter selects.
      Verified: `CheckpointStoreTests` reports `7/7`.
- [x] `execute_csharp` failed with "The filename or extension is too long". **The earlier
      diagnosis here was wrong** — it was not the snippet being multi-line. `CSharpCodeProvider`
      puts every reference on the compiler command line as `/r:`, and Leaf loads several hundred
      assemblies, which blew the ~32 KB Windows limit; even `return 2+2;` failed. References now
      go in an mcs response file. Verified: a 62-line snippet and an 8-line one doing real editor
      work both run, and bad code returns a genuine C# diagnostic.
- [x] **`execute_csharp` "defined multiple times" is fixed** (2026-07-31). Naming `List<T>`,
      `Dictionary<,>`, `StringBuilder` or `System.IO.Path` — most of `System.*` — used to fail even
      fully qualified. **The diagnosis recorded here was wrong**, and wrong in a way worth keeping:
      it blamed the netstandard/System.Runtime facades, which are innocent. Measured by trying each
      strategy and printing the result: dropping netstandard alone (321 refs) reproduces the error,
      dropping the entire `Facades/` directory (313 refs) reproduces it, and `-nostdlib` with
      netstandard as the only core fails differently ("predefined type `System.Object' is defined
      in an assembly that is not referenced") because a pure-forward assembly cannot *be* the core
      library. The actual cause: **mcs implicitly references its own mscorlib**, so passing the
      loaded copy with `-r:` as well gives it two physically different files defining the same
      types. Fix is one exclusion — never reference the loaded `mscorlib`, and keep every facade,
      because they forward to the implicit core and LINQ's signatures need `netstandard` present
      (excluding facades broke `Select`). Verified: `List` + `Dictionary` + `StringBuilder` +
      `Path.Combine` + LINQ + `AssetDatabase` + `Selection` in one snippet; a bad snippet still
      returns a plain C# diagnostic after a single compile; a runtime fault still surfaces as
      `Runtime error: DivideByZeroException`. Lesson: the plausible cause was asserted three
      revisions running without being measured — printing what each candidate actually did found
      it in one round trip.

## World loop (first run, 2026-07-31)

- [x] Ran the world audit loop for the first time. It worked, and exposed the same class of bug
      as the avatar loop: its only texture signal was `unity_optimization oversized_textures`,
      which judged by the **larger** dimension, so a 2048×2048 albedo costing 11 MB passed a
      "> 2048" check while two Poiyomi TPS baked *mesh-data* strips (8190×2, ~64 KB, and
      corrupted if shrunk) were the only things reported. It also scans the whole project via
      `FindAssets`, so it cannot respond to scene edits at all. Now: degenerate short-edge
      textures are excluded and surfaced separately as `skippedDataTextures`, hits are ranked by
      real memory, and the world loop gets scene-scoped `Texture VRAM (MB)` / `Textures > 1024`
      from the dossier. On the open scene that moved the signal from "2 oversized" (128 KB of
      untouchable data) to the actual 301 MB across 51 textures.

## Advisor HUD (2026-07-31, verified with a human in the loop)

- [x] **Full round trip works.** AI → user: `hud_post` and `hud_post_card` both render in the feed.
      User → AI: a card button, a typed note and an attached selection all arrived via `hud_poll`
      intact and correctly typed, and the outbox drained to zero. The selection payload is
      genuinely useful on its own — it reported LEAF's four VRCFury components unprompted.
      This settles the open question from the overhaul: the HUD is real, so `hud-drain.mjs`
      stays.
- [x] Card titles were **silently clipped** — `boldLabel` does not wrap and IMGUI truncates with no
      ellipsis, so "…normal maps on LEAF?" rendered as "…normal map". Titles now derive from
      `wordWrappedLabel`. Bad on a surface whose job is asking yes/no questions.
- [x] The queue showed `[card_action] roundtrip-test` — which card, but not which **button**, so
      approve and dismiss looked identical while queued. Now `roundtrip-test → dismiss`, and
      selections/console entries summarise instead of dumping raw JSON.
- [x] The Screenshot attach enqueued a **project-relative** path, which the AI resolves against its
      own repo, not the Unity project — so the file was never findable. Now absolute.

## Bugs the HUD test flushed out

- [x] **`unity-verify.mjs` could report a false clean.** It printed "editor is compiling" off the
      response to the `refresh_unity` call that *starts* the compile, so the note fired almost
      every run and re-running just triggered another one. Worse, it then read the console
      immediately — before the compile finished — so a fresh syntax error would not be logged yet
      and the harness would print `clean`. The one tool whose job is catching bad C# had the exact
      false-clean failure mode its own header warns about for `get_compilation_errors`. It now
      polls `health_check` until `isCompiling`/`isUpdating` are clear for three consecutive
      samples. Verified both directions: a deliberate `CS0029` probe is caught (exit 1), and clean
      after removal. Console entries also print as `error CS0029: …` + `file:line` instead of a
      raw JSON blob (the entries use PascalCase keys, which the formatter did not match).
- [x] **`capture_screenshot source:"editor"` produced upside-down images.** The composite path
      flips each dock-area tile *and* the whole image, so panels landed in the right places with
      their contents inverted — layout upright, all text mirrored. `GetPixels` after `ReadPixels`
      is already top-down where the UV origin is at the top (D3D). Now guarded by
      `SystemInfo.graphicsUVStartsAtTop`. The single-window path was already correct and is
      untouched. Verified by capturing and reading the result back.

## Learned from the first readable editor screenshot

- [x] ~~The editor is running the Russian localization~~ — **wrong, and worth not repeating.**
      Unity 2022.3 ships no Russian language pack. Only the *Transform* inspector is Russian,
      because `Assets/MyScripts/Editor/EasyTransforn.cs` registers
      `[CustomEditor(typeof(Transform), true)]` and replaces Unity's built-in one project-wide.
      Menu paths are English, so `execute_menu_item` is unaffected. Lesson: a localized-looking
      inspector means a custom editor, not a localized Unity.
- [x] `EasyTransforn.cs` **Reset All pasted instead of resetting** — it assigned the clipboard
      (`TransformCopier.*`) to all three fields, so with something copied it was a second Paste,
      and with nothing copied it zeroed position/rotation and only then fell back to default scale.
      Now matches the per-row reset buttons directly above it: `Vector3.zero` / `Quaternion.identity`
      / `globalScale`. Pre-existing bug in the user's own script, not from the translation pass.
      (Backup remains at `C:\VRChatProjectsAlcom\Leaf\EasyTransforn.cs.bak`.)
- [ ] Console carries a recurring `Serialization depth limit 10 exceeded at
      'ConditionGroup.conditions'` warning and `Cannot add menu item 'Tools/YUCP/Other…'`.
      Third-party, but they are noise in every `read_console` and worth knowing are expected.

## LEAF optimization (2026-07-31, first real pass — not a test)

- [x] **Pass 1: six 2048² normal maps → 1024.** `Texture VRAM (MB)` 187 → 139 (−48). Textures
      > 1024: 26 → 20. Checkpoint `20260731-152805-e5e207`.
- [x] **Pass 2: two 2048² matcaps (`Skin4`, `Neon`) → 1024.** 139 → 123 (−64 cumulative, −34%).
      Textures > 1024: 20 → 18. Checkpoint `20260731-153352-6cf4b8`. Separate checkpoint per pass,
      so either is revertible on its own.
- [x] Verified after each pass: zero console errors, no other metric moved (polys, bones,
      PhysBones, blendshapes, param cost all identical), and a scene capture confirms the avatar
      renders correctly — including the matcap-driven collar hardware.
- [x] **`Texture VRAM (MB)` reads ~2× real GPU VRAM.** `Profiler.GetRuntimeMemorySizeLong` counts
      the CPU-side copy too in the Editor. Confirmed by format: `normal 1` is DXT5 2048² with mips
      = 5.59 MB on the GPU but reports 11.19 MB; `white` is DXT1 = 2.80 MB but reports 5.59 MB.
      So LEAF went ~93 → ~62 MB of actual GPU texture memory, crossing under VRChat's 75 MB
      "Good" line. The metric is exact for deltas — just halve it before quoting a rank.
- [ ] Remaining headroom if wanted: `T_Shine_CM` cubemap (8.39 MB at 1024), `AlphalMap Eliza`
      hair alpha mask and the 2048 albedos (`09`, `Aurora Alpha; Sleepy 9`, `Grayy 2`, `5`).
      Albedo downscales are visible, so those are a judgement call, not autonomous Tier 1.

## Third-party test noise (not ours — do not chase)

The full EditMode suite is **260 tests: 236 pass, 17 fail, 7 skip**. Every one of the 17 belongs
to the vendored **YUCP / Novaspil** package (`ParseTrustedRootKeys`, `DirectVpmInstaller`,
`GuardianTransaction`, `PackageSigningTab`, `ProtectedImportFastPath`, `TryFinalizeProtectedInstall`,
…). Several cannot pass on this machine at all — they reference `C:\Users\svalp\Downloads\…`, a
different developer's paths. Treat **17 failures as the green baseline**; only investigate a
failure whose name is under `AutonomousMcp.SelfTest`.

## CI

- [x] Confirm `ci.yml` goes green on the private remote — run 30614005298, both jobs passed
      (relay 24s, agent-scripts 6s).
- [ ] Decide whether to commit `package-lock.json` (currently gitignored). Without it CI must
      use `npm install`, so builds are not reproducible.

## Known-stale or unverified

- [x] **Animation generator — verified.** All four presets produce real clips, not just files:
      `spin` 1 curve/2 keys on `localEulerAnglesRaw.y`, `bob` 1/31 on `localPosition.y`,
      `pulse` 3/93 on `localScale.x/y/z`, `blink` 1/5 on `localScale.y`; all 2s and looping.
      Fully offline, no key, no network — the one generator that is unconditionally usable.
- [x] **Audio + Model3D — unconfigured path verified.** Both correctly report themselves
      unconfigured with actionable status text and fall back to the stub rather than failing
      obscurely. Their network paths remain unverified: Audio needs `GENERATOR_HF_TOKEN`,
      Model3D needs `GENERATOR_MESHY_API_KEY`.
- [x] **Model3D main-thread freeze — fixed.** Its Meshy poll loop `Thread.Sleep`s on the *editor
      main thread* for up to 300s (900s ceiling) while the dispatcher only waits 75s. With a valid
      key that guaranteed a 503 at 75s plus a frozen editor for the remaining ~225s, during which
      no tool could run. The wait is now capped at 60s (70s ceiling) so it fails cleanly inside
      the dispatch window, and the error returns the Meshy taskId so a server-side task is not
      lost. Genuinely long generations need a job-based flow, which does not exist yet.
- [ ] **TerrainLayer — network path still unverified.** Registered, configured, `terrain_layer`
      snake_case parsing works and the failure path is clean and actionable, but every attempt
      today hit the keyless Pollinations throttle ("Queue full for IP", HTTP 402/429) including
      after a 75s wait. Needs `GENERATOR_HF_TOKEN` or a quiet window to confirm it actually
      writes the `.terrainlayer` + albedo pair.
- [ ] `com.autonomous-unity.mcp/Editor/AutonomousMcpToolDispatcher.cs` is ~5,350 lines.
      Split opportunistically only; it is high-churn, low-reward risk.

## Unity supervisor (2026-07-31)

- [x] `status` / `ensure` fast-path / `enable-autoconnect` guard all verified against the live
      editor. Project + editor + version resolve correctly from `.claude/unity-project.json` and
      `ProjectVersion.txt`; the Win32_Process query correctly reports the running editor.
- [x] `AutoConnect` on this machine is **already `True`**, so the cold-launch path should work here.
      `enable-autoconnect` is only needed to provision a fresh machine — and only with the editor
      closed. While the bridge is up, set the pref through `execute_csharp` on
      `EditorPrefs`/`AutonomousMcp.AutoConnect` instead (`AutonomousMcpSettings` is `internal`,
      so it is unreachable from a snippet).
- [ ] **Untested: the actual cold launch.** Verifying it means closing Unity, and doing that
      unasked while the user is at the machine is not acceptable. Launching a *second* project to
      test it would be worse — with AutoConnect on, the new editor would race the running one for
      port 8080 and MCP traffic could land on the wrong editor. Next time Unity is closed:
      `node .claude/tools/unity-supervisor.mjs ensure`.
- [ ] Untested: the Safe Mode / licence log diagnostics. They need a project that genuinely fails
      to start, so they are pattern-matched from the documented log text rather than observed.

## Full EditMode suite, post-`execute_csharp` change (2026-07-31)

- [x] 260 tests: 236 passed, **17 failed, 7 skipped — zero failures in this package.** Every failure
      belongs to YUCP's signing/licensing stack (`DirectVpmInstaller`, `GuardianTransaction`,
      `PackageSigningTab`, `ProtectedImport*`, `TrustedRootKeys`). Two are unambiguously not ours:
      one needs `C:\Users\svalp\Downloads\…`, a hardcoded path from another developer's machine, and
      one needs `Leaf/build-src/YUCP.PatchRuntime/`. Ours (`AdvisorStore`, `CheckpointStore`,
      `StateDossier`, `McpMutateTests_*`) all pass; the 7 skips are our own network/manual guards.
      Treat 17 YUCP failures as the expected baseline in this project, not a regression signal.

## LEAF model import review (2026-07-31)

- [x] **Read/Write disabled on `LEAF.fbx`.** All 18 meshes shipped with it on. 17 of 18 now report
      `isReadable=false` (the holdout is `FT_Debug`, whose FBX lives in `Packages/` and was left
      alone deliberately). Console clean, scene capture confirms the avatar renders intact.
      Checkpoint `20260731-164111-0ebde9`.
- [x] **The saving is real but NOT measurable in the Editor — do not quote a number for it.**
      `Profiler.GetRuntimeMemorySizeLong(mesh)` reported exactly 50.45 MB before and after, because
      the Editor keeps mesh data resident regardless of the flag; the CPU-side copy is only stripped
      in a *player* build, i.e. the uploaded avatar. Second Profiler caveat found today and it cuts
      the opposite way to the first: **texture** VRAM over-reports ~2×, **mesh** memory does not move
      at all. Neither is trustworthy as an absolute, and unlike textures, meshes are not even
      trustworthy as a delta. The only real confirmation is an upload.
- [x] Not worth touching, checked and dismissed: mesh compression is already `Off` (correct — it is
      lossy and only shrinks disk, not runtime), `optimizeMeshVertices/Polygons` already on, and the
      importer has `importCameras`/`importLights` **true** while the hierarchy contains **zero** of
      either, so flipping them costs a reimport for no gain.
- [x] ~~Biggest remaining win: ~110,800 of 231,895 **verts** are on disabled toggles~~ —
      **measured properly by the new `cost` section, and the framing was wrong twice.**
      (a) It was quoted in *verts*; VRChat ranks *polygons*, and they order differently —
      `ANIMAL DOG` is the largest mesh by verts (43,000) but only **8th** by polygons (20,315),
      so ranking removal candidates by vertex count picks the wrong target outright.
      (b) The real figure is **86,619 of 278,879 polys (31%)** on 5 disabled objects, and
      `ifAllRemoved` says deleting *all* of them leaves **192,260** — still far past the 70,000
      `good` ceiling. So it is worth real download size and memory but **changes no rank**,
      which is the opposite of the "beats every importer flag" claim made above.
      LEAF is `Over` on polygons (278,879), material slots (28) and skinned meshes (18); no
      cleanup short of heavy decimation moves its PC rank.
- [ ] `LEAF.fbx` imports 150 blendshapes on the head with **blend-shape normals = `Calculate`**,
      which is very likely why `Body` alone is 13.47 MB. Setting it to `None` is the largest
      mesh-memory win available, but it changes expression shading — usually imperceptible,
      occasionally not. Semi-visual, so not autonomous Tier 1.
