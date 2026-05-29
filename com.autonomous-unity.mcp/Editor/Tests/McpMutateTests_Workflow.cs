using NUnit.Framework;

namespace AutonomousMcp.SelfTest
{
    public sealed class McpMutateTests_Workflow : McpTestHarness
    {
        private const string WfName = "_mcp_selftest_wf";

        [Test]
        public void Save_list_load_append_delete()
        {
            AssertOk(Invoke("unity_workflow", new { action = "save", name = WfName, description = "selftest" }));
            AssertOk(Invoke("unity_workflow", new { action = "list" }));
            AssertOk(Invoke("unity_workflow", new { action = "load", name = WfName }));
            AssertOk(Invoke("unity_workflow", new { action = "append_step", name = WfName, tool = "health_check", note = "ping" }));
            AssertOk(Invoke("unity_workflow", new { action = "delete", name = WfName }));
        }
    }
}
