import { buildPlan } from "./planner.js";
import { executePlan } from "./executor.js";
import { MockUnityBridge, createUnityBridgeFromEnv } from "./unityBridge.js";
import { startMcpServer, startMcpSseServer } from "./mcpServer.js";
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
  // MCP server modes (mutually exclusive with autonomous bootstrap):
  //   --mcp       : stdio MCP transport (default for Cascade / Claude Desktop)
  //   --mcp-sse   : SSE MCP transport on $MCP_SSE_PORT (default 18008)
  if (process.argv.includes("--mcp-sse")) {
    const port = Number(process.env.MCP_SSE_PORT ?? "18008");
    await startMcpSseServer(port);
    return;
  }
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
