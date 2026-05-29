# Mutate Write-Path Self-Test Harness — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **Commit locally to `feat/unified-mcp`. Do NOT push** (private remote push is the user's manual step).

**Goal:** A reusable Unity EditMode test suite that exercises every mutating action of the 13 Mutate tool families against a disposable temp surface, asserting both the tool response and the real side-effect, leaving the project pristine.

**Architecture:** A new EditMode test assembly under the package. Tests invoke the real registered path (`ToolRegistry.TryResolve(name).Handler(args)`) and assert side-effects via the Unity API. A base class manages a temp scene + `Assets/_MCPSelfTest/` folder and restores any mutated global state. Re-runnable via Unity Test Runner or over MCP (`run_tests {mode:"editmode"}` → `get_test_job`).

**Tech Stack:** Unity 2022.3.22f1 EditMode tests (NUnit via `com.unity.test-framework`), C#, Newtonsoft.Json (auto-referenced), `UnityEngine.UI`.

---

## Verified facts (from reading the tool sources)

- Tools resolve via `ToolRegistry.TryResolve(string name, out RegistryEntry entry)`; invoke `entry.Handler(JObject)` → `AutonomousMcpToolResponse { bool success; JToken data; string error; }`.
- GameObject-targeting tools resolve by `name` (string, `GameObject.Find`) or `instanceId` (int).
- Exact action + param names per family are embedded in each task's test code below (read from source, not guessed).
- `unity_cinemachine` / `unity_timeline` default action is `detect`; both are reflection-based and return `success:false` with a "package not detected" message when the package is absent (graceful — assert accordingly).
- `delete_orphans` / `delete_empty_folders` take `folder` (default `"Assets"`) + require `confirm:true`.
- `unity_importer set_property` takes `asset_path`, `property_path`, and `value` (`args["value"]`); operates on `new SerializedObject(importer)`.
- `unity_event add_persistent` supports only no-arg `UnityEvent`; params `source`, `event_field`, `component_type`, `target_object`, `target_component_type`, `method_name`.

## Testing-loop note (tests validate existing code)

The tools already exist; these tests *encode correct 2022.3 behavior*. The loop per family: write the test file → let Unity recompile → run EditMode tests → **if a test is red, fix the tool source** (not the test) → re-run green → commit. This is the same root-cause loop that fixed the 5 first-compile bugs.

**Running tests during execution:** Unity must recompile after each new test file. With the editor open and the MCP bridge connected (Agent mode + autoApproveMutate), run over the bridge:
```
refresh_unity {}                 # force recompile so new tests register
run_tests {"mode":"editmode"}    # returns jobId
get_test_job {"jobId":"<id>"}    # poll until status terminal; inspect per-test results
```
Alternatively open **Window > General > Test Runner > EditMode > Run All**. Filter to the `AutonomousMcp.SelfTest` namespace to skip the project's other tests.

## File structure

```
com.autonomous-unity.mcp/Editor/Tests/
  AutonomousMcp.Editor.Tests.asmdef        # EditMode test assembly
  McpTestHarness.cs                        # base: SetUp/TearDown + Invoke()/AssertOk() + McpTestSO/McpTestTarget helpers
  McpMutateTests_UI.cs                     # unity_ui (7 actions)
  McpMutateTests_Physics.cs                # unity_physics (6)
  McpMutateTests_SceneObjects.cs           # lighting, camera, event, navmesh, terrain, cinemachine, timeline
  McpMutateTests_Assets.cs                 # importer, cleaner (scoped)
  McpMutateTests_Build.cs                  # build defines + [Explicit] switch_target
  McpMutateTests_Workflow.cs               # unity_workflow
```

All test code is in namespace `AutonomousMcp.SelfTest`.

---

## Task 1: Test assembly + harness base + first smoke test

**Files:**
- Create: `com.autonomous-unity.mcp/Editor/Tests/AutonomousMcp.Editor.Tests.asmdef`
- Create: `com.autonomous-unity.mcp/Editor/Tests/McpTestHarness.cs`

