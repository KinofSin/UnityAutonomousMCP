# Mutate write-path self-test harness

## Context

The unified v2 MCP (`feat/unified-mcp` / private repo `main`) compiles on Unity
2022.3.22f1 and all tool families are reachable. But the 13 **Mutate** tool
families have only been compile-checked and exercised with their *read-only*
actions. Their ~40 **mutating write actions** (create UI, bake navmesh, set
importer properties, add components, delete orphans, set scripting defines, …)
are unproven on 2022.3 — they could hit a Unity-6-vs-2022.3 API gap or perform
the wrong mutation, and several are destructive.

This builds a **reusable EditMode self-test harness** that exercises every
mutating action against a disposable temp surface, asserts both the tool
response and the real side-effect, and leaves the project pristine. It doubles
as regression protection for all future changes ("far from done"), and is
re-runnable over MCP via the existing `run_tests` / `get_test_job` tools.

The 5 compile bugs found on first import (2 of them pre-existing patch bugs)
proved that "compiles + static review" is not enough — runtime exercise is
required. This harness makes that runtime exercise repeatable.

## Goal / success criteria

1. A new EditMode test assembly under the package with one `[Test]` per mutating
   action (~40), invoking the **real registered path**
   (`ToolRegistry.TryResolve(name).Handler(args)`).
2. Each test asserts **both** `AutonomousMcpToolResponse.success` **and** the
   concrete side-effect (e.g. after `create_canvas`, a `Canvas` exists).
3. Running `run_tests {mode:"editmode"}` (Test Runner or over the bridge) returns
   all-green except the single `[Explicit]` `switch_target` test.
4. The project is left **pristine** after a run: temp scene + `Assets/_MCPSelfTest/`
   deleted; all mutated global state (scripting defines, ambient, skybox, gravity,
   layer-collision) restored.
5. Any 2022.3 API gap or wrong-mutation found is **fixed at the source** in the
   tool, and the test then passes.

## Architecture

```
com.autonomous-unity.mcp/Editor/Tests/
  AutonomousMcp.Editor.Tests.asmdef   (EditMode test asm)
  McpTestHarness.cs                   (base: SetUp/TearDown, Invoke(), asserts)
  McpMutateTests_UI.cs                (one class per family group)
  McpMutateTests_Physics.cs
  McpMutateTests_SceneObjects.cs      (lighting, camera, event, cinemachine, timeline, navmesh, terrain)
  McpMutateTests_Assets.cs            (importer, cleaner — scoped to Assets/_MCPSelfTest)
  McpMutateTests_Build.cs             (defines; switch_target [Explicit])
  McpMutateTests_Workflow.cs
```

**Test asmdef** references: `AutonomousMcp.Editor` (the package), `nunit.framework`,
`UnityEditor.TestRunner`, `UnityEngine.TestRunner`; `"optionalUnityReferences":
["TestAssemblies"]`; `includePlatforms: ["Editor"]`; `defineConstraints:
["UNITY_INCLUDE_TESTS"]`. Newtonsoft.Json is transitively available via the
package reference.

**Invocation pattern (every test):**
```csharp
Assert.IsTrue(ToolRegistry.TryResolve("unity_ui", out var entry));
var resp = entry.Handler(JObject.FromObject(new { action = "create_canvas", name = "T_Canvas" }));
Assert.IsTrue(resp.success, resp.error);
Assert.IsNotNull(GameObject.Find("T_Canvas")?.GetComponent<Canvas>());
```

## McpTestHarness (base class)

- `[SetUp] Setup()`:
  - `_origScene = EditorSceneManager.GetActiveScene().path` (remember to restore).
  - `_scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single)`.
  - create `_root = new GameObject("_MCPTestRoot")`.
  - `AssetDatabase.CreateFolder("Assets", "_MCPSelfTest")` if absent (asset tests).
- `protected AutonomousMcpToolResponse Invoke(string tool, object args)` — resolves
  via `ToolRegistry.TryResolve`, fails the test if unregistered, returns the response.
- `protected void AssertOk(AutonomousMcpToolResponse r)` — `Assert.IsTrue(r.success, r.error)`.
- `[TearDown] Teardown()`:
  - destroy `_root` and any test objects.
  - `AssetDatabase.DeleteAsset("Assets/_MCPSelfTest")`.
  - reopen `_origScene` if it was a saved scene, else new empty scene.
  - **global-state restore** is per-test (see Build/Physics/Lighting below), not here.
- **Global-restore pattern** (used by tests that touch project/global state): capture
  the original value at the top of the test, mutate + assert, then restore in a
  `try/finally` so a failed assert still restores.

## Coverage matrix (~40 actions)

### Tier 1 — scene mutations (temp scene; no project impact)

- **unity_ui:** create_canvas, create_panel, create_button, create_text, create_image,
  set_anchor, set_rect. Assert each created GO + component exists / RectTransform values set.
