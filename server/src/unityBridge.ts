import type { UnityRequest, UnityResponse } from "./types.js";
import net from "node:net";

export type UnityTransportMode = "http" | "tcp" | "mock";

export interface UnityBridgeOptions {
  mode: UnityTransportMode;
  host: string;
  httpPort: number;
  tcpPort: number;
  timeoutMs: number;
}

export interface UnityBridge {
  call(request: UnityRequest): Promise<UnityResponse>;
}

interface UnityEnvelope {
  requestId: string;
  tool: string;
  params: Record<string, unknown>;
}

function withDefaults(options?: Partial<UnityBridgeOptions>): UnityBridgeOptions {
  return {
    mode: options?.mode ?? "http",
    host: options?.host ?? process.env.UNITY_HOST ?? "127.0.0.1",
    httpPort: options?.httpPort ?? Number(process.env.UNITY_HTTP_PORT ?? process.env.UNITY_PORT ?? "8080"),
    tcpPort: options?.tcpPort ?? Number(process.env.UNITY_TCP_PORT ?? "8081"),
    timeoutMs: options?.timeoutMs ?? Number(process.env.UNITY_TIMEOUT_MS ?? "10000")
  };
}

function toEnvelope(request: UnityRequest): UnityEnvelope {
  return {
    requestId: request.requestId ?? `${Date.now()}-${Math.random().toString(36).slice(2, 8)}`,
    tool: request.tool,
    params: request.params
  };
}

async function callHttp(request: UnityRequest, options: UnityBridgeOptions): Promise<UnityResponse> {
  const envelope = toEnvelope(request);
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), options.timeoutMs);

  try {
    const response = await fetch(`http://${options.host}:${options.httpPort}/mcp/tool`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(envelope),
      signal: controller.signal
    });

    if (!response.ok) {
      return {
        success: false,
        error: `Unity HTTP bridge returned ${response.status} ${response.statusText}`
      };
    }

    const payload = (await response.json()) as UnityResponse;
    return payload;
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    return {
      success: false,
      error: `Unity HTTP bridge error: ${message}`
    };
  } finally {
    clearTimeout(timeout);
  }
}

async function callTcp(request: UnityRequest, options: UnityBridgeOptions): Promise<UnityResponse> {
  const envelope = toEnvelope(request);

  return new Promise<UnityResponse>((resolve) => {
    const socket = new net.Socket();
    let settled = false;
    let buffer = "";

    const finish = (result: UnityResponse) => {
      if (settled) {
        return;
      }
      settled = true;
      socket.destroy();
      resolve(result);
    };

    socket.setTimeout(options.timeoutMs, () => {
      finish({ success: false, error: "Unity TCP bridge timeout." });
    });

    socket.on("error", (error) => {
      finish({ success: false, error: `Unity TCP bridge error: ${error.message}` });
    });

    socket.on("data", (chunk) => {
      buffer += chunk.toString("utf8");
      const separatorIndex = buffer.indexOf("\n");
      if (separatorIndex === -1) {
        return;
      }

      const line = buffer.slice(0, separatorIndex).trim();
      if (!line) {
        finish({ success: false, error: "Unity TCP bridge returned empty response." });
        return;
      }

      try {
        const parsed = JSON.parse(line) as UnityResponse;
        finish(parsed);
      } catch (error) {
        const message = error instanceof Error ? error.message : String(error);
        finish({ success: false, error: `Failed parsing Unity TCP response: ${message}` });
      }
    });

    socket.connect(options.tcpPort, options.host, () => {
      socket.write(`${JSON.stringify(envelope)}\n`);
    });
  });
}

export class RealUnityBridge implements UnityBridge {
  public constructor(private readonly options: UnityBridgeOptions) {}

  public async call(request: UnityRequest): Promise<UnityResponse> {
    if (this.options.mode === "tcp") {
      return callTcp(request, this.options);
    }

    if (this.options.mode === "mock") {
      return new MockUnityBridge().call(request);
    }

    return callHttp(request, this.options);
  }
}

export function createUnityBridgeFromEnv(overrides?: Partial<UnityBridgeOptions>): UnityBridge {
  const mode = (overrides?.mode ?? (process.env.UNITY_TRANSPORT as UnityTransportMode | undefined) ?? "http").toLowerCase();
  const options = withDefaults({ ...overrides, mode: mode as UnityTransportMode });
  return new RealUnityBridge(options);
}

export class MockUnityBridge implements UnityBridge {
  public async call(request: UnityRequest): Promise<UnityResponse> {
    return {
      success: true,
      data: {
        echoedTool: request.tool,
        echoedParams: request.params,
        message: "Mock Unity bridge response (replace with TCP/HTTP bridge)."
      }
    };
  }
}
