#!/usr/bin/env node
// Scene/avatar state dossier harness.
//
// Pulls sectioned unity_perception { action: "dossier" } calls over the bridge,
// writes a grep-able markdown + JSON artifact under .claude/.vrc-state/, and
// prints a ~40-line summary. The agent reads the summary (and greps the file);
// full inspector/material detail never lands in the conversation.
//
// Usage:
//   node .claude/tools/scene-dossier.mjs avatar <goName>
//   node .claude/tools/scene-dossier.mjs scene
//   node .claude/tools/scene-dossier.mjs verify [slug]
//
// Exit codes:
//   0 = ok / fresh
//   1 = stale (verify only)
//   2 = usage error, bridge unreachable, or tool error
//
// Env: BRIDGE=http://127.0.0.1:8080/mcp/tool  CLIENT=scene-dossier
import { mkdirSync, readFileSync, writeFileSync, existsSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";
import { request, describe, READ_ONLY_TOOLS, DEFAULT_BRIDGE, maybeEnsureBridge } from "./bridge.mjs";

const BRIDGE = DEFAULT_BRIDGE;
const CLIENT = process.env.CLIENT || "scene-dossier";
const STATE_DIR = join(dirname(fileURLToPath(import.meta.url)), "..", ".vrc-state");

const EXIT_OK = 0;
const EXIT_STALE = 1;
const EXIT_ERROR = 2;

const AVATAR_SECTIONS = [
  "identity",
  "descriptor",
  "frameworks",
  "renderers",
  "materials",
  "material_detail",
  "textures",
  "physbones",
  "animators",
  "budgets",
  "cost",
];

const SCENE_SECTIONS = ["identity", "world", "renderers", "materials", "textures"];

function slugify(s) {
  return String(s).trim().toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "") || "unnamed";
}

// A dossier is a read-only pull across many sections; a domain reload part-way
// through would otherwise discard every section already fetched.
async function call(tool, params = {}, timeoutMs = 20000) {
  let r = await request(tool, params, {
    client: CLIENT,
    timeoutMs,
    reconnectMs: 120000,
    idempotent: READ_ONLY_TOOLS.has(tool),
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
        idempotent: READ_ONLY_TOOLS.has(tool),
      });
    }
  }
  if (!r.ok) fail(r.kind === "tool" ? `${tool} failed: ${r.message}` : describe(r, BRIDGE));
  return r.data ?? {};
}

function fail(msg) {
  console.error(`error: ${msg}`);
  process.exit(EXIT_ERROR);
}

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
    console.error(`note: '${name}' is inactive (typical for VRChat twins) — dossier via instanceId ${id}`);
  }
  return id;
}

async function pullSections(baseParams, sections) {
  const merged = { action: "dossier", sections: {}, truncated: {}, mode: baseParams.mode };
  for (const section of sections) {
    const data = await call("unity_perception", {
      ...baseParams,
      action: "dossier",
      sections: [section],
    });
    if (data?.sections?.[section] !== undefined) {
      merged.sections[section] = data.sections[section];
    }
    if (data?.truncated) {
      for (const [k, v] of Object.entries(data.truncated)) {
        if (v) merged.truncated[k] = true;
      }
    }
    if (data?.target) merged.target = data.target;
    if (data?.instanceId != null) merged.instanceId = data.instanceId;
    if (data?.mode) merged.mode = data.mode;
  }
  return merged;
}

function artifactPaths(slug) {
  return {
    json: join(STATE_DIR, `dossier-${slug}.json`),
    md: join(STATE_DIR, `dossier-${slug}.md`),
  };
}

function writeArtifacts(slug, dossier) {
  mkdirSync(STATE_DIR, { recursive: true });
  const paths = artifactPaths(slug);
  writeFileSync(paths.json, JSON.stringify(dossier, null, 2) + "\n", "utf8");
  writeFileSync(paths.md, toMarkdown(slug, dossier), "utf8");
  return paths;
}

