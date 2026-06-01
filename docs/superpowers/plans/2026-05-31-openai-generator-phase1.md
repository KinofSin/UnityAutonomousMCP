# OpenAI Generator (Thread A) — Phase 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:executing-plans. Steps use checkbox (`- [ ]`).

**Goal:** Add OpenAI (BYOK) texture/sprite/material generators, built on a **key-free, unit-tested** `GeneratedAssetWriter`, plus a `run_tests` `testFilter`/`category` param so specific tests can be run over the bridge.

**Architecture:** Split the network half from the Unity half (what makes most of this testable without a key): `IImageSource.FetchPng` (OpenAI, key-gated) returns PNG bytes; `GeneratedAssetWriter.Write` (main-thread, KEY-FREE) turns bytes into a texture/sprite/material asset. Three `IGenerator`s (provider `"openai"`) compose source + writer; the registry already discovers them (last-write-wins per Kind+ProviderId, so they coexist with the `free-tier` provider). Sync only — the existing `manage_generator generate` path + 75s dispatch budget cover it; async/generate_status is a separate Phase 2.

**Tech Stack:** Unity 2022.3 Editor C# (`AssetDatabase`, `TextureImporter`, `Material`/`Shader`, `GraphicsSettings`), `System.Net.HttpWebRequest` + Newtonsoft for the OpenAI call, BYOK env var `GENERATOR_OPENAI_API_KEY`, the existing `IGenerator`/`GenerationRequest`/`GenerationResult` (`Editor/Core/IGenerator.cs`), `UnityEditor.TestTools.TestRunner.Api.Filter`.

**Spec:** `docs/superpowers/specs/2026-05-29-openai-generator-backend-design.md`

**Policy:** BYOK only — the OpenAI key is the user's own `GENERATOR_OPENAI_API_KEY`. Never harvest/embed keys.

**Dev-loop / regression guard:** junction-embedded; compile via user focus; verify with `read_console{level:"error"}` + `health_check.buildStamp`; `driver.mjs tests editmode` — new tests pass, overall **failed stays 17**. The OpenAI *network* path needs the user's key for a live gen; everything else verifies key-free.

---

## File Structure

**Create (`com.autonomous-unity.mcp/Editor/Generators/`):**
- `GeneratedAssetWriter.cs` — `internal static Write(GeneratorKind, byte[] png, string requestedPath, out string error) -> assetPath`. KEY-FREE, main-thread, unit-tested.
- `IImageSource.cs` — `internal interface IImageSource { byte[] FetchPng(string prompt, GenerationRequest req, out string error); }`.
- `OpenAiImageSource.cs` — implements `IImageSource` (BYOK) + `static bool HasKey()`.
- `OpenAiImageGenerators.cs` — `OpenAiTextureGenerator`/`OpenAiSpriteGenerator`/`OpenAiMaterialGenerator` (provider `"openai"`), source + writer.

**Create (tests):** `Editor/Tests/GeneratedAssetWriterTests.cs`.

**Modify:** `Editor/AutonomousMcpTestRunner.cs`, `Editor/AutonomousMcpToolDispatcher.cs` (`HandleRunTests`), `server/src/mcpServer.ts` (`run_tests` schema). All Core; no new asmdef (Generators is its own assembly already — these files land in it).

---

## Task 0: Test-assembly wiring for the Generators assembly

Since Thread C split `Editor/Generators/` into its own assembly (`AutonomousMcp.Editor.Generators`), the EditMode test assembly must reference it and be allowed to see its internals (`GeneratedAssetWriter`, the OpenAI generators are `internal`).

**Files:** Create `Editor/Generators/AssemblyInfo.cs`; Modify `Editor/Tests/AutonomousMcp.Editor.Tests.asmdef`

- [ ] **Step 1: Expose Generators internals to the test assembly**

