#!/usr/bin/env node
// SessionStart notice for the VRChat optimization loop, plus unverified-work reminders.
//
// A loop can be interrupted by a domain reload, a crash, or simply the end of a
// session. The measurements survive on disk, so surface any tracked target at
// session start - otherwise a half-finished optimization is silently abandoned
// and the next session re-baselines on top of partly-modified assets.
//
// The same applies to code written while Unity was closed: it is easy to believe a
// change is done when it has never been compiled. PENDING-CHECKS.md carries those
// across sessions.
//
// Prints nothing when there is no state, so it stays invisible during unrelated
// work. Always exits 0; this hook is informational and must never block.
import { readFileSync, existsSync, readdirSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const CLAUDE_DIR = join(dirname(fileURLToPath(import.meta.url)), "..");
const STATE_DIR = join(CLAUDE_DIR, ".vrc-state");
const CHECKS_FILE = join(CLAUDE_DIR, "PENDING-CHECKS.md");

// Unchecked "- [ ]" bullets, with their wrapped continuation lines folded in.
function pendingChecks() {
  if (!existsSync(CHECKS_FILE)) return [];
  let text;
  try {
    text = readFileSync(CHECKS_FILE, "utf8");
  } catch {
    return [];
  }

  const items = [];
  let current = null;
  for (const raw of text.split(/\r?\n/)) {
    const open = raw.match(/^\s*-\s\[ \]\s+(.*)$/);
    if (open) {
      if (current) items.push(current);
      current = open[1].trim();
      continue;
    }
    if (/^\s*-\s\[[xX]\]/.test(raw) || /^\s*#/.test(raw) || !raw.trim()) {
      if (current) items.push(current);
      current = null;
      continue;
    }
    if (current) current += " " + raw.trim();
  }
  if (current) items.push(current);
  return items;
}

function reportChecks() {
  const items = pendingChecks();
  if (!items.length) return;

  const shown = items.slice(0, 6);
  console.log(
    [
      `${items.length} pending check(s) from earlier work (.claude/PENDING-CHECKS.md):`,
      ...shown.map((i) => `  - ${i.length > 160 ? i.slice(0, 157) + "..." : i}`),
      ...(items.length > shown.length ? [`  ... and ${items.length - shown.length} more`] : []),
      "Tick a line to '- [x]' once verified so it stops being reported.",
      "",
    ].join("\n")
  );
}

function main() {
  reportChecks();
  if (!existsSync(STATE_DIR)) return;

  const files = readdirSync(STATE_DIR).filter((f) => f.endsWith(".json"));
  if (!files.length) return;

  const lines = [];
  let anyRegressed = false;
  for (const f of files) {
    let s;
    try {
      s = JSON.parse(readFileSync(join(STATE_DIR, f), "utf8"));
    } catch {
      continue;
    }
    // scene-dossier.mjs writes dossier-<slug>.json here too; those are not loop
    // baselines and were being reported as "undefined 'LEAF'".
    if (!s || (s.kind !== "avatar" && s.kind !== "world") || !s.baseline || !Array.isArray(s.passes)) continue;
    const last = s.passes.length ? s.passes[s.passes.length - 1] : null;
    if (!last) {
      lines.push(`  ${s.kind} '${s.target}': baseline recorded, never measured. Run: node .claude/tools/vrc-loop.mjs ${s.kind} measure ${s.kind === "avatar" ? s.target : ""}`.trimEnd());
      continue;
    }
    const regressed = [];
    const improved = [];
    for (const [k, base] of Object.entries(s.baseline?.metrics ?? {})) {
      const now = last.metrics?.[k];
      if (typeof now !== "number") continue;
      if (now > base) regressed.push(k);
      else if (now < base) improved.push(k);
    }
    if (regressed.length) anyRegressed = true;
    const verdict = regressed.length
      ? `REGRESSED (${regressed.join(", ")})`
      : improved.length
        ? `improved (${improved.join(", ")})`
        : "no change";
    lines.push(`  ${s.kind} '${s.target}': ${s.passes.length} pass(es), ${verdict}`);
  }

  if (!lines.length) return;

  console.log(
    [
      "VRChat optimization loop state carried over from a previous session:",
      ...lines,
      "",
      "Inspect with: node .claude/tools/vrc-loop.mjs list",
      ...(anyRegressed
        ? ["A regressed target still has un-restored changes - check its checkpoint before continuing."]
        : []),
    ].join("\n")
  );
}

main();
