using Newtonsoft.Json.Linq;

namespace AutonomousMcp.Editor
{
    /// <summary>
    /// Inbound MCP envelope. Extended in Phase 1 with client identification fields used
    /// by PermissionStore for Ask/Agent mode enforcement and per-client approvals.
    /// </summary>
    public sealed class AutonomousMcpEnvelope
    {
        public string requestId;
        public string tool;
        public JObject @params;

        /// <summary>Stable client identifier. Optional; defaulted from transport peer when empty.</summary>
        public string clientId;

        /// <summary>Human-readable client name (e.g. "cascade", "claude-code"). Optional.</summary>
        public string clientName;

        /// <summary>Transport name set by the host: "http" | "tcp" | "stdio" | "sse".</summary>
        public string transport;
    }

    public sealed class AutonomousMcpToolResponse
    {
        public bool success;
        public JToken data;
        public string error;

        /// <summary>
        /// Set by the dispatcher on every response so the AI client notices queued Advisor-HUD
        /// sends without a dedicated call: <c>{ "pending": N }</c>. Drain with the hud_poll tool.
        /// </summary>
        public JToken hudOutbox;
    }
}
