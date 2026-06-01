import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { SSEServerTransport } from "@modelcontextprotocol/sdk/server/sse.js";
import { createServer as createHttpServer } from "node:http";
import { z } from "zod";
import { runAutonomousGoal } from "./orchestrator.js";
import { createUnityBridgeFromEnv } from "./unityBridge.js";
import { TOOL_CAPABILITIES } from "./capabilityCatalog.js";
import { queryKnowledgeBase } from "./vrcKnowledgeBase.js";
import { queryInstallGuide } from "./vrcInstallGuide.js";
import { listSkills, findSkill, invokeSkill, type Skill } from "./skills.js";

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

function buildServer(): McpServer {
  const server = new McpServer({
    name: "unity-autonomous-agent",
    version: "0.3.0"
  });

  registerAllTools(server);
  return server;
}

export async function startMcpServer(): Promise<void> {
  const server = buildServer();
  const transport = new StdioServerTransport();
  await server.connect(transport);
}

export async function startMcpSseServer(port: number): Promise<void> {
  // One SSE session per HTTP GET /sse, message ingestion via POST /messages.
  // Multiple concurrent clients supported via session id query param.
  const sessions = new Map<string, { server: McpServer; transport: SSEServerTransport }>();

  const httpServer = createHttpServer(async (req, res) => {
    if (!req.url) { res.writeHead(400).end(); return; }
    const url = new URL(req.url, `http://${req.headers.host}`);

    if (req.method === "GET" && url.pathname === "/sse") {
      const server = buildServer();
      const transport = new SSEServerTransport("/messages", res);
      sessions.set(transport.sessionId, { server, transport });
      res.on("close", () => { sessions.delete(transport.sessionId); });
      await server.connect(transport);
      return;
    }

    if (req.method === "POST" && url.pathname === "/messages") {
      const sessionId = url.searchParams.get("sessionId") ?? "";
      const session = sessions.get(sessionId);
      if (!session) { res.writeHead(404).end("Unknown session"); return; }
      await session.transport.handlePostMessage(req, res);
      return;
    }

    if (req.method === "GET" && url.pathname === "/health") {
      res.writeHead(200, { "Content-Type": "application/json" });
      res.end(JSON.stringify({ ok: true, sessions: sessions.size }));
      return;
    }

    res.writeHead(404).end();
  });

  await new Promise<void>((resolve) => httpServer.listen(port, () => resolve()));
  process.stderr.write(`[autonomous-mcp] SSE listening on http://127.0.0.1:${port}/sse (POST /messages?sessionId=...)\n`);
}

