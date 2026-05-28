using System.Linq;
using AutonomousMcp.Editor.Core;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace AutonomousMcp.Editor.Tools
{
    /// <summary>
    /// unity_camera — SceneView control + runtime camera listing/creation.
    /// </summary>
    public static class UnityCameraTool
    {
        [InitializeOnLoadMethod]
        private static void Register()
        {
            ToolRegistry.Register("unity_camera", ToolMode.Mutate, ToolCategory.Camera,
                "Camera control. Actions: list, create, sceneview_focus, sceneview_pose, sceneview_align_with_view.",
                Handle);
        }

        private static AutonomousMcpToolResponse Handle(JObject args)
        {
            var action = args.Value<string>("action") ?? "list";
            switch (action)
            {
                case "list":
                {
                    var cams = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)
                        .Select(c => new
                        {
                            name = c.name,
                            instanceId = c.GetInstanceID(),
                            isMain = c == Camera.main,
                            c.fieldOfView,
                            c.orthographic,
                            position = new { c.transform.position.x, c.transform.position.y, c.transform.position.z },
                            clearFlags = c.clearFlags.ToString()
                        }).ToList();
                    return Ok(new { action, count = cams.Count, cameras = cams });
                }
                case "create":
                {
                    var name = args.Value<string>("name") ?? "Camera";
                    var go = new GameObject(name, typeof(Camera));
                    var c = go.GetComponent<Camera>();
                    c.fieldOfView = args.Value<float?>("fov") ?? 60f;
                    if (args.Value<bool?>("tag_main") == true) go.tag = "MainCamera";
                    return Ok(new { action, instanceId = go.GetInstanceID(), name });
                }
                case "sceneview_focus":
                {
                    var name = args.Value<string>("name");
                    GameObject target = string.IsNullOrEmpty(name) ? Selection.activeGameObject : GameObject.Find(name);
                    if (target == null) return Err("No target (provide name or select something).");
                    Selection.activeGameObject = target;
                    SceneView.FrameLastActiveSceneView();
                    return Ok(new { action, focused = target.name });
                }
                case "sceneview_pose":
                {
                    var sv = SceneView.lastActiveSceneView;
                    if (sv == null) return Err("No active SceneView.");
                    return Ok(new
                    {
                        action,
                        pivot = new { sv.pivot.x, sv.pivot.y, sv.pivot.z },
                        rotation = new { sv.rotation.x, sv.rotation.y, sv.rotation.z, sv.rotation.w },
                        size = sv.size
                    });
                }
                case "sceneview_align_with_view":
                {
                    var sv = SceneView.lastActiveSceneView;
                    if (sv == null) return Err("No active SceneView.");
                    var name = args.Value<string>("camera");
                    Camera cam = string.IsNullOrEmpty(name) ? Camera.main : GameObject.Find(name)?.GetComponent<Camera>();
                    if (cam == null) return Err("Camera not found.");
                    sv.AlignViewToObject(cam.transform);
                    return Ok(new { action, alignedWith = cam.name });
                }
                default:
                    return Err($"Unsupported unity_camera action '{action}'.");
            }
        }

        private static AutonomousMcpToolResponse Ok(object d) =>
            new AutonomousMcpToolResponse { success = true, data = JToken.FromObject(d), error = string.Empty };
        private static AutonomousMcpToolResponse Err(string m) =>
            new AutonomousMcpToolResponse { success = false, data = null, error = m };
    }
}
