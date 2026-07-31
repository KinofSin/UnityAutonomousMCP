#!/usr/bin/env node
// Console/compile-error measurement for the unity-compile-fix loop.
//
// Uses read_console { level: "error" }, NOT get_compilation_errors.
// get_compilation_errors reads a stale last-good assembly and can report
// false-clean (see CLAUDE.md).
//
// Usage:
//   node .claude/tools/unity-verify.mjs           # refresh + read errors
//   node .claude/tools/unity-verify.mjs --no-refresh
//
// Exit: 0 clean  1 errors present  2 bridge unreachable / tool error
//
// Env: BRIDGE=http://127.0.0.1:8080/mcp/tool  CLIENT=unity-verify
import { request, describe, READ_ONLY_TOOLS, DEFAULT_BRIDGE } from "./bridge.mjs";

const BRIDGE = DEFAULT_BRIDGE;
const CLIENT = process.env.CLIENT || "unity-verify";
const EXIT_OK = 0;
const EXIT_ERRORS = 1;
const EXIT_ERROR = 2;

// A recompile is a domain reload, which drops the listener — the one thing this
// harness is guaranteed to run into. Ride it out instead of reporting "Unity is
// closed" at the exact moment Unity is doing what we asked.
async function call(tool, params = {}, timeoutMs = 20000) {
  const r = await request(tool, params, {
    client: CLIENT,
    timeoutMs,
    reconnectMs: 120000,
    idempotent: READ_ONLY_TOOLS.has(tool) || tool === "refresh_unity",
    onRetry: ({ kind, recovered }) =>
      console.error(recovered ? "  (bridge back)" : `  (bridge ${kind}, retrying…)`),
  });
  if (!r.ok) {
    console.error(r.kind === "tool" ? `error: ${tool} failed: ${r.message}` : `error: ${describe(r, BRIDGE)}`);
    process.exit(EXIT_ERROR);
  }
  return r.data ?? {};
}

async function main() {
  const noRefresh = process.argv.includes("--no-refresh") || process.argv.includes("-n");
  if (process.argv.includes("-h") || process.argv.includes("--help")) {
    console.error("usage: unity-verify.mjs [--no-refresh]\nexit: 0 clean  1 errors  2 bridge/tool error");
    process.exit(EXIT_ERROR);
  }

  if (!noRefresh) {
    const refresh = await call("refresh_unity", {});
    if (refresh?.editor?.isCompiling || refresh?.isCompiling) {
      console.log("note: editor is compiling (focus is not required) — re-run once isCompiling clears.");
    }
  }

  const data = await call("read_console", { level: "error", limit: 50 });
  const count = typeof data.count === "number" ? data.count : (data.entries?.length ?? 0);
  const entries = Array.isArray(data.entries) ? data.entries : [];

  console.log(`console errors: ${count}`);
  const preview = entries.slice(0, 8);
  for (const e of preview) {
    const msg = typeof e === "string" ? e : e?.message ?? e?.condition ?? JSON.stringify(e);
    const file = e?.file ?? e?.path ?? "";
    const line = e?.line ?? e?.lineNumber ?? "";
    const loc = file ? `  (${file}${line !== "" ? ":" + line : ""})` : "";
    console.log(`  - ${String(msg).split("\n")[0]}${loc}`);
  }
  if (entries.length > preview.length) {
    console.log(`  … and ${entries.length - preview.length} more`);
  }

  if (count > 0) {
    console.log("");
    console.log("Fix one root cause, then re-run unity-verify.mjs (it triggers the recompile itself).");
    process.exit(EXIT_ERRORS);
  }
  console.log("clean");
  process.exit(EXIT_OK);
}

main();
