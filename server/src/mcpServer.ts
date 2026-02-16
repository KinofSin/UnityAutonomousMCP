import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { z } from "zod";
import { runAutonomousGoal } from "./orchestrator.js";
import { createUnityBridgeFromEnv } from "./unityBridge.js";
import { TOOL_CAPABILITIES, isKnownTool } from "./capabilityCatalog.js";

const bridge = createUnityBridgeFromEnv();

function toTextResult(payload: unknown) {
  return {
    content: [{ type: "text" as const, text: JSON.stringify(payload, null, 2) }]
  };
}

export async function startMcpServer(): Promise<void> {
  const server = new McpServer({
    name: "unity-autonomous-agent",
    version: "0.1.0"
  });

  server.tool(
    "list_capabilities",
    "List current autonomous Unity MCP capabilities and tool metadata.",
    async () => toTextResult({ capabilities: TOOL_CAPABILITIES })
  );

  server.tool(
    "autonomous_plan",
    "Build and execute a bounded autonomous plan against Unity tools.",
    {
      goal: z.string().min(1),
      constraints: z.array(z.string()).optional(),
      maxSteps: z.number().int().min(1).max(50).optional(),
      allowDestructive: z.boolean().optional(),
      stopOnError: z.boolean().optional()
    },
    async (input: {
      goal: string;
      constraints?: string[];
      maxSteps?: number;
      allowDestructive?: boolean;
      stopOnError?: boolean;
    }) => {
      const { goal, constraints, maxSteps, allowDestructive, stopOnError } = input;
      const result = await runAutonomousGoal(
        { goal, constraints, maxSteps },
        bridge,
        { allowDestructive, stopOnError }
      );

      return toTextResult(result);
    }
  );

  server.tool(
    "unity_tool_call",
    "Call a Unity tool directly through the bridge (single tool invocation).",
    {
      tool: z.string().min(1),
      params: z.record(z.unknown()).optional()
    },
    async (input: { tool: string; params?: Record<string, unknown> }) => {
      const { tool, params } = input;
      if (!isKnownTool(tool)) {
        return toTextResult({
          success: false,
          error: `Unknown tool '${tool}'. Call list_capabilities for supported tools.`
        });
      }

      const response = await bridge.call({
        tool,
        params: params ?? {}
      });

      return toTextResult(response);
    }
  );

  const transport = new StdioServerTransport();
  await server.connect(transport);
}
