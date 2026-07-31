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
import { request, describe, READ_ONLY_TOOLS, DEFAULT_BRIDGE, maybeEnsureBridge } from "./bridge.mjs";

const BRIDGE = DEFAULT_BRIDGE;
const CLIENT = process.env.CLIENT || "unity-verify";
const EXIT_OK = 0;
const EXIT_ERRORS = 1;
const EXIT_ERROR = 2;

// A recompile is a domain reload, which drops the listener — the one thing this
// harness is guaranteed to run into. Ride it out instead of reporting "Unity is
// closed" at the exact moment Unity is doing what we asked.
async function call(tool, params = {}, timeoutMs = 20000) {
  let r = await request(tool, params, {
    client: CLIENT,
    timeoutMs,
    reconnectMs: 120000,
    idempotent: READ_ONLY_TOOLS.has(tool) || tool === "refresh_unity",
    onRetry: ({ kind, recovered }) =>
      console.error(recovered ? "  (bridge back)" : `  (bridge ${kind}, retrying…)`),
  });
  if (!r.ok && r.kind === "refused") {
    const ensured = await maybeEnsureBridge();
    if (ensured.ok) {
      r = await request(tool, params, {
        client: CLIENT,
        timeoutMs,
        reconnectMs: 120000,
        idempotent: READ_ONLY_TOOLS.has(tool) || tool === "refresh_unity",
      });
    }
  }
  if (!r.ok) {
    console.error(r.kind === "tool" ? `error: ${tool} failed: ${r.message}` : `error: ${describe(r, BRIDGE)}`);
    process.exit(EXIT_ERROR);
  }
  return r.data ?? {};
}

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

// Reading the console while Unity is still compiling reports the *previous* compile's
// errors — a false clean on the one tool that exists to catch bad C#. Wait for the editor
// to go idle first. Requires consecutive idle samples because there is a gap between
// refresh_unity returning and the compile actually starting, and another between import
// and compile, where isCompiling reads false while work is still pending.
async function waitForIdle(maxMs = 180000) {
  const deadline = Date.now() + maxMs;
  const settleSamples = 3;
  let idleStreak = 0;
  let waited = false;

  await sleep(750); // let the refresh actually kick off before sampling

  while (Date.now() < deadline) {
    const health = await call("health_check", {});
    const editor = health?.editor ?? {};
    const busy = Boolean(editor.isCompiling || editor.isUpdating);

    if (busy) {
      idleStreak = 0;
      waited = true;
    } else if (++idleStreak >= settleSamples) {
      return { idle: true, waited };
    }
    await sleep(500);
  }
  return { idle: false, waited: true };
}

// The bridge serializes log entries with PascalCase keys (Message/StackTrace/TimestampUtc).
// Accept both casings so a schema tweak degrades to an ugly line rather than a JSON dump.
function formatEntry(e) {
  if (typeof e === "string") return e.split("\n")[0];
  if (!e || typeof e !== "object") return String(e);

  const msg = e.Message ?? e.message ?? e.condition ?? e.Condition;
  if (typeof msg !== "string") return JSON.stringify(e);

  const first = msg.split("\n")[0].trim();

  // Compiler errors already carry "Path/File.cs(line,col): error CSxxxx: text" — rewrite to
  // the conventional file:line form so the location is greppable and reads at a glance.
  const compiler = first.match(/^(.+?)\((\d+),(\d+)\):\s*(.*)$/);
  if (compiler) {
    const [, file, line, , rest] = compiler;
    return `${rest}\n      ${file.replace(/\\/g, "/")}:${line}`;
  }

  const file = e.File ?? e.file ?? e.Path ?? e.path ?? "";
  const line = e.Line ?? e.line ?? e.LineNumber ?? e.lineNumber ?? "";
  return file ? `${first}\n      ${file}${line !== "" ? ":" + line : ""}` : first;
}

async function main() {
  const noRefresh = process.argv.includes("--no-refresh") || process.argv.includes("-n");
  if (process.argv.includes("-h") || process.argv.includes("--help")) {
    console.error("usage: unity-verify.mjs [--no-refresh]\nexit: 0 clean  1 errors  2 bridge/tool error");
    process.exit(EXIT_ERROR);
  }

  if (!noRefresh) {
    const started = Date.now();
    await call("refresh_unity", {});
    const { idle, waited } = await waitForIdle();
    const secs = ((Date.now() - started) / 1000).toFixed(1);
    if (!idle) {
      console.log(`note: editor still busy after ${secs}s — results below may be from the previous compile.`);
    } else if (waited) {
      console.log(`compiled in ${secs}s (focus is not required)`);
    }
  }

  const data = await call("read_console", { level: "error", limit: 50 });
  const count = typeof data.count === "number" ? data.count : (data.entries?.length ?? 0);
  const entries = Array.isArray(data.entries) ? data.entries : [];

  console.log(`console errors: ${count}`);
  const preview = entries.slice(0, 8);
  for (const e of preview) {
    console.log(`  - ${formatEntry(e)}`);
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
