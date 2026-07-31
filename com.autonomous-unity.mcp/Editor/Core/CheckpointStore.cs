using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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
    ///   - Assets captured copy-on-first-touch by mutating tools, with their .meta siblings
    ///   - Editor state metadata (active scene path, selection, play mode, timestamp, label)
    ///
    /// Lives under Library/MCP_Checkpoints/&lt;id&gt;/. The Library/ folder is VCS-ignored by
    /// default in Unity projects, so checkpoints don't pollute the user's repo.
    ///
    /// Asset coverage is copy-on-first-touch: a mutating tool calls
    /// <see cref="CaptureAssets"/> BEFORE it writes. Only the first capture of a given path
    /// per checkpoint is kept, so the stored bytes are the state as of checkpoint creation
    /// (nothing can modify an asset without routing through a capture first).
    ///
    /// Importer settings live in the .meta sibling, which is why .meta is captured too —
    /// texture max size / crunch / compression are otherwise unrecoverable.
    ///
    /// Still NOT a full project rollback: assets never touched by a tool are not stored.
    /// For high-stakes operations, prefer Source Control + Unity's Undo system in tandem.
    /// </summary>
    public static class CheckpointStore
    {
        [Serializable]
        public sealed class CapturedAsset
        {
            public string assetPath;
            public string storedFile;   // relative to the checkpoint folder
            public string storedMeta;   // relative; empty when the asset had no .meta
            public string capturedUtc;
            public string capturedByTool;
        }

        [Serializable]
        public sealed class Manifest
        {
            public string id;
            public string label;
            public string createdUtc;
            public string activeScenePath;
            public bool sceneWasDirty;
            public List<string> trackedAssetPaths = new List<string>();
            public List<CapturedAsset> capturedAssets = new List<CapturedAsset>();
            public string toolThatTriggered;
            public string clientId;
        }

        private const string SubFolder = "MCP_Checkpoints";
        private const string SceneSnapshotFile = "active_scene.unity";
        private const string AssetsSubFolder = "assets";

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
                    SnapshotScene(scene, Path.Combine(dir, SceneSnapshotFile));
                    manifest.trackedAssetPaths.Add(scene.path);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[AutonomousMCP] Checkpoint scene snapshot failed: {ex.Message}");
                }
            }

            WriteManifest(manifest);
            return manifest;
        }

        // A dirty scene's in-memory state only reaches disk via SaveScene, and SaveScene
        // requires a path under Assets/. So: save a copy to a temp asset, move the bytes into
        // the checkpoint, delete the temp. saveAsCopy leaves the open scene dirty and keeps its
        // path, so the user's unsaved work is preserved either way.
        private static void SnapshotScene(Scene scene, string destAbsPath)
        {
            if (!scene.isDirty)
            {
                File.Copy(AbsPath(scene.path), destAbsPath, overwrite: true);
                return;
            }

            var tempAsset = $"Assets/__mcp_checkpoint_tmp_{Guid.NewGuid().ToString("N").Substring(0, 8)}.unity";
            try
            {
                if (EditorSceneManager.SaveScene(scene, tempAsset, true) && File.Exists(AbsPath(tempAsset)))
                {
                    File.Copy(AbsPath(tempAsset), destAbsPath, overwrite: true);
                    return;
                }
                throw new IOException("SaveScene(saveAsCopy) did not produce a file.");
            }
            catch (Exception ex)
            {
                // Fall back to the last-saved bytes rather than force-saving the user's scene.
                Debug.LogWarning(
                    $"[AutonomousMCP] Could not snapshot unsaved scene changes ({ex.Message}); " +
                    "storing the last-saved scene instead. Unsaved edits are NOT in this checkpoint.");
                File.Copy(AbsPath(scene.path), destAbsPath, overwrite: true);
            }
            finally
            {
                if (!string.IsNullOrEmpty(tempAsset) && File.Exists(AbsPath(tempAsset)))
                    AssetDatabase.DeleteAsset(tempAsset);
            }
        }

        // ── Capture (copy-on-first-touch, called BEFORE a tool writes) ──────────────

        /// <summary>
        /// Stores the current bytes of <paramref name="assetPaths"/> (and their .meta siblings)
        /// into the newest checkpoint so a later Restore can put them back. Only the first
        /// capture of a path per checkpoint is kept.
        ///
        /// If no checkpoint exists yet, one is created automatically — an autonomous tool must
        /// never be able to make an unrecoverable asset edit just because nobody checkpointed.
        /// Best-effort: capture failures are logged, never thrown, so they cannot block a tool.
        /// </summary>
        public static Manifest CaptureAssets(IEnumerable<string> assetPaths, string tool = null, string clientId = null)
        {
            if (assetPaths == null) return null;
            var paths = assetPaths
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Replace('\\', '/'))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (paths.Count == 0) return null;

            try
            {
                var manifest = List().FirstOrDefault()
                               ?? Create($"auto-before-{tool ?? "mutate"}", tool, clientId);
                var dir = Path.Combine(Root, manifest.id);
                var assetsDir = Path.Combine(dir, AssetsSubFolder);
                Directory.CreateDirectory(assetsDir);

                bool changed = false;
                foreach (var assetPath in paths)
                {
                    if (manifest.capturedAssets.Any(c =>
                            string.Equals(c.assetPath, assetPath, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue; // already captured at its checkpoint-time state
                    }

                    var abs = AbsPath(assetPath);
                    if (!File.Exists(abs)) continue;

                    var stored = Flatten(assetPath);
                    File.Copy(abs, Path.Combine(assetsDir, stored), overwrite: true);

                    var storedMeta = string.Empty;
                    var absMeta = abs + ".meta";
                    if (File.Exists(absMeta))
                    {
                        storedMeta = stored + ".meta";
                        File.Copy(absMeta, Path.Combine(assetsDir, storedMeta), overwrite: true);
                    }

                    manifest.capturedAssets.Add(new CapturedAsset
                    {
                        assetPath = assetPath,
                        storedFile = AssetsSubFolder + "/" + stored,
                        storedMeta = string.IsNullOrEmpty(storedMeta) ? string.Empty : AssetsSubFolder + "/" + storedMeta,
                        capturedUtc = DateTime.UtcNow.ToString("o"),
                        capturedByTool = tool ?? string.Empty
                    });
                    if (!manifest.trackedAssetPaths.Contains(assetPath))
                        manifest.trackedAssetPaths.Add(assetPath);
                    changed = true;
                }

                if (changed) WriteManifest(manifest);
                return manifest;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AutonomousMCP] Checkpoint asset capture failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>Convenience overload for the common single-asset case.</summary>
        public static Manifest CaptureAsset(string assetPath, string tool = null, string clientId = null) =>
            CaptureAssets(new[] { assetPath }, tool, clientId);

        private static void WriteManifest(Manifest manifest)
        {
            File.WriteAllText(Path.Combine(Root, manifest.id, "manifest.json"),
                JsonConvert.SerializeObject(manifest, Formatting.Indented));
        }

        private static string Flatten(string assetPath)
        {
            var flat = assetPath.Replace(":", "_").Replace('\\', '/').Replace("/", "__");
            if (flat.Length <= 150) return flat;

            // Windows caps a filename at 255 chars and deep avatar asset paths blow past it.
            // A failed File.Copy here would silently discard the only copy of the pre-edit state,
            // so fall back to a truncated name plus a digest of the full path.
            using (var md5 = MD5.Create())
            {
                var digest = BitConverter.ToString(md5.ComputeHash(Encoding.UTF8.GetBytes(assetPath)))
                    .Replace("-", string.Empty)
                    .Substring(0, 12);
                return flat.Substring(0, 120) + "__" + digest + Path.GetExtension(flat);
            }
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
                    if (m == null) continue;
                    // Manifests written before asset capture existed have no list at all; a null
                    // here would make capture silently no-op, which is the one outcome we cannot
                    // afford in a safety net.
                    if (m.trackedAssetPaths == null) m.trackedAssetPaths = new List<string>();
                    if (m.capturedAssets == null) m.capturedAssets = new List<CapturedAsset>();
                    results.Add(m);
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

        public static bool Restore(string id, out string error) => Restore(id, true, out error);

        /// <summary>
        /// Restores captured assets and, when <paramref name="includeScene"/> is true, the active
        /// scene file (which reopens the scene and discards unsaved work). Pass false to revert
        /// only asset/importer edits — e.g. undoing a texture downsize without a scene reload.
        /// </summary>
        public static bool Restore(string id, bool includeScene, out string error)
        {
            error = string.Empty;
            var manifest = Find(id);
            if (manifest == null)
            {
                error = $"Checkpoint '{id}' not found.";
                return false;
            }

            var dir = Path.Combine(Root, id);
            var snapshotScene = Path.Combine(dir, SceneSnapshotFile);

            // Assets first: importer settings live in .meta, so restore both, then let the
            // AssetDatabase reimport before the scene reopens and rebinds references.
            var restoredAssets = 0;
            var failures = new List<string>();
            try
            {
                AssetDatabase.StartAssetEditing();
                foreach (var captured in manifest.capturedAssets ?? new List<CapturedAsset>())
                {
                    if (captured == null || string.IsNullOrEmpty(captured.assetPath)) continue;
                    try
                    {
                        var storedAbs = Path.Combine(dir, captured.storedFile.Replace('/', Path.DirectorySeparatorChar));
                        if (!File.Exists(storedAbs))
                        {
                            failures.Add($"{captured.assetPath} (snapshot missing)");
                            continue;
                        }

                        var destAbs = AbsPath(captured.assetPath);
                        var destDir = Path.GetDirectoryName(destAbs);
                        if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);
                        File.Copy(storedAbs, destAbs, overwrite: true);

                        if (!string.IsNullOrEmpty(captured.storedMeta))
                        {
                            var metaAbs = Path.Combine(dir, captured.storedMeta.Replace('/', Path.DirectorySeparatorChar));
                            if (File.Exists(metaAbs))
                                File.Copy(metaAbs, destAbs + ".meta", overwrite: true);
                        }
                        restoredAssets++;
                    }
                    catch (Exception ex)
                    {
                        failures.Add($"{captured.assetPath} ({ex.Message})");
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            foreach (var captured in manifest.capturedAssets ?? new List<CapturedAsset>())
            {
                if (captured?.assetPath == null) continue;
                try { AssetDatabase.ImportAsset(captured.assetPath, ImportAssetOptions.ForceUpdate); }
                catch { /* reported via failures below */ }
            }

            if (includeScene && !string.IsNullOrEmpty(manifest.activeScenePath) && File.Exists(snapshotScene))
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
                    error = $"Scene restore failed: {ex.Message}" +
                            (failures.Count > 0 ? $" (asset failures: {string.Join(", ", failures)})" : string.Empty);
                    return false;
                }
            }

            AssetDatabase.Refresh();

            if (failures.Count > 0)
            {
                error = $"Restored {restoredAssets} asset(s), but failed: {string.Join(", ", failures)}";
                return false;
            }

            return true;
        }

        /// <summary>Assets captured into a checkpoint, for reporting.</summary>
        public static int CapturedAssetCount(string id) => Find(id)?.capturedAssets?.Count ?? 0;

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
            var dir = Path.Combine(Root, manifest.id);
            sb.AppendLine($"Tracked assets ({manifest.trackedAssetPaths.Count}), captured: {manifest.capturedAssets?.Count ?? 0}");
            foreach (var path in manifest.trackedAssetPaths)
            {
                // Each tracked path has its own snapshot: the scene uses active_scene.unity,
                // everything else uses its captured copy under assets/.
                var captured = manifest.capturedAssets?
                    .FirstOrDefault(c => string.Equals(c.assetPath, path, StringComparison.OrdinalIgnoreCase));
                var snapshot = captured != null
                    ? Path.Combine(dir, captured.storedFile.Replace('/', Path.DirectorySeparatorChar))
                    : Path.Combine(dir, SceneSnapshotFile);

                var current = AbsPath(path);
                string status;
                if (!File.Exists(current)) status = "missing-from-disk";
                else if (!File.Exists(snapshot)) status = "no-snapshot";
                else
                {
                    var snapSize = new FileInfo(snapshot).Length;
                    var curSize = new FileInfo(current).Length;
                    status = (snapSize == curSize) ? "size-match" : $"changed (snap={snapSize}, cur={curSize})";
                }
                var metaNote = captured != null && !string.IsNullOrEmpty(captured.storedMeta) ? " +meta" : string.Empty;
                sb.AppendLine($"  - {path} [{status}]{metaNote}");
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
