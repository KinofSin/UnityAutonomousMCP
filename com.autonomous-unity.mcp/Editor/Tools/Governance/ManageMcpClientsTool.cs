using AutonomousMcp.Editor.Core;
using Newtonsoft.Json.Linq;

namespace AutonomousMcp.Editor.Tools.Governance
{
    [McpTool("manage_mcp_clients",
        "Inspect/approve/deny/revoke MCP clients. Actions: list, get, approve, deny, revoke, set_tool_override.",
        Mode = ToolMode.Mutate, Category = ToolCategory.Diagnostic)]
    public sealed class ManageMcpClientsTool : IMcpTool
    {
        public string Name => "manage_mcp_clients";
        public string Description => "Inspect/approve/deny/revoke MCP clients tracked by the Unity bridge.";
        public ToolMode Mode => ToolMode.Mutate;
        public ToolCategory Category => ToolCategory.Diagnostic;

        public AutonomousMcpToolResponse Execute(JObject args)
        {
            var action = args.Value<string>("action") ?? "list";
            var clientId = args.Value<string>("clientId");

            switch (action)
            {
                case "list":
                {
                    var arr = new JArray();
                    foreach (var record in PermissionStore.ListClients()) arr.Add(Serialize(record));
                    return Ok(new JObject { ["clients"] = arr });
                }
                case "get":
                {
                    if (string.IsNullOrEmpty(clientId)) return Err("manage_mcp_clients.get requires 'clientId'.");
                    var record = PermissionStore.GetClient(clientId);
                    if (record == null) return Err($"Client '{clientId}' not found.");
                    return Ok(Serialize(record));
                }
                case "approve":
                    return RequireAndApply(clientId, id => PermissionStore.ApproveClient(id), "approved");
                case "deny":
                    return RequireAndApply(clientId, id => PermissionStore.DenyClient(id), "denied");
                case "revoke":
                    return RequireAndApply(clientId, id => PermissionStore.RevokeClient(id), "revoked");
                case "set_tool_override":
                {
                    if (string.IsNullOrEmpty(clientId)) return Err("manage_mcp_clients.set_tool_override requires 'clientId'.");
                    var tool = args.Value<string>("tool");
                    var value = args.Value<string>("value");
                    if (string.IsNullOrEmpty(tool)) return Err("manage_mcp_clients.set_tool_override requires 'tool'.");
                    if (!PermissionStore.SetClientToolOverride(clientId, tool, value))
                    {
                        return Err($"Client '{clientId}' not found.");
                    }
                    return Ok(Serialize(PermissionStore.GetClient(clientId)));
                }
                default:
                    return Err($"Unknown manage_mcp_clients action '{action}'.");
            }
        }

        private static AutonomousMcpToolResponse RequireAndApply(string id, System.Func<string, bool> action, string verb)
        {
            if (string.IsNullOrEmpty(id)) return Err($"manage_mcp_clients.{verb} requires 'clientId'.");
            if (!action(id)) return Err($"Client '{id}' not found.");
            return Ok(Serialize(PermissionStore.GetClient(id)));
        }

        private static JToken Serialize(ClientRecord record)
        {
            if (record == null) return JValue.CreateNull();
            var overrides = new JObject();
            if (record.ToolOverrides != null)
            {
                foreach (var kv in record.ToolOverrides) overrides[kv.Key] = kv.Value;
            }
            return new JObject
            {
                ["id"] = record.Id,
                ["name"] = record.Name,
                ["transport"] = record.Transport,
                ["state"] = record.State,
                ["firstSeenUtc"] = record.FirstSeenUtc,
                ["lastSeenUtc"] = record.LastSeenUtc,
                ["toolOverrides"] = overrides
            };
        }

        private static AutonomousMcpToolResponse Ok(JToken data) =>
            new AutonomousMcpToolResponse { success = true, data = data, error = string.Empty };

        private static AutonomousMcpToolResponse Err(string message) =>
            new AutonomousMcpToolResponse { success = false, data = null, error = message };
    }
}
