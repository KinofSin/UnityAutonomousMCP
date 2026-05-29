using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;

namespace AutonomousMcp.SelfTest
{
    public sealed class McpMutateTests_SceneObjects : McpTestHarness
    {
        // ---- lighting ----
        [Test]
        public void CreateLight_adds_Light()
        {
            AssertOk(Invoke("unity_lighting", new { action = "create_light", name = "T_Light", type = "Point", intensity = 2f }));
            Assert.IsNotNull(GameObject.Find("T_Light")?.GetComponent<Light>());
        }

        [Test]
        public void SetAmbient_then_restore()
        {
            var orig = RenderSettings.ambientLight;
            try
            {
                AssertOk(Invoke("unity_lighting", new { action = "set_ambient", r = 0.1f, g = 0.2f, b = 0.3f }));
                AssertOk(Invoke("unity_lighting", new { action = "get_ambient" }));
            }
            finally { RenderSettings.ambientLight = orig; }
        }

        [Test]
        public void SetSkybox_is_graceful_then_restore()
        {
            var orig = RenderSettings.skybox;
            try
            {
                var r = Invoke("unity_lighting", new { action = "set_skybox", material_path = "Assets/_MCPSelfTest/none.mat" });
                Assert.IsNotNull(r); // success or graceful error both acceptable; must not throw.
                AssertOk(Invoke("unity_lighting", new { action = "get_skybox" }));
            }
            finally { RenderSettings.skybox = orig; }
        }

        // ---- camera ----
        [Test]
        public void CreateCamera_adds_Camera()
        {
            AssertOk(Invoke("unity_camera", new { action = "create", name = "T_Cam", fov = 50f }));
            Assert.IsNotNull(GameObject.Find("T_Cam")?.GetComponent<Camera>());
        }

        [Test]
        public void SceneViewActions_execute()
        {
            new GameObject("T_Focus");
            AssertOk(Invoke("unity_camera", new { action = "sceneview_focus", name = "T_Focus" }));
            AssertOk(Invoke("unity_camera", new { action = "sceneview_pose" }));
        }

        // ---- event (no-arg UnityEvent via Button.onClick -> McpTestTarget.Ping) ----
        [Test]
        public void AddAndRemovePersistent_listener()
        {
            Invoke("unity_ui", new { action = "create_canvas", name = "T_Canvas" });
            Invoke("unity_ui", new { action = "create_button", name = "T_Btn" });
            var target = new GameObject("T_Target");
            target.AddComponent<McpTestTarget>();

            AssertOk(Invoke("unity_event", new {
                action = "add_persistent",
                source = "T_Btn", component_type = "Button", event_field = "onClick",
                target_object = "T_Target", target_component_type = "McpTestTarget", method_name = "Ping"
            }));
            var btn = GameObject.Find("T_Btn").GetComponent<Button>();
            Assert.AreEqual(1, btn.onClick.GetPersistentEventCount());

            AssertOk(Invoke("unity_event", new {
                action = "remove_persistent",
                source = "T_Btn", component_type = "Button", event_field = "onClick", index = 0
            }));
            Assert.AreEqual(0, btn.onClick.GetPersistentEventCount());
        }

        // ---- navmesh (mark floor Navigation Static so bake yields verts) ----
        [Test]
        public void Bake_then_clear_navmesh()
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "T_Floor";
            floor.transform.localScale = new Vector3(5, 1, 5);
            GameObjectUtility.SetStaticEditorFlags(floor, StaticEditorFlags.NavigationStatic);

            AssertOk(Invoke("unity_navmesh", new { action = "bake" }));
            AssertOk(Invoke("unity_navmesh", new { action = "info" }));
            AssertOk(Invoke("unity_navmesh", new { action = "clear" }));
            AssertOk(Invoke("unity_navmesh", new { action = "list_agent_types" }));
        }

        // ---- terrain ----
        [Test]
        public void CreateAndFlatten_terrain()
        {
            AssertOk(Invoke("unity_terrain", new {
                action = "create", name = "T_Terrain",
                asset_path = "Assets/_MCPSelfTest/T_Terrain.asset",
                heightmap_resolution = 33, alphamap_resolution = 32
            }));
            Assert.IsNotNull(GameObject.Find("T_Terrain")?.GetComponent<Terrain>());
            AssertOk(Invoke("unity_terrain", new { action = "flatten", name = "T_Terrain", height = 0f }));
            AssertOk(Invoke("unity_terrain", new { action = "info", name = "T_Terrain" }));
        }

        // ---- cinemachine / timeline (reflection; graceful if package absent) ----
        [Test]
        public void Cinemachine_detect_is_graceful()
        {
            var r = Invoke("unity_cinemachine", new { action = "detect" });
            Assert.IsNotNull(r); // must not throw; package-present -> success, absent -> clean error
        }

        [Test]
        public void Timeline_listdirectors_is_graceful()
        {
            var r = Invoke("unity_timeline", new { action = "list_directors" });
            Assert.IsNotNull(r);
        }
    }
}
