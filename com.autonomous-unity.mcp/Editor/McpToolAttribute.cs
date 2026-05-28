using System;
using AutonomousMcp.Editor.Core;

namespace AutonomousMcp
{
    /// <summary>
    /// Tag a static method OR a class to register it as an MCP tool.
    ///
    /// Static method form (backwards compatible):
    /// <code>
    /// [McpTool("optimize_textures", "Downsize textures to 1024 with crunch")]
    /// public static JToken OptimizeTextures(JObject args) { ... }
    /// </code>
    /// Discovered by list_custom_tools, executed via execute_custom_tool.
    ///
    /// Class form (Phase 0+):
    /// <code>
    /// [McpTool("my_tool", "Description", Mode = ToolMode.Mutate, Category = ToolCategory.Asset)]
    /// public sealed class MyTool : IMcpTool { ... }
    /// </code>
    /// Auto-registered by ToolRegistry on editor load.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class McpToolAttribute : Attribute
    {
        public string Name { get; }
        public string Description { get; }

        /// <summary>Mutation level. Defaults to Mutate for safety on legacy custom tools.</summary>
        public ToolMode Mode { get; set; } = ToolMode.Mutate;

        /// <summary>Discovery category. Defaults to Custom.</summary>
        public ToolCategory Category { get; set; } = ToolCategory.Custom;

        public McpToolAttribute(string name, string description = "")
        {
            Name = name;
            Description = description;
        }
    }
}
