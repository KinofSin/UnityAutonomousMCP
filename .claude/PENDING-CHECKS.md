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
- [ ] `execute_csharp` passes the snippet to `mono.exe` as an argv, so anything multi-line
      fails with "The filename or extension is too long". Write to a temp file and compile
      that instead.

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

- [ ] Generators added in parallel and never reviewed: Audio, Model3D, Animation,
      TerrainLayer. Either verify them or mark them experimental in their tool descriptions.
- [ ] `com.autonomous-unity.mcp/Editor/AutonomousMcpToolDispatcher.cs` is ~5,350 lines.
      Split opportunistically only; it is high-churn, low-reward risk.
