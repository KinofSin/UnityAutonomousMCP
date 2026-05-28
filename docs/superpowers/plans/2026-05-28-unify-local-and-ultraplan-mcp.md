# Unify Local + Ultraplan MCP Branches (with 2022.3 API validation) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **DO NOT COMMIT OR PUSH until the user explicitly approves.** Execution
> (including the commit steps below) begins only after the user sets up the
> private repo in Task 0 and says go. Until then, all work stays in the working
> tree.

**Goal:** Merge the broad local Phase 0–7 implementation onto the cloud-reviewed Ultraplan Phase 0/1 patch, producing one buildable, VRChat-native Unity-AI-Assistant-superset MCP for Unity 2022.3.22f1, then validate every ported tool against the live 2022.3 APIs.

**Architecture:** The patch branch (`feat/phase01-core-and-settings-window`) is the foundation — its `Editor/Core/*`, governance tools, dispatcher (registry-first, switch-fallback), and Settings window are canonical. A single `ToolRegistry.Register(...)` compatibility overload lets all 19 local tool families + checkpoints + generators self-register unchanged. Redundant local Core duplicates are dropped in favour of the patch's. Skills become the deduped union of both catalogs. Three tabs are added to the patch's Settings window. Finally each ported tool is hardened against the 2022.3 ScriptReference.

**Tech Stack:** Unity 2022.3.22f1 Editor C# (Newtonsoft.Json via `com.unity.nuget.newtonsoft-json`), Node 22 + TypeScript MCP server (`@modelcontextprotocol/sdk`, `zod`), git.

---

## Verified facts this plan relies on (evidence-gathered, not assumed)

- Patch dispatcher routes **registry-first, switch-fallback** (`Dispatch`/`DispatchOnMainThread` calls `ToolRegistry.TryResolve` before the legacy switch). Ported tools are reachable the moment they register — **no dispatch wiring needed.**
- Patch `ToolRegistry` exposes only `Register(RegistryEntry)` + reflection discovery of `[McpTool]` class/static forms. Local tools call `Register(name, mode, category, desc, handler)` → **needs a compat overload.**
- Patch `ToolCategory` has 10 members `{Editor, Scene, GameObject, Component, Asset, Script, Vrchat, Test, Custom, Diagnostic}`. Local tools additionally use **13** values → must add `UI, Physics, Navigation, Terrain, Timeline, Cinemachine, Lighting, Camera, Workflow, Checkpoint, Generator, Build, Profiler`.
- Patch `ToolMode` `{Read, Mutate, Destructive}` ⊇ the `{Read, Mutate}` the local tools use. Compatible.
- `AutonomousMcpToolResponse` is byte-identical across branches (`{ bool success; JToken data; string error; }`).
- None of the 19 tool files or ported Core files carry `[McpTool]` → no double-registration via the patch's reflection discovery.
- No ported file references a dropped type (`ServerMode`, `McpAdminTools`, `ClientRecord`, local `ToolDelegate`/`ToolEntry`, local `PermissionStore`) in code.
- Patch `server/src/mcpServer.ts` **already registers all 62 tool schemas** (identical to local) → **no server-schema work; only `skills.ts` changes.**
- `CheckpointStore` public API: `List()`, `Find(id)`, `Restore(id, out err)`, `Delete(id, out err)`, `DeleteAll()`, `TotalDiskUsageBytes()`, `SizeOf(id)`, `Diff(id)`, `Create(...)`, nested `Manifest{ id,label,createdUtc,activeScenePath,trackedAssetPaths,toolThatTriggered,clientId }`. **No `RootDirectory`** — Task 9 adds it.
- `GeneratorConfig`: `Data` (`{ providerByKind, defaultOutputDirectory }`), `GetProviderFor(kind)`, `SetProviderFor(kind, providerId)`. `GeneratorRegistry`: `List()`, `For(kind)`, `Resolve(kind, provider)`, `Count`. `GeneratorKind` enum + `IGenerator` (`ProviderId, Kind, DisplayName, IsConfigured(), GetStatus()`) in `IGenerator.cs`.

## Testing note (honest verification, not fabricated unit tests)

This project has **no headless C# unit-test harness** (Unity Editor tests require the editor). The truthful verifications are: (a) `npm --workspace server run build` + `run smoke` for the TS side (runnable here), (b) a `node -e` skills sanity check, (c) the Unity Editor opening the package with zero console errors, and (d) live MCP round-trips. Plan steps use these rather than inventing C# asserts that can't run.

## File structure

**Modified (patch branch):**
- `com.autonomous-unity.mcp/Editor/Core/ToolRegistry.cs` — add compat overload.
- `com.autonomous-unity.mcp/Editor/Core/ToolCategory.cs` — add 13 members.
- `com.autonomous-unity.mcp/Editor/Core/CheckpointStore.cs` — add `RootDirectory`.
- `com.autonomous-unity.mcp/Editor/UI/AutonomousMcpSettingsWindow.cs` — add 3 tabs.
- `server/src/skills.ts` — add 12 local-only skills.

