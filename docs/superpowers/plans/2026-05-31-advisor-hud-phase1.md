# Advisor HUD — Phase 1 (Backbone) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the usable backbone of the in-Unity Advisor HUD — the AI can post text advice into a dockable Unity panel, and the user can send a note / the current selection / console errors back, picked up by the AI via an outbox (explicit `hud_poll` + an auto-piggyback `pending` count on every tool response).

**Architecture:** A pure-C# `AdvisorStore` (advice feed + outbox ring buffers, `SessionState`-persisted so it survives domain reloads) is the single source of truth. A dockable `AdvisorHudWindow` renders it and enqueues user sends. Two legacy bridge tools — `hud_post` (AI→store) and `hud_poll` (store→AI, drains the outbox) — plus a one-line piggyback that stamps every dispatch response with the pending outbox count. The HUD never executes Unity changes; it only relays.

**Tech Stack:** Unity 2022.3 Editor C# (IMGUI `EditorWindow`, `SessionState`, `EditorPrefs`), Newtonsoft.Json (already a package dependency, used for store serialization), the existing `AutonomousMcpToolDispatcher` legacy-switch + `AutonomousMcpMainThread`, and the Node MCP relay (`server/src/mcpServer.ts`, zod).

**Spec:** `docs/superpowers/specs/2026-05-31-advisor-hud-design.md`

**Project test/dev-loop note (read before running anything):** This package is mounted into the Leaf project as an embedded junction. C# edits go live only after Unity recompiles — **the user must focus Unity (Auto Refresh on) to compile**, since driving the bridge while the editor is unfocored will not recompile (see CLAUDE.md). After a focused recompile: verify compiles with `read_console {level:"error"}` (NOT `get_compilation_errors`, which reads a stale assembly), confirm the live build via `health_check.buildStamp` changing, then run EditMode tests with `run_tests {mode:"editmode"}` → poll `get_test_job`. The driver `.claude/skills/run-autonomous-unity-mcp/driver.mjs` wraps these.

---

## File Structure

**Create:**
- `com.autonomous-unity.mcp/Editor/Advisor/AdvisorModels.cs` — serializable data types: `AdviceItem`, `CardAction`, `OutboxItem`. One responsibility: the shapes that cross the store/UI/bridge boundary.
- `com.autonomous-unity.mcp/Editor/Advisor/AdvisorStore.cs` — `internal static` store: advice feed + outbox, ring-buffer caps, `SessionState` persistence, `PendingCount`. No UI, no network. Fully unit-testable.
- `com.autonomous-unity.mcp/Editor/UI/AdvisorHudWindow.cs` — dockable `EditorWindow` rendering the store and enqueuing user sends.
- `com.autonomous-unity.mcp/Editor/Tests/AdvisorStoreTests.cs` — EditMode unit tests for the store (key-free, no bridge).

**Modify:**
- `com.autonomous-unity.mcp/Editor/AutonomousMcpToolDispatcher.cs` — add `HandleHudPost` + `HandleHudPoll`, their switch cases, `LegacyToolNames` entries, and the piggyback stamp in the response path.
- `com.autonomous-unity.mcp/Editor/AutonomousMcpToolResponse` (in its defining file) — add an optional `hudOutbox` field carrying `{ pending }`.
- `server/src/mcpServer.ts` — register `hud_post` and `hud_poll`.

> Assembly placement: `Editor/Advisor/` and `Editor/UI/` are **Core** (no `.asmdef` there → covered by `AutonomousMcp.Editor`). The dispatcher is Core. No new assembly.

---

## Task 1: Advisor data models

**Files:**
- Create: `com.autonomous-unity.mcp/Editor/Advisor/AdvisorModels.cs`

- [ ] **Step 1: Create the models file**

