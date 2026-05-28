using System.Linq;
using AutonomousMcp.Editor.Core;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace AutonomousMcp.Editor.Tools
{
    /// <summary>
    /// Phase 2: manage_checkpoint MCP tool surface.
    /// Actions: create, list, get, restore, diff, delete, delete_all, disk_usage.
    /// </summary>
    public static class CheckpointTool
    {
        [InitializeOnLoadMethod]
        private static void Register()
        {
            ToolRegistry.Register("manage_checkpoint", ToolMode.Mutate, ToolCategory.Checkpoint,
                "Scene/asset checkpoints under Library/MCP_Checkpoints. " +
                "Actions: create, list, get, restore, diff, delete, delete_all, disk_usage.",
                Handle);
        }

        private static AutonomousMcpToolResponse Handle(JObject args)
        {
            var action = args.Value<string>("action") ?? "list";
            switch (action)
            {
                case "create":
                {
                    var label = args.Value<string>("label");
                    var trigger = args.Value<string>("trigger");
                    var clientId = args.Value<string>("clientId");
                    var manifest = CheckpointStore.Create(label, trigger, clientId);
                    return Ok(new
                    {
                        action,
                        manifest.id,
                        manifest.label,
                        manifest.activeScenePath,
                        manifest.createdUtc,
                        trackedAssetCount = manifest.trackedAssetPaths.Count
                    });
                }
                case "list":
                {
                    var manifests = CheckpointStore.List().Select(m => new
                    {
                        m.id,
                        m.label,
                        m.createdUtc,
                        m.activeScenePath,
                        m.toolThatTriggered,
                        m.clientId,
                        trackedAssetCount = m.trackedAssetPaths.Count
                    }).ToList();
                    return Ok(new { action, count = manifests.Count, checkpoints = manifests });
                }
                case "get":
                {
                    var id = args.Value<string>("id");
                    var manifest = CheckpointStore.Find(id);
                    if (manifest == null) return Err($"Checkpoint '{id}' not found.");
                    return Ok(new { action, manifest });
                }
                case "restore":
                {
                    var id = args.Value<string>("id");
                    if (string.IsNullOrEmpty(id)) return Err("id required.");
                    if (!CheckpointStore.Restore(id, out var error))
                        return Err($"Restore failed: {error}");
                    return Ok(new { action, id, restored = true });
                }
                case "diff":
                {
                    var id = args.Value<string>("id");
                    if (string.IsNullOrEmpty(id)) return Err("id required.");
                    return Ok(new { action, id, diff = CheckpointStore.Diff(id) });
                }
                case "delete":
                {
                    var id = args.Value<string>("id");
                    if (string.IsNullOrEmpty(id)) return Err("id required.");
                    if (!CheckpointStore.Delete(id, out var error))
                        return Err($"Delete failed: {error}");
                    return Ok(new { action, id, deleted = true });
                }
                case "delete_all":
                {
                    var count = CheckpointStore.DeleteAll();
                    return Ok(new { action, deleted = count });
                }
                case "disk_usage":
                {
                    var bytes = CheckpointStore.TotalDiskUsageBytes();
                    return Ok(new
                    {
                        action,
                        bytes,
                        megabytes = bytes / 1024.0 / 1024.0
                    });
                }
                default:
                    return Err($"Unsupported manage_checkpoint action '{action}'.");
            }
        }

        private static AutonomousMcpToolResponse Ok(object data) =>
            new AutonomousMcpToolResponse { success = true, data = JToken.FromObject(data), error = string.Empty };

        private static AutonomousMcpToolResponse Err(string message) =>
            new AutonomousMcpToolResponse { success = false, data = null, error = message };
    }
}
