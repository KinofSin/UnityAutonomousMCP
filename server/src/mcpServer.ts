import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { z } from "zod";
import { runAutonomousGoal } from "./orchestrator.js";
import { createUnityBridgeFromEnv } from "./unityBridge.js";
import { TOOL_CAPABILITIES } from "./capabilityCatalog.js";

const bridge = createUnityBridgeFromEnv();

function toTextResult(payload: unknown) {
  return {
    content: [{ type: "text" as const, text: JSON.stringify(payload, null, 2) }]
  };
}

// Helper: call Unity bridge and return MCP-formatted result
async function callUnity(tool: string, params: Record<string, unknown>) {
  const response = await bridge.call({ tool, params });
  return toTextResult(response);
}

// Reusable Zod fragments
const zInstanceIdOrName = {
  instanceId: z.number().optional().describe("Unity instance ID of the target GameObject"),
  name: z.string().optional().describe("Name or path of the target GameObject (e.g. 'Player' or '/Root/Child')"),
};

const zVector3 = z.object({
  x: z.number().optional(),
  y: z.number().optional(),
  z: z.number().optional(),
}).optional();

export async function startMcpServer(): Promise<void> {
  const server = new McpServer({
    name: "unity-autonomous-agent",
    version: "0.2.0"
  });

  // ── Meta tools ──

  server.tool(
    "list_capabilities",
    "List all Unity MCP tool capabilities and metadata.",
    async () => toTextResult({ capabilities: TOOL_CAPABILITIES })
  );

  server.tool(
    "autonomous_plan",
    "Build and execute a bounded autonomous plan against Unity tools.",
    {
      goal: z.string().min(1).describe("High-level goal to accomplish"),
      constraints: z.array(z.string()).optional().describe("Constraints for the plan"),
      maxSteps: z.number().int().min(1).max(50).optional().describe("Max steps in the plan"),
      allowDestructive: z.boolean().optional().describe("Allow destructive operations"),
      stopOnError: z.boolean().optional().describe("Stop on first error"),
    },
    async (input) => {
      const { goal, constraints, maxSteps, allowDestructive, stopOnError } = input;
      const result = await runAutonomousGoal(
        { goal, constraints, maxSteps },
        bridge,
        { allowDestructive, stopOnError }
      );
      return toTextResult(result);
    }
  );

  // ── health_check ──

  server.tool(
    "health_check",
    "Check Unity Editor status, compilation state, and list all supported tools/actions.",
    async () => callUnity("health_check", {})
  );

  // ── read_console ──

  server.tool(
    "read_console",
    "Read Unity console log entries. Filter by level and limit count.",
    {
      level: z.enum(["all", "log", "warning", "error"]).optional().describe("Log level filter (default: all)"),
      limit: z.number().int().optional().describe("Max entries to return"),
    },
    async (input) => callUnity("read_console", input)
  );

  // ── manage_scene ──

  server.tool(
    "manage_scene",
    "Manage Unity scenes: inspect_active_scene, save_active_scene, open_scene, list_scenes.",
    {
      action: z.enum(["inspect_active_scene", "save_active_scene", "open_scene", "list_scenes"])
        .describe("Scene action to perform"),
      path: z.string().optional().describe("Scene asset path for open_scene (e.g. 'Assets/Scenes/Main.unity')"),
      save_first: z.boolean().optional().describe("Auto-save dirty scene before opening another (default: true)"),
    },
    async (input) => callUnity("manage_scene", input)
  );

  // ── manage_gameobject ──

  server.tool(
    "manage_gameobject",
    "Manage GameObjects: create, find, transform, hierarchy, rename, destroy. Actions: create, create_empty, create_primitive, find, find_by_name, find_contains, set_transform, get_world_transform, reparent, get_children, get_parent, get_full_hierarchy, set_active, rename, destroy.",
    {
      action: z.string().min(1).describe("Action: create|create_empty|create_primitive|find|find_by_name|find_contains|set_transform|get_world_transform|reparent|get_children|get_parent|get_full_hierarchy|set_active|rename|destroy"),
      ...zInstanceIdOrName,
      target: z.string().optional().describe("Alternative target name"),
      primitiveType: z.string().optional().describe("Primitive type for create_primitive (Cube, Sphere, Capsule, etc.)"),
      parent: z.string().optional().describe("Parent name for reparent or create"),
      newName: z.string().optional().describe("New name for rename action"),
      active: z.boolean().optional().describe("Active state for set_active"),
      position: zVector3.describe("Local position {x,y,z}"),
      rotation: zVector3.describe("Local euler rotation {x,y,z}"),
      scale: zVector3.describe("Local scale {x,y,z}"),
      worldSpace: z.boolean().optional().describe("Use world space for set_transform"),
      searchTerm: z.string().optional().describe("Search term for find_contains"),
    },
    async (input) => callUnity("manage_gameobject", input)
  );

  // ── manage_component ──

  server.tool(
    "manage_component",
    "Manage components on GameObjects: add, remove, get_all, get_properties, set_property.",
    {
      action: z.enum(["add", "remove", "get_all", "get_properties", "set_property"])
        .describe("Component action"),
      ...zInstanceIdOrName,
      component_type: z.string().optional().describe("Fully-qualified or short type name (e.g. 'Rigidbody', 'UnityEngine.BoxCollider')"),
      property_name: z.string().optional().describe("Property name for get/set_property"),
      value: z.unknown().optional().describe("Value to set (type depends on property: number, bool, string, {r,g,b,a}, {x,y,z}, etc.)"),
    },
    async (input) => callUnity("manage_component", input)
  );

  // ── manage_script ──

  server.tool(
    "manage_script",
    "Create or update a C# script file in the Unity project. Triggers recompilation.",
    {
      action: z.literal("create_or_update").describe("Script action"),
      scriptPath: z.string().min(1).describe("Asset path starting with 'Assets/' (e.g. 'Assets/Scripts/MyScript.cs')"),
      contents: z.string().min(1).describe("Full C# source code contents of the script"),
    },
    async (input) => callUnity("manage_script", input)
  );

  // ── read_script ──

  server.tool(
    "read_script",
    "Read the contents of a script file from the Unity project.",
    {
      scriptPath: z.string().min(1).describe("Asset path starting with 'Assets/' (e.g. 'Assets/Scripts/MyScript.cs')"),
    },
    async (input) => callUnity("read_script", input)
  );

  // ── manage_asset ──

  server.tool(
    "manage_asset",
    "Search assets or instantiate prefabs. Actions: find, instantiate_prefab.",
    {
      action: z.enum(["find", "instantiate_prefab"]).describe("Asset action"),
      filter: z.string().optional().describe("AssetDatabase search filter for find (e.g. 't:Prefab', 't:Material MyMat')"),
      folder: z.string().optional().describe("Folder to search in for find (e.g. 'Assets/Prefabs')"),
      limit: z.number().int().optional().describe("Max results for find"),
      asset_path: z.string().optional().describe("Asset path for instantiate_prefab (e.g. 'Assets/Prefabs/Enemy.prefab')"),
      parent: z.string().optional().describe("Parent GameObject name for instantiate_prefab"),
      position: zVector3.describe("Local position for instantiated prefab"),
      rotation: zVector3.describe("Local euler rotation for instantiated prefab"),
      scale: zVector3.describe("Local scale for instantiated prefab"),
    },
    async (input) => callUnity("manage_asset", input)
  );

  // ── manage_editor ──

  server.tool(
    "manage_editor",
    "Control Unity Editor: enter_play_mode, exit_play_mode, pause, step, undo, redo.",
    {
      action: z.enum(["enter_play_mode", "exit_play_mode", "pause", "step", "undo", "redo"])
        .describe("Editor control action"),
    },
    async (input) => callUnity("manage_editor", input)
  );

  // ── execute_menu_item ──

  server.tool(
    "execute_menu_item",
    "Execute a Unity Editor menu item by its full path (e.g. 'Tools/My Tool', 'GameObject/Create Empty').",
    {
      menu_path: z.string().min(1).describe("Full menu path (e.g. 'Tools/My Custom Tool')"),
    },
    async (input) => callUnity("execute_menu_item", input)
  );

  // ── capture_screenshot ──

  server.tool(
    "capture_screenshot",
    "Capture a screenshot of the Unity Scene view or Game view as PNG. Returns base64-encoded image.",
    {
      source: z.enum(["scene", "game"]).optional().describe("Which view to capture (default: scene)"),
      width: z.number().int().min(64).max(2048).optional().describe("Image width in pixels (default: 512)"),
      height: z.number().int().min(64).max(2048).optional().describe("Image height in pixels (default: 512)"),
      save_path: z.string().optional().describe("Optional file path to save the PNG (e.g. 'Assets/Screenshots/shot.png')"),
    },
    async (input) => callUnity("capture_screenshot", input)
  );

  // ── manage_animator ──

  server.tool(
    "manage_animator",
    "Inspect and control Animator controllers: get_parameters, set_parameter, get_layers, get_states, get_current_state.",
    {
      action: z.enum(["get_parameters", "set_parameter", "get_layers", "get_states", "get_current_state"])
        .describe("Animator action"),
      ...zInstanceIdOrName,
      parameter: z.string().optional().describe("Parameter name for set_parameter"),
      value: z.unknown().optional().describe("Parameter value for set_parameter (float, int, bool)"),
      layer_index: z.number().int().optional().describe("Layer index for get_states/get_current_state (default: 0)"),
    },
    async (input) => callUnity("manage_animator", input)
  );

  // ── manage_material ──

  server.tool(
    "manage_material",
    "Inspect and modify materials on Renderers: get, get_properties, set_property, list_materials.",
    {
      action: z.enum(["get", "get_properties", "set_property", "list_materials"])
        .describe("Material action"),
      ...zInstanceIdOrName,
      material_index: z.number().int().optional().describe("Material slot index (default: 0)"),
      property: z.string().optional().describe("Shader property name for set_property (e.g. '_Color', '_Metallic')"),
      value: z.unknown().optional().describe("Property value: float, int, {r,g,b,a} for color, {x,y,z,w} for vector"),
    },
    async (input) => callUnity("manage_material", input)
  );

  // ── validate_script ──

  server.tool(
    "validate_script",
    "Trigger Unity script compilation refresh and check for errors.",
    {
      strict: z.boolean().optional().describe("Enable strict validation mode"),
    },
    async (input) => callUnity("validate_script", input)
  );

  // ── run_tests ──

  server.tool(
    "run_tests",
    "Run Unity Test Runner (EditMode or PlayMode tests). Returns a jobId to poll with get_test_job.",
    {
      mode: z.enum(["editmode", "playmode"]).optional().describe("Test mode (default: editmode)"),
    },
    async (input) => callUnity("run_tests", input)
  );

  // ── get_test_job ──

  server.tool(
    "get_test_job",
    "Poll the status and results of a Unity Test Runner job started by run_tests.",
    {
      jobId: z.string().min(1).describe("Job ID returned by run_tests"),
    },
    async (input) => callUnity("get_test_job", input)
  );

  // ── batch_execute ──

  server.tool(
    "batch_execute",
    "Execute multiple Unity tool calls in a single batch. Each item has a tool name and params.",
    {
      calls: z.array(z.object({
        tool: z.string().min(1).describe("Unity tool name"),
        params: z.record(z.unknown()).optional().describe("Tool parameters"),
      })).min(1).describe("Array of tool calls to execute sequentially"),
    },
    async (input) => callUnity("batch_execute", { calls: input.calls })
  );

  // ── Generic fallback for any future tools ──

  server.tool(
    "unity_tool_call",
    "Generic fallback: call any Unity tool by name. Prefer the specific tools above.",
    {
      tool: z.string().min(1).describe("Unity tool name"),
      params: z.record(z.unknown()).optional().describe("Tool parameters as JSON object"),
    },
    async (input) => {
      const response = await bridge.call({ tool: input.tool, params: input.params ?? {} });
      return toTextResult(response);
    }
  );

  const transport = new StdioServerTransport();
  await server.connect(transport);
}