```csharp
using System;
using System.Collections.Generic;

namespace AutonomousMcp.Editor.Advisor
{
    // One advice entry shown in the HUD feed. kind = "text" or "card".
    [Serializable]
    public sealed class AdviceItem
    {
        public string id;
        public string kind;          // "text" | "card"
        public string level;         // "info" | "success" | "warning"
        public string text;          // kind == "text"
        public string title;         // kind == "card"
        public string body;          // kind == "card"
        public List<CardAction> actions = new List<CardAction>(); // kind == "card"
        public string postedAtUtc;
    }

    [Serializable]
    public sealed class CardAction
    {
        public string id;
        public string label;
    }

    // One queued user→AI item. payload is a free-form JSON string (note text,
    // selection summary, console entries, etc.) interpreted by the AI client.
    [Serializable]
    public sealed class OutboxItem
    {
        public string type;          // note|selection|screenshot|console|card_action|quick_ask
        public string payload;       // JSON string
        public string enqueuedAtUtc;
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add com.autonomous-unity.mcp/Editor/Advisor/AdvisorModels.cs
git commit -m "feat(advisor): data models for advice feed + outbox"
```

---

## Task 2: AdvisorStore — advice feed with ring-buffer cap (TDD)

**Files:**
- Create: `com.autonomous-unity.mcp/Editor/Advisor/AdvisorStore.cs`
- Test: `com.autonomous-unity.mcp/Editor/Tests/AdvisorStoreTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using System.Linq;
using NUnit.Framework;
using AutonomousMcp.Editor.Advisor;

namespace AutonomousMcp.SelfTest
{
    public sealed class AdvisorStoreTests
    {
        [SetUp] public void Reset() => AdvisorStore.ResetForTests();

        [Test]
        public void AddAdvice_appends_and_returns_in_order()
        {
            AdvisorStore.AddText("first", "info");
            AdvisorStore.AddText("second", "warning");
            var all = AdvisorStore.GetAdvice();
            Assert.AreEqual(2, all.Count);
            Assert.AreEqual("first", all[0].text);
            Assert.AreEqual("second", all[1].text);
            Assert.AreEqual("warning", all[1].level);
        }

        [Test]
        public void AddAdvice_caps_at_MaxAdvice_dropping_oldest()
        {
            for (int i = 0; i < AdvisorStore.MaxAdvice + 10; i++)
                AdvisorStore.AddText("a" + i, "info");
            var all = AdvisorStore.GetAdvice();
            Assert.AreEqual(AdvisorStore.MaxAdvice, all.Count);
            Assert.AreEqual("a10", all[0].text); // first 10 dropped
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

After a focused recompile (see dev-loop note): `node .claude/skills/run-autonomous-unity-mcp/driver.mjs tests editmode`
Expected: FAIL — `AdvisorStore` does not exist / `AddText` not defined.

- [ ] **Step 3: Write minimal implementation**

```csharp
using System;
using System.Collections.Generic;
using UnityEditor;

namespace AutonomousMcp.Editor.Advisor
{
    internal static class AdvisorStore
    {
        public const int MaxAdvice = 100;
        public const int MaxOutbox = 50;

        private static readonly List<AdviceItem> _advice = new List<AdviceItem>();

        public static void AddText(string text, string level)
        {
            var item = new AdviceItem
            {
                id = Guid.NewGuid().ToString("N").Substring(0, 8),
                kind = "text",
                level = string.IsNullOrEmpty(level) ? "info" : level,
                text = text ?? string.Empty,
                postedAtUtc = DateTime.UtcNow.ToString("o")
            };
            _advice.Add(item);
            while (_advice.Count > MaxAdvice) _advice.RemoveAt(0);
        }

        public static List<AdviceItem> GetAdvice() => new List<AdviceItem>(_advice);

