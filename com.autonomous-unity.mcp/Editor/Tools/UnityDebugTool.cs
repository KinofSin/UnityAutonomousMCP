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
    /// unity_debug — diagnostic queries (read-only).
    /// </summary>
    public static class UnityDebugTool
    {
        [InitializeOnLoadMethod]
        private static void Register()
        {
            ToolRegistry.Register("unity_debug", ToolMode.Read, ToolCategory.Diagnostic,
                "Diagnostic queries. Actions: count_objects, find_null_components, " +
                "active_camera, layer_collision_matrix, render_pipeline.",
                Handle);
        }

        private static AutonomousMcpToolResponse Handle(JObject args)
        {
            var action = args.Value<string>("action") ?? "count_objects";
            switch (action)
            {
                case "count_objects": return CountObjects();
                case "find_null_components": return FindNullComponents();
                case "active_camera": return ActiveCamera();
                case "layer_collision_matrix": return LayerCollisionMatrix();
                case "render_pipeline": return RenderPipeline();
                default:
                    return Err($"Unsupported unity_debug action '{action}'.");
            }
        }

        private static AutonomousMcpToolResponse CountObjects()
        {
            var scene = SceneManager.GetActiveScene();
            int total = 0, active = 0, withRenderer = 0, withCollider = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    total++;
                    if (t.gameObject.activeInHierarchy) active++;
                    if (t.GetComponent<Renderer>() != null) withRenderer++;
                    if (t.GetComponent<Collider>() != null) withCollider++;
                }
            }
            return Ok(new { action = "count_objects", total, active, withRenderer, withCollider });
        }

        private static AutonomousMcpToolResponse FindNullComponents()
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
                            hits.Add(new { path = GetPath(t), slot = i });
                        }
                    }
                }
            }
            return Ok(new { action = "find_null_components", count = hits.Count, results = hits });
        }

        private static AutonomousMcpToolResponse ActiveCamera()
        {
            var cam = Camera.main;
            if (cam == null) return Ok(new { action = "active_camera", found = false });
            return Ok(new
            {
                action = "active_camera",
                found = true,
                name = cam.name,
                fov = cam.fieldOfView,
                clearFlags = cam.clearFlags.ToString(),
                clip = new { near = cam.nearClipPlane, far = cam.farClipPlane },
                position = new { cam.transform.position.x, cam.transform.position.y, cam.transform.position.z }
            });
        }

        private static AutonomousMcpToolResponse LayerCollisionMatrix()
        {
            var matrix = new List<object>();
            for (int i = 0; i < 32; i++)
            {
                var name = LayerMask.LayerToName(i);
                if (string.IsNullOrEmpty(name)) continue;
                for (int j = i; j < 32; j++)
                {
                    var name2 = LayerMask.LayerToName(j);
                    if (string.IsNullOrEmpty(name2)) continue;
                    if (Physics.GetIgnoreLayerCollision(i, j))
                    {
                        matrix.Add(new { a = name, b = name2, ignored = true });
                    }
                }
            }
            return Ok(new { action = "layer_collision_matrix", ignoredCount = matrix.Count, ignored = matrix });
        }

        private static AutonomousMcpToolResponse RenderPipeline()
        {
            var rp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            return Ok(new
            {
                action = "render_pipeline",
                name = rp == null ? "Built-in (Standard)" : rp.GetType().Name,
                colorSpace = QualitySettings.activeColorSpace.ToString()
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
