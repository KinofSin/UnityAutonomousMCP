#!/usr/bin/env node
// ENFORCE layer for the VRChat optimization loop.
//
// Two events, one script:
//   PreToolUse  - block a Tier-2 (component-level) mutation when a VRChat
//                 optimization loop is active and no checkpoint exists yet.
//   PostToolUse - record the checkpoint marker, but only after a
//                 manage_checkpoint create actually SUCCEEDED. PreToolUse
//                 cannot see results, so marking there would let a failed
//                 checkpoint satisfy the guard.
//
// Scoping is deliberate: the guard is inert unless .claude/.vrc-state/ holds a
// baseline. Without that, this hook must never interfere with unrelated work in
// the repo (package edits, server builds, generator runs).
//
// Manual in-editor checkpoints (HUD / menu) never pass through Claude's
// PostToolUse, so when no local marker exists we query the bridge
// (manage_checkpoint list) for a recent checkpoint before denying.
// Fail open if Unity is closed.
import { readFileSync, writeFileSync, mkdirSync, existsSync, readdirSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const BRIDGE = process.env.BRIDGE || "http://127.0.0.1:8080/mcp/tool";
const CLAUDE_DIR = join(dirname(fileURLToPath(import.meta.url)), "..");
const STATE_DIR = join(CLAUDE_DIR, ".vrc-state");
const RECENT_MS = 30 * 60 * 1000; // accept a checkpoint created in the last 30 minutes

const GUARDED = [
  { tool: "manage_component", actions: ["remove"] },
  { tool: "manage_gameobject", actions: ["destroy"] },
  { tool: "manage_prefab", actions: ["unpack", "apply_overrides", "revert_overrides"] },
  { tool: "manage_script", actions: ["delete"] },
  { tool: "execute_csharp", actions: null },
  { tool: "execute_menu_item", actions: null },
];

function readStdin() {
  try {
    return readFileSync(0, "utf8");
  } catch {
    return "";
  }
}

function allow() {
  process.exit(0);
}

function block(reason) {
  console.error(reason);
  process.exit(2);
}

function bareToolName(name) {
  const parts = String(name || "").split("__");
  return parts[parts.length - 1];
}

function loopActive() {
  if (!existsSync(STATE_DIR)) return false;
  return readdirSync(STATE_DIR).some((f) => f.endsWith(".json"));
}

function markerPath(sessionId) {
  return join(STATE_DIR, `.checkpoint-${String(sessionId || "nosession").replace(/[^a-zA-Z0-9-]/g, "")}`);
}

function describeCall(payload) {
  const tool = bareToolName(payload.tool_name);
  const input = payload.tool_input ?? {};

  if (tool === "Bash") {
    const cmd = String(input.command ?? "");
    if (!/driver\.mjs|\/mcp\/tool|vrc-loop\.mjs/.test(cmd)) return null;
    for (const g of GUARDED) {
      if (!cmd.includes(g.tool)) continue;
      if (!g.actions) return { tool: g.tool, action: null, via: "bash" };
      const hit = g.actions.find((a) => new RegExp(`["']?action["']?\\s*[:=]\\s*["']?${a}\\b`).test(cmd));
      if (hit) return { tool: g.tool, action: hit, via: "bash" };
    }
    return null;
  }

  return { tool, action: input.action ?? null, via: "tool" };
}

function isGuarded(call) {
  if (!call) return false;
  const g = GUARDED.find((x) => x.tool === call.tool);
  if (!g) return false;
  if (!g.actions) return true;
  return call.action != null && g.actions.includes(String(call.action));
}

// Returns { ok:true, checkpoint } | { ok:true, checkpoint:null } | { ok:false }
// ok:false means bridge unreachable — caller must fail open.
async function recentBridgeCheckpoint() {
  try {
    const res = await fetch(BRIDGE, {
      method: "POST",
      headers: { "Content-Type": "application/json", "X-MCP-Client": "require-checkpoint" },
      body: JSON.stringify({ tool: "manage_checkpoint", params: { action: "list" } }),
      signal: AbortSignal.timeout(4000),
    });
    const json = await res.json();
    if (!json?.success) return { ok: false };
    const list = json?.data?.checkpoints ?? json?.data?.manifests ?? [];
    if (!Array.isArray(list) || !list.length) return { ok: true, checkpoint: null };
    const now = Date.now();
    for (const m of list) {
      const ts = Date.parse(m.createdUtc ?? m.createdAt ?? m.created ?? "");
      if (Number.isFinite(ts) && now - ts < RECENT_MS) return { ok: true, checkpoint: m };
    }
    // Timestamps missing: accept newest-first entry from CheckpointStore.List.
    return { ok: true, checkpoint: list[0] ?? null };
  } catch {
    return { ok: false };
  }
}

async function main() {
  const raw = readStdin();
  if (!raw.trim()) allow();

  let payload;
  try {
    payload = JSON.parse(raw);
  } catch {
    allow();
  }

  const event = payload.hook_event_name ?? "";
  const tool = bareToolName(payload.tool_name);

  if (event === "PostToolUse") {
    if (tool !== "manage_checkpoint") allow();
    const action = payload.tool_input?.action;
    if (action !== "create") allow();
    const res = JSON.stringify(payload.tool_response ?? {});
    if (!/"success"\s*:\s*true/.test(res) && !/checkpoint/i.test(res)) allow();
    try {
      mkdirSync(STATE_DIR, { recursive: true });
      writeFileSync(markerPath(payload.session_id), new Date().toISOString() + "\n", "utf8");
    } catch {
      /* best effort */
    }
    allow();
  }

  if (event !== "PreToolUse") allow();
  if (!loopActive()) allow();

  const call = describeCall(payload);
  if (!isGuarded(call)) allow();
  if (existsSync(markerPath(payload.session_id))) allow();

  // Manual HUD/menu checkpoints never write a local marker — check the bridge.
  const bridge = await recentBridgeCheckpoint();
  if (!bridge.ok) allow(); // Unity closed / bridge down — fail open
  if (bridge.checkpoint) {
    try {
      mkdirSync(STATE_DIR, { recursive: true });
      writeFileSync(
        markerPath(payload.session_id),
        (bridge.checkpoint.createdUtc ?? new Date().toISOString()) + "\n",
        "utf8"
      );
    } catch {
      /* best effort */
    }
    allow();
  }

  block(
    [
      `Blocked: ${call.tool}${call.action ? ` (action: ${call.action})` : ""} is a Tier-2/3 change and a VRChat optimization loop is active,`,
      "but no checkpoint has been created in this session (and none recent on the bridge).",
      "",
      'Create one:  manage_checkpoint { action: "create", label: "pre-opt <target>" }',
      "Or: Window > Autonomous MCP > Create Checkpoint / Advisor HUD Checkpoint button.",
      "",
      "Then retry. Tier 3 (geometry, bones, material merges, lightmap rebake) still needs explicit user approval.",
    ].join("\n")
  );
}

main();
