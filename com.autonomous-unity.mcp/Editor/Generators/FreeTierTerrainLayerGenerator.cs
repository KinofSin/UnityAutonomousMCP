using System;
using System.Collections.Generic;
using System.IO;
using AutonomousMcp.Editor.Core;
using UnityEditor;
using UnityEngine;

namespace AutonomousMcp.Editor.Generators
{
    /// <summary>
    /// Real TerrainLayer generator built on the same <see cref="FreeTierImageClient"/> failover
    /// engine. It generates an albedo (diffuse) texture — rotating your owned free-tier keys and
    /// falling back to the keyless provider on rate limits — imports it, then wraps it in a
    /// <see cref="TerrainLayer"/> asset ready to paint onto a Unity Terrain.
    ///
    /// Output layout for an output path "Assets/Generated/TerrainLayer_x":
    ///   Assets/Generated/TerrainLayer_x.terrainlayer   (primary asset returned)
    ///   Assets/Generated/TerrainLayer_x_albedo.png      (generated diffuse texture)
    ///
    /// Options: width, height, tileSize (world units per tile; defaults to 15).
    /// </summary>
    internal sealed class FreeTierTerrainLayerGenerator : IGenerator
    {
        public string ProviderId => "free-tier";
        public GeneratorKind Kind => GeneratorKind.TerrainLayer;
        public string DisplayName => "Free-tier image (TerrainLayer)";

        public bool IsConfigured() => FreeTierImageClient.AnyProviderAvailable();
        public string GetStatus() => FreeTierImageClient.DescribeAvailability();

        public GenerationResult Generate(GenerationRequest request)
        {
            if (request == null)
                return GenerationResult.Fail("Null request.", ProviderId);

            var width = OptInt(request.ProviderOptions, "width", 1024);
            var height = OptInt(request.ProviderOptions, "height", 1024);
            var tileSize = OptFloat(request.ProviderOptions, "tileSize", 15f);

            ImageGenResult img;
            try
            {
                img = FreeTierImageClient.Generate(request.Prompt, width, height);
            }
            catch (Exception ex)
            {
                return GenerationResult.Fail($"Image client threw: {ex.Message}", ProviderId);
            }

            if (!img.Success || img.Bytes == null || img.Bytes.Length == 0)
                return GenerationResult.Fail(img.Error ?? "Diffuse texture generation failed.", ProviderId);

            try
            {
                var (layerPath, texPath) = WriteTerrainLayer(request.OutputAssetPath, img, tileSize);
                return GenerationResult.Ok(layerPath, img.ProviderUsed, new Dictionary<string, object>
                {
                    ["model"] = img.Model,
                    ["texturePath"] = texPath,
                    ["width"] = width,
                    ["height"] = height,
                    ["tileSize"] = tileSize,
                    ["bytes"] = img.Bytes.Length,
                    ["attempts"] = string.Join(" | ", img.Attempts)
                });
            }
            catch (Exception ex)
            {
                return GenerationResult.Fail($"Generated diffuse but failed to build terrain layer: {ex.Message}", img.ProviderUsed);
            }
        }

        private (string layerPath, string texturePath) WriteTerrainLayer(string requestedOutput, ImageGenResult img, float tileSize)
        {
            var baseRel = NormalizeBase(requestedOutput);
            EnsureFolder(ParentFolder(baseRel));

            // 1) Write + import the diffuse texture.
            var ext = string.IsNullOrEmpty(img.Extension) ? ".png" : img.Extension;
            var texRel = AssetDatabase.GenerateUniqueAssetPath($"{baseRel}_albedo{ext}");
            File.WriteAllBytes(ToAbsolute(texRel), img.Bytes);
            AssetDatabase.ImportAsset(texRel, ImportAssetOptions.ForceSynchronousImport);
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texRel);

            // 2) Build the TerrainLayer asset referencing it.
            var layer = new TerrainLayer
            {
                diffuseTexture = tex,
                tileSize = new Vector2(Mathf.Max(0.01f, tileSize), Mathf.Max(0.01f, tileSize)),
                tileOffset = Vector2.zero
            };

            var layerRel = AssetDatabase.GenerateUniqueAssetPath($"{baseRel}.terrainlayer");
            AssetDatabase.CreateAsset(layer, layerRel);
            AssetDatabase.SaveAssets();

            return (layerRel, texRel);
        }

        // ── path helpers (Assets-relative) ─────────────────────────────────────────────

        private static string NormalizeBase(string requestedOutput)
        {
            var rel = string.IsNullOrWhiteSpace(requestedOutput)
                ? $"{GeneratorConfig.Data.defaultOutputDirectory.TrimEnd('/')}/TerrainLayer_{DateTime.UtcNow:yyyyMMdd_HHmmss}"
                : requestedOutput.Replace('\\', '/');

            if (!rel.StartsWith("Assets/", StringComparison.Ordinal))
                rel = "Assets/" + rel.TrimStart('/');

            foreach (var e in new[] { ".terrainlayer", ".png", ".jpg", ".jpeg", ".gif", ".webp" })
                if (rel.EndsWith(e, StringComparison.OrdinalIgnoreCase))
                    return rel.Substring(0, rel.Length - e.Length);
            return rel;
        }

        private static string ParentFolder(string assetPath)
        {
            var dir = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            return string.IsNullOrEmpty(dir) ? "Assets" : dir;
        }

        private static string ToAbsolute(string assetRel)
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            return Path.GetFullPath(Path.Combine(projectRoot, assetRel));
        }

        private static void EnsureFolder(string assetFolder)
        {
            if (string.IsNullOrEmpty(assetFolder) || AssetDatabase.IsValidFolder(assetFolder)) return;
            var parent = ParentFolder(assetFolder);
            var name = Path.GetFileName(assetFolder);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            if (!string.IsNullOrEmpty(name)) AssetDatabase.CreateFolder(parent, name);
        }

        private static int OptInt(Dictionary<string, object> opts, string key, int fallback)
        {
            if (opts == null || !opts.TryGetValue(key, out var raw) || raw == null) return fallback;
            try
            {
                switch (raw)
                {
                    case int i: return i;
                    case long l: return (int)l;
                    case double d: return (int)d;
                    case float f: return (int)f;
                    default: return int.TryParse(raw.ToString(), out var parsed) ? parsed : fallback;
                }
            }
            catch { return fallback; }
        }

        private static float OptFloat(Dictionary<string, object> opts, string key, float fallback)
        {
            if (opts == null || !opts.TryGetValue(key, out var raw) || raw == null) return fallback;
            try
            {
                switch (raw)
                {
                    case float f: return f;
                    case double d: return (float)d;
                    case int i: return i;
                    case long l: return l;
                    default:
                        return float.TryParse(raw.ToString(), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;
                }
            }
            catch { return fallback; }
        }
    }
}