        // Test seam — clears in-memory state.
        public static void ResetForTests() => _advice.Clear();
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `node .claude/skills/run-autonomous-unity-mcp/driver.mjs tests editmode`
Expected: PASS (the two advice tests; the whole EditMode suite also runs — the ~17 unrelated YUCP/VPM failures are pre-existing and ignored).

- [ ] **Step 5: Commit**

```bash
git add com.autonomous-unity.mcp/Editor/Advisor/AdvisorStore.cs com.autonomous-unity.mcp/Editor/Tests/AdvisorStoreTests.cs
git commit -m "feat(advisor): store advice feed with ring-buffer cap"
```

---

## Task 3: AdvisorStore — outbox enqueue/drain/pending (TDD)

**Files:**
- Modify: `com.autonomous-unity.mcp/Editor/Advisor/AdvisorStore.cs`
- Test: `com.autonomous-unity.mcp/Editor/Tests/AdvisorStoreTests.cs`

- [ ] **Step 1: Write the failing test (append to AdvisorStoreTests)**

```csharp
        [Test]
        public void Outbox_enqueue_then_drain_returns_in_order_and_clears()
        {
            AdvisorStore.Enqueue("note", "{\"text\":\"hi\"}");
            AdvisorStore.Enqueue("console", "{\"entries\":[]}");
            Assert.AreEqual(2, AdvisorStore.PendingCount());

            var drained = AdvisorStore.DrainOutbox();
            Assert.AreEqual(2, drained.Count);
            Assert.AreEqual("note", drained[0].type);
            Assert.AreEqual("console", drained[1].type);
            Assert.AreEqual(0, AdvisorStore.PendingCount(), "drain clears the queue");
            Assert.AreEqual(0, AdvisorStore.DrainOutbox().Count, "second drain is empty");
        }

        [Test]
        public void Outbox_caps_at_MaxOutbox_dropping_oldest()
        {
            for (int i = 0; i < AdvisorStore.MaxOutbox + 5; i++)
                AdvisorStore.Enqueue("note", "{\"n\":" + i + "}");
            Assert.AreEqual(AdvisorStore.MaxOutbox, AdvisorStore.PendingCount());
            var drained = AdvisorStore.DrainOutbox();
            StringAssert.Contains("\"n\":5", drained[0].payload); // first 5 dropped
        }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `node .claude/skills/run-autonomous-unity-mcp/driver.mjs tests editmode`
Expected: FAIL — `Enqueue`/`DrainOutbox`/`PendingCount` not defined.

- [ ] **Step 3: Add the outbox to AdvisorStore**

```csharp
        private static readonly List<OutboxItem> _outbox = new List<OutboxItem>();

        public static void Enqueue(string type, string payloadJson)
        {
            _outbox.Add(new OutboxItem
            {
                type = type,
                payload = payloadJson ?? string.Empty,
                enqueuedAtUtc = DateTime.UtcNow.ToString("o")
            });
            while (_outbox.Count > MaxOutbox) _outbox.RemoveAt(0);
        }

        public static int PendingCount() => _outbox.Count;

        public static List<OutboxItem> DrainOutbox()
        {
            var copy = new List<OutboxItem>(_outbox);
            _outbox.Clear();
            return copy;
        }
```

Also extend `ResetForTests`:

```csharp
        public static void ResetForTests() { _advice.Clear(); _outbox.Clear(); }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `node .claude/skills/run-autonomous-unity-mcp/driver.mjs tests editmode`
Expected: PASS (four AdvisorStore tests now green).

- [ ] **Step 5: Commit**

```bash
git add com.autonomous-unity.mcp/Editor/Advisor/AdvisorStore.cs com.autonomous-unity.mcp/Editor/Tests/AdvisorStoreTests.cs
git commit -m "feat(advisor): outbox enqueue/drain/pending with cap"
```

---

## Task 4: AdvisorStore — SessionState persistence (reload survival) (TDD)

**Files:**
- Modify: `com.autonomous-unity.mcp/Editor/Advisor/AdvisorStore.cs`
- Test: `com.autonomous-unity.mcp/Editor/Tests/AdvisorStoreTests.cs`

- [ ] **Step 1: Write the failing test (append)**

```csharp
        [Test]
        public void State_round_trips_through_SessionState()
        {
            AdvisorStore.AddText("persisted advice", "info");
            AdvisorStore.Enqueue("note", "{\"text\":\"persisted note\"}");

            // Simulate a domain reload: drop in-memory state, then reload from SessionState.
            AdvisorStore.DropInMemoryForTests();
            AdvisorStore.EnsureLoaded();

            Assert.AreEqual(1, AdvisorStore.GetAdvice().Count);
            Assert.AreEqual("persisted advice", AdvisorStore.GetAdvice()[0].text);
            Assert.AreEqual(1, AdvisorStore.PendingCount());
            Assert.AreEqual("note", AdvisorStore.DrainOutbox()[0].type);
        }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `node .claude/skills/run-autonomous-unity-mcp/driver.mjs tests editmode`
Expected: FAIL — `DropInMemoryForTests`/`EnsureLoaded` not defined; state not persisted.

- [ ] **Step 3: Add persistence (Newtonsoft via SessionState)**

Add `using Newtonsoft.Json;` at the top. Add keys + persist-on-mutation + load:

```csharp
        private const string AdviceKey = "AutonomousMcp.Advisor.Advice";
        private const string OutboxKey = "AutonomousMcp.Advisor.Outbox";
        private static bool _loaded;

