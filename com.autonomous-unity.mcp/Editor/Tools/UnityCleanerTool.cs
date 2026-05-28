using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AutonomousMcp.Editor.Core;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace AutonomousMcp.Editor.Tools
{
    /// <summary>
    /// unity_cleaner — find orphan assets, unused materials, empty folders, internal-error shaders.
    /// Read-only by default. Pass action=delete_orphans or action=delete_empty_folders with confirm=true to remove.
    /// </summary>
    public static class UnityCleanerTool
    {
        [InitializeOnLoadMethod]
        private static void Register()
        {
            ToolRegistry.Register("unity_cleaner", ToolMode.Mutate, ToolCategory.Diagnostic,
                "Find orphan assets, unused materials, empty folders, internal-error shaders. " +
                "Actions: find_orphans, find_unused_materials, find_empty_folders, find_internal_error_shaders, " +
                "delete_orphans (confirm=true), delete_empty_folders (confirm=true).",
                Handle);
        }

        private static AutonomousMcpToolResponse Handle(JObject args)
        {
            var action = args.Value<string>("action") ?? "find_orphans";
            switch (action)
            {
                case "find_orphans": return FindOrphans(args, false);
                case "find_unused_materials": return FindUnusedMaterials(args);
                case "find_empty_folders": return FindEmptyFolders(args, false);
                case "find_internal_error_shaders": return FindInternalErrorShaders(args);
                case "delete_orphans": return FindOrphans(args, true);
                case "delete_empty_folders": return FindEmptyFolders(args, true);
                default:
                    return Err($"Unsupported unity_cleaner action '{action}'.");
            }
        }

        private static AutonomousMcpToolResponse FindOrphans(JObject args, bool deleteMode)
        {
            // "Orphan" = asset not referenced by any other asset in Assets/, ignoring
            // scenes/prefabs/scripts (which can be entry points). Best-effort, not exhaustive.
            var folder = args.Value<string>("folder") ?? "Assets";
            var confirm = args.Value<bool?>("confirm") ?? false;

            var allAssets = AssetDatabase.FindAssets("", new[] { folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !string.IsNullOrEmpty(p) && !AssetDatabase.IsValidFolder(p))
                .ToList();

            // Build reverse reference set from scene/prefab/scriptableobject/material dependencies
            var referenced = new HashSet<string>(StringComparer.Ordinal);
            var entryExts = new HashSet<string>(new[] { ".unity", ".prefab", ".asset", ".mat", ".controller", ".overrideController" }, StringComparer.OrdinalIgnoreCase);
            foreach (var path in allAssets)
            {
                var ext = Path.GetExtension(path);
                if (!entryExts.Contains(ext)) continue;
                foreach (var dep in AssetDatabase.GetDependencies(path, true))
                {
                    referenced.Add(dep);
                }
            }

            var orphans = allAssets
                .Where(p => !referenced.Contains(p))
                .Where(p => !p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                .Where(p => !p.EndsWith(".asmdef", StringComparison.OrdinalIgnoreCase))
                .Where(p => !p.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (deleteMode && confirm)
            {
                var deleted = new List<string>();
                foreach (var p in orphans)
                {
                    if (AssetDatabase.DeleteAsset(p)) deleted.Add(p);
                }
                AssetDatabase.Refresh();
                return Ok(new { action = "delete_orphans", deletedCount = deleted.Count, deleted });
            }

            return Ok(new
            {
                action = deleteMode ? "delete_orphans" : "find_orphans",
                requiresConfirmation = deleteMode && !confirm,
                count = orphans.Count,
                orphans
            });
        }

        private static AutonomousMcpToolResponse FindUnusedMaterials(JObject args)
        {
            var folder = args.Value<string>("folder") ?? "Assets";
            var materialGuids = AssetDatabase.FindAssets("t:Material", new[] { folder });
            var allMaterials = materialGuids.Select(AssetDatabase.GUIDToAssetPath).ToHashSet();

            var consumers = AssetDatabase.FindAssets("t:Prefab t:Scene", new[] { folder })
                .Select(AssetDatabase.GUIDToAssetPath);
            var referenced = new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in consumers)
            {
                foreach (var dep in AssetDatabase.GetDependencies(c, true))
                {
                    if (allMaterials.Contains(dep)) referenced.Add(dep);
                }
            }

            var unused = allMaterials.Where(m => !referenced.Contains(m)).ToList();
            return Ok(new { action = "find_unused_materials", count = unused.Count, unused });
        }

        private static AutonomousMcpToolResponse FindEmptyFolders(JObject args, bool deleteMode)
        {
            var root = args.Value<string>("folder") ?? "Assets";
            var confirm = args.Value<bool?>("confirm") ?? false;
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            var abs = Path.Combine(projectRoot ?? "", root.Replace('/', Path.DirectorySeparatorChar));

            if (!Directory.Exists(abs))
                return Err($"Folder not found: {root}");

            var empty = Directory.GetDirectories(abs, "*", SearchOption.AllDirectories)
                .Where(d => !Directory.EnumerateFileSystemEntries(d).Any(f => !f.EndsWith(".meta")))
                .Select(d => "Assets" + d.Substring(Application.dataPath.Length).Replace(Path.DirectorySeparatorChar, '/'))
                .ToList();

            if (deleteMode && confirm)
            {
                var deleted = new List<string>();
                foreach (var p in empty)
                {
                    if (AssetDatabase.DeleteAsset(p)) deleted.Add(p);
                }
                AssetDatabase.Refresh();
                return Ok(new { action = "delete_empty_folders", deletedCount = deleted.Count, deleted });
            }

            return Ok(new
            {
                action = deleteMode ? "delete_empty_folders" : "find_empty_folders",
                requiresConfirmation = deleteMode && !confirm,
                count = empty.Count,
                folders = empty
            });
        }

        private static AutonomousMcpToolResponse FindInternalErrorShaders(JObject args)
        {
            var folder = args.Value<string>("folder") ?? "Assets";
            var materialGuids = AssetDatabase.FindAssets("t:Material", new[] { folder });
            var hits = new List<object>();
            foreach (var g in materialGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null) continue;
                if (mat.shader == null || mat.shader.name == "Hidden/InternalErrorShader")
                {
                    hits.Add(new { path, shaderName = mat.shader?.name ?? "<null>" });
                }
            }
            return Ok(new { action = "find_internal_error_shaders", count = hits.Count, materials = hits });
        }

        private static AutonomousMcpToolResponse Ok(object d) =>
            new AutonomousMcpToolResponse { success = true, data = JToken.FromObject(d), error = string.Empty };
        private static AutonomousMcpToolResponse Err(string m) =>
            new AutonomousMcpToolResponse { success = false, data = null, error = m };
    }
}