- [ ] **Step 1: Create the test asmdef**

`AutonomousMcp.Editor.Tests.asmdef`:
```json
{
  "name": "AutonomousMcp.Editor.Tests",
  "references": [
    "AutonomousMcp.Editor",
    "UnityEngine.UI",
    "UnityEngine.TestRunner",
    "UnityEditor.TestRunner"
  ],
  "includePlatforms": ["Editor"],
  "optionalUnityReferences": ["TestAssemblies"],
  "overrideReferences": false,
  "autoReferenced": false,
  "defineConstraints": ["UNITY_INCLUDE_TESTS"],
  "noEngineReferences": false
}
```

- [ ] **Step 2: Write the harness base + shared helpers + one smoke test**

`McpTestHarness.cs`:
```csharp
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
```

- [ ] **Step 3: Recompile + run the smoke test**

Run (over bridge): `refresh_unity {}` then `run_tests {"mode":"editmode"}` then poll `get_test_job`.
Expected: `AutonomousMcp.SelfTest.McpHarnessSmokeTest.Registry_resolves_a_known_read_tool` → **passed**. (Confirms asmdef compiles, harness works, registry reachable from tests.)

- [ ] **Step 4: Commit**

```bash
git add com.autonomous-unity.mcp/Editor/Tests/AutonomousMcp.Editor.Tests.asmdef \
        com.autonomous-unity.mcp/Editor/Tests/McpTestHarness.cs
git commit -m "test: add EditMode self-test assembly + harness base + smoke test"
```

---

## Task 2: unity_ui write-path tests

**Files:**
- Create: `com.autonomous-unity.mcp/Editor/Tests/McpMutateTests_UI.cs`

- [ ] **Step 1: Write the UI tests**

```csharp
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace AutonomousMcp.SelfTest
{
    public sealed class McpMutateTests_UI : McpTestHarness
    {
        [Test]
        public void CreateCanvas_makes_a_Canvas()
        {
            AssertOk(Invoke("unity_ui", new { action = "create_canvas", name = "T_Canvas" }));
            Assert.IsNotNull(GameObject.Find("T_Canvas")?.GetComponent<Canvas>());
        }

        [Test]
        public void CreatePanel_makes_an_Image()
        {
            Invoke("unity_ui", new { action = "create_canvas", name = "T_Canvas" });
            AssertOk(Invoke("unity_ui", new { action = "create_panel", name = "T_Panel" }));
            Assert.IsNotNull(GameObject.Find("T_Panel")?.GetComponent<Image>());
        }

        [Test]
        public void CreateButton_makes_a_Button()
        {
            Invoke("unity_ui", new { action = "create_canvas", name = "T_Canvas" });
            AssertOk(Invoke("unity_ui", new { action = "create_button", name = "T_Btn", label = "Hi" }));
            Assert.IsNotNull(GameObject.Find("T_Btn")?.GetComponent<Button>());
        }

        [Test]
        public void CreateText_makes_a_Text()
        {
            Invoke("unity_ui", new { action = "create_canvas", name = "T_Canvas" });
            AssertOk(Invoke("unity_ui", new { action = "create_text", name = "T_Txt", text = "Hello" }));
            Assert.IsNotNull(GameObject.Find("T_Txt")?.GetComponent<Text>());
        }

        [Test]
        public void CreateImage_makes_an_Image()
        {
            Invoke("unity_ui", new { action = "create_canvas", name = "T_Canvas" });
            AssertOk(Invoke("unity_ui", new { action = "create_image", name = "T_Img" }));
            Assert.IsNotNull(GameObject.Find("T_Img")?.GetComponent<Image>());
        }

        [Test]
        public void SetAnchor_updates_anchorMin()
        {
            Invoke("unity_ui", new { action = "create_canvas", name = "T_Canvas" });
            Invoke("unity_ui", new { action = "create_image", name = "T_Img" });
            AssertOk(Invoke("unity_ui", new { action = "set_anchor", name = "T_Img", min_x = 0.25f, min_y = 0.25f, max_x = 0.75f, max_y = 0.75f }));
            var rt = GameObject.Find("T_Img").GetComponent<RectTransform>();
            Assert.AreEqual(0.25f, rt.anchorMin.x, 0.001f);
            Assert.AreEqual(0.75f, rt.anchorMax.y, 0.001f);
        }

        [Test]
        public void SetRect_executes()
        {
            Invoke("unity_ui", new { action = "create_canvas", name = "T_Canvas" });
            Invoke("unity_ui", new { action = "create_image", name = "T_Img" });
            AssertOk(Invoke("unity_ui", new { action = "set_rect", name = "T_Img" }));
        }
    }
}
```

