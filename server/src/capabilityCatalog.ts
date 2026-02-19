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
    notes: "Scene inspection, load, save, and active-scene operations."
  },
  {
    tool: "manage_gameobject",
    category: "editor",
    destructive: false,
    supportsBatch: true,
    notes: "Create/create_primitive, find/find_by_name, set_transform, and destroy lifecycle actions."
  },
  {
    tool: "manage_script",
    category: "script",
    destructive: true,
    supportsBatch: true,
    notes: "Create/update/delete script files. Deletion requires explicit policy override."
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