        public static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            TryLoad(AdviceKey, _advice);
            TryLoad(OutboxKey, _outbox);
        }

        private static void TryLoad<T>(string key, List<T> into)
        {
            into.Clear();
            var json = SessionState.GetString(key, string.Empty);
            if (string.IsNullOrEmpty(json)) return;
            try
            {
                var list = JsonConvert.DeserializeObject<List<T>>(json);
                if (list != null) into.AddRange(list);
            }
            catch { /* corrupt/empty — start fresh */ }
        }

        private static void PersistAdvice() => SessionState.SetString(AdviceKey, JsonConvert.SerializeObject(_advice));
        private static void PersistOutbox() => SessionState.SetString(OutboxKey, JsonConvert.SerializeObject(_outbox));

        // Test seam — drop RAM only, leave SessionState intact (simulates a domain reload).
        public static void DropInMemoryForTests() { _advice.Clear(); _outbox.Clear(); _loaded = false; }
```

Call `EnsureLoaded()` at the top of `AddText`, `GetAdvice`, `Enqueue`, `PendingCount`, `DrainOutbox`; call `PersistAdvice()` after mutating `_advice` and `PersistOutbox()` after mutating `_outbox`. Update `ResetForTests` to also clear SessionState:

```csharp
        public static void ResetForTests()
        {
            _advice.Clear(); _outbox.Clear(); _loaded = true;
            SessionState.EraseString(AdviceKey); SessionState.EraseString(OutboxKey);
        }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `node .claude/skills/run-autonomous-unity-mcp/driver.mjs tests editmode`
Expected: PASS (all AdvisorStore tests green, including round-trip).

- [ ] **Step 5: Commit**

```bash
git add com.autonomous-unity.mcp/Editor/Advisor/AdvisorStore.cs com.autonomous-unity.mcp/Editor/Tests/AdvisorStoreTests.cs
git commit -m "feat(advisor): SessionState persistence (survives domain reloads)"
```

---

## Task 5: `hud_post` bridge tool (AI → HUD)

**Files:**
- Modify: `com.autonomous-unity.mcp/Editor/AutonomousMcpToolDispatcher.cs`

- [ ] **Step 1: Add the handler** (place it next to `HandleHealthCheck`)

```csharp
        internal static AutonomousMcpToolResponse HandleHudPost(JObject args)
        {
            var text = args.Value<string>("text");
            if (string.IsNullOrWhiteSpace(text))
                return Error("hud_post requires non-empty 'text'.");
            var level = args.Value<string>("level") ?? "info";
            AutonomousMcp.Editor.Advisor.AdvisorStore.AddText(text, level);
            return Success(JToken.FromObject(new { posted = true, level }));
        }
```

- [ ] **Step 2: Wire the switch + legacy names**

In the legacy `switch`, add: `case "hud_post": legacy = HandleHudPost(args); break;`
In `LegacyToolNames` (and the `supportedTools` array in `HandleHealthCheck`), add `"hud_post"`.

- [ ] **Step 3: Verify over the bridge** (after focused recompile)

```bash
node .claude/skills/run-autonomous-unity-mcp/driver.mjs call hud_post '{"text":"hello from the AI","level":"success"}'
```
Expected: `{ "success": true, "data": { "posted": true, "level": "success" } }` and `read_console {level:"error"}` shows 0 CS errors.

- [ ] **Step 4: Commit**

```bash
git add com.autonomous-unity.mcp/Editor/AutonomousMcpToolDispatcher.cs
git commit -m "feat(advisor): hud_post bridge tool (AI posts advice)"
```

---

## Task 6: `hud_poll` bridge tool (HUD → AI, drains outbox)

**Files:**
- Modify: `com.autonomous-unity.mcp/Editor/AutonomousMcpToolDispatcher.cs`

- [ ] **Step 1: Add the handler**

```csharp
        internal static AutonomousMcpToolResponse HandleHudPoll(JObject args)
        {
            var items = AutonomousMcp.Editor.Advisor.AdvisorStore.DrainOutbox();
            return Success(JToken.FromObject(new
            {
                count = items.Count,
                items = items.Select(i => new
                {
                    type = i.type,
                    payload = i.payload,
                    enqueuedAtUtc = i.enqueuedAtUtc
                }).ToArray()
            }));
        }
```

(`System.Linq` is already imported in the dispatcher.)

- [ ] **Step 2: Wire the switch + legacy names**

Add `case "hud_poll": legacy = HandleHudPoll(args); break;` and add `"hud_poll"` to `LegacyToolNames` + `supportedTools`.

- [ ] **Step 3: Verify over the bridge** (after focused recompile)

```bash
node .claude/skills/run-autonomous-unity-mcp/driver.mjs call hud_poll '{}'
```
Expected: `{ "success": true, "data": { "count": 0, "items": [] } }` when the outbox is empty (it will have items after Task 9's UI sends).

- [ ] **Step 4: Commit**

```bash
git add com.autonomous-unity.mcp/Editor/AutonomousMcpToolDispatcher.cs
git commit -m "feat(advisor): hud_poll bridge tool (drains the outbox)"
```

---

## Task 7: Piggyback `hudOutbox.pending` on every response

**Files:**
- Modify: `com.autonomous-unity.mcp/Editor/AutonomousMcpToolResponse.cs` (the file declaring the type)
- Modify: `com.autonomous-unity.mcp/Editor/AutonomousMcpToolDispatcher.cs`

- [ ] **Step 1: Add the optional field to the response type**

Locate `public sealed class AutonomousMcpToolResponse` and add:

```csharp
        // Optional: set by the dispatcher so the AI client notices queued HUD sends.
        public object hudOutbox;   // { pending = N } or null
```

- [ ] **Step 2: Stamp it in the single dispatch return path**

In `DispatchOnMainThread`, the response is wrapped by a local `R(...)` helper before return (it sets logging/category fields). Add the stamp there so it covers every tool. If no such helper exists, stamp immediately before each `return R(legacy);` / final return:

```csharp
        private static AutonomousMcpToolResponse R(AutonomousMcpToolResponse resp)
        {
            if (resp != null)
                resp.hudOutbox = new { pending = AutonomousMcp.Editor.Advisor.AdvisorStore.PendingCount() };
            return resp;
        }
```

(If `R` already exists, add only the `resp.hudOutbox = ...` line inside it.)

- [ ] **Step 3: Verify over the bridge** (after focused recompile)

```bash
node .claude/skills/run-autonomous-unity-mcp/driver.mjs call health_check '{}'
```
Expected (raw bridge JSON): includes top-level `"hudOutbox": { "pending": 0 }` alongside `data`. The driver prints the raw bridge envelope, so it appears here directly.

**Surfacing to the AI client:** the bridge envelope is `{ success, data, error, hudOutbox }`. Confirm the Node relay's `callUnity` returns the **full envelope** (not just `data`) to the MCP client — otherwise the sibling `hudOutbox` is dropped before the AI sees it. If `callUnity` returns only `data`, fold the pending count into the client-facing result there (e.g. append `hudOutbox` to the returned object in `server/src/mcpServer.ts`'s `callUnity`). Re-verify by inspecting an MCP client's view of a `health_check` result.

- [ ] **Step 4: Commit**

```bash
git add com.autonomous-unity.mcp/Editor/AutonomousMcpToolResponse.cs com.autonomous-unity.mcp/Editor/AutonomousMcpToolDispatcher.cs
git commit -m "feat(advisor): piggyback outbox pending-count on every response"
```

---

## Task 8: Register `hud_post` + `hud_poll` in the Node relay

**Files:**
- Modify: `server/src/mcpServer.ts`

- [ ] **Step 1: Add the tool registrations** (near `health_check`)

```typescript
  server.tool(
    "hud_post",
    "Post advice into the in-Unity Advisor HUD (appears in the dockable panel for the user). Use plain language a novice understands.",
    {
      text: z.string().min(1).describe("Advice text (plain/markdown)"),
      level: z.enum(["info", "success", "warning"]).optional().describe("Tint (default info)"),
    },
    async (input) => callUnity("hud_post", input)
  );

  server.tool(
    "hud_poll",
    "Drain the Advisor HUD outbox: returns everything the user sent from Unity (notes, selection, console errors) and clears the queue. Call this when a tool response shows hudOutbox.pending > 0, or when the user says they sent something.",
    {},
    async (input) => callUnity("hud_poll", input)
  );
```

- [ ] **Step 2: Build to verify TypeScript compiles**

Run: `npm --workspace server run build`
Expected: exits 0, no errors.

- [ ] **Step 3: Commit**

```bash
git add server/src/mcpServer.ts
git commit -m "feat(advisor): expose hud_post + hud_poll in the Node relay"
```

---

## Task 9: `AdvisorHudWindow` — feed + compose + attach (note/selection/console)

**Files:**
- Create: `com.autonomous-unity.mcp/Editor/UI/AdvisorHudWindow.cs`

This is IMGUI UI (not unit-tested); verify visually with `capture_screenshot` per the spec.

- [ ] **Step 1: Create the window**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using AutonomousMcp.Editor.Advisor;

namespace AutonomousMcp.Editor.UI
{
    internal sealed class AdvisorHudWindow : EditorWindow
    {
        private Vector2 _feedScroll;
        private string _compose = string.Empty;
        private bool _attachSelection, _attachConsole;

