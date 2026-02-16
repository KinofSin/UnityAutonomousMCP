using UnityEditor;
using UnityEngine;

namespace AutonomousMcp.Editor
{
    internal static class AutonomousMcpSettingsProvider
    {
        private static AutonomousMcpSettings _settings;
        private static readonly AutonomousMcpConnection Connection = new AutonomousMcpConnection();

        [SettingsProvider]
        public static SettingsProvider CreateProvider()
        {
            return new SettingsProvider("Preferences/Autonomous Agent MCP", SettingsScope.User)
            {
                label = "Autonomous Agent MCP",
                guiHandler = _ =>
                {
                    _settings ??= AutonomousMcpSettings.Load();

                    EditorGUILayout.LabelField("Connection", EditorStyles.boldLabel);
                    _settings.Host = EditorGUILayout.TextField("Host", _settings.Host);
                    _settings.HttpPort = EditorGUILayout.IntField("HTTP Port", _settings.HttpPort);
                    _settings.TcpPort = EditorGUILayout.IntField("TCP Port", _settings.TcpPort);
                    _settings.AutoConnect = EditorGUILayout.Toggle("Auto Connect", _settings.AutoConnect);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("Save"))
                        {
                            _settings.Save();
                        }

                        if (!Connection.IsConnected)
                        {
                            if (GUILayout.Button("Connect"))
                            {
                                Connection.Connect(_settings);
                            }
                        }
                        else
                        {
                            if (GUILayout.Button("Disconnect"))
                            {
                                Connection.Disconnect();
                            }
                        }
                    }

                    EditorGUILayout.Space();
                    EditorGUILayout.HelpBox(
                        Connection.IsConnected
                            ? "Status: Connected (scaffold mode)"
                            : "Status: Disconnected",
                        Connection.IsConnected ? MessageType.Info : MessageType.Warning
                    );

                    if (!string.IsNullOrEmpty(Connection.LastError))
                    {
                        EditorGUILayout.HelpBox(Connection.LastError, MessageType.Error);
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
                Connection.Connect(_settings);
            }
        }
    }
}
