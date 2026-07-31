#!/usr/bin/env node
// Deterministic measurement harness for the VRChat optimization loop.
//
// This is the MEASURE layer. It records a baseline from the live Unity editor,
// then diffs every later pass against it. It never decides what to fix — the
// audit skills do that. Keeping measurement out of the model's context is the
// whole point: the numbers live on disk and cannot drift or be summarized away.
//
// Usage:
//   node .claude/tools/vrc-loop.mjs avatar baseline <goName>
//   node .claude/tools/vrc-loop.mjs avatar measure  <goName>
//   node .claude/tools/vrc-loop.mjs world  baseline [label]
//   node .claude/tools/vrc-loop.mjs world  measure  [label]
//   node .claude/tools/vrc-loop.mjs report [slug]        # offline, no bridge
//   node .claude/tools/vrc-loop.mjs list                 # offline, no bridge
//
// Exit codes (the loop's control flow):
//   0 = improved or unchanged
//   1 = a tracked metric regressed vs baseline
//   2 = usage error, bridge unreachable, or tool error
//
// Env: BRIDGE=http://127.0.0.1:8080/mcp/tool  CLIENT=vrc-loop
import { mkdirSync, readFileSync, writeFileSync, readdirSync, existsSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";
import { request, describe, READ_ONLY_TOOLS, DEFAULT_BRIDGE } from "./bridge.mjs";

const BRIDGE = DEFAULT_BRIDGE;
const CLIENT = process.env.CLIENT || "vrc-loop";
const STATE_DIR = join(dirname(fileURLToPath(import.meta.url)), "..", ".vrc-state");

const EXIT_OK = 0;
const EXIT_REGRESSED = 1;
const EXIT_ERROR = 2;

// Every tracked metric is "lower is better" — that is what makes a plain
// numeric diff a valid fitness signal. `budget` marks VRChat hard caps.
const AVATAR_METRICS = [
  { key: "polygons", label: "Polygons", pick: (d) => d?.meshStats?.totalPolygons },
  { key: "materials", label: "Material slots", pick: (d) => d?.meshStats?.totalMaterials },
  { key: "skinnedMeshes", label: "Skinned meshes", pick: (d) => d?.meshStats?.skinnedMeshRendererCount },
  { key: "meshRenderers", label: "Mesh renderers", pick: (d) => d?.meshStats?.meshRendererCount },
  { key: "blendShapes", label: "Blendshapes", pick: (d) => d?.meshStats?.totalBlendShapes },
  { key: "bones", label: "Bones (transforms)", pick: (d) => d?.totalBoneCount },
  { key: "physBones", label: "PhysBones", pick: (d) => d?.physBoneCount },
  { key: "physBoneColliders", label: "PhysBone colliders", pick: (d) => d?.physBoneColliderCount },
  { key: "contacts", label: "Contacts (recv+send)", pick: (d) => sum(d?.contactReceiverCount, d?.contactSenderCount) },
  { key: "exprParamCost", label: "Expr param cost", pick: (d) => d?.expressionParameterCost, budget: (d) => d?.expressionParameterBudget },
  // scan_avatar reports geometry and components but nothing about textures, so
  // until these existed the loop could not see its own primary Tier-1 lever
  // (max size / crunch via manage_texture): a working texture pass measured as an
  // all-zero delta, and the "stop after two unchanged passes" rule would end the
  // run. Sourced from the dossier, which already computes per-texture bytes.
  { key: "textureVramMB", label: "Texture VRAM (MB)", pick: (d) => d?.textureStats?.vramMB },
  { key: "texturesOver1024", label: "Textures > 1024", pick: (d) => d?.textureStats?.over1024 },
];

const WORLD_METRICS = [
  { key: "triangles", label: "Triangles", pick: (d) => d?.summary?.totalTriangles },
  { key: "vertices", label: "Vertices", pick: (d) => d?.summary?.totalVertices },
  { key: "uniqueMaterials", label: "Unique materials", pick: (d) => d?.summary?.uniqueMaterials },
  { key: "uniqueMeshes", label: "Unique meshes", pick: (d) => d?.summary?.uniqueMeshes },
  { key: "renderers", label: "Renderers", pick: (d) => d?.summary?.renderers },
  { key: "gameObjects", label: "GameObjects", pick: (d) => d?.summary?.gameObjects },
  { key: "drawCalls", label: "Draw call estimate", pick: (d) => d?.drawCalls?.estimate },
  { key: "oversizedTextures", label: "Oversized textures", pick: (d) => d?.oversized?.count },
];

function sum(...vals) {
  const nums = vals.filter((v) => typeof v === "number");
  return nums.length ? nums.reduce((a, b) => a + b, 0) : undefined;
}

function slugify(s) {
  return String(s).trim().toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "") || "unnamed";
}