- [ ] **Step 2: Recompile + run; fix tool source if any test is red; re-run green**

Run: `refresh_unity {}` → `run_tests {"mode":"editmode"}` → `get_test_job`.
Expected: all 7 `McpMutateTests_UI` tests pass. If `create_button` fails on `label` or a Text/Image type mismatch, fix in `UnityUITool.cs` and re-run. (`unity_ui` uses classic uGUI — Text, not TMP — verified.)

- [ ] **Step 3: Commit**

```bash
git add com.autonomous-unity.mcp/Editor/Tests/McpMutateTests_UI.cs
git commit -m "test: unity_ui write-path coverage (7 actions)"
```

---

## Task 3: unity_physics write-path tests

**Files:**
- Create: `com.autonomous-unity.mcp/Editor/Tests/McpMutateTests_Physics.cs`

- [ ] **Step 1: Write the physics tests (gravity/layer captured + restored)**

```csharp
using NUnit.Framework;
using UnityEngine;

namespace AutonomousMcp.SelfTest
{
    public sealed class McpMutateTests_Physics : McpTestHarness
    {
        [Test]
        public void AddRigidbody_adds_component()
        {
            var go = new GameObject("T_Phys");
            AssertOk(Invoke("unity_physics", new { action = "add_rigidbody", name = "T_Phys", mass = 2f }));
            Assert.IsNotNull(go.GetComponent<Rigidbody>());
            Assert.AreEqual(2f, go.GetComponent<Rigidbody>().mass, 0.001f);
        }

        [Test]
        public void AddCollider_box_adds_BoxCollider()
        {
            var go = new GameObject("T_Col");
            AssertOk(Invoke("unity_physics", new { action = "add_collider", name = "T_Col", type = "box" }));
            Assert.IsNotNull(go.GetComponent<BoxCollider>());
        }

        [Test]
        public void AddCollider_sphere_capsule_mesh()
        {
            var go = new GameObject("T_Col2");
            go.AddComponent<MeshFilter>().sharedMesh = new Mesh();
            AssertOk(Invoke("unity_physics", new { action = "add_collider", name = "T_Col2", type = "sphere" }));
            AssertOk(Invoke("unity_physics", new { action = "add_collider", name = "T_Col2", type = "capsule" }));
            AssertOk(Invoke("unity_physics", new { action = "add_collider", name = "T_Col2", type = "mesh" }));
            Assert.IsNotNull(go.GetComponent<SphereCollider>());
            Assert.IsNotNull(go.GetComponent<CapsuleCollider>());
            Assert.IsNotNull(go.GetComponent<MeshCollider>());
        }

        [Test]
        public void SetGravity_then_restore()
        {
            var orig = Physics.gravity;
            try
            {
                AssertOk(Invoke("unity_physics", new { action = "set_gravity", x = 0f, y = -5f, z = 0f }));
                Assert.AreEqual(-5f, Physics.gravity.y, 0.01f);
            }
            finally { Physics.gravity = orig; }
        }

        [Test]
        public void GetGravity_and_settings_read()
        {
            AssertOk(Invoke("unity_physics", new { action = "get_gravity" }));
            AssertOk(Invoke("unity_physics", new { action = "get_physics_settings" }));
        }

        [Test]
        public void SetIgnoreLayerCollision_then_restore()
        {
            bool orig = Physics.GetIgnoreLayerCollision(8, 9);
            try
            {
                AssertOk(Invoke("unity_physics", new { action = "set_ignore_layer_collision", layer_a = 8, layer_b = 9, ignore = true }));
                Assert.IsTrue(Physics.GetIgnoreLayerCollision(8, 9));
            }
            finally { Physics.IgnoreLayerCollision(8, 9, orig); }
        }
    }
}
```