function toMarkdown(slug, d) {
  const lines = [];
  const id = d.sections?.identity ?? {};
  lines.push(`# Dossier: ${d.target ?? "scene"} (${slug})`);
  lines.push("");
  lines.push(`- mode: ${d.mode}`);
  lines.push(`- instanceId: ${d.instanceId ?? "(scene)"}`);
  lines.push(`- scene: ${id.scene?.name ?? "?"}  path=${id.scene?.path ?? "?"}  dirty=${id.scene?.isDirty}`);
  lines.push(`- stateHash: ${id.stateHash ?? "?"}`);
  lines.push(`- timestampUtc: ${id.timestampUtc ?? "?"}`);
  lines.push(`- unity: ${id.unityVersion ?? "?"}  buildTarget=${id.buildTarget ?? "?"}`);
  if (Object.keys(d.truncated || {}).length) {
    lines.push(`- truncated: ${Object.keys(d.truncated).filter((k) => d.truncated[k]).join(", ")}`);
  }
  lines.push("");

  const desc = d.sections?.descriptor;
  if (desc && !desc.note) {
    lines.push("## Descriptor");
    lines.push(`- hasAvatarDescriptor: ${desc.hasAvatarDescriptor}`);
    lines.push(`- lipSync: ${desc.lipSyncType ?? "?"}  visemeMesh: ${desc.visemeMesh ?? "?"}`);
    lines.push(`- expressionsMenu: ${desc.expressionsMenuAsset ?? "(none)"}`);
    const ep = desc.expressionParameters;
    if (ep) {
      lines.push(`- expr params: ${ep.count}  cost=${ep.cost}/${ep.budget}  remaining=${ep.remaining}`);
      for (const p of ep.parameters ?? []) {
        lines.push(`  - ${p.name} (${p.type}, cost=${p.cost})`);
      }
    }
    lines.push("");
  }

  const fw = d.sections?.frameworks;
  if (Array.isArray(fw) && fw.length) {
    lines.push("## Frameworks");
    for (const f of fw) {
      lines.push(`- ${JSON.stringify(f)}`);
    }
    lines.push("");
  }

  const budgets = d.sections?.budgets?.measured;
  if (budgets) {
    lines.push("## Budgets (measured)");
    for (const [k, v] of Object.entries(budgets)) lines.push(`- ${k}: ${v}`);
    lines.push("");
  }

  const cost = d.sections?.cost;
  if (cost?.totals) {
    const pct = (n) => `${Math.round((n ?? 0) * 100)}%`;
    lines.push("## Cost attribution");
    lines.push(
      `- totals: polys=${cost.totals.polygons} (${cost.rank?.polygons}) mats=${cost.totals.materialSlots} (${cost.rank?.materialSlots}) smr=${cost.totals.skinnedMeshes} (${cost.rank?.skinnedMeshes}) bones=${cost.totals?.bones ?? "?"} pb=${cost.totals?.physBones ?? "?"}`
    );
    const off = cost.inactive;
    if (off?.objects) {
      lines.push(
        `- **inactive: ${off.objects} objects = ${off.polygons} polys (${pct(off.shareOfPolygons)})** — driven ${off.driven ?? "?"} (${off.drivenPolygons ?? "?"} polys) / undriven ${off.undriven ?? "?"} (${off.undrivenPolygons ?? "?"} polys). VRChat counts all; only undriven are free to delete.`
      );
    }
    if (Array.isArray(cost.twins) && cost.twins.length) {
      lines.push(`- twins: ${cost.twins.map((t) => `${t.name}${t.active ? "" : " (inactive)"}`).join(", ")} — edits do not propagate`);
    }
    lines.push("");
    lines.push("| object | active | polys | share | mats | driven by | polys if removed | rank after |");
    lines.push("|---|---|---|---|---|---|---|---|");
    for (const c of cost.candidates ?? []) {
      lines.push(
        `| ${c.path} | ${c.active ? "on" : "**OFF**"} | ${c.polygons} | ${pct(c.shareOfPolygons)} | ${c.materialSlots} | ${c.drivenBySummary ?? "-"} | ${c.ifRemoved?.polygons} | ${c.ifRemoved?.polygonRank} |`
      );
    }
    lines.push("");
  }

  const rends = d.sections?.renderers?.renderers ?? [];
  if (rends.length) {
    lines.push(`## Renderers (${rends.length})`);
    lines.push("| path | type | tris | mats | blendshapes | bones | active |");
    lines.push("|---|---|---:|---:|---:|---:|---|");
    const sorted = [...rends].sort((a, b) => (b.tris ?? 0) - (a.tris ?? 0));
    for (const r of sorted) {
      lines.push(
        `| ${r.path} | ${r.type} | ${r.tris} | ${r.materialCount} | ${r.blendshapes ?? 0} | ${r.bones ?? 0} | ${r.active} |`
      );
    }
    lines.push("");
  }

  const mats = d.sections?.materials?.materials ?? [];
  if (mats.length) {
    lines.push(`## Materials (${mats.length})`);
    lines.push("| name | family | locked | shader | renderQueue | usedBy |");
    lines.push("|---|---|---|---|---:|---|");
    for (const m of mats) {
      const used = Array.isArray(m.usedBy) ? m.usedBy.slice(0, 3).join("; ") : "";
      lines.push(
        `| ${m.name} | ${m.family} | ${m.locked} | ${m.displayShader ?? m.shader} | ${m.renderQueue} | ${used} |`
      );
    }
    lines.push("");
  }

  const details = d.sections?.material_detail?.materials ?? [];
  if (details.length) {
    lines.push(`## Material detail (non-default props)`);
    for (const m of details) {
      lines.push(`### ${m.name}  (${m.family}${m.locked ? ", locked" : ""})`);
      if (m.note) lines.push(`> ${m.note}`);
      lines.push(`- suppressedDefaults: ${m.suppressedDefaults}  changed: ${m.changedPropertyCount}`);
      for (const p of m.changedProperties ?? []) {
        lines.push(`  - ${p.name} (${p.type}) = ${JSON.stringify(p.value)}`);
      }
      lines.push("");
    }
  }

  const texs = d.sections?.textures?.textures ?? [];
  if (texs.length) {
    lines.push(`## Textures (${texs.length})`);
    lines.push("| name | WxH | maxSize | androidMax | crunch | runtimeBytes | refs |");
    lines.push("|---|---|---:|---:|---|---:|---|");
    for (const t of texs) {
      const refs = Array.isArray(t.referencedBy) ? t.referencedBy.slice(0, 2).join("; ") : "";
      lines.push(
        `| ${t.name} | ${t.width}x${t.height} | ${t.maxSize ?? "?"} | ${t.androidMaxSize ?? "-"} | ${t.crunch} | ${t.runtimeBytes} | ${refs} |`
      );
    }
    lines.push("");
  }

  const pbs = d.sections?.physbones?.physBones ?? [];
  if (pbs.length) {
    lines.push(`## PhysBones (${pbs.length})`);
    for (const pb of pbs) {
      lines.push(
        `- ${pb.path}  pull=${pb.pull} spring=${pb.spring} stiff=${pb.stiffness} radius=${pb.radius} colliders=${pb.colliders}`
      );
    }
    lines.push("");
  }

  const world = d.sections?.world;
  if (world) {
    lines.push("## World");
    lines.push(`- lights: ${world.lightCount}  probes: ${world.reflectionProbeCount}  audio: ${world.audioSourceCount}`);
    lines.push(`- udon: ${world.udonBehaviourCount}  staticRenderers: ${world.staticRendererCount}`);
    lines.push(`- fog: ${world.fog?.enabled}  skybox: ${world.skybox}`);
    lines.push(`- lightmaps: ${world.lighting?.lightmaps}  bakedGI: ${world.lighting?.bakedGI}`);
    if (Array.isArray(world.topMeshesByTris)) {
      lines.push("- top meshes by tris:");
      for (const m of world.topMeshesByTris.slice(0, 15)) {
        lines.push(`  - ${m.path}: ${m.tris}`);
      }
    }
    lines.push("");
  }

  lines.push("---");
  lines.push("Grep this file for a specific material, mesh path, or property name. Do not re-dump the JSON into chat.");
  lines.push("");
  return lines.join("\n");
}

