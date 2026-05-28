using System.Collections.Generic;
using System.Linq;
using AutonomousMcp.Editor.Core;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.AI;
using UnityEngine;
using UnityEngine.AI;

namespace AutonomousMcp.Editor.Tools
{
    /// <summary>
    /// unity_navmesh — bake/clear/info using the legacy Navigation system.
    /// </summary>
    public static class UnityNavMeshTool
    {
        [InitializeOnLoadMethod]
        private static void Register()
        {
            ToolRegistry.Register("unity_navmesh", ToolMode.Mutate, ToolCategory.Navigation,
                "NavMesh actions: bake, clear, info, list_agent_types.",
                Handle);
        }

        private static AutonomousMcpToolResponse Handle(JObject args)
        {
            var action = args.Value<string>("action") ?? "info";
            switch (action)
            {
                case "bake":
                    NavMeshBuilder.BuildNavMesh();
                    return Ok(new { action, baked = true });
                case "clear":
                    NavMeshBuilder.ClearAllNavMeshes();
                    return Ok(new { action, cleared = true });
                case "info":
                {
                    var triangulation = NavMesh.CalculateTriangulation();
                    return Ok(new
                    {
                        action,
                        vertices = triangulation.vertices.Length,
                        indices = triangulation.indices.Length,
                        areas = triangulation.areas.Length
                    });
                }
                case "list_agent_types":
                {
                    var types = new List<object>();
                    for (int i = 0; i < NavMesh.GetSettingsCount(); i++)
                    {
                        var s = NavMesh.GetSettingsByIndex(i);
                        types.Add(new
                        {
                            id = s.agentTypeID,
                            name = NavMesh.GetSettingsNameFromID(s.agentTypeID),
                            s.agentRadius,
                            s.agentHeight,
                            s.agentSlope,
                            s.agentClimb
                        });
                    }
                    return Ok(new { action, count = types.Count, agents = types });
                }
                default:
                    return Err($"Unsupported unity_navmesh action '{action}'.");
            }
        }

        private static AutonomousMcpToolResponse Ok(object d) =>
            new AutonomousMcpToolResponse { success = true, data = JToken.FromObject(d), error = string.Empty };
        private static AutonomousMcpToolResponse Err(string m) =>
            new AutonomousMcpToolResponse { success = false, data = null, error = m };
    }
}