```csharp
// com.autonomous-unity.mcp/Editor/Generators/AssemblyInfo.cs
using System.Runtime.CompilerServices;
[assembly: InternalsVisibleTo("AutonomousMcp.Editor.Tests")]
```

- [ ] **Step 2: Reference the Generators assembly from the tests** — add `"AutonomousMcp.Editor.Generators"` to the `references` array in `Editor/Tests/AutonomousMcp.Editor.Tests.asmdef`.

- [ ] **Step 3: Commit** — `git commit -m "build(tests): reference Generators assembly + IVT for generator tests"`

---

## Task 1: `GeneratedAssetWriter` (key-free, TDD)

**Files:** Create `Editor/Generators/GeneratedAssetWriter.cs`; Test `Editor/Tests/GeneratedAssetWriterTests.cs`

- [ ] **Step 1: Failing tests**

```csharp
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using AutonomousMcp.Editor.Core;
using AutonomousMcp.Editor.Generators;

namespace AutonomousMcp.SelfTest
{
    public sealed class GeneratedAssetWriterTests
    {
        private const string Dir = "Assets/_MCPSelfTest";

        private static byte[] TinyPng()
        {
            var t = new Texture2D(4, 4);
            for (int x = 0; x < 4; x++) for (int y = 0; y < 4; y++) t.SetPixel(x, y, Color.red);
            t.Apply();
            var png = t.EncodeToPNG();
            Object.DestroyImmediate(t);
            return png;
        }

        [TearDown]
        public void Cleanup()
        {
            if (AssetDatabase.IsValidFolder(Dir)) AssetDatabase.DeleteAsset(Dir);
        }

        [Test]
        public void Write_texture_creates_a_texture_asset()
        {
            var path = GeneratedAssetWriter.Write(GeneratorKind.Texture, TinyPng(), Dir + "/tex", out var err);
            Assert.IsNull(err, err);
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<Texture2D>(path));
            StringAssert.EndsWith(".png", path);
        }

        [Test]
        public void Write_sprite_sets_importer_to_sprite()
        {
            var path = GeneratedAssetWriter.Write(GeneratorKind.Sprite, TinyPng(), Dir + "/spr", out var err);
            Assert.IsNull(err, err);
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.IsNotNull(ti);
            Assert.AreEqual(TextureImporterType.Sprite, ti.textureType);
        }

        [Test]
        public void Write_material_creates_a_material_with_main_texture()
        {
            var path = GeneratedAssetWriter.Write(GeneratorKind.Material, TinyPng(), Dir + "/mat", out var err);
            Assert.IsNull(err, err);
            StringAssert.EndsWith(".mat", path);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            Assert.IsNotNull(mat);
            Assert.IsNotNull(mat.mainTexture, "material should reference the generated texture");
        }

        [Test]
        public void Write_rejects_non_image_bytes()
        {
            var path = GeneratedAssetWriter.Write(GeneratorKind.Texture, new byte[] { 1, 2, 3 }, Dir + "/bad", out var err);
            Assert.IsNull(path);
            Assert.IsNotNull(err);
        }
    }
}
```

- [ ] **Step 2: Run — expect FAIL** (`GeneratedAssetWriter` undefined). `driver.mjs tests editmode`.

- [ ] **Step 3: Implement `GeneratedAssetWriter`**

