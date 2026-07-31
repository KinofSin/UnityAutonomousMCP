#!/usr/bin/env node
// Advisor HUD liveness hook.
//
// The outbox only drains when the agent calls hud_poll. Without a nudge, items
// queued from "What's next?" / Send sit forever while the agent is idle.
//
// Events:
//   UserPromptSubmit - if hudOutbox.pending > 0, inject additionalContext telling
//                      the agent to hud_poll first. Silent when zero / bridge down.
//   Stop             - if items are still unread, block ending the turn once per
//                      ~60s (and honour stop_hook_active) so we cannot loop forever.
//
// Fails open on every error path.
import { readFileSync, writeFileSync, mkdirSync, existsSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const BRIDGE = process.env.BRIDGE || "http://127.0.0.1:8080/mcp/tool";
const CLAUDE_DIR = join(dirname(fileURLToPath(import.meta.url)), "..");
const MARKER = join(CLAUDE_DIR, ".vrc-state", ".hud-stop-block");
const BLOCK_COOLDOWN_MS = 60_000;

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

async function pendingCount() {
  try {
    const res = await fetch(BRIDGE, {
      method: "POST",
      headers: { "Content-Type": "application/json", "X-MCP-Client": "hud-drain" },
      body: JSON.stringify({ tool: "health_check", params: {} }),
      signal: AbortSignal.timeout(4000),
    });
    const json = await res.json();
    const n = json?.hudOutbox?.pending ?? json?.data?.hudOutbox?.pending;
    return typeof n === "number" ? n : 0;
  } catch {
    return -1; // bridge down / error — fail open
  }
}

function inject(context) {
  process.stdout.write(
    JSON.stringify({
      hookSpecificOutput: {
        hookEventName: "UserPromptSubmit",
        additionalContext: context,
      },
    })
  );
  process.exit(0);
}

function blockStop(reason) {
  process.stdout.write(JSON.stringify({ decision: "block", reason }));
  process.exit(0);
}

function recentlyBlocked() {
  if (!existsSync(MARKER)) return false;
  try {
    const ts = Date.parse(readFileSync(MARKER, "utf8").trim());
    if (!Number.isFinite(ts)) return false;
    return Date.now() - ts < BLOCK_COOLDOWN_MS;
  } catch {
    return false;
  }
}

function markBlocked() {
  try {
    mkdirSync(dirname(MARKER), { recursive: true });
    writeFileSync(MARKER, new Date().toISOString() + "\n", "utf8");
  } catch {
    /* best effort */
  }
}

async function main() {
  const raw = readStdin();
  let payload = {};
  try {
    if (raw.trim()) payload = JSON.parse(raw);
  } catch {
    allow();
  }

  const event = payload.hook_event_name ?? "";

  if (event === "UserPromptSubmit") {
    const n = await pendingCount();
    if (n <= 0) allow();
    inject(
      [
        `Advisor HUD has ${n} queued item(s) waiting for you.`,
        "Call hud_poll NOW (before other tools) to drain the outbox, then answer those items.",
        "Quick-asks and Send from Window > Autonomous MCP > Advisor land here — they are not visible until you poll.",
      ].join(" ")
    );
  }

  if (event === "Stop") {
    // Claude Code sets stop_hook_active when this Stop block already forced a
    // continuation — exit 0 immediately to avoid an infinite loop.
    if (payload.stop_hook_active === true) allow();
    if (recentlyBlocked()) allow();

    const n = await pendingCount();
    if (n <= 0) allow();

    markBlocked();
    blockStop(
      `Advisor HUD still has ${n} unread queued item(s). Call hud_poll to drain them before ending the turn.`
    );
  }

  allow();
}

main();