**Created (ported verbatim from `local/phase0-7-wip` via `git show`):**
- 19 × `com.autonomous-unity.mcp/Editor/Tools/Unity*Tool.cs` (+ `.meta`).
- `com.autonomous-unity.mcp/Editor/Tools/CheckpointTool.cs`, `ManageGeneratorTool.cs` (+ `.meta`).
- `com.autonomous-unity.mcp/Editor/Core/IGenerator.cs`, `GeneratorConfig.cs`, `GeneratorRegistry.cs` (+ `.meta`); note `CheckpointStore.cs` is created here too.
- `com.autonomous-unity.mcp/Editor/Generators/StubGenerators.cs` (+ `Generators.meta`).

**NOT carried over:** local `ServerMode.cs`, `ClientRecord.cs`, `PermissionStore.cs`, `McpAdminTools.cs`, local `IMcpTool.cs`, local `ToolMode.cs`, local `BuiltinToolRegistration.cs`, local Settings window.

---

## Task 0: Private repo + working branch

**Files:** none (git/remote setup).

- [ ] **Step 1: Confirm starting point**

Run:
```bash
cd UnityAutonomousMCP && git branch --show-current && git status --porcelain | head
```
Expected: branch `feat/phase01-core-and-settings-window`; clean (no output) or only expected untracked.

- [ ] **Step 2: Create the working branch off the patch**

```bash
git checkout -b feat/unified-mcp
```
Expected: `Switched to a new branch 'feat/unified-mcp'`.

- [ ] **Step 3: Create the private GitHub repo (user-authenticated)**

```bash
gh repo create UnityAutonomousMCP-v2 --private --source=. --remote=private --description "Unified VRChat-native Unity AI Assistant superset MCP (v2)"
```
If `gh` is not authenticated, run `gh auth login` first. If the user prefers full history, instead `gh repo create UnityAutonomousMCP-v2 --private` then `git remote add private https://github.com/<user>/UnityAutonomousMCP-v2.git`.
Expected: repo created; `git remote -v` lists a `private` remote.

- [ ] **Step 4: Verify remotes**

Run: `git remote -v`
Expected: `origin` (public) **and** `private` (new) both listed. We push only to `private` for v2.

> No push yet — push happens after the user approves a milestone.

---

## Task 1: ToolRegistry compatibility overload

**Files:**
- Modify: `com.autonomous-unity.mcp/Editor/Core/ToolRegistry.cs`

- [ ] **Step 1: Read the patch RegistryEntry constructor to confirm arg order**

Run:
```bash
grep -nE 'public RegistryEntry\(|public static void Register' com.autonomous-unity.mcp/Editor/Core/ToolRegistry.cs
```
Expected: `RegistryEntry(string name, string description, ToolMode mode, ToolCategory category, Func<JObject,AutonomousMcpToolResponse> handler)` and `Register(RegistryEntry entry)`.

- [ ] **Step 2: Add the overload**

Inside `public static class ToolRegistry`, next to the existing `Register(RegistryEntry)`, add:

```csharp
/// <summary>
/// Convenience overload for delegate-form tools (the ported Unity*Tool families
/// register this way). Wraps the args into a RegistryEntry. Last write wins.
/// </summary>
public static void Register(string name, ToolMode mode, ToolCategory category,
    string description, System.Func<Newtonsoft.Json.Linq.JObject, AutonomousMcpToolResponse> handler)
    => Register(new RegistryEntry(name, description, mode, category, handler));
```

- [ ] **Step 3: Sanity-compile the TS-independent piece is N/A; defer compile to Task 6**

No build here (Unity-only file). Proceed.

- [ ] **Step 4: Commit**

```bash
git add com.autonomous-unity.mcp/Editor/Core/ToolRegistry.cs
git commit -m "feat(core): add delegate-form ToolRegistry.Register overload for ported tools"
```

---

## Task 2: Extend ToolCategory

**Files:**
- Modify: `com.autonomous-unity.mcp/Editor/Core/ToolCategory.cs`

- [ ] **Step 1: Replace the enum body**

