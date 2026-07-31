#!/usr/bin/env node
// SessionStart notice for the VRChat optimization loop.
//
// A loop can be interrupted by a domain reload, a crash, or simply the end of a
// session. The measurements survive on disk, so surface any tracked target at
// session start - otherwise a half-finished optimization is silently abandoned
// and the next session re-baselines on top of partly-modified assets.
//
// Prints nothing when there is no state, so it stays invisible during unrelated
// work. Always exits 0; this hook is informational and must never block.
import { readFileSync, existsSync, readdirSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const STATE_DIR = join(dirname(fileURLToPath(import.meta.url)), "..", ".vrc-state");

function main() {
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
    const last = s.passes?.length ? s.passes[s.passes.length - 1] : null;
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
