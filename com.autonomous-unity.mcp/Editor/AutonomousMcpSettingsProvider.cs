using AutonomousMcp.Editor.UI;
using UnityEditor;
using UnityEngine;

namespace AutonomousMcp.Editor
{
    internal static class AutonomousMcpSettingsProvider
    {
        private static AutonomousMcpSettings _settings;

        [SettingsProvider]
        public static SettingsProvider CreateProvider()
        {
            return new SettingsProvider("Preferences/Autonomous Agent MCP", SettingsScope.User)
            {
                label = "Autonomous Agent MCP",
                guiHandler = _ =>
                {
                    EditorGUILayout.HelpBox(
                        "Autonomous MCP now has a dedicated window with Server, Tools, Logs, Integrations, and Clients tabs.",
                        MessageType.Info);

                    if (GUILayout.Button("Open Autonomous MCP Window", GUILayout.Height(28)))
                    {
                        AutonomousMcpSettingsWindow.Open();
                    }
                },
                keywords = new System.Collections.Generic.HashSet<string>(
                    new[] { "MCP", "Unity", "Autonomous", "AI", "Agent" }
                )
            };
        }

        [InitializeOnLoadMethod]
        private static void TryAutoConnect()
        {
            // Never start the bridge inside headless child processes. Unity's AssetImportWorker
            // runs with -batchMode and also executes [InitializeOnLoadMethod]; if it auto-connects
            // it grabs the default HTTP/TCP port before the interactive Editor can, forcing the
            // real Editor onto a fallback port (8080 -> 8082) and routing all MCP traffic to a
            // worker that has no live Editor/scene state. Only the interactive Editor should host.
            if (Application.isBatchMode)
            {
                return;
            }

            _settings = AutonomousMcpSettings.Load();
            if (_settings.AutoConnect)
            {
                AutonomousMcpConnection.Current.Connect(_settings);
            }
        }
    }
}
