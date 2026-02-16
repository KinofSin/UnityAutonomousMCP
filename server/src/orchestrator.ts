import { buildPlan } from "./planner.js";
import { executePlan } from "./executor.js";
import type { AgentGoal, AgentPlan } from "./types.js";
import type { UnityBridge } from "./unityBridge.js";

export interface OrchestratorOptions {
  allowDestructive?: boolean;
  stopOnError?: boolean;
}

export interface OrchestratorResult {
  plan: AgentPlan;
  report: Awaited<ReturnType<typeof executePlan>>;
}

export async function runAutonomousGoal(
  goal: AgentGoal,
  bridge: UnityBridge,
  options: OrchestratorOptions = {}
): Promise<OrchestratorResult> {
  const plan = buildPlan(goal);
  const report = await executePlan(plan, bridge, {
    allowDestructive: options.allowDestructive ?? false,
    stopOnError: options.stopOnError ?? true
  });

  return {
    plan,
    report
  };
}
