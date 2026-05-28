import type { AgentGoal, AgentPlan, AgentStep } from "./types.js";
import {
  defaultScriptPath,
  generatedScriptTemplate,
  type BatchExecuteParams,
  type ManageScriptParams,
  type RunTestsParams,
  type ValidateScriptParams
} from "./contracts.js";
import { listSkills, type Skill } from "./skills.js";

const DEFAULT_MAX_STEPS = 12;

/**
 * Phase 6: Planner v2.
 *
 * Three-stage pipeline:
 *   1. Perception stem  — read-only context (perception + console + scene digest).
 *   2. Skill-driven body — top-scoring skill seeds steps from its recommendedTools;
 *                          falls back to legacy keyword inference when no skill matches.
 *   3. Verification tail — unity_validation + optional save batch when mutation occurred.
 *
 * Cost-aware (reads before mutates). Self-correction lives in the executor.
 */

interface CapabilityHint {
  tool: string;
  reads?: boolean;
  defaultParams?: Record<string, unknown>;
  risk: AgentStep["risk"];
}

const TOOL_HINTS: Record<string, CapabilityHint> = {
  read_console: { tool: "read_console", reads: true, defaultParams: { level: "all", limit: 200 }, risk: "low" },
  unity_perception: { tool: "unity_perception", reads: true, defaultParams: { action: "snapshot" }, risk: "low" },
  manage_scene: { tool: "manage_scene", reads: true, defaultParams: { action: "inspect_active_scene" }, risk: "low" },
  scan_avatar: { tool: "scan_avatar", reads: true, defaultParams: {}, risk: "low" },
  scan_armature: { tool: "scan_armature", reads: true, defaultParams: {}, risk: "low" },
  get_vrc_knowledge: { tool: "get_vrc_knowledge", reads: true, defaultParams: {}, risk: "low" },
  get_install_guide: { tool: "get_install_guide", reads: true, defaultParams: {}, risk: "low" },
  unity_validation: { tool: "unity_validation", reads: true, defaultParams: { action: "audit_active_scene" }, risk: "low" },
  unity_optimization: { tool: "unity_optimization", reads: true, defaultParams: { action: "scene_summary" }, risk: "low" },
  unity_cleaner: { tool: "unity_cleaner", reads: true, defaultParams: { action: "find_orphans" }, risk: "low" },
  unity_smart: { tool: "unity_smart", reads: true, defaultParams: { action: "meshes_over_tris", min_tris: 5000 }, risk: "low" },
  unity_profiler: { tool: "unity_profiler", reads: true, defaultParams: { action: "frame_timing" }, risk: "low" },
  unity_debug: { tool: "unity_debug", reads: true, defaultParams: { action: "count_objects" }, risk: "low" },
  list_shaders: { tool: "list_shaders", reads: true, defaultParams: {}, risk: "low" },
  list_menu_items: { tool: "list_menu_items", reads: true, defaultParams: {}, risk: "low" },
  get_compilation_errors: { tool: "get_compilation_errors", reads: true, defaultParams: {}, risk: "low" },
  manage_script: { tool: "manage_script", reads: false, defaultParams: {}, risk: "medium" },
  validate_script: { tool: "validate_script", reads: true, defaultParams: { strict: true }, risk: "low" },
  manage_component: { tool: "manage_component", reads: false, defaultParams: {}, risk: "medium" },
  manage_material: { tool: "manage_material", reads: false, defaultParams: {}, risk: "medium" },
  manage_animator: { tool: "manage_animator", reads: false, defaultParams: { action: "get_layers" }, risk: "low" },
  manage_texture: { tool: "manage_texture", reads: false, defaultParams: {}, risk: "medium" },
  manage_scriptable_object: { tool: "manage_scriptable_object", reads: false, defaultParams: {}, risk: "medium" },
  manage_gameobject: { tool: "manage_gameobject", reads: false, defaultParams: {}, risk: "medium" },
  manage_mcp_mode: { tool: "manage_mcp_mode", reads: true, defaultParams: { action: "get" }, risk: "low" },
  unity_ui: { tool: "unity_ui", reads: false, defaultParams: {}, risk: "medium" },
  unity_event: { tool: "unity_event", reads: true, defaultParams: { action: "list_persistent" }, risk: "low" },
  unity_physics: { tool: "unity_physics", reads: true, defaultParams: { action: "get_physics_settings" }, risk: "low" },
  unity_navmesh: { tool: "unity_navmesh", reads: false, defaultParams: { action: "info" }, risk: "low" },
  unity_terrain: { tool: "unity_terrain", reads: true, defaultParams: { action: "list" }, risk: "low" },
  unity_lighting: { tool: "unity_lighting", reads: true, defaultParams: { action: "list_lights" }, risk: "low" },
  unity_camera: { tool: "unity_camera", reads: true, defaultParams: { action: "list" }, risk: "low" },
  unity_cinemachine: { tool: "unity_cinemachine", reads: true, defaultParams: { action: "detect" }, risk: "low" },
  unity_timeline: { tool: "unity_timeline", reads: true, defaultParams: { action: "detect" }, risk: "low" },
  unity_build_manage: { tool: "unity_build_manage", reads: true, defaultParams: { action: "get_defines" }, risk: "low" },
  unity_importer: { tool: "unity_importer", reads: true, defaultParams: { action: "get_importer_type" }, risk: "low" },
  unity_workflow: { tool: "unity_workflow", reads: true, defaultParams: { action: "list" }, risk: "low" },
  run_tests: { tool: "run_tests", reads: false, defaultParams: { mode: "editmode" }, risk: "low" },
  execute_csharp: { tool: "execute_csharp", reads: false, defaultParams: {}, risk: "high" },
  execute_menu_item: { tool: "execute_menu_item", reads: false, defaultParams: {}, risk: "high" },
};

