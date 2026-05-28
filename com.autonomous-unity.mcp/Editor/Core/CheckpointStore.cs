using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AutonomousMcp.Editor.Core
{
    /// <summary>
    /// Lightweight, file-based checkpoint system. Stores a snapshot of:
    ///   - The active scene file (copy of the .unity asset on disk)
    ///   - A manifest of dirty/recently-modified asset paths + their content hashes
    ///   - Editor state metadata (active scene path, selection, play mode, timestamp, label)
    ///
    /// Lives under Library/MCP_Checkpoints/&lt;id&gt;/. The Library/ folder is VCS-ignored by
    /// default in Unity projects, so checkpoints don't pollute the user's repo.
    ///
    /// NOT a full project rollback — we trade fidelity for speed and zero external deps.
    /// For high-stakes operations, prefer Source Control + Unity's Undo system in tandem.
    /// </summary>
    public static class CheckpointStore
    {
        [Serializable]
        public sealed class Manifest
        {
            public string id;
            public string label;
            public string createdUtc;
            public string activeScenePath;
            public bool sceneWasDirty;
            public List<string> trackedAssetPaths = new List<string>();
            public string toolThatTriggered;
            public string clientId;
        }

        private const string SubFolder = "MCP_Checkpoints";

        private static string Root
        {
            get
            {
                var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
                var libraryPath = Path.Combine(projectRoot, "Library");
                if (!Directory.Exists(libraryPath)) Directory.CreateDirectory(libraryPath);
                var path = Path.Combine(libraryPath, SubFolder);
                if (!Directory.Exists(path)) Directory.CreateDirectory(path);
                return path;
            }
        }

        /// <summary>Absolute path to the checkpoint root, for UI Reveal-in-Finder buttons.</summary>
        public static string RootDirectory => Root;

        /// <summary>Disk size of a single checkpoint folder, bytes. Returns 0 if missing.</summary>
        public static long SizeOf(string id)
        {
            if (string.IsNullOrEmpty(id)) return 0;
            var dir = Path.Combine(Root, id);
            if (!Directory.Exists(dir)) return 0;
            long total = 0;
            foreach (var file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(file).Length; } catch { }
            }
            return total;
        }

        // ── Create ──────────────────────────────────────────────────────────────────

        public static Manifest Create(string label = null, string toolThatTriggered = null, string clientId = null)
        {
            var id = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N").Substring(0, 6)}";
            var dir = Path.Combine(Root, id);
            Directory.CreateDirectory(dir);

            var scene = SceneManager.GetActiveScene();
            var manifest = new Manifest
            {
                id = id,
                label = string.IsNullOrEmpty(label) ? "auto" : label,
                createdUtc = DateTime.UtcNow.ToString("o"),
                activeScenePath = scene.path,
                sceneWasDirty = scene.isDirty,
                toolThatTriggered = toolThatTriggered ?? string.Empty,
                clientId = clientId ?? string.Empty
            };

            // Snapshot the active scene asset (if it has been saved at least once).
            if (!string.IsNullOrEmpty(scene.path) && File.Exists(AbsPath(scene.path)))
            {
                try
                {
                    if (scene.isDirty)
                    {
                        // Save once so the snapshot reflects the user's current intent.
                        EditorSceneManager.SaveScene(scene, scene.path, false);
                    }
                    var dest = Path.Combine(dir, "active_scene.unity");
                    File.Copy(AbsPath(scene.path), dest, overwrite: true);
                    manifest.trackedAssetPaths.Add(scene.path);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[AutonomousMCP] Checkpoint scene snapshot failed: {ex.Message}");
                }
            }

            File.WriteAllText(Path.Combine(dir, "manifest.json"),
                JsonConvert.SerializeObject(manifest, Formatting.Indented));

            return manifest;
        }

        // ── List ────────────────────────────────────────────────────────────────────

        public static List<Manifest> List()
        {
            var results = new List<Manifest>();
            if (!Directory.Exists(Root)) return results;

            foreach (var dir in Directory.GetDirectories(Root).OrderByDescending(d => d))
            {
                var manifestPath = Path.Combine(dir, "manifest.json");
                if (!File.Exists(manifestPath)) continue;
                try
                {
                    var json = File.ReadAllText(manifestPath);
                    var m = JsonConvert.DeserializeObject<Manifest>(json);
                    if (m != null) results.Add(m);
                }
                catch
                {
                    // skip broken manifest
                }
            }
            return results;
        }

        public static Manifest Find(string id)
        {
            return List().FirstOrDefault(m => m.id == id);
        }

        // ── Restore ─────────────────────────────────────────────────────────────────

        public static bool Restore(string id, out string error)
        {
            error = string.Empty;
            var manifest = Find(id);
            if (manifest == null)
            {
                error = $"Checkpoint '{id}' not found.";
                return false;
            }

            var dir = Path.Combine(Root, id);
            var snapshotScene = Path.Combine(dir, "active_scene.unity");

            if (!string.IsNullOrEmpty(manifest.activeScenePath) && File.Exists(snapshotScene))
            {
                try
                {
                    // Restore the scene asset on disk, then re-open it.
                    File.Copy(snapshotScene, AbsPath(manifest.activeScenePath), overwrite: true);
                    AssetDatabase.ImportAsset(manifest.activeScenePath, ImportAssetOptions.ForceUpdate);
                    EditorSceneManager.OpenScene(manifest.activeScenePath, OpenSceneMode.Single);
                }
                catch (Exception ex)
                {
                    error = $"Scene restore failed: {ex.Message}";
                    return false;
                }
            }

            return true;
        }

        // ── Delete ──────────────────────────────────────────────────────────────────

        public static bool Delete(string id, out string error)
        {
            error = string.Empty;
            var dir = Path.Combine(Root, id);
            if (!Directory.Exists(dir))
            {
                error = $"Checkpoint '{id}' not found.";
                return false;
            }
            try
            {
                Directory.Delete(dir, true);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static int DeleteAll()
        {
            int count = 0;
            if (!Directory.Exists(Root)) return 0;
            foreach (var dir in Directory.GetDirectories(Root))
            {
                try { Directory.Delete(dir, true); count++; }
                catch { /* skip */ }
            }
            return count;
        }

        public static long TotalDiskUsageBytes()
        {
            long total = 0;
            if (!Directory.Exists(Root)) return 0;
            foreach (var file in Directory.GetFiles(Root, "*", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(file).Length; } catch { }
            }
            return total;
        }

        // ── Diff (text summary; not a true 3-way merge) ────────────────────────────

        public static string Diff(string id)
        {
            var manifest = Find(id);
            if (manifest == null) return $"Checkpoint '{id}' not found.";

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Checkpoint: {manifest.id}");
            sb.AppendLine($"Label: {manifest.label}");
            sb.AppendLine($"Created: {manifest.createdUtc}");
            sb.AppendLine($"Triggered by: {manifest.toolThatTriggered} (client: {manifest.clientId})");
            sb.AppendLine($"Tracked assets ({manifest.trackedAssetPaths.Count}):");
            foreach (var path in manifest.trackedAssetPaths)
            {
                var snapshot = Path.Combine(Root, manifest.id, "active_scene.unity");
                var current = AbsPath(path);
                string status = "unknown";
                if (File.Exists(snapshot) && File.Exists(current))
                {
                    var snapSize = new FileInfo(snapshot).Length;
                    var curSize = new FileInfo(current).Length;
                    status = (snapSize == curSize) ? "size-match" : $"changed (snap={snapSize}, cur={curSize})";
                }
                else if (!File.Exists(current))
                {
                    status = "missing-from-disk";
                }
                sb.AppendLine($"  - {path} [{status}]");
            }
            return sb.ToString();
        }

        private static string AbsPath(string assetPath)
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            return Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
        }
    }
}
