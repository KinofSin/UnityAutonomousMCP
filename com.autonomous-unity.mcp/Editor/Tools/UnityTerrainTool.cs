using System.Linq;
using AutonomousMcp.Editor.Core;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace AutonomousMcp.Editor.Tools
{
    /// <summary>
    /// unity_terrain — read terrain info + create blank terrains.
    /// </summary>
    public static class UnityTerrainTool
    {
        [InitializeOnLoadMethod]
        private static void Register()
        {
            ToolRegistry.Register("unity_terrain", ToolMode.Mutate, ToolCategory.Terrain,
                "Terrain actions: list, info, create, set_height, flatten.",
                Handle);
        }

        private static AutonomousMcpToolResponse Handle(JObject args)
        {
            var action = args.Value<string>("action") ?? "list";
            switch (action)
            {
                case "list":
                {
                    var terrains = Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None)
                        .Select(t => new
                        {
                            name = t.name,
                            instanceId = t.GetInstanceID(),
                            heightmapResolution = t.terrainData.heightmapResolution,
                            size = new { t.terrainData.size.x, t.terrainData.size.y, t.terrainData.size.z },
                            treeCount = t.terrainData.treeInstanceCount
                        }).ToList();
                    return Ok(new { action, count = terrains.Count, terrains });
                }
                case "info":
                {
                    var t = ResolveTerrain(args, out var err);
                    if (t == null) return Err(err);
                    return Ok(new
                    {
                        action,
                        name = t.name,
                        size = new { t.terrainData.size.x, t.terrainData.size.y, t.terrainData.size.z },
                        heightmapResolution = t.terrainData.heightmapResolution,
                        alphamapResolution = t.terrainData.alphamapResolution,
                        detailResolution = t.terrainData.detailResolution,
                        treeCount = t.terrainData.treeInstanceCount,
                        layerCount = t.terrainData.terrainLayers?.Length ?? 0
                    });
                }
                case "create":
                {
                    var size = args["size"] as JObject;
                    var tData = new TerrainData
                    {
                        heightmapResolution = args.Value<int?>("heightmap_resolution") ?? 513,
                        alphamapResolution = args.Value<int?>("alphamap_resolution") ?? 512,
                        size = new Vector3(
                            size?.Value<float?>("x") ?? 500f,
                            size?.Value<float?>("y") ?? 600f,
                            size?.Value<float?>("z") ?? 500f)
                    };
                    var path = args.Value<string>("asset_path") ?? "Assets/NewTerrain.asset";
                    AssetDatabase.CreateAsset(tData, path);
                    var go = Terrain.CreateTerrainGameObject(tData);
                    go.name = args.Value<string>("name") ?? "Terrain";
                    return Ok(new { action, name = go.name, instanceId = go.GetInstanceID(), asset_path = path });
                }
                case "flatten":
                {
                    var t = ResolveTerrain(args, out var err);
                    if (t == null) return Err(err);
                    var height = args.Value<float?>("height") ?? 0f;
                    var w = t.terrainData.heightmapResolution;
                    var heights = new float[w, w];
                    for (int i = 0; i < w; i++) for (int j = 0; j < w; j++) heights[i, j] = height;
                    t.terrainData.SetHeights(0, 0, heights);
                    return Ok(new { action, height });
                }
                default:
                    return Err($"Unsupported unity_terrain action '{action}'.");
            }
        }

        private static Terrain ResolveTerrain(JObject args, out string err)
        {
            err = string.Empty;
            var name = args.Value<string>("name");
            if (string.IsNullOrEmpty(name))
            {
                var first = Object.FindFirstObjectByType<Terrain>();
                if (first == null) { err = "No Terrain in scene."; return null; }
                return first;
            }
            var go = GameObject.Find(name);
            if (go == null) { err = $"GameObject '{name}' not found."; return null; }
            var t = go.GetComponent<Terrain>();
            if (t == null) { err = $"GameObject '{name}' has no Terrain."; return null; }
            return t;
        }

        private static AutonomousMcpToolResponse Ok(object d) =>
            new AutonomousMcpToolResponse { success = true, data = JToken.FromObject(d), error = string.Empty };
        private static AutonomousMcpToolResponse Err(string m) =>
            new AutonomousMcpToolResponse { success = false, data = null, error = m };
    }
}
