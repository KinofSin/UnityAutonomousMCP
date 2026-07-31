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
    ///
    /// "Disabled" is NOT "unused". Objects a menu toggle switches on and off, or that VRCFury /
    /// Modular Avatar reference directly, are live wardrobe items; bulk-select skips them. Objects
    /// merely animated by blendshape or material curves are not protected — that signal fires on
    /// nearly every renderer and protects nothing if you honour it.
    /// </summary>
    internal sealed class AvatarCleanupWindow : EditorWindow
    {
        private enum SortMode { Polygons, MaterialSlots, ExclusiveVram, Name, DisabledFirst, UndrivenFirst }

        private GameObject _root;
        private CostReport _report;
        private readonly HashSet<int> _selected = new HashSet<int>();
        private Vector2 _scroll;
        private SortMode _sort = SortMode.Polygons;
        private bool _onlyDisabled;
        private bool _onlyUndriven;
        private string _status = string.Empty;

        [MenuItem("Window/Autonomous MCP/Avatar Cleanup")]
        public static void Open()
        {
            var w = GetWindow<AvatarCleanupWindow>(false, "Avatar Cleanup", true);
            w.minSize = new Vector2(720, 380);
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
                    _root, typeof(GameObject), true, GUILayout.Width(200));
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
                _sort = (SortMode)EditorGUILayout.EnumPopup(_sort, EditorStyles.toolbarPopup, GUILayout.Width(120));

                _onlyDisabled = GUILayout.Toggle(
                    _onlyDisabled, new GUIContent("Disabled", "Show only objects that are switched off"),
                    EditorStyles.toolbarButton, GUILayout.Width(70));
                _onlyUndriven = GUILayout.Toggle(
                    _onlyUndriven,
                    new GUIContent("Undriven",
                        "Show only objects no menu toggle / VRCFury / MA controls. " +
                        "Blendshape and material animation does not count as control."),
                    EditorStyles.toolbarButton, GUILayout.Width(70));

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
                    $"{_report.SkinnedMeshes} skinned meshes ({_report.SkinnedMeshRank})   " +
                    $"{_report.TotalPhysBones} PhysBones   " +
                    $"{_report.TotalBones} bones",
                    EditorStyles.boldLabel);

                var exclMb = _report.TotalExclusiveVramBytes / (1024d * 1024d);
                var sharedMb = _report.TotalSharedVramBytes / (1024d * 1024d);
                EditorGUILayout.LabelField(
                    $"VRAM (editor, ~2× over-report): exclusive {exclMb:F1} MB · shared {sharedMb:F1} MB. " +
                    "Deleting a renderer does NOT reclaim bones.",
                    EditorStyles.wordWrappedMiniLabel);

                if (_report.InactiveObjects > 0)
                {
                    EditorGUILayout.LabelField(
                        $"{_report.InactiveObjects} disabled ({_report.InactivePolygons:N0} polys): " +
                        $"{_report.InactiveDriven} menu-driven wardrobe ({_report.InactiveDrivenPolygons:N0} polys), " +
                        $"{_report.InactiveUndriven} undriven ({_report.InactiveUndrivenPolygons:N0} polys). " +
                        "VRChat counts all of them — but driven ones are live toggles, not free space. " +
                        "Objects only touched by blendshape/material curves count as undriven.",
                        EditorStyles.wordWrappedMiniLabel);

                    if (GUILayout.Button(
                            $"Select undriven ({_report.InactiveUndriven})",
                            GUILayout.Width(160)))
                    {
                        _selected.Clear();
                        foreach (var e in _report.Entries.Where(e => !e.Active && !e.IsDriven))
                            _selected.Add(e.InstanceId);
                    }
                }

                if (_report.Twins != null && _report.Twins.Count > 0)
                {
                    var names = string.Join(", ", _report.Twins.Select(t => t.Name + (t.Active ? "" : " (inactive)")));
                    EditorGUILayout.HelpBox(
                        $"Sibling avatar twin(s) detected: {names}. Edits here do NOT propagate — " +
                        "optimize / clean each twin separately.",
                        MessageType.Warning);
                }
            }
        }

        private IEnumerable<CostEntry> VisibleEntries()
        {
            IEnumerable<CostEntry> rows = _report.Entries;
            if (_onlyDisabled) rows = rows.Where(e => !e.Active);
            if (_onlyUndriven) rows = rows.Where(e => !e.IsDriven);

            switch (_sort)
            {
                case SortMode.MaterialSlots: return rows.OrderByDescending(e => e.MaterialSlots);
                case SortMode.ExclusiveVram: return rows.OrderByDescending(e => e.ExclusiveVramBytes);
                case SortMode.Name: return rows.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase);
                case SortMode.DisabledFirst: return rows.OrderBy(e => e.Active).ThenByDescending(e => e.Polygons);
                case SortMode.UndrivenFirst: return rows.OrderBy(e => e.IsDriven).ThenByDescending(e => e.Polygons);
                default: return rows.OrderByDescending(e => e.Polygons);
            }
        }

        private void DrawRows()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Space(20);
                GUILayout.Label("Object", EditorStyles.miniBoldLabel, GUILayout.MinWidth(140));
                GUILayout.Label("Polys", EditorStyles.miniBoldLabel, GUILayout.Width(56));
                GUILayout.Label("Share", EditorStyles.miniBoldLabel, GUILayout.Width(40));
                GUILayout.Label("Mats", EditorStyles.miniBoldLabel, GUILayout.Width(36));
                GUILayout.Label("Excl MB", EditorStyles.miniBoldLabel, GUILayout.Width(52));
                GUILayout.Label("PB", EditorStyles.miniBoldLabel, GUILayout.Width(28));
                GUILayout.Label("State", EditorStyles.miniBoldLabel, GUILayout.Width(40));
                GUILayout.Label("Driven by", EditorStyles.miniBoldLabel, GUILayout.MinWidth(100));
                GUILayout.Space(44);
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

                        var prev = GUI.color;
                        if (!e.Active && e.IsDriven) GUI.color = new Color(1f, 0.78f, 0.45f);
                        else if (!e.Active) GUI.color = new Color(0.7f, 0.9f, 0.7f);
                        GUILayout.Label(new GUIContent(e.Name, e.Path), GUILayout.MinWidth(140));
                        GUI.color = prev;

                        GUILayout.Label($"{e.Polygons:N0}", GUILayout.Width(56));
                        GUILayout.Label($"{_report.ShareOfPolygons(e.Polygons):P0}", GUILayout.Width(40));
                        GUILayout.Label(e.MaterialSlots.ToString(), GUILayout.Width(36));
                        var excl = e.ExclusiveVramBytes / (1024d * 1024d);
                        GUILayout.Label(excl > 0.01 ? excl.ToString("F1") : "-", GUILayout.Width(52));
                        GUILayout.Label(e.PhysBones > 0 ? e.PhysBones.ToString() : "-", GUILayout.Width(28));
                        GUILayout.Label(e.Active ? "on" : "OFF", GUILayout.Width(40));
                        DrawDrivenBy(e);

                        if (GUILayout.Button("Ping", EditorStyles.miniButton, GUILayout.Width(40)))
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

        /// <summary>
        /// Controlling drivers read plainly; a purely animated object is dimmed and prefixed so the
        /// column never implies "do not touch" for something only a hue-shift slider targets.
        /// </summary>
        private void DrawDrivenBy(CostEntry e)
        {
            if (e.IsDriven)
            {
                GUILayout.Label(new GUIContent(e.DrivenBySummary, e.ReferencedBySummary),
                    GUILayout.MinWidth(100));
                return;
            }

            if (!e.IsReferenced)
            {
                GUILayout.Label("-", GUILayout.MinWidth(100));
                return;
            }

            var prev = GUI.color;
            GUI.color = new Color(prev.r, prev.g, prev.b, 0.55f);
            GUILayout.Label(
                new GUIContent(
                    "~ " + e.ReferencedBySummary,
                    "Animated but not switched on/off — blendshape or material curves only. " +
                    "Deleting it drops those curves' target, it does not break a menu toggle."),
                GUILayout.MinWidth(100));
            GUI.color = prev;
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
                var driven = picked.Count(e => e.IsDriven);

                EditorGUILayout.LabelField(
                    $"{picked.Count} selected · {polys:N0} polys · {mats} material slots  →  " +
                    $"leaves {after.Polygons:N0} ({after.PolygonRank}) and " +
                    $"{after.MaterialSlots} slots ({after.MaterialSlotRank})" +
                    (driven > 0 ? $"  ·  {driven} are menu-driven" : ""),
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
            var driven = targets.Where(t => t.Entry.IsDriven).ToList();
            var prefabs = targets.Where(t => PrefabUtility.IsPartOfPrefabInstance(t.Go)).ToList();

            var message =
                $"Delete {targets.Count} object(s), saving {polys:N0} polys?\n\n" +
                string.Join("\n", targets.Take(12).Select(t =>
                    $"  {(t.Entry.Active ? "on " : "OFF")}  {t.Entry.Name}  ({t.Entry.Polygons:N0})" +
                    (t.Entry.IsDriven ? $"  ← {t.Entry.DrivenBySummary}" : ""))) +
                (targets.Count > 12 ? $"\n  …and {targets.Count - 12} more" : "") +
                $"\n\nLeaves {after.Polygons:N0} polys ({after.PolygonRank}).";

            if (active.Count > 0)
                message += $"\n\nWARNING: {active.Count} of these are currently ENABLED and visible.";

            if (driven.Count > 0)
            {
                message += $"\n\nWARNING: {driven.Count} are CONTROLLED by a menu toggle / VRCFury / Modular Avatar:\n" +
                    string.Join("\n", driven.Take(8).Select(t =>
                        $"  · {t.Entry.Name}: {t.Entry.DrivenBySummary}")) +
                    "\nDeleting them leaves dead menu controls.";
            }

            var animatedOnly = targets.Where(t => !t.Entry.IsDriven && t.Entry.IsReferenced).ToList();
            if (animatedOnly.Count > 0)
                message +=
                    $"\n\n{animatedOnly.Count} are animated but never switched off (blendshape / material " +
                    "curves only). Those curves just lose a target — no menu breaks.";

            if (prefabs.Count > 0)
                message +=
                    $"\n\n{prefabs.Count} are part of a prefab instance. Deleting them is recorded as " +
                    "a prefab override — it holds for upload, but reverting the prefab brings them back.";

            message += "\n\nA checkpoint is taken first, and this is a single Undo step.";

            if (!EditorUtility.DisplayDialog("Delete objects", message, "Delete", "Cancel"))
                return;

            // Second confirmation when anything is menu-driven — the whole point of Phase A.
            if (driven.Count > 0)
            {
                if (!EditorUtility.DisplayDialog(
                        "Confirm deleting menu-driven objects",
                        $"{driven.Count} selected object(s) are live wardrobe toggles.\n\n" +
                        "This will leave dead menu controls and broken FX curves.\n\n" +
                        "Really delete them?",
                        "Yes, delete driven objects",
                        "Cancel"))
                    return;
            }

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
