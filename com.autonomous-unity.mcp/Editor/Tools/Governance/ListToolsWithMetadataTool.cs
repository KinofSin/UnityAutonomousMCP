using System;
using AutonomousMcp.Editor.Core;
using Newtonsoft.Json.Linq;

namespace AutonomousMcp.Editor.Tools.Governance
{
    [McpTool("list_tools_with_metadata",
        "List every Unity-side tool with mode/category/description.",
        Mode = ToolMode.Read, Category = ToolCategory.Diagnostic)]
    public sealed class ListToolsWithMetadataTool : IMcpTool
    {
        public string Name => "list_tools_with_metadata";
        public string Description => "List every Unity-side tool with full metadata (mode, category, description, source).";
        public ToolMode Mode => ToolMode.Read;
        public ToolCategory Category => ToolCategory.Diagnostic;

        public AutonomousMcpToolResponse Execute(JObject args)
        {
            var filter = args.Value<string>("filter");
            var categoryFilter = args.Value<string>("category");

            var arr = new JArray();

            foreach (var entry in ToolRegistry.All())
            {
                if (!PassesFilter(entry.Name, entry.Category.ToString(), filter, categoryFilter)) continue;
                arr.Add(new JObject
                {
                    ["name"] = entry.Name,
                    ["description"] = entry.Description ?? string.Empty,
                    ["mode"] = entry.Mode.ToString(),
                    ["category"] = entry.Category.ToString(),
                    ["source"] = "registry"
                });
            }

            foreach (var legacy in AutonomousMcpToolDispatcher.LegacyToolNames)
            {
                if (!PassesFilter(legacy, "Editor", filter, categoryFilter)) continue;
                arr.Add(new JObject
                {
                    ["name"] = legacy,
                    ["description"] = string.Empty,
                    ["mode"] = ToolMode.Mutate.ToString(),
                    ["category"] = ToolCategory.Editor.ToString(),
                    ["source"] = "legacy_switch"
                });
            }

            return new AutonomousMcpToolResponse
            {
                success = true,
                data = new JObject { ["count"] = arr.Count, ["tools"] = arr },
                error = string.Empty
            };
        }

        private static bool PassesFilter(string name, string category, string filter, string categoryFilter)
        {
            if (!string.IsNullOrEmpty(filter) &&
                name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }
            if (!string.IsNullOrEmpty(categoryFilter) &&
                !string.Equals(category, categoryFilter, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            return true;
        }
    }
}
