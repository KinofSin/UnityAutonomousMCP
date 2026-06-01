# Advisor HUD — Phase 2 (Scaffolding) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:executing-plans. Steps use checkbox (`- [ ]`).

**Goal:** Add the novice-empowering scaffolding to the HUD: **action cards** (AI posts a plain-language card with Approve/Why/Dismiss; clicking relays consent to the AI), **quick-ask buttons** (one-click "What's next? / What's wrong? / Upload-ready?"), and a **screenshot attach** chip. All additive — Phase 1 paths are untouched.

**Architecture:** Reuse the Phase 1 `AdvisorStore` + outbox + piggyback. New: `AdvisorStore.AddCard`, a `hud_post_card` bridge tool, card rendering + action/quick-ask/screenshot enqueues in `AdvisorHudWindow`. The HUD still never executes fixes — Approve enqueues a `card_action` the AI acts on with its own (permission-gated) tools.

**Tech Stack:** As Phase 1. Screenshot reuses `AutonomousMcpToolDispatcher.HandleCaptureScreenshot`.

**Spec:** `docs/superpowers/specs/2026-05-31-advisor-hud-design.md` · **Phase 1 plan:** `docs/superpowers/plans/2026-05-31-advisor-hud-phase1.md`

**Regression guard:** after each compile, run the FULL EditMode suite (`driver.mjs tests editmode`) and confirm the 5 Phase-1 `AdvisorStore` tests + the new ones are green and the overall failed-count stays at the pre-existing 17 (foreign YUCP/VPM). Verify a `hud_post` text advice still renders (Phase 1 unbroken).

---

## Task 1: `AdvisorStore.AddCard` (+ test)

**Files:** Modify `Editor/Advisor/AdvisorStore.cs`; Test `Editor/Tests/AdvisorStoreTests.cs`

- [ ] **Step 1: Failing test (append)**

```csharp
        [Test]
        public void AddCard_stores_card_with_actions()
        {
            AdvisorStore.AddCard("c1", "No visemes", "explanation",
                new System.Collections.Generic.List<CardAction> { new CardAction { id = "approve", label = "Approve fix" } });
            var all = AdvisorStore.GetAdvice();
            Assert.AreEqual(1, all.Count);
            Assert.AreEqual("card", all[0].kind);
            Assert.AreEqual("c1", all[0].id);
            Assert.AreEqual("No visemes", all[0].title);
            Assert.AreEqual(1, all[0].actions.Count);
            Assert.AreEqual("approve", all[0].actions[0].id);
        }
```

- [ ] **Step 2: Implement `AddCard`**

```csharp
        public static void AddCard(string id, string title, string body, List<CardAction> actions)
        {
            EnsureLoaded();
            _advice.Add(new AdviceItem
            {
                id = string.IsNullOrEmpty(id) ? Guid.NewGuid().ToString("N").Substring(0, 8) : id,
                kind = "card",
                level = "info",
                title = title ?? string.Empty,
                body = body ?? string.Empty,
                actions = actions ?? new List<CardAction>(),
                postedAtUtc = DateTime.UtcNow.ToString("o")
            });
            while (_advice.Count > MaxAdvice) _advice.RemoveAt(0);
            PersistAdvice();
        }
```

- [ ] **Step 3: Compile (user focus) + run full suite** — `driver.mjs tests editmode`; expect 6 AdvisorStore tests green, overall failed == 17.

- [ ] **Step 4: Commit** — `git commit -m "feat(advisor): AdvisorStore.AddCard + test"`

---

## Task 2: `hud_post_card` bridge tool + Node registration

**Files:** Modify `Editor/AutonomousMcpToolDispatcher.cs`, `server/src/mcpServer.ts`

- [ ] **Step 1: Handler** (next to `HandleHudPost`)

