export interface ToolCapability {
  tool: string;
  category: "editor" | "assets" | "scene" | "script" | "runtime" | "agent" | "knowledge";
  destructive: boolean;
  supportsBatch: boolean;
  notes: string;
}

export const TOOL_CAPABILITIES: ToolCapability[] = [
  {
    tool: "health_check",
    category: "editor",
    destructive: false,
    supportsBatch: false,
    notes: "Returns package/runtime status and currently supported Unity tool actions."
  },
  {
    tool: "batch_execute",
    category: "agent",
    destructive: false,
    supportsBatch: true,
    notes: "Preferred for grouped operations; mirrors CoplayDev high-throughput pattern."
  },
  {
    tool: "manage_scene",
    category: "scene",
    destructive: false,
    supportsBatch: true,
    notes: "Scene actions: inspect_active_scene, save_active_scene, open_scene (auto-saves dirty scene), list_scenes (find all .unity assets)."
  },
  {
    tool: "manage_gameobject",
    category: "editor",
    destructive: false,
    supportsBatch: true,
    notes: "Expanded lifecycle and hierarchy actions: create/create_empty/create_primitive, find/find_by_name/find_contains, set_transform(local|world), get_world_transform, reparent, get_children/get_parent/get_full_hierarchy, set_active, rename, destroy."
  },
  {
    tool: "manage_component",
    category: "editor",
    destructive: false,
    supportsBatch: true,
    notes: "Component lifecycle: add (by type name), remove, get_all (list components on GO), get_properties (SerializedProperty introspection), set_property (write int/float/bool/string/enum/color/vector/objectRef)."
  },
  {
    tool: "manage_script",
    category: "script",
    destructive: true,
    supportsBatch: true,
    notes: "Create/update/delete script files. Deletion requires explicit policy override."
  },
  {
    tool: "read_script",
    category: "script",
    destructive: false,
    supportsBatch: false,
    notes: "Read any script file by asset path (Assets/...). Returns full contents, line count, and size."
  },
  {
    tool: "manage_asset",
    category: "assets",
    destructive: false,
    supportsBatch: true,
    notes: "Asset actions: find (AssetDatabase filter search with folder/limit), instantiate_prefab (load and place prefab with optional parent/transform)."
  },
  {
    tool: "manage_editor",
    category: "editor",
    destructive: false,
    supportsBatch: true,
    notes: "Editor control: enter_play_mode, exit_play_mode, pause (toggle), step, undo, redo."
  },
  {
    tool: "execute_menu_item",
    category: "editor",
    destructive: true,
    supportsBatch: true,
    notes: "Execute any Unity Editor menu item by path (e.g. 'Tools/My Tool'). Marked destructive as menu items can have side effects."
  },
  {
    tool: "capture_screenshot",
    category: "editor",
    destructive: false,
    supportsBatch: false,
    notes: "Capture Scene or Game view as PNG. Params: source (scene|game), width, height (64-2048), save_path (optional). Returns base64png."
  },
  {
    tool: "manage_animator",
    category: "editor",
    destructive: false,
    supportsBatch: true,
    notes: "Animator introspection: get_parameters (with live values in Play mode), set_parameter (runtime or default), get_layers, get_states (with transitions), get_current_state (Play mode only). Resolves AnimatorOverrideController automatically."
  },
  {
    tool: "manage_material",
    category: "editor",
    destructive: false,
    supportsBatch: true,
    notes: "Material actions: get (info), get_properties (all shader properties with values), set_property (color/float/vector/int), list_materials (all materials on a Renderer). Uses sharedMaterials to avoid leaks."
  },
  {
    tool: "execute_csharp",
    category: "editor",
    destructive: true,
    supportsBatch: false,
    notes: "Execute arbitrary C# code in the Unity Editor. The ultimate escape hatch — code is compiled in-memory and executed immediately. Has access to UnityEngine, UnityEditor, and all loaded assemblies. Returns the result as JSON."
  },
  {
    tool: "search_hierarchy",
    category: "scene",
    destructive: false,
    supportsBatch: false,
    notes: "Deep scene search with filters: name_pattern (regex), component_type, tag, layer, active_only, include_inactive, include_components. Returns matching GameObjects with full context."
  },
  {
    tool: "get_project_structure",
    category: "assets",
    destructive: false,
    supportsBatch: false,
    notes: "Get asset folder tree with file counts and types. Params: path (default 'Assets'), depth (1-10), extensions (comma-separated filter), include_meta."
  },
  {
    tool: "manage_prefab",
    category: "editor",
    destructive: true,
    supportsBatch: true,
    notes: "Prefab workflow: get_status (type, overrides, asset path), open (prefab mode), apply_overrides, revert_overrides, unpack (outermost or completely)."
  },
  {
    tool: "manage_selection",
    category: "editor",
    destructive: false,
    supportsBatch: true,
    notes: "Editor selection: get (current selection), set (by names or instanceIds), clear, focus (frame in scene view)."
  },
  {
    tool: "manage_layer_tag",
    category: "editor",
    destructive: false,
    supportsBatch: true,
    notes: "Layer/tag management: get (layer+tag of GO), set_layer (by name or index, recursive option), set_tag, list_layers, list_tags, list_sorting_layers."
  },
  {
    tool: "get_compilation_errors",
    category: "script",
    destructive: false,
    supportsBatch: false,
    notes: "Get detailed compilation errors with file path and line numbers. Option to include warnings. Parses Unity console for structured error data."
  },
  {
    tool: "manage_project_settings",
    category: "editor",
    destructive: false,
    supportsBatch: false,
    notes: "Read/write project settings: get_player_settings, set_player_setting (companyName, productName, bundleVersion, runInBackground), get_quality_settings, get_physics_settings, get_time_settings."
  },
  {
    tool: "get_installed_packages",
    category: "assets",
    destructive: false,
    supportsBatch: false,
    notes: "List all installed Unity packages from manifest.json with version, source (git/local/registry), and VRC ecosystem identification. Flags 40+ known VRChat packages (MA, VRCFury, AAO, Poiyomi, lilToon, etc.) with descriptions. Also detects loaded framework assemblies."
  },
  {
    tool: "list_shaders",
    category: "assets",
    destructive: false,
    supportsBatch: false,
    notes: "Enumerate all shaders in the project with family detection (Poiyomi, lilToon, SCSS, ORL, etc.). Optional property listing. Filter by name. Identifies VRC ecosystem shaders with descriptions."
  },
  {
    tool: "get_asset_info",
    category: "assets",
    destructive: false,
    supportsBatch: false,
    notes: "Deep inspect any asset by path. For prefabs: hierarchy, components, VRC ecosystem components. For materials: shader, textures, render queue. For textures: dimensions, format, compression. For AnimatorControllers: layers, states, parameters. For AnimationClips: curves, bindings."
  },
  {
    tool: "scan_armature",
    category: "scene",
    destructive: false,
    supportsBatch: false,
    notes: "VRChat avatar armature analysis: bone tree, humanoid rig mapping (all HumanBodyBones), PhysBone chains, SkinnedMeshRenderer info (vertex/blendshape/material counts, root bone). Essential for understanding avatar structure before attaching accessories."
  },
  {
    tool: "scan_avatar",
    category: "scene",
    destructive: false,
    supportsBatch: false,
    notes: "Comprehensive VRChat avatar scan: VRCAvatarDescriptor (lip sync, view position, expression parameters with cost/budget), PhysBones, PhysBoneColliders, Contacts, installed frameworks (MA, VRCFury, AAO, lilycalInventory), mesh stats (polygons, materials, blendshapes), shader usage, bone count."
  },
  {
    tool: "get_vrc_knowledge",
    category: "knowledge",
    destructive: false,
    supportsBatch: false,
    notes: "Query the VRChat ecosystem knowledge base covering 150+ tools across 21 categories. Search by category, tool name, or free text. Returns conventions, best practices, tool descriptions, and best-pick recommendations for shaders, optimization, toggles, expressions, physics, and more."
  },
  {
    tool: "get_install_guide",
    category: "knowledge",
    destructive: false,
    supportsBatch: false,
    notes: "Step-by-step install, setup, and test instructions for 60+ VRChat Unity tools. VPM repo URLs, global prerequisites (Unity Hub, Unity 2022.3.22f1, VCC, ALCOM), common errors & fixes. Query by tool name, section, or search."
  },
  {
    tool: "validate_script",
    category: "script",
    destructive: false,
    supportsBatch: false,
    notes: "Optional strict Roslyn-mode validation support."
  },
  {
    tool: "run_tests",
    category: "script",
    destructive: false,
    supportsBatch: false,
    notes: "Runs EditMode/PlayMode tests and returns job status."
  },
  {
    tool: "get_test_job",
    category: "script",
    destructive: false,
    supportsBatch: false,
    notes: "Fetches execution status and final results for an asynchronous Unity Test Runner job."
  },
  {
    tool: "read_console",
    category: "editor",
    destructive: false,
    supportsBatch: false,
    notes: "Read logs to drive autonomous retry and debugging loops."
  },
  {
    tool: "manage_scriptable_object",
    category: "assets",
    destructive: true,
    supportsBatch: true,
    notes: "Create, find, inspect, and edit ScriptableObject assets. Actions: find (search by filter), get_properties (read all serialized fields via SerializedObject), set_property (write with Undo support), create (new SO asset from type name), list_fields (inspect type schema via reflection). Useful for VRC expression parameters, tool configs, etc."
  },
  {
    tool: "manage_texture",
    category: "assets",
    destructive: true,
    supportsBatch: true,
    notes: "Inspect and modify texture import settings: max size, compression (crunch), sRGB, mipmaps, filter mode, aniso, texture type. Includes Android override info. find_textures searches project. Essential for Quest optimization (downsize textures, enable crunch)."
  },
  {
    tool: "refresh_unity",
    category: "editor",
    destructive: false,
    supportsBatch: true,
    notes: "Force AssetDatabase.Refresh() — reimport changed assets, recompile scripts. Use after external file edits. Optional import_all for full force reimport."
  },
  {
    tool: "list_menu_items",
    category: "editor",
    destructive: false,
    supportsBatch: false,
    notes: "Scan all loaded assemblies for [MenuItem] attributes and return their paths. Discovers installed package menu items (VRCFury, Modular Avatar, Poiyomi, etc.) and user-defined ones. Filter by substring. Use with execute_menu_item to invoke discovered commands."
  },
  {
    tool: "inspect_type",
    category: "knowledge",
    destructive: false,
    supportsBatch: true,
    notes: "Reflect on any C# type: methods (with full parameter signatures, return types), properties (read/write), fields (serialized). Works on all loaded types: Unity API, VRChat SDK, installed packages (VRCFury, MA, AAO, lilToon), user scripts. Supports enums (returns all values). Filter members by name. include_inherited to see base class members."
  },
  {
    tool: "list_custom_tools",
    category: "knowledge",
    destructive: false,
    supportsBatch: false,
    notes: "Discover user-registered custom MCP tools. Scans project assemblies for static methods with [McpTool(name, description)] attribute. Cacheable with rescan option. Returns tool name, description, declaring type, assembly."
  },
  {
    tool: "execute_custom_tool",
    category: "runtime",
    destructive: true,
    supportsBatch: true,
    notes: "Run a user-registered custom tool by name. Method must be static, take JObject args, return JToken/JObject/string. Safe invocation with error handling. Use list_custom_tools first to discover available tools."
  }
];

export function isKnownTool(tool: string): boolean {
  return TOOL_CAPABILITIES.some((entry) => entry.tool === tool);
}

export function isDestructiveTool(tool: string): boolean {
  const match = TOOL_CAPABILITIES.find((entry) => entry.tool === tool);
  return Boolean(match?.destructive);
}
