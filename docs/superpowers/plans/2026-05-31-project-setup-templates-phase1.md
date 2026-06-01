# Project-setup Templates — Phase 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:executing-plans (or subagent-driven-development). Steps use checkbox (`- [ ]`).

**Goal:** A `manage_project_template` bridge tool that inspects an avatar's project state and idempotently applies the missing pro-foundation for an `avatar-pc` (VRC Avatar Descriptor + viewpoint, Expression Menu/Parameters assets, recommended folders) — non-destructive, with the deterministic detection/diff core unit-tested.

**Architecture:** A **pure** `ProjectTemplateEngine` (platform classification + missing-step diff from an `AvatarState` struct) is the testable core. The `manage_project_template` handler builds `AvatarState` from a live GameObject (reading the VRC descriptor via **reflection**, since the package can't hard-reference the VRChat SDK), then `apply` runs the not-done steps — folders via `AssetDatabase`, VRC components/assets via reflection (skipped with a clear note if the SDK is absent). `inspect`/`list` are read-only.

**Tech Stack:** Unity 2022.3 Editor C#, reflection for VRCSDK3 Avatars types (`VRCAvatarDescriptor`, `VRCExpressionsMenu`, `VRCExpressionParameters`), `AssetDatabase`, the existing legacy-switch dispatcher, Newtonsoft.Json, Node relay (zod).

**Spec:** `docs/superpowers/specs/2026-05-31-project-setup-templates-design.md`

**Dev-loop / test note (read first):** package is junction-embedded in Leaf; C# edits compile only after the user **focuses Unity** (Auto Refresh). Verify compiles with `read_console {level:"error"}` (NOT `get_compilation_errors`) and confirm live via `health_check.buildStamp` changing. Run EditMode tests with `driver.mjs tests editmode` (jobs SessionState-persist across the run's reload); the ~17 YUCP/VPM failures are pre-existing — guard that the count stays 17. Leaf has the VRChat SDK + the `LEAF` (PC) and `LEAF QUEST` avatars, so reflection paths verify live there.

---

## File Structure

**Create:**
- `com.autonomous-unity.mcp/Editor/Templates/ProjectTemplateModels.cs` — `AvatarState` struct, `TemplateStep`, `InspectReport`, `ApplyResult`.
- `com.autonomous-unity.mcp/Editor/Templates/ProjectTemplateEngine.cs` — pure: `ClassifyPlatform`, `ComputeSteps`. No Unity mutation, no reflection. Unit-testable.
- `com.autonomous-unity.mcp/Editor/Templates/VrcReflection.cs` — `internal static` helpers wrapping the VRCSDK3 reflection (find descriptor type, has/add descriptor, read/set viewpoint, create + link expression assets). Returns booleans/notes; no exceptions escape.
- `com.autonomous-unity.mcp/Editor/Tests/ProjectTemplateEngineTests.cs` — key-free EditMode tests for the engine.

**Modify:**
- `com.autonomous-unity.mcp/Editor/AutonomousMcpToolDispatcher.cs` — `HandleManageProjectTemplate` (+ `inspect`/`list`/`apply`), switch case, `LegacyToolNames`, `supportedTools`.
- `server/src/mcpServer.ts` — register `manage_project_template`.

> Placement: all Core (no new asmdef).

---

## Task 1: Models

**Files:** Create `Editor/Templates/ProjectTemplateModels.cs`

- [ ] **Step 1: Create the file**

```csharp
using System;
using System.Collections.Generic;

namespace AutonomousMcp.Editor.Templates
{
    // Snapshot of one avatar's setup state, built from the live scene by the handler and fed to
    // the pure engine. Booleans only — no Unity types — so the engine stays unit-testable.
    public struct AvatarState
    {
        public bool hasDescriptor;
        public bool hasViewpoint;
        public bool hasExpressionMenu;
        public bool hasExpressionParams;
        public bool hasFolders;
    }

    [Serializable]
    public sealed class TemplateStep
    {
        public string id;
        public string label;
        public bool done;
    }

    [Serializable]
    public sealed class InspectReport
    {
        public string avatarName;
        public string platform;      // "pc" | "quest" | "unknown"
        public bool isAvatar;
        public List<TemplateStep> steps = new List<TemplateStep>();
    }

    [Serializable]
    public sealed class ApplyResult
    {
        public string avatarName;
        public List<string> changed = new List<string>();
        public List<string> skipped = new List<string>();
        public List<string> notes = new List<string>();
    }
}
```

- [ ] **Step 2: Commit** — `git commit -m "feat(templates): project-template data models"`

---

## Task 2: Pure engine — platform classification + step diff (TDD)

**Files:** Create `Editor/Templates/ProjectTemplateEngine.cs`; Test `Editor/Tests/ProjectTemplateEngineTests.cs`

- [ ] **Step 1: Failing tests**

```csharp
using NUnit.Framework;
using AutonomousMcp.Editor.Templates;

namespace AutonomousMcp.SelfTest
{
    public sealed class ProjectTemplateEngineTests
    {
        [Test]
        public void ClassifyPlatform_detects_quest_from_name()
        {
            Assert.AreEqual("quest", ProjectTemplateEngine.ClassifyPlatform("LEAF QUEST"));
            Assert.AreEqual("quest", ProjectTemplateEngine.ClassifyPlatform("avatar_Android"));
            Assert.AreEqual("pc", ProjectTemplateEngine.ClassifyPlatform("LEAF"));
            Assert.AreEqual("unknown", ProjectTemplateEngine.ClassifyPlatform(""));
        }

        [Test]
        public void ComputeSteps_marks_done_from_state()
        {
            var s = new AvatarState { hasDescriptor = true, hasViewpoint = false,
                hasExpressionMenu = true, hasExpressionParams = false, hasFolders = true };
            var steps = ProjectTemplateEngine.ComputeSteps(s);
            Assert.AreEqual(4, steps.Count);
            Assert.IsTrue(steps.Find(x => x.id == "descriptor").done);
            Assert.IsFalse(steps.Find(x => x.id == "viewpoint").done);
            // expressions step needs BOTH menu and params
            Assert.IsFalse(steps.Find(x => x.id == "expressions").done);
            Assert.IsTrue(steps.Find(x => x.id == "folders").done);
        }

        [Test]
        public void ComputeSteps_expressions_done_only_when_both_present()
        {
            var s = new AvatarState { hasExpressionMenu = true, hasExpressionParams = true };
            Assert.IsTrue(ProjectTemplateEngine.ComputeSteps(s).Find(x => x.id == "expressions").done);
        }
    }
}
```

- [ ] **Step 2: Run — expect FAIL** — `driver.mjs tests editmode` (after focus-compile): `ProjectTemplateEngine` undefined.

- [ ] **Step 3: Implement the engine**

```csharp
using System.Collections.Generic;

namespace AutonomousMcp.Editor.Templates
{
    // Pure, deterministic, unit-testable. No Unity API, no reflection.
    internal static class ProjectTemplateEngine
    {
        public static string ClassifyPlatform(string name)
        {
            if (string.IsNullOrEmpty(name)) return "unknown";
            var n = name.ToLowerInvariant();
            if (n.Contains("quest") || n.Contains("android")) return "quest";
            return "pc";
        }

        public static List<TemplateStep> ComputeSteps(AvatarState s)
        {
            return new List<TemplateStep>
            {
                new TemplateStep { id = "descriptor",  label = "VRC Avatar Descriptor",        done = s.hasDescriptor },
                new TemplateStep { id = "viewpoint",   label = "Viewpoint set",                done = s.hasViewpoint },
                new TemplateStep { id = "expressions", label = "Expression Menu + Parameters", done = s.hasExpressionMenu && s.hasExpressionParams },
                new TemplateStep { id = "folders",     label = "Project folders",              done = s.hasFolders },
            };
        }
    }
}
```

- [ ] **Step 4: Run — expect PASS** — 3 engine tests green; overall failed count stays 17.

- [ ] **Step 5: Commit** — `git commit -m "feat(templates): pure engine (platform classify + step diff) + tests"`

---

## Task 3: VRC reflection helpers (live-verified, SDK-safe)

**Files:** Create `Editor/Templates/VrcReflection.cs`

Not unit-tested (needs the SDK); each method no-ops + returns false/note when the SDK or a member is absent, so the package always compiles and runs without VRChat.

- [ ] **Step 1: Create the helper**

```csharp
using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace AutonomousMcp.Editor.Templates
{
    internal static class VrcReflection
    {
        private const string DescriptorTypeName  = "VRC.SDK3.Avatars.Components.VRCAvatarDescriptor";
        private const string MenuTypeName        = "VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu";
        private const string ParamsTypeName      = "VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters";

        public static bool SdkPresent => FindType(DescriptorTypeName) != null;

        public static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { var t = asm.GetType(fullName); if (t != null) return t; } catch { }
            }
            return null;
        }

        public static Component GetDescriptor(GameObject go)
        {
            var t = FindType(DescriptorTypeName);
            return t == null || go == null ? null : go.GetComponent(t);
        }

        // ViewPosition is a public Vector3 field on VRCAvatarDescriptor. Treat default (0,0,0) as unset.
        public static bool HasViewpoint(Component descriptor)
        {
            if (descriptor == null) return false;
            var f = descriptor.GetType().GetField("ViewPosition");
            if (f == null) return false;
            var v = (Vector3)f.GetValue(descriptor);
            return v != Vector3.zero;
        }

        public static bool HasExpressionMenu(Component descriptor) => RefNonNull(descriptor, "expressionsMenu");
        public static bool HasExpressionParams(Component descriptor) => RefNonNull(descriptor, "expressionParameters");

        private static bool RefNonNull(Component descriptor, string fieldName)
        {
            if (descriptor == null) return false;
            var f = descriptor.GetType().GetField(fieldName);
            return f != null && f.GetValue(descriptor) != null;
        }

        // ── mutations (apply) ──

        public static Component AddDescriptor(GameObject go)
        {
            var t = FindType(DescriptorTypeName);
            if (t == null || go == null) return null;
            return go.GetComponent(t) ?? go.AddComponent(t);
        }

        // Default viewpoint: head bone position (humanoid) nudged slightly forward, in avatar-local space.
        public static bool SetDefaultViewpoint(GameObject avatar, Component descriptor)
        {
            if (avatar == null || descriptor == null) return false;
            var f = descriptor.GetType().GetField("ViewPosition");
            if (f == null) return false;
            var animator = avatar.GetComponent<Animator>();
            Vector3 local = new Vector3(0f, 1.5f, 0.1f); // fallback if no head bone
            if (animator != null && animator.isHuman)
            {
                var head = animator.GetBoneTransform(HumanBodyBones.Head);
                if (head != null)
                    local = avatar.transform.InverseTransformPoint(head.position) + new Vector3(0f, 0.05f, 0.1f);
            }
            f.SetValue(descriptor, local);
            return true;
        }

        // Create the two expression assets at folderPath (e.g. "Assets/_Project/<Name>/Expressions"),
        // link them to the descriptor, set customExpressions = true. Returns notes via out.
        public static bool CreateAndLinkExpressions(Component descriptor, string folderPath, out string note)
        {
            note = null;
            var menuType = FindType(MenuTypeName);
            var paramsType = FindType(ParamsTypeName);
            if (descriptor == null || menuType == null || paramsType == null)
            { note = "VRChat SDK expression types not found"; return false; }

            var menu = ScriptableObject.CreateInstance(menuType);
            var prms = ScriptableObject.CreateInstance(paramsType);
            UnityEditor.AssetDatabase.CreateAsset(menu, folderPath + "/ExpressionsMenu.asset");
            UnityEditor.AssetDatabase.CreateAsset(prms, folderPath + "/ExpressionParameters.asset");

            var dt = descriptor.GetType();
            dt.GetField("customExpressions")?.SetValue(descriptor, true);
            dt.GetField("expressionsMenu")?.SetValue(descriptor, menu);
            dt.GetField("expressionParameters")?.SetValue(descriptor, prms);
            UnityEditor.EditorUtility.SetDirty(descriptor);
            return true;
        }
    }
}
```

- [ ] **Step 2: Compile (user focus) + verify** — `read_console {level:"error"}` 0 CS errors (it must compile even though tests don't cover it).

- [ ] **Step 3: Commit** — `git commit -m "feat(templates): VRCSDK reflection helpers (SDK-safe)"`

---

## Task 4: `manage_project_template` — `inspect` + `list`

**Files:** Modify `Editor/AutonomousMcpToolDispatcher.cs`

- [ ] **Step 1: Add the handler** (near `HandleHudPost`)

```csharp
        internal static AutonomousMcpToolResponse HandleManageProjectTemplate(JObject args)
        {
            var action = args.Value<string>("action") ?? "inspect";
            switch (action)
            {
                case "list":
                    return Success(JToken.FromObject(new { templates = new[] { "avatar-pc" } }));
                case "inspect":
                    return Success(JToken.FromObject(InspectScene()));
                case "apply":
                    return Success(JToken.FromObject(ApplyTemplate(args)));
                default:
                    return Error($"manage_project_template: unknown action '{action}'.");
            }
        }

        private static System.Collections.Generic.List<AutonomousMcp.Editor.Templates.InspectReport> InspectScene()
        {
            var reports = new System.Collections.Generic.List<AutonomousMcp.Editor.Templates.InspectReport>();
            foreach (var go in EnumerateAvatarRoots())
            {
                var desc = AutonomousMcp.Editor.Templates.VrcReflection.GetDescriptor(go);
                var animator = go.GetComponent<Animator>();
                var isAvatar = desc != null || (animator != null && animator.isHuman);
                if (!isAvatar) continue;

                var state = new AutonomousMcp.Editor.Templates.AvatarState
                {
                    hasDescriptor       = desc != null,
                    hasViewpoint        = AutonomousMcp.Editor.Templates.VrcReflection.HasViewpoint(desc),
                    hasExpressionMenu   = AutonomousMcp.Editor.Templates.VrcReflection.HasExpressionMenu(desc),
                    hasExpressionParams = AutonomousMcp.Editor.Templates.VrcReflection.HasExpressionParams(desc),
                    hasFolders          = AssetDatabase.IsValidFolder(AvatarFolder(go.name)),
                };
                reports.Add(new AutonomousMcp.Editor.Templates.InspectReport
                {
                    avatarName = go.name,
                    platform   = AutonomousMcp.Editor.Templates.ProjectTemplateEngine.ClassifyPlatform(go.name),
                    isAvatar   = true,
                    steps      = AutonomousMcp.Editor.Templates.ProjectTemplateEngine.ComputeSteps(state),
                });
            }
            return reports;
        }

        // Top-level scene roots that look like avatars (humanoid Animator or a VRC descriptor).
        private static System.Collections.Generic.IEnumerable<GameObject> EnumerateAvatarRoots()
        {
            var scene = SceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects())
            {
                var animator = root.GetComponent<Animator>();
                if (AutonomousMcp.Editor.Templates.VrcReflection.GetDescriptor(root) != null ||
                    (animator != null && animator.isHuman))
                    yield return root;
            }
        }

        private static string AvatarFolder(string avatarName)
        {
            var safe = string.Join("_", avatarName.Split(System.IO.Path.GetInvalidFileNameChars()));
            return "Assets/_Project/" + safe;
        }
```

- [ ] **Step 2: Wire switch + names** — add `case "manage_project_template": legacy = HandleManageProjectTemplate(args); break;`; add `"manage_project_template"` to `LegacyToolNames` and `supportedTools`.

- [ ] **Step 3: Compile (user focus) + verify on Leaf**

```bash
node .claude/skills/run-autonomous-unity-mcp/driver.mjs call manage_project_template '{"action":"inspect"}'
```
Expected: a report array naming `LEAF` (platform "pc") and `LEAF QUEST` (platform "quest"), each with the 4 steps and their done flags. `read_console{level:error}` 0 errors.

- [ ] **Step 4: Commit** — `git commit -m "feat(templates): manage_project_template inspect + list"`

---

## Task 5: `apply` — folders + descriptor + viewpoint + expressions (idempotent)

**Files:** Modify `Editor/AutonomousMcpToolDispatcher.cs`

- [ ] **Step 1: Add `ApplyTemplate`**

```csharp
        private static AutonomousMcp.Editor.Templates.ApplyResult ApplyTemplate(JObject args)
        {
            var targetName = args.Value<string>("avatar");
            var result = new AutonomousMcp.Editor.Templates.ApplyResult();

            GameObject avatar = null;
            foreach (var go in EnumerateAvatarRoots())
                if (string.IsNullOrEmpty(targetName) || go.name == targetName) { avatar = go; break; }
            if (avatar == null) { result.notes.Add("No avatar found in the active scene."); return result; }
            result.avatarName = avatar.name;

            // 1) Folders (no SDK needed) — idempotent.
            var folder = AvatarFolder(avatar.name);
            if (!AssetDatabase.IsValidFolder(folder))
            {
                EnsureFolderPath(folder);
                result.changed.Add("Created folders under " + folder);
            }
            else result.skipped.Add("Folders already present");

            if (!AutonomousMcp.Editor.Templates.VrcReflection.SdkPresent)
            {
                result.skipped.Add("VRChat SDK not detected — skipped descriptor/expressions");
                result.notes.Add("Install the VRChat SDK (via the Creator Companion) for avatar components.");
                AssetDatabase.Refresh();
                return result;
            }

            // 2) Descriptor (idempotent add).
            var desc = AutonomousMcp.Editor.Templates.VrcReflection.GetDescriptor(avatar);
            if (desc == null)
            {
                desc = AutonomousMcp.Editor.Templates.VrcReflection.AddDescriptor(avatar);
                result.changed.Add("Added VRC Avatar Descriptor");
            }
            else result.skipped.Add("Descriptor already present");

            // 3) Viewpoint (only if unset).
            if (!AutonomousMcp.Editor.Templates.VrcReflection.HasViewpoint(desc))
            {
                if (AutonomousMcp.Editor.Templates.VrcReflection.SetDefaultViewpoint(avatar, desc))
                    result.changed.Add("Set default viewpoint");
            }
            else result.skipped.Add("Viewpoint already set");

            // 4) Expressions (only if missing).
            if (!AutonomousMcp.Editor.Templates.VrcReflection.HasExpressionMenu(desc) ||
                !AutonomousMcp.Editor.Templates.VrcReflection.HasExpressionParams(desc))
            {
                var exprFolder = folder + "/Expressions";
                EnsureFolderPath(exprFolder);
                if (AutonomousMcp.Editor.Templates.VrcReflection.CreateAndLinkExpressions(desc, exprFolder, out var note))
                    result.changed.Add("Created + linked Expression Menu/Parameters");
                else if (note != null) result.notes.Add(note);
            }
            else result.skipped.Add("Expression assets already present");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return result;
        }

        // Create "Assets/A/B/C" one segment at a time (AssetDatabase.CreateFolder needs each parent to exist).
        private static void EnsureFolderPath(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath)) return;
            var parts = assetPath.Split('/');
            var cur = parts[0]; // "Assets"
            for (int i = 1; i < parts.Length; i++)
            {
                var next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }
```

- [ ] **Step 2: Compile (user focus) + apply on Leaf**

```bash
node .claude/skills/run-autonomous-unity-mcp/driver.mjs call manage_project_template '{"action":"apply","avatar":"LEAF"}'
```
Expected: `changed`/`skipped`/`notes` arrays. On an already-set-up `LEAF`, most steps land in `skipped` (idempotent).

- [ ] **Step 3: Idempotency check** — run the same `apply` again; expect **all** steps in `skipped`, `changed` empty.

- [ ] **Step 4: Visual confirmation** — `inspect` again and/or screenshot the Inspector of `LEAF` → descriptor present, viewpoint set, expression assets linked; folders exist under `Assets/_Project/LEAF/`.

- [ ] **Step 5: Commit** — `git commit -m "feat(templates): apply (folders+descriptor+viewpoint+expressions), idempotent"`

---

## Task 6: Node registration + E2E + docs

**Files:** Modify `server/src/mcpServer.ts`, `CLAUDE.md`

- [ ] **Step 1: Register the tool**

```typescript
  server.tool(
    "manage_project_template",
    "Set up a VRChat avatar project to a pro baseline. inspect = report each avatar's state (PC/Quest) and what's missing; list = available templates; apply = idempotently add the missing foundation (VRC descriptor + viewpoint, Expression Menu/Parameters, project folders). Non-destructive; skips VRChat-SDK steps with a note if the SDK isn't present.",
    {
      action: z.enum(["inspect", "list", "apply"]).describe("inspect (read-only) | list | apply"),
      avatar: z.string().optional().describe("For apply: avatar root name (defaults to the first avatar found)"),
    },
    async (input) => callUnity("manage_project_template", input)
  );
```

- [ ] **Step 2: Build Node** — `npm --workspace server run build` (exit 0).

- [ ] **Step 3: Full regression sweep** — `driver.mjs tests editmode`: 3 engine tests green; overall failed == 17. Phase-1 (Advisor) + everything else unchanged.

- [ ] **Step 4: Doc + commit** — add a one-line `manage_project_template` bullet to `CLAUDE.md`'s tool list; `git commit -m "feat(templates): expose manage_project_template in the relay; docs"`.

---

## Done-when (Phase 1 acceptance)

- `manage_project_template inspect` on Leaf reports `LEAF` (pc) + `LEAF QUEST` (quest) with per-step done flags.
- `apply` idempotently adds only the missing pieces (folders + descriptor + viewpoint + expressions); a second `apply` changes nothing.
- No SDK → folders still created, VRC steps skipped with a clear note (never throws).
- 3 engine tests green; overall failed-count unchanged (17); nothing else regressed.
- Deferred to Phase 2: the **empty-scene starter scaffold** (from-scratch avatar root — Phase 1 sets up an *existing* avatar and reports "no avatar found" otherwise), the `avatar-quest` variant, PC↔Quest pairing, scoped project settings, the package/prefab interaction-notes layer, and Advisor "Set up my project" card wiring.