```csharp
using System;
using System.IO;
using AutonomousMcp.Editor.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace AutonomousMcp.Editor.Generators
{
    // Turns generated image bytes into a Unity asset (texture/sprite/material). KEY-FREE and
    // main-thread — the testable half of every image generator. Returns the asset path, or
    // null + an error message.
    internal static class GeneratedAssetWriter
    {
        public static string Write(GeneratorKind kind, byte[] png, string requestedPath, out string error)
        {
            error = null;
            if (png == null || png.Length == 0) { error = "no image bytes"; return null; }

            var probe = new Texture2D(2, 2);
            var valid = probe.LoadImage(png);
            UnityEngine.Object.DestroyImmediate(probe);
            if (!valid) { error = "bytes are not a valid image"; return null; }

            var texPath = NormalizePath(requestedPath, kind, ".png");
            EnsureDir(texPath);
            File.WriteAllBytes(ToAbsolute(texPath), png);
            AssetDatabase.ImportAsset(texPath, ImportAssetOptions.ForceSynchronousImport);

            if (kind == GeneratorKind.Sprite)
            {
                if (AssetImporter.GetAtPath(texPath) is TextureImporter ti && ti.textureType != TextureImporterType.Sprite)
                {
                    ti.textureType = TextureImporterType.Sprite;
                    ti.SaveAndReimport();
                }
                return texPath;
            }

            if (kind == GeneratorKind.Material)
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
                var usingSrp = GraphicsSettings.currentRenderPipeline != null;
                var shaderName = usingSrp ? "Universal Render Pipeline/Lit" : "Standard";
                var mapProp = usingSrp ? "_BaseMap" : "_MainTex";
                var shader = Shader.Find(shaderName) ?? Shader.Find("Standard");
                var mat = new Material(shader);
                if (tex != null && mat.HasProperty(mapProp)) mat.SetTexture(mapProp, tex);
                var matPath = Path.ChangeExtension(texPath, ".mat");
                AssetDatabase.CreateAsset(mat, matPath);
                AssetDatabase.SaveAssets();
                return matPath;
            }

            return texPath; // Texture
        }

        private static string NormalizePath(string requested, GeneratorKind kind, string ext)
        {
            var rel = string.IsNullOrWhiteSpace(requested)
                ? $"{GeneratorConfig.Data.defaultOutputDirectory.TrimEnd('/')}/{kind}_{DateTime.UtcNow:yyyyMMdd_HHmmss}"
                : requested.Replace('\\', '/');
            if (!rel.StartsWith("Assets/", StringComparison.Ordinal)) rel = "Assets/" + rel.TrimStart('/');
            foreach (var e in new[] { ".png", ".jpg", ".jpeg", ".mat" })
                if (rel.EndsWith(e, StringComparison.OrdinalIgnoreCase)) { rel = rel.Substring(0, rel.Length - e.Length); break; }
            return rel + ext;
        }

        private static string ToAbsolute(string assetRel)
        {
            var root = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            return Path.GetFullPath(Path.Combine(root, assetRel));
        }

        private static void EnsureDir(string assetRel)
        {
            var dir = Path.GetDirectoryName(ToAbsolute(assetRel));
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            AssetDatabase.Refresh();
        }
    }
}
```

- [ ] **Step 4: Run — expect PASS** (4 writer tests; overall failed == 17).

- [ ] **Step 5: Commit** — `git commit -m "feat(generators): GeneratedAssetWriter (key-free texture/sprite/material) + tests"`

---

## Task 2: `IImageSource` + `OpenAiImageSource` (BYOK)

**Files:** Create `Editor/Generators/IImageSource.cs`, `Editor/Generators/OpenAiImageSource.cs`

- [ ] **Step 1: Create the interface**

```csharp
namespace AutonomousMcp.Editor.Generators
{
    // The network half of an image generator. Implementations are key-gated; the Unity write half
    // is GeneratedAssetWriter (key-free), so most of the pipeline is testable without a key.
    internal interface IImageSource
    {
        byte[] FetchPng(string prompt, AutonomousMcp.Editor.Core.GenerationRequest req, out string error);
    }
}
```

- [ ] **Step 2: Create `OpenAiImageSource`**

