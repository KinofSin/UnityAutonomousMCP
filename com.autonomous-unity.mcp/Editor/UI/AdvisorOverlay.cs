using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using AutonomousMcp.Editor.Advisor;

namespace AutonomousMcp.Editor.UI
{
    // Glanceable Scene-view badge (hybrid placement): shows advice + queued counts and opens the
    // full Advisor panel on click. Hosted as a Unity Editor Overlay so the user can dock/move/hide
    // it from the Scene view's overlay menu.
    [Overlay(typeof(SceneView), "autonomousmcp.advisor", "MCP Advisor", defaultDisplay = true)]
    [Icon("d_console.infoicon")]
    internal sealed class AdvisorOverlay : IMGUIOverlay
    {
        public override void OnGUI()
        {
            var advice = AdvisorStore.GetAdvice().Count;
            var pending = AdvisorStore.PendingCount();
            var label = pending > 0
                ? $"Advisor — {advice} advice · {pending} queued ↑"
                : $"Advisor — {advice} advice";
            if (GUILayout.Button(label))
                AdvisorHudWindow.Open();
        }
    }
}