function printSummary(slug, paths, d) {
  const id = d.sections?.identity ?? {};
  const budgets = d.sections?.budgets?.measured;
  const rends = d.sections?.renderers?.renderers ?? [];
  const mats = d.sections?.materials?.materials ?? [];
  const locked = mats.filter((m) => m.locked).length;
  const top = [...rends].sort((a, b) => (b.tris ?? 0) - (a.tris ?? 0)).slice(0, 5);

  console.log(`dossier ok  slug=${slug}`);
  console.log(`  mode=${d.mode}  target=${d.target ?? "(scene)"}  instanceId=${d.instanceId ?? "-"}`);
  console.log(`  scene=${id.scene?.name ?? "?"}  dirty=${id.scene?.isDirty}  stateHash=${id.stateHash ?? "?"}`);
  if (budgets) {
    console.log(
      `  measured: polys=${budgets.polygons} mats=${budgets.materialSlots} smr=${budgets.skinnedMeshes} bones=${budgets.bones} pb=${budgets.physBones}`
    );
  }
  console.log(`  materials=${mats.length} (locked=${locked})  renderers=${rends.length}`);

  // Surfaced in the inline summary because it is the finding people do not go looking for:
  // a switched-off wardrobe toggle still costs rank, so it reads as free when it is not.
  const cost = d.sections?.cost;
  if (cost?.inactive?.objects) {
    const off = cost.inactive;
    console.log(
      `  inactive cost: ${off.objects} disabled objects = ${off.polygons} polys ` +
        `(${Math.round((off.shareOfPolygons ?? 0) * 100)}% of ${cost.totals?.polygons}) — VRChat counts these`
    );
  }
  if (top.length) {
    console.log("  heaviest renderers:");
    for (const r of top) console.log(`    ${r.tris} tris  ${r.path}`);
  }
  if (Object.keys(d.truncated || {}).length) {
    console.log(`  truncated: ${Object.keys(d.truncated).filter((k) => d.truncated[k]).join(", ")}`);
  }
  console.log(`  artifact.md:  ${paths.md}`);
  console.log(`  artifact.json: ${paths.json}`);
  console.log("  next: Grep the .md for a mesh/material name; run verify after edits.");
}