// The editor dispatches tools serially on its main thread, so callers must
// issue one request at a time. Non-generator tools get a 10s budget in-editor;
// 20s here keeps the client from giving up before the dispatcher does.
//
// Tier-1 levers write to the AssetDatabase, which can trigger a domain reload and
// drop the bridge mid-pass. Losing a baseline to that would waste the whole run,
// so reconnect rather than abort.
async function call(tool, params = {}, timeoutMs = 20000) {
  const r = await request(tool, params, {
    client: CLIENT,
    timeoutMs,
    reconnectMs: 120000,
    idempotent: READ_ONLY_TOOLS.has(tool),
    onRetry: ({ kind, recovered }) =>
      console.error(recovered ? "  (bridge back)" : `  (bridge ${kind}, retrying…)`),
  });
  if (!r.ok) fail(r.kind === "tool" ? `${tool} failed: ${r.message}` : describe(r, BRIDGE));
  return r.data ?? {};
}

function fail(msg) {
  console.error(`error: ${msg}`);
  process.exit(EXIT_ERROR);
}

function statePath(slug) {
  return join(STATE_DIR, `${slug}.json`);
}

// A loop baseline, as opposed to a dossier artifact sharing the same directory.
function isLoopState(s) {
  return !!s && (s.kind === "avatar" || s.kind === "world") && !!s.baseline && Array.isArray(s.passes);
}

// PowerShell redirects write a UTF-8 BOM, which JSON.parse rejects. Strip it rather
// than declaring an otherwise-valid state file corrupt.
function readJson(p) {
  return JSON.parse(readFileSync(p, "utf8").replace(/^\uFEFF/, ""));
}

function loadState(slug) {
  const p = statePath(slug);
  if (!existsSync(p)) return null;
  try {
    return readJson(p);
  } catch (e) {
    fail(`corrupt state file ${p}: ${String(e)}`);
  }
}

// Directory scans must not die on one unreadable file: a stray artifact should never
// make `list` unusable for every real baseline.
function tryLoadState(slug) {
  const p = statePath(slug);
  if (!existsSync(p)) return null;
  try {
    return readJson(p);
  } catch {
    return null;
  }
}

function saveState(slug, state) {
  mkdirSync(STATE_DIR, { recursive: true });
  writeFileSync(statePath(slug), JSON.stringify(state, null, 2) + "\n", "utf8");
}

// Resolve by instanceId every pass. GameObject.Find (used by scan_avatar's
// name path) skips inactive objects, and VRChat PC/Quest twins are normally
// toggled inactive. InstanceIds are NOT stable across domain reloads, so we
// look up fresh from the name each time and keep only the name in state.
async function resolveAvatarInstanceId(name) {
  const escaped = String(name).replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  const found = await call("search_hierarchy", {
    name_pattern: `^${escaped}$`,
    include_inactive: true,
  });
  const matches = found?.matches ?? [];
  if (!matches.length) {
    fail(
      `no GameObject named '${name}' (including inactive). ` +
        "Check the spelling, or that the avatar is in the active scene."
    );
  }
  if (matches.length > 1) {
    const list = matches
      .map((m) => `  ${m.fullPath ?? m.name}  (instanceId=${m.instanceId}, active=${m.activeSelf})`)
      .join("\n");
    fail(`ambiguous name '${name}' matched ${matches.length} objects:\n${list}\nUse a unique name.`);
  }
  const id = matches[0].instanceId;
  if (typeof id !== "number") fail(`search_hierarchy returned no instanceId for '${name}'`);
  if (matches[0].activeSelf === false) {
    console.error(`note: '${name}' is inactive (typical for VRChat twins) — scanning via instanceId ${id}`);
  }
  return id;
}

async function scanAvatar(name) {
  const instanceId = await resolveAvatarInstanceId(name);
  const data = await call("scan_avatar", { instanceId });
  data.textureStats = await scanTextures(instanceId);
  return { raw: data, metrics: extract(AVATAR_METRICS, data), budgets: budgets(AVATAR_METRICS, data) };
}