```csharp
using System;
using System.IO;
using System.Net;
using System.Text;
using AutonomousMcp.Editor.Core;
using Newtonsoft.Json.Linq;

namespace AutonomousMcp.Editor.Generators
{
    // BYOK OpenAI image generation. Reads GENERATOR_OPENAI_API_KEY (fallback GENERATOR_API_KEY).
    // POSTs /v1/images/generations and decodes data[0].b64_json -> PNG bytes. Never embeds a key.
    internal sealed class OpenAiImageSource : IImageSource
    {
        public static string Key() =>
            Environment.GetEnvironmentVariable("GENERATOR_OPENAI_API_KEY")
            ?? Environment.GetEnvironmentVariable("GENERATOR_API_KEY");

        public static bool HasKey() => !string.IsNullOrWhiteSpace(Key());

        public byte[] FetchPng(string prompt, GenerationRequest req, out string error)
        {
            error = null;
            var key = Key();
            if (string.IsNullOrWhiteSpace(key)) { error = "Set GENERATOR_OPENAI_API_KEY (BYOK)."; return null; }
            if (string.IsNullOrWhiteSpace(prompt)) { error = "Empty prompt."; return null; }

            var model = Opt(req, "model", "gpt-image-1");
            var size = Opt(req, "size", "1024x1024");
            var endpoint = Opt(req, "endpoint", "https://api.openai.com/v1/images/generations");

            var body = new JObject { ["model"] = model, ["prompt"] = prompt, ["size"] = size, ["n"] = 1 };
            // dall-e-* needs response_format; gpt-image-1 returns b64_json by default and REJECTS it.
            if (model.IndexOf("dall-e", StringComparison.OrdinalIgnoreCase) >= 0)
                body["response_format"] = "b64_json";

            try
            {
                var req2 = (HttpWebRequest)WebRequest.Create(endpoint);
                req2.Method = "POST";
                req2.ContentType = "application/json";
                req2.Headers["Authorization"] = "Bearer " + key;
                req2.Timeout = 60_000;
                req2.ReadWriteTimeout = 60_000;
                var bytes = Encoding.UTF8.GetBytes(body.ToString());
                req2.ContentLength = bytes.Length;
                using (var s = req2.GetRequestStream()) s.Write(bytes, 0, bytes.Length);

                using (var resp = (HttpWebResponse)req2.GetResponse())
                using (var rs = resp.GetResponseStream())
                using (var sr = new StreamReader(rs))
                {
                    var json = JObject.Parse(sr.ReadToEnd());
                    var b64 = (string)json["data"]?[0]?["b64_json"];
                    if (string.IsNullOrEmpty(b64)) { error = "OpenAI response had no image data."; return null; }
                    return Convert.FromBase64String(b64);
                }
            }
            catch (WebException we)
            {
                var detail = we.Message;
                try { using (var er = we.Response?.GetResponseStream()) if (er != null) detail = new StreamReader(er).ReadToEnd(); }
                catch { }
                error = "OpenAI request failed: " + Truncate(detail, 300);
                return null;
            }
            catch (Exception ex) { error = "OpenAI error: " + ex.Message; return null; }
        }

        private static string Opt(GenerationRequest req, string key, string fallback)
        {
            if (req?.ProviderOptions != null && req.ProviderOptions.TryGetValue(key, out var v) && v != null)
            {
                var s = v.ToString();
                if (!string.IsNullOrWhiteSpace(s)) return s;
            }
            return fallback;
        }

        private static string Truncate(string s, int max) =>
            string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s.Substring(0, max) + "…");
    }
}
```

- [ ] **Step 3: Compile + verify** — `read_console{level:error}` 0 errors.

- [ ] **Step 4: Commit** — `git commit -m "feat(generators): IImageSource + OpenAiImageSource (BYOK)"`

---

## Task 3: OpenAI generators (texture/sprite/material) + IsConfigured test

**Files:** Create `Editor/Generators/OpenAiImageGenerators.cs`; Modify `Editor/Tests/GeneratedAssetWriterTests.cs` (add IsConfigured test)

- [ ] **Step 1: Create the generators**

