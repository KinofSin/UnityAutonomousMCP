using System;

namespace AutonomousMcp
{
    /// <summary>
    /// Tag a static method to register it as a custom MCP tool.
    /// The method must have signature: static JToken/JObject/string MethodName(JObject args)
    /// 
    /// Example:
    /// <code>
    /// [McpTool("optimize_textures", "Downsize all avatar textures to 1024 with crunch")]
    /// public static JToken OptimizeTextures(JObject args)
    /// {
    ///     // your logic here
    ///     return JToken.FromObject(new { optimized = 12 });
    /// }
    /// </code>
    /// 
    /// Discovered automatically by list_custom_tools. Execute via execute_custom_tool.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public class McpToolAttribute : Attribute
    {
        public string Name { get; }
        public string Description { get; }

        public McpToolAttribute(string name, string description = "")
        {
            Name = name;
            Description = description;
        }
    }
}
