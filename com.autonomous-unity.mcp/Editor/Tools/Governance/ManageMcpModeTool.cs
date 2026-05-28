using AutonomousMcp.Editor.Core;
using Newtonsoft.Json.Linq;

namespace AutonomousMcp.Editor.Tools.Governance
{
    [McpTool("manage_mcp_mode",
        "Get or set the server-wide operating mode (Ask = read-only, Agent = full).",
        Mode = ToolMode.Mutate, Category = ToolCategory.Diagnostic)]
    public sealed class ManageMcpModeTool : IMcpTool
    {
        public string Name => "manage_mcp_mode";
        public string Description => "Get or set the server-wide operating mode (Ask = read-only, Agent = full).";
        public ToolMode Mode => ToolMode.Mutate;
        public ToolCategory Category => ToolCategory.Diagnostic;

        public AutonomousMcpToolResponse Execute(JObject args)
        {
            var action = args.Value<string>("action") ?? "get";
            switch (action)
            {
                case "get":
                    return Ok(new JObject { ["mode"] = PermissionStore.Mode.ToString() });
                case "set_ask":
                    PermissionStore.SetMode(AutonomousMcpMode.Ask);
                    return Ok(new JObject { ["mode"] = AutonomousMcpMode.Ask.ToString() });
                case "set_agent":
                    PermissionStore.SetMode(AutonomousMcpMode.Agent);
                    return Ok(new JObject { ["mode"] = AutonomousMcpMode.Agent.ToString() });
                default:
                    return Err($"Unknown manage_mcp_mode action '{action}'. Use get, set_ask, set_agent.");
            }
        }

        private static AutonomousMcpToolResponse Ok(JToken data) =>
            new AutonomousMcpToolResponse { success = true, data = data, error = string.Empty };

        private static AutonomousMcpToolResponse Err(string message) =>
            new AutonomousMcpToolResponse { success = false, data = null, error = message };
    }
}
