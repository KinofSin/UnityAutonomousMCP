using System.Collections.Generic;
using System.Linq;
using AutonomousMcp.Editor.Core;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AutonomousMcp.Editor.Tools
{
    /// <summary>
    /// unity_smart — high-level structured queries. Predicate-driven without needing to
    /// chain manage_gameobject + manage_component yourself.
    /// </summary>
    public static class UnitySmartTool
    {
        [InitializeOnLoadMethod]
        private static void Register()
        {
            ToolRegistry.Register("unity_smart", ToolMode.Read, ToolCategory.Diagnostic,
                "Predicate scene queries. Actions: meshes_over_tris, renderers_with_shader, " +
                "objects_with_component, materials_using_texture, find_in_layer.",
                Handle);
        }

        private static AutonomousMcpToolResponse Handle(JObject args)
        {
            var action = args.Value<string>("action") ?? "objects_with_component";
            switch (action)
            {
                case "meshes_over_tris": return MeshesOverTris(args);
                case "renderers_with_shader": return RenderersWithShader(args);
                case "objects_with_component": return ObjectsWithComponent(args);
                case "materials_using_texture": return MaterialsUsingTexture(args);
                case "find_in_layer": return FindInLayer(args);
                default: return Err($"Unsupported unity_smart action '{action}'.");
            }
        }

        private static AutonomousMcpToolResponse MeshesOverTris(JObject args)
        {
            var min = args.Value<int?>("min_tris") ?? 5000;
            var scene = SceneManager.GetActiveScene();
            var hits = new List<object>();
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (smr.sharedMesh == null) continue;
                    int tris = smr.sharedMesh.triangles.Length / 3;
                    if (tris >= min) hits.Add(new { path = GetPath(smr.transform), kind = "SkinnedMesh", tris });
                }
                foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null) continue;
                    int tris = mf.sharedMesh.triangles.Length / 3;
                    if (tris >= min) hits.Add(new { path = GetPath(mf.transform), kind = "MeshFilter", tris });
                }
            }
            return Ok(new { action = "meshes_over_tris", min, count = hits.Count, results = hits });
        }

        private static AutonomousMcpToolResponse RenderersWithShader(JObject args)
        {
            var shaderName = args.Value<string>("shader") ?? "";
            var scene = SceneManager.GetActiveScene();
            var hits = new List<object>();
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (r.sharedMaterials == null) continue;
                    foreach (var m in r.sharedMaterials)
                    {
                        if (m == null || m.shader == null) continue;
                        if (m.shader.name.IndexOf(shaderName, System.StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            hits.Add(new
                            {
                                path = GetPath(r.transform),
                                material = m.name,
                                shaderName = m.shader.name
                            });
                            break;
                        }
                    }
                }
            }
            return Ok(new { action = "renderers_with_shader", shader = shaderName, count = hits.Count, results = hits });
        }

        private static AutonomousMcpToolResponse ObjectsWithComponent(JObject args)
        {
            var typeName = args.Value<string>("component_type");
            if (string.IsNullOrEmpty(typeName)) return Err("component_type required.");
            var scene = SceneManager.GetActiveScene();
            var hits = new List<object>();
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    foreach (var c in t.GetComponents<Component>())
                    {
                        if (c == null) continue;
                        if (c.GetType().Name == typeName || c.GetType().FullName == typeName)
                        {
                            hits.Add(new { path = GetPath(t), component = c.GetType().FullName });
                            break;
                        }
                    }
                }
            }
            return Ok(new { action = "objects_with_component", typeName, count = hits.Count, results = hits });
        }

        private static AutonomousMcpToolResponse MaterialsUsingTexture(JObject args)
        {
            var texPath = args.Value<string>("texture_path");
            if (string.IsNullOrEmpty(texPath)) return Err("texture_path required.");
            var folder = args.Value<string>("folder") ?? "Assets";

            var tex = AssetDatabase.LoadAssetAtPath<Texture>(texPath);
            if (tex == null) return Err($"Texture not found at {texPath}.");

            var hits = new List<string>();
            foreach (var g in AssetDatabase.FindAssets("t:Material", new[] { folder }))
            {
                var mPath = AssetDatabase.GUIDToAssetPath(g);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(mPath);
                if (mat == null) continue;
                var so = new SerializedObject(mat);
                var p = so.FindProperty("m_SavedProperties.m_TexEnvs");
                if (p == null || !p.isArray) continue;
                for (int i = 0; i < p.arraySize; i++)
                {
                    var envProp = p.GetArrayElementAtIndex(i).FindPropertyRelative("second");
                    var texProp = envProp?.FindPropertyRelative("m_Texture");
                    if (texProp?.objectReferenceValue == tex) { hits.Add(mPath); break; }
                }
            }
            return Ok(new { action = "materials_using_texture", texture_path = texPath, count = hits.Count, materials = hits });
        }

        private static AutonomousMcpToolResponse FindInLayer(JObject args)
        {
            var layerName = args.Value<string>("layer");
            if (string.IsNullOrEmpty(layerName)) return Err("layer required.");
            int idx = LayerMask.NameToLayer(layerName);
            if (idx < 0) return Err($"Layer '{layerName}' not defined.");

            var scene = SceneManager.GetActiveScene();
            var hits = new List<string>();
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.gameObject.layer == idx) hits.Add(GetPath(t));
                }
            }
            return Ok(new { action = "find_in_layer", layer = layerName, count = hits.Count, paths = hits });
        }

        private static string GetPath(Transform t)
        {
            var sb = new System.Text.StringBuilder(t.name);
            var cur = t.parent;
            while (cur != null) { sb.Insert(0, cur.name + "/"); cur = cur.parent; }
            return sb.ToString();
        }

        private static AutonomousMcpToolResponse Ok(object d) =>
            new AutonomousMcpToolResponse { success = true, data = JToken.FromObject(d), error = string.Empty };
        private static AutonomousMcpToolResponse Err(string m) =>
            new AutonomousMcpToolResponse { success = false, data = null, error = m };
    }
}
