# OpenAI generator backend (BYOK) + run_tests testFilter

## Context

The generators scaffold (`IGenerator`, `GeneratorRegistry`, `GeneratorConfig`,
`ManageGeneratorTool`) ships only stubs that return "not implemented". This adds
the **first real generator** — OpenAI image generation — producing **texture /
sprite / material** assets from a text prompt, BYOK (the user's own
`GENERATOR_OPENAI_API_KEY`). No harvested/third-party keys, ever.

A second, smaller change rides along because it's required to run the live test
on demand over MCP: **`run_tests` gains a `testFilter`/`category` param** (today
it only accepts `mode`, so `[Explicit]`/category tests can't be triggered over
the bridge at all). This is broadly useful — it lets any single test run over MCP.

## Goal / success criteria

1. `manage_generator { action:"generate", kind:"texture"|"sprite"|"material", prompt }`
   produces the asset at a path under the configured output dir, via OpenAI, using
   the user's env key.
2. **Sync** (default) and **async** (`async:true` → `jobId`, poll
   `generate_status`) execution both work; async never freezes the editor.
3. The **key-free** asset-writing path is covered by auto-running self-tests
   (no network/key needed).
4. Missing key / HTTP error / bad image → clear error, no half-written asset, no throw.
5. `run_tests { mode, testFilter?, category? }` runs the matching subset, incl.
   `[Explicit]`/`[Category]` tests, over the bridge.
6. Purely additive: no existing tool/behavior changes except the additive
   `run_tests` params and the registry stubs being superseded for openai kinds.

## Architecture

**Split the network part from the Unity part** — this is what makes most of the
feature testable without a key.

```
IImageSource.FetchPng(prompt, opts) -> byte[]     (network; OpenAiImageSource; key-gated)
GeneratedAssetWriter.Write(kind, bytes, path)     (Unity main-thread; KEY-FREE; testable)
3 × IGenerator (texture/sprite/material, provider "openai") = source + writer
```

### Files

**Created (`com.autonomous-unity.mcp/Editor/Generators/`):**
- `IImageSource.cs` — `interface IImageSource { byte[] FetchPng(string prompt, GenerationRequest req, out string error); }`.
- `OpenAiImageSource.cs` — implements `IImageSource`. Reads `GENERATOR_OPENAI_API_KEY`
  (fallback `GENERATOR_API_KEY`). POST `https://api.openai.com/v1/images/generations`
  via `System.Net.Http.HttpClient`, body `{ model, prompt, size, n:1, response_format:"b64_json" }`
  (model default `gpt-image-1`, size default `1024x1024`, both overridable via
  `ProviderOptions["model"]`/`["size"]`/`["endpoint"]`). Decode `data[0].b64_json` → `byte[]`.
  60s timeout. Non-200 → `error` = status + body snippet.
- `GeneratedAssetWriter.cs` — static, main-thread, **no network**:
  - `texture`: write PNG to path (ensure `.png`), `AssetDatabase.ImportAsset`, return path.
  - `sprite`: as texture, then set importer `textureType = Sprite`, reimport.
  - `material`: import texture, create `Material` (shader: if
    `GraphicsSettings.currentRenderPipeline != null` use `Universal Render Pipeline/Lit`
    + `_BaseMap`; else `Standard` + `_MainTex`), assign texture, `CreateAsset` `.mat`,
    return the `.mat` path. Validates bytes load as an image first (else error).
- `OpenAiTextureGenerator.cs`, `OpenAiSpriteGenerator.cs`, `OpenAiMaterialGenerator.cs`
  — each `ProviderId="openai"`, its `Kind`, `IsConfigured()` = key present,
  `GetStatus()` describes key state, `Generate()` = `source.FetchPng` → `writer.Write`.
  Discovered by `GeneratorRegistry` (last-write-wins → supersede the stub for those kinds).
- `GeneratorJobStore.cs` — async job store mirroring the reload-durable
  `AutonomousMcpTestJobs`: in-memory dict + `SessionState` JSON persistence +
  rehydrate on miss. Job = `{ jobId, status: queued|running|completed|failed,
  kind, prompt, assetPath, error, provider }`.

