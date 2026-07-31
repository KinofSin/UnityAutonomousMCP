using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using AutonomousMcp.Editor.Advisor;
using AutonomousMcp.Editor.Core;

namespace AutonomousMcp.Editor.UI
{
    // Dockable Advisor HUD. Renders the advice feed (AI -> user) and lets the user enqueue sends
    // (note / selection / console errors) to the outbox (user -> AI). Thin: it never executes Unity
    // changes — Send just queues structured items the AI picks up via hud_poll.
    internal sealed class AdvisorHudWindow : EditorWindow
    {
        private Vector2 _feedScroll;
        private Vector2 _queueScroll;
        private string _compose = string.Empty;
        private string _checkpointLabel = "manual";
        private bool _attachSelection, _attachConsole, _attachScreenshot;
        private bool _queueExpanded = true;

        [MenuItem("Window/Autonomous MCP/Advisor")]
        public static void Open()
        {
            var w = GetWindow<AdvisorHudWindow>(false, "MCP Advisor", true);
            w.minSize = new Vector2(360, 420);
            w.Show();
        }

        [MenuItem("Window/Autonomous MCP/Create Checkpoint")]
        public static void CreateCheckpointMenu()
        {
            var label = "manual-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            if (!EditorUtility.DisplayDialog(
                    "Create Checkpoint",
                    "Create a scene/asset checkpoint now?\n\nLabel: " + label +
                    "\n\n(Open the Advisor HUD to set a custom label before creating.)",
                    "Create",
                    "Cancel"))
                return;
            CreateCheckpoint(label, "menu");
        }

        private void OnEnable() => AdvisorStore.EnsureLoaded();
        private void OnInspectorUpdate() => Repaint(); // pick up advice posted over the bridge

        private void OnGUI()
        {
            DrawToolbar();
            DrawQueueBanner();
            DrawQuickAsk();
            DrawFeed();
            DrawComposer();
        }

        private static bool BridgeConnected
        {
            get
            {
                var conn = AutonomousMcpConnection.Current;
                return conn != null && conn.IsConnected;
            }
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("MCP Advisor", EditorStyles.boldLabel);
                GUILayout.Space(8);

                var connected = BridgeConnected;
                var prev = GUI.color;
                GUI.color = connected ? new Color(0.55f, 0.9f, 0.55f) : new Color(1f, 0.55f, 0.4f);
                GUILayout.Label(connected ? "Connected" : "Disconnected", EditorStyles.miniLabel);
                GUI.color = prev;

                GUILayout.FlexibleSpace();

                var pending = AdvisorStore.PendingCount();
                if (pending > 0)
                    GUILayout.Label(pending + " queued", EditorStyles.miniLabel);

                if (GUILayout.Button(new GUIContent("Checkpoint", "Create a scene/asset checkpoint now"),
                        EditorStyles.toolbarButton, GUILayout.Width(80)))
                {
                    var label = string.IsNullOrWhiteSpace(_checkpointLabel) ? "manual" : _checkpointLabel.Trim();
                    if (EditorUtility.DisplayDialog("Create Checkpoint", "Create checkpoint labeled:\n" + label + "?", "Create", "Cancel"))
                        CreateCheckpoint(label, "hud");
                }

                if (GUILayout.Button(new GUIContent("Cleanup", "Per-object cost and removal for the selected avatar"),
                        EditorStyles.toolbarButton, GUILayout.Width(62)))
                {
                    AvatarCleanupWindow.Open();
                }

                if (GUILayout.Button(new GUIContent("Clear feed", "Dismiss all advice items"),
                        EditorStyles.toolbarButton, GUILayout.Width(70)))
                {
                    if (EditorUtility.DisplayDialog("Clear advice feed?", "Remove all advice items from the feed?", "Clear", "Cancel"))
                        AdvisorStore.ClearAdvice();
                }
            }
        }

        private void DrawQueueBanner()
        {
            var pending = AdvisorStore.PendingCount();
            if (pending <= 0) return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    var icon = EditorGUIUtility.IconContent("console.warnicon.sml");
                    if (icon != null && icon.image != null)
                        GUILayout.Label(icon, GUILayout.Width(18), GUILayout.Height(18));
                    GUILayout.Label(
                        pending + " queued — the AI reads these on its next action (hud_poll).",
                        EditorStyles.wordWrappedMiniLabel);
                    _queueExpanded = GUILayout.Toggle(_queueExpanded, _queueExpanded ? "Hide" : "Show",
                        EditorStyles.miniButton, GUILayout.Width(48));
                }

                if (!_queueExpanded) return;

                _queueScroll = EditorGUILayout.BeginScrollView(_queueScroll, GUILayout.MaxHeight(120));
                var items = AdvisorStore.GetOutbox();
                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label((i + 1) + ". [" + item.type + "] " + SummarizeOutbox(item),
                            EditorStyles.miniLabel);
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button("Cancel", EditorStyles.miniButton, GUILayout.Width(52)))
                        {
                            AdvisorStore.RemoveOutboxAt(i);
                            GUIUtility.ExitGUI();
                        }
                    }
                }
                EditorGUILayout.EndScrollView();

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Clear queue", EditorStyles.miniButton, GUILayout.Width(80)))
                    {
                        if (EditorUtility.DisplayDialog("Clear queued items?",
                                "Discard all unread items waiting for the AI?", "Clear", "Cancel"))
                            AdvisorStore.ClearOutbox();
                    }
                }
            }
        }

        private static string SummarizeOutbox(OutboxItem item)
        {
            if (item == null) return "";
            var payload = item.payload ?? "";
            try
            {
                var jo = JObject.Parse(payload);

                // Which button was pressed matters as much as which card — approve and dismiss are
                // indistinguishable in the queue otherwise.
                var actionId = jo.Value<string>("actionId");
                if (!string.IsNullOrEmpty(actionId))
                    return Truncate((jo.Value<string>("cardId") ?? "card") + " → " + actionId, 60);

                if (jo["objects"] is JArray objects)
                {
                    var names = objects
                        .Select(o => o.Value<string>("name"))
                        .Where(n => !string.IsNullOrEmpty(n))
                        .ToArray();
                    if (names.Length == 1) return Truncate(names[0], 60);
                    if (names.Length > 1)
                        return Truncate(names[0] + " +" + (names.Length - 1) + " more", 60);
                }

                if (jo["entries"] is JArray entries)
                    return entries.Count + (entries.Count == 1 ? " console entry" : " console entries");

                var text = jo.Value<string>("text");
                if (string.IsNullOrEmpty(text)) text = jo.Value<string>("key");
                if (string.IsNullOrEmpty(text)) text = jo.Value<string>("path");
                if (string.IsNullOrEmpty(text)) text = jo.Value<string>("cardId");
                if (!string.IsNullOrEmpty(text)) return Truncate(text, 60);
            }
            catch { /* free-form */ }
            return Truncate(payload.Replace("\r", " ").Replace("\n", " "), 60);
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s.Substring(0, max - 1) + "…";
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

        private void DrawFeed()
        {
            _feedScroll = EditorGUILayout.BeginScrollView(_feedScroll, GUILayout.ExpandHeight(true));
            var advice = AdvisorStore.GetAdvice();
            if (advice.Count == 0)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    GUILayout.Space(24);
                    EditorGUILayout.LabelField("No advice yet.", EditorStyles.centeredGreyMiniLabel);
                    EditorGUILayout.LabelField(
                        "When the AI posts via hud_post / hud_post_card, it shows up here.\n" +
                        "Use Send or a quick-ask below to queue something for the AI.",
                        EditorStyles.wordWrappedMiniLabel);
                    GUILayout.Space(24);
                }
            }
            else
            {
                foreach (var a in advice)
                {
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            var icon = IconForLevel(a.level);
                            if (icon != null && icon.image != null)
                                GUILayout.Label(icon, GUILayout.Width(18), GUILayout.Height(18));

                            if (a.kind == "card")
                                EditorGUILayout.LabelField(a.title ?? string.Empty, CardTitleStyle);
                            else
                                EditorGUILayout.LabelField(a.text ?? string.Empty, EditorStyles.wordWrappedLabel);

                            GUILayout.FlexibleSpace();
                            EditorGUILayout.LabelField(RelativeTime(a.postedAtUtc), EditorStyles.miniLabel, GUILayout.Width(56));
                            if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(22)))
                            {
                                AdvisorStore.DismissAdvice(a.id);
                                GUIUtility.ExitGUI();
                            }
                        }

                        if (a.kind == "card")
                        {
                            if (!string.IsNullOrEmpty(a.body))
                                EditorGUILayout.LabelField(a.body, EditorStyles.wordWrappedLabel);
                            using (new EditorGUILayout.HorizontalScope())
                            {
                                var actions = a.actions;
                                if (actions != null)
                                {
                                    foreach (var act in actions)
                                    {
                                        if (act == null) continue;
                                        if (GUILayout.Button(act.label ?? act.id, EditorStyles.miniButton))
                                            EnqueueCardAction(a.id, act.id);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private static GUIStyle _cardTitle;
        private static bool _cardTitleProSkin;

        // boldLabel does not wrap, and IMGUI clips the overflow with no ellipsis, so a long card
        // title silently loses its tail. Derive from wordWrappedLabel so the header lays out like
        // the body does, then bold it. Rebuilt on skin change because the base colors are baked in.
        private static GUIStyle CardTitleStyle
        {
            get
            {
                if (_cardTitle == null || _cardTitleProSkin != EditorGUIUtility.isProSkin)
                {
                    _cardTitle = new GUIStyle(EditorStyles.wordWrappedLabel) { fontStyle = FontStyle.Bold };
                    _cardTitleProSkin = EditorGUIUtility.isProSkin;
                }
                return _cardTitle;
            }
        }

        private static GUIContent IconForLevel(string level)
        {
            if (level == "warning")
                return EditorGUIUtility.IconContent("console.warnicon.sml");
            if (level == "success")
                return EditorGUIUtility.IconContent("TestPassed");
            return EditorGUIUtility.IconContent("console.infoicon.sml");
        }

        private static string RelativeTime(string isoUtc)
        {
            if (string.IsNullOrEmpty(isoUtc)) return "";
            DateTime dt;
            if (!DateTime.TryParse(isoUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out dt))
                return "";
            var ago = DateTime.UtcNow - dt.ToUniversalTime();
            if (ago.TotalSeconds < 60) return "just now";
            if (ago.TotalMinutes < 60) return ((int)ago.TotalMinutes) + "m ago";
            if (ago.TotalHours < 24) return ((int)ago.TotalHours) + "h ago";
            return ((int)ago.TotalDays) + "d ago";
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
                    var selCount = Selection.gameObjects != null ? Selection.gameObjects.Length : 0;
                    _attachSelection = GUILayout.Toggle(_attachSelection,
                        "Selection (" + selCount + ")", EditorStyles.miniButton);
                    _attachConsole = GUILayout.Toggle(_attachConsole,
                        "Console errors", EditorStyles.miniButton);
                    _attachScreenshot = GUILayout.Toggle(_attachScreenshot,
                        "Screenshot", EditorStyles.miniButton);
                }

                EditorGUILayout.LabelField("Note", EditorStyles.miniLabel);
                _compose = EditorGUILayout.TextArea(_compose, GUILayout.MinHeight(54));

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Checkpoint label", EditorStyles.miniLabel, GUILayout.Width(110));
                    _checkpointLabel = EditorGUILayout.TextField(_checkpointLabel);
                    GUILayout.FlexibleSpace();
                    var canSend = !string.IsNullOrWhiteSpace(_compose) || _attachSelection || _attachConsole || _attachScreenshot;
                    using (new EditorGUI.DisabledScope(!canSend))
                    {
                        if (GUILayout.Button("Send", GUILayout.Width(90), GUILayout.Height(22))) Send();
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
                var objs = (Selection.gameObjects ?? Array.Empty<GameObject>()).Select(g =>
                {
                    // Avoid ?? on UnityEngine.Object (fake-null pitfall).
                    var comps = g.GetComponents<Component>();
                    var names = comps.Where(c => c != null).Select(c => c.GetType().Name);
                    return new JObject
                    {
                        ["name"] = g.name,
                        ["path"] = GetPath(g.transform),
                        ["components"] = new JArray(names)
                    };
                });
                AdvisorStore.Enqueue("selection", new JObject { ["objects"] = new JArray(objs) }.ToString());
            }

            if (_attachConsole)
            {
                var resp = AutonomousMcpToolDispatcher.HandleReadConsole(
                    new JObject { ["level"] = "error", ["limit"] = 50 });
                var payload = "{}";
                if (resp != null && resp.data != null) payload = resp.data.ToString();
                AdvisorStore.Enqueue("console", payload);
            }

            if (_attachScreenshot)
            {
                // Absolute: the AI's working directory is its own repo, not the Unity project, so a
                // project-relative path here would be unresolvable on the other end.
                var projectRoot = System.IO.Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
                var path = System.IO.Path.Combine(
                    projectRoot, "Temp", "advisor_shot_" + DateTime.UtcNow.Ticks + ".png");
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

        internal static void CreateCheckpoint(string label, string trigger)
        {
            try
            {
                var manifest = CheckpointStore.Create(label, trigger, "editor-ui");
                EditorUtility.DisplayDialog(
                    "Checkpoint created",
                    "id: " + manifest.id +
                    "\nlabel: " + manifest.label +
                    "\nscene: " + manifest.activeScenePath +
                    "\nassets: " + manifest.trackedAssetPaths.Count,
                    "OK");
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Checkpoint failed", ex.Message, "OK");
            }
        }

        private static string GetPath(Transform t)
        {
            var p = t.name;
            while (t.parent != null) { t = t.parent; p = t.name + "/" + p; }
            return p;
        }
    }
}
