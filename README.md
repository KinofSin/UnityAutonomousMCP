# Autonomous Unity MCP — v2

A **VRChat-native, Unity 2022.3 Model Context Protocol (MCP) server** that meets and
exceeds Unity's own AI Assistant — without requiring Unity 6, a subscription, or Unity
Cloud auth. It lets an MCP client (Claude Code, Claude Desktop, Cursor, etc.) drive the
Unity Editor: inspect and edit scenes, audit and optimize avatars, run tests, manage
checkpoints, and more — behind a real Ask/Agent permission model.

> **Status:** unified v2 builds and runs on **Unity 2022.3.22f1**. Verified live: 62 tools
> registered, all tool families execute, Ask/Agent governance enforced, domain reload ~0.9 s.
> See `docs/superpowers/` for the design spec, implementation plan, and validation log.

## Why this exists

Unity's AI Assistant (`com.unity.ai.assistant`) requires **Unity 6**, is closed, and is
subscription/credit-gated. This project brings the same shape of capability —
modes, a tool registry, skills, client governance, a Settings window — down to
**Unity 2022.3.22f1**, adds **VRChat-specific** tooling (avatar/armature scans, a
200+ entry install/knowledge base, PhysBones/expression-params/FBT/OSC skills), and
keeps everything **open and local**.

## Features

- **62 tools** spanning editor control, scene/GameObject/component editing, scripts,
  assets, materials, animation, tests, plus 21 newer families: `unity_validation`,
  `unity_cleaner`, `unity_optimization`, `unity_profiler`, `unity_debug`,
  `unity_importer`, `unity_build_manage`, `unity_ui`, `unity_physics`, `unity_navmesh`,
  `unity_terrain`, `unity_cinemachine`, `unity_timeline`, `unity_lighting`,
  `unity_camera`, `unity_event`, `unity_smart`, `unity_perception`, `unity_workflow`,
  `manage_checkpoint`, `manage_generator`.
- **Ask / Agent governance** — Read tools always allowed; Mutate/Destructive tools gated
  by mode, per-client approval, and `autoApprove*` flags. Persisted to
  `Library/AutonomousMcp/permissions.json`.
- **Checkpoints** — snapshot/restore the active scene before destructive operations
  (`Library/MCP_Checkpoints/`).
- **Generators scaffold** — pluggable `IGenerator` providers (BYOK); ships stubs for 8
  asset kinds.
- **23-skill catalog** — VRChat (avatar, VRCFury, Modular Avatar, Poiyomi, Quest,
  PhysBones, expression params, OSC face tracking, FBT) + Unity-core + C# instruction skills.
- **Settings window** (`Window > Autonomous MCP > Settings`) — 8 tabs: Server, Tools, Logs,
  Integrations, Clients, Checkpoints, Generators, Skills.
- **Reflection-guarded** tools (Cinemachine, Timeline) degrade gracefully when a package
  isn't installed; NavMesh/UI/Profiler target the **2022.3** APIs (not Unity 6).

## Architecture

```
AI client (Claude Code / Desktop / Cursor / …)
        │  stdio | SSE | HTTP | TCP
        ▼
Node MCP relay  (server/)         ── mcpServer.ts · planner.ts · executor.ts · skills.ts
        │  HTTP/TCP
        ▼
Unity Editor package  (com.autonomous-unity.mcp/)
   ToolRegistry (registry-first dispatch, legacy switch fallback)
   PermissionStore · CheckpointStore · GeneratorRegistry · Settings window
```

## Install

**1. Unity package** — reference the package (not the repo root) in your Unity project's
`Packages/manifest.json`:

```json
"com.autonomous.unity.mcp": "file:/ABS/PATH/UnityAutonomousMCP/com.autonomous-unity.mcp"
```

> Point at the `com.autonomous-unity.mcp/` subfolder, **not** the repo root — otherwise
> Unity imports `node_modules/` + `server/` and domain reloads balloon to ~40 s.

**2. Node MCP server:**

```bash
cd UnityAutonomousMCP
npm install
npm --workspace server run build
```

**3. Configure your MCP client** (example snippet — also generated in the Settings →
Integrations tab):

```json
{ "mcpServers": { "autonomous-unity": {
    "command": "node",
    "args": ["/ABS/PATH/UnityAutonomousMCP/server/dist/index.js", "--mcp"]
} } }
```

**4. In Unity:** `Window > Autonomous MCP > Settings` → Server tab → Connect. Approve your
client in the Clients tab, choose Ask or Agent mode.

## Security

- **Ask mode** (default): only Read tools run; Mutate/Destructive are denied.
- **Agent mode**: Mutate runs only with `autoApproveMutate` (or per-client allow);
  Destructive needs `autoApproveDestructive`.
- New clients start **pending** and must be approved (toggle auto-approve in the Clients tab).
- Keep Ask mode on when not actively driving the editor on a project you care about.

## Development

```bash
npm --workspace server run build   # TypeScript build
node server/dist/smokeTest.js      # planner/executor smoke (fake bridge, no Unity needed)
```

Unity C# has no headless test harness; verify by opening the project in 2022.3.22f1
(console should be error-free) and exercising tools over the bridge.

## Compatibility

- **Unity 2022.3.22f1** (LTS). Tools target 2022.3 APIs deliberately — e.g. uGUI (not
  UI Toolkit UXML source-gen), legacy `UnityEditor.AI.NavMeshBuilder` (not
  `com.unity.ai.navigation`), Cinemachine 2.x type names via reflection.
- Newtonsoft.Json via `com.unity.nuget.newtonsoft-json`.

## Roadmap

This is an ongoing effort. Next up:

- **Real generator backends** — wire one `IGenerator` end-to-end (e.g. OpenAI texture, BYOK).
- **Full 2022.3 API-validation** of Mutate-tool *write* paths in a live editor.
- **Distribution & onboarding** — installer, first-run wizard, demos, version pinning.
- **Phase 9+** — named-pipe transport, multi-project coordination, deeper VRChat tooling,
  more skills.

## License

See repository. VRChat SDK, VRCFury, Modular Avatar, Poiyomi, etc. are property of their
respective owners.
