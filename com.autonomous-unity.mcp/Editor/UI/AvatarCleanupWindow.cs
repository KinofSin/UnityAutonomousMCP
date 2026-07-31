using System;
using System.Collections.Generic;
using System.Linq;
using AutonomousMcp.Editor.Core;
using AutonomousMcp.Editor.Perception;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AutonomousMcp.Editor.UI
{
    /// <summary>
    /// Avatar Cleanup — per-object cost, with removal.
    ///
    /// Reads <see cref="AvatarCost"/>, the same model behind the <c>cost</c> dossier section, so
    /// the window and the agent cannot disagree about the same avatar.
    ///
    /// This is the one place the MCP UI performs a scene edit rather than queuing consent for the
    /// agent, and the reason is narrow: deleting a scene object is undoable natively. Every removal
    /// goes onto Unity's undo stack as a single collapsed step *and* takes a checkpoint first, so
    /// it is reversible two ways without duplicating the agent's permission layer. Asset edits are
    /// deliberately still out of scope here — those are not undoable and stay with the agent.
    /// </summary>
    internal sealed class AvatarCleanupWindow : EditorWindow
    {
        private enum SortMode { Polygons, MaterialSlots, Name, DisabledFirst }

        private GameObject _root;
        private CostReport _report;
        private readonly HashSet<int> _selected = new HashSet<int>();
        private Vector2 _scroll;
        private SortMode _sort = SortMode.Polygons;
        private bool _onlyDisabled;
        private string _status = string.Empty;

        [MenuItem("Window/Autonomous MCP/Avatar Cleanup")]
        public static void Open()
        {
            var w = GetWindow<AvatarCleanupWindow>(false, "Avatar Cleanup", true);
            w.minSize = new Vector2(560, 380);
            w.Show();
        }

        private void OnEnable()
        {
            if (_root == null && Selection.activeGameObject != null)
                _root = Selection.activeGameObject.transform.root.gameObject;
            Refresh();
        }

        private void Refresh()
        {
            // Instance ids in the previous report are stale after a delete or a scene change.
            _selected.Clear();
            _report = _root != null
                ? AvatarCost.Build(_root, SceneManager.GetActiveScene())
                : null;
        }

        private void OnGUI()
        {
            DrawToolbar();

            if (_root == null)
            {
                EditorGUILayout.HelpBox(
                    "Assign an avatar root above, or select one in the Hierarchy and press Refresh.",
                    MessageType.Info);
                return;
            }
            if (_report == null || _report.Entries.Count == 0)
            {
                EditorGUILayout.HelpBox($"No renderers found under '{_root.name}'.", MessageType.Info);
                return;
            }

            DrawSummary();
            DrawRows();
            DrawFooter();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                EditorGUI.BeginChangeCheck();
                _root = (GameObject)EditorGUILayout.ObjectField(
                    _root, typeof(GameObject), true, GUILayout.Width(220));
                if (EditorGUI.EndChangeCheck()) Refresh();

                if (GUILayout.Button("Use selection", EditorStyles.toolbarButton, GUILayout.Width(90)))
                {
                    if (Selection.activeGameObject != null)
                    {
                        _root = Selection.activeGameObject.transform.root.gameObject;
                        Refresh();
                    }
                }
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60)))
                    Refresh();

                GUILayout.Space(8);
                GUILayout.Label("Sort", GUILayout.Width(28));
                var next = (SortMode)EditorGUILayout.EnumPopup(_sort, EditorStyles.toolbarPopup, GUILayout.Width(110));
                if (next != _sort) _sort = next;

                _onlyDisabled = GUILayout.Toggle(
                    _onlyDisabled, new GUIContent("Disabled only", "Show only objects that are switched off"),
                    EditorStyles.toolbarButton, GUILayout.Width(90));

                GUILayout.FlexibleSpace();
                if (!string.IsNullOrEmpty(_status)) GUILayout.Label(_status, EditorStyles.miniLabel);
            }
        }

        private void DrawSummary()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(
                    $"{_report.TotalPolygons:N0} polys ({_report.PolygonRank})   " +
                    $"{_report.TotalMaterialSlots} material slots ({_report.MaterialSlotRank})   " +
                    $"{_report.SkinnedMeshes} skinned meshes ({_report.SkinnedMeshRank})",
                    EditorStyles.boldLabel);

                if (_report.InactiveObjects > 0)
                {
                    var all = _report.Without(_report.InactivePolygons, _report.InactiveMaterialSlots);
                    // The headline finding: switched off does not mean free. VRChat's stats walk
                    // renderers with includeInactive, so these cost rank and download size today.
                    EditorGUILayout.LabelField(
                        $"{_report.InactiveObjects} disabled objects still cost " +
                        $"{_report.InactivePolygons:N0} polys " +
                        $"({_report.ShareOfPolygons(_report.InactivePolygons):P0}) — VRChat counts them. " +
                        $"Removing all leaves {all.Polygons:N0} ({all.PolygonRank}).",
                        EditorStyles.wordWrappedMiniLabel);

                    if (GUILayout.Button("Select all disabled", GUILayout.Width(140)))
                    {
                        _selected.Clear();
                        foreach (var e in _report.Entries.Where(e => !e.Active))
                            _selected.Add(e.InstanceId);
                    }
                }
            }
        }

        private IEnumerable<CostEntry> VisibleEntries()
        {
            var rows = _onlyDisabled
                ? _report.Entries.Where(e => !e.Active)
                : (IEnumerable<CostEntry>)_report.Entries;

            switch (_sort)
            {
                case SortMode.MaterialSlots: return rows.OrderByDescending(e => e.MaterialSlots);
                case SortMode.Name: return rows.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase);
                case SortMode.DisabledFirst: return rows.OrderBy(e => e.Active).ThenByDescending(e => e.Polygons);
                default: return rows.OrderByDescending(e => e.Polygons);
            }
        }

        private void DrawRows()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Space(20);
                GUILayout.Label("Object", EditorStyles.miniBoldLabel, GUILayout.MinWidth(160));
                GUILayout.Label("Polys", EditorStyles.miniBoldLabel, GUILayout.Width(64));
                GUILayout.Label("Share", EditorStyles.miniBoldLabel, GUILayout.Width(48));
                GUILayout.Label("Mats", EditorStyles.miniBoldLabel, GUILayout.Width(40));
                GUILayout.Label("Shapes", EditorStyles.miniBoldLabel, GUILayout.Width(48));
                GUILayout.Label("State", EditorStyles.miniBoldLabel, GUILayout.Width(56));
                GUILayout.Space(48);
            }

            using (var scroll = new EditorGUILayout.ScrollViewScope(_scroll))
            {
                _scroll = scroll.scrollPosition;
                foreach (var e in VisibleEntries())
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        var picked = _selected.Contains(e.InstanceId);
                        var now = GUILayout.Toggle(picked, GUIContent.none, GUILayout.Width(18));
                        if (now != picked)
                        {
                            if (now) _selected.Add(e.InstanceId);
                            else _selected.Remove(e.InstanceId);
                        }

                        // Disabled objects are the ones people assume are free, so they are the
                        // ones worth drawing attention to rather than greying out.
                        var prev = GUI.color;
                        if (!e.Active) GUI.color = new Color(1f, 0.78f, 0.45f);
                        GUILayout.Label(new GUIContent(e.Name, e.Path), GUILayout.MinWidth(160));
                        GUI.color = prev;

                        GUILayout.Label($"{e.Polygons:N0}", GUILayout.Width(64));
                        GUILayout.Label($"{_report.ShareOfPolygons(e.Polygons):P0}", GUILayout.Width(48));
                        GUILayout.Label(e.MaterialSlots.ToString(), GUILayout.Width(40));
                        GUILayout.Label(e.Blendshapes > 0 ? e.Blendshapes.ToString() : "-", GUILayout.Width(48));
                        GUILayout.Label(e.Active ? "on" : "OFF", GUILayout.Width(56));

                        if (GUILayout.Button("Ping", EditorStyles.miniButton, GUILayout.Width(44)))
                        {
                            var go = e.Resolve();
                            if (go != null)
                            {
                                Selection.activeGameObject = go;
                                EditorGUIUtility.PingObject(go);
                            }
                        }
                    }
                }
            }
        }

        private void DrawFooter()
        {
            var picked = _report.Entries.Where(e => _selected.Contains(e.InstanceId)).ToList();
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (picked.Count == 0)
                {
                    EditorGUILayout.LabelField("Nothing selected.", EditorStyles.miniLabel);
                    return;
                }

                long polys = picked.Sum(e => (long)e.Polygons);
                int mats = picked.Sum(e => e.MaterialSlots);
                var after = _report.Without(polys, mats);

                EditorGUILayout.LabelField(
                    $"{picked.Count} selected · {polys:N0} polys · {mats} material slots  →  " +
                    $"leaves {after.Polygons:N0} ({after.PolygonRank}) and " +
                    $"{after.MaterialSlots} slots ({after.MaterialSlotRank})",
                    EditorStyles.wordWrappedLabel);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Select in Hierarchy", GUILayout.Width(140)))
                        Selection.objects = picked.Select(e => (UnityEngine.Object)e.Resolve())
                            .Where(o => o != null).ToArray();

                    if (GUILayout.Button("Clear", GUILayout.Width(60)))
                        _selected.Clear();

                    GUILayout.FlexibleSpace();

                    var prev = GUI.backgroundColor;
                    GUI.backgroundColor = new Color(1f, 0.6f, 0.55f);
                    if (GUILayout.Button($"Delete {picked.Count} object(s)…", GUILayout.Width(160)))
                        RemoveSelected(picked);
                    GUI.backgroundColor = prev;
                }
            }
        }

        private void RemoveSelected(List<CostEntry> picked)
        {
            var targets = picked.Select(e => new { Entry = e, Go = e.Resolve() })
                .Where(t => t.Go != null)
                .ToList();
            if (targets.Count == 0)
            {
                _status = "nothing left to delete — refreshing";
                Refresh();
                return;
            }

            if (targets.Any(t => t.Go == _root))
            {
                EditorUtility.DisplayDialog("Cannot delete the avatar root",
                    "Deselect the avatar root itself before deleting.", "OK");
                return;
            }

            long polys = targets.Sum(t => (long)t.Entry.Polygons);
            var after = _report.Without(polys, targets.Sum(t => t.Entry.MaterialSlots));
            var active = targets.Where(t => t.Entry.Active).ToList();
            var prefabs = targets.Where(t => PrefabUtility.IsPartOfPrefabInstance(t.Go)).ToList();

            var message =
                $"Delete {targets.Count} object(s), saving {polys:N0} polys?\n\n" +
                string.Join("\n", targets.Take(12).Select(t =>
                    $"  {(t.Entry.Active ? "on " : "OFF")}  {t.Entry.Name}  ({t.Entry.Polygons:N0})")) +
                (targets.Count > 12 ? $"\n  …and {targets.Count - 12} more" : "") +
                $"\n\nLeaves {after.Polygons:N0} polys ({after.PolygonRank}).";

            // Deleting something currently switched ON removes a visible part of the avatar; that
            // deserves louder billing than removing an unused wardrobe toggle.
            if (active.Count > 0)
                message += $"\n\nWARNING: {active.Count} of these are currently ENABLED and visible.";

            if (prefabs.Count > 0)
                message +=
                    $"\n\n{prefabs.Count} are part of a prefab instance. Deleting them is recorded as " +
                    "a prefab override — it holds for upload, but reverting the prefab brings them back.";

            message += "\n\nA checkpoint is taken first, and this is a single Undo step.";

            if (!EditorUtility.DisplayDialog("Delete objects", message, "Delete", "Cancel"))
                return;

            string checkpointId;
            try
            {
                var manifest = CheckpointStore.Create(
                    "avatar-cleanup-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"),
                    "avatar-cleanup", "editor-ui");
                checkpointId = manifest?.id ?? "(none)";
            }
            catch (Exception ex)
            {
                // No checkpoint means no safety net, so this stops rather than proceeding on Undo alone.
                EditorUtility.DisplayDialog("Checkpoint failed — nothing deleted", ex.Message, "OK");
                return;
            }

            var group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName($"Avatar cleanup: delete {targets.Count} object(s)");
            var failures = new List<string>();
            int removed = 0;
            foreach (var t in targets)
            {
                try
                {
                    Undo.DestroyObjectImmediate(t.Go);
                    removed++;
                }
                catch (Exception ex)
                {
                    failures.Add($"{t.Entry.Name}: {ex.Message}");
                }
            }
            Undo.CollapseUndoOperations(group);

            Refresh();
            _status = $"deleted {removed} · checkpoint {checkpointId}";

            var summary = $"Deleted {removed} object(s).\n\nCheckpoint: {checkpointId}\nUndo restores them in one step.";
            if (failures.Count > 0)
                summary += "\n\nFailed:\n  " + string.Join("\n  ", failures.Take(8));
            EditorUtility.DisplayDialog("Avatar cleanup", summary, "OK");
        }
    }
}
