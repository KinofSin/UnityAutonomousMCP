export interface ToolCapability {
  tool: string;
  category: "editor" | "assets" | "scene" | "script" | "runtime" | "agent";
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
  }
];

export function isKnownTool(tool: string): boolean {
  return TOOL_CAPABILITIES.some((entry) => entry.tool === tool);
}

export function isDestructiveTool(tool: string): boolean {
  const match = TOOL_CAPABILITIES.find((entry) => entry.tool === tool);
  return Boolean(match?.destructive);
}