**Modified:**
- `Editor/Tools/ManageGeneratorTool.cs` — `generate` gains `async?` (default false)
  and routes: sync = `source→writer` inline (returns `{success, assetPath, kind, provider}`,
  blocks ~5–20s); async = create job, start `Task` (FetchPng off-thread), marshal
  `GeneratedAssetWriter.Write` to main thread via `AutonomousMcpMainThread`, update job,
  return `{ jobId }`. New action `generate_status { jobId }` → job snapshot. Existing
  `list`/`get_config`/`set_provider`/`set_output_dir` unchanged.
- `Editor/AutonomousMcpToolDispatcher.cs` `HandleRunTests` — read `testFilter`
  (string; test full-name substring) and `category` (string); pass to runner.
- `Editor/AutonomousMcpTestRunner.cs` — `Run(string mode, string testFilter, string category)`;
  build a `UnityEditor.TestTools.TestRunner.Api.Filter` setting `testMode`,
  `groupNames` (regex from `testFilter`) and/or `categoryNames` when provided.
- `server/src/mcpServer.ts` — `manage_generator` schema: add `async`, `generate_status`;
  `run_tests` schema: add optional `testFilter`, `category`.

### Execution detail

- **Sync:** main-thread; the HTTP call blocks the editor briefly. Used by default.
- **Async:** `Task.Run(FetchPng)` off the main thread; on success,
  `AutonomousMcpMainThread.Enqueue(() => GeneratedAssetWriter.Write(...))` then mark
  job completed with the asset path; on failure mark failed. Client polls
  `generate_status`. Survives reloads via `SessionState`.

## Testing

- **Key-free (added to the self-test suite, auto-run):** `McpMutateTests_Generators.cs`
  — write a local 4×4 PNG bytes through `GeneratedAssetWriter`:
  - texture → assert a `Texture2D` exists at the path.
  - sprite → assert importer `textureType == Sprite`.
  - material → assert a `Material` exists with its main texture assigned.
  - `OpenAiTextureGenerator.IsConfigured()` == false when the env key is unset.
  Scoped to `Assets/_MCPSelfTest/`, cleaned up by the harness. **No network/key.**
- **`run_tests testFilter` self-coverage:** one test confirming a filtered run
  returns only matching tests (run via the new param).
- **Key-gated live (you, once key is set):** primary = direct
  `manage_generator { action:"generate", kind:"texture", prompt:"a seamless mossy stone tile" }`
  over the bridge → confirm a real texture lands; `async:true` → poll `generate_status`
  to `completed`. Optional belt-and-suspenders: a `[Category("Network"), Explicit]`
  live test runnable on demand via `run_tests { testFilter:"...", category:"Network" }`.

## Error handling

Missing key → `IsConfigured()=false`; `generate` returns "Set GENERATOR_OPENAI_API_KEY…".
HTTP non-200 → status + body snippet. Timeout (60s) → failed. Invalid `kind`/empty
`prompt` → validation error. Bytes not a valid image → writer error, no asset written.

## Operating caveats (per user)

- **Purely additive** — real generators supersede stubs via registry last-write-wins;
  no existing tool/behavior is modified beyond the additive `run_tests` params.
- **Domain reload on focus** — adding the new test file triggers a recompile/reload
  when Unity regains focus; let any in-flight `run_tests` finish before re-focusing
  so it isn't interrupted mid-reload.

## Verification

1. Unity compiles clean (0 errors) on 2022.3.22f1.
2. Self-test suite green incl. the new `McpMutateTests_Generators` (key-free) and
   the `testFilter` test; project left pristine.
3. `manage_generator { action:"list" }` shows `openai` providers for texture/sprite/material
   (not just `stub`).
4. With key set: sync `generate` returns an `assetPath` that exists and imports as
   the right asset; `async:true` + `generate_status` reaches `completed`.
5. `run_tests { mode:"editmode", testFilter:"GeneratedAssetWriter" }` runs only those tests.

## Out of scope

Other kinds (cubemap/audio/animation/model/terrain) and other providers (scaffold
stays open). Prompt cost estimation / spend confirmation. Image editing/variations.