- [ ] **Step 2: Recompile + run; fix tool source if red; re-run green**

Expected: all 6 pass. Globals (`Physics.gravity`, layer-collision) restored via `finally`.

- [ ] **Step 3: Commit**

```bash
git add com.autonomous-unity.mcp/Editor/Tests/McpMutateTests_Physics.cs
git commit -m "test: unity_physics write-path coverage (6 actions, globals restored)"
```

---

## Task 4: scene-object tests (lighting, camera, event, navmesh, terrain, cinemachine, timeline)

**Files:**
- Create: `com.autonomous-unity.mcp/Editor/Tests/McpMutateTests_SceneObjects.cs`

- [ ] **Step 1: Write the tests**

```csharp
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
        public void SetSkybox_then_restore()
        {
            var orig = RenderSettings.skybox;
            try
            {
                // No path → tool should report a clear error, not throw.
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

        // ---- event (no-arg UnityEvent via Button.onClick → McpTestTarget.Ping) ----
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
            Assert.IsNotNull(r); // must not throw; package-present → success, absent → clean error
        }

        [Test]
        public void Timeline_listdirectors_is_graceful()
        {
            var r = Invoke("unity_timeline", new { action = "list_directors" });
            Assert.IsNotNull(r);
        }
    }
}
```

- [ ] **Step 2: Recompile + run; fix tool source if red; re-run green**

Expected: all pass. Notes: `set_skybox`/`Cinemachine`/`Timeline` tests assert *non-throwing* (success OR graceful error) because the result depends on assets/packages present. `navmesh` bake asserts success; if `info` shows 0 verts despite the static flag, that's a tool/setup bug to investigate.

- [ ] **Step 3: Commit**

```bash
git add com.autonomous-unity.mcp/Editor/Tests/McpMutateTests_SceneObjects.cs
git commit -m "test: lighting/camera/event/navmesh/terrain/cinemachine/timeline write-paths"
```

---

## Task 5: asset tests (importer, cleaner — scoped to temp folder)

**Files:**
- Create: `com.autonomous-unity.mcp/Editor/Tests/McpMutateTests_Assets.cs`

- [ ] **Step 1: Write the tests**

