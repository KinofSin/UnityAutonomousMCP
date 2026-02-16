export type ToolName =
  | "batch_execute"
  | "autonomous_plan"
  | "autonomous_execute_plan"
  | "unity_tool_call";

export interface UnityRequest {
  tool: string;
  params: Record<string, unknown>;
  requestId?: string;
}

export interface UnityResponse {
  success: boolean;
  data?: unknown;
  error?: string;
}

export interface AgentGoal {
  goal: string;
  constraints?: string[];
  maxSteps?: number;
}

export interface AgentStep {
  id: string;
  action: string;
  tool: string;
  params: Record<string, unknown>;
  risk: "low" | "medium" | "high";
}

export interface AgentPlan {
  goal: string;
  summary: string;
  steps: AgentStep[];
  constraints?: string[];
}
