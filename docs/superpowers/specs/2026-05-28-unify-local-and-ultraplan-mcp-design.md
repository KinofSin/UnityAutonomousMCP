# Unify the local + Ultraplan MCP branches and validate tools against Unity 2022.3

> **Status:** design / spec. **Do not commit anything to git** until the user
> explicitly approves. This file is written to disk for review only.

## Context

North-star goal (unchanged): take the existing VRChat-focused autonomous MCP,
study how Unity's newer **AI Assistant** MCP is structured
(`docs.unity.com/en-us/ai`, `com.unity.ai.assistant@2.6`), and build **one**
unified system that brings those capabilities (Ask/Agent modes, a tool
registry, skills, client governance, a real Settings window) down to the user's
specific Unity **2022.3.22f1** — VRChat-native, open, no subscription — and
ultimately exceeds Unity's product (which requires Unity 6).

Work toward that goal ended up split across two places because of a round-trip
accident:

- **`local/phase0-7-wip`** (`e249c9f`) — the broad in-session implementation:
  registry, Ask/Agent modes, checkpoints, 19 `Unity*Tool` families, generators
  scaffold, planner v2, a 23-skill catalog, and a 7-tab Settings window. This
  was never committed before the round-trip, so the cloud never saw it.
- **`feat/phase01-core-and-settings-window`** (`b10134e`) — a tighter,
  cloud-reviewed Phase 0/1: the Core substrate, four governance tools, and a
  5-tab Settings window. Built from committed `main`, which lacked all the
  local work above. **Builds clean; server smoke passes.**
- **`main`** (`53e7163`) — untouched, equals `origin/main`.

Both branches are pieces of the *same* mission. This spec defines how to merge
them into **one buildable codebase** so the larger build can continue.

## Goal / success criteria

Two coupled parts on a single branch: **(1)** unify the branches into one
buildable codebase with tool + UI parity, and **(2)** validate/harden the
ported tools against the live Unity **2022.3.22f1** APIs (not Unity 6).

1. Unity package compiles on 2022.3.22f1 with the patch's Core/governance as
   the foundation **plus** all local Phase 2–7 tool families ported on top.
2. Every ported capability is callable via MCP (server advertises the schemas).
3. Every capability has a UI surface: the patch's window (Server / Tools / Logs
   / Integrations / Clients) gains **Checkpoints**, **Generators**, and
   **Skills** tabs.
4. Skills = merged + deduped union of both catalogs, conforming to the patch's
   exported `Skill` interface.
5. `npm --workspace server run build` and `… run smoke` both exit 0.
6. Every ported tool's version-sensitive Unity API calls are confirmed against
   the 2022.3.22f1 ScriptReference and pass a live-editor runtime smoke (Part B
   below). No tool silently depends on a Unity 6-only API.

## Architecture

**Working branch.** The merge is staged on a new branch off the patch
(`feat/unified-mcp`, branched from `feat/phase01-core-and-settings-window`) so
the reviewed patch branch stays pristine. Per the user's instruction, **nothing
is committed** until they approve — all merge work sits in the working tree
until then.

**Foundation = the patch branch.** Its `Editor/Core/*` (ToolRegistry,
PermissionStore, ToolMode, ToolCategory, PermissionDecision, IMcpTool,
AutonomousMcpMode), its `Editor/Tools/Governance/*` (manage_mcp_mode,
manage_mcp_clients, manage_mcp_permissions, list_tools_with_metadata), and its
`Editor/UI/AutonomousMcpSettingsWindow.cs` are canonical. The dispatcher and its
74 `Handle*` methods stay as-is (the patch keeps them).

**The one leveraged move — a registry compatibility overload.** The patch's
`ToolRegistry` only exposes `Register(RegistryEntry)` plus reflection discovery
of `[McpTool]` class/static forms. My 19 tool files self-register with the
delegate signature `Register(name, mode, category, description, handler)`. Add
that overload to the patch's `ToolRegistry` so it wraps args into a
`RegistryEntry`:

```csharp
public static void Register(string name, ToolMode mode, ToolCategory category,
    string description, Func<JObject, AutonomousMcpToolResponse> handler)
    => Register(new RegistryEntry(name, description, mode, category, handler));
```

With this single addition, **every local tool file compiles and self-registers
unchanged.** Local tools are plain `static` classes (no `[McpTool]` attribute),
so the patch's reflection discovery will not double-register them — only their
own `[InitializeOnLoadMethod] Register()` fires.

## Part A — components / steps (the merge)

1. **Add the compat overload** to `Editor/Core/ToolRegistry.cs` (patch branch).

