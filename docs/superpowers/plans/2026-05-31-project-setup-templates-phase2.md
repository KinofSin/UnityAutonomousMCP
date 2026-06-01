# Project-setup Templates — Phase 2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:executing-plans. Steps use checkbox (`- [ ]`).

**Goal:** Round out the templates with PC↔Quest pairing detection, a package/prefab interaction-notes knowledge layer, an empty-scene starter scaffold, and an Advisor-HUD "Set up my project" entry — all additive on top of Phase 1.

**Architecture:** Pairing is pure logic added to `ProjectTemplateEngine` (unit-tested) and surfaced in `inspect`. Interaction notes are a declarative JSON data file read by a new `notes` action (and referenced in reports). The starter scaffold is the `apply`-with-no-avatar branch. The HUD gets one more quick-ask button. **No project-settings mutation** in this phase (deferred — risky).

**Tech Stack:** As Phase 1 (Unity 2022.3 Editor C#, reflection for VRCSDK, Newtonsoft, legacy-switch dispatcher, Node zod) + the Advisor HUD window from the HUD feature.

**Spec:** `docs/superpowers/specs/2026-05-31-project-setup-templates-design.md` · **Phase 1:** `…-phase1.md`

**Dev-loop / regression guard:** package junction-embedded in Leaf; compile via user focus; verify with `read_console{level:"error"}` + `health_check.buildStamp`. After each compile run `driver.mjs tests editmode` — the new pairing tests must pass and the overall **failed count must stay 17** (foreign YUCP/VPM). Verify live on `LEAF`/`LEAF QUEST`.

---

## Task 1: PC↔Quest pairing detection (pure, TDD)

**Files:** Modify `Editor/Templates/ProjectTemplateEngine.cs`, `Editor/Templates/ProjectTemplateModels.cs`; Test `Editor/Tests/ProjectTemplateEngineTests.cs`

- [ ] **Step 1: Failing tests (append to ProjectTemplateEngineTests)**

```csharp
        [Test]
        public void BaseName_strips_quest_and_android_tokens()
        {
            Assert.AreEqual("leaf", ProjectTemplateEngine.BaseName("LEAF QUEST"));
            Assert.AreEqual("leaf", ProjectTemplateEngine.BaseName("LEAF"));
            Assert.AreEqual("cat", ProjectTemplateEngine.BaseName("Cat_Android"));
        }

        [Test]
        public void ComputePairs_matches_pc_with_quest_twin()
        {
            var pairs = ProjectTemplateEngine.ComputePairs(
                new System.Collections.Generic.List<string> { "LEAF", "LEAF QUEST", "Lonely" });
            Assert.AreEqual("LEAF QUEST", pairs["LEAF"]);
            Assert.AreEqual("LEAF", pairs["LEAF QUEST"]);
            Assert.IsFalse(pairs.ContainsKey("Lonely"), "no twin -> not in the map");
        }
```

- [ ] **Step 2: Run — expect FAIL** (`BaseName`/`ComputePairs` undefined).

- [ ] **Step 3: Implement in `ProjectTemplateEngine`**

```csharp
        // Strip quest/android tokens and non-alphanumerics, lowercase — the "base" avatar identity.
        public static string BaseName(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            var n = name.ToLowerInvariant();
            n = n.Replace("quest", " ").Replace("android", " ").Replace("(pc)", " ").Replace("pc", " ");
            var sb = new System.Text.StringBuilder();
            foreach (var c in n) if (char.IsLetterOrDigit(c)) sb.Append(c);
            return sb.ToString();
        }

        // Map each avatar that has a PC↔Quest twin (same base name, different platform) to its twin's name.
        public static System.Collections.Generic.Dictionary<string, string> ComputePairs(
            System.Collections.Generic.List<string> names)
        {
            var pairs = new System.Collections.Generic.Dictionary<string, string>();
            for (int i = 0; i < names.Count; i++)
                for (int j = i + 1; j < names.Count; j++)
                {
                    if (BaseName(names[i]) != BaseName(names[j]) || BaseName(names[i]).Length == 0) continue;
                    if (ClassifyPlatform(names[i]) == ClassifyPlatform(names[j])) continue;
                    pairs[names[i]] = names[j];
                    pairs[names[j]] = names[i];
                }
            return pairs;
        }
```

- [ ] **Step 4: Add `pairedWith` to the report model** — in `ProjectTemplateModels.cs`, add to `InspectReport`:

```csharp
        public string pairedWith;    // the PC↔Quest twin's name, or null
```

- [ ] **Step 5: Surface it in `InspectScene`** — in `AutonomousMcpToolDispatcher.InspectScene`, after building `reports`, fill `pairedWith`:

```csharp
            var names = reports.ConvertAll(r => r.avatarName);
            var pairs = AutonomousMcp.Editor.Templates.ProjectTemplateEngine.ComputePairs(names);
            foreach (var r in reports)
                if (pairs.TryGetValue(r.avatarName, out var twin)) r.pairedWith = twin;
            return reports;
```

(Change the method's final `return reports;` to be preceded by this block.)

- [ ] **Step 6: Compile + run** — pairing tests green; overall failed == 17. Then `manage_project_template inspect` → `LEAF.pairedWith == "LEAF QUEST"` and vice-versa.

- [ ] **Step 7: Commit** — `git commit -m "feat(templates): PC<->Quest pairing detection + tests"`

---

## Task 2: Package/prefab interaction-notes knowledge layer

**Files:** Create `com.autonomous-unity.mcp/Editor/Templates/InteractionNotes.json`; Modify `AutonomousMcpToolDispatcher.cs`

- [ ] **Step 1: Create the notes data**

```json
{
  "version": 1,
  "notes": [
    { "topic": "modular-avatar-vs-vrcfury", "text": "Modular Avatar and VRCFury both run as non-destructive build plugins. They can coexist, but if both drive the same toggle/menu the merge order matters — prefer one system per feature to avoid duplicate menu controls." },
    { "topic": "poiyomi-locking", "text": "Lock Poiyomi materials BEFORE uploading (Unlocked shaders fail VRChat's shader keyword limits and bloat the avatar). Unlock to edit, re-lock to ship." },
    { "topic": "prefab-install-order", "text": "Install avatar base prefab first, then drag accessory/clothing prefabs UNDER the avatar so Modular Avatar's Bone Proxy / Merge Armature can find the target armature. Accessories placed at scene root won't merge." },
    { "topic": "quest-limits", "text": "Quest/Android: VRChat enforces material/poly/texture limits and only Quest-compatible shaders (no Poiyomi). Keep a separate Quest material set + fallback. Use the PC<->Quest twin (e.g. LEAF / LEAF QUEST) so you ship both." },
    { "topic": "physbones-vs-dynamicbones", "text": "Use PhysBones (VRChat SDK3), not legacy DynamicBones, for hair/clothing jiggle. PhysBones count against the avatar's performance rank." }
  ]
}
```

- [ ] **Step 2: Add a `notes` action** to `HandleManageProjectTemplate`'s switch:

```csharp
                case "notes":
                    return Success(InteractionNotes());
```

And the helper (near `InspectScene`):

```csharp
        private static JToken InteractionNotes()
        {
            try
            {
                var path = AutonomousMcp.Editor.Templates.TemplatePaths.InteractionNotesPath();
                if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
                    return JToken.Parse(System.IO.File.ReadAllText(path));
            }
            catch { /* fall through */ }
            return JToken.FromObject(new { version = 1, notes = new object[0] });
        }
```

- [ ] **Step 3: Add the path resolver** — Create `Editor/Templates/TemplatePaths.cs` (reuse the robust package-resolve pattern from the Skills tab):

```csharp
using System.IO;
using UnityEditor;

namespace AutonomousMcp.Editor.Templates
{
    internal static class TemplatePaths
    {
        public static string InteractionNotesPath()
        {
            try
            {
                var pkg = UnityEditor.PackageManager.PackageInfo.FindForAssembly(
                    typeof(TemplatePaths).Assembly);
                if (pkg != null && !string.IsNullOrEmpty(pkg.resolvedPath))
                {
                    var p = Path.Combine(pkg.resolvedPath, "Editor", "Templates", "InteractionNotes.json");
                    if (File.Exists(p)) return p;
                }
            }
            catch { }
            return string.Empty;
        }
    }
}
```

- [ ] **Step 4: Compile + verify** — `manage_project_template {action:"notes"}` returns the 5 notes. `read_console{level:error}` 0 errors.

- [ ] **Step 5: Commit** — `git commit -m "feat(templates): package/prefab interaction-notes knowledge layer (notes action)"`

---

## Task 3: Empty-scene starter scaffold

**Files:** Modify `AutonomousMcpToolDispatcher.cs` (`ApplyTemplate`)

- [ ] **Step 1: Replace the no-avatar early return** with a starter scaffold

In `ApplyTemplate`, change:

```csharp
            if (avatar == null) { result.notes.Add("No avatar found in the active scene."); return result; }
```

to:

```csharp
            if (avatar == null)
            {
                avatar = new GameObject("New Avatar");
                UnityEditor.Undo.RegisterCreatedObjectUndo(avatar, "Create starter avatar");
                result.changed.Add("Created starter avatar root 'New Avatar' (empty scene)");
                result.notes.Add("Drag your avatar mesh/FBX under 'New Avatar', then re-run apply to finish setup.");
            }
```

(The rest of `ApplyTemplate` — folders + SDK steps — then runs on the new root. The descriptor/viewpoint add safely; expressions create assets; humanoid-less viewpoint uses the fallback `(0,1.5,0.1)`.)

- [ ] **Step 2: Compile + verify (force the branch, then clean up)** — Leaf's scene has avatars, so target a name that matches none to force the no-avatar branch:
  `manage_project_template {action:"apply", avatar:"__mcp_scaffold_probe__"}` → report `changed` includes "Created starter avatar root 'New Avatar' (empty scene)". Then **clean up** the probe artifacts so the Leaf scene is left pristine:
  `manage_gameobject {action:"destroy", name:"New Avatar"}` and `manage_asset` delete `Assets/_Project/New Avatar` (or delete the folder via the Project window). Confirm the scene/project are back to their prior state.

- [ ] **Step 3: Commit** — `git commit -m "feat(templates): empty-scene starter avatar scaffold"`

---

## Task 4: Advisor HUD "Set up my project" quick-ask

**Files:** Modify `Editor/UI/AdvisorHudWindow.cs`

- [ ] **Step 1: Add a fourth quick-ask button** — in `DrawQuickAsk`, append:

```csharp
                if (GUILayout.Button("Set up my project", EditorStyles.miniButton)) QuickAsk("setup_project");
```

(No new code path needed: `QuickAsk` already enqueues `{type:"quick_ask", key:"setup_project"}`; the AI, on poll, runs `manage_project_template inspect` and posts a card.)

- [ ] **Step 2: Compile + verify** — click "Set up my project" in the HUD → `hud_poll` returns `{type:"quick_ask", payload:{key:"setup_project"}}`.

- [ ] **Step 3: Commit** — `git commit -m "feat(templates): Advisor HUD 'Set up my project' quick-ask"`

---

## Task 5: Node schema + E2E + docs

**Files:** Modify `server/src/mcpServer.ts`, `CLAUDE.md`

- [ ] **Step 1: Add `notes` to the action enum** — in the `manage_project_template` registration change `z.enum(["inspect", "list", "apply"])` to `z.enum(["inspect", "list", "apply", "notes"])` and mention notes in the description.

- [ ] **Step 2: Build Node** — `npm --workspace server run build` (exit 0).

- [ ] **Step 3: Full regression sweep** — `driver.mjs tests editmode`: pairing tests green; overall failed == 17.

- [ ] **Step 4: Doc + commit** — update the `manage_project_template` CLAUDE.md bullet to mention `notes` + pairing; `git commit -m "feat(templates): notes action in relay; docs"`.

---

## Done-when (Phase 2 acceptance)

- `inspect` reports `pairedWith` (LEAF↔LEAF QUEST); pairing logic unit-tested.
- `notes` returns the interaction-notes knowledge layer.
- `apply` on an empty scene creates a starter avatar root (+ folders/descriptor) with guidance.
- The HUD "Set up my project" button enqueues a `quick_ask{key:"setup_project"}`.
- Pairing tests green; overall failed-count unchanged (17); Phase 1 + HUD + everything else unregressed.
- Deferred (own step): project-settings mutation (color space etc.).
