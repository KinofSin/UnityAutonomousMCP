using Newtonsoft.Json.Linq;

namespace AutonomousMcp.Editor.Core
{
    public interface IMcpTool
    {
        string Name { get; }
        string Description { get; }
        ToolMode Mode { get; }
        ToolCategory Category { get; }
        AutonomousMcpToolResponse Execute(JObject args);
    }
}
