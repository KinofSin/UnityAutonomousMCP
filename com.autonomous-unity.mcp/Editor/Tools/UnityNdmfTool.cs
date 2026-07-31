using AutonomousMcp.Editor.Core;
using AutonomousMcp.Editor.Perception;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace AutonomousMcp.Editor.Tools
{
    /// <summary>
    /// unity_ndmf — NDMF bake measurement (and later AAO-related helpers).
    /// Mutate because ManualProcessAvatar writes Assets/ZZZ_GeneratedAssets.
    /// </summary>
    public static class UnityNdmfTool
    {
        [InitializeOnLoadMethod]
        private static void Register()
        {
            ToolRegistry.Register("unity_ndmf", ToolMode.Mutate, ToolCategory.Diagnostic,
                "NDMF helpers. Actions: bake_cost (manual-bake a clone, AvatarCost diff, destroy clone).",
                Handle);
        }

        private static AutonomousMcpToolResponse Handle(JObject args)
        {
            var action = args.Value<string>("action") ?? "bake_cost";
            switch (action)
            {
                case "bake_cost":
                    return BakeCost(args);
                default:
                    return Err($"Unsupported unity_ndmf action '{action}'.");
            }
        }

        private static AutonomousMcpToolResponse BakeCost(JObject args)
        {
            var root = ResolveRoot(args, out var err);
            if (root == null) return Err(err);

            // Bake writes generated assets; capture a checkpoint so the side effect is recoverable.
            try
            {
                CheckpointStore.Create("ndmf-bake-cost", "unity_ndmf", "bridge");
            }
            catch
            {
                /* non-fatal — measurement still useful */
            }

            var result = NdmfBakeMeasure.Measure(root);
            var token = JToken.FromObject(result);
            if (token is JObject jo && jo.Value<bool?>("success") == false)
                return Err(jo.Value<string>("error") ?? "bake_cost failed", token);

            return new AutonomousMcpToolResponse
            {
                success = true,
                data = token,
                error = string.Empty
            };
        }

        private static GameObject ResolveRoot(JObject args, out string error)
        {
            error = null;
            var instanceId = args.Value<int?>("instanceId");
            if (instanceId.HasValue)
            {
                var byId = EditorUtility.InstanceIDToObject(instanceId.Value) as GameObject;
                if (byId != null) return byId;
                error = $"instanceId {instanceId.Value} is not a GameObject.";
                return null;
            }

            var name = args.Value<string>("target") ?? args.Value<string>("name");
            if (string.IsNullOrWhiteSpace(name))
            {
                error = "bake_cost requires instanceId or target/name.";
                return null;
            }

            // Prefer exact root match (inactive included).
            foreach (var go in Object.FindObjectsOfType<GameObject>(true))
            {
                if (go != null && go.name == name && go.transform.parent == null)
                    return go;
            }
            foreach (var go in Object.FindObjectsOfType<GameObject>(true))
            {
                if (go != null && go.name == name)
                    return go;
            }
            error = $"No GameObject named '{name}'.";
            return null;
        }

        private static AutonomousMcpToolResponse Err(string msg, JToken data = null) =>
            new AutonomousMcpToolResponse { success = false, error = msg, data = data };

        private static AutonomousMcpToolResponse Ok(object data) =>
            new AutonomousMcpToolResponse { success = true, data = JToken.FromObject(data), error = string.Empty };
    }
}
