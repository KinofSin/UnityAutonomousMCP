import type { AgentGoal, AgentPlan, AgentStep } from "./types.js";
import {
  defaultScriptPath,
  generatedScriptTemplate,
  type BatchExecuteParams,
  type ManageSceneParams,
  type ManageScriptParams,
  type ReadConsoleParams,
  type RunTestsParams,
  type ValidateScriptParams
} from "./contracts.js";

const DEFAULT_MAX_STEPS = 8;

function normalizeGoal(raw: string): string {
  return raw.trim().replace(/\s+/g, " ");
}

function inferInitialSteps(goal: string): Array<Omit<AgentStep, "id">> {
  const lowered = goal.toLowerCase();
  const readConsoleParams: ReadConsoleParams = { level: "all", limit: 200 };
  const inspectSceneParams: ManageSceneParams = { action: "inspect_active_scene" };

  const steps: Array<Omit<AgentStep, "id">> = [
    {
      action: "Capture current editor and project state",
      tool: "read_console",
      params: readConsoleParams,
      risk: "low"
    },
    {
      action: "Inspect scene and gameobjects related to goal",
      tool: "manage_scene",
      params: inspectSceneParams,
      risk: "low"
    }
  ];

  if (lowered.includes("script") || lowered.includes("code")) {
    const manageScriptParams: ManageScriptParams = {
      action: "create_or_update",
      scriptPath: defaultScriptPath(),
      contents: generatedScriptTemplate(goal)
    };
    const validateScriptParams: ValidateScriptParams = { strict: true };

    steps.push({
      action: "Draft or update target scripts",
      tool: "manage_script",
      params: manageScriptParams,
      risk: "medium"
    });
    steps.push({
      action: "Validate and compile updated scripts",
      tool: "validate_script",
      params: validateScriptParams,
      risk: "low"
    });
  }

  if (lowered.includes("test")) {
    const runTestsParams: RunTestsParams = { mode: "editmode" };
    steps.push({
      action: "Run tests relevant to changes",
      tool: "run_tests",
      params: runTestsParams,
      risk: "low"
    });
  }

  const batchOps: BatchExecuteParams = {
    operations: [
      {
        tool: "manage_scene",
        params: { action: "save_active_scene" }
      }
    ]
  };

  steps.push({
    action: "Commit grouped scene changes via batch operation",
    tool: "batch_execute",
    params: batchOps,
    risk: "medium"
  });

  return steps;
}

export function buildPlan(goalInput: AgentGoal): AgentPlan {
  const goal = normalizeGoal(goalInput.goal);
  const maxSteps = Math.max(1, Math.min(goalInput.maxSteps ?? DEFAULT_MAX_STEPS, 50));
  const inferred = inferInitialSteps(goal).slice(0, maxSteps);

  const steps: AgentStep[] = inferred.map((step, index) => ({
    id: `step-${index + 1}`,
    ...step
  }));

  return {
    goal,
    summary: `Autonomous plan with ${steps.length} step(s) for: ${goal}`,
    steps,
    constraints: goalInput.constraints ?? []
  };
}
