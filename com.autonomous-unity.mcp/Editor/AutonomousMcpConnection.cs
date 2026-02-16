using UnityEngine;

namespace AutonomousMcp.Editor
{
    internal sealed class AutonomousMcpConnection
    {
        private AutonomousMcpTransportHost _host;

        public bool IsConnected { get; private set; }
        public string LastError { get; private set; } = string.Empty;

        public void Connect(AutonomousMcpSettings settings)
        {
            LastError = string.Empty;

            if (string.IsNullOrWhiteSpace(settings.Host) || settings.HttpPort <= 0 || settings.TcpPort <= 0)
            {
                LastError = "Invalid host/httpPort/tcpPort settings.";
                IsConnected = false;
                return;
            }

            try
            {
                _host?.Dispose();
                _host = new AutonomousMcpTransportHost(settings);
                _host.Start();
                IsConnected = true;
                Debug.Log($"[AutonomousMCP] Bridge online. HTTP={settings.Host}:{settings.HttpPort}, TCP={settings.Host}:{settings.TcpPort}");
            }
            catch (System.Exception ex)
            {
                LastError = ex.Message;
                IsConnected = false;
            }
        }

        public void Disconnect()
        {
            if (!IsConnected)
            {
                return;
            }

            _host?.Dispose();
            _host = null;
            IsConnected = false;
            Debug.Log("[AutonomousMCP] Disconnected.");
        }
    }
}
