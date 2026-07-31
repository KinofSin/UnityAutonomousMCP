#!/usr/bin/env node
// Driver for the Autonomous Unity MCP project.
//
// Two surfaces:
//   1. The Node MCP relay (server/) — build + offline smoke, no Unity needed.
//   2. The LIVE Unity editor, driven over the MCP bridge (HTTP POST /mcp/tool).
//      Requires Unity 2022.3.22f1 open with the package mounted (see SKILL.md)
//      and the bridge connected (Settings > Server > Connect, or AutoConnect).
//
// Usage:
//   node driver.mjs health                         # ping the live editor
//   node driver.mjs call <tool> '<jsonParams>'     # any tool, raw
//   node driver.mjs call <tool> @params.json       # params from a file (quote-safe)
//   node driver.mjs call <tool> -                  # params from stdin
//   node driver.mjs tools [category]               # list_tools_with_metadata
//   node driver.mjs gen <kind> "<prompt>"          # manage_generator generate (writes an asset)
//   node driver.mjs tests [editmode|playmode]      # run_tests + poll (reload-durable)
//
// On PowerShell an inline '{"a":"b"}' argument gets mangled (nested quotes need
// backtick escapes and `<` is a reserved operator), so prefer @file or stdin there.
//
// Env: BRIDGE=http://127.0.0.1:8080/mcp/tool  CLIENT=run-driver
import { readFileSync } from "node:fs";

const BRIDGE = process.env.BRIDGE || "http://127.0.0.1:8080/mcp/tool";
const CLIENT = process.env.CLIENT || "run-driver";
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

// Accepts inline JSON, @path, or "-" for stdin. Strips the BOM that PowerShell
// redirects prepend, which JSON.parse otherwise rejects.
function parseParams(raw) {
  if (!raw) return {};
  let text;
  try {
    if (raw === "-") text = readFileSync(0, "utf8");
    else if (raw.startsWith("@")) text = readFileSync(raw.slice(1), "utf8");
    else text = raw;
  } catch (e) {
    console.error(`could not read params: ${e.message}`);
    process.exit(2);
  }

  text = text.replace(/^\uFEFF/, "").trim();
  if (!text) return {};

  try {
    return JSON.parse(text);
  } catch (e) {
    console.error(`invalid JSON params: ${e.message}`);
    console.error(`received: ${text.slice(0, 200)}`);
    console.error("On PowerShell, pass @params.json or pipe JSON and use '-' instead of inline quotes.");
    process.exit(2);
  }
}

async function call(tool, params = {}, timeoutMs = 155000) {
  const t0 = Date.now();
  try {
    const res = await fetch(BRIDGE, {
      method: "POST",
      headers: { "Content-Type": "application/json", "X-MCP-Client": CLIENT },
      body: JSON.stringify({ tool, params }),
      signal: AbortSignal.timeout(timeoutMs),
    });
    const json = await res.json();
    return { elapsed: (Date.now() - t0) / 1000, json };
  } catch (e) {
    return { elapsed: (Date.now() - t0) / 1000, threw: String(e) };
  }
}

// Issue ONE bridge request at a time. The editor dispatches tools on its main
// thread serially; firing concurrent requests can starve/timeout them.
async function main() {
  const [cmd, a, b] = process.argv.slice(2);
  switch (cmd) {
    case "health": {
      const r = await call("health_check", {}, 15000);
      console.log(JSON.stringify(r.json ?? r, null, 2));
      break;
    }
    case "call": {
      if (!a) { console.error("usage: call <tool> ['<json>' | @params.json | -]"); process.exit(2); }
      const params = parseParams(b);
      const r = await call(a, params);
      console.log(`(${r.elapsed.toFixed(1)}s)`, JSON.stringify(r.json ?? r, null, 2));
      break;
    }
    case "tools": {
      const r = await call("list_tools_with_metadata", a ? { category: a } : {}, 15000);
      const tools = r.json?.data?.tools ?? [];
      console.log(`${tools.length} tools`);
      for (const t of tools) console.log(`  ${t.name}  [${t.mode}/${t.category}]`);
      break;
    }
    case "gen": {
      if (!a || !b) { console.error('usage: gen <kind> "<prompt>"'); process.exit(2); }
      const r = await call("manage_generator", {
        action: "generate", kind: a, prompt: b,
        outputAssetPath: `Assets/Generated/driver_${a}_${Date.now()}`,
        options: { width: 512, height: 512 },
      });
      console.log(`(${r.elapsed.toFixed(1)}s)`, JSON.stringify(r.json ?? r, null, 2));
      break;
    }
    case "tests": {
      const mode = a || "editmode";
      const start = await call("run_tests", { mode }, 15000);
      const jobId = start.json?.data?.jobId;
      if (!jobId) { console.error("no jobId:", JSON.stringify(start.json ?? start)); process.exit(1); }
      console.log("jobId", jobId);
      // get_test_job is reload-durable (SessionState) — survives the domain reload a run triggers.
      const deadline = Date.now() + 240000;
      let job = null;
      while (Date.now() < deadline) {
        await sleep(3000);
        const r = await call("get_test_job", { jobId }, 10000);
        if (!r.json?.success) continue;       // transient during reload
        job = r.json.data.job;
        process.stdout.write(`\r  ${job.status} ${job.completedTests}/${job.totalTests}   `);
        if (job.status === "completed" || job.status === "failed") break;
      }
      console.log("");
      if (!job) { console.error("job lost / timed out"); process.exit(1); }
      console.log(`status=${job.status} total=${job.totalTests} passed=${job.passed} failed=${job.failed} skipped=${job.skipped}`);
      break;
    }
    default:
      console.error(
        "commands: health | call <tool> ['<json>'|@file|-] | tools [category] | " +
        'gen <kind> "<prompt>" | tests [editmode|playmode]'
      );
      process.exit(2);
  }
}
main();
