using System;
using System.Linq;
using AutonomousMcp.Editor.Core;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace AutonomousMcp.Editor.Tools
{
    /// <summary>
    /// unity_timeline — reflection-based so it doesn't break compilation when Timeline is absent.
    /// </summary>
    public static class UnityTimelineTool
    {
        private static Type _playableDirectorType;
        private static Type _timelineAssetType;
        private static bool _scanned;

        [InitializeOnLoadMethod]
        private static void Register()
        {
            ToolRegistry.Register("unity_timeline", ToolMode.Mutate, ToolCategory.Timeline,
                "Timeline helpers. Actions: detect, list_directors, create_director, bind_timeline_asset.",
                Handle);
        }

        private static void EnsureTypes()
        {
            if (_scanned) return;
            _scanned = true;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (_playableDirectorType != null && _timelineAssetType != null) break;
                try
                {
                    foreach (var t in asm.GetTypes())
                    {
                        if (_playableDirectorType == null && t.FullName == "UnityEngine.Playables.PlayableDirector")
                            _playableDirectorType = t;
                        if (_timelineAssetType == null && t.FullName == "UnityEngine.Timeline.TimelineAsset")
                            _timelineAssetType = t;
                    }
                }
                catch { }
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
                    installed = _playableDirectorType != null && _timelineAssetType != null,
                    directorTypeFound = _playableDirectorType != null,
                    assetTypeFound = _timelineAssetType != null
                });
            }

            if (_playableDirectorType == null) return Err("Timeline package not detected.");

            switch (action)
            {
                case "list_directors":
                {
                    var found = Resources.FindObjectsOfTypeAll(_playableDirectorType)
                        .OfType<Component>()
                        .Where(c => c.gameObject.scene.IsValid())
                        .Select(c => new
                        {
                            name = c.name,
                            instanceId = c.GetInstanceID(),
                            asset = (c.GetType().GetProperty("playableAsset")?.GetValue(c) as Object)?.name
                        }).ToList();
                    return Ok(new { action, count = found.Count, directors = found });
                }
                case "create_director":
                {
                    var name = args.Value<string>("name") ?? "Director";
                    var go = new GameObject(name);
                    var d = go.AddComponent(_playableDirectorType);
                    Selection.activeGameObject = go;
                    return Ok(new { action, instanceId = go.GetInstanceID(), name });
                }
                case "bind_timeline_asset":
                {
                    var name = args.Value<string>("name");
                    var path = args.Value<string>("asset_path");
                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(path))
                        return Err("name and asset_path required.");
                    var go = GameObject.Find(name);
                    if (go == null) return Err($"GameObject '{name}' not found.");
                    var dir = go.GetComponent(_playableDirectorType);
                    if (dir == null) return Err($"'{name}' has no PlayableDirector.");
                    var asset = AssetDatabase.LoadAssetAtPath(path, _timelineAssetType);
                    if (asset == null) return Err($"Timeline asset not found at {path}.");
                    _playableDirectorType.GetProperty("playableAsset")?.SetValue(dir, asset);
                    EditorUtility.SetDirty(dir);
                    return Ok(new { action, name, asset_path = path });
                }
                default:
                    return Err($"Unsupported unity_timeline action '{action}'.");
            }
        }

        private static AutonomousMcpToolResponse Ok(object d) =>
            new AutonomousMcpToolResponse { success = true, data = JToken.FromObject(d), error = string.Empty };
        private static AutonomousMcpToolResponse Err(string m) =>
            new AutonomousMcpToolResponse { success = false, data = null, error = m };
    }
}
