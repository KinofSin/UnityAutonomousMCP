---
name: run-autonomous-unity-mcp
description: Run, build, drive, test, or screenshot the Autonomous Unity MCP. Launches the Node MCP relay (build + offline smoke), and drives the live Unity 2022.3 editor over the MCP bridge — health checks, listing tools, running EditMode tests, generating assets. Use when asked to run/start/build/test/drive this Unity MCP or its server.
---

# Run: Autonomous Unity MCP

Two surfaces, two ways to drive them. **Paths below are relative to the package root** (`UnityAutonomousMCP/`).

- **Node MCP relay** (`server/`) — pure TypeScript; builds and self-tests on any machine, no Unity needed.
- **The live Unity editor** — driven over an HTTP **bridge** the package exposes. A clean machine can't *launch* Unity (licensed editor + GUI), so the live path drives an **already-running** editor that has the package loaded. The harness is `.claude/skills/run-autonomous-unity-mcp/driver.mjs`.

## Prerequisites
- **Node 18+** (uses global `fetch`; tested on Node 22). No npm deps needed to *run* the driver.
- Server build needs the workspace deps: `npm install` (installs `@modelcontextprotocol/sdk`, `zod`, `typescript`).
- Live path only: **Unity 2022.3.22f1** open with this package mounted (see Gotchas) and the bridge connected — `Window > Autonomous MCP > Settings > Server > Connect` (or enable AutoConnect). Bridge defaults to `http://127.0.0.1:8080`.

## Build (Node relay)
```bash
npm install
npm --workspace server run build        # tsc -p; exits 0
```

## Run — offline (no Unity)
The planner/executor smoke runs against a fake bridge — proves the relay end without a live editor:
```bash
node server/dist/smokeTest.js
# → prints {"smoke": true, "message": "Planner/executor smoke scenarios passed (success + failure)."}
```

## Run — agent path (drive the LIVE editor)
With Unity open + bridge connected, use the driver (one bridge call per invocation):
```bash
node .claude/skills/run-autonomous-unity-mcp/driver.mjs health
#   → {"success":true,"data":{"package":"com.autonomous.unity.mcp","unityVersion":"2022.3.22f1", ...}}

node .claude/skills/run-autonomous-unity-mcp/driver.mjs tools
#   → lists registered tools with [mode/category]

node .claude/skills/run-autonomous-unity-mcp/driver.mjs call manage_scene '{"action":"list_scenes"}'
#   ⚠ PowerShell only: inline JSON gets mangled ('{"a":"b"}' arrives as {a:b}, and `<` is a
#   reserved operator). Use a file or stdin instead:
node .claude/skills/run-autonomous-unity-mcp/driver.mjs call manage_scene @params.json
'{"action":"list_scenes"}' | node .claude/skills/run-autonomous-unity-mcp/driver.mjs call manage_scene -

node .claude/skills/run-autonomous-unity-mcp/driver.mjs gen texture "a seamless mossy stone tile, top-down"
#   → writes an asset under Assets/Generated/ and returns its assetPath (keyless Pollinations by default)

node .claude/skills/run-autonomous-unity-mcp/driver.mjs tests editmode
#   → run_tests + polls get_test_job to a terminal status (reload-durable)
```
Override the endpoint/client with `BRIDGE=...` / `CLIENT=...` env vars.

The raw protocol (what the driver does): `POST http://127.0.0.1:8080/mcp/tool` with body `{"tool":"<name>","params":{...}}`.

## Gotchas (paid for in real debugging this session)
- **Drive one bridge call at a time.** The editor dispatches tools on its main thread **serially**; firing concurrent requests starves/timeouts them. The driver is one-call-per-invocation by design.
- **Generation is synchronous, and rapid back-to-back gens currently HANG.** A single `gen` works (~1–3s, keyless Pollinations) — but the *2nd+* generation fired soon after the first hangs until timeout (the public keyless endpoint appears to hold rapid repeat connections; the call blocks on the editor main thread). Verified open issue — three attempted fixes (dispatch-timeout bump, total-budget cap, `KeepAlive=false`) did **not** resolve it; the real fix is to run the network off the main thread + short keyless timeout/backoff. **For now: generate one asset at a time and let it finish before the next.**
- **`run_tests` is now reload-durable.** Jobs persist to `SessionState`, so `get_test_job` survives the domain reload an EditMode run triggers (it used to return "Unknown job"). The driver's `tests` poller tolerates the transient drop during reload.
- **Mounting the package (Unity side):** in the Unity project's `Packages/manifest.json`, reference the **subfolder**, not the repo root:
  `"com.autonomous.unity.mcp": "file:.../UnityAutonomousMCP/com.autonomous-unity.mcp"`. Mounting the repo root makes Unity import `node_modules/` + `server/` → ~40s domain reloads + ~23k GUID conflicts.
- **Registry vs legacy tools:** registry tools (`unity_*`, `manage_generator`, governance) pass through the Ask/Agent permission gate; the ~37 legacy switch tools (`health_check`, `manage_scene`, `run_tests`, …) bypass it. To call Mutate registry tools, the editor must be in **Agent** mode with `autoApproveMutate` (Settings > Clients).
- **Generators are BYOK / keyless-public only.** Keys come from your own `GENERATOR_*` env vars (HuggingFace = your tokens; Pollinations = a free keyless public API). Never harvest third-party keys.

## Troubleshooting
- **Driver `health` throws `TimeoutError` / connection refused:** Unity isn't running, or the bridge isn't connected (Settings > Server > Connect), or it's mid-recompile. Wait for the editor to go idle and retry.
- **`tests` returns "no jobId":** the editor is compiling or in Safe Mode (compile errors) — fix the Console first.
- **Tool returns `permission_denied`:** a Mutate registry tool in Ask mode or from an unapproved client. Switch to Agent + approve the client in Settings > Clients.
- **Server build TS errors after pulling:** run `npm install` (the workspace deps may be missing) then rebuild.

## Driver
`.claude/skills/run-autonomous-unity-mcp/driver.mjs` — zero-dep Node bridge harness (`health` / `call` / `tools` / `gen` / `tests`). It's the agent's handle on the live editor; extend it with new subcommands as needed.

`call` takes params as inline JSON, `@path/to/file.json`, or `-` for stdin. On PowerShell prefer the last two — inline quoting cannot be made to survive. A UTF-8 BOM (which PowerShell redirects add) is stripped before parsing.
```
