using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;

namespace AutonomousMcp.Editor.Perception
{
    /// <summary>
    /// Texture importer + runtime memory digest for the scene dossier.
    /// Always reports default maxSize and the Android/Quest platform override when present.
    /// </summary>
    public static class TextureDigest
    {
        public static object DigestsFor(IEnumerable<Texture> textures, Dictionary<int, List<string>> referencedBy)
        {
            var list = new List<object>();
            var seen = new HashSet<int>();
            foreach (var tex in textures)
            {
                if (tex == null) continue;
                var id = tex.GetInstanceID();
                if (!seen.Add(id)) continue;
                list.Add(DigestOne(tex, referencedBy != null && referencedBy.TryGetValue(id, out var refs) ? refs : null));
            }
            return list;
        }

        public static object DigestOne(Texture tex, List<string> referencedBy)
        {
            var path = AssetDatabase.GetAssetPath(tex);
            var importer = string.IsNullOrEmpty(path) ? null : AssetImporter.GetAtPath(path) as TextureImporter;

            int? androidMaxSize = null;
            bool? androidOverride = null;
            string androidFormat = null;
            if (importer != null)
            {
                var android = importer.GetPlatformTextureSettings("Android");
                androidOverride = android.overridden;
                if (android.overridden)
                {
                    androidMaxSize = android.maxTextureSize;
                    androidFormat = android.format.ToString();
                }
            }

            long runtimeBytes = 0;
            try { runtimeBytes = Profiler.GetRuntimeMemorySizeLong(tex); }
            catch { /* profiler unavailable in some contexts */ }

            var tex2d = tex as Texture2D;
            return new
            {
                name = tex.name,
                instanceId = tex.GetInstanceID(),
                path,
                width = tex.width,
                height = tex.height,
                format = tex2d != null ? tex2d.format.ToString() : tex.GetType().Name,
                mipmaps = tex.mipmapCount,
                sRGB = importer != null ? importer.sRGBTexture : (bool?)null,
                maxSize = importer != null ? importer.maxTextureSize : (int?)null,
                androidOverride,
                androidMaxSize,
                androidFormat,
                crunch = importer != null ? importer.crunchedCompression : (bool?)null,
                streaming = importer != null ? importer.streamingMipmaps : (bool?)null,
                isReadable = importer != null ? importer.isReadable : (bool?)null,
                runtimeBytes,
                referencedBy = referencedBy ?? new List<string>()
            };
        }

        /// <summary>
        /// Offline-friendly helper used by EditMode tests: reads Android override from an importer.
        /// </summary>
        public static void ReadAndroidOverride(TextureImporter importer, out bool overridden, out int maxSize)
        {
            overridden = false;
            maxSize = 0;
            if (importer == null) return;
            var android = importer.GetPlatformTextureSettings("Android");
            overridden = android.overridden;
            maxSize = android.maxTextureSize;
        }
    }
}
