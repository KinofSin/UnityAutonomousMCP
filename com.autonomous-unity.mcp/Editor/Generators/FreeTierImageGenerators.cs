using System;
using System.Collections.Generic;
using System.IO;
using AutonomousMcp.Editor.Core;
using UnityEditor;
using UnityEngine;

namespace AutonomousMcp.Editor.Generators
{
    /// <summary>
    /// Real text-to-image generator built on <see cref="FreeTierImageClient"/>: rotates your
    /// owned free-tier keys, backs off on rate limits, and fails over to a keyless provider so a
    /// generation request still produces an asset. Shared by the Texture and Sprite kinds.
    ///
    /// ProviderId is "free-tier", so <see cref="Core.GeneratorRegistry.Resolve"/> prefers it over
    /// the "stub" provider whenever it reports configured (which is always, given the keyless
    /// fallback — unless you set GENERATOR_DISABLE_KEYLESS and provide no keys).
    /// </summary>
    internal abstract class FreeTierImageGeneratorBase : IGenerator
    {
        public string ProviderId => "free-tier";
        public abstract GeneratorKind Kind { get; }
        public string DisplayName => $"Free-tier image ({Kind})";

        /// <summary>True if any provider — keyed or keyless — can currently service a request.</summary>
        public bool IsConfigured() => FreeTierImageClient.AnyProviderAvailable();

        public string GetStatus() => FreeTierImageClient.DescribeAvailability();

        /// <summary>Sprite kind flips the imported texture to Sprite mode; Texture leaves it default.</summary>
        protected virtual bool ImportAsSprite => false;

        public GenerationResult Generate(GenerationRequest request)
        {
            if (request == null)
                return GenerationResult.Fail("Null request.", ProviderId);

            var (width, height) = ReadSize(request);

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
                return GenerationResult.Fail(img.Error ?? "Image generation failed.", ProviderId);

            string assetPath;
            try
            {
                assetPath = WriteAndImport(request.OutputAssetPath, img);
            }
            catch (Exception ex)
            {
                return GenerationResult.Fail($"Generated image but failed to import it: {ex.Message}", img.ProviderUsed);
            }

            return GenerationResult.Ok(assetPath, img.ProviderUsed, new Dictionary<string, object>
            {
                ["model"] = img.Model,
                ["width"] = width,
                ["height"] = height,
                ["bytes"] = img.Bytes.Length,
                ["importedAs"] = ImportAsSprite ? "Sprite" : "Texture",
                ["attempts"] = string.Join(" | ", img.Attempts)
            });
        }

        private static (int width, int height) ReadSize(GenerationRequest request)
        {
            var width = OptInt(request.ProviderOptions, "width", 1024);
            var height = OptInt(request.ProviderOptions, "height", 1024);
            return (width, height);
        }

        private string WriteAndImport(string requestedOutput, ImageGenResult img)
        {
            // Normalize to an Assets-relative path with the correct extension for the returned bytes.
            var rel = string.IsNullOrWhiteSpace(requestedOutput)
                ? $"{GeneratorConfig.Data.defaultOutputDirectory.TrimEnd('/')}/{Kind}_{DateTime.UtcNow:yyyyMMdd_HHmmss}"
                : requestedOutput.Replace('\\', '/');

            if (!rel.StartsWith("Assets/", StringComparison.Ordinal))
                rel = "Assets/" + rel.TrimStart('/');

            var ext = string.IsNullOrEmpty(img.Extension) ? ".png" : img.Extension;
            if (!rel.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                rel = StripKnownImageExtension(rel) + ext;

            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            var absolute = Path.GetFullPath(Path.Combine(projectRoot, rel));

            var dir = Path.GetDirectoryName(absolute);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllBytes(absolute, img.Bytes);

            AssetDatabase.ImportAsset(rel, ImportAssetOptions.ForceSynchronousImport);

            if (ImportAsSprite)
            {
                if (AssetImporter.GetAtPath(rel) is TextureImporter ti && ti.textureType != TextureImporterType.Sprite)
                {
                    ti.textureType = TextureImporterType.Sprite;
                    ti.SaveAndReimport();
                }
            }

            return rel;
        }

        private static string StripKnownImageExtension(string path)
        {
            foreach (var e in new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp" })
                if (path.EndsWith(e, StringComparison.OrdinalIgnoreCase))
                    return path.Substring(0, path.Length - e.Length);
            return path;
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
                    default:
                        return int.TryParse(raw.ToString(), out var parsed) ? parsed : fallback;
                }
            }
            catch { return fallback; }
        }
    }

    /// <summary>Texture asset via the free-tier image pipeline.</summary>
    internal sealed class FreeTierTextureGenerator : FreeTierImageGeneratorBase
    {
        public override GeneratorKind Kind => GeneratorKind.Texture;
    }

    /// <summary>Sprite asset via the free-tier image pipeline (imported as a Sprite).</summary>
    internal sealed class FreeTierSpriteGenerator : FreeTierImageGeneratorBase
    {
        public override GeneratorKind Kind => GeneratorKind.Sprite;
        protected override bool ImportAsSprite => true;
    }
}
