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
    /// unity_validation — project audit.
    /// Actions: missing_scripts, broken_refs, duplicate_names, empty_renderers,
    ///          missing_textures_on_materials, audit_active_scene.
    /// </summary>
    public static class UnityValidationTool
    {
        [InitializeOnLoadMethod]
        private static void Register()
        {
            ToolRegistry.Register("unity_validation", ToolMode.Read, ToolCategory.Diagnostic,
                "Project + scene audit: missing scripts, broken references, duplicate names, " +
                "empty renderers, missing textures. Actions: missing_scripts, broken_refs, " +
                "duplicate_names, empty_renderers, missing_textures_on_materials, audit_active_scene.",
                Handle);
        }

        private static AutonomousMcpToolResponse Handle(JObject args)
        {
            var action = args.Value<string>("action") ?? "audit_active_scene";
            switch (action)
            {
                case "missing_scripts": return MissingScripts(args);
                case "broken_refs": return BrokenRefs(args);
                case "duplicate_names": return DuplicateNames(args);
                case "empty_renderers": return EmptyRenderers(args);
                case "missing_textures_on_materials": return MissingTexturesOnMaterials(args);
                case "audit_active_scene": return AuditActiveScene(args);
                default:
                    return Err($"Unsupported unity_validation action '{action}'.");
            }
        }

        private static AutonomousMcpToolResponse MissingScripts(JObject args)
        {
            var scene = SceneManager.GetActiveScene();
            var hits = new List<object>();
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    var comps = t.GetComponents<Component>();
                    for (int i = 0; i < comps.Length; i++)
                    {
                        if (comps[i] == null)
                        {
                            hits.Add(new
                            {
                                path = GetPath(t),
                                instanceId = t.gameObject.GetInstanceID(),
                                componentSlot = i
                            });
                        }
                    }
                }
            }
            return Ok(new { action = "missing_scripts", count = hits.Count, results = hits });
        }

        private static AutonomousMcpToolResponse BrokenRefs(JObject args)
        {
            // Lightweight: SerializedProperty walk on every component on active scene
            var scene = SceneManager.GetActiveScene();
            var hits = new List<object>();
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var c in root.GetComponentsInChildren<Component>(true))
                {
                    if (c == null) continue;
                    var so = new SerializedObject(c);
                    var p = so.GetIterator();
                    while (p.NextVisible(true))
                    {
                        if (p.propertyType == SerializedPropertyType.ObjectReference
                            && p.objectReferenceValue == null
                            && p.objectReferenceInstanceIDValue != 0)
                        {
                            hits.Add(new
                            {
                                path = GetPath(c.transform),
                                component = c.GetType().Name,
                                property = p.propertyPath,
                                missingInstanceId = p.objectReferenceInstanceIDValue
                            });
                        }
                    }
                }
            }
            return Ok(new { action = "broken_refs", count = hits.Count, results = hits });
        }

        private static AutonomousMcpToolResponse DuplicateNames(JObject args)
        {
            var includeInactive = args.Value<bool?>("include_inactive") ?? true;
            var scene = SceneManager.GetActiveScene();
            var byName = new Dictionary<string, List<string>>();
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(includeInactive))
                {
                    if (!byName.TryGetValue(t.name, out var list))
                    {
                        list = new List<string>();
                        byName[t.name] = list;
                    }
                    list.Add(GetPath(t));
                }
            }
            var dupes = byName.Where(kv => kv.Value.Count > 1)
                              .Select(kv => new { name = kv.Key, count = kv.Value.Count, paths = kv.Value })
                              .ToList();
            return Ok(new { action = "duplicate_names", count = dupes.Count, results = dupes });
        }

        private static AutonomousMcpToolResponse EmptyRenderers(JObject args)
        {
            var scene = SceneManager.GetActiveScene();
            var hits = new List<object>();
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    bool noMesh = false;
                    if (r is MeshRenderer mr)
                    {
                        var mf = mr.GetComponent<MeshFilter>();
                        noMesh = mf == null || mf.sharedMesh == null;
                    }
                    else if (r is SkinnedMeshRenderer smr)
                    {
                        noMesh = smr.sharedMesh == null;
                    }
                    if (noMesh || r.sharedMaterials == null || r.sharedMaterials.Length == 0 ||
                        r.sharedMaterials.All(m => m == null))
                    {
                        hits.Add(new
                        {
                            path = GetPath(r.transform),
                            type = r.GetType().Name,
                            hasMesh = !noMesh,
                            materialCount = r.sharedMaterials?.Length ?? 0
                        });
                    }
                }
            }
            return Ok(new { action = "empty_renderers", count = hits.Count, results = hits });
        }

        private static AutonomousMcpToolResponse MissingTexturesOnMaterials(JObject args)
        {
            var folder = args.Value<string>("folder") ?? "Assets";
            var guids = AssetDatabase.FindAssets("t:Material", new[] { folder });
            var hits = new List<object>();
            foreach (var g in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null) continue;
                var so = new SerializedObject(mat);
                var p = so.FindProperty("m_SavedProperties.m_TexEnvs");
                if (p == null || !p.isArray) continue;
                for (int i = 0; i < p.arraySize; i++)
                {
                    var entry = p.GetArrayElementAtIndex(i);
                    var nameProp = entry.FindPropertyRelative("first");
                    var envProp = entry.FindPropertyRelative("second");
                    var texProp = envProp?.FindPropertyRelative("m_Texture");
                    if (texProp == null) continue;
                    if (texProp.objectReferenceValue == null
                        && texProp.objectReferenceInstanceIDValue != 0)
                    {
                        hits.Add(new
                        {
                            material = path,
                            slot = nameProp?.stringValue,
                            missingInstanceId = texProp.objectReferenceInstanceIDValue
                        });
                    }
                }
            }
            return Ok(new { action = "missing_textures_on_materials", count = hits.Count, results = hits });
        }

        private static AutonomousMcpToolResponse AuditActiveScene(JObject args)
        {
            var missing = MissingScripts(args).data;
            var broken = BrokenRefs(args).data;
            var dupes = DuplicateNames(args).data;
            var empty = EmptyRenderers(args).data;
            return Ok(new
            {
                action = "audit_active_scene",
                missingScripts = missing,
                brokenRefs = broken,
                duplicateNames = dupes,
                emptyRenderers = empty
            });
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
