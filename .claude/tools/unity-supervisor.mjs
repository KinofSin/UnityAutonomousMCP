#!/usr/bin/env node
// Unity supervisor: make the bridge available, or explain precisely why it is not.
//
// Every other harness assumes a live editor and fails with "open Unity" when there
// is not one — fine when a human is watching, useless when nobody is. This turns
// "Unity is closed" from a dead end into a recoverable state.
//
// The hard part is not launching Unity, it is failing legibly. An unattended launch
// has several failure modes that all look identical from outside (a running editor
// that never answers): AutoConnect off, the Safe Mode prompt after a compile error,
// an expired licence, a stale lock file. Each gets diagnosed from the launch log
// rather than reported as a timeout.
//
//   status              report bridge + editor + config. exit 0 if the bridge answers
//   ensure              bring the bridge up, launching Unity if needed
//   enable-autoconnect  one-time: set the EditorPref the bridge needs to self-start
//
// Config resolution, first hit wins:
//   --project <path> / --editor <path>
//   UNITY_PROJECT_PATH / UNITY_EDITOR_PATH
//   .claude/unity-project.json  { "projectPath": "...", "editorPath": "..." }
//   editorPath alone can be derived from the project's ProjectVersion.txt.

import { spawn, spawnSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { request, waitForBridge, DEFAULT_BRIDGE } from "./bridge.mjs";

const EXIT_OK = 0;
const EXIT_NOT_READY = 1;
const EXIT_CONFIG = 2;

const REPO_ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..", "..");
const CONFIG_PATH = path.join(REPO_ROOT, ".claude", "unity-project.json");
const LOG_DIR = path.join(REPO_ROOT, ".claude", ".vrc-state");

const IS_WINDOWS = process.platform === "win32";

function arg(name) {
  const i = process.argv.indexOf(`--${name}`);
  return i >= 0 && process.argv[i + 1] && !process.argv[i + 1].startsWith("--")
    ? process.argv[i + 1]
    : null;
}

function readJson(file) {
  try {
    return JSON.parse(fs.readFileSync(file, "utf8").replace(/^\uFEFF/, ""));
  } catch {
    return null;
  }
}

// ── config ───────────────────────────────────────────────────────────────────

function resolveProject() {
  const explicit = arg("project") || process.env.UNITY_PROJECT_PATH;
  if (explicit) return path.resolve(explicit);
  const cfg = readJson(CONFIG_PATH);
  return cfg?.projectPath ? path.resolve(cfg.projectPath) : null;
}

function projectVersion(projectPath) {
  const file = path.join(projectPath, "ProjectSettings", "ProjectVersion.txt");
  const text = fs.existsSync(file) ? fs.readFileSync(file, "utf8") : "";
  return text.match(/m_EditorVersion:\s*(\S+)/)?.[1] ?? null;
}

// Derived from the project rather than hardcoded: the editor version is already
// recorded in ProjectVersion.txt, and opening a VRChat project with the wrong one
// silently upgrades it.
function resolveEditor(projectPath) {
  const explicit = arg("editor") || process.env.UNITY_EDITOR_PATH || readJson(CONFIG_PATH)?.editorPath;
  if (explicit) return { path: path.resolve(explicit), source: "configured" };

  if (!projectPath) return { path: null, source: "no project" };
  const version = projectVersion(projectPath);
  if (!version) return { path: null, source: "no ProjectVersion.txt" };

  const candidates = IS_WINDOWS
    ? [
        `C:\\Program Files\\Unity\\Hub\\Editor\\${version}\\Editor\\Unity.exe`,
        `C:\\Program Files (x86)\\Unity\\Hub\\Editor\\${version}\\Editor\\Unity.exe`,
      ]
    : [
        `/Applications/Unity/Hub/Editor/${version}/Unity.app/Contents/MacOS/Unity`,
        `${process.env.HOME}/Unity/Hub/Editor/${version}/Editor/Unity`,
      ];

  for (const candidate of candidates) {
    if (fs.existsSync(candidate)) return { path: candidate, source: `hub ${version}` };
  }
  return { path: null, source: `hub ${version} not installed`, version };
}

// ── editor process state ─────────────────────────────────────────────────────

// Unity keeps Temp/UnityLockfile open while a project is loaded, but leaves it
// behind after a crash — so it proves "maybe", never "yes". The process list is
// authoritative, matched on -projectPath so a second project does not count.
function editorRunningFor(projectPath) {
  if (!IS_WINDOWS) {
    return { running: fs.existsSync(path.join(projectPath, "Temp", "UnityLockfile")), how: "lockfile" };
  }
  const script =
    "Get-CimInstance Win32_Process -Filter \"Name='Unity.exe'\" | " +
    "Select-Object -ExpandProperty CommandLine";
  const out = spawnSync("powershell", ["-NoProfile", "-NonInteractive", "-Command", script], {
    encoding: "utf8",
    timeout: 20000,
  });
  if (out.status !== 0 || typeof out.stdout !== "string") {
    return { running: fs.existsSync(path.join(projectPath, "Temp", "UnityLockfile")), how: "lockfile (process query failed)" };
  }
  const needle = projectPath.toLowerCase().replace(/\\/g, "/");
  const running = out.stdout
    .split(/\r?\n/)
    .some((line) => line.toLowerCase().replace(/\\/g, "/").includes(needle));
  return { running, how: "process list" };
}

// ── launch ───────────────────────────────────────────────────────────────────

function launch(editorPath, projectPath) {
  fs.mkdirSync(LOG_DIR, { recursive: true });
  const logPath = path.join(LOG_DIR, "unity-launch.log");
  try {
    fs.rmSync(logPath, { force: true });
  } catch {
    /* a previous run may still hold it; Unity truncates anyway */
  }

  // Own log file: the shared Editor.log is overwritten by any other editor and is
  // the only evidence of why an unattended launch never produced a bridge.
  const child = spawn(editorPath, ["-projectPath", projectPath, "-logFile", logPath], {
    detached: true,
    stdio: "ignore",
  });
  child.unref();
  return { pid: child.pid, logPath };
}

const BLOCKERS = [
  [/safe\s*mode/i, "Unity is showing the Safe Mode prompt — the project has compile errors and the editor is waiting on a dialog. Fix the C# (unity-verify.mjs) or dismiss it once by hand."],
  [/no valid unity editor license|license is not valid|returning license|sign in/i, "Unity is asking for a licence/sign-in. Activate it once in the GUI; an unattended launch cannot answer that dialog."],
  [/multiple unity instances cannot open the same project|another unity instance/i, "Another editor already holds this project. Close it, or delete a stale Temp/UnityLockfile."],
  [/failed to (initialize|load)|fatal error/i, "The editor reported a fatal startup error — see the log."],
];

// A timeout says nothing actionable; the log almost always does.
function diagnose(logPath) {
  if (!logPath || !fs.existsSync(logPath)) return null;
  let text = "";
  try {
    text = fs.readFileSync(logPath, "utf8").slice(-20000);
  } catch {
    return null;
  }
  for (const [pattern, explanation] of BLOCKERS) {
    if (pattern.test(text)) return explanation;
  }
  return null;
}

// ── commands ─────────────────────────────────────────────────────────────────

async function bridgeUp(timeoutMs = 3000) {
  const r = await request("health_check", {}, { timeoutMs, reconnectMs: 0, idempotent: true });
  return r.ok ? r.data : null;
}

// health_check.projectPath is Application.dataPath (…/Assets). Compare its parent
// to the configured project so a second editor on port 8080 cannot look "ready".
function bridgeProjectRoot(health) {
  const dataPath = health?.projectPath;
  if (!dataPath || typeof dataPath !== "string") return null;
  return path.resolve(path.dirname(dataPath));
}

function sameProject(a, b) {
  if (!a || !b) return false;
  const norm = (p) => path.resolve(p).replace(/\\/g, "/").toLowerCase().replace(/\/+$/, "");
  return norm(a) === norm(b);
}

function reportIdentityMismatch(configured, health) {
  const answering = bridgeProjectRoot(health);
  console.error("bridge is answering for a DIFFERENT project:");
  console.error(`  configured: ${configured}`);
  console.error(`  answering:  ${answering ?? "(unknown)"}`);
  console.error("  Close the other editor, or point UNITY_PROJECT_PATH / unity-project.json at the live one.");
}

function requireConfig() {
  const projectPath = resolveProject();
  if (!projectPath) {
    console.error("no Unity project configured.");
    console.error(`  set UNITY_PROJECT_PATH, pass --project <path>, or write ${CONFIG_PATH}:`);
    console.error('  { "projectPath": "C:/path/to/Project" }');
    process.exit(EXIT_CONFIG);
  }
  if (!fs.existsSync(projectPath)) {
    console.error(`project path does not exist: ${projectPath}`);
    process.exit(EXIT_CONFIG);
  }
  return projectPath;
}

async function cmdStatus() {
  const projectPath = resolveProject();
  const health = await bridgeUp();

  console.log(`bridge:  ${health ? "up" : "down"}  (${DEFAULT_BRIDGE})`);
  if (health) {
    const e = health.editor ?? {};
    const answering = bridgeProjectRoot(health);
    console.log(`  unity=${health.unityVersion} scene=${health.activeScene?.name} dirty=${health.activeScene?.isDirty}`);
    console.log(`  compiling=${e.isCompiling} updating=${e.isUpdating} playing=${e.isPlaying}`);
    if (answering) console.log(`  answering project: ${answering}`);
  }

  if (!projectPath) {
    console.log("project: (not configured)");
    console.log(`  set UNITY_PROJECT_PATH or write ${CONFIG_PATH}`);
    process.exit(health ? EXIT_OK : EXIT_CONFIG);
  }

  const editor = resolveEditor(projectPath);
  const proc = editorRunningFor(projectPath);
  console.log(`project: ${projectPath}`);
  console.log(`  version=${projectVersion(projectPath) ?? "?"}  editor=${editor.path ?? `(${editor.source})`}`);
  console.log(`  editor running: ${proc.running} (${proc.how})`);

  if (health && !sameProject(projectPath, bridgeProjectRoot(health))) {
    reportIdentityMismatch(projectPath, health);
    process.exit(EXIT_NOT_READY);
  }

  if (!health && proc.running) {
    console.log("");
    console.log("Editor is running but the bridge is not answering. Most likely AutoConnect is off:");
    console.log("  Window > Autonomous MCP > Settings > Server > Auto-connect,");
    console.log("  or run: node .claude/tools/unity-supervisor.mjs enable-autoconnect  (editor must be closed)");
  }
  process.exit(health ? EXIT_OK : EXIT_NOT_READY);
}

async function cmdEnsure() {
  const waitMs = Number(arg("wait-ms") ?? 300000);
  const noLaunch = process.argv.includes("--no-launch");
  const projectPath = resolveProject();

  const already = await bridgeUp();
  if (already) {
    if (projectPath && !sameProject(projectPath, bridgeProjectRoot(already))) {
      reportIdentityMismatch(projectPath, already);
      process.exit(EXIT_NOT_READY);
    }
    console.log("bridge already up" + (projectPath ? ` for ${projectPath}` : ""));
    process.exit(EXIT_OK);
  }

  const configured = requireConfig();
  const proc = editorRunningFor(configured);
  let logPath = path.join(LOG_DIR, "unity-launch.log");

  if (proc.running) {
    // Never launch over a live editor: Unity refuses the second instance and the
    // real problem is almost always AutoConnect, which launching cannot fix.
    console.log(`editor already running for ${configured} — waiting for the bridge rather than launching`);
  } else if (noLaunch) {
    console.log("bridge down and editor not running (--no-launch given)");
    process.exit(EXIT_NOT_READY);
  } else {
    const editor = resolveEditor(configured);
    if (!editor.path) {
      console.error(`cannot find a Unity editor: ${editor.source}`);
      console.error("  pass --editor <path> or set UNITY_EDITOR_PATH");
      process.exit(EXIT_CONFIG);
    }
    const started = launch(editor.path, configured);
    logPath = started.logPath;
    console.log(`launched ${editor.path}`);
    console.log(`  project=${configured} pid=${started.pid}`);
    console.log(`  log=${logPath}`);
  }

  const t0 = Date.now();
  const health = await waitForBridge({ waitMs, idempotent: true });
  const secs = ((Date.now() - t0) / 1000).toFixed(0);

  if (health) {
    if (!sameProject(configured, bridgeProjectRoot(health))) {
      reportIdentityMismatch(configured, health);
      process.exit(EXIT_NOT_READY);
    }
    console.log(`bridge up after ${secs}s — unity=${health.unityVersion} scene=${health.activeScene?.name}`);
    process.exit(EXIT_OK);
  }

  console.log(`bridge did not answer within ${secs}s`);
  const blocker = diagnose(logPath);
  if (blocker) {
    console.log(`  cause: ${blocker}`);
  } else {
    console.log("  no known blocker found in the launch log. If the editor is up, AutoConnect is");
    console.log("  probably off: Window > Autonomous MCP > Settings > Server > Auto-connect.");
    if (fs.existsSync(logPath)) console.log(`  log: ${logPath}`);
  }
  process.exit(EXIT_NOT_READY);
}

function cmdEnableAutoConnect() {
  const projectPath = requireConfig();
  const proc = editorRunningFor(projectPath);
  if (proc.running) {
    console.error("an editor already has this project open — Unity cannot open it twice.");
    console.error("  Either close it, or just tick Window > Autonomous MCP > Settings > Server > Auto-connect.");
    process.exit(EXIT_NOT_READY);
  }

  const editor = resolveEditor(projectPath);
  if (!editor.path) {
    console.error(`cannot find a Unity editor: ${editor.source}`);
    process.exit(EXIT_CONFIG);
  }

  fs.mkdirSync(LOG_DIR, { recursive: true });
  const logPath = path.join(LOG_DIR, "unity-autoconnect.log");
  console.log("running Unity in batch mode to set the EditorPref (this takes a minute)…");

  const out = spawnSync(
    editor.path,
    [
      "-batchmode",
      "-quit",
      "-projectPath", projectPath,
      "-logFile", logPath,
      "-executeMethod", "AutonomousMcp.Editor.AutonomousMcpBootstrap.EnableAutoConnect",
    ],
    { stdio: "ignore", timeout: 900000 }
  );

  if (out.status === 0) {
    console.log("AutoConnect enabled — the bridge will now start on its own when Unity loads.");
    process.exit(EXIT_OK);
  }
  console.error(`Unity exited ${out.status}. Log: ${logPath}`);
  const blocker = diagnose(logPath);
  if (blocker) console.error(`  cause: ${blocker}`);
  process.exit(EXIT_NOT_READY);
}

const command = process.argv[2] ?? "status";
switch (command) {
  case "status":
    await cmdStatus();
    break;
  case "ensure":
    await cmdEnsure();
    break;
  case "enable-autoconnect":
    cmdEnableAutoConnect();
    break;
  default:
    console.error("usage: unity-supervisor.mjs [status|ensure|enable-autoconnect]");
    console.error("  --project <path>  --editor <path>  --wait-ms N  --no-launch");
    console.error("exit: 0 bridge ready  1 not ready  2 misconfigured");
    process.exit(EXIT_CONFIG);
}
