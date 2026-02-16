using UnityEngine;

namespace AutonomousMcp.Runtime
{
    public sealed class AutonomousMcpRuntime : MonoBehaviour
    {
        [SerializeField] private bool enableRuntimeAgent = false;
        [SerializeField] private string activeProfile = "default";

        public bool EnableRuntimeAgent => enableRuntimeAgent;
        public string ActiveProfile => activeProfile;

        private void Awake()
        {
            if (!enableRuntimeAgent)
            {
                return;
            }

            Debug.Log($"[AutonomousMCP] Runtime agent enabled with profile '{activeProfile}'.");
        }
    }
}