```csharp
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AutonomousMcp.SelfTest
{
    public sealed class McpMutateTests_Assets : McpTestHarness
    {
        private static string MakeTempTexture()
        {
            var path = TestFolder + "/t_tex.png";
            var tex = new Texture2D(4, 4);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            return path;
        }

        [Test]
        public void Importer_get_then_set_property()
        {
            var path = MakeTempTexture();
            AssertOk(Invoke("unity_importer", new { action = "get_importer_type", asset_path = path }));
            AssertOk(Invoke("unity_importer", new { action = "get_properties", asset_path = path, prefix = "m_" }));
            AssertOk(Invoke("unity_importer", new { action = "set_property", asset_path = path, property_path = "m_MaxTextureSize", value = 512 }));
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            Assert.AreEqual(512, imp.maxTextureSize);
        }

        [Test]
        public void Cleaner_finds_and_deletes_scoped_orphan()
        {
            AssetDatabase.CreateFolder(TestFolder, "Orphans");
            var so = ScriptableObject.CreateInstance<McpTestSO>();
            AssetDatabase.CreateAsset(so, TestFolder + "/Orphans/orphan.asset");
            AssetDatabase.SaveAssets();

            var find = Invoke("unity_cleaner", new { action = "find_orphans", folder = TestFolder + "/Orphans" });
            AssertOk(find);
            AssertOk(Invoke("unity_cleaner", new { action = "delete_orphans", folder = TestFolder + "/Orphans", confirm = true }));
            Assert.IsFalse(File.Exists(TestFolder + "/Orphans/orphan.asset"));
        }

        [Test]
        public void Cleaner_finds_and_deletes_scoped_empty_folder()
        {
            AssetDatabase.CreateFolder(TestFolder, "EmptyDir");
            AssetDatabase.Refresh();
            AssertOk(Invoke("unity_cleaner", new { action = "find_empty_folders", folder = TestFolder }));
            AssertOk(Invoke("unity_cleaner", new { action = "delete_empty_folders", folder = TestFolder, confirm = true }));
            Assert.IsFalse(AssetDatabase.IsValidFolder(TestFolder + "/EmptyDir"));
        }

        [Test]
        public void Cleaner_reads_are_safe()
        {
            AssertOk(Invoke("unity_cleaner", new { action = "find_unused_materials", folder = TestFolder }));
            AssertOk(Invoke("unity_cleaner", new { action = "find_internal_error_shaders", folder = TestFolder }));
        }
    }
}
```

- [ ] **Step 2: Recompile + run; fix tool source if red; re-run green**

Expected: all pass. If `set_property` errors on `m_MaxTextureSize` (wrong serialized name on 2022.3), use the `get_properties` output to pick the correct path and fix the test args (this is a test-data fix, not a tool bug). The `delete_*` tests are scoped to `Assets/_MCPSelfTest/...` + `confirm:true` so no real asset is touched.

- [ ] **Step 3: Commit**

```bash
git add com.autonomous-unity.mcp/Editor/Tests/McpMutateTests_Assets.cs
git commit -m "test: unity_importer + unity_cleaner write-paths (scoped, safe)"
```

---

## Task 6: build tests (defines round-trip; switch_target Explicit)

**Files:**
- Create: `com.autonomous-unity.mcp/Editor/Tests/McpMutateTests_Build.cs`

- [ ] **Step 1: Write the tests**

```csharp
using NUnit.Framework;
using UnityEditor;

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
```

> **Note:** if `add_define`/`get_defines` in the tool use the obsolete `PlayerSettings.GetScriptingDefineSymbolsForGroup(BuildTargetGroup)` API rather than `NamedBuildTarget`, the test still works (both read the same underlying defines). Only adjust if a test goes red.

- [ ] **Step 2: Recompile + run (Explicit test skipped by default); fix if red; re-run**

Expected: `Defines_add_then_remove_restores` + `Build_reads_are_safe` pass; `SwitchTarget_roundtrips` is **not run** (Explicit). Defines restored via `finally`.

- [ ] **Step 3: Commit**

```bash
git add com.autonomous-unity.mcp/Editor/Tests/McpMutateTests_Build.cs
git commit -m "test: unity_build_manage defines round-trip + [Explicit] switch_target"
```

---

## Task 7: unity_workflow tests

**Files:**
- Create: `com.autonomous-unity.mcp/Editor/Tests/McpMutateTests_Workflow.cs`

- [ ] **Step 1: Write the tests**

```csharp
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
```

