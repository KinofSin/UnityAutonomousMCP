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
        private bool _attachSelection, _attachConsole, _attachScreenshot;

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
            DrawQuickAsk();
            DrawFeed();
            DrawComposer();
        }

        private void DrawQuickAsk()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("What's next?", EditorStyles.miniButton)) QuickAsk("whats_next");
                if (GUILayout.Button("What's wrong?", EditorStyles.miniButton)) QuickAsk("whats_wrong");
                if (GUILayout.Button("Upload-ready?", EditorStyles.miniButton)) QuickAsk("upload_ready");
                if (GUILayout.Button("Set up my project", EditorStyles.miniButton)) QuickAsk("setup_project");
            }
        }

        private void QuickAsk(string key)
        {
            AdvisorStore.Enqueue("quick_ask", new JObject { ["key"] = key }.ToString());
        }

        private void DrawHeader()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("MCP Advisor", EditorStyles.boldLabel);
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
                    if (a.kind == "card")
                    {
                        EditorGUILayout.LabelField(a.title ?? string.Empty, EditorStyles.boldLabel);
                        if (!string.IsNullOrEmpty(a.body))
                            EditorGUILayout.LabelField(a.body, EditorStyles.wordWrappedLabel);
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            foreach (var act in a.actions ?? new System.Collections.Generic.List<CardAction>())
                                if (GUILayout.Button(act.label ?? act.id, EditorStyles.miniButton))
                                    EnqueueCardAction(a.id, act.id);
                        }
                    }
                    else
                    {
                        var icon = a.level == "warning" ? "⚠ " : a.level == "success" ? "[ok] " : "";
                        EditorGUILayout.LabelField(icon + (a.text ?? string.Empty), EditorStyles.wordWrappedLabel);
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private void EnqueueCardAction(string cardId, string actionId)
        {
            AdvisorStore.Enqueue("card_action",
                new JObject { ["cardId"] = cardId, ["actionId"] = actionId }.ToString());
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
                    _attachScreenshot = GUILayout.Toggle(_attachScreenshot,
                        "Screenshot", EditorStyles.miniButton);
                }
                _compose = EditorGUILayout.TextField("Note", _compose);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    using (new EditorGUI.DisabledScope(
                        string.IsNullOrWhiteSpace(_compose) && !_attachSelection && !_attachConsole && !_attachScreenshot))
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

            if (_attachScreenshot)
            {
                var path = "Temp/advisor_shot_" + DateTime.UtcNow.Ticks + ".png";
                var resp = AutonomousMcpToolDispatcher.HandleCaptureScreenshot(
                    new JObject { ["source"] = "editor", ["save_path"] = path });
                if (resp != null && resp.success)
                    AdvisorStore.Enqueue("screenshot",
                        new JObject { ["source"] = "editor", ["path"] = path }.ToString());
            }

            _compose = string.Empty;
            _attachSelection = _attachConsole = _attachScreenshot = false;
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