2. **Extend `Editor/Core/ToolCategory.cs`** with the **13** additional members
   the local tools actually reference (verified by enumerating every
   `ToolCategory.X` usage across the 19 tool files): `UI, Physics, Navigation,
   Terrain, Timeline, Cinemachine, Lighting, Camera, Workflow, Checkpoint,
   Generator, Build, Profiler`. Purely additive — existing patch values keep
   their ordinal positions. (`Build` and `Profiler` were missed in an earlier
   draft of this list and are required, or `unity_build_manage` /
   `unity_profiler` fail to compile.)

3. **Port local tool files** (copy from `local/phase0-7-wip`, no logic change):
   - 19 families: `UnityValidationTool, UnityCleanerTool, UnityOptimizationTool,
     UnityProfilerTool, UnityDebugTool, UnityImporterTool, UnityBuildTool,
     UnityUITool, UnityPhysicsTool, UnityNavMeshTool, UnityTerrainTool,
     UnityCinemachineTool, UnityTimelineTool, UnityLightingTool, UnityCameraTool,
     UnityEventTool, UnitySmartTool, UnityPerceptionTool, UnityWorkflowTool`.
   - Checkpoints: `Editor/Core/CheckpointStore.cs`, `Editor/Tools/CheckpointTool.cs`.
   - Generators: `Editor/Core/{IGenerator,GeneratorConfig,GeneratorRegistry}.cs`,
     `Editor/Generators/StubGenerators.cs`, `Editor/Tools/ManageGeneratorTool.cs`.
   - Each needs a `.meta`; generate deterministic GUIDs matching the existing
     `.meta` format.

