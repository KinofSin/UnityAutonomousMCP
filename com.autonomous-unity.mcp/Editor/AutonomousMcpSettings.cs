using UnityEditor;

namespace AutonomousMcp.Editor
{
    internal sealed class AutonomousMcpSettings
    {
        private const string HostKey = "AutonomousMcp.Host";
        private const string HttpPortKey = "AutonomousMcp.HttpPort";
        private const string TcpPortKey = "AutonomousMcp.TcpPort";
        private const string AutoConnectKey = "AutonomousMcp.AutoConnect";

        public string Host { get; set; } = "127.0.0.1";
        public int HttpPort { get; set; } = 8080;
        public int TcpPort { get; set; } = 8081;
        public bool AutoConnect { get; set; } = false;

        public static AutonomousMcpSettings Load()
        {
            return new AutonomousMcpSettings
            {
                Host = EditorPrefs.GetString(HostKey, "127.0.0.1"),
                HttpPort = EditorPrefs.GetInt(HttpPortKey, 8080),
                TcpPort = EditorPrefs.GetInt(TcpPortKey, 8081),
                AutoConnect = EditorPrefs.GetBool(AutoConnectKey, false)
            };
        }

        public void Save()
        {
            EditorPrefs.SetString(HostKey, Host);
            EditorPrefs.SetInt(HttpPortKey, HttpPort);
            EditorPrefs.SetInt(TcpPortKey, TcpPort);
            EditorPrefs.SetBool(AutoConnectKey, AutoConnect);
        }
    }
}
