export interface ReadConsoleParams {
  [key: string]: unknown;
  level: "all" | "log" | "warning" | "error";
  limit: number;
}

export interface ManageSceneParams {
  [key: string]: unknown;
  action: "inspect_active_scene" | "save_active_scene";
}

export interface ManageScriptParams {
  [key: string]: unknown;
  action: "create_or_update";
  scriptPath: string;
  contents: string;
}

export interface ValidateScriptParams {
  [key: string]: unknown;
  strict: boolean;
}

export interface RunTestsParams {
  [key: string]: unknown;
  mode: "editmode" | "playmode";
}

export interface GetTestJobParams {
  [key: string]: unknown;
  jobId: string;
}

export interface BatchOperation {
  tool: string;
  params: Record<string, unknown>;
}

export interface BatchExecuteParams {
  [key: string]: unknown;
  operations: BatchOperation[];
}

export function defaultScriptPath(): string {
  return "Assets/Scripts/Generated/AgentGeneratedAction.cs";
}

export function generatedScriptTemplate(goal: string): string {
  return [
    "using UnityEngine;",
    "",
    "public sealed class AgentGeneratedAction : MonoBehaviour",
    "{",
    "    [SerializeField] private string goal = \"" + goal.replace(/\"/g, "\\\"") + "\";",
    "",
    "    public void ApplyGoal()",
    "    {",
    "        Debug.Log($\"[AgentGeneratedAction] Goal: {goal}\");",
    "    }",
    "}"
  ].join("\n");
}