// Texture memory comes from the dossier, not scan_avatar. Only the summary is kept:
// the per-texture list is large and belongs in the dossier artifact, not in state.
async function scanTextures(instanceId) {
  const d = await call(
    "unity_perception",
    { action: "dossier", instanceId, sections: ["textures"] },
    60000
  );
  const list = d?.sections?.textures?.textures ?? [];
  if (!list.length) return undefined;

  let bytes = 0;
  let over1024 = 0;
  for (const t of list) {
    bytes += Number(t?.runtimeBytes) || 0;
    if ((Number(t?.maxSize) || 0) > 1024) over1024++;
  }
  return {
    count: list.length,
    vramMB: Math.round(bytes / 1048576),
    over1024,
  };
}

// unity_optimization always reports on the ACTIVE scene, so there is no path
// param — three read actions are combined into one world snapshot.
async function scanWorld() {
  const summary = await call("unity_optimization", { action: "scene_summary" });
  const drawCalls = await call("unity_optimization", { action: "draw_call_estimate" });
  const oversized = await call("unity_optimization", { action: "oversized_textures" });
  const data = { summary, drawCalls, oversized };
  return { raw: data, metrics: extract(WORLD_METRICS, data), budgets: budgets(WORLD_METRICS, data), sceneName: summary?.scene };
}

function extract(defs, data) {
  const out = {};
  for (const d of defs) {
    const v = d.pick(data);
    if (typeof v === "number" && Number.isFinite(v)) out[d.key] = v;
  }
  return out;
}

function budgets(defs, data) {
  const out = {};
  for (const d of defs) {
    if (!d.budget) continue;
    const v = d.budget(data);
    if (typeof v === "number" && Number.isFinite(v)) out[d.key] = v;
  }
  return out;
}

function defsFor(kind) {
  return kind === "avatar" ? AVATAR_METRICS : WORLD_METRICS;
}

function fmt(n) {
  return typeof n === "number" ? n.toLocaleString("en-US") : "-";
}

function printTable(kind, state, now) {
  const defs = defsFor(kind);
  const base = state.baseline.metrics;
  const prevPass = state.passes.length ? state.passes[state.passes.length - 1].metrics : null;

  const rows = [];
  for (const d of defs) {
    if (!(d.key in now.metrics) && !(d.key in base)) continue;
    const b = base[d.key];
    const p = prevPass ? prevPass[d.key] : undefined;
    const c = now.metrics[d.key];
    const delta = typeof b === "number" && typeof c === "number" ? c - b : undefined;
    const budget = now.budgets[d.key] ?? state.baseline.budgets?.[d.key];
    rows.push({ label: d.label, b, p, c, delta, budget });
  }

  const w = (vals, min) => Math.max(min, ...vals.map((v) => String(v).length));
  const lw = w(rows.map((r) => r.label), 6);
  const nw = Math.max(7, ...rows.map((r) => Math.max(fmt(r.b).length, fmt(r.p).length, fmt(r.c).length)));

  const head =
    "METRIC".padEnd(lw) + "  " + "BASE".padStart(nw) + "  " + "PREV".padStart(nw) + "  " + "NOW".padStart(nw) + "   DELTA";
  console.log(head);
  console.log("-".repeat(head.length + 4));

  const regressed = [];
  const improved = [];
  for (const r of rows) {
    let mark = "     .";
    if (typeof r.delta === "number" && r.delta !== 0) {
      mark = (r.delta > 0 ? "  +" : "  ") + fmt(r.delta);
      if (r.delta > 0) regressed.push(r.label);
      else improved.push(r.label);
    }
    let line =
      r.label.padEnd(lw) + "  " + fmt(r.b).padStart(nw) + "  " + fmt(r.p).padStart(nw) + "  " + fmt(r.c).padStart(nw) + mark;
    if (typeof r.budget === "number") {
      const over = typeof r.c === "number" && r.c > r.budget;
      line += `   [budget ${fmt(r.budget)}${over ? " EXCEEDED" : ""}]`;
    }
    console.log(line);
  }
  return { regressed, improved };
}

function usage(code = EXIT_ERROR) {
  console.error(
    [
      "usage:",
      "  vrc-loop.mjs avatar baseline <goName>    record baseline from live editor",
      "  vrc-loop.mjs avatar measure  <goName>    re-scan and diff vs baseline",
      "  vrc-loop.mjs world  baseline [label]     baseline the ACTIVE scene",
      "  vrc-loop.mjs world  measure  [label]     re-scan and diff the ACTIVE scene",
      "  vrc-loop.mjs report [slug]               print stored state (offline)",
      "  vrc-loop.mjs list                        list tracked targets (offline)",
      "",
      "exit: 0 improved/unchanged  1 regressed  2 error",
    ].join("\n")
  );
  process.exit(code);
}

