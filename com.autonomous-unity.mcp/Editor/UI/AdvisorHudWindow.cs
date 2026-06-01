using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using AutonomousMcp.Editor.Advisor;

namespace AutonomousMcp.Editor.UI
{
    // Dockable Advisor HUD. Renders the advice feed (AI -> user) and lets the user enqueue sends
    // (note / selection / console errors) to the outbox (user -> AI). Thin: it never executes Unity
    // changes — Send just queues structured items the AI picks up via hud_poll.
    internal sealed class AdvisorHudWindow : EditorWindow
    {
        private Vector2 _feedScroll;
        private string _compose = string.Empty;
        private bool _attachSelection, _attachConsole;

        [MenuItem("Window/Autonomous MCP/Advisor")]
        public static void Open()
        {
            var w = GetWindow<AdvisorHudWindow>(false, "MCP Advisor", true);
            w.minSize = new Vector2(320, 360);
            w.Show();
        }

        private void OnEnable() => AdvisorStore.EnsureLoaded();
        private void OnInspectorUpdate() => Repaint(); // pick up advice posted over the bridge

        private void OnGUI()
        {
            DrawHeader();
            DrawFeed();
            DrawComposer();
        }

        private void DrawHeader()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("\U0001F6F0 MCP Advisor", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                var pending = AdvisorStore.PendingCount();
                if (pending > 0) GUILayout.Label($"{pending} queued →", EditorStyles.miniLabel);
            }
        }

        private void DrawFeed()
        {
            _feedScroll = EditorGUILayout.BeginScrollView(_feedScroll, GUILayout.ExpandHeight(true));
            foreach (var a in AdvisorStore.GetAdvice())
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    var icon = a.level == "warning" ? "⚠ " : a.level == "success" ? "✅ " : "";
                    EditorGUILayout.LabelField(icon + (a.text ?? a.title ?? string.Empty),
                        EditorStyles.wordWrappedLabel);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawComposer()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    var selCount = Selection.gameObjects?.Length ?? 0;
                    _attachSelection = GUILayout.Toggle(_attachSelection,
                        $"◳ Selection ({selCount})", EditorStyles.miniButton);
                    _attachConsole = GUILayout.Toggle(_attachConsole,
                        "⚠ Console errors", EditorStyles.miniButton);
                }
                _compose = EditorGUILayout.TextField("Note", _compose);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    using (new EditorGUI.DisabledScope(
                        string.IsNullOrWhiteSpace(_compose) && !_attachSelection && !_attachConsole))
                    {
                        if (GUILayout.Button("Send", GUILayout.Width(90))) Send();
                    }
                }
            }
        }

        private void Send()
        {
            if (!string.IsNullOrWhiteSpace(_compose))
                AdvisorStore.Enqueue("note", new JObject { ["text"] = _compose }.ToString());

            if (_attachSelection)
            {
                var objs = (Selection.gameObjects ?? Array.Empty<GameObject>()).Select(g => new JObject
                {
                    ["name"] = g.name,
                    ["path"] = GetPath(g.transform),
                    ["components"] = new JArray(g.GetComponents<Component>()
                        .Where(c => c != null).Select(c => c.GetType().Name))
                });
                AdvisorStore.Enqueue("selection", new JObject { ["objects"] = new JArray(objs) }.ToString());
            }

            if (_attachConsole)
            {
                var resp = AutonomousMcpToolDispatcher.HandleReadConsole(
                    new JObject { ["level"] = "error", ["limit"] = 50 });
                AdvisorStore.Enqueue("console", resp?.data?.ToString() ?? "{}");
            }

            _compose = string.Empty;
            _attachSelection = _attachConsole = false;
            GUI.FocusControl(null);
        }

        private static string GetPath(Transform t)
        {
            var p = t.name;
            while (t.parent != null) { t = t.parent; p = t.name + "/" + p; }
            return p;
        }
    }
}
