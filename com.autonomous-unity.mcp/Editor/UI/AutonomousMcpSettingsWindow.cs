using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using AutonomousMcp.Editor.Core;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace AutonomousMcp.Editor.UI
{
    internal sealed class AutonomousMcpSettingsWindow : EditorWindow
    {
        private const string TabPrefKey = "AutonomousMcp.UI.SelectedTab";
        private const string FoldoutPrefPrefix = "AutonomousMcp.UI.Tools.Foldout.";

        private static readonly string[] TabLabels = { "Server", "Tools", "Logs", "Integrations", "Clients", "Checkpoints", "Generators", "Skills" };

        private static AutonomousMcpConnection ConnectionSingleton => AutonomousMcpConnection.Current;

        private AutonomousMcpSettings _settings;
        private Vector2 _toolsScroll, _logsScroll, _integrationsScroll, _clientsScroll;
        private Vector2 _checkpointsScroll, _generatorsScroll, _skillsScroll;
        private string _skillFilter = string.Empty;
        private int _selectedTab;
        private double _lastRepaintTime;

        [MenuItem("Window/Autonomous MCP/Settings")]
        public static void Open()
        {
            var window = GetWindow<AutonomousMcpSettingsWindow>(false, "Autonomous MCP", true);
            window.minSize = new Vector2(560, 380);
            window.Show();
        }

        private void OnEnable()
        {
            _settings = AutonomousMcpSettings.Load();
            _selectedTab = Mathf.Clamp(EditorPrefs.GetInt(TabPrefKey, 0), 0, TabLabels.Length - 1);
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
        }

        private void OnEditorUpdate()
        {
            // Repaint roughly every 1s so live tool calls show up on Logs tab.
            if (EditorApplication.timeSinceStartup - _lastRepaintTime > 1.0)
            {
                _lastRepaintTime = EditorApplication.timeSinceStartup;
                Repaint();
            }
        }

        private void OnGUI()
        {
            DrawToolbar();
            EditorGUILayout.Space(2);
            DrawTabSelector();
            EditorGUILayout.Space(4);

            switch (_selectedTab)
            {
                case 0: DrawServerTab(); break;
                case 1: DrawToolsTab(); break;
                case 2: DrawLogsTab(); break;
                case 3: DrawIntegrationsTab(); break;
                case 4: DrawClientsTab(); break;
                case 5: DrawCheckpointsTab(); break;
                case 6: DrawGeneratorsTab(); break;
                case 7: DrawSkillsTab(); break;
            }
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("Autonomous MCP", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();

                var mode = PermissionStore.Mode;
                var previousColor = GUI.contentColor;
                GUI.contentColor = mode == AutonomousMcpMode.Agent ? new Color(0.13f, 0.67f, 0.27f) : new Color(1.0f, 0.65f, 0.0f);
                GUILayout.Label(mode == AutonomousMcpMode.Agent ? "AGENT" : "ASK", EditorStyles.miniLabel);
                GUI.contentColor = previousColor;

                if (GUILayout.Button(mode == AutonomousMcpMode.Agent ? "Switch to Ask" : "Switch to Agent", EditorStyles.toolbarButton, GUILayout.Width(130)))
                {
                    PermissionStore.SetMode(mode == AutonomousMcpMode.Agent ? AutonomousMcpMode.Ask : AutonomousMcpMode.Agent);
                }
            }
        }

        private void DrawTabSelector()
        {
            var next = GUILayout.Toolbar(_selectedTab, TabLabels);
            if (next != _selectedTab)
            {
                _selectedTab = next;
                EditorPrefs.SetInt(TabPrefKey, next);
            }
        }

        // ───────── Server tab ─────────

        private void DrawServerTab()
        {
            EditorGUILayout.LabelField("Connection", EditorStyles.boldLabel);
            _settings.Host = EditorGUILayout.TextField("Host", _settings.Host);
            _settings.HttpPort = EditorGUILayout.IntField("HTTP Port", _settings.HttpPort);
            _settings.TcpPort = EditorGUILayout.IntField("TCP Port", _settings.TcpPort);
            _settings.AutoConnect = EditorGUILayout.Toggle("Auto Connect", _settings.AutoConnect);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Save")) _settings.Save();

                if (!ConnectionSingleton.IsConnected)
                {
                    if (GUILayout.Button("Connect")) ConnectionSingleton.Connect(_settings);
                }
                else
                {
                    if (GUILayout.Button("Disconnect")) ConnectionSingleton.Disconnect();
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                ConnectionSingleton.IsConnected ? "Status: Connected" : "Status: Disconnected",
                ConnectionSingleton.IsConnected ? MessageType.Info : MessageType.Warning);

            if (!string.IsNullOrEmpty(ConnectionSingleton.LastError))
            {
                EditorGUILayout.HelpBox(ConnectionSingleton.LastError, MessageType.Error);
            }
        }

        // ───────── Tools tab ─────────

        private void DrawToolsTab()
        {
            _toolsScroll = EditorGUILayout.BeginScrollView(_toolsScroll);

            var byCategory = new Dictionary<string, List<(string name, string mode, string description, string source)>>();
            foreach (var entry in ToolRegistry.All())
            {
                var key = entry.Category.ToString();
                if (!byCategory.TryGetValue(key, out var list))
                {
                    list = new List<(string name, string mode, string description, string source)>();
                    byCategory[key] = list;
                }
                list.Add((entry.Name, entry.Mode.ToString(), entry.Description ?? string.Empty, "registry"));
            }
            foreach (var legacy in AutonomousMcpToolDispatcher.LegacyToolNames)
            {
                const string legacyCat = "Editor";
                if (!byCategory.TryGetValue(legacyCat, out var list))
                {
                    list = new List<(string name, string mode, string description, string source)>();
                    byCategory[legacyCat] = list;
                }
                list.Add((legacy, "Mutate", string.Empty, "legacy_switch"));
            }

            foreach (var category in byCategory.Keys.OrderBy(c => c, StringComparer.Ordinal))
            {
                var tools = byCategory[category];
                var foldKey = FoldoutPrefPrefix + category;
                var isOpen = EditorPrefs.GetBool(foldKey, true);
                var header = $"{category}  ({tools.Count})";
                var next = EditorGUILayout.Foldout(isOpen, header, true, EditorStyles.foldoutHeader);
                if (next != isOpen) EditorPrefs.SetBool(foldKey, next);
                if (!next) continue;

                using (new EditorGUI.IndentLevelScope())
                {
                    foreach (var t in tools.OrderBy(x => x.name, StringComparer.Ordinal))
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            EditorGUILayout.LabelField(t.name, EditorStyles.miniBoldLabel, GUILayout.Width(260));
                            EditorGUILayout.LabelField(t.mode, EditorStyles.miniLabel, GUILayout.Width(80));
                            var desc = string.IsNullOrEmpty(t.description) ? $"({t.source})" : Truncate(t.description, 110);
                            EditorGUILayout.LabelField(desc, EditorStyles.miniLabel);
                        }
                    }
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max) return s ?? string.Empty;
            return s.Substring(0, max - 1) + "…";
        }

        // ───────── Logs tab ─────────

        private void DrawLogsTab()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Clear", GUILayout.Width(80)))
                {
                    AutonomousMcpLogStore.ClearToolCalls();
                }
                if (GUILayout.Button("Copy all (TSV)", GUILayout.Width(120)))
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("timestamp\ttool\tcategory\tduration_ms\tok\terror");
                    foreach (var entry in AutonomousMcpLogStore.ReadToolCalls(200))
                    {
                        sb.Append(entry.TimestampUtc).Append('\t')
                          .Append(entry.Tool).Append('\t')
                          .Append(entry.Category).Append('\t')
                          .Append(entry.DurationMs).Append('\t')
                          .Append(entry.Success ? "ok" : "err").Append('\t')
                          .AppendLine(entry.Error?.Replace('\t', ' ') ?? string.Empty);
                    }
                    EditorGUIUtility.systemCopyBuffer = sb.ToString();
                }
                GUILayout.FlexibleSpace();
            }

            _logsScroll = EditorGUILayout.BeginScrollView(_logsScroll);

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                EditorGUILayout.LabelField("Time (UTC)", EditorStyles.miniBoldLabel, GUILayout.Width(170));
                EditorGUILayout.LabelField("Tool", EditorStyles.miniBoldLabel, GUILayout.Width(200));
                EditorGUILayout.LabelField("Duration", EditorStyles.miniBoldLabel, GUILayout.Width(80));
                EditorGUILayout.LabelField("Result", EditorStyles.miniBoldLabel, GUILayout.Width(60));
                EditorGUILayout.LabelField("Error", EditorStyles.miniBoldLabel);
            }

            foreach (var entry in AutonomousMcpLogStore.ReadToolCalls(200))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(ShortTimestamp(entry.TimestampUtc), EditorStyles.miniLabel, GUILayout.Width(170));
                    EditorGUILayout.LabelField(entry.Tool, EditorStyles.miniLabel, GUILayout.Width(200));
                    EditorGUILayout.LabelField($"{entry.DurationMs} ms", EditorStyles.miniLabel, GUILayout.Width(80));

                    var prev = GUI.contentColor;
                    GUI.contentColor = entry.Success ? new Color(0.13f, 0.67f, 0.27f) : new Color(0.85f, 0.25f, 0.25f);
                    EditorGUILayout.LabelField(entry.Success ? "ok" : "err", EditorStyles.miniLabel, GUILayout.Width(60));
                    GUI.contentColor = prev;

                    EditorGUILayout.LabelField(Truncate(entry.Error, 200), EditorStyles.miniLabel);
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private static string ShortTimestamp(string iso)
        {
            if (string.IsNullOrEmpty(iso)) return string.Empty;
            // 2026-05-28T12:34:56.7890123Z -> 2026-05-28 12:34:56
            return iso.Length >= 19 ? iso.Substring(0, 19).Replace('T', ' ') : iso;
        }

        // ───────── Integrations tab ─────────

        private static readonly (string label, string commandHint)[] IntegrationTargets =
        {
            ("Claude Code", "Claude Code"),
            ("Claude Desktop", "Claude Desktop"),
            ("Cursor", "Cursor"),
        };

        private void DrawIntegrationsTab()
        {
            _integrationsScroll = EditorGUILayout.BeginScrollView(_integrationsScroll);

            var distPath = ResolveServerDistPath();
            if (string.IsNullOrEmpty(distPath))
            {
                EditorGUILayout.HelpBox(
                    "Could not find server/dist/index.js. Run `npm install && npm --workspace server run build` from the repo root first.",
                    MessageType.Warning);
            }

            foreach (var target in IntegrationTargets)
            {
                EditorGUILayout.LabelField(target.label, EditorStyles.boldLabel);
                var snippet = BuildSnippet(target.label, distPath);

                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.TextArea(snippet, GUILayout.MinHeight(90));
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Copy snippet", GUILayout.Width(140)))
                    {
                        EditorGUIUtility.systemCopyBuffer = snippet;
                    }
                    if (GUILayout.Button("Reveal config folder", GUILayout.Width(180)))
                    {
                        var folder = ResolveConfigFolder(target.label);
                        if (!string.IsNullOrEmpty(folder))
                        {
                            try { if (!Directory.Exists(folder)) Directory.CreateDirectory(folder); }
                            catch { /* best-effort */ }
                            EditorUtility.RevealInFinder(folder);
                        }
                    }
                    GUILayout.FlexibleSpace();
                }
                EditorGUILayout.Space(8);
            }

            EditorGUILayout.EndScrollView();
        }

        private static string BuildSnippet(string client, string distPath)
        {
            var pathLiteral = string.IsNullOrEmpty(distPath) ? "/path/to/server/dist/index.js" : distPath.Replace("\\", "\\\\");
            return "{\n" +
                   "  \"mcpServers\": {\n" +
                   "    \"autonomous-unity\": {\n" +
                   "      \"command\": \"node\",\n" +
                   "      \"args\": [\"" + pathLiteral + "\", \"--mcp\"]\n" +
                   "    }\n" +
                   "  }\n" +
                   "}";
        }

        private static string ResolveServerDistPath()
        {
            var projectRoot = Path.GetDirectoryName(Application.dataPath) ?? string.Empty;

            // Candidates: repo-root sibling layout (Assets next to server/), or package-embedded layout.
            var candidates = new[]
            {
                Path.Combine(projectRoot, "server", "dist", "index.js"),
                Path.Combine(projectRoot, "..", "server", "dist", "index.js"),
                Path.Combine(projectRoot, "Packages", "com.autonomous.unity.mcp", "server", "dist", "index.js"),
            };

            foreach (var candidate in candidates)
            {
                try
                {
                    var full = Path.GetFullPath(candidate);
                    if (File.Exists(full)) return full;
                }
                catch { /* ignore malformed paths */ }
            }
            return string.Empty;
        }

        private static string ResolveConfigFolder(string client)
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            bool isMac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
            bool isWin = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

            switch (client)
            {
                case "Claude Code":
                    if (isMac) return Path.Combine(home, "Library", "Application Support", "Claude Code");
                    if (isWin) return Path.Combine(appData, "Claude Code");
                    return Path.Combine(home, ".config", "claude-code");
                case "Claude Desktop":
                    if (isMac) return Path.Combine(home, "Library", "Application Support", "Claude");
                    if (isWin) return Path.Combine(appData, "Claude");
                    return Path.Combine(home, ".config", "Claude");
                case "Cursor":
                    return Path.Combine(home, ".cursor");
            }
            return string.Empty;
        }

        // ───────── Clients tab ─────────

        private void DrawClientsTab()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Approve all pending", GUILayout.Width(180)))
                {
                    PermissionStore.BulkSetState("approved");
                }
                if (GUILayout.Button("Deny all pending", GUILayout.Width(160)))
                {
                    PermissionStore.BulkSetState("denied");
                }
                GUILayout.FlexibleSpace();
            }

            PermissionStore.AutoApproveNewClients = EditorGUILayout.ToggleLeft(
                "Auto-approve new clients",
                PermissionStore.AutoApproveNewClients);

            EditorGUILayout.Space(4);

            _clientsScroll = EditorGUILayout.BeginScrollView(_clientsScroll);

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                EditorGUILayout.LabelField("Name", EditorStyles.miniBoldLabel, GUILayout.Width(160));
                EditorGUILayout.LabelField("Transport", EditorStyles.miniBoldLabel, GUILayout.Width(80));
                EditorGUILayout.LabelField("State", EditorStyles.miniBoldLabel, GUILayout.Width(80));
                EditorGUILayout.LabelField("Last seen", EditorStyles.miniBoldLabel, GUILayout.Width(160));
                EditorGUILayout.LabelField("Actions", EditorStyles.miniBoldLabel);
            }

            foreach (var client in PermissionStore.ListClients())
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(client.Name ?? client.Id, EditorStyles.miniLabel, GUILayout.Width(160));
                    EditorGUILayout.LabelField(client.Transport ?? string.Empty, EditorStyles.miniLabel, GUILayout.Width(80));

                    var prev = GUI.contentColor;
                    GUI.contentColor = StateColor(client.State);
                    EditorGUILayout.LabelField(client.State ?? "?", EditorStyles.miniLabel, GUILayout.Width(80));
                    GUI.contentColor = prev;

                    EditorGUILayout.LabelField(ShortTimestamp(client.LastSeenUtc), EditorStyles.miniLabel, GUILayout.Width(160));

                    if (GUILayout.Button("Approve", EditorStyles.miniButton, GUILayout.Width(70)))
                    {
                        PermissionStore.ApproveClient(client.Id);
                    }
                    if (GUILayout.Button("Deny", EditorStyles.miniButton, GUILayout.Width(60)))
                    {
                        PermissionStore.DenyClient(client.Id);
                    }
                    if (GUILayout.Button("Revoke", EditorStyles.miniButton, GUILayout.Width(70)))
                    {
                        PermissionStore.RevokeClient(client.Id);
                    }
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private static Color StateColor(string state)
        {
            switch (state)
            {
                case "approved": return new Color(0.13f, 0.67f, 0.27f);
                case "denied": return new Color(0.85f, 0.25f, 0.25f);
                case "revoked": return new Color(0.85f, 0.25f, 0.25f);
                case "pending":
                default: return new Color(1.0f, 0.65f, 0.0f);
            }
        }

        // ───────── Checkpoints tab ─────────

        private void DrawCheckpointsTab()
        {
            var list = CheckpointStore.List();
            var totalKb = CheckpointStore.TotalDiskUsageBytes() / 1024.0;
            EditorGUILayout.LabelField($"{list.Count} stored · {totalKb:0.#} KB total", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Refresh", GUILayout.Width(90))) Repaint();
                if (GUILayout.Button("Open folder", GUILayout.Width(110)))
                    EditorUtility.RevealInFinder(CheckpointStore.RootDirectory);
                if (GUILayout.Button("Delete all", GUILayout.Width(90)) &&
                    EditorUtility.DisplayDialog("Delete all checkpoints?", "Remove every saved checkpoint?", "Delete", "Cancel"))
                    CheckpointStore.DeleteAll();
                GUILayout.FlexibleSpace();
            }
            EditorGUILayout.Space(4);

            _checkpointsScroll = EditorGUILayout.BeginScrollView(_checkpointsScroll);
            foreach (var cp in list)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField($"{cp.id}  ·  {cp.label}", EditorStyles.miniBoldLabel);
                    var kb = CheckpointStore.SizeOf(cp.id) / 1024.0;
                    var scene = string.IsNullOrEmpty(cp.activeScenePath) ? "(none)" : cp.activeScenePath;
                    EditorGUILayout.LabelField($"{ShortTimestamp(cp.createdUtc)} · {kb:0.#} KB · scene={scene}", EditorStyles.miniLabel);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("Restore", EditorStyles.miniButton, GUILayout.Width(70)) &&
                            EditorUtility.DisplayDialog($"Restore {cp.id}?", "Replace current scene with checkpoint?", "Restore", "Cancel"))
                        {
                            if (!CheckpointStore.Restore(cp.id, out var err))
                                Debug.LogError($"[AutonomousMCP] Restore failed: {err}");
                        }
                        if (GUILayout.Button("Diff", EditorStyles.miniButton, GUILayout.Width(60)))
                            Debug.Log(CheckpointStore.Diff(cp.id));
                        if (GUILayout.Button("Delete", EditorStyles.miniButton, GUILayout.Width(70)))
                        {
                            if (!CheckpointStore.Delete(cp.id, out var err))
                                Debug.LogError($"[AutonomousMCP] Delete failed: {err}");
                        }
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }

        // ───────── Generators tab ─────────

        private void DrawGeneratorsTab()
        {
            EditorGUILayout.HelpBox(
                "Stub providers ship for every kind. API keys are read from GENERATOR_* env vars at request time, never stored.",
                MessageType.Info);

            var data = GeneratorConfig.Data;
            var newOut = EditorGUILayout.TextField("Default output dir", data.defaultOutputDirectory);
            if (newOut != data.defaultOutputDirectory && newOut.StartsWith("Assets/", StringComparison.Ordinal))
            {
                data.defaultOutputDirectory = newOut.TrimEnd('/');
                GeneratorConfig.Save();
            }

            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField("Environment detection", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                foreach (var n in new[] { "GENERATOR_API_KEY", "GENERATOR_OPENAI_API_KEY", "GENERATOR_ANTHROPIC_API_KEY", "GENERATOR_LOCAL_LLM_URL" })
                {
                    var present = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(n));
                    EditorGUILayout.LabelField($"{n}: {(present ? "set" : "(missing)")}", EditorStyles.miniLabel);
                }
            }

            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField($"Registered generators: {GeneratorRegistry.Count}", EditorStyles.boldLabel);
            _generatorsScroll = EditorGUILayout.BeginScrollView(_generatorsScroll);
            foreach (var g in GeneratorRegistry.List())
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField($"{g.Kind} · {g.ProviderId}", EditorStyles.miniBoldLabel);
                    EditorGUILayout.LabelField($"{(g.IsConfigured() ? "ready" : "not configured")} — {Truncate(g.GetStatus(), 140)}", EditorStyles.miniLabel);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        // ───────── Skills tab ─────────

        private void DrawSkillsTab()
        {
            _skillFilter = EditorGUILayout.TextField("Filter", _skillFilter);

            var path = ResolveSkillsIndexPath();
            if (string.IsNullOrEmpty(path))
            {
                EditorGUILayout.HelpBox("Skills/index.json not found in the package.", MessageType.Warning);
                return;
            }

            JArray skills;
            try { skills = (JArray)JObject.Parse(File.ReadAllText(path))["skills"]; }
            catch (Exception e) { EditorGUILayout.HelpBox($"Parse error: {e.Message}", MessageType.Error); return; }
            if (skills == null) { EditorGUILayout.HelpBox("No 'skills' array in index.json.", MessageType.Warning); return; }

            EditorGUILayout.LabelField($"{skills.Count} skills", EditorStyles.boldLabel);
            _skillsScroll = EditorGUILayout.BeginScrollView(_skillsScroll);
            foreach (var s in skills)
            {
                var id = (string)s["id"];
                var name = (string)s["name"];
                if (!string.IsNullOrEmpty(_skillFilter) &&
                    (id ?? string.Empty).IndexOf(_skillFilter, StringComparison.OrdinalIgnoreCase) < 0 &&
                    (name ?? string.Empty).IndexOf(_skillFilter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField($"{id} — {name}", EditorStyles.miniBoldLabel);
                    EditorGUILayout.LabelField($"category: {(string)s["category"]}", EditorStyles.miniLabel);
                    EditorGUILayout.LabelField(Truncate((string)s["description"], 160), EditorStyles.wordWrappedMiniLabel);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private static string ResolveSkillsIndexPath()
        {
            var projectRoot = Path.GetDirectoryName(Application.dataPath) ?? string.Empty;
            var candidates = new[]
            {
                Path.Combine(projectRoot, "Packages", "com.autonomous-unity.mcp", "Skills", "index.json"),
                Path.Combine(projectRoot, "..", "com.autonomous-unity.mcp", "Skills", "index.json"),
                Path.Combine(projectRoot, "com.autonomous-unity.mcp", "Skills", "index.json"),
            };
            foreach (var c in candidates)
            {
                try { var full = Path.GetFullPath(c); if (File.Exists(full)) return full; }
                catch { /* ignore */ }
            }
            return string.Empty;
        }
    }
}
