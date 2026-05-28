using System.Collections.Generic;
using System.Linq;
using AutonomousMcp.Editor.Core;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace AutonomousMcp.Editor.Tools
{
    /// <summary>
    /// unity_build_manage — scripting defines, active build target, build pipeline launch.
    /// </summary>
    public static class UnityBuildTool
    {
        [InitializeOnLoadMethod]
        private static void Register()
        {
            ToolRegistry.Register("unity_build_manage", ToolMode.Mutate, ToolCategory.Build,
                "Build management. Actions: get_defines, set_defines, add_define, remove_define, " +
                "get_target, switch_target, list_targets, get_scenes.",
                Handle);
        }

        private static AutonomousMcpToolResponse Handle(JObject args)
        {
            var action = args.Value<string>("action") ?? "get_target";
            switch (action)
            {
                case "get_defines": return GetDefines();
                case "set_defines": return SetDefines(args);
                case "add_define": return AddDefine(args);
                case "remove_define": return RemoveDefine(args);
                case "get_target": return GetTarget();
                case "switch_target": return SwitchTarget(args);
                case "list_targets": return ListTargets();
                case "get_scenes": return GetScenes();
                default:
                    return Err($"Unsupported unity_build_manage action '{action}'.");
            }
        }

        private static NamedBuildTarget CurrentNamedTarget()
        {
            return NamedBuildTarget.FromBuildTargetGroup(
                BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget));
        }

        private static AutonomousMcpToolResponse GetDefines()
        {
            var target = CurrentNamedTarget();
            PlayerSettings.GetScriptingDefineSymbols(target, out string[] defines);
            return Ok(new { action = "get_defines", target = target.TargetName, defines });
        }

        private static AutonomousMcpToolResponse SetDefines(JObject args)
        {
            var arr = args["defines"] as JArray;
            if (arr == null) return Err("defines array required.");
            var defines = arr.Select(j => j.Value<string>()).Where(s => !string.IsNullOrEmpty(s)).ToArray();
            var target = CurrentNamedTarget();
            PlayerSettings.SetScriptingDefineSymbols(target, defines);
            return Ok(new { action = "set_defines", target = target.TargetName, defines });
        }

        private static AutonomousMcpToolResponse AddDefine(JObject args)
        {
            var name = args.Value<string>("define");
            if (string.IsNullOrEmpty(name)) return Err("define required.");
            var target = CurrentNamedTarget();
            PlayerSettings.GetScriptingDefineSymbols(target, out string[] current);
            var set = new HashSet<string>(current);
            set.Add(name);
            PlayerSettings.SetScriptingDefineSymbols(target, set.ToArray());
            return Ok(new { action = "add_define", define = name, defines = set.ToArray() });
        }

        private static AutonomousMcpToolResponse RemoveDefine(JObject args)
        {
            var name = args.Value<string>("define");
            if (string.IsNullOrEmpty(name)) return Err("define required.");
            var target = CurrentNamedTarget();
            PlayerSettings.GetScriptingDefineSymbols(target, out string[] current);
            var arr = current.Where(d => d != name).ToArray();
            PlayerSettings.SetScriptingDefineSymbols(target, arr);
            return Ok(new { action = "remove_define", define = name, defines = arr });
        }

        private static AutonomousMcpToolResponse GetTarget()
        {
            return Ok(new
            {
                action = "get_target",
                target = EditorUserBuildSettings.activeBuildTarget.ToString(),
                group = BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget).ToString()
            });
        }

        private static AutonomousMcpToolResponse SwitchTarget(JObject args)
        {
            var name = args.Value<string>("target");
            if (string.IsNullOrEmpty(name)) return Err("target required (e.g. 'StandaloneWindows64', 'Android').");
            if (!System.Enum.TryParse(name, out BuildTarget target))
                return Err($"Unknown BuildTarget '{name}'.");
            var group = BuildPipeline.GetBuildTargetGroup(target);
            EditorUserBuildSettings.SwitchActiveBuildTargetAsync(group, target);
            return Ok(new { action = "switch_target", target = name, queued = true });
        }

        private static AutonomousMcpToolResponse ListTargets()
        {
            var values = System.Enum.GetValues(typeof(BuildTarget))
                .Cast<BuildTarget>()
                .Where(t => BuildPipeline.IsBuildTargetSupported(BuildPipeline.GetBuildTargetGroup(t), t))
                .Select(t => t.ToString())
                .ToList();
            return Ok(new { action = "list_targets", count = values.Count, supported = values });
        }

        private static AutonomousMcpToolResponse GetScenes()
        {
            var scenes = EditorBuildSettings.scenes
                .Select(s => new { s.path, s.enabled, s.guid })
                .ToList();
            return Ok(new { action = "get_scenes", count = scenes.Count, scenes });
        }

        private static AutonomousMcpToolResponse Ok(object d) =>
            new AutonomousMcpToolResponse { success = true, data = JToken.FromObject(d), error = string.Empty };
        private static AutonomousMcpToolResponse Err(string m) =>
            new AutonomousMcpToolResponse { success = false, data = null, error = m };
    }
}