> Skip `replay` in the default suite — it executes the recorded tools, which can mutate; `append_step` + `delete` already prove the write paths. If desired, `replay` of a single `health_check` step can be added (it's a Read tool, safe).

- [ ] **Step 2: Recompile + run; fix if red; re-run green**

Expected: pass. Cleans up its own workflow via `delete`.

- [ ] **Step 3: Commit**

```bash
git add com.autonomous-unity.mcp/Editor/Tests/McpMutateTests_Workflow.cs
git commit -m "test: unity_workflow write-path coverage"
```

---

## Task 8: full-suite run, bug-fix sweep, pristine verification

**Files:** none new (fixes land in the relevant `Editor/Tools/Unity*Tool.cs` if tests are red).

- [ ] **Step 1: Capture pre-run global state**

Over the bridge, record current values to compare after:
```
unity_build_manage {"action":"get_defines"}
unity_physics {"action":"get_gravity"}
unity_lighting {"action":"get_ambient"}
unity_lighting {"action":"get_skybox"}
```
Save the responses.

- [ ] **Step 2: Run the whole EditMode suite**

```
refresh_unity {}
run_tests {"mode":"editmode"}
get_test_job {"jobId":"<id>"}   # poll to terminal
```
Expected: every `AutonomousMcp.SelfTest` test **passed** except `SwitchTarget_roundtrips` (Explicit, not run).

- [ ] **Step 3: Fix any red test at the tool source, then re-run**

For each failure, read the error, fix the relevant `Unity*Tool.cs` (2022.3 API gap or wrong mutation) — not the test. Re-run `run_tests` until green. Commit each fix:
```bash
git add com.autonomous-unity.mcp/Editor/Tools/<Tool>.cs
git commit -m "fix(<tool>): <root-cause> surfaced by self-test"
```

- [ ] **Step 4: Pristine check**

Re-query the four reads from Step 1 and confirm values match pre-run. Confirm `Assets/_MCPSelfTest` does not exist and no `T_*` objects remain in the active scene. Then run the suite a **second** time — it must be all-green again (no state leakage between runs).

- [ ] **Step 5: Final commit (progress log)**

```bash
git add docs/superpowers/plans/2026-05-28-mutate-writepath-selftest.md
git commit -m "test: mutate write-path self-test suite green; project pristine"
```

---

## Self-review

- **Spec coverage:** harness/asmdef → Task 1; UI → T2; physics → T3; lighting/camera/event/navmesh/terrain/cinemachine/timeline → T4; importer/cleaner (scoped+confirm) → T5; build defines + Explicit switch_target → T6; workflow → T7; full run + bug loop + pristine + re-run → T8. All spec tiers covered.
- **Placeholder scan:** every test method has complete code with real action/param names read from source; no TBD/TODO.
- **Type consistency:** all tests use `Invoke(string, object)` + `AssertOk(...)` from `McpTestHarness`; `McpTestSO`/`McpTestTarget` defined in Task 1 and used in T4/T5; tool/param names match the verified-facts section.
- **Known soft spots flagged inline:** `m_MaxTextureSize` serialized name (test-data fix if wrong), `set_skybox`/Cinemachine/Timeline assert non-throwing (asset/package-dependent), build-defines API variant note.

---

## Execution results (2026-05-28, live Unity 2022.3.22f1, project: Leaf)

Suite GREEN: all 31 runnable self-tests pass; switch_target [Explicit] skipped (1).
(Project total 190 incl. 17 pre-existing YUCP package-test failures — unrelated.)

Setup learnings (for re-running):
- Package must be in Packages/manifest.json "testables" or the test asm never compiles.
- Test asmdef must use explicit precompiledReferences (nunit.framework.dll +
  Newtonsoft.Json.dll) + UnityEngine/UnityEditor.TestRunner refs — the
  optionalUnityReferences/TestAssemblies style strips Newtonsoft.
- run_tests over the MCP bridge loses the job on the domain reload that compiling a
  NEW test asm triggers; once compiled, bridge runs complete cleanly. (Follow-up:
  make AutonomousMcpTestJobs survive domain reload via SessionState.)

Tool bugs found + fixed (root cause, not symptom):
- unity_cleaner: orphan detection self-reference (GetDependencies includes self).
- unity_physics: GetComponent ?? AddComponent fake-null pitfall.

Pristine verified: Assets/_MCPSelfTest removed; active scene, Physics.gravity,
scripting defines all restored to pre-run values.
