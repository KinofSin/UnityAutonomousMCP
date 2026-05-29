using AutonomousMcp.Editor;
using AutonomousMcp.Editor.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AutonomousMcp.SelfTest
{
    /// <summary>Test-only ScriptableObject used to create a deliberate orphan asset.</summary>
    public sealed class McpTestSO : ScriptableObject { public int dummy; }

    /// <summary>Test-only target with a no-arg method for UnityEvent persistent-listener tests.</summary>
    public sealed class McpTestTarget : MonoBehaviour { public void Ping() { } }

    public abstract class McpTestHarness
    {
        protected const string TestFolder = "Assets/_MCPSelfTest";
        private string _origScenePath;

        [SetUp]
        public void Setup()
        {
            _origScenePath = SceneManager.GetActiveScene().path;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (!AssetDatabase.IsValidFolder(TestFolder))
                AssetDatabase.CreateFolder("Assets", "_MCPSelfTest");
        }

        [TearDown]
        public void Teardown()
        {
            if (AssetDatabase.IsValidFolder(TestFolder))
                AssetDatabase.DeleteAsset(TestFolder);
            if (!string.IsNullOrEmpty(_origScenePath))
                EditorSceneManager.OpenScene(_origScenePath, OpenSceneMode.Single);
            else
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        /// <summary>Resolve a registered tool and invoke its handler; fails the test if unregistered.</summary>
        protected static AutonomousMcpToolResponse Invoke(string tool, object args)
        {
            Assert.IsTrue(ToolRegistry.TryResolve(tool, out var entry), $"Tool '{tool}' not registered.");
            return entry.Handler(JObject.FromObject(args));
        }

        protected static void AssertOk(AutonomousMcpToolResponse r)
        {
            Assert.IsNotNull(r, "Null response.");
            Assert.IsTrue(r.success, r.error);
        }
    }

    public sealed class McpHarnessSmokeTest : McpTestHarness
    {
        [Test]
        public void Registry_resolves_a_known_read_tool()
        {
            var r = Invoke("unity_validation", new { action = "audit_active_scene" });
            AssertOk(r);
        }
    }
}
