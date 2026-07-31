// Shared MCP bridge client: one request path with reconnect + busy handling.
//
// Why this exists: every harness used to call fetch() once and hard-fail with
// "bridge unreachable — open Unity". But the bridge legitimately disappears
// mid-run. Any AssetDatabase write can trigger a domain reload, which tears the
// HTTP listener down and brings it back seconds later. A one-shot call turns
// that routine blip into a dead optimization pass and a lost measurement.
//
// Retry safety is the whole design constraint here — a blind retry of a mutating
// tool could apply it twice. Failures are classified by whether the editor can
// possibly have executed the tool:
//
//   refused  — nothing listening on the port. The request was never delivered,
//              so a retry cannot double-apply. The ONLY provably safe case.
//   busy     — our HTTP 503 {busy:true}. Do not read this as "the tool did not run":
//              AutonomousMcpMainThread throws TimeoutException when it gives up
//              *waiting*, but the queued action is never cancelled and may still be
//              executing. Ambiguous, so idempotent-only.
//   timeout  — connection established, then no answer in time. Same reasoning.
//   network  — socket died mid-flight. Same reasoning.
//
// Env: BRIDGE=http://127.0.0.1:8080/mcp/tool

export const DEFAULT_BRIDGE = process.env.BRIDGE || "http://127.0.0.1:8080/mcp/tool";

const UNITY_HINT =
  "  Open Unity 2022.3.22f1 with the package mounted, then Window > Autonomous MCP > Settings > Server > Connect.";

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

function classify(err) {
  if (err?.name === "TimeoutError" || err?.name === "AbortError") return "timeout";
  const cause = String(err?.cause?.code ?? err?.code ?? "");
  if (cause === "ECONNREFUSED" || cause === "ENOTFOUND" || cause === "EADDRNOTAVAIL") return "refused";
  // Undici reports a listener that vanished mid-handshake this way; treat it as
  // "never delivered" like a refusal rather than as an ambiguous failure.
  if (cause === "ECONNRESET" && err?.message?.includes("other side closed")) return "refused";
  return "network";
}

async function attempt(tool, params, { bridge, client, timeoutMs }) {
  let res;
  try {
    res = await fetch(bridge, {
      method: "POST",
      headers: { "Content-Type": "application/json", "X-MCP-Client": client },
      body: JSON.stringify({ tool, params }),
      signal: AbortSignal.timeout(timeoutMs),
    });
  } catch (e) {
    return { ok: false, kind: classify(e), message: String(e?.message ?? e) };
  }

  let json = null;
  try {
    json = await res.json();
  } catch {
    if (res.status === 503) return { ok: false, kind: "busy", message: "editor main thread busy", status: 503 };
    return { ok: false, kind: "nonjson", message: `bridge returned non-JSON (HTTP ${res.status})`, status: res.status };
  }

  if (res.status === 503 || json?.busy === true) {
    return { ok: false, kind: "busy", message: json?.error ?? "editor main thread busy", status: 503, json };
  }
  if (!res.ok) {
    return { ok: false, kind: "http", message: json?.error ?? `HTTP ${res.status}`, status: res.status, json };
  }
  if (!json?.success) {
    // The tool ran and said no. Not a transport problem, so never retried.
    return { ok: false, kind: "tool", message: json?.error ?? JSON.stringify(json), json };
  }

  return { ok: true, json, data: json.data ?? {} };
}

/**
 * Call a bridge tool, riding out domain reloads and busy periods.
 * Resolves to { ok:true, json, data } or { ok:false, kind, message, ... }.
 */
export async function request(tool, params = {}, opts = {}) {
  const {
    bridge = DEFAULT_BRIDGE,
    client = "mcp-harness",
    timeoutMs = 20000,
    reconnectMs = 90000,
    idempotent = false,
    onRetry = null,
  } = opts;

  const deadline = Date.now() + reconnectMs;
  let delay = 1000;
  let attempts = 0;
  let last;

  for (;;) {
    last = await attempt(tool, params, { bridge, client, timeoutMs });
    attempts++;
    if (last.ok) {
      if (attempts > 1 && onRetry) onRetry({ tool, attempts, recovered: true });
      return last;
    }

    const ambiguous = last.kind === "busy" || last.kind === "timeout" || last.kind === "network";
    const retryable = last.kind === "refused" || (idempotent && ambiguous);
    if (!retryable || Date.now() + delay > deadline) {
      last.attempts = attempts;
      return last;
    }

    if (onRetry) onRetry({ tool, attempts, kind: last.kind, waitMs: delay });
    await sleep(delay);
    delay = Math.min(Math.round(delay * 1.5), 5000);
  }
}

/** Human-readable failure text, including the "is Unity even open" hint. */
export function describe(result, bridge = DEFAULT_BRIDGE) {
  if (!result || result.ok) return "";
  switch (result.kind) {
    case "refused":
      return `bridge unreachable at ${bridge} (no listener${result.attempts > 1 ? `, gave up after ${result.attempts} attempts` : ""})\n${UNITY_HINT}`;
    case "busy":
      return `editor main thread stayed busy${result.attempts > 1 ? ` across ${result.attempts} attempts` : ""} — a test run or long import is holding it.`;
    case "timeout":
      return `bridge timed out at ${bridge} (${result.message}). The editor may still be working; re-run to check.`;
    case "network":
      return `bridge connection failed at ${bridge} (${result.message})`;
    case "nonjson":
    case "http":
      return result.message;
    default:
      return result.message;
  }
}

/** Wait for the bridge to answer a health_check. Returns the payload or null. */
export async function waitForBridge(opts = {}) {
  const { waitMs = 120000, ...rest } = opts;
  const r = await request("health_check", {}, { ...rest, reconnectMs: waitMs, idempotent: true });
  return r.ok ? r.data : null;
}

/**
 * Build a call() with fixed defaults, matching the shape the harnesses already use:
 * resolves to the tool's data, or hands the failure text to onFail (which should exit).
 */
export function makeCaller({ onFail, ...defaults }) {
  return async function call(tool, params = {}, timeoutMs, extra = {}) {
    const r = await request(tool, params, {
      ...defaults,
      ...extra,
      ...(timeoutMs ? { timeoutMs } : {}),
    });
    if (!r.ok) onFail(describe(r, defaults.bridge ?? DEFAULT_BRIDGE), r);
    return r.data ?? {};
  };
}

/** Tools that only read editor state, so an ambiguous failure is safe to retry. */
export const READ_ONLY_TOOLS = new Set([
  "health_check",
  "read_console",
  "get_test_job",
  "get_compilation_errors",
  "search_hierarchy",
  "unity_perception",
  "scan_avatar",
  "scan_armature",
  "unity_optimization",
  "get_project_structure",
  "get_asset_info",
  "list_shaders",
  "list_materials",
  "get_installed_packages",
  "list_menu_items",
  "inspect_type",
  "read_script",
]);
