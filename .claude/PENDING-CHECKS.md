# Pending checks

Work that is code-complete but **not yet verified in a running Unity Editor**, plus
open questions worth settling. `.claude/hooks/vrc-session-note.mjs` prints the
unchecked items at session start, so delete or tick a line once it is genuinely done.

Format: one `- [ ]` per item. Ticked (`- [x]`) items are ignored by the hook.

## Needs Unity open

- [ ] Compile the package after the checkpoint change (commit `9293de6`). Written with
      Unity closed, so it has never been through a compiler. Watch for the fully-qualified
      `AutonomousMcp.Editor.Core.CheckpointStore` calls added to `AutonomousMcpToolDispatcher.cs`.
- [ ] Run `CheckpointStoreTests` (7 cases). The material round-trip is the flake risk:
      it replaces `.mat` bytes on disk and reloads, and Unity sometimes serves a cached
      instance despite `ImportAsset(ForceUpdate)`.
- [ ] Run the full EditMode suite for a health baseline. It has never been run end to end;
      the last attempt died when the bridge went down mid-run.
- [ ] Live-test a real restore: `manage_texture set_import_settings` to shrink a texture,
      then `manage_checkpoint restore {include_scene:false}`, and confirm the importer
      max size actually comes back.
- [ ] Untested branch: `CaptureAssets` auto-creating a checkpoint when **zero** exist.
      Forcing it in a test would need `DeleteAll()`, which would wipe real checkpoints.

## Open question — the biggest autonomy unknown

- [ ] Does Unity compile while unfocused? `CLAUDE.md` says focus is required, but on
      2026-07-30 `compiledAtUtc` advanced 02:37 -> 03:10 after a `refresh_unity` with no
      deliberate focus. Controlled test: record `buildStamp` + `compiledAtUtc`, make a
      comment-only edit in a package file, call `refresh_unity`, poll without touching the
      window. If it compiles, delete the stale claim — the code-edit loop is already
      autonomous. If not, an opt-in focus nudge is the unblock.

## CI

- [ ] Confirm `ci.yml` actually goes green on the private remote. The relay build, smoke,
      `npm test` and the `node --check` loop were all run locally and pass, but the workflow
      itself has never executed on a runner.
- [ ] Decide whether to commit `package-lock.json` (currently gitignored). Without it CI must
      use `npm install`, so builds are not reproducible.

## Known-stale or unverified

- [ ] Generators added in parallel and never reviewed: Audio, Model3D, Animation,
      TerrainLayer. Either verify them or mark them experimental in their tool descriptions.
- [ ] `com.autonomous-unity.mcp/Editor/AutonomousMcpToolDispatcher.cs` is ~5,350 lines.
      Split opportunistically only; it is high-churn, low-reward risk.
