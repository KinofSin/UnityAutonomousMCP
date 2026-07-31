using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace AutonomousMcp.Editor.Perception
{
    /// <summary>
    /// Material identity + non-default property filtering for the scene dossier.
    /// Locked Poiyomi shaders bake values into a generated variant — property diffs
    /// against a fresh Material(shader) are not meaningful until unlocked.
    /// </summary>
    public static class MaterialDigest
    {
        public sealed class ShaderIdentity
        {
            public bool locked;
            public string family;
            public string displayName;
            public string rawName;
            public string note;
        }

        public sealed class PropertyDiff
        {
            public string name;
            public string displayName;
            public string type;
            public object value;
            public object defaultValue;
        }

        public sealed class DiffResult
        {
            public List<PropertyDiff> changed = new List<PropertyDiff>();
            public int suppressedDefaults;
            public bool propertiesNotMeaningfullyReadable;
            public string note;
        }

        public static ShaderIdentity ParseShaderIdentity(string shaderName)
        {
            var identity = new ShaderIdentity
            {
                rawName = shaderName ?? string.Empty,
                family = "other",
                displayName = shaderName ?? string.Empty
            };

            if (string.IsNullOrEmpty(shaderName))
                return identity;

            const string lockedPrefix = "Hidden/Locked/";
            var rest = shaderName;
            if (rest.StartsWith(lockedPrefix, StringComparison.Ordinal))
            {
                identity.locked = true;
                rest = rest.Substring(lockedPrefix.Length);
                identity.note =
                    "Poiyomi (or similar) locked shader: chosen values are baked into this variant. " +
                    "Per-property diffs are not meaningfully readable or editable until unlocked; " +
                    "texture assignments and render queue still are.";
            }

            // Strip trailing /<hash> that locked shaders append.
            var lastSlash = rest.LastIndexOf('/');
            if (lastSlash >= 0)
            {
                var tail = rest.Substring(lastSlash + 1);
                if (LooksLikeHash(tail))
                    rest = rest.Substring(0, lastSlash);
            }

            identity.displayName = rest;
            identity.family = DetectFamily(rest);
            return identity;
        }

        public static DiffResult DiffNonDefault(Material mat)
        {
            var result = new DiffResult();
            if (mat == null || mat.shader == null)
            {
                result.note = "Material or shader is null.";
                return result;
            }

            var identity = ParseShaderIdentity(mat.shader.name);
            if (identity.locked)
            {
                result.propertiesNotMeaningfullyReadable = true;
                result.note = identity.note;
                // Still surface texture slots + any clearly assigned textures — those remain useful.
                CollectTextureAssignments(mat, result);
                return result;
            }

            Material defaults = null;
            try
            {
                defaults = new Material(mat.shader);
                int count = mat.shader.GetPropertyCount();
                for (int i = 0; i < count; i++)
                {
                    var propName = mat.shader.GetPropertyName(i);
                    var propType = mat.shader.GetPropertyType(i);
                    var display = mat.shader.GetPropertyDescription(i);
                    if (!TryReadProperty(mat, propName, propType, out var cur) ||
                        !TryReadProperty(defaults, propName, propType, out var def))
                    {
                        continue;
                    }

                    if (ValuesEqual(cur, def))
                    {
                        result.suppressedDefaults++;
                        continue;
                    }

                    result.changed.Add(new PropertyDiff
                    {
                        name = propName,
                        displayName = display,
                        type = propType.ToString(),
                        value = cur,
                        defaultValue = def
                    });
                }
            }
            finally
            {
                if (defaults != null)
                    UnityEngine.Object.DestroyImmediate(defaults);
            }

            return result;
        }

        public static object Summarize(Material mat, IEnumerable<string> usedByPaths)
        {
            var shaderName = mat?.shader != null ? mat.shader.name : "(none)";
            var identity = ParseShaderIdentity(shaderName);
            var texRefs = new List<object>();
            if (mat != null && mat.shader != null)
            {
                int count = mat.shader.GetPropertyCount();
                for (int i = 0; i < count; i++)
                {
                    if (mat.shader.GetPropertyType(i) != ShaderPropertyType.Texture) continue;
                    var propName = mat.shader.GetPropertyName(i);
                    var tex = mat.GetTexture(propName);
                    if (tex == null) continue;
                    texRefs.Add(new
                    {
                        property = propName,
                        texture = tex.name,
                        path = AssetDatabase.GetAssetPath(tex),
                        instanceId = tex.GetInstanceID()
                    });
                }
            }

            return new
            {
                name = mat != null ? mat.name : null,
                instanceId = mat != null ? mat.GetInstanceID() : 0,
                path = mat != null ? AssetDatabase.GetAssetPath(mat) : null,
                shader = shaderName,
                family = identity.family,
                locked = identity.locked,
                displayShader = identity.displayName,
                lockedNote = identity.locked ? identity.note : null,
                renderQueue = mat != null ? mat.renderQueue : 0,
                usedBy = usedByPaths,
                textures = texRefs
            };
        }

        private static void CollectTextureAssignments(Material mat, DiffResult result)
        {
            int count = mat.shader.GetPropertyCount();
            for (int i = 0; i < count; i++)
            {
                if (mat.shader.GetPropertyType(i) != ShaderPropertyType.Texture) continue;
                var propName = mat.shader.GetPropertyName(i);
                var tex = mat.GetTexture(propName);
                if (tex == null)
                {
                    result.suppressedDefaults++;
                    continue;
                }

                result.changed.Add(new PropertyDiff
                {
                    name = propName,
                    displayName = mat.shader.GetPropertyDescription(i),
                    type = "Texture",
                    value = new { name = tex.name, path = AssetDatabase.GetAssetPath(tex) },
                    defaultValue = null
                });
            }
        }

        private static bool TryReadProperty(Material mat, string propName, ShaderPropertyType propType, out object value)
        {
            value = null;
            try
            {
                switch (propType)
                {
                    case ShaderPropertyType.Color:
                        var c = mat.GetColor(propName);
                        value = new { r = c.r, g = c.g, b = c.b, a = c.a };
                        return true;
                    case ShaderPropertyType.Float:
                    case ShaderPropertyType.Range:
                        value = mat.GetFloat(propName);
                        return true;
                    case ShaderPropertyType.Vector:
                        var v = mat.GetVector(propName);
                        value = new { x = v.x, y = v.y, z = v.z, w = v.w };
                        return true;
                    case ShaderPropertyType.Texture:
                        var tex = mat.GetTexture(propName);
                        value = tex != null
                            ? (object)new { name = tex.name, path = AssetDatabase.GetAssetPath(tex) }
                            : null;
                        return true;
#if UNITY_2021_1_OR_NEWER
                    case ShaderPropertyType.Int:
                        value = mat.GetInteger(propName);
                        return true;
#endif
                    default:
                        return false;
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool ValuesEqual(object a, object b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;
            if (a is float fa && b is float fb) return Mathf.Approximately(fa, fb);
            if (a is int ia && b is int ib) return ia == ib;
            // Anonymous Color/Vector/Texture payloads: ToString includes property values.
            return string.Equals(a.ToString(), b.ToString(), StringComparison.Ordinal);
        }

        private static bool LooksLikeHash(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length < 8) return false;
            foreach (var ch in s)
            {
                bool hex = (ch >= '0' && ch <= '9') ||
                           (ch >= 'a' && ch <= 'f') ||
                           (ch >= 'A' && ch <= 'F');
                if (!hex) return false;
            }
            return true;
        }

        private static string DetectFamily(string displayName)
        {
            if (string.IsNullOrEmpty(displayName)) return "other";
            if (displayName.IndexOf("poiyomi", StringComparison.OrdinalIgnoreCase) >= 0) return "poiyomi";
            if (displayName.IndexOf("lilToon", StringComparison.OrdinalIgnoreCase) >= 0 ||
                displayName.IndexOf("lil/", StringComparison.OrdinalIgnoreCase) >= 0) return "lilToon";
            if (displayName.IndexOf("SCSS", StringComparison.OrdinalIgnoreCase) >= 0 ||
                displayName.IndexOf("Silent's Cel Shading", StringComparison.OrdinalIgnoreCase) >= 0)
                return "scss";
            if (displayName.IndexOf("ORL", StringComparison.OrdinalIgnoreCase) >= 0) return "orl";
            if (displayName.StartsWith("Standard", StringComparison.Ordinal) ||
                displayName.StartsWith("Universal Render Pipeline", StringComparison.Ordinal) ||
                displayName.StartsWith("HDRP/", StringComparison.Ordinal))
                return "builtin";
            return "other";
        }
    }
}
