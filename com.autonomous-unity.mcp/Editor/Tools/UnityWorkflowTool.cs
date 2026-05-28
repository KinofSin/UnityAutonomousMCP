using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AutonomousMcp.Editor.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace AutonomousMcp.Editor.Tools
{
    /// <summary>
    /// unity_workflow — record & replay a sequence of MCP tool calls as a named workflow.
    /// Storage: Library/MCP_Workflows/&lt;name&gt;.json.
    /// </summary>
    public static class UnityWorkflowTool
    {
        [Serializable]
        public sealed class WorkflowStep
        {
            public string tool;
            public JObject @params;
            public string note;
        }

        [Serializable]
        public sealed class Workflow
        {
            public string name;
            public string description;
            public string createdUtc;
            public List<WorkflowStep> steps = new List<WorkflowStep>();
        }

        private const string SubFolder = "MCP_Workflows";

        private static string Root
        {
            get
            {
                var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
                var path = Path.Combine(projectRoot, "Library", SubFolder);
                if (!Directory.Exists(path)) Directory.CreateDirectory(path);
                return path;
            }
        }

        [InitializeOnLoadMethod]
        private static void Register()
        {
            ToolRegistry.Register("unity_workflow", ToolMode.Mutate, ToolCategory.Workflow,
                "Workflow recordings: save, load, list, delete, replay, append_step.",
                Handle);
        }

        private static AutonomousMcpToolResponse Handle(JObject args)
        {
            var action = args.Value<string>("action") ?? "list";
            switch (action)
            {
                case "list": return List();
                case "save": return Save(args);
                case "load": return Load(args);
                case "delete": return Delete(args);
                case "append_step": return AppendStep(args);
                case "replay": return Replay(args);
                default: return Err($"Unsupported unity_workflow action '{action}'.");
            }
        }

        private static string PathFor(string name) => Path.Combine(Root, name + ".json");

        private static AutonomousMcpToolResponse List()
        {
            var list = Directory.GetFiles(Root, "*.json").Select(f => new
            {
                name = Path.GetFileNameWithoutExtension(f),
                bytes = new FileInfo(f).Length,
                modified = File.GetLastWriteTimeUtc(f).ToString("o")
            }).ToList();
            return Ok(new { action = "list", count = list.Count, workflows = list });
        }

        private static AutonomousMcpToolResponse Save(JObject args)
        {
            var name = args.Value<string>("name");
            if (string.IsNullOrEmpty(name)) return Err("name required.");
            var stepsToken = args["steps"] as JArray;
            if (stepsToken == null) return Err("steps array required.");

            var wf = new Workflow
            {
                name = name,
                description = args.Value<string>("description") ?? string.Empty,
                createdUtc = DateTime.UtcNow.ToString("o"),
                steps = stepsToken.Select(s => new WorkflowStep
                {
                    tool = s.Value<string>("tool"),
                    @params = s["params"] as JObject ?? new JObject(),
                    note = s.Value<string>("note")
                }).ToList()
            };
            File.WriteAllText(PathFor(name), JsonConvert.SerializeObject(wf, Formatting.Indented));
            return Ok(new { action = "save", name, stepCount = wf.steps.Count });
        }

        private static AutonomousMcpToolResponse Load(JObject args)
        {
            var name = args.Value<string>("name");
            if (string.IsNullOrEmpty(name)) return Err("name required.");
            var path = PathFor(name);
            if (!File.Exists(path)) return Err($"Workflow '{name}' not found.");
            var wf = JsonConvert.DeserializeObject<Workflow>(File.ReadAllText(path));
            return Ok(new { action = "load", workflow = wf });
        }

        private static AutonomousMcpToolResponse Delete(JObject args)
        {
            var name = args.Value<string>("name");
            if (string.IsNullOrEmpty(name)) return Err("name required.");
            var path = PathFor(name);
            if (!File.Exists(path)) return Err($"Workflow '{name}' not found.");
            File.Delete(path);
            return Ok(new { action = "delete", name });
        }

        private static AutonomousMcpToolResponse AppendStep(JObject args)
        {
            var name = args.Value<string>("name");
            if (string.IsNullOrEmpty(name)) return Err("name required.");
            var path = PathFor(name);
            Workflow wf;
            if (File.Exists(path))
            {
                wf = JsonConvert.DeserializeObject<Workflow>(File.ReadAllText(path));
            }
            else
            {
                wf = new Workflow { name = name, createdUtc = DateTime.UtcNow.ToString("o") };
            }

            wf.steps.Add(new WorkflowStep
            {
                tool = args.Value<string>("tool"),
                @params = args["params"] as JObject ?? new JObject(),
                note = args.Value<string>("note")
            });
            File.WriteAllText(path, JsonConvert.SerializeObject(wf, Formatting.Indented));
            return Ok(new { action = "append_step", name, totalSteps = wf.steps.Count });
        }

        private static AutonomousMcpToolResponse Replay(JObject args)
        {
            var name = args.Value<string>("name");
            if (string.IsNullOrEmpty(name)) return Err("name required.");
            var path = PathFor(name);
            if (!File.Exists(path)) return Err($"Workflow '{name}' not found.");
            var wf = JsonConvert.DeserializeObject<Workflow>(File.ReadAllText(path));

            var results = new List<object>();
            foreach (var s in wf.steps)
            {
                var envelope = new AutonomousMcpEnvelope
                {
                    requestId = $"wf-{name}-{results.Count}",
                    tool = s.tool,
                    @params = s.@params,
                    clientId = "workflow",
                    clientName = $"workflow:{name}",
                    transport = "workflow"
                };
                var response = AutonomousMcpToolDispatcher.Dispatch(envelope);
                results.Add(new { tool = s.tool, success = response.success, data = response.data, error = response.error });
                if (!response.success) break;
            }
            return Ok(new { action = "replay", name, ranSteps = results.Count, results });
        }

        private static AutonomousMcpToolResponse Ok(object d) =>
            new AutonomousMcpToolResponse { success = true, data = JToken.FromObject(d), error = string.Empty };
        private static AutonomousMcpToolResponse Err(string m) =>
            new AutonomousMcpToolResponse { success = false, data = null, error = m };
    }
}
