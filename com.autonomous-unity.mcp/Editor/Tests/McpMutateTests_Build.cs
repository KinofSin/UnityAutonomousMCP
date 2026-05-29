using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;

namespace AutonomousMcp.SelfTest
{
    public sealed class McpMutateTests_Build : McpTestHarness
    {
        private static NamedBuildTarget Active =>
            NamedBuildTarget.FromBuildTargetGroup(
                BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget));

        [Test]
        public void Defines_add_then_remove_restores()
        {
            var orig = PlayerSettings.GetScriptingDefineSymbols(Active);
            try
            {
                AssertOk(Invoke("unity_build_manage", new { action = "get_defines" }));
                AssertOk(Invoke("unity_build_manage", new { action = "add_define", define = "MCP_SELFTEST_TMP" }));
                Assert.IsTrue(PlayerSettings.GetScriptingDefineSymbols(Active).Contains("MCP_SELFTEST_TMP"));
                AssertOk(Invoke("unity_build_manage", new { action = "remove_define", define = "MCP_SELFTEST_TMP" }));
                Assert.IsFalse(PlayerSettings.GetScriptingDefineSymbols(Active).Contains("MCP_SELFTEST_TMP"));
            }
            finally { PlayerSettings.SetScriptingDefineSymbols(Active, orig); }
        }

        [Test]
        public void Build_reads_are_safe()
        {
            AssertOk(Invoke("unity_build_manage", new { action = "get_target" }));
            AssertOk(Invoke("unity_build_manage", new { action = "list_targets" }));
            AssertOk(Invoke("unity_build_manage", new { action = "get_scenes" }));
        }

        [Test, Explicit("Switches build platform; slow + global. Run manually.")]
        public void SwitchTarget_roundtrips()
        {
            var orig = EditorUserBuildSettings.activeBuildTarget;
            try
            {
                AssertOk(Invoke("unity_build_manage", new { action = "switch_target", target = orig.ToString() }));
            }
            finally
            {
                if (EditorUserBuildSettings.activeBuildTarget != orig)
                    EditorUserBuildSettings.SwitchActiveBuildTarget(
                        BuildPipeline.GetBuildTargetGroup(orig), orig);
            }
        }
    }
}
