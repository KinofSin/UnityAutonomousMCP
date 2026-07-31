using UnityEditor;
using UnityEngine;

namespace AutonomousMcp.Editor
{
    /// <summary>
    /// Command-line entry points for provisioning an editor an agent can drive unattended.
    ///
    /// The bridge only starts on load when AutoConnect is set, and that flag lives in EditorPrefs —
    /// per-user editor state with no file on disk for an external supervisor to edit. Left off, an
    /// unattended launch produces an editor that is running and permanently unreachable, which from
    /// the outside is indistinguishable from a hung launch. Run once per machine:
    ///
    ///   Unity -batchmode -quit -projectPath &lt;project&gt; \
    ///         -executeMethod AutonomousMcp.Editor.AutonomousMcpBootstrap.EnableAutoConnect
    ///
    /// Batch mode is deliberate: TryAutoConnect skips headless processes, so writing the pref here
    /// cannot race the interactive editor for the port. EditorPrefs are per-user rather than
    /// per-project, so the GUI editor picks the change up on its next launch.
    /// </summary>
    public static class AutonomousMcpBootstrap
    {
        public static void EnableAutoConnect()
        {
            var settings = AutonomousMcpSettings.Load();
            settings.AutoConnect = true;
            settings.Save();
            Debug.Log($"[AutonomousMCP] AutoConnect enabled (host {settings.Host}, http {settings.HttpPort}, tcp {settings.TcpPort}).");
        }

        public static void DisableAutoConnect()
        {
            var settings = AutonomousMcpSettings.Load();
            settings.AutoConnect = false;
            settings.Save();
            Debug.Log("[AutonomousMCP] AutoConnect disabled.");
        }

        public static void PrintStatus()
        {
            var settings = AutonomousMcpSettings.Load();
            Debug.Log($"[AutonomousMCP] autoConnect={settings.AutoConnect} host={settings.Host} " +
                      $"http={settings.HttpPort} tcp={settings.TcpPort}");
        }
    }
}
