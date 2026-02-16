using Newtonsoft.Json.Linq;

namespace AutonomousMcp.Editor
{
    internal sealed class AutonomousMcpEnvelope
    {
        public string requestId;
        public string tool;
        public JObject @params;
    }

    internal sealed class AutonomousMcpToolResponse
    {
        public bool success;
        public JToken data;
        public string error;
    }
}