- **unity_physics:** add_rigidbody, add_collider(box/sphere/capsule/mesh),
  set_gravity (capture+restore `Physics.gravity`), get_gravity,
  set_ignore_layer_collision (capture+restore), get_physics_settings. Assert components added.
- **unity_lighting:** create_light (assert `Light` exists), set_ambient
  (capture+restore `RenderSettings.ambient*`), get_ambient, set_skybox
  (capture+restore `RenderSettings.skybox`), get_skybox.
- **unity_camera:** create (assert `Camera` exists), sceneview_focus, sceneview_pose,
  sceneview_align_with_view (these only move the SceneView camera — harmless, assert success).
- **unity_event:** add_persistent (on a temp `UnityEvent` component / Button), list_persistent
  (assert count), remove_persistent (assert removed).
- **unity_navmesh:** create a temp floor (scaled plane/quad) and mark it Navigation Static
  via `GameObjectUtility.SetStaticEditorFlags(floor, StaticEditorFlags.NavigationStatic)`
  (required or bake yields 0 verts). Then bake, info (assert verts > 0), clear (assert
  cleared), list_agent_types. Primary assertion is bake **success**; verts > 0 is the
  stronger check enabled by the static flag.
- **unity_terrain:** create (assert `Terrain` exists), info, flatten (assert heights zeroed).
- **unity_cinemachine:** create_vcam, set_priority — assert **graceful** result: if the
  Cinemachine package is absent (as in LEAF), the response is a clean "package not detected"
  (success=false with that message), NOT a crash. If present, assert vcam created.
- **unity_timeline:** create_director (assert `PlayableDirector` via reflection),
  bind_timeline_asset, list_directors.

### Tier 2 — asset mutations (scoped to `Assets/_MCPSelfTest/`)

- **unity_importer:** create a temp 4x4 texture PNG in the test folder, import it,
  then get_importer_type, get_properties, set_property (e.g. `maxTextureSize`=512) —
  assert the property changed via a fresh `AssetImporter.GetAtPath`.
- **unity_cleaner:** create `Assets/_MCPSelfTest/Orphans/orphan.asset` (an unreferenced
  ScriptableObject) and an empty `Assets/_MCPSelfTest/EmptyDir/`. Assert
  `find_orphans{folder:"Assets/_MCPSelfTest"}` lists the orphan;
  `delete_orphans{folder:"Assets/_MCPSelfTest", confirm:true}` removes it (assert gone).
  Same for find/delete_empty_folders. Also find_unused_materials,
  find_internal_error_shaders (read; assert success). **Scope + confirm guarantee no
  real asset is touched.**

### Tier 3 — global/build (capture+restore; one Explicit)

- **unity_build_manage:** get_defines, add_define("MCP_SELFTEST_TMP"), assert present,
  remove_define, assert gone; set_defines round-trip with original captured+restored in
  `finally`. get_target, list_targets, get_scenes (read; assert success).
- **switch_target:** `[Test, Explicit("Switches build platform; slow + global")]` — not
  run by the default suite. When run explicitly: capture current target, switch to the
  same target (no-op) OR to a sibling and back, assert success, restore.
- **unity_workflow:** save("_mcp_selftest") a 1-step workflow, list (assert present),
  load (assert returns it), append_step, replay (assert executes), delete (assert gone).

## Files

**Created:**
- `com.autonomous-unity.mcp/Editor/Tests/AutonomousMcp.Editor.Tests.asmdef` (+ `.meta`)
- `McpTestHarness.cs`, `McpMutateTests_UI.cs`, `McpMutateTests_Physics.cs`,
  `McpMutateTests_SceneObjects.cs`, `McpMutateTests_Assets.cs`,
  `McpMutateTests_Build.cs`, `McpMutateTests_Workflow.cs` (+ `.meta` each)

**Modified (only if a test surfaces a bug):** the relevant
`com.autonomous-unity.mcp/Editor/Tools/Unity*Tool.cs` — fix the 2022.3 API gap or
wrong-mutation at the source.

**Unchanged:** the 6 Read tools (validated live), `manage_generator generate`
(stub), `manage_checkpoint`.

## Verification

1. **Compile:** open in Unity 2022.3.22f1; Test Runner (EditMode) lists the new tests; 0 compile errors.
2. **Run:** `run_tests {mode:"editmode"}` over the bridge → `get_test_job` until terminal.
   Expected: all pass except the `[Explicit]` `switch_target` (not run).
3. **Pristine check:** after the run, `Assets/_MCPSelfTest/` is gone, no `_MCPTestRoot`
   leftover, scripting defines list unchanged, `RenderSettings`/`Physics.gravity`
   unchanged (re-query via tools and compare to pre-run values).
4. **Bug loop:** any red test → fix the tool source → re-run → green.
5. **Re-runnable:** a second consecutive `run_tests` run is also all-green (no state leakage).

## Out of scope (roadmap)

- PlayMode tests (these are EditMode only).
- `manage_generator generate` real backends (separate sprint).
- CI wiring (GitHub Actions running Unity headless) — possible later once the suite is stable.
- Mutating the real LEAF avatar.
