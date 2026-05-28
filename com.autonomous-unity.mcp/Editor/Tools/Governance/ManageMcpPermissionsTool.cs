using AutonomousMcp.Editor.Core;
using Newtonsoft.Json.Linq;

namespace AutonomousMcp.Editor.Tools.Governance
{
    [McpTool("manage_mcp_permissions",
        "Manage permission auto-approve flags and global per-tool overrides.",
        Mode = ToolMode.Mutate, Category = ToolCategory.Diagnostic)]
    public sealed class ManageMcpPermissionsTool : IMcpTool
    {
        public string Name => "manage_mcp_permissions";
        public string Description => "Manage permission auto-approve flags and global per-tool overrides.";
        public ToolMode Mode => ToolMode.Mutate;
        public ToolCategory Category => ToolCategory.Diagnostic;

        public AutonomousMcpToolResponse Execute(JObject args)
        {
            var action = args.Value<string>("action") ?? "get";
            switch (action)
            {
                case "get":
                    return Ok(BuildSnapshot());
                case "set_auto_approve_mutate":
                    PermissionStore.AutoApproveMutate = args.Value<bool?>("value") ?? false;
                    return Ok(BuildSnapshot());
                case "set_auto_approve_destructive":
                    PermissionStore.AutoApproveDestructive = args.Value<bool?>("value") ?? false;
                    return Ok(BuildSnapshot());
                case "set_auto_approve_new_clients":
                    PermissionStore.AutoApproveNewClients = args.Value<bool?>("value") ?? false;
                    return Ok(BuildSnapshot());
                case "set_global_tool_override":
                {
                    var tool = args.Value<string>("tool");
                    var value = args.Value<string>("value");
                    if (string.IsNullOrEmpty(tool)) return Err("manage_mcp_permissions.set_global_tool_override requires 'tool'.");
                    PermissionStore.SetGlobalToolOverride(tool, value);
                    return Ok(BuildSnapshot());
                }
                default:
                    return Err($"Unknown manage_mcp_permissions action '{action}'.");
            }
        }

        private static JToken BuildSnapshot()
        {
            var overrides = new JObject();
            foreach (var kv in PermissionStore.GetGlobalToolOverrides()) overrides[kv.Key] = kv.Value;
            return new JObject
            {
                ["mode"] = PermissionStore.Mode.ToString(),
                ["autoApproveMutate"] = PermissionStore.AutoApproveMutate,
                ["autoApproveDestructive"] = PermissionStore.AutoApproveDestructive,
                ["autoApproveNewClients"] = PermissionStore.AutoApproveNewClients,
                ["globalToolOverrides"] = overrides
            };
        }

        private static AutonomousMcpToolResponse Ok(JToken data) =>
            new AutonomousMcpToolResponse { success = true, data = data, error = string.Empty };

        private static AutonomousMcpToolResponse Err(string message) =>
            new AutonomousMcpToolResponse { success = false, data = null, error = message };
    }
}