async function cmdAvatar(name) {
  if (!name) fail("usage: scene-dossier.mjs avatar <goName>");
  const instanceId = await resolveAvatarInstanceId(name);
  const dossier = await pullSections({ instanceId }, AVATAR_SECTIONS);
  const slug = slugify(name);
  const paths = writeArtifacts(slug, dossier);
  printSummary(slug, paths, dossier);
  process.exit(EXIT_OK);
}

async function cmdScene() {
  const dossier = await pullSections({ mode: "scene" }, SCENE_SECTIONS);
  const slug = slugify(dossier.sections?.identity?.scene?.name ?? "scene");
  const paths = writeArtifacts(slug, dossier);
  printSummary(slug, paths, dossier);
  process.exit(EXIT_OK);
}

async function cmdVerify(slugArg) {
  let slug = slugArg;
  if (!slug) {
    // Prefer the most recently written dossier-*.json
    fail("usage: scene-dossier.mjs verify <slug>  (slug from dossier-<slug>.json)");
  }
  const paths = artifactPaths(slug);
  if (!existsSync(paths.json)) fail(`no dossier at ${paths.json} — run avatar/scene first`);
  let prior;
  try {
    prior = JSON.parse(readFileSync(paths.json, "utf8"));
  } catch (e) {
    fail(`corrupt dossier ${paths.json}: ${String(e)}`);
  }
  const priorHash = prior?.sections?.identity?.stateHash;
  const priorDirty = prior?.sections?.identity?.scene?.isDirty;
  const params =
    prior.mode === "scene" || prior.instanceId == null
      ? { mode: "scene" }
      : { instanceId: prior.instanceId };
  const fresh = await call("unity_perception", {
    ...params,
    action: "dossier",
    sections: ["identity"],
  });
  const id = fresh?.sections?.identity ?? {};
  const hash = id.stateHash;
  const dirty = id.scene?.isDirty;
  const hashMatch = priorHash && hash && priorHash === hash;
  const dirtyMatch = priorDirty === dirty;
  if (hashMatch && dirtyMatch) {
    console.log(`dossier fresh  slug=${slug}  stateHash=${hash}  dirty=${dirty}`);
    console.log(`  artifact.md: ${paths.md}`);
    process.exit(EXIT_OK);
  }
  console.log(`dossier STALE  slug=${slug}`);
  console.log(`  prior stateHash=${priorHash}  dirty=${priorDirty}`);
  console.log(`  now   stateHash=${hash}  dirty=${dirty}`);
  console.log(`  re-run: node .claude/tools/scene-dossier.mjs ${prior.mode === "scene" ? "scene" : `avatar ${prior.target}`}`);
  process.exit(EXIT_STALE);
}

async function main() {
  const [cmd, arg] = process.argv.slice(2);
  switch (cmd) {
    case "avatar":
      await cmdAvatar(arg);
      break;
    case "scene":
      await cmdScene();
      break;
    case "verify":
      await cmdVerify(arg);
      break;
    default:
      fail(
        "usage:\n" +
          "  node .claude/tools/scene-dossier.mjs avatar <goName>\n" +
          "  node .claude/tools/scene-dossier.mjs scene\n" +
          "  node .claude/tools/scene-dossier.mjs verify <slug>"
      );
  }
}

main();
