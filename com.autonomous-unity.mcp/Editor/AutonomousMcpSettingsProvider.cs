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
            _settings = AutonomousMcpSettings.Load();
            if (_settings.AutoConnect)
            {
                AutonomousMcpConnection.Current.Connect(_settings);
            }
        }
    }
}