function normalizeGoal(raw: string): string {
  return raw.trim().replace(/\s+/g, " ");
}

interface SkillScore { skill: Skill; score: number; }

function scoreSkillAgainstGoal(skill: Skill, lowered: string): number {
  let score = 0;
  if (lowered.includes(skill.id.toLowerCase())) score += 10;
  if (lowered.includes(skill.name.toLowerCase())) score += 8;
  if (lowered.includes(skill.category)) score += 2;
  const tokens = skill.description.toLowerCase().split(/[^a-z0-9]+/).filter((t: string) => t.length > 3);
  for (const t of tokens) if (lowered.includes(t)) score += 1;
  for (const pkg of skill.requiredPackages) {
    const last = pkg.split(".").pop()?.toLowerCase();
    if (last && last.length > 2 && lowered.includes(last)) score += 2;
  }
  return score;
}

function pickTopSkill(goal: string): Skill | null {
  const lowered = goal.toLowerCase();
  const skills = listSkills();
  if (skills.length === 0) return null;
  const scored: SkillScore[] = skills.map((s: Skill) => ({ skill: s, score: scoreSkillAgainstGoal(s, lowered) }));
  scored.sort((a, b) => b.score - a.score);
  if (!scored[0] || scored[0].score < 4) return null;
  return scored[0].skill;
}

function buildPerceptionStem(): Array<Omit<AgentStep, "id">> {
  return [
    {
      action: "Capture project + scene snapshot for AI context",
      tool: "unity_perception",
      params: { ...TOOL_HINTS.unity_perception.defaultParams! },
      risk: "low",
    },
    {
      action: "Read recent console messages",
      tool: "read_console",
      params: { ...TOOL_HINTS.read_console.defaultParams! },
      risk: "low",
    },
    {
      action: "Inspect active scene",
      tool: "manage_scene",
      params: { ...TOOL_HINTS.manage_scene.defaultParams! },
      risk: "low",
    },
  ];
}