```csharp
using System.Collections.Generic;
using AutonomousMcp.Editor.Core;

namespace AutonomousMcp.Editor.Generators
{
    // OpenAI (BYOK) image generators: provider "openai". Compose the key-gated source with the
    // key-free writer. The registry (last-write-wins per Kind+ProviderId) keeps these alongside the
    // free-tier provider; pick one with manage_generator { provider:"openai" } or set it as default.
    internal abstract class OpenAiImageGeneratorBase : IGenerator
    {
        private static readonly IImageSource Source = new OpenAiImageSource();

        public string ProviderId => "openai";
        public abstract GeneratorKind Kind { get; }
        public string DisplayName => $"OpenAI ({Kind})";
        public bool IsConfigured() => OpenAiImageSource.HasKey();
        public string GetStatus() => OpenAiImageSource.HasKey()
            ? "OpenAI key set (GENERATOR_OPENAI_API_KEY)."
            : "Set GENERATOR_OPENAI_API_KEY for OpenAI generation.";

        public GenerationResult Generate(GenerationRequest request)
        {
            if (request == null) return GenerationResult.Fail("Null request.", ProviderId);
            var png = Source.FetchPng(request.Prompt, request, out var err);
            if (png == null) return GenerationResult.Fail(err ?? "OpenAI returned no image.", ProviderId);
            var path = GeneratedAssetWriter.Write(Kind, png, request.OutputAssetPath, out var werr);
            if (path == null) return GenerationResult.Fail(werr ?? "Generated image but failed to write the asset.", ProviderId);
            return GenerationResult.Ok(path, ProviderId, new Dictionary<string, object>
            {
                ["provider"] = "openai",
                ["bytes"] = png.Length,
                ["importedAs"] = Kind.ToString()
            });
        }
    }

    internal sealed class OpenAiTextureGenerator : OpenAiImageGeneratorBase { public override GeneratorKind Kind => GeneratorKind.Texture; }
    internal sealed class OpenAiSpriteGenerator  : OpenAiImageGeneratorBase { public override GeneratorKind Kind => GeneratorKind.Sprite; }
    internal sealed class OpenAiMaterialGenerator : OpenAiImageGeneratorBase { public override GeneratorKind Kind => GeneratorKind.Material; }
}
```

- [ ] **Step 2: Add a key-free IsConfigured test** (append to GeneratedAssetWriterTests)

```csharp
        [Test]
        public void OpenAi_generator_is_not_configured_without_a_key()
        {
            // CI/test env has no GENERATOR_OPENAI_API_KEY set, so IsConfigured must be false (no throw).
            if (!string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("GENERATOR_OPENAI_API_KEY"))) Assert.Ignore("key is set");
            Assert.IsFalse(new OpenAiTextureGenerator().IsConfigured());
        }
```

- [ ] **Step 3: Compile + run** — writer + IsConfigured tests green; overall failed == 17. Then `manage_generator {action:"list"}` shows an `openai` provider for texture/sprite/material.

- [ ] **Step 4: Commit** — `git commit -m "feat(generators): OpenAI texture/sprite/material generators (provider openai)"`

---

## Task 4: `run_tests` `testFilter` + `category`

**Files:** Modify `Editor/AutonomousMcpTestRunner.cs`, `Editor/AutonomousMcpToolDispatcher.cs`, `server/src/mcpServer.ts`

- [ ] **Step 1: Extend the runner** — change `AutonomousMcpTestRunner.Run` to accept the filters and set them on the `Filter`:

```csharp
        public static string Run(string mode, string testFilter = null, string category = null)
        {
            var normalized = string.Equals(mode, "playmode", System.StringComparison.OrdinalIgnoreCase)
                ? "playmode" : "editmode";
            var job = AutonomousMcpTestJobs.Create(normalized);
            var callback = new AutonomousMcpTestCallbacks(job);
            Api.RegisterCallbacks(callback);

            var filter = new Filter { testMode = normalized == "playmode" ? TestMode.PlayMode : TestMode.EditMode };
            if (!string.IsNullOrWhiteSpace(testFilter)) filter.groupNames = new[] { testFilter };
            if (!string.IsNullOrWhiteSpace(category)) filter.categoryNames = new[] { category };

            try { Api.Execute(new ExecutionSettings(filter)); }
            catch (System.Exception ex) { job.MarkFailed($"Unity Test Runner failed to start: {ex.Message}"); Api.UnregisterCallbacks(callback); throw; }
            return job.JobId;
        }
```