        [MenuItem("Window/Autonomous MCP/Advisor")]
        public static void Open()
        {
            var w = GetWindow<AdvisorHudWindow>(false, "MCP Advisor", true);
            w.minSize = new Vector2(320, 360);
            w.Show();
        }

        private void OnEnable() => AdvisorStore.EnsureLoaded();
        private void OnInspectorUpdate() => Repaint(); // pick up advice posted over the bridge

        private void OnGUI()
        {
            DrawHeader();
            DrawFeed();
            DrawComposer();
        }

        private void DrawHeader()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("🛰 MCP Advisor", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                var pending = AdvisorStore.PendingCount();
                if (pending > 0) GUILayout.Label($"{pending} queued →", EditorStyles.miniLabel);
            }
        }

        private void DrawFeed()
        {
            _feedScroll = EditorGUILayout.BeginScrollView(_feedScroll, GUILayout.ExpandHeight(true));
            foreach (var a in AdvisorStore.GetAdvice())
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    var icon = a.level == "warning" ? "⚠ " : a.level == "success" ? "✅ " : "";
                    EditorGUILayout.LabelField(icon + (a.text ?? a.title ?? string.Empty),
                        EditorStyles.wordWrappedLabel);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawComposer()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    var selCount = Selection.gameObjects?.Length ?? 0;
                    _attachSelection = GUILayout.Toggle(_attachSelection,
                        $"◳ Selection ({selCount})", EditorStyles.miniButton);
                    _attachConsole = GUILayout.Toggle(_attachConsole,
                        "⚠ Console errors", EditorStyles.miniButton);
                }
                _compose = EditorGUILayout.TextField("Note", _compose);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_compose) && !_attachSelection && !_attachConsole))
                        if (GUILayout.Button("Send", GUILayout.Width(90))) Send();
                }
            }
        }

        private void Send()
        {
            if (!string.IsNullOrWhiteSpace(_compose))
                AdvisorStore.Enqueue("note", new JObject { ["text"] = _compose }.ToString());

            if (_attachSelection)
            {
                var objs = (Selection.gameObjects ?? Array.Empty<GameObject>()).Select(g => new JObject
                {
                    ["name"] = g.name,
                    ["path"] = GetPath(g.transform),
                    ["components"] = new JArray(g.GetComponents<Component>()
                        .Where(c => c != null).Select(c => c.GetType().Name))
                });
                AdvisorStore.Enqueue("selection", new JObject { ["objects"] = new JArray(objs) }.ToString());
            }

            if (_attachConsole)
            {
                // Reuse the existing console capture via the dispatcher handler.
                var resp = AutonomousMcpToolDispatcher.HandleReadConsole(new JObject { ["level"] = "error", ["limit"] = 50 });
                AdvisorStore.Enqueue("console", resp?.data?.ToString() ?? "{}");
            }

            _compose = string.Empty;
            _attachSelection = _attachConsole = false;
            GUI.FocusControl(null);
        }

        private static string GetPath(Transform t)
        {
            var p = t.name;
            while (t.parent != null) { t = t.parent; p = t.name + "/" + p; }
            return p;
        }
    }
}
```

- [ ] **Step 2: Compile + open the window** (focused recompile, then over the bridge)

```bash
node .claude/skills/run-autonomous-unity-mcp/driver.mjs call execute_menu_item '{"menu_path":"Window/Autonomous MCP/Advisor"}'
```
If `execute_menu_item` returns `executed:false` (custom-menu quirk), ask the user to open **Window → Autonomous MCP → Advisor** manually. Confirm `read_console {level:"error"}` shows 0 CS errors.

- [ ] **Step 3: Visual confirmation (mandatory)**

```bash
# post advice, then screenshot the window
node .claude/skills/run-autonomous-unity-mcp/driver.mjs call hud_post '{"text":"Looks good — next, set up your toggle menu.","level":"success"}'
node .claude/skills/run-autonomous-unity-mcp/driver.mjs call capture_screenshot '{"source":"window","window":"MCP Advisor","save_path":"C:/Users/Trick/CascadeProjects/windsurf-project/_advisor.png"}'
```
Then open `_advisor.png` and confirm the advice bubble renders and the composer (Selection/Console toggles, Note field, Send) is visible. Delete the temp PNG after.

- [ ] **Step 4: Commit**

```bash
git add com.autonomous-unity.mcp/Editor/UI/AdvisorHudWindow.cs
git commit -m "feat(advisor): dockable HUD window (feed + compose + note/selection/console attach)"
```

---

## Task 10: End-to-end round-trip verification

**Files:** none (verification only)

- [ ] **Step 1: AI → HUD**

```bash
node .claude/skills/run-autonomous-unity-mcp/driver.mjs call hud_post '{"text":"Your avatar has no visemes set up.","level":"warning"}'
```
Screenshot the window (Task 9 Step 3 command) → confirm the warning bubble appears.

- [ ] **Step 2: HUD → AI (manual send + piggyback + poll)**

In Unity: select a GameObject, toggle **◳ Selection**, type a note, click **Send**. Then:

```bash
node .claude/skills/run-autonomous-unity-mcp/driver.mjs call health_check '{}'   # expect hudOutbox.pending >= 1
node .claude/skills/run-autonomous-unity-mcp/driver.mjs call hud_poll '{}'        # expect the note + selection items, queue cleared
node .claude/skills/run-autonomous-unity-mcp/driver.mjs call health_check '{}'   # expect hudOutbox.pending == 0
```
Expected: the first `health_check` shows `pending >= 1`; `hud_poll` returns the `note` + `selection` items; the second `health_check` shows `pending == 0`.

- [ ] **Step 3: Reload survival**

Post advice + send a note, then force a domain reload (touch any package script + focus Unity to recompile). After it returns, screenshot the window → advice still present; `hud_poll` still returns the queued note. Confirms SessionState persistence end-to-end.

- [ ] **Step 4: Final commit (docs)**

Update `CLAUDE.md` Bridge section with the three new tools (`hud_post`, `hud_poll`, and the `hudOutbox.pending` piggyback) in one sentence, then:

```bash
git add CLAUDE.md
git commit -m "docs: note hud_post/hud_poll + outbox piggyback in the bridge"
```

---

## Done-when (Phase 1 acceptance)

- `AdvisorStore` EditMode tests green (advice cap, outbox drain/cap, SessionState round-trip).
- `hud_post` puts advice in the panel (screenshot-confirmed).
- A UI **Send** (note + selection + console) enqueues outbox items; `hudOutbox.pending` rises on the next response; `hud_poll` drains them; pending returns to 0.
- State survives a domain reload.
- Node server builds clean and exposes both tools.
- Out of scope for Phase 1 (tracked for Phase 2/3): action cards + Approve protocol, screenshot attach, quick-ask buttons, the Scene-view overlay badge.
