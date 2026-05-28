using System.Linq;
using AutonomousMcp.Editor.Core;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace AutonomousMcp.Editor.Tools
{
    /// <summary>
    /// unity_lighting — lights + ambient + skybox + reflection probes.
    /// </summary>
    public static class UnityLightingTool
    {
        [InitializeOnLoadMethod]
        private static void Register()
        {
            ToolRegistry.Register("unity_lighting", ToolMode.Mutate, ToolCategory.Lighting,
                "Lights, ambient, skybox, reflection probes. Actions: list_lights, " +
                "create_light, set_ambient, get_ambient, set_skybox, get_skybox.",
                Handle);
        }

        private static AutonomousMcpToolResponse Handle(JObject args)
        {
            var action = args.Value<string>("action") ?? "list_lights";
            switch (action)
            {
                case "list_lights":
                {
                    var lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None)
                        .Select(l => new
                        {
                            name = l.name,
                            instanceId = l.GetInstanceID(),
                            type = l.type.ToString(),
                            intensity = l.intensity,
                            range = l.range,
                            color = new { l.color.r, l.color.g, l.color.b, l.color.a },
                            shadows = l.shadows.ToString()
                        }).ToList();
                    return Ok(new { action, count = lights.Count, lights });
                }
                case "create_light":
                {
                    var name = args.Value<string>("name") ?? "Light";
                    var typeStr = args.Value<string>("type") ?? "Directional";
                    if (!System.Enum.TryParse(typeStr, out LightType type)) return Err($"Unknown light type '{typeStr}'.");
                    var go = new GameObject(name, typeof(Light));
                    var l = go.GetComponent<Light>();
                    l.type = type;
                    l.intensity = args.Value<float?>("intensity") ?? 1f;
                    l.range = args.Value<float?>("range") ?? 10f;
                    return Ok(new { action, instanceId = go.GetInstanceID(), name, type = typeStr });
                }
                case "set_ambient":
                {
                    var c = args["color"] as JObject;
                    if (c == null) return Err("color {r,g,b,a} required.");
                    RenderSettings.ambientLight = new Color(
                        c.Value<float?>("r") ?? RenderSettings.ambientLight.r,
                        c.Value<float?>("g") ?? RenderSettings.ambientLight.g,
                        c.Value<float?>("b") ?? RenderSettings.ambientLight.b,
                        c.Value<float?>("a") ?? RenderSettings.ambientLight.a);
                    return Ok(new { action, ambientLight = ColorObj(RenderSettings.ambientLight) });
                }
                case "get_ambient":
                    return Ok(new
                    {
                        action,
                        ambientLight = ColorObj(RenderSettings.ambientLight),
                        ambientMode = RenderSettings.ambientMode.ToString(),
                        ambientIntensity = RenderSettings.ambientIntensity
                    });
                case "set_skybox":
                {
                    var path = args.Value<string>("material_path");
                    if (string.IsNullOrEmpty(path)) return Err("material_path required.");
                    var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (mat == null) return Err($"Material not found at {path}.");
                    RenderSettings.skybox = mat;
                    return Ok(new { action, material_path = path });
                }
                case "get_skybox":
                    return Ok(new
                    {
                        action,
                        skybox = RenderSettings.skybox == null ? null : AssetDatabase.GetAssetPath(RenderSettings.skybox)
                    });
                default:
                    return Err($"Unsupported unity_lighting action '{action}'.");
            }
        }

        private static object ColorObj(Color c) => new { c.r, c.g, c.b, c.a };

        private static AutonomousMcpToolResponse Ok(object d) =>
            new AutonomousMcpToolResponse { success = true, data = JToken.FromObject(d), error = string.Empty };
        private static AutonomousMcpToolResponse Err(string m) =>
            new AutonomousMcpToolResponse { success = false, data = null, error = m };
    }
}