function registerAllTools(server: McpServer): void {
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

  // ── Advisor HUD ──

  server.tool(
    "hud_post",
    "Post advice into the in-Unity Advisor HUD (appears in the dockable panel for the user). Use plain language a novice understands.",
    {
      text: z.string().min(1).describe("Advice text (plain/markdown)"),
      level: z.enum(["info", "success", "warning"]).optional().describe("Tint (default info)"),
    },
    async (input) => callUnity("hud_post", input)
  );

  server.tool(
    "hud_poll",
    "Drain the Advisor HUD outbox: returns everything the user sent from Unity (notes, selection, console errors) and clears the queue. Call this when a tool response shows hudOutbox.pending > 0, or when the user says they sent something.",
    {},
    async () => callUnity("hud_poll", {})
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
    "Create or update a C# script file in the Unity project. Supports both Assets/ and Packages/ paths. Triggers recompilation.",
    {
      action: z.literal("create_or_update").describe("Script action"),
      scriptPath: z.string().min(1).describe("Path starting with 'Assets/' or 'Packages/' (e.g. 'Assets/Scripts/MyScript.cs')"),
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
    "Capture a PNG of the Unity Scene/Game view, a specific EditorWindow, or the whole editor. Returns base64 image (and the real captured dimensions).",
    {
      source: z.enum(["scene", "game", "window", "editor"]).optional()
        .describe("scene|game = camera view; window = a specific EditorWindow (see `window`); editor = the whole Unity main window. Default: scene"),
      window: z.string().optional()
        .describe("For source='window': EditorWindow title or type to capture (substring match), e.g. 'Autonomous MCP', 'Package Manager', 'Project', 'Console', 'Inspector'"),
      width: z.number().int().min(64).max(2048).optional().describe("Image width for scene/game (default 512; ignored for window/editor, which use the real window size)"),
      height: z.number().int().min(64).max(2048).optional().describe("Image height for scene/game (default 512; ignored for window/editor)"),
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

  // ── execute_csharp ──

  server.tool(
    "execute_csharp",
    "Execute arbitrary C# code in the Unity Editor. The ultimate escape hatch — code is compiled in-memory with full access to UnityEngine, UnityEditor, and all loaded assemblies. The code is wrapped in a static method body: write statements that optionally end with 'return <expr>;' to get a result back.",
    {
      code: z.string().min(1).describe("C# code to execute. Written as method body statements, e.g. 'return GameObject.FindObjectsOfType<Camera>().Length;'"),
      return_result: z.boolean().optional().describe("Whether to capture and return the result (default: true)"),
    },
    async (input) => callUnity("execute_csharp", input)
  );

  // ── search_hierarchy ──

  server.tool(
    "search_hierarchy",
    "Deep scene search with powerful filters. Find GameObjects by regex name, component type, tag, layer, active state. Returns rich context including components list.",
    {
      name_pattern: z.string().optional().describe("Regex pattern to match GameObject names (case-insensitive)"),
      component_type: z.string().optional().describe("Filter by component type name (e.g. 'Animator', 'MeshRenderer')"),
      tag: z.string().optional().describe("Filter by tag (e.g. 'Player', 'MainCamera')"),
      layer: z.string().optional().describe("Filter by layer name or index"),
      active_only: z.boolean().optional().describe("Only return objects active in hierarchy (default: false)"),
      include_inactive: z.boolean().optional().describe("Include inactive objects in search (default: true)"),
      include_components: z.boolean().optional().describe("Include component list on each result (default: false)"),
      limit: z.number().int().optional().describe("Max results to return (default: 100, max: 500)"),
    },
    async (input) => callUnity("search_hierarchy", input)
  );

  // ── get_project_structure ──

  server.tool(
    "get_project_structure",
    "Get the asset folder tree structure with file counts, types, and sizes. Essential for understanding project layout.",
    {
      path: z.string().optional().describe("Root path to scan (default: 'Assets')"),
      depth: z.number().int().min(1).max(10).optional().describe("Max folder depth to recurse (default: 3)"),
      extensions: z.string().optional().describe("Comma-separated file extension filter (e.g. '.cs,.shader,.prefab')"),
      include_meta: z.boolean().optional().describe("Include .meta files (default: false)"),
    },
    async (input) => callUnity("get_project_structure", input)
  );

  // ── manage_prefab ──

  server.tool(
    "manage_prefab",
    "Prefab workflow: get_status (type, overrides, asset path), open (enter prefab mode), apply_overrides, revert_overrides, unpack.",
    {
      action: z.enum(["get_status", "open", "apply_overrides", "revert_overrides", "unpack"])
        .describe("Prefab action"),
      ...zInstanceIdOrName,
      asset_path: z.string().optional().describe("Prefab asset path for open action (e.g. 'Assets/Prefabs/Player.prefab')"),
      completely: z.boolean().optional().describe("For unpack: true = unpack completely, false = outermost only (default: false)"),
    },
    async (input) => callUnity("manage_prefab", input)
  );

  // ── manage_selection ──

  server.tool(
    "manage_selection",
    "Control Unity Editor selection: get current selection, set selection, clear, or focus/frame selected object in scene view.",
    {
      action: z.enum(["get", "set", "clear", "focus"]).optional().describe("Selection action (default: get)"),
      ...zInstanceIdOrName,
      names: z.array(z.string()).optional().describe("Array of GameObject names for set action"),
      instanceIds: z.array(z.number()).optional().describe("Array of instance IDs for set action"),
    },
    async (input) => callUnity("manage_selection", input)
  );

  // ── manage_layer_tag ──

  server.tool(
    "manage_layer_tag",
    "Manage layers and tags: get (layer+tag of GO), set_layer, set_tag, list_layers, list_tags, list_sorting_layers.",
    {
      action: z.enum(["get", "set_layer", "set_tag", "list_layers", "list_tags", "list_sorting_layers"])
        .describe("Layer/tag action"),
      ...zInstanceIdOrName,
      layer: z.string().optional().describe("Layer name for set_layer"),
      layer_index: z.number().int().optional().describe("Layer index for set_layer (alternative to name)"),
      tag: z.string().optional().describe("Tag name for set_tag"),
      recursive: z.boolean().optional().describe("Apply layer change to all children (default: false)"),
    },
    async (input) => callUnity("manage_layer_tag", input)
  );

  // ── get_compilation_errors ──

  server.tool(
    "get_compilation_errors",
    "Get detailed compilation errors with file paths and line numbers. More structured than read_console for debugging script issues.",
    {
      include_warnings: z.boolean().optional().describe("Include warnings in addition to errors (default: false)"),
    },
    async (input) => callUnity("get_compilation_errors", input)
  );

  // ── manage_project_settings ──

  server.tool(
    "manage_project_settings",
    "Read/write Unity project settings: player settings, quality settings, physics settings, time settings.",
    {
      action: z.enum(["get_player_settings", "set_player_setting", "get_quality_settings", "get_physics_settings", "get_time_settings"])
        .describe("Settings action"),
      setting: z.string().optional().describe("Setting name for set_player_setting (companyName, productName, bundleVersion, runInBackground)"),
      value: z.string().optional().describe("New value for set_player_setting"),
    },
    async (input) => callUnity("manage_project_settings", input)
  );

  // ── get_installed_packages ──

  server.tool(
    "get_installed_packages",
    "List all installed Unity packages with VRC ecosystem identification. Flags 40+ known VRChat packages (MA, VRCFury, AAO, Poiyomi, lilToon, etc.) with descriptions. Detects loaded framework assemblies.",
    {
      include_builtin: z.boolean().optional().describe("Include com.unity.modules.* packages (default: false)"),
    },
    async (input) => callUnity("get_installed_packages", input)
  );

  // ── list_shaders ──

  server.tool(
    "list_shaders",
    "Enumerate all shaders in the project with VRC ecosystem family detection (Poiyomi, lilToon, SCSS, ORL, etc.). Filter and inspect shader properties.",
    {
      filter: z.string().optional().describe("Filter shaders by name substring"),
      limit: z.number().int().min(1).max(500).optional().describe("Max results (default: 100)"),
      include_properties: z.boolean().optional().describe("Include shader property list (default: false)"),
      include_builtin: z.boolean().optional().describe("Include Hidden/Legacy/GUI/UI shaders (default: false)"),
    },
    async (input) => callUnity("list_shaders", input)
  );

  // ── get_asset_info ──

  server.tool(
    "get_asset_info",
    "Deep inspect any Unity asset by path. Returns type-specific details: prefab hierarchy + VRC components, material shader + textures, texture dimensions + compression, AnimatorController layers + parameters, AnimationClip curves + bindings.",
    {
      asset_path: z.string().min(1).describe("Asset path (e.g. 'Assets/Prefabs/MyAvatar.prefab', 'Assets/Materials/Body.mat')"),
    },
    async (input) => callUnity("get_asset_info", input)
  );

  // ── scan_armature ──

  server.tool(
    "scan_armature",
    "VRChat avatar armature analysis: full bone tree, humanoid rig mapping (all HumanBodyBones), PhysBone chains, SkinnedMeshRenderer stats. Essential before attaching accessories or clothing.",
    {
      ...zInstanceIdOrName,
    },
    async (input) => callUnity("scan_armature", input)
  );

  // ── scan_avatar ──

  server.tool(
    "scan_avatar",
    "Comprehensive VRChat avatar scan: VRCAvatarDescriptor (lip sync, view position, expression parameters with cost/budget/remaining), PhysBones, Contacts, installed frameworks (MA, VRCFury, AAO, lilycalInventory), mesh stats (polygons, materials, blendshapes), shader usage, bone count.",
    {
      ...zInstanceIdOrName,
    },
    async (input) => callUnity("scan_avatar", input)
  );

  // ── get_vrc_knowledge ──

  server.tool(
    "get_vrc_knowledge",
    "Query the VRChat ecosystem knowledge base covering 200+ tools across 31 categories (shaders, optimization, toggles, expressions, physics, assembly, Quest, VRM, lighting, OSC protocol/libs/apps/query, face tracking, FBT stacks, SteamVR tools, haptics, desktop companions, marketplace). Returns conventions, best practices, tool descriptions, and best-pick recommendations.",
    {
      category: z.string().optional().describe("Category ID to fetch (e.g. 'shaders', 'optimization', 'assembly', 'osc_core', 'osc_libraries', 'osc_query', 'osc_apps', 'face_tracking', 'fbt_stacks', 'steamvr_tools', 'haptics', 'desktop_companions', 'osc_marketplace', 'avatar_conventions')"),
      tool_name: z.string().optional().describe("Search for a specific tool by name (e.g. 'Poiyomi', 'Modular Avatar', 'AAO')"),
      search: z.string().optional().describe("Free-text search across all tools, descriptions, and conventions"),
      list_categories: z.boolean().optional().describe("List all available categories (default: false)"),
    },
    async (input) => {
      const result = queryKnowledgeBase(input);
      return toTextResult(result);
    }
  );

  // ── get_install_guide ──

  server.tool(
    "get_install_guide",
    "Step-by-step installation, setup, and testing instructions for 60+ VRChat Unity tools. Includes VPM repo URLs, prerequisites, common errors & fixes. Sourced from a comprehensive 3300-line install guide.",
    {
      tool_name: z.string().optional().describe("Get install/setup/test steps for a specific tool (e.g. 'Modular Avatar', 'Poiyomi', 'AAO', 'FaceEmo', 'GoGo Loco')"),
      section: z.string().optional().describe("Get all tools in a section (e.g. 'Shaders', 'Toggle Systems', 'Facial Expressions', 'Physics', 'Emulators')"),
      search: z.string().optional().describe("Free-text search across all install/setup instructions"),
      list_sections: z.boolean().optional().describe("List all sections with their tools"),
      get_prerequisites: z.boolean().optional().describe("Get global prerequisites (Unity Hub, Unity 2022.3.22f1, VCC, ALCOM, project creation)"),
      get_repos: z.boolean().optional().describe("Get all 30 VPM repository URLs"),
      get_errors: z.boolean().optional().describe("Get common errors & fixes"),
    },
    async (input) => {
      const result = queryInstallGuide(input);
      return toTextResult(result);
    }
  );

  // ── manage_scriptable_object ──

  server.tool(
    "manage_scriptable_object",
    "Create, find, inspect, and edit ScriptableObject assets. Actions: find (search project), get_properties (read all serialized fields), set_property (write a field), create (new SO asset), list_fields (inspect type schema).",
    {
      action: z.enum(["find", "get_properties", "set_property", "create", "list_fields"])
        .describe("ScriptableObject action"),
      asset_path: z.string().optional().describe("Asset path for get_properties/set_property (e.g. 'Assets/Data/MyConfig.asset')"),
      instanceId: z.number().int().optional().describe("Instance ID alternative to asset_path"),
      filter: z.string().optional().describe("Search filter for find (e.g. 'MyConfig' or 't:ScriptableObject MyConfig')"),
      type: z.string().optional().describe("Type name for create/list_fields (e.g. 'VRCExpressionParameters', 'MyConfigSO')"),
      path: z.string().optional().describe("Save path for create (e.g. 'Assets/Data/NewConfig.asset')"),
      property: z.string().optional().describe("Property name for set_property"),
      value: z.unknown().optional().describe("Value for set_property (type-matched: int, float, bool, string, {r,g,b,a}, etc.)"),
    },
    async (input) => callUnity("manage_scriptable_object", input)
  );

  // ── manage_texture ──

  server.tool(
    "manage_texture",
    "Inspect and modify texture import settings. Actions: get_import_settings (full import config + Android overrides), set_import_settings (max size, compression, crunch, sRGB, mipmaps, filter, etc.), get_info (runtime texture dimensions/format), find_textures (search project).",
    {
      action: z.enum(["get_import_settings", "set_import_settings", "get_info", "find_textures"])
        .describe("Texture action"),
      asset_path: z.string().optional().describe("Texture asset path (e.g. 'Assets/Textures/Body_diffuse.png')"),
      filter: z.string().optional().describe("Search filter for find_textures"),
      max_texture_size: z.number().int().optional().describe("Max texture size (32-8192, e.g. 2048, 1024, 512)"),
      texture_compression: z.string().optional().describe("Compression: Uncompressed, Compressed, CompressedHQ, CompressedLQ"),
      crunch_compression: z.boolean().optional().describe("Enable crunch compression (smaller file size)"),
      compression_quality: z.number().int().min(0).max(100).optional().describe("Crunch compression quality 0-100"),
      sRGB: z.boolean().optional().describe("sRGB color space (true for albedo/diffuse, false for normals/masks)"),
      is_readable: z.boolean().optional().describe("CPU-readable (needed for scripts, costs memory)"),
      mipmap_enabled: z.boolean().optional().describe("Generate mipmaps"),
      filter_mode: z.string().optional().describe("Filter mode: Point, Bilinear, Trilinear"),
      aniso_level: z.number().int().min(0).max(16).optional().describe("Anisotropic filtering level"),
      texture_type: z.string().optional().describe("Texture type: Default, NormalMap, Sprite, Cursor, Cookie, Lightmap, SingleChannel"),
    },
    async (input) => callUnity("manage_texture", input)
  );

  // ── refresh_unity ──

  server.tool(
    "refresh_unity",
    "Force Unity to refresh AssetDatabase — reimport changed assets, recompile scripts. Use after external file changes.",
    {
      import_all: z.boolean().optional().describe("Force reimport all assets (slower, default: false)"),
    },
    async (input) => callUnity("refresh_unity", input)
  );

  // ── list_menu_items ──

  server.tool(
    "list_menu_items",
    "List all registered Unity Editor MenuItem paths. Discovers what menu commands exist from installed packages (VRCFury, Modular Avatar, Poiyomi, etc.) and user scripts. Use with execute_menu_item to run them.",
    {
      filter: z.string().optional().describe("Filter menu items by path substring (e.g. 'VRCFury', 'Modular Avatar', 'Tools', 'Poiyomi')"),
      limit: z.number().int().min(1).max(1000).optional().describe("Max results (default: 200)"),
    },
    async (input) => callUnity("list_menu_items", input)
  );

  // ── inspect_type ──

  server.tool(
    "inspect_type",
    "Inspect any C# type's API surface: methods (with parameter signatures), properties, fields. Works on installed package types (VRCFuryComponent, ModularAvatarMergeAnimator, VRCAvatarDescriptor, etc.), Unity types, and user scripts. Essential for learning how to call package APIs via execute_csharp.",
    {
      type: z.string().min(1).describe("Type name to inspect (short or fully qualified, e.g. 'Camera', 'VRCFuryComponent', 'ModularAvatarMergeAnimator', 'TextureImporterCompression')"),
      include: z.enum(["all", "methods", "properties", "fields"]).optional().describe("What to include (default: all)"),
      filter: z.string().optional().describe("Filter members by name substring"),
      include_inherited: z.boolean().optional().describe("Include inherited members from base classes (default: false — declared only)"),
    },
    async (input) => callUnity("inspect_type", input)
  );

  // ── list_custom_tools ──

  server.tool(
    "list_custom_tools",
    "Discover user-registered custom MCP tools. Scans project assemblies for static methods tagged with [McpTool] attribute. These are project-specific automation tools created by users.",
    {
      rescan: z.boolean().optional().describe("Force re-scan assemblies (default: false, uses cache)"),
    },
    async (input) => callUnity("list_custom_tools", input)
  );

  // ── execute_custom_tool ──

  server.tool(
    "execute_custom_tool",
    "Execute a user-registered custom MCP tool by name. Use list_custom_tools first to discover available tools.",
    {
      tool_name: z.string().min(1).describe("Name of the custom tool to execute"),
      args: z.record(z.unknown()).optional().describe("Arguments to pass to the custom tool as JSON object"),
    },
    async (input) => callUnity("execute_custom_tool", input)
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

  // ── Phase 4: New tool families ──

  server.tool(
    "unity_validation",
    "Project + scene audit. Actions: missing_scripts, broken_refs, duplicate_names, empty_renderers, missing_textures_on_materials, audit_active_scene.",
    {
      action: z.enum([
        "missing_scripts", "broken_refs", "duplicate_names",
        "empty_renderers", "missing_textures_on_materials", "audit_active_scene"
      ]).describe("Validation action"),
      include_inactive: z.boolean().optional(),
      folder: z.string().optional(),
    },
    async (input) => callUnity("unity_validation", input)
  );

  server.tool(
    "unity_cleaner",
    "Find/remove orphan assets, unused materials, empty folders, internal-error-shader materials. Delete actions require confirm=true.",
    {
      action: z.enum([
        "find_orphans", "find_unused_materials", "find_empty_folders",
        "find_internal_error_shaders", "delete_orphans", "delete_empty_folders"
      ]).describe("Cleaner action"),
      folder: z.string().optional().describe("Restrict scope to a folder (default Assets)"),
      confirm: z.boolean().optional().describe("Required true for delete actions"),
    },
    async (input) => callUnity("unity_cleaner", input)
  );

  server.tool(
    "unity_optimization",
    "Mesh + texture + draw-call audit. Actions: mesh_audit, texture_audit, draw_call_estimate, scene_summary, oversized_textures.",
    {
      action: z.enum(["mesh_audit", "texture_audit", "draw_call_estimate", "scene_summary", "oversized_textures"]),
      triangle_threshold: z.number().int().optional().describe("Triangles threshold for mesh_audit"),
      max_size: z.number().int().optional().describe("Max texture size for oversized_textures"),
      folder: z.string().optional(),
    },
    async (input) => callUnity("unity_optimization", input)
  );

  server.tool(
    "unity_profiler",
    "Profiler samplers + memory + frame timing. Actions: read_sampler, frame_timing, memory_snapshot, list_recorders.",
    {
      action: z.enum(["read_sampler", "frame_timing", "memory_snapshot", "list_recorders"]),
      name: z.string().optional().describe("Sampler name for read_sampler (e.g. 'Camera.Render')"),
    },
    async (input) => callUnity("unity_profiler", input)
  );

  server.tool(
    "unity_debug",
    "Diagnostic queries. Actions: count_objects, find_null_components, active_camera, layer_collision_matrix, render_pipeline.",
    {
      action: z.enum(["count_objects", "find_null_components", "active_camera", "layer_collision_matrix", "render_pipeline"]),
    },
    async (input) => callUnity("unity_debug", input)
  );

  server.tool(
    "unity_importer",
    "Generic AssetImporter SerializedProperty editor. Actions: get_properties, set_property, get_importer_type.",
    {
      action: z.enum(["get_properties", "set_property", "get_importer_type"]),
      asset_path: z.string().describe("Asset path (e.g. 'Assets/Textures/x.png')"),
      property_path: z.string().optional(),
      prefix: z.string().optional(),
      value: z.unknown().optional(),
    },
    async (input) => callUnity("unity_importer", input)
  );

  server.tool(
    "unity_build_manage",
    "Build settings + scripting defines. Actions: get_defines, set_defines, add_define, remove_define, get_target, switch_target, list_targets, get_scenes.",
    {
      action: z.enum([
        "get_defines", "set_defines", "add_define", "remove_define",
        "get_target", "switch_target", "list_targets", "get_scenes"
      ]),
      define: z.string().optional(),
      defines: z.array(z.string()).optional(),
      target: z.string().optional(),
    },
    async (input) => callUnity("unity_build_manage", input)
  );

  server.tool(
    "unity_ui",
    "uGUI scaffolding. Actions: create_canvas, create_panel, create_button, create_text, create_image, set_anchor, set_rect.",
    {
      action: z.enum(["create_canvas", "create_panel", "create_button", "create_text", "create_image", "set_anchor", "set_rect"]),
      name: z.string().optional(),
      parent: z.string().optional(),
      label: z.string().optional(),
      text: z.string().optional(),
      instanceId: z.number().int().optional(),
      min_x: z.number().optional(), min_y: z.number().optional(),
      max_x: z.number().optional(), max_y: z.number().optional(),
      sizeDelta: z.object({ x: z.number(), y: z.number() }).optional(),
      anchoredPosition: z.object({ x: z.number(), y: z.number() }).optional(),
    },
    async (input) => callUnity("unity_ui", input)
  );

  server.tool(
    "unity_physics",
    "Physics scaffolding. Actions: add_rigidbody, add_collider, set_gravity, get_gravity, set_ignore_layer_collision, get_physics_settings.",
    {
      action: z.enum(["add_rigidbody", "add_collider", "set_gravity", "get_gravity", "set_ignore_layer_collision", "get_physics_settings"]),
      name: z.string().optional(),
      instanceId: z.number().int().optional(),
      type: z.string().optional().describe("Collider type: box|sphere|capsule|mesh"),
      mass: z.number().optional(),
      use_gravity: z.boolean().optional(),
      is_kinematic: z.boolean().optional(),
      is_trigger: z.boolean().optional(),
      gravity: z.object({ x: z.number(), y: z.number(), z: z.number() }).optional(),
      layer_a: z.number().int().optional(),
      layer_b: z.number().int().optional(),
      ignore: z.boolean().optional(),
    },
    async (input) => callUnity("unity_physics", input)
  );

  server.tool(
    "unity_navmesh",
    "NavMesh actions: bake, clear, info, list_agent_types.",
    {
      action: z.enum(["bake", "clear", "info", "list_agent_types"]),
    },
    async (input) => callUnity("unity_navmesh", input)
  );

  server.tool(
    "unity_terrain",
    "Terrain actions: list, info, create, set_height, flatten.",
    {
      action: z.enum(["list", "info", "create", "set_height", "flatten"]),
      name: z.string().optional(),
      asset_path: z.string().optional(),
      height: z.number().optional(),
      heightmap_resolution: z.number().int().optional(),
      alphamap_resolution: z.number().int().optional(),
      size: z.object({ x: z.number(), y: z.number(), z: z.number() }).optional(),
    },
    async (input) => callUnity("unity_terrain", input)
  );

  server.tool(
    "unity_lighting",
    "Lighting management. Actions: list_lights, create_light, set_ambient, get_ambient, set_skybox, get_skybox.",
    {
      action: z.enum(["list_lights", "create_light", "set_ambient", "get_ambient", "set_skybox", "get_skybox"]),
      name: z.string().optional(),
      type: z.string().optional().describe("Light type: Directional, Point, Spot, Area"),
      intensity: z.number().optional(),
      range: z.number().optional(),
      color: z.object({ r: z.number(), g: z.number(), b: z.number(), a: z.number() }).optional(),
      material_path: z.string().optional(),
    },
    async (input) => callUnity("unity_lighting", input)
  );

  server.tool(
    "unity_camera",
    "Camera + SceneView. Actions: list, create, sceneview_focus, sceneview_pose, sceneview_align_with_view.",
    {
      action: z.enum(["list", "create", "sceneview_focus", "sceneview_pose", "sceneview_align_with_view"]),
      name: z.string().optional(),
      camera: z.string().optional(),
      fov: z.number().optional(),
      tag_main: z.boolean().optional(),
    },
    async (input) => callUnity("unity_camera", input)
  );

  server.tool(
    "unity_event",
    "UnityEvent persistent listeners (e.g. Button.onClick). Actions: list_persistent, add_persistent, remove_persistent.",
    {
      action: z.enum(["list_persistent", "add_persistent", "remove_persistent"]),
      source: z.string().describe("GameObject hosting the event"),
      event_field: z.string().describe("Event field name (e.g. 'onClick')"),
      component_type: z.string().optional(),
      target_object: z.string().optional(),
      target_component_type: z.string().optional(),
      method_name: z.string().optional(),
      index: z.number().int().optional(),
    },
    async (input) => callUnity("unity_event", input)
  );

  server.tool(
    "unity_cinemachine",
    "Cinemachine helpers (reflection-based; works without package, returns 'not installed'). Actions: detect, list_vcams, create_vcam, set_priority.",
    {
      action: z.enum(["detect", "list_vcams", "create_vcam", "set_priority"]),
      name: z.string().optional(),
      priority: z.number().int().optional(),
    },
    async (input) => callUnity("unity_cinemachine", input)
  );

  server.tool(
    "unity_timeline",
    "Timeline helpers (reflection-based). Actions: detect, list_directors, create_director, bind_timeline_asset.",
    {
      action: z.enum(["detect", "list_directors", "create_director", "bind_timeline_asset"]),
      name: z.string().optional(),
      asset_path: z.string().optional(),
    },
    async (input) => callUnity("unity_timeline", input)
  );

  server.tool(
    "unity_smart",
    "High-level predicate queries. Actions: meshes_over_tris, renderers_with_shader, objects_with_component, materials_using_texture, find_in_layer.",
    {
      action: z.enum(["meshes_over_tris", "renderers_with_shader", "objects_with_component", "materials_using_texture", "find_in_layer"]),
      min_tris: z.number().int().optional(),
      shader: z.string().optional(),
      component_type: z.string().optional(),
      texture_path: z.string().optional(),
      layer: z.string().optional(),
      folder: z.string().optional(),
    },
    async (input) => callUnity("unity_smart", input)
  );

  server.tool(
    "unity_perception",
    "One-shot world snapshot for AI context. Actions: snapshot, scene_digest, project_digest.",
    {
      action: z.enum(["snapshot", "scene_digest", "project_digest"]).optional(),
      hierarchy_depth: z.number().int().optional(),
    },
    async (input) => callUnity("unity_perception", input)
  );

  server.tool(
    "unity_workflow",
    "Workflow recordings under Library/MCP_Workflows. Actions: list, save, load, delete, append_step, replay.",
    {
      action: z.enum(["list", "save", "load", "delete", "append_step", "replay"]),
      name: z.string().optional(),
      description: z.string().optional(),
      tool: z.string().optional(),
      params: z.record(z.unknown()).optional(),
      note: z.string().optional(),
      steps: z.array(z.object({
        tool: z.string(),
        params: z.record(z.unknown()).optional(),
        note: z.string().optional(),
      })).optional(),
    },
    async (input) => callUnity("unity_workflow", input)
  );

  // ── Phase 2: Checkpoint tool ──

  server.tool(
    "manage_checkpoint",
    "Scene/asset checkpoints stored under Library/MCP_Checkpoints. Actions: create, list, get, restore, diff, delete, delete_all, disk_usage.",
    {
      action: z.enum(["create", "list", "get", "restore", "diff", "delete", "delete_all", "disk_usage"])
        .describe("Checkpoint action"),
      id: z.string().optional().describe("Checkpoint id (required for get/restore/diff/delete)"),
      label: z.string().optional().describe("Human-readable label for create"),
      trigger: z.string().optional().describe("Tool name that triggered the checkpoint"),
      clientId: z.string().optional().describe("Client id that triggered the checkpoint"),
    },
    async (input) => callUnity("manage_checkpoint", input)
  );

  // ── Phase 7: Generators (pluggable IGenerator scaffold) ──

  server.tool(
    "manage_generator",
    "Pluggable asset generator surface (sprite/texture/material/cubemap/audio/animation/model/terrain_layer). " +
      "Stubs ship by default; drop real IGenerator implementations under Editor/Generators/. " +
      "Actions: list, generate, get_config, set_provider, set_output_dir.",
    {
      action: z.enum(["list", "generate", "get_config", "set_provider", "set_output_dir"])
        .describe("Generator action"),
      kind: z.string().optional().describe("Generator kind: sprite | texture | material | cubemap | audio | animation | model | terrain_layer"),
      prompt: z.string().optional().describe("Generation prompt (required for generate)"),
      provider: z.string().optional().describe("Provider override (e.g. 'openai-dalle3'); falls back to GeneratorConfig"),
      outputAssetPath: z.string().optional().describe("Target asset path; defaults to <defaultOutputDirectory>/<kind>_<timestamp>"),
      options: z.record(z.unknown()).optional().describe("Provider-specific options bag"),
      path: z.string().optional().describe("New default output directory (must start with 'Assets/'); used by set_output_dir"),
    },
    async (input) => callUnity("manage_generator", input)
  );

  // ── Phase 1: Server governance tools ──

  server.tool(
    "manage_mcp_mode",
    "Get or set the server-wide operating mode (Ask = read-only, Agent = full). Actions: get, set_ask, set_agent.",
    {
      action: z.enum(["get", "set_ask", "set_agent"]).describe("Mode action"),
    },
    async (input) => callUnity("manage_mcp_mode", input)
  );

  server.tool(
    "manage_mcp_clients",
    "Inspect/approve/deny/revoke MCP clients tracked by the Unity bridge. Actions: list, get, approve, deny, revoke, set_tool_override.",
    {
      action: z.enum(["list", "get", "approve", "deny", "revoke", "set_tool_override"])
        .describe("Client action"),
      clientId: z.string().optional().describe("Client identifier"),
      tool: z.string().optional().describe("Tool name for set_tool_override"),
      value: z.string().optional().describe("Override value: allow, deny, ask, default"),
    },
    async (input) => callUnity("manage_mcp_clients", input)
  );

  server.tool(
    "manage_mcp_permissions",
    "Manage permission auto-approve flags + global per-tool overrides. Actions: get, set_auto_approve_mutate, set_auto_approve_destructive, set_auto_approve_new_clients, set_global_tool_override.",
    {
      action: z.enum([
        "get",
        "set_auto_approve_mutate",
        "set_auto_approve_destructive",
        "set_auto_approve_new_clients",
        "set_global_tool_override",
      ]).describe("Permission action"),
      value: z.union([z.boolean(), z.string()]).optional().describe("Value (bool for auto-approve flags, string for tool override)"),
      tool: z.string().optional().describe("Tool name for set_global_tool_override"),
    },
    async (input) => callUnity("manage_mcp_permissions", input)
  );

  server.tool(
    "list_tools_with_metadata",
    "List every Unity-side tool with full metadata: mode (read/mutate/destructive), category, description, implementation type. Supersedes list_capabilities for runtime discovery.",
    {
      filter: z.string().optional().describe("Substring filter on tool name"),
      category: z.string().optional().describe("Filter by ToolCategory name (e.g. 'Vrchat', 'Asset', 'Editor')"),
    },
    async (input) => callUnity("list_tools_with_metadata", input)
  );

  // ── Phase 3: Skills ──

  server.tool(
    "list_skills",
    "List all available expert skills (VRChat avatar, VRCFury, Modular Avatar, Poiyomi, Cinemachine, UI, physics, performance, mobile, etc.). Filter by category or free-text search.",
    {
      category: z.string().optional().describe("Filter by category (e.g. 'vrchat', 'unity-core', 'instruction')"),
      search: z.string().optional().describe("Free-text search across id/name/description/prompt"),
    },
    async (input) => {
      const skills = listSkills(input);
      return toTextResult({
        count: skills.length,
        skills: skills.map((s: Skill) => ({
          id: s.id,
          name: s.name,
          category: s.category,
          description: s.description,
          recommendedTools: s.recommendedTools,
          requiredPackages: s.requiredPackages,
        })),
      });
    }
  );

  server.tool(
    "invoke_skill",
    "Activate a skill: returns its system prompt + recommended tools + examples for the AI to follow. Use list_skills to discover ids.",
    {
      id: z.string().min(1).describe("Skill id (e.g. 'vrchat-avatar', 'vrchat-upload-recipe', 'cinemachine')"),
    },
    async (input) => {
      const result = invokeSkill(input.id);
      if (!result.ok) {
        return { content: [{ type: "text" as const, text: result.error }], isError: true };
      }
      return {
        content: [{ type: "text" as const, text: result.inject }],
      };
    }
  );

  server.tool(
    "get_skill",
    "Fetch the full JSON definition of a skill (system prompt, recommendedTools, examples, requiredPackages).",
    {
      id: z.string().min(1).describe("Skill id"),
    },
    async (input) => {
      const skill = findSkill(input.id);
      if (!skill) {
        return { content: [{ type: "text" as const, text: `Skill '${input.id}' not found.` }], isError: true };
      }
      return toTextResult({ skill });
    }
  );

  // ── Phase 5: MCP Resources (@-mentionable from clients that support resources) ──

  const resourceText = (mime: string, uri: string, payload: unknown) => ({
    contents: [{
      uri,
      mimeType: mime,
      text: typeof payload === "string" ? payload : JSON.stringify(payload, null, 2),
    }],
  });

  server.resource(
    "scene-active",
    "unity://scene/active",
    { description: "Active Unity scene snapshot (name, path, dirty flag, root GameObjects)." },
    async (uri) => {
      const r = await bridge.call({ tool: "manage_scene", params: { action: "inspect_active_scene" } });
      return resourceText("application/json", uri.href, r);
    }
  );

  server.resource(
    "project-structure",
    "unity://project/structure",
    { description: "Asset folder tree of the active project." },
    async (uri) => {
      const r = await bridge.call({ tool: "get_project_structure", params: {} });
      return resourceText("application/json", uri.href, r);
    }
  );

  server.resource(
    "compilation-errors",
    "unity://compilation/errors",
    { description: "Current Unity compilation errors with file paths and line numbers." },
    async (uri) => {
      const r = await bridge.call({ tool: "get_compilation_errors", params: {} });
      return resourceText("application/json", uri.href, r);
    }
  );

  server.resource(
    "avatar-active",
    "unity://avatar/active",
    { description: "Scan of the active VRChat avatar: descriptor, params, frameworks, meshes, shaders." },
    async (uri) => {
      const r = await bridge.call({ tool: "scan_avatar", params: {} });
      return resourceText("application/json", uri.href, r);
    }
  );

  server.resource(
    "screenshot-scene",
    "unity://screenshot/scene",
    { description: "PNG capture of the Scene view (base64 inside JSON)." },
    async (uri) => {
      const r = await bridge.call({ tool: "capture_screenshot", params: { source: "scene", width: 1024, height: 768 } });
      return resourceText("application/json", uri.href, r);
    }
  );

  server.resource(
    "screenshot-game",
    "unity://screenshot/game",
    { description: "PNG capture of the Game view (base64 inside JSON)." },
    async (uri) => {
      const r = await bridge.call({ tool: "capture_screenshot", params: { source: "game", width: 1024, height: 768 } });
      return resourceText("application/json", uri.href, r);
    }
  );

  server.resource(
    "perception",
    "unity://perception/snapshot",
    { description: "One-shot AI context: editor state + scene digest + project digest." },
    async (uri) => {
      const r = await bridge.call({ tool: "unity_perception", params: { action: "snapshot" } });
      return resourceText("application/json", uri.href, r);
    }
  );

  // ── Phase 5: MCP Prompts (slash-command style starters) ──

  server.prompt(
    "skill",
    "Activate one of the registered expert skills (run list_skills first to discover ids).",
    {
      id: z.string().describe("Skill id, e.g. 'vrchat-upload-recipe', 'vrchat-avatar', 'cinemachine'."),
    },
    async ({ id }) => {
      const result = invokeSkill(id);
      const text = result.ok
        ? result.inject
        : `Skill '${id}' not found. Call list_skills to see available skills.`;
      return {
        messages: [{ role: "user" as const, content: { type: "text" as const, text } }],
      };
    }
  );

  server.prompt(
    "vrchat-fix-upload",
    "Pre-flight checklist for a VRChat avatar upload: scan, validate, optimize, report. Read-only.",
    {},
    async () => {
      const skill = invokeSkill("vrchat-upload-recipe");
      return {
        messages: [{
          role: "user" as const,
          content: {
            type: "text" as const,
            text: skill.ok ? skill.inject : "Run scan_avatar, unity_validation.audit_active_scene, unity_optimization.scene_summary in order. Do not auto-fix; report findings and let the user approve.",
          },
        }],
      };
    }
  );

  server.prompt(
    "optimize-avatar",
    "Audit + suggest optimizations for the active VRChat avatar.",
    {},
    async () => {
      const skill = invokeSkill("vrchat-quest");
      return {
        messages: [{
          role: "user" as const,
          content: {
            type: "text" as const,
            text: (skill.ok ? skill.inject : "") +
              "\n\n## First step\nCall scan_avatar, then unity_optimization with actions mesh_audit, texture_audit, oversized_textures. Group findings by severity (Block/Warn/Info).",
          },
        }],
      };
    }
  );

  server.prompt(
    "perception-snapshot",
    "Inject a one-shot world snapshot (editor + scene + project) before further reasoning.",
    {},
    async () => {
      const r = await bridge.call({ tool: "unity_perception", params: { action: "snapshot" } });
      return {
        messages: [{
          role: "user" as const,
          content: {
            type: "text" as const,
            text: `# Unity world snapshot\n\n\`\`\`json\n${JSON.stringify(r, null, 2)}\n\`\`\``,
          },
        }],
      };
    }
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
}
