using System;
using System.IO;
using System.Linq;
using AutonomousMcp.Editor.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AutonomousMcp.SelfTest
{
    /// <summary>
    /// Deterministic, offline tests for the owned-key rotation + rate-limit backoff logic.
    /// No network, no Unity scene — pure <see cref="ProviderKeyPool"/> behavior.
    /// </summary>
    public sealed class ProviderKeyPoolTests
    {
        [Test]
        public void ParseKeys_splits_on_mixed_separators_and_dedupes()
        {
            var keys = ProviderKeyPool.ParseKeys("k1, k2\nk3\tk4;k5 k1");
            CollectionAssert.AreEquivalent(new[] { "k1", "k2", "k3", "k4", "k5" }, keys.ToArray());
        }

        [Test]
        public void ParseKeys_empty_returns_empty()
        {
            Assert.AreEqual(0, ProviderKeyPool.ParseKeys(null).Count);
            Assert.AreEqual(0, ProviderKeyPool.ParseKeys("   ").Count);
        }

        [Test]
        public void TryLease_round_robins_across_keys()
        {
            var pool = new ProviderKeyPool(new[] { "a", "b", "c" });
            var now = DateTime.UtcNow;

            Assert.IsTrue(pool.TryLease(now, out var k1));
            Assert.IsTrue(pool.TryLease(now, out var k2));
            Assert.IsTrue(pool.TryLease(now, out var k3));
            Assert.IsTrue(pool.TryLease(now, out var k4));

            Assert.AreEqual("a", k1);
            Assert.AreEqual("b", k2);
            Assert.AreEqual("c", k3);
            Assert.AreEqual("a", k4, "Should wrap around to the first key.");
        }

        [Test]
        public void RateLimited_key_is_skipped_until_cooldown_expires()
        {
            var pool = new ProviderKeyPool(new[] { "a", "b" });
            var now = DateTime.UtcNow;

            Assert.IsTrue(pool.TryLease(now, out var first));
            Assert.AreEqual("a", first);

            // Park 'a' for 60s; next lease must skip it and hand back 'b'.
            pool.ReportRateLimited(first, TimeSpan.FromSeconds(60), now);
            Assert.IsTrue(pool.TryLease(now, out var second));
            Assert.AreEqual("b", second);

            // Park 'b' too → nothing usable right now → caller would fail over.
            pool.ReportRateLimited(second, TimeSpan.FromSeconds(60), now);
            Assert.IsFalse(pool.HasUsableKey(now));
            Assert.IsFalse(pool.TryLease(now, out _));

            // Past the cooldown window, keys re-arm.
            Assert.IsTrue(pool.HasUsableKey(now + TimeSpan.FromSeconds(90)));
        }

        [Test]
        public void Honors_explicit_retry_after_window()
        {
            var pool = new ProviderKeyPool(new[] { "only" });
            var now = DateTime.UtcNow;

            Assert.IsTrue(pool.TryLease(now, out var k));
            pool.ReportRateLimited(k, TimeSpan.FromSeconds(60), now);

            Assert.IsFalse(pool.HasUsableKey(now + TimeSpan.FromSeconds(30)), "Still inside Retry-After window.");
            Assert.IsTrue(pool.HasUsableKey(now + TimeSpan.FromSeconds(61)), "Past Retry-After window.");
        }

        [Test]
        public void ReportSuccess_clears_cooldown()
        {
            var pool = new ProviderKeyPool(new[] { "a" });
            var now = DateTime.UtcNow;

            Assert.IsTrue(pool.TryLease(now, out var k));
            pool.ReportRateLimited(k, TimeSpan.FromMinutes(5), now);
            Assert.IsFalse(pool.HasUsableKey(now));

            pool.ReportSuccess(k);
            Assert.IsTrue(pool.HasUsableKey(now), "A success should re-arm the key immediately.");
        }

        [Test]
        public void Auth_failure_parks_key_longer_than_a_rate_limit()
        {
            var pool = new ProviderKeyPool(new[] { "a" });
            var now = DateTime.UtcNow;

            Assert.IsTrue(pool.TryLease(now, out var k));
            pool.ReportAuthFailure(k, now);

            // Auth failures rest for ~30 minutes — well beyond a transient 429 window.
            Assert.IsFalse(pool.HasUsableKey(now + TimeSpan.FromMinutes(10)));
            Assert.IsTrue(pool.HasUsableKey(now + TimeSpan.FromMinutes(31)));
        }

        [Test]
        public void Empty_pool_never_leases()
        {
            var pool = new ProviderKeyPool(Array.Empty<string>());
            Assert.AreEqual(0, pool.Count);
            Assert.IsFalse(pool.HasUsableKey(DateTime.UtcNow));
            Assert.IsFalse(pool.TryLease(DateTime.UtcNow, out _));
            Assert.IsNull(pool.NextAvailableUtc());
        }
    }

    /// <summary>
    /// Tool-surface tests for the free-tier image generator. The registration/availability checks
    /// run in the normal suite; the actual generation hits the network and is therefore
    /// <see cref="ExplicitAttribute"/> (run it on demand, like the other side-effectful checks).
    /// </summary>
    public sealed class McpGeneratorTests : McpTestHarness
    {
        [Test]
        public void List_includes_free_tier_image_providers()
        {
            var r = Invoke("manage_generator", new { action = "list" });
            AssertOk(r);

            var generators = (JArray)((JObject)r.data)["generators"];
            Assert.IsNotNull(generators);

            bool HasFreeTier(string kind) => generators.Any(g =>
                (string)g["kind"] == kind && (string)g["provider"] == "free-tier");

            Assert.IsTrue(HasFreeTier("Texture"), "free-tier Texture generator should be registered.");
            Assert.IsTrue(HasFreeTier("Sprite"), "free-tier Sprite generator should be registered.");
            Assert.IsTrue(HasFreeTier("Material"), "free-tier Material generator should be registered.");
            Assert.IsTrue(HasFreeTier("Cubemap"), "free-tier Cubemap generator should be registered.");
            Assert.IsTrue(HasFreeTier("TerrainLayer"), "free-tier TerrainLayer generator should be registered.");
            Assert.IsTrue(HasFreeTier("Audio"), "free-tier Audio generator should be registered.");
            Assert.IsTrue(HasFreeTier("Model"), "free-tier Model generator should be registered.");
            Assert.IsTrue(HasFreeTier("Animation"), "free-tier Animation generator should be registered.");
        }

        [Test]
        public void Model_provider_reports_status_without_throwing()
        {
            var status = FreeTierModel3DClient.DescribeAvailability();
            Assert.IsFalse(string.IsNullOrEmpty(status));
            Assert.AreEqual(
                !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GENERATOR_MESHY_API_KEY")),
                FreeTierModel3DClient.AnyProviderAvailable());
        }

        [Test]
        public void Generate_animation_spin_writes_anim_clip_with_curves()
        {
            var outPath = TestFolder + "/gen_anim_spin";
            var r = Invoke("manage_generator", new
            {
                action = "generate",
                kind = "animation",
                prompt = "spin rotate",
                outputAssetPath = outPath,
                options = new { preset = "spin", duration = 2, loop = true }
            });

            AssertOk(r);
            var assetPath = (string)((JObject)r.data)["assetPath"];
            Assert.IsFalse(string.IsNullOrEmpty(assetPath), "Expected an animation asset path on success.");
            StringAssert.EndsWith(".anim", assetPath);
            Assert.IsTrue(File.Exists(assetPath), $"Expected a .anim file at '{assetPath}'.");

            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
            Assert.IsNotNull(clip, "AnimationClip should load from the generated asset.");
            Assert.Greater(clip.length, 0f, "Clip should have a positive duration.");

            var bindings = AnimationUtility.GetCurveBindings(clip);
            Assert.GreaterOrEqual(bindings.Length, 1, "Spin preset should author at least one curve.");
        }

        [Test]
        public void Audio_provider_reports_status_without_throwing()
        {
            // Availability depends on an owned HF token; either way the status line is informative
            // and must never throw. With no token, the stub remains the fallback.
            var status = FreeTierAudioClient.DescribeAvailability();
            Assert.IsFalse(string.IsNullOrEmpty(status));
            Assert.AreEqual(
                !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GENERATOR_HF_TOKEN")) ||
                !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HUGGINGFACE_API_KEY")) ||
                !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HF_TOKEN")),
                FreeTierAudioClient.AnyProviderAvailable());
        }

        [Test]
        public void Keyless_provider_is_available_by_default()
        {
            // With no keys and keyless enabled (default), the engine must still be able to serve.
            Assert.IsTrue(FreeTierImageClient.AnyProviderAvailable(),
                "Keyless fallback should make at least one provider available out of the box.");
            StringAssert.Contains("Providers:", FreeTierImageClient.DescribeAvailability());
        }

        [Test]
        public void Generate_rejects_empty_prompt()
        {
            var r = Invoke("manage_generator", new { action = "generate", kind = "texture", prompt = "" });
            Assert.IsNotNull(r);
            Assert.IsFalse(r.success, "Empty prompt should be rejected before any network call.");
        }

        [Test]
        public void Generate_accepts_snake_case_kind()
        {
            // Use an empty prompt so we stop at prompt validation (offline) — the point is that the
            // snake_case kind parsed at all, i.e. we must NOT see an "Unknown generator kind" error.
            var r = Invoke("manage_generator", new { action = "generate", kind = "terrain_layer", prompt = "" });
            Assert.IsNotNull(r);
            Assert.IsFalse(r.success);
            StringAssert.DoesNotContain("Unknown generator kind", r.error ?? "",
                "snake_case 'terrain_layer' should parse to GeneratorKind.TerrainLayer.");
        }

        [Test]
        [Explicit("Hits the keyless Pollinations free tier over the network.")]
        [Category("Network")]
        public void Generate_texture_via_keyless_path_writes_asset()
        {
            var outPath = TestFolder + "/gen_tex";
            var r = Invoke("manage_generator", new
            {
                action = "generate",
                kind = "texture",
                prompt = "a seamless smooth grey stone tile texture, top-down",
                outputAssetPath = outPath,
                options = new { width = 256, height = 256 }
            });

            AssertOk(r);
            var assetPath = (string)((JObject)r.data)["assetPath"];
            Assert.IsFalse(string.IsNullOrEmpty(assetPath), "Expected an asset path on success.");
            Assert.IsTrue(File.Exists(assetPath), $"Expected a generated image file at '{assetPath}'.");
        }

        [Test]
        [Explicit("Hits the keyless Pollinations free tier over the network.")]
        [Category("Network")]
        public void Generate_material_via_keyless_path_writes_mat_and_texture()
        {
            var outPath = TestFolder + "/gen_mat";
            var r = Invoke("manage_generator", new
            {
                action = "generate",
                kind = "material",
                prompt = "weathered rusted metal panel, seamless, top-down",
                outputAssetPath = outPath,
                options = new { width = 256, height = 256 }
            });

            AssertOk(r);
            var data = (JObject)r.data;
            var matPath = (string)data["assetPath"];
            var texPath = (string)data["metadata"]?["texturePath"];

            Assert.IsFalse(string.IsNullOrEmpty(matPath), "Expected a material asset path on success.");
            StringAssert.EndsWith(".mat", matPath, "Primary asset should be the material.");
            Assert.IsFalse(string.IsNullOrEmpty(texPath), "Expected an albedo texture path in metadata.");
            Assert.IsTrue(File.Exists(matPath), $"Expected a .mat file at '{matPath}'.");
            Assert.IsTrue(File.Exists(texPath), $"Expected an albedo texture file at '{texPath}'.");

            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            Assert.IsNotNull(mat, "Material should load from the generated asset.");
            Assert.IsNotNull(mat.mainTexture, "Material should have its base map wired to the generated texture.");
        }

        [Test]
        [Explicit("Hits the keyless Pollinations free tier over the network.")]
        [Category("Network")]
        public void Generate_cubemap_via_keyless_path_imports_as_cubemap()
        {
            var outPath = TestFolder + "/gen_cube";
            var r = Invoke("manage_generator", new
            {
                action = "generate",
                kind = "cubemap",
                prompt = "360 equirectangular panorama of a clear blue sky with soft clouds",
                outputAssetPath = outPath,
                options = new { width = 1024, height = 512 }
            });

            AssertOk(r);
            var data = (JObject)r.data;
            var assetPath = (string)data["assetPath"];
            Assert.IsFalse(string.IsNullOrEmpty(assetPath), "Expected an asset path on success.");
            Assert.IsTrue(File.Exists(assetPath), $"Expected a generated image file at '{assetPath}'.");

            var cube = AssetDatabase.LoadAssetAtPath<Cubemap>(assetPath);
            Assert.IsNotNull(cube, "Equirectangular image should import as a Cubemap (TextureCube shape).");
        }

        [Test]
        [Explicit("Hits the keyless Pollinations free tier over the network.")]
        [Category("Network")]
        public void Generate_terrain_layer_via_keyless_path_writes_layer_and_texture()
        {
            var outPath = TestFolder + "/gen_layer";
            var r = Invoke("manage_generator", new
            {
                action = "generate",
                kind = "TerrainLayer",
                prompt = "seamless mossy forest ground, top-down, tileable",
                outputAssetPath = outPath,
                options = new { width = 256, height = 256, tileSize = 10 }
            });

            AssertOk(r);
            var data = (JObject)r.data;
            var layerPath = (string)data["assetPath"];
            var texPath = (string)data["metadata"]?["texturePath"];

            Assert.IsFalse(string.IsNullOrEmpty(layerPath), "Expected a terrain layer asset path on success.");
            StringAssert.EndsWith(".terrainlayer", layerPath, "Primary asset should be the terrain layer.");
            Assert.IsTrue(File.Exists(layerPath), $"Expected a .terrainlayer file at '{layerPath}'.");
            Assert.IsFalse(string.IsNullOrEmpty(texPath), "Expected a diffuse texture path in metadata.");
            Assert.IsTrue(File.Exists(texPath), $"Expected a diffuse texture file at '{texPath}'.");

            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(layerPath);
            Assert.IsNotNull(layer, "TerrainLayer should load from the generated asset.");
            Assert.IsNotNull(layer.diffuseTexture, "TerrainLayer should reference the generated diffuse texture.");
        }

        [Test]
        [Explicit("Hits HuggingFace text-to-audio; requires an owned HF token.")]
        [Category("Network")]
        public void Generate_audio_via_huggingface_writes_audio_clip()
        {
            if (!FreeTierAudioClient.AnyProviderAvailable())
                Assert.Ignore("No HF token set (GENERATOR_HF_TOKEN) — audio provider unavailable.");

            var outPath = TestFolder + "/gen_audio";
            var r = Invoke("manage_generator", new
            {
                action = "generate",
                kind = "audio",
                prompt = "short upbeat 8-bit game victory jingle",
                outputAssetPath = outPath
            });

            AssertOk(r);
            var assetPath = (string)((JObject)r.data)["assetPath"];
            Assert.IsFalse(string.IsNullOrEmpty(assetPath), "Expected an audio asset path on success.");
            Assert.IsTrue(File.Exists(assetPath), $"Expected a generated audio file at '{assetPath}'.");

            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath);
            Assert.IsNotNull(clip, "Generated audio should import as an AudioClip.");
        }

        [Test]
        [Explicit("Hits Meshy text-to-3D API; requires GENERATOR_MESHY_API_KEY.")]
        [Category("Network")]
        public void Generate_model_via_meshy_writes_glb()
        {
            if (!FreeTierModel3DClient.AnyProviderAvailable())
                Assert.Ignore("No Meshy API key set (GENERATOR_MESHY_API_KEY) — model provider unavailable.");

            var outPath = TestFolder + "/gen_model";
            var r = Invoke("manage_generator", new
            {
                action = "generate",
                kind = "model",
                prompt = "a simple low-poly wooden crate",
                outputAssetPath = outPath
            });

            AssertOk(r);
            var data = (JObject)r.data;
            var assetPath = (string)data["assetPath"];
            Assert.IsFalse(string.IsNullOrEmpty(assetPath), "Expected a model asset path on success.");
            StringAssert.EndsWith(".glb", assetPath);
            Assert.IsTrue(File.Exists(assetPath), $"Expected a .glb file at '{assetPath}'.");

            var gltfDetected = (bool?)data["metadata"]?["gltfImporterDetected"] == true;
            if (gltfDetected)
            {
                var meshLoaded = (bool?)data["metadata"]?["meshAssetLoaded"] == true;
                if (meshLoaded)
                {
                    var hasMesh = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath) != null
                                  || AssetDatabase.LoadAllAssetsAtPath(assetPath).Any(a => a is Mesh);
                    Assert.IsTrue(hasMesh, "With a glTF importer present, mesh or prefab should load.");
                }
            }
        }
    }
}
