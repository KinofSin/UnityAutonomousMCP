using System;
using System.Collections.Generic;
using System.IO;
using AutonomousMcp.Editor.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace AutonomousMcp.Editor.Generators
{
    /// <summary>
    /// Real Material generator built on the same <see cref="FreeTierImageClient"/> failover engine:
    /// it generates a base-color (albedo) texture — rotating your owned free-tier keys and falling
    /// back to the keyless provider on rate limits — imports it, then creates a Material wired to
    /// that texture using a shader appropriate to the active render pipeline (URP / HDRP / Built-in).
    ///
    /// Output layout for an output path "Assets/Generated/Material_x":
    ///   Assets/Generated/Material_x.mat            (primary asset returned)
    ///   Assets/Generated/Material_x_albedo.png     (generated texture, referenced by the material)
    /// </summary>
    internal sealed class FreeTierMaterialGenerator : IGenerator
    {
        public string ProviderId => "free-tier";
        public GeneratorKind Kind => GeneratorKind.Material;
        public string DisplayName => "Free-tier image (Material)";

        public bool IsConfigured() => FreeTierImageClient.AnyProviderAvailable();
        public string GetStatus() => FreeTierImageClient.DescribeAvailability();

        public GenerationResult Generate(GenerationRequest request)
        {
            if (request == null)
                return GenerationResult.Fail("Null request.", ProviderId);

            var width = OptInt(request.ProviderOptions, "width", 1024);
            var height = OptInt(request.ProviderOptions, "height", 1024);

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
                return GenerationResult.Fail(img.Error ?? "Albedo generation failed.", ProviderId);

            try
            {
                var (matPath, texPath) = WriteMaterial(request.OutputAssetPath, img);
                return GenerationResult.Ok(matPath, img.ProviderUsed, new Dictionary<string, object>
                {
                    ["model"] = img.Model,
                    ["texturePath"] = texPath,
                    ["width"] = width,
                    ["height"] = height,
                    ["bytes"] = img.Bytes.Length,
                    ["shader"] = _lastShaderName,
                    ["attempts"] = string.Join(" | ", img.Attempts)
                });
            }
            catch (Exception ex)
            {
                return GenerationResult.Fail($"Generated albedo but failed to build material: {ex.Message}", img.ProviderUsed);
            }
        }

        private string _lastShaderName = "";

        private (string materialPath, string texturePath) WriteMaterial(string requestedOutput, ImageGenResult img)
        {
            var baseRel = NormalizeBase(requestedOutput);
            EnsureFolder(ParentFolder(baseRel));

            // 1) Write + import the albedo texture.
            var ext = string.IsNullOrEmpty(img.Extension) ? ".png" : img.Extension;
            var texRel = AssetDatabase.GenerateUniqueAssetPath($"{baseRel}_albedo{ext}");
            File.WriteAllBytes(ToAbsolute(texRel), img.Bytes);
            AssetDatabase.ImportAsset(texRel, ImportAssetOptions.ForceSynchronousImport);
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texRel);

            // 2) Build a material on a render-pipeline-appropriate shader and wire the base map.
            var shader = ResolveLitShader(out var baseMapProp);
            _lastShaderName = shader != null ? shader.name : "(none)";
            var mat = new Material(shader != null ? shader : Shader.Find("Standard"));
            if (tex != null) AssignBaseMap(mat, baseMapProp, tex);

            var matRel = AssetDatabase.GenerateUniqueAssetPath($"{baseRel}.mat");
            AssetDatabase.CreateAsset(mat, matRel);
            AssetDatabase.SaveAssets();

            return (matRel, texRel);
        }

        private static void AssignBaseMap(Material mat, string preferredProp, Texture2D tex)
        {
            // Try the pipeline's known base-map property first, then common fallbacks.
            foreach (var prop in new[] { preferredProp, "_BaseMap", "_MainTex", "_BaseColorMap" })
            {
                if (!string.IsNullOrEmpty(prop) && mat.HasProperty(prop))
                {
                    mat.SetTexture(prop, tex);
                    return;
                }
            }
            // Last resort: the convenience accessor (maps to whatever the shader tags as the main tex).
            try { mat.mainTexture = tex; } catch { /* shader exposes no main texture */ }
        }

        private static Shader ResolveLitShader(out string baseMapProp)
        {
            var rp = GraphicsSettings.currentRenderPipeline != null
                ? GraphicsSettings.currentRenderPipeline
                : GraphicsSettings.defaultRenderPipeline;
            var rpName = rp != null ? rp.GetType().FullName ?? "" : "";

            if (rpName.IndexOf("Universal", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var s = Shader.Find("Universal Render Pipeline/Lit");
                if (s != null) { baseMapProp = "_BaseMap"; return s; }
            }
            else if (rpName.IndexOf("HighDefinition", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     rpName.IndexOf("HDRenderPipeline", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var s = Shader.Find("HDRP/Lit");
                if (s != null) { baseMapProp = "_BaseColorMap"; return s; }
            }

            // Built-in (or pipeline shader unavailable): Standard, then a guaranteed fallback.
            var std = Shader.Find("Standard");
            if (std != null) { baseMapProp = "_MainTex"; return std; }

            baseMapProp = "_MainTex";
            return Shader.Find("Sprites/Default");
        }

        // ── path helpers (Assets-relative) ─────────────────────────────────────────────

        private static string NormalizeBase(string requestedOutput)
        {
            var rel = string.IsNullOrWhiteSpace(requestedOutput)
                ? $"{GeneratorConfig.Data.defaultOutputDirectory.TrimEnd('/')}/Material_{DateTime.UtcNow:yyyyMMdd_HHmmss}"
                : requestedOutput.Replace('\\', '/');

            if (!rel.StartsWith("Assets/", StringComparison.Ordinal))
                rel = "Assets/" + rel.TrimStart('/');

            // Drop any extension the caller may have supplied; we add .mat / _albedo.ext ourselves.
            foreach (var e in new[] { ".mat", ".png", ".jpg", ".jpeg", ".gif", ".webp" })
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

        /// <summary>Create nested asset folders so AssetDatabase.CreateAsset has a valid destination.</summary>
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
    }
}
