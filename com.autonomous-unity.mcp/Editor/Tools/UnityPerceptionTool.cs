using System.Collections.Generic;
using System.Linq;
using AutonomousMcp.Editor.Core;
using AutonomousMcp.Editor.Perception;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AutonomousMcp.Editor.Tools
{
    /// <summary>
    /// unity_perception — compact text "world snapshot" so an LLM can build context with one call.
    /// Combines scene digest + project summary + active asset. Also hosts the sectioned dossier.
    /// </summary>
    public static class UnityPerceptionTool
    {
        [InitializeOnLoadMethod]
        private static void Register()
        {
            ToolRegistry.Register("unity_perception", ToolMode.Read, ToolCategory.Diagnostic,
                "One-shot world snapshot for AI context. Actions: snapshot, scene_digest, project_digest, dossier.",
                Handle);
        }

        private static AutonomousMcpToolResponse Handle(JObject args)
        {
            var action = args.Value<string>("action") ?? "snapshot";
            switch (action)
            {
                case "snapshot":
                    return Ok(new
                    {
                        action,
                        editor = EditorState(),
                        scene = SceneDigest(args),
                        project = ProjectDigest()
                    });
                case "scene_digest":
                    return Ok(new { action, scene = SceneDigest(args) });
                case "project_digest":
                    return Ok(new { action, project = ProjectDigest() });
                case "dossier":
                {
                    var payload = StateDossier.Build(args);
                    var token = JToken.FromObject(payload);
                    if (token is JObject jo && jo.Value<bool?>("success") == false)
                        return Err(jo.Value<string>("error") ?? "dossier failed");
                    return new AutonomousMcpToolResponse
                    {
                        success = true,
                        data = token,
                        error = string.Empty
                    };
                }
                default:
                    return Err($"Unsupported unity_perception action '{action}'.");
            }
        }

        private static object EditorState() => new
        {
            unityVersion = Application.unityVersion,
            isPlaying = EditorApplication.isPlaying,
            isCompiling = EditorApplication.isCompiling,
            isUpdating = EditorApplication.isUpdating,
            buildTarget = EditorUserBuildSettings.activeBuildTarget.ToString()
        };

        private static object SceneDigest(JObject args)
        {
            var includeHierarchyDepth = args.Value<int?>("hierarchy_depth") ?? 2;
            var scene = SceneManager.GetActiveScene();
            var roots = scene.GetRootGameObjects();
            return new
            {
                name = scene.name,
                path = scene.path,
                isDirty = scene.isDirty,
                rootCount = roots.Length,
                rootSummary = roots.Take(50).Select(r => new
                {
                    name = r.name,
                    active = r.activeInHierarchy,
                    childCount = r.transform.childCount,
                    hierarchy = BuildHierarchy(r.transform, 0, includeHierarchyDepth)
                }).ToList()
            };
        }

        private static List<object> BuildHierarchy(Transform t, int depth, int max)
        {
            if (depth >= max) return null;
            var children = new List<object>();
            foreach (Transform c in t)
            {
                children.Add(new
                {
                    name = c.name,
                    children = BuildHierarchy(c, depth + 1, max)
                });
            }
            return children;
        }

        private static object ProjectDigest()
        {
            int sceneCount = AssetDatabase.FindAssets("t:Scene").Length;
            int prefabCount = AssetDatabase.FindAssets("t:Prefab").Length;
            int materialCount = AssetDatabase.FindAssets("t:Material").Length;
            int textureCount = AssetDatabase.FindAssets("t:Texture").Length;
            int scriptCount = AssetDatabase.FindAssets("t:Script").Length;
            int animatorControllerCount = AssetDatabase.FindAssets("t:AnimatorController").Length;
            return new
            {
                sceneCount, prefabCount, materialCount, textureCount, scriptCount, animatorControllerCount,
                renderPipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline?.GetType().Name ?? "Built-in"
            };
        }

        private static AutonomousMcpToolResponse Ok(object d) =>
            new AutonomousMcpToolResponse { success = true, data = JToken.FromObject(d), error = string.Empty };
        private static AutonomousMcpToolResponse Err(string m) =>
            new AutonomousMcpToolResponse { success = false, data = null, error = m };
    }
}