(`Filter.groupNames` is a regex matched against the full test name; `categoryNames` matches `[Category]`.)

- [ ] **Step 2: Read the params in `HandleRunTests`**

```csharp
        internal static AutonomousMcpToolResponse HandleRunTests(JObject args)
        {
            var mode = args.Value<string>("mode") ?? "editmode";
            var testFilter = args.Value<string>("testFilter");
            var category = args.Value<string>("category");
            try
            {
                var jobId = AutonomousMcpTestRunner.Run(mode, testFilter, category);
                return Success(JToken.FromObject(new { mode, testFilter, category, status = "queued", jobId,
                    next = "Call get_test_job with jobId until status is completed/failed." }));
            }
            catch (Exception ex) { return Error($"Failed to start Unity Test Runner: {ex.Message}"); }
        }
```

- [ ] **Step 3: Node schema** — in `server/src/mcpServer.ts` `run_tests`, add:

```typescript
      testFilter: z.string().optional().describe("Regex matched against full test names (e.g. 'GeneratedAssetWriter')"),
      category: z.string().optional().describe("Only run tests with this [Category]"),
```

Build: `npm --workspace server run build` (exit 0).

- [ ] **Step 4: Compile + verify (live)** — `manage_project_template` unaffected; then:
`node .claude/skills/run-autonomous-unity-mcp/driver.mjs call run_tests '{"mode":"editmode","testFilter":"GeneratedAssetWriter"}'` → poll `get_test_job`; the job's `totalTests` is just the GeneratedAssetWriter tests (≈4–5), NOT ~235. Confirms filtering works.

- [ ] **Step 5: Commit** — `git commit -m "feat(tests): run_tests testFilter + category (run a subset over the bridge)"`

---

## Task 5: Live BYOK gen (your key) + docs

- [ ] **Step 1: Live OpenAI gen (requires your key)** — once `GENERATOR_OPENAI_API_KEY` is set (`setx`, then restart Unity so the editor sees it):
`node .claude/skills/run-autonomous-unity-mcp/driver.mjs call manage_generator '{"action":"generate","kind":"texture","prompt":"a seamless mossy stone tile","provider":"openai"}'`
Expected: `{ success:true, ... assetPath: "Assets/Generated/..png" }`; the texture imports. Also `kind:"material"` → a `.mat`. (If the key isn't set, expect the actionable "Set GENERATOR_OPENAI_API_KEY" error — that's correct.)

- [ ] **Step 2: Full regression sweep** — `driver.mjs tests editmode`: writer + IsConfigured tests green; overall failed == 17.

- [ ] **Step 3: Docs + commit** — add `openai` provider + `run_tests testFilter` to `CLAUDE.md`; `git commit -m "docs: OpenAI generator provider + run_tests testFilter"`.

---

## Done-when (Phase 1 acceptance)

- `GeneratedAssetWriter` writes texture/sprite/material from PNG bytes (4 key-free tests green).
- `manage_generator {action:list}` shows `openai` for texture/sprite/material; `IsConfigured()` is false without the key (tested).
- With the key set, `manage_generator generate {provider:"openai"}` lands a real OpenAI texture + material.
- `run_tests {testFilter:"GeneratedAssetWriter"}` runs only those tests.
- Overall failed-count unchanged (17); nothing regressed.
- Deferred to Phase 2: async generation + `generate_status` + `GeneratorJobStore` (sync works under the 75s budget); cubemap/other kinds; local Stable Diffusion provider.