function cmdList() {
  if (!existsSync(STATE_DIR)) {
    console.log("no tracked targets (.claude/.vrc-state/ is empty)");
    return EXIT_OK;
  }
  const files = readdirSync(STATE_DIR).filter((f) => f.endsWith(".json"));
  const rows = [];
  for (const f of files) {
    const s = tryLoadState(f.replace(/\.json$/, ""));
    // scene-dossier.mjs writes dossier-<slug>.json into this same directory, so
    // identify loop state by shape rather than by extension.
    if (!isLoopState(s)) continue;
    rows.push(`${f.replace(/\.json$/, "").padEnd(28)} ${s.kind}  target=${s.target}  passes=${s.passes.length}  baseline=${s.baseline.at}`);
  }
  if (!rows.length) {
    console.log("no tracked targets (.claude/.vrc-state/ holds no loop baselines)");
    return EXIT_OK;
  }
  for (const row of rows) console.log(row);
  return EXIT_OK;
}

function cmdReport(slug) {
  if (!slug) return cmdList();
  const state = loadState(slug);
  if (!state) fail(`no baseline for '${slug}'. Run a baseline first.`);
  console.log(`${state.kind} '${state.target}'  baseline ${state.baseline.at}  passes ${state.passes.length}`);
  const defs = defsFor(state.kind);
  const last = state.passes.length ? state.passes[state.passes.length - 1] : null;
  for (const d of defs) {
    const b = state.baseline.metrics[d.key];
    if (typeof b !== "number") continue;
    const c = last ? last.metrics[d.key] : b;
    const delta = typeof c === "number" ? c - b : 0;
    console.log(`  ${d.label.padEnd(22)} ${fmt(b)} -> ${fmt(c)}  (${delta > 0 ? "+" : ""}${fmt(delta)})`);
  }
  return EXIT_OK;
}

async function main() {
  const [kind, action, arg] = process.argv.slice(2);

  if (!kind || kind === "-h" || kind === "--help") usage();
  if (kind === "list") process.exit(cmdList());
  if (kind === "report") process.exit(cmdReport(action));
  if (kind !== "avatar" && kind !== "world") usage();
  if (action !== "baseline" && action !== "measure") usage();
  if (kind === "avatar" && !arg) fail("avatar commands need a GameObject name, e.g. avatar baseline LEAF");

  const snap = kind === "avatar" ? await scanAvatar(arg) : await scanWorld();
  const target = kind === "avatar" ? arg : arg || snap.sceneName || "active-scene";
  const slug = `${kind}-${slugify(target)}`;

  if (!Object.keys(snap.metrics).length) {
    fail(`scan returned no numeric metrics for '${target}'. Wrong target name, or the VRChat SDK is not loaded.`);
  }

  const at = new Date().toISOString();

  if (action === "baseline") {
    const existing = loadState(slug);
    if (existing) {
      console.log(`note: replacing existing baseline for '${target}' (${existing.passes.length} pass(es) discarded)`);
    }
    saveState(slug, { kind, target, slug, baseline: { at, metrics: snap.metrics, budgets: snap.budgets }, passes: [] });
    console.log(`baseline recorded for ${kind} '${target}' -> .claude/.vrc-state/${slug}.json`);
    for (const d of defsFor(kind)) {
      if (d.key in snap.metrics) console.log(`  ${d.label.padEnd(22)} ${fmt(snap.metrics[d.key])}`);
    }
    process.exit(EXIT_OK);
  }

  const state = loadState(slug);
  if (!state) fail(`no baseline for ${kind} '${target}'. Run: vrc-loop.mjs ${kind} baseline ${kind === "avatar" ? target : ""}`.trim());

  console.log(`${kind} '${target}'  pass ${state.passes.length + 1}`);
  const { regressed, improved } = printTable(kind, state, snap);

  state.passes.push({ at, metrics: snap.metrics });
  saveState(slug, state);

  console.log("");
  if (regressed.length) {
    console.log(`REGRESSED vs baseline: ${regressed.join(", ")}`);
    console.log("The last change made a tracked metric worse. Restore the checkpoint or justify the trade-off.");
    process.exit(EXIT_REGRESSED);
  }
  console.log(improved.length ? `improved: ${improved.join(", ")}` : "no change vs baseline");
  process.exit(EXIT_OK);
}

main();