function stepsFromSkill(skill: Skill): Array<Omit<AgentStep, "id">> {
  const steps: Array<Omit<AgentStep, "id">> = [];
  for (const toolName of skill.recommendedTools) {
    const hint = TOOL_HINTS[toolName];
    if (!hint) continue;
    steps.push({
      action: `[skill:${skill.id}] ${hint.reads ? "Read" : "Mutate"} via ${toolName}`,
      tool: toolName,
      params: { ...(hint.defaultParams ?? {}) },
      risk: hint.risk,
    });
  }
  return steps;
}

function inferLegacyKeywordSteps(goal: string): Array<Omit<AgentStep, "id">> {
  const lowered = goal.toLowerCase();
  const steps: Array<Omit<AgentStep, "id">> = [];

  if (lowered.includes("script") || lowered.includes("code")) {
    const manageScriptParams: ManageScriptParams = {
      action: "create_or_update",
      scriptPath: defaultScriptPath(),
      contents: generatedScriptTemplate(goal),
    };
    const validateScriptParams: ValidateScriptParams = { strict: true };
    steps.push({
      action: "Draft or update target scripts",
      tool: "manage_script",
      params: manageScriptParams,
      risk: "medium",
    });
    steps.push({
      action: "Validate and compile updated scripts",
      tool: "validate_script",
      params: validateScriptParams,
      risk: "low",
    });
  }

  if (lowered.includes("test")) {
    const runTestsParams: RunTestsParams = { mode: "editmode" };
    steps.push({
      action: "Run tests relevant to changes",
      tool: "run_tests",
      params: runTestsParams,
      risk: "low",
    });
  }

  if (lowered.includes("clean") || lowered.includes("orphan")) {
    steps.push({
      action: "Scan for orphan/unused assets",
      tool: "unity_cleaner",
      params: { action: "find_orphans" },
      risk: "low",
    });
  }
  if (lowered.includes("optimize") || lowered.includes("performance")) {
    steps.push({
      action: "Run optimization audit",
      tool: "unity_optimization",
      params: { action: "scene_summary" },
      risk: "low",
    });
  }

  return steps;
}

function verificationTail(): Array<Omit<AgentStep, "id">> {
  return [
    {
      action: "Verify scene + project health",
      tool: "unity_validation",
      params: { ...TOOL_HINTS.unity_validation.defaultParams! },
      risk: "low",
    },
  ];
}

function buildSaveBatch(): Omit<AgentStep, "id"> {
  const batchOps: BatchExecuteParams = {
    operations: [{ tool: "manage_scene", params: { action: "save_active_scene" } }],
  };
  return {
    action: "Save active scene",
    tool: "batch_execute",
    params: batchOps,
    risk: "medium",
  };
}

export function buildPlan(goalInput: AgentGoal): AgentPlan {
  const goal = normalizeGoal(goalInput.goal);
  const maxSteps = Math.max(1, Math.min(goalInput.maxSteps ?? DEFAULT_MAX_STEPS, 50));
  const lowered = goal.toLowerCase();

  const stem = buildPerceptionStem();
  const matchedSkill = pickTopSkill(goal);
  const body = matchedSkill ? stepsFromSkill(matchedSkill) : inferLegacyKeywordSteps(goal);
  const tail = verificationTail();
  const includeSave =
    body.some((s) => s.risk === "medium" || s.risk === "high") ||
    lowered.includes("save") || lowered.includes("commit");

  const all: Array<Omit<AgentStep, "id">> = [
    ...stem,
    ...body,
    ...tail,
    ...(includeSave ? [buildSaveBatch()] : []),
  ];

  const trimmed = all.slice(0, maxSteps);
  const steps: AgentStep[] = trimmed.map((step, index) => ({ id: `step-${index + 1}`, ...step }));

  return {
    goal,
    summary: matchedSkill
      ? `Plan (skill=${matchedSkill.id}, ${steps.length} steps) for: ${goal}`
      : `Plan (${steps.length} steps) for: ${goal}`,
    steps,
    constraints: goalInput.constraints ?? [],
  };
}