```csharp
        internal static AutonomousMcpToolResponse HandleHudPostCard(JObject args)
        {
            var title = args.Value<string>("title");
            if (string.IsNullOrWhiteSpace(title))
                return Error("hud_post_card requires non-empty 'title'.");
            var id = args.Value<string>("id");
            var body = args.Value<string>("body") ?? string.Empty;

            var actions = new System.Collections.Generic.List<AutonomousMcp.Editor.Advisor.CardAction>();
            if (args["actions"] is JArray arr)
                foreach (var a in arr)
                    actions.Add(new AutonomousMcp.Editor.Advisor.CardAction
                    { id = a.Value<string>("id"), label = a.Value<string>("label") });
            if (actions.Count == 0)
            {
                actions.Add(new AutonomousMcp.Editor.Advisor.CardAction { id = "approve", label = "Approve" });
                actions.Add(new AutonomousMcp.Editor.Advisor.CardAction { id = "why", label = "Why?" });
                actions.Add(new AutonomousMcp.Editor.Advisor.CardAction { id = "dismiss", label = "Dismiss" });
            }
            AutonomousMcp.Editor.Advisor.AdvisorStore.AddCard(id, title, body, actions);
            return Success(JToken.FromObject(new { posted = true, actionCount = actions.Count }));
        }
```

- [ ] **Step 2: Switch + names** — add `case "hud_post_card": legacy = HandleHudPostCard(args); break;`; add `"hud_post_card"` to `LegacyToolNames` and `supportedTools`.

- [ ] **Step 3: Node registration** (after `hud_post`)

```typescript
  server.tool(
    "hud_post_card",
    "Post an action card to the Advisor HUD: a plain-language explanation plus buttons the user can click. Approve relays consent — YOU then perform the fix with your tools. Omit actions to get default Approve/Why/Dismiss.",
    {
      title: z.string().min(1).describe("Short headline, e.g. 'Your avatar has no visemes set up'"),
      body: z.string().optional().describe("Plain-language explanation a novice understands"),
      id: z.string().optional().describe("Stable card id (echoed back in the card_action you receive on Approve)"),
      actions: z.array(z.object({ id: z.string(), label: z.string() })).optional()
        .describe("Buttons; default Approve/Why/Dismiss"),
    },
    async (input) => callUnity("hud_post_card", input)
  );
```

- [ ] **Step 4: Build Node** — `npm --workspace server run build` (exit 0).

- [ ] **Step 5: Compile (user focus) + verify** — `driver.mjs call hud_post_card '{"title":"No visemes","body":"I can wire them from your face mesh."}'` → `{posted:true, actionCount:3}`; `read_console{level:error}` 0 CS errors.

- [ ] **Step 6: Commit** — `git commit -m "feat(advisor): hud_post_card bridge tool + Node registration"`

---

## Task 3: Render cards + Approve/Why/Dismiss in the window

**Files:** Modify `Editor/UI/AdvisorHudWindow.cs`

- [ ] **Step 1: Replace `DrawFeed`'s per-item block to branch on kind**

```csharp
        private void DrawFeed()
        {
            _feedScroll = EditorGUILayout.BeginScrollView(_feedScroll, GUILayout.ExpandHeight(true));
            foreach (var a in AdvisorStore.GetAdvice())
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    if (a.kind == "card")
                    {
                        EditorGUILayout.LabelField(a.title ?? string.Empty, EditorStyles.boldLabel);
                        if (!string.IsNullOrEmpty(a.body))
                            EditorGUILayout.LabelField(a.body, EditorStyles.wordWrappedLabel);
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            foreach (var act in a.actions ?? new System.Collections.Generic.List<CardAction>())
                                if (GUILayout.Button(act.label ?? act.id, EditorStyles.miniButton))
                                    EnqueueCardAction(a.id, act.id);
                        }
                    }
                    else
                    {
                        var icon = a.level == "warning" ? "⚠ " : a.level == "success" ? "[ok] " : "";
                        EditorGUILayout.LabelField(icon + (a.text ?? string.Empty), EditorStyles.wordWrappedLabel);
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private void EnqueueCardAction(string cardId, string actionId)
        {
            AdvisorStore.Enqueue("card_action",
                new JObject { ["cardId"] = cardId, ["actionId"] = actionId }.ToString());
        }
```

