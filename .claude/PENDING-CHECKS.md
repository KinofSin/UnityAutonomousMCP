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
- [ ] `execute_csharp` remaining sharp edge: a snippet that **names** a duplicated BCL type
      (`List<T>`, `Dictionary<,>`, `StringBuilder`, and also `System.IO.Path` — it is most of
      `System.*`, not just collections) still fails with "defined multiple times",
      because mscorlib and the netstandard/System.Runtime facades all get referenced and mcs
      counts a forwarded type as a second definition. Dropping the facades is not the fix — it
      was tried, and Unity's own assemblies are built against netstandard, so every snippet
      touching a Unity type then fails with "System.Object is defined in an assembly that is not
      referenced". Arrays, strings, primitives and the Unity/UnityEditor APIs all work, which
      covers most editor scripting. A real fix likely means a curated reference set rather than
      "every loaded assembly".

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
