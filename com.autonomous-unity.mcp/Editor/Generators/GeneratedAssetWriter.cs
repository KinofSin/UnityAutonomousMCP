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
