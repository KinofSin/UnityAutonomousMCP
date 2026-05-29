using System;
using System.Collections.Generic;
using System.IO;
using AutonomousMcp.Editor.Core;
using UnityEditor;
using UnityEngine;

namespace AutonomousMcp.Editor.Generators
{
    /// <summary>
    /// Real Cubemap generator built on the same <see cref="FreeTierImageClient"/> failover engine.
    /// It generates a 2:1 equirectangular (latlong) panorama — rotating your owned free-tier keys
    /// and falling back to the keyless provider on rate limits — then imports it as a
    /// <see cref="TextureImporterShape.TextureCube"/> so it can be dropped straight onto a skybox.
    ///
    /// Defaults to 2048x1024 (equirectangular needs a 2:1 aspect); width/height options are honored
    /// but coerced toward 2:1 if a caller passes a mismatched aspect.
    /// </summary>
    internal sealed class FreeTierCubemapGenerator : IGenerator
    {
        public string ProviderId => "free-tier";
        public GeneratorKind Kind => GeneratorKind.Cubemap;
        public string DisplayName => "Free-tier image (Cubemap / equirectangular)";

        public bool IsConfigured() => FreeTierImageClient.AnyProviderAvailable();
        public string GetStatus() => FreeTierImageClient.DescribeAvailability();

        public GenerationResult Generate(GenerationRequest request)
        {
            if (request == null)
                return GenerationResult.Fail("Null request.", ProviderId);

            var (width, height) = ResolveEquirectSize(request.ProviderOptions);

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
                return GenerationResult.Fail(img.Error ?? "Panorama generation failed.", ProviderId);

            try
            {
                var assetPath = WriteAndImportCubemap(request.OutputAssetPath, img, out var isCubemap);
                return GenerationResult.Ok(assetPath, img.ProviderUsed, new Dictionary<string, object>
                {
                    ["model"] = img.Model,
                    ["width"] = width,
                    ["height"] = height,
                    ["bytes"] = img.Bytes.Length,
                    ["importedAs"] = isCubemap ? "Cubemap (latlong)" : "Texture (cube import unavailable)",
                    ["mapping"] = "equirectangular",
                    ["attempts"] = string.Join(" | ", img.Attempts)
                });
            }
            catch (Exception ex)
            {
                return GenerationResult.Fail($"Generated panorama but failed to import as cubemap: {ex.Message}", img.ProviderUsed);
            }
        }

        private static (int width, int height) ResolveEquirectSize(Dictionary<string, object> opts)
        {
            var width = OptInt(opts, "width", 2048);
            var height = OptInt(opts, "height", 0);
            // Equirectangular maps require a 2:1 aspect; derive height when unset or mismatched.
            if (height <= 0 || width != height * 2)
                height = Math.Max(64, width / 2);
            return (width, height);
        }

        private string WriteAndImportCubemap(string requestedOutput, ImageGenResult img, out bool isCubemap)
        {
            var rel = NormalizeImagePath(requestedOutput, img.Extension);

            var dir = Path.GetDirectoryName(ToAbsolute(rel));
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllBytes(ToAbsolute(rel), img.Bytes);
            AssetDatabase.ImportAsset(rel, ImportAssetOptions.ForceSynchronousImport);

            isCubemap = false;
            if (AssetImporter.GetAtPath(rel) is TextureImporter ti)
            {
                ti.textureShape = TextureImporterShape.TextureCube;
                // AutoCubemap picks latlong/cylindrical based on the 2:1 aspect ratio.
                ti.generateCubemap = TextureImporterGenerateCubemap.AutoCubemap;
                ti.SaveAndReimport();
                isCubemap = AssetDatabase.LoadAssetAtPath<Cubemap>(rel) != null;
            }

            return rel;
        }

        // ── path helpers (Assets-relative) ─────────────────────────────────────────────

        private static string NormalizeImagePath(string requestedOutput, string ext)
        {
            ext = string.IsNullOrEmpty(ext) ? ".png" : ext;
            var rel = string.IsNullOrWhiteSpace(requestedOutput)
                ? $"{GeneratorConfig.Data.defaultOutputDirectory.TrimEnd('/')}/Cubemap_{DateTime.UtcNow:yyyyMMdd_HHmmss}"
                : requestedOutput.Replace('\\', '/');

            if (!rel.StartsWith("Assets/", StringComparison.Ordinal))
                rel = "Assets/" + rel.TrimStart('/');

            foreach (var e in new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp" })
                if (rel.EndsWith(e, StringComparison.OrdinalIgnoreCase))
                    rel = rel.Substring(0, rel.Length - e.Length);

            return rel + ext;
        }

        private static string ToAbsolute(string assetRel)
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            return Path.GetFullPath(Path.Combine(projectRoot, assetRel));
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
    }
}