4. **Drop local Core duplicates** (do NOT carry these over — the patch's win):
   `ServerMode.cs`, `ClientRecord.cs`, my `PermissionStore.cs`,
   `McpAdminTools.cs`, my `IMcpTool.cs`, my `ToolMode.cs`, my Settings window.
   Any ported tool that referenced `ServerMode` switches to the patch's
   `AutonomousMcpMode`; anything that used my `McpAdminTools` is already covered
   by the patch's Governance tools.

5. **Skills merge.** Produce `server/src/skills.ts` as the deduped union: the
   patch's ~9 verified core skills + the local unique extras
   (`osc-faceTracking, fbt-stacks, vrchat-physbones, vrchat-expression-params,
   unity-navmesh, unity-terrain, unity-timeline, unity-animator,
   unity-testrunner, csharp-pro, unity-async, unity-collection-pool`, etc.).
   Conform every entry to the patch's exported `Skill` interface so
   `mcpServer.ts` and `planner.ts` (patch's annotated versions) compile. The
   Unity-side `Skills/index.json` (if surfaced in the UI) mirrors the same set.

6. **UI parity.** Add three tabs to the patch's
   `Editor/UI/AutonomousMcpSettingsWindow.cs`:
   - **Checkpoints** — list/restore/diff/delete via `CheckpointStore`,
     total disk usage, "Open folder".
   - **Generators** — default output dir, env-var detection, per-kind provider
     dropdown via `GeneratorConfig`/`GeneratorRegistry`.
   - **Skills** — read-only catalog browser (id/name/category/description/
     recommended tools), filter box.

7. **Server schemas.** Re-add the MCP `server.tool(...)` registrations the local
   branch had for the ported tools (the 19 families + `manage_checkpoint` +
   `manage_generator`) into the patch's `mcpServer.ts`, so clients can call
   them. Keep the patch's existing governance-tool registrations.

## Files

**Modified (patch branch):**
- `Editor/Core/ToolRegistry.cs` — add compat overload.
- `Editor/Core/ToolCategory.cs` — add 13 enum members (incl. Build, Profiler).
- `Editor/UI/AutonomousMcpSettingsWindow.cs` — add 3 tabs.
- `server/src/mcpServer.ts` — re-add ported tool schemas.
- `server/src/skills.ts` — replace with merged/deduped catalog.

**Created (ported from `local/phase0-7-wip`):**
- 19 `Editor/Tools/Unity*Tool.cs` (+ `.meta`).
- `Editor/Core/CheckpointStore.cs`, `Editor/Tools/CheckpointTool.cs`.
- `Editor/Core/IGenerator.cs`, `GeneratorConfig.cs`, `GeneratorRegistry.cs`,
  `Editor/Generators/StubGenerators.cs`, `Editor/Tools/ManageGeneratorTool.cs`.

**Explicitly NOT carried over:** local `ServerMode.cs`, `ClientRecord.cs`,
`PermissionStore.cs`, `McpAdminTools.cs`, local `IMcpTool.cs`, local
`ToolMode.cs`, local Settings window.

## Part B — 2022.3 API-validation pass (harden the ported tools)

The 19 tool families were written this session targeting 2022.3 but never
compiled against a live editor. Unity AI Assistant docs describe Unity 6 APIs;
several families touch version-sensitive surfaces. For **each** ported family:
(a) compile against 2022.3 reference assemblies, (b) confirm every
version-sensitive API exists in the 2022.3.22f1 ScriptReference, fixing
mismatches, (c) run a live-editor runtime smoke of its primary action.

**High-risk families (Unity 6 vs 2022.3 divergence) — validate first:**

- `unity_ui` — must use the 2022.3 `UxmlFactory`/`UxmlTraits` pattern, **not**
  Unity 6's source-generated `UxmlElement`/`UxmlAttribute`.
- `unity_navmesh` — must use the legacy in-editor `NavMeshBuilder`/`UnityEngine.AI`
  surface, **not** the Unity 6-gated `com.unity.ai.navigation` package.
- `unity_cinemachine` — must target Cinemachine **2.x** types
  (`CinemachineVirtualCamera`, classic component names), not 3.x
  (`CinemachineCamera`).
- `unity_importer` — texture/model/audio importer `SerializedProperty` path
  names; some platform-override fields differ in Unity 6. Confirm paths resolve
  on 2022.3.
- `unity_profiler` — restrict to `FrameTimingManager` + sampler categories that
  exist in 2022.3; drop any Unity 6-only categories.
- `unity_timeline`, `unity_terrain`, `unity_lighting`, `unity_camera` —
  spot-check API surfaces; lower risk but confirm.

**Lower-risk families** (`unity_validation`, `unity_cleaner`,
`unity_optimization`, `unity_debug`, `unity_build_manage`, `unity_physics`,
`unity_event`, `unity_smart`, `unity_perception`, `unity_workflow`) — compile +
single runtime smoke each; deeper review only if smoke fails.

Output: a short per-family checklist (✅ compiles / ✅ API confirmed / ✅ runtime
smoke) appended to this spec's progress notes during implementation. Any API
swap is a code fix in the relevant tool file, written to the 2022.3 idiom.

Because (c) needs a live editor, Part B is partly an interactive verification
step the user runs in Unity, with code fixes applied as mismatches surface.

## Unknowns to resolve during planning (not blockers)

1. **Dispatcher routing.** Confirm how the patch's `AutonomousMcpToolDispatcher`
   chooses registry vs switch. Ported tools live only in the registry, so the
   dispatch path must do a registry lookup (likely registry-first, switch
   fallback). If it is switch-only, add a registry lookup to the dispatch entry.
2. **Dropped-type references.** Grep each ported tool for `ServerMode`,
   `McpAdminTools`, local `PermissionStore` members; rewire the few hits to the
   patch equivalents.
3. **skills.ts interface drift.** Diff the two `Skill` interfaces; conform local
   entries (field names, required vs optional) to the patch's exported shape.

## Verification

1. `npm --workspace server run build` → exit 0.
2. `npm --workspace server run smoke` → both scenarios print `smoke: true`.
3. Unity opens the package on 2022.3.22f1 with **zero** console errors.
4. MCP round-trip on a sample of ported tools: `health_check`,
   `unity_validation {action:audit_active_scene}`,
   `manage_checkpoint {action:list}`, `manage_generator {action:list}`,
   `list_tools_with_metadata {}` (should now list governance + legacy + the
   ported families).
5. Settings window walkthrough: every tab renders; Checkpoints lists/restore;
   Generators shows kinds + env detection; Skills lists the merged catalog;
   mode badge still flips.
6. **Part B:** per-family checklist complete — each of the 19 families shows
   ✅ compiles / ✅ 2022.3 API confirmed / ✅ runtime smoke. High-risk families
   (`unity_ui`, `unity_navmesh`, `unity_cinemachine`, `unity_importer`,
   `unity_profiler`) explicitly exercised in a live 2022.3.22f1 editor with no
   missing-API or wrong-version errors in the console.

## Out of scope (roadmap, separate sprints)

- Real generator backend (stays stub / BYOK) — its own sprint.
- Distribution beyond the Integrations tab (installer, first-run wizard, README,
  demos) — its own sprint.
- Phase 9+ extensions: named-pipe transport, multi-project coordination, deeper
  VRChat-specific tools, additional skills.
- Dispatcher decomposition / asmdef split — only if a measured reload-time
  problem appears.

(The 2022.3 API-validation pass is now **in scope** as Part B above.)