Set the enum to (additive — patch's 10 keep their order, 13 appended):

```csharp
namespace AutonomousMcp.Editor.Core
{
    public enum ToolCategory
    {
        Editor,
        Scene,
        GameObject,
        Component,
        Asset,
        Script,
        Vrchat,
        Test,
        Custom,
        Diagnostic,
        // Appended for ported Phase 2–7 tool families:
        UI,
        Physics,
        Navigation,
        Terrain,
        Timeline,
        Cinemachine,
        Lighting,
        Camera,
        Workflow,
        Checkpoint,
        Generator,
        Build,
        Profiler
    }
}
```

- [ ] **Step 2: Verify all used categories are now present**

Run:
```bash
git show local/phase0-7-wip:com.autonomous-unity.mcp/Editor/Tools/UnityProfilerTool.cs | grep -o 'ToolCategory\.[A-Za-z]*' | sort -u
grep -o 'ToolCategory\.[A-Za-z]*' com.autonomous-unity.mcp/Editor/Core/ToolCategory.cs # not applicable; instead eyeball the enum
```
Expected: every `ToolCategory.X` the tools use (incl. `Profiler`, `Build`) appears in the enum above.

- [ ] **Step 3: Commit**

```bash
git add com.autonomous-unity.mcp/Editor/Core/ToolCategory.cs
git commit -m "feat(core): extend ToolCategory with 13 members for ported tool families"
```

---

## Task 3: Port the 19 Unity*Tool families

**Files:**
- Create: `com.autonomous-unity.mcp/Editor/Tools/Unity{Validation,Cleaner,Optimization,Profiler,Debug,Importer,Build,UI,Physics,NavMesh,Terrain,Cinemachine,Timeline,Lighting,Camera,Event,Smart,Perception,Workflow}Tool.cs` (+ `.cs.meta`)

- [ ] **Step 1: Port each `.cs` and its `.meta` verbatim from the local branch**

The code already exists and compiled-clean against the same Core types in the local branch; copy it rather than re-author (DRY). Run:

```bash
cd UnityAutonomousMCP
for t in UnityValidationTool UnityCleanerTool UnityOptimizationTool UnityProfilerTool \
         UnityDebugTool UnityImporterTool UnityBuildTool UnityUITool UnityPhysicsTool \
         UnityNavMeshTool UnityTerrainTool UnityCinemachineTool UnityTimelineTool \
         UnityLightingTool UnityCameraTool UnityEventTool UnitySmartTool \
         UnityPerceptionTool UnityWorkflowTool; do
  p="com.autonomous-unity.mcp/Editor/Tools/$t.cs"
  git show "local/phase0-7-wip:$p"      > "$p"
  git show "local/phase0-7-wip:$p.meta" > "$p.meta" 2>/dev/null || echo "  ($t.meta absent — Unity will regenerate)"
done
ls com.autonomous-unity.mcp/Editor/Tools/Unity*Tool.cs | wc -l
```
Expected: `19`.

- [ ] **Step 2: Confirm each calls the delegate-form Register and has no `[McpTool]`**

```bash
grep -L 'ToolRegistry.Register(' com.autonomous-unity.mcp/Editor/Tools/Unity*Tool.cs   # expect: no output
grep -l '\[McpTool' com.autonomous-unity.mcp/Editor/Tools/Unity*Tool.cs                 # expect: no output
```
Expected: both produce no output (all register via the overload; none would double-register).

- [ ] **Step 3: Commit**

```bash
git add com.autonomous-unity.mcp/Editor/Tools/Unity*Tool.cs com.autonomous-unity.mcp/Editor/Tools/Unity*Tool.cs.meta
git commit -m "feat(tools): port 19 Unity tool families onto the patch registry"
```

---

## Task 4: Port checkpoints

**Files:**
- Create: `com.autonomous-unity.mcp/Editor/Core/CheckpointStore.cs` (+ `.meta`)
- Create: `com.autonomous-unity.mcp/Editor/Tools/CheckpointTool.cs` (+ `.meta`)

- [ ] **Step 1: Port both files verbatim**

```bash
for p in com.autonomous-unity.mcp/Editor/Core/CheckpointStore.cs \
         com.autonomous-unity.mcp/Editor/Tools/CheckpointTool.cs; do
  git show "local/phase0-7-wip:$p"      > "$p"
  git show "local/phase0-7-wip:$p.meta" > "$p.meta" 2>/dev/null || echo "  ($p.meta absent — Unity regenerates)"
done
```

- [ ] **Step 2: Confirm CheckpointTool registers `manage_checkpoint` via the overload**

```bash
grep -n 'ToolRegistry.Register("manage_checkpoint"' com.autonomous-unity.mcp/Editor/Tools/CheckpointTool.cs
```
Expected: one match.

- [ ] **Step 3: Commit**

```bash
git add com.autonomous-unity.mcp/Editor/Core/CheckpointStore.cs* com.autonomous-unity.mcp/Editor/Tools/CheckpointTool.cs*
git commit -m "feat(checkpoints): port CheckpointStore + manage_checkpoint tool"
```

---

## Task 5: Port generators

**Files:**
- Create: `com.autonomous-unity.mcp/Editor/Core/IGenerator.cs`, `GeneratorConfig.cs`, `GeneratorRegistry.cs` (+ `.meta`)
- Create: `com.autonomous-unity.mcp/Editor/Generators/StubGenerators.cs` (+ `.meta`)
- Create: `com.autonomous-unity.mcp/Editor/Tools/ManageGeneratorTool.cs` (+ `.meta`)
- Create: `com.autonomous-unity.mcp/Editor/Generators.meta` (folder)

- [ ] **Step 1: Port files + folder meta verbatim**

```bash
for p in com.autonomous-unity.mcp/Editor/Core/IGenerator.cs \
         com.autonomous-unity.mcp/Editor/Core/GeneratorConfig.cs \
         com.autonomous-unity.mcp/Editor/Core/GeneratorRegistry.cs \
         com.autonomous-unity.mcp/Editor/Generators/StubGenerators.cs \
         com.autonomous-unity.mcp/Editor/Tools/ManageGeneratorTool.cs; do
  mkdir -p "$(dirname "$p")"
  git show "local/phase0-7-wip:$p"      > "$p"
  git show "local/phase0-7-wip:$p.meta" > "$p.meta" 2>/dev/null || echo "  ($p.meta absent — Unity regenerates)"
done
git show "local/phase0-7-wip:com.autonomous-unity.mcp/Editor/Generators.meta" \
   > com.autonomous-unity.mcp/Editor/Generators.meta 2>/dev/null || echo "  (folder meta absent — Unity regenerates)"
```

- [ ] **Step 2: Confirm ManageGeneratorTool registers `manage_generator`**

```bash
grep -n 'ToolRegistry.Register("manage_generator"' com.autonomous-unity.mcp/Editor/Tools/ManageGeneratorTool.cs
```
Expected: one match.

- [ ] **Step 3: Commit**

```bash
git add com.autonomous-unity.mcp/Editor/Core/IGenerator.cs* com.autonomous-unity.mcp/Editor/Core/Generator*.cs* \
        com.autonomous-unity.mcp/Editor/Generators* com.autonomous-unity.mcp/Editor/Tools/ManageGeneratorTool.cs*
git commit -m "feat(generators): port IGenerator scaffold + manage_generator tool"
```

---

## Task 6: Unity compile checkpoint (live editor)

**Files:** none (verification).

- [ ] **Step 1: Open the project in Unity 2022.3.22f1**

Open the Unity project containing this package. Let it reimport + recompile.

- [ ] **Step 2: Read the Console**

Expected: **zero compile errors.** If errors appear, they will name a missing type/member — fix at the source (most likely a missed `ToolCategory` value or a stray reference to a dropped type) before continuing. Do **not** proceed to UI tasks until the console is clean.

- [ ] **Step 3: Confirm tool count via the registry**

In Unity, `Window > Autonomous MCP > Settings` → Tools tab. Expected: the governance + legacy tools **plus** the 21 newly ported tools (19 families + `manage_checkpoint` + `manage_generator`) appear.

- [ ] **Step 4: Commit (only a progress note; no code changed)**

No commit needed unless fixes were applied. If fixes were applied:
```bash
git add -A && git commit -m "fix(core): resolve 2022.3 compile errors surfaced on first import"
```

---

## Task 7: Merge skills catalog (server)

**Files:**
- Modify: `server/src/skills.ts`

Patch catalog ids (11): `cinemachine, mobile, modular-avatar, performance, physics, poiyomi, ui-toolkit, vrcfury, vrchat-avatar, vrchat-quest, vrchat-upload-recipe`. Add the **12 local-only** skills (concept not already covered): `csharp-pro, fbt-stacks, osc-faceTracking, unity-animator, unity-async, unity-collection-pool, unity-navmesh, unity-terrain, unity-testrunner, unity-timeline, vrchat-expression-params, vrchat-physbones`.

- [ ] **Step 1: Read the patch's Skill interface + SKILLS array shape**

```bash
sed -n '1,30p' server/src/skills.ts
```
Expected: `interface Skill { id; name; category; description; systemPrompt; recommendedTools: string[]; requiredPackages: string[]; examples?: string[] }` and a `const SKILLS: Skill[] = [ ... ]`.

- [ ] **Step 2: Extract the 12 local-only skills from the local catalog**

The content already exists in the local Unity-side catalog. View it:
```bash
git show local/phase0-7-wip:com.autonomous-unity.mcp/Skills/index.json | \
  python -c "import json,sys; d=json.load(sys.stdin); print('\n'.join(s['id'] for s in d['skills']))"
```
Expected: the 23 local ids. For each of the 12 to add, read its object:
```bash
git show local/phase0-7-wip:com.autonomous-unity.mcp/Skills/index.json | \
  python -c "import json,sys; d=json.load(sys.stdin); print(json.dumps([s for s in d['skills'] if s['id']=='csharp-pro'][0], indent=2))"
```

- [ ] **Step 3: Append the 12 as TS `Skill` literals**

Map JSON → TS `Skill`: `id, name, category, description, systemPrompt, recommendedTools, requiredPackages` carry over 1:1; `examples` is optional (include if present). Worked example for the first one (use the real fields from Step 2 output; this shows the exact shape):

```ts
  {
    id: "csharp-pro",
    name: "C# Pro Patterns",
    category: "unity-core",
    description: "Modern C# idioms for Unity 2022.3 (records, span, async/await, nullable).",
    systemPrompt: "<copy systemPrompt from local index.json for csharp-pro>",
    recommendedTools: ["manage_script", "read_script", "execute_csharp"],
    requiredPackages: [],
  },
```
Append all 12 inside the `SKILLS` array. Keep the patch's 11 untouched (their ids/slugs win for overlapping concepts).

- [ ] **Step 4: Build the server**

Run: `npm --workspace server run build`
Expected: exit 0, no TS errors.

- [ ] **Step 5: Skills sanity check**

Run:
```bash
node --input-type=module -e "import('./server/dist/skills.js').then(m=>{const a=m.listSkills();console.log('count',a.length);const r=m.invokeSkill('vrchat-upload-recipe');console.log('invoke ok', r.ok===true && typeof r.inject==='string' && r.inject.length>0);})"
```
Expected: `count 23` and `invoke ok true`.

- [ ] **Step 6: Commit**

```bash
git add server/src/skills.ts
git commit -m "feat(skills): merge local + patch catalogs into deduped 23-skill set"
```

---

## Task 8: Server build + smoke gate

**Files:** none (verification).

- [ ] **Step 1: Build**

Run: `npm --workspace server run build`
Expected: exit 0.

- [ ] **Step 2: Smoke**

Run: `node UnityAutonomousMCP/server/dist/smokeTest.js`
Expected: prints `"smoke": true` for both success and failure scenarios.

- [ ] **Step 3: Commit (only if a fix was needed)**

```bash
git add -A && git commit -m "test(server): confirm build + smoke green after skills merge"
```

---

## Task 9: Settings window — Checkpoints tab

**Files:**
- Modify: `com.autonomous-unity.mcp/Editor/Core/CheckpointStore.cs`
- Modify: `com.autonomous-unity.mcp/Editor/UI/AutonomousMcpSettingsWindow.cs`

- [ ] **Step 1: Expose the checkpoint root directory (no public accessor exists)**

In `CheckpointStore.cs`, immediately after the private `Root` property, add:
```csharp
/// <summary>Public read access to the checkpoint root for the Settings UI.</summary>
public static string RootDirectory => Root;
```

- [ ] **Step 2: Learn the patch window's tab pattern**

```bash
grep -nE 'enum Tab|case Tab\.|GUILayout.Toolbar|private void Draw' \
  com.autonomous-unity.mcp/Editor/UI/AutonomousMcpSettingsWindow.cs | head -40
```
Note the `Tab` enum members, the toolbar strings array, and the `OnGUI` switch.

- [ ] **Step 3: Add `Checkpoints` to the Tab enum and toolbar**

Add `Checkpoints` to the `Tab` enum, its label to the toolbar string array (same order), and `case Tab.Checkpoints: DrawCheckpoints(); break;` to the `OnGUI` switch — matching the existing pattern exactly.

- [ ] **Step 4: Add the `DrawCheckpoints` method (uses only verified APIs)**

```csharp
private void DrawCheckpoints()
{
    EditorGUILayout.LabelField("Checkpoints", EditorStyles.boldLabel);
    var list = AutonomousMcp.Editor.Core.CheckpointStore.List();
    var totalKb = AutonomousMcp.Editor.Core.CheckpointStore.TotalDiskUsageBytes() / 1024.0;
    EditorGUILayout.LabelField($"{list.Count} stored · {totalKb:0.#} KB total");

    using (new EditorGUILayout.HorizontalScope())
    {
        if (GUILayout.Button("Refresh", GUILayout.Width(90))) Repaint();
        if (GUILayout.Button("Open folder", GUILayout.Width(110)))
            EditorUtility.RevealInFinder(AutonomousMcp.Editor.Core.CheckpointStore.RootDirectory);
        if (GUILayout.Button("Delete all", GUILayout.Width(90)) &&
            EditorUtility.DisplayDialog("Delete all checkpoints?", "Remove every saved checkpoint?", "Delete", "Cancel"))
            AutonomousMcp.Editor.Core.CheckpointStore.DeleteAll();
    }
    EditorGUILayout.Space();

    foreach (var cp in list)
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField($"{cp.id}  ·  {cp.label}", EditorStyles.boldLabel);
            var kb = AutonomousMcp.Editor.Core.CheckpointStore.SizeOf(cp.id) / 1024.0;
            var scene = string.IsNullOrEmpty(cp.activeScenePath) ? "(none)" : cp.activeScenePath;
            EditorGUILayout.LabelField($"{cp.createdUtc} · {kb:0.#} KB · scene={scene}", EditorStyles.miniLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Restore") &&
                    EditorUtility.DisplayDialog($"Restore {cp.id}?", "Replace current scene with checkpoint?", "Restore", "Cancel"))
                {
                    if (!AutonomousMcp.Editor.Core.CheckpointStore.Restore(cp.id, out var err))
                        Debug.LogError($"[AutonomousMCP] Restore failed: {err}");
                }
                if (GUILayout.Button("Diff")) Debug.Log(AutonomousMcp.Editor.Core.CheckpointStore.Diff(cp.id));
                if (GUILayout.Button("Delete"))
                {
                    if (!AutonomousMcp.Editor.Core.CheckpointStore.Delete(cp.id, out var err))
                        Debug.LogError($"[AutonomousMCP] Delete failed: {err}");
                }
            }
        }
    }
}
```

- [ ] **Step 5: Verify in the live editor**

Reopen the Settings window → Checkpoints tab renders; if any checkpoints exist they list with Restore/Diff/Delete. Console clean.

- [ ] **Step 6: Commit**

```bash
git add com.autonomous-unity.mcp/Editor/Core/CheckpointStore.cs com.autonomous-unity.mcp/Editor/UI/AutonomousMcpSettingsWindow.cs
git commit -m "feat(ui): add Checkpoints tab + CheckpointStore.RootDirectory accessor"
```

---

## Task 10: Settings window — Generators tab

**Files:**
- Modify: `com.autonomous-unity.mcp/Editor/UI/AutonomousMcpSettingsWindow.cs`

- [ ] **Step 1: Add `Generators` to the Tab enum, toolbar, and switch** (same pattern as Task 9 Step 3).

- [ ] **Step 2: Add the `DrawGenerators` method (verified APIs only)**

```csharp
private void DrawGenerators()
{
    EditorGUILayout.LabelField("Generators (scaffold)", EditorStyles.boldLabel);
    EditorGUILayout.HelpBox(
        "Stub providers ship for every kind. API keys are read from GENERATOR_* env vars at request time, never stored.",
        MessageType.Info);

    var data = AutonomousMcp.Editor.Core.GeneratorConfig.Data;
    var newOut = EditorGUILayout.TextField("Default output dir", data.defaultOutputDirectory);
    if (newOut != data.defaultOutputDirectory && newOut.StartsWith("Assets/", System.StringComparison.Ordinal))
    {
        data.defaultOutputDirectory = newOut.TrimEnd('/');
        AutonomousMcp.Editor.Core.GeneratorConfig.Save();
    }

    EditorGUILayout.Space();
    EditorGUILayout.LabelField("Environment detection", EditorStyles.boldLabel);
    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
    {
        foreach (var n in new[] { "GENERATOR_API_KEY", "GENERATOR_OPENAI_API_KEY", "GENERATOR_ANTHROPIC_API_KEY", "GENERATOR_LOCAL_LLM_URL" })
        {
            var present = !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable(n));
            EditorGUILayout.LabelField($"{n}: {(present ? "set" : "(missing)")}", EditorStyles.miniLabel);
        }
    }

    EditorGUILayout.Space();
    EditorGUILayout.LabelField($"Registered generators: {AutonomousMcp.Editor.Core.GeneratorRegistry.Count}", EditorStyles.boldLabel);
    foreach (var g in AutonomousMcp.Editor.Core.GeneratorRegistry.List())
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField($"{g.Kind} · {g.ProviderId}", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"  {(g.IsConfigured() ? "ready" : "not configured")} — {g.GetStatus()}", EditorStyles.wordWrappedMiniLabel);
        }
    }
}
```

- [ ] **Step 3: Verify in live editor** — Generators tab renders kinds + env detection; console clean.

- [ ] **Step 4: Commit**

```bash
git add com.autonomous-unity.mcp/Editor/UI/AutonomousMcpSettingsWindow.cs
git commit -m "feat(ui): add Generators tab"
```

---

## Task 11: Settings window — Skills tab

**Files:**
- Modify: `com.autonomous-unity.mcp/Editor/UI/AutonomousMcpSettingsWindow.cs`

The Unity side reads the catalog from `Packages/com.autonomous-unity.mcp/Skills/index.json` (or sibling dev path). Port the catalog file first so the tab has data.

- [ ] **Step 1: Port the Unity-side merged catalog**

Update `com.autonomous-unity.mcp/Skills/index.json` to mirror the merged 23-skill set from Task 7 (same ids). Port the local file as the base, then ensure its ids match the Task 7 union:
```bash
git show local/phase0-7-wip:com.autonomous-unity.mcp/Skills/index.json > com.autonomous-unity.mcp/Skills/index.json
git show "local/phase0-7-wip:com.autonomous-unity.mcp/Skills/index.json.meta" > com.autonomous-unity.mcp/Skills/index.json.meta 2>/dev/null || true
git show "local/phase0-7-wip:com.autonomous-unity.mcp/Skills.meta" > com.autonomous-unity.mcp/Skills.meta 2>/dev/null || true
```

- [ ] **Step 2: Add `Skills` to the Tab enum, toolbar, and switch** (same pattern).

- [ ] **Step 3: Add the `DrawSkills` method (read-only catalog browser)**

```csharp
private string _skillFilter = string.Empty;
private void DrawSkills()
{
    EditorGUILayout.LabelField("Skills (Skills/index.json)", EditorStyles.boldLabel);
    _skillFilter = EditorGUILayout.TextField("Filter", _skillFilter);
    var path = System.IO.Path.GetFullPath(System.IO.Path.Combine("Packages", "com.autonomous-unity.mcp", "Skills", "index.json"));
    if (!System.IO.File.Exists(path))
    {
        var sibling = System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath)?.Parent?.FullName ?? "", "com.autonomous-unity.mcp", "Skills", "index.json");
        if (System.IO.File.Exists(sibling)) path = sibling;
    }
    if (!System.IO.File.Exists(path)) { EditorGUILayout.HelpBox("Skills/index.json not found.", MessageType.Warning); return; }

    Newtonsoft.Json.Linq.JArray skills;
    try { skills = (Newtonsoft.Json.Linq.JArray)Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(path))["skills"]; }
    catch (System.Exception e) { EditorGUILayout.HelpBox($"Parse error: {e.Message}", MessageType.Error); return; }

    EditorGUILayout.LabelField($"{skills.Count} skills");
    foreach (var s in skills)
    {
        var id = (string)s["id"]; var name = (string)s["name"];
        if (!string.IsNullOrEmpty(_skillFilter) &&
            (id ?? "").IndexOf(_skillFilter, System.StringComparison.OrdinalIgnoreCase) < 0 &&
            (name ?? "").IndexOf(_skillFilter, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField($"{id} — {name}", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"category: {(string)s["category"]}", EditorStyles.miniLabel);
            EditorGUILayout.LabelField((string)s["description"], EditorStyles.wordWrappedLabel);
        }
    }
}
```

- [ ] **Step 4: Verify in live editor** — Skills tab lists 23 skills; filter works; console clean.

- [ ] **Step 5: Commit**

```bash
git add com.autonomous-unity.mcp/Editor/UI/AutonomousMcpSettingsWindow.cs com.autonomous-unity.mcp/Skills/index.json*
git commit -m "feat(ui): add Skills tab + port merged Unity-side catalog"
```

---

## Task 12: Part B — validate HIGH-RISK families against 2022.3

**Files (fix-as-needed):** `Editor/Tools/{UnityUITool,UnityNavMeshTool,UnityCinemachineTool,UnityImporterTool,UnityProfilerTool}.cs`

These touch APIs that diverge between Unity 6 and 2022.3. For each: confirm it compiled in Task 6 (already done), then confirm the specific API and run one runtime smoke.

- [ ] **Step 1: `unity_ui` — UxmlFactory/UxmlTraits, not UxmlElement**

```bash
grep -nE 'UxmlElement|UxmlAttribute|UxmlFactory|UxmlTraits' com.autonomous-unity.mcp/Editor/Tools/UnityUITool.cs
```
Expected: **no** `UxmlElement`/`UxmlAttribute` (Unity 6 source-gen). If present, rewrite to the 2022.3 `UxmlFactory<T>`/`UxmlTraits` pattern. Runtime smoke: call `unity_ui` create-canvas action; verify the canvas appears.

- [ ] **Step 2: `unity_navmesh` — legacy UnityEngine.AI, not com.unity.ai.navigation**

```bash
grep -nE 'Unity\.AI\.Navigation|NavMeshSurface|using UnityEngine.AI|NavMeshBuilder' com.autonomous-unity.mcp/Editor/Tools/UnityNavMeshTool.cs
```
Expected: uses `UnityEngine.AI`/`NavMeshBuilder`; **no** `Unity.AI.Navigation`/`NavMeshSurface` (that package is Unity 6-gated and not installed). Fix if present. Runtime smoke: call the bake action; verify no missing-type error.

- [ ] **Step 3: `unity_cinemachine` — 2.x types**

```bash
grep -nE 'CinemachineCamera|CinemachineVirtualCamera' com.autonomous-unity.mcp/Editor/Tools/UnityCinemachineTool.cs
```
Expected: uses `CinemachineVirtualCamera` (2.x); **no** `CinemachineCamera` (3.x). Note: if Cinemachine isn't installed, the tool should guard with reflection/`#if` rather than hard reference — confirm it degrades gracefully. Runtime smoke (if Cinemachine present): create a vcam.

- [ ] **Step 4: `unity_importer` — SerializedProperty paths resolve on 2022.3**

```bash
grep -nE 'FindProperty\("[^"]+"\)' com.autonomous-unity.mcp/Editor/Tools/UnityImporterTool.cs
```
Runtime smoke: run the texture-importer read action on a real texture; confirm each `FindProperty(...)` returns non-null (no Unity 6-only field paths). Fix any null path to the 2022.3 equivalent.

- [ ] **Step 5: `unity_profiler` — 2022.3 sampler/FrameTiming surface**

```bash
grep -nE 'FrameTimingManager|Sampler\.|ProfilerRecorder|ProfilerCategory' com.autonomous-unity.mcp/Editor/Tools/UnityProfilerTool.cs
```
Runtime smoke: call the profiler capture action; confirm no missing-API error and a value returns. Drop any Unity 6-only category.

- [ ] **Step 6: Record results + commit any fixes**

Append a checklist block to the spec's progress notes (one line per family: ✅ compiles / ✅ API confirmed / ✅ runtime smoke). Then:
```bash
git add -A && git commit -m "fix(tools): harden high-risk families for Unity 2022.3 APIs"
```
(If no fixes were needed, commit only the checklist note.)

---

## Task 13: Part B — validate remaining families + checklist

**Files (fix-as-needed):** the other 14 tool files.

- [ ] **Step 1: Runtime smoke each lower-risk family**

For `unity_validation, unity_cleaner, unity_optimization, unity_debug, unity_build_manage, unity_physics, unity_event, unity_smart, unity_perception, unity_workflow` (and `unity_terrain, unity_lighting, unity_timeline, unity_camera`): invoke its primary action once via MCP and confirm a successful response with no console error. Example:
```
health_check
unity_validation {action:"audit_active_scene"}
unity_optimization {action:"mesh_stats"}   # use each tool's real default action
```

- [ ] **Step 2: Complete the per-family checklist**

Append the full 19-family checklist (✅ compiles / ✅ API confirmed / ✅ runtime smoke) to the spec progress notes.

- [ ] **Step 3: Commit fixes (if any)**

```bash
git add -A && git commit -m "fix(tools): 2022.3 validation pass for remaining tool families"
```

---

## Task 14: Final end-to-end verification

**Files:** none.

- [ ] **Step 1: Server build + smoke** — `npm --workspace server run build` (exit 0) and `node UnityAutonomousMCP/server/dist/smokeTest.js` (`smoke: true` ×2).

- [ ] **Step 2: Unity console clean** — reimport; zero errors/warnings from this package.

- [ ] **Step 3: Live MCP round-trip** — start the server (`node UnityAutonomousMCP/server/dist/index.js --mcp`), connect a client, and call: `health_check`, `unity_validation {action:"audit_active_scene"}`, `manage_checkpoint {action:"list"}`, `manage_generator {action:"list"}`, `list_tools_with_metadata {}`. Expected: all succeed; `list_tools_with_metadata` returns governance + legacy + the 21 ported tools.

- [ ] **Step 4: Settings window walkthrough** — every tab (Server/Tools/Logs/Integrations/Clients/Checkpoints/Generators/Skills) renders; mode badge flips; Skills shows 23.

- [ ] **Step 5: Push to the private remote (only after user approval of the milestone)**

```bash
git push -u private feat/unified-mcp
```
Expected: branch on the private repo. (Do not push to `origin`.)

---

## Self-review notes

- **Spec coverage:** Part A steps 1–6 → Tasks 1,2,3,4,5,7,9,10,11 (step 7 "re-add server schemas" is a verified no-op, documented above). Part B → Tasks 12–13. UI parity (3 tabs) → Tasks 9–11. Private repo → Task 0. Verification → Tasks 6, 8, 14.
- **Placeholder scan:** bulk C# ports use exact `git show` extraction (code already in git); the one authored example (skills `csharp-pro`) names the real source for the systemPrompt; UI method bodies are complete and use only verified APIs.
- **Type consistency:** `Register(name,mode,category,desc,handler)` (Task 1) matches the call sites in the ported tools; `ToolCategory` additions (Task 2) cover every value the tools use incl. `Build`/`Profiler`; `CheckpointStore.RootDirectory` is added in Task 9 before the UI references it; `GeneratorConfig.Data.defaultOutputDirectory` / `GeneratorRegistry.List()/Count` match the verified APIs.

---

## Part B — static validation log (agent-completed; runtime pending live editor)

Compile-risk review: 5/6 categories clean; fixed `using Unity.Profiling` (unused) in UnityProfilerTool.

High-risk family static API checks (Unity 6 vs 2022.3):
- `unity_ui` — ✅ uses classic uGUI (Canvas/CanvasScaler/GraphicRaycaster), no UXML; version-agnostic.
- `unity_cinemachine` — ✅ reflection on `Cinemachine.CinemachineVirtualCamera` (2.x); compiles without the package.
- `unity_navmesh` — ✅ legacy `UnityEditor.AI.NavMeshBuilder` + `UnityEngine.AI` (built-in to 2022.3), not `Unity.AI.Navigation`.
- `unity_timeline` — ✅ reflection (`GetProperty("playableAsset")`), no hard Timeline using.
- `unity_profiler` — ✅ `UnityEngine.Profiling.Sampler` (built-in) after removing the unused `Unity.Profiling` using.
- `unity_importer` — ⏳ 1 FindProperty call; confirm path resolves at runtime in the editor.

**Remaining (require live Unity 2022.3.22f1 — user-run):** Task 6 compile gate, Task 12/13 runtime smokes per family, Task 14 MCP round-trip + Settings-window walkthrough, then `git push -u private feat/unified-mcp` after the private remote is set up.

---

## Live runtime validation results (over HTTP bridge, Unity 2022.3.22f1, project: Leaf)

Import fix verified: domain reload 41.6s -> 0.9s; GUID conflicts 23,362 -> 0; 0 compile errors.
First-compile fixes applied (5): dispatcher crunchedCompression x2, dispatcher !IndexOf precedence,
UnityNavMeshTool NavMeshBuilder qualify, UnityTimelineTool Object qualify.

Tool execution (live):
- Read tools: unity_validation, unity_optimization (mesh_audit found LEAF/Body >5k tris),
  unity_profiler (frame_timing fps real), unity_debug (count_objects 572), unity_perception,
  unity_smart (input validation) — all execute.
- Mutate tools (Agent mode + autoApproveMutate, read-only actions): unity_navmesh info,
  unity_timeline list_directors, manage_checkpoint list, manage_generator list (8 stubs) — all OK;
  unity_cinemachine gracefully reports package not installed (reflection guard works).
Governance fully validated: Ask-mode mode-gate, unapproved-client gate, Agent-mode mutate-approval gate.
list_tools_with_metadata returns 62 tools incl. all 21 ported.

Remaining: Settings-window visual tab walk (user); private-remote setup + push (deferred).
