import { buildPlan } from "./planner.js";
import { executePlan } from "./executor.js";
import { MockUnityBridge, createUnityBridgeFromEnv } from "./unityBridge.js";
import { startMcpServer } from "./mcpServer.js";
import type { AgentGoal } from "./types.js";

function parseGoalFromArgs(args: string[]): AgentGoal {
  const joined = args.join(" ").trim();
  if (!joined) {
    return {
      goal: "Inspect active scene, validate scripts, and run tests",
      constraints: ["No destructive operations"],
      maxSteps: 8
    };
  }

  return {
    goal: joined,
    constraints: ["No destructive operations"],
    maxSteps: 8
  };
}

async function main(): Promise<void> {
  if (process.argv.includes("--mcp")) {
    await startMcpServer();
    return;
  }

  const goal = parseGoalFromArgs(process.argv.slice(2));
  const plan = buildPlan(goal);
  const bridge = process.argv.includes("--mock")
    ? new MockUnityBridge()
    : createUnityBridgeFromEnv();
  const report = await executePlan(plan, bridge, {
    allowDestructive: false,
    stopOnError: true
  });

  process.stdout.write(
    `${JSON.stringify(
      {
        mode: "autonomous-bootstrap",
        plan,
        report,
        note: "Next step: wire this core into @modelcontextprotocol/sdk transport handlers"
      },
      null,
      2
    )}\n`
  );
}

main().catch((error: unknown) => {
  const message = error instanceof Error ? error.message : String(error);
  process.stderr.write(`Autonomous MCP bootstrap failed: ${message}\n`);
  process.exit(1);
});
