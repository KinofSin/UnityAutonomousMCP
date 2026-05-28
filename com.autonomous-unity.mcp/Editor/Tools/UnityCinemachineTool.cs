using System;
using System.Linq;
using AutonomousMcp.Editor.Core;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace AutonomousMcp.Editor.Tools
{
    /// <summary>
    /// unity_cinemachine — reflection-based so it compiles even when the package isn't installed.
    /// When Cinemachine is missing, every action returns a clear "not installed" error.
    /// </summary>
    public static class UnityCinemachineTool
    {
        private static Type _cmBrainType;
        private static Type _cmVCamType;
        private static bool _scanned;

        [InitializeOnLoadMethod]
        private static void Register()
        {
            ToolRegistry.Register("unity_cinemachine", ToolMode.Mutate, ToolCategory.Cinemachine,
                "Cinemachine helpers. Actions: detect, list_vcams, create_vcam, set_priority. " +
                "Returns 'not installed' when Cinemachine isn't in the project.",
                Handle);
        }

        private static void EnsureTypes()
        {
            if (_scanned) return;
            _scanned = true;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (_cmBrainType != null && _cmVCamType != null) break;
                try
                {
                    foreach (var t in asm.GetTypes())
                    {
                        if (_cmBrainType == null && t.FullName == "Cinemachine.CinemachineBrain") _cmBrainType = t;
                        if (_cmVCamType == null && t.FullName == "Cinemachine.CinemachineVirtualCamera") _cmVCamType = t;
                    }
                }
                catch { /* ignore reflection failures */ }
            }
        }

        private static AutonomousMcpToolResponse Handle(JObject args)
        {
            EnsureTypes();
            var action = args.Value<string>("action") ?? "detect";

            if (action == "detect")
            {
                return Ok(new
                {
                    action,
                    installed = _cmBrainType != null && _cmVCamType != null,
                    brainTypeFound = _cmBrainType != null,
                    vcamTypeFound = _cmVCamType != null
                });
            }

            if (_cmVCamType == null) return Err("Cinemachine package not detected.");

            switch (action)
            {
                case "list_vcams":
                {
                    var found = Resources.FindObjectsOfTypeAll(_cmVCamType)
                        .OfType<Component>()
                        .Where(c => c.gameObject.scene.IsValid())
                        .Select(c => new
                        {
                            name = c.name,
                            instanceId = c.GetInstanceID(),
                            priority = (int)(c.GetType().GetProperty("Priority")?.GetValue(c) ?? 0)
                        }).ToList();
                    return Ok(new { action, count = found.Count, vcams = found });
                }
                case "create_vcam":
                {
                    var name = args.Value<string>("name") ?? "VCam";
                    var go = new GameObject(name);
                    var vcam = go.AddComponent(_cmVCamType);
                    Selection.activeGameObject = go;
                    return Ok(new { action, instanceId = go.GetInstanceID(), name });
                }
                case "set_priority":
                {
                    var name = args.Value<string>("name");
                    var priority = args.Value<int?>("priority") ?? 10;
                    if (string.IsNullOrEmpty(name)) return Err("name required.");
                    var go = GameObject.Find(name);
                    if (go == null) return Err($"GameObject '{name}' not found.");
                    var vcam = go.GetComponent(_cmVCamType);
                    if (vcam == null) return Err($"'{name}' has no CinemachineVirtualCamera.");
                    var prop = _cmVCamType.GetProperty("Priority");
                    prop?.SetValue(vcam, priority);
                    EditorUtility.SetDirty(vcam);
                    return Ok(new { action, name, priority });
                }
                default:
                    return Err($"Unsupported unity_cinemachine action '{action}'.");
            }
        }

        private static AutonomousMcpToolResponse Ok(object d) =>
            new AutonomousMcpToolResponse { success = true, data = JToken.FromObject(d), error = string.Empty };
        private static AutonomousMcpToolResponse Err(string m) =>
            new AutonomousMcpToolResponse { success = false, data = null, error = m };
    }
}