> Note: this also swaps the non-rendering `✅` emoji for a `[ok]` text marker (the `⚠` glyph renders fine; `✅`/`🛰` do not in Unity's default font).

- [ ] **Step 2: Swap the header glyph** — change `"\U0001F6F0 MCP Advisor"` to `"MCP Advisor"` in `DrawHeader` (drop the non-rendering rocket).

- [ ] **Step 3: Compile (user focus) + visual verify** — post a card via `hud_post_card`, screenshot `source:"window", window:"MCP Advisor"`, read the PNG → card shows title + body + three buttons. Click **Approve** in Unity → `hud_poll` returns a `card_action` item `{cardId, actionId:"approve"}`.

- [ ] **Step 4: Commit** — `git commit -m "feat(advisor): render action cards with Approve/Why/Dismiss"`

---

## Task 4: Quick-ask buttons

**Files:** Modify `Editor/UI/AdvisorHudWindow.cs`

- [ ] **Step 1: Add a quick-ask row** at the top of `OnGUI` (after `DrawHeader();`)

```csharp
        private void DrawQuickAsk()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("What's next?", EditorStyles.miniButton)) QuickAsk("whats_next");
                if (GUILayout.Button("What's wrong?", EditorStyles.miniButton)) QuickAsk("whats_wrong");
                if (GUILayout.Button("Upload-ready?", EditorStyles.miniButton)) QuickAsk("upload_ready");
            }
        }

        private void QuickAsk(string key)
        {
            AdvisorStore.Enqueue("quick_ask", new JObject { ["key"] = key }.ToString());
        }
```

Call `DrawQuickAsk();` between `DrawHeader();` and `DrawFeed();` in `OnGUI`.

- [ ] **Step 2: Compile (user focus) + verify** — click **What's next?** in Unity → `hud_poll` returns `{type:"quick_ask", payload:{key:"whats_next"}}`.

- [ ] **Step 3: Commit** — `git commit -m "feat(advisor): quick-ask buttons (whats_next/whats_wrong/upload_ready)"`

---

## Task 5: Screenshot attach chip

**Files:** Modify `Editor/UI/AdvisorHudWindow.cs`

- [ ] **Step 1: Add the toggle** — add a field `private bool _attachScreenshot;` and a toggle in `DrawComposer`'s chip row:

```csharp
                    _attachScreenshot = GUILayout.Toggle(_attachScreenshot, "📷 Screenshot", EditorStyles.miniButton);
```

(Include `_attachScreenshot` in the Send-button `DisabledScope` condition: `string.IsNullOrWhiteSpace(_compose) && !_attachSelection && !_attachConsole && !_attachScreenshot`.)

- [ ] **Step 2: Capture + enqueue in `Send()`** (before resetting the toggles)

```csharp
            if (_attachScreenshot)
            {
                var path = "Temp/advisor_shot_" + DateTime.UtcNow.Ticks + ".png";
                var resp = AutonomousMcpToolDispatcher.HandleCaptureScreenshot(
                    new JObject { ["source"] = "editor", ["save_path"] = path });
                if (resp != null && resp.success)
                    AdvisorStore.Enqueue("screenshot",
                        new JObject { ["source"] = "editor", ["path"] = path }.ToString());
            }
```

Add `_attachScreenshot = false;` to the reset block.

- [ ] **Step 3: Compile (user focus) + verify** — toggle **📷 Screenshot**, Send → `hud_poll` returns `{type:"screenshot", payload:{source:"editor", path:"Temp/advisor_shot_*.png"}}` and the PNG exists at that path under the Leaf project.

- [ ] **Step 4: Commit** — `git commit -m "feat(advisor): screenshot attach chip (whole-editor capture)"`

---

## Task 6: E2E + regression sweep

- [ ] **Step 1:** Full suite green (`driver.mjs tests editmode`) — 6 AdvisorStore tests pass, overall failed == 17.
- [ ] **Step 2:** Phase 1 unbroken — `hud_post` text advice still renders (screenshot); UI note Send still round-trips via `hud_poll`.
- [ ] **Step 3:** Phase 2 paths — card renders + Approve → `card_action`; quick-ask → `quick_ask`; screenshot chip → `screenshot` item + file exists.
- [ ] **Step 4:** Update `CLAUDE.md` bridge note with `hud_post_card`; commit.

---

## Done-when (Phase 2 acceptance)

- `hud_post_card` renders an action card; Approve/Why/Dismiss enqueue a `card_action` the AI drains.
- Quick-ask buttons enqueue `quick_ask` items.
- Screenshot chip captures the editor and enqueues a `screenshot` item (file on disk).
- 6 `AdvisorStore` tests green; Phase 1 behavior unchanged; overall failed-count unchanged (17).
- Deferred to Phase 3: the Scene-view overlay badge.
