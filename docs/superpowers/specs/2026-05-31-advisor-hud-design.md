# Advisor HUD — design

## Context & north star

Autonomous Unity MCP exists to make a **brand-new VRChat developer** (avatar *or*
world) operate like an advanced pro **with as little prior knowledge as possible** —
the system has to understand a great deal *for* them. The Advisor HUD is the in-Unity
surface that delivers on this: it's where the AI client's expertise reaches a novice in
plain language, and where the novice points at things ("what's wrong with *this*?")
without knowing the terminology.

The HUD is a **two-way panel**:
- **AI → you:** plain-language advice + actionable "fix this?" cards appear inside Unity.
- **You → AI:** send a note, the current selection, a screenshot, or console errors —
  and approve suggested fixes — without copy-pasting or switching to the chat window.

The *intelligence* stays in the AI client (driving the existing 62 MCP tools, the
23-skill catalog, and the validation/audit tools). The HUD is a **thin relay + display**.
Crucially, **the HUD never executes Unity changes itself** — approving a card relays
consent, and the AI performs the fix with its own tools under the user's existing
Ask/Agent permissions. That is what keeps "rich action cards" safe.

## Constraint that shapes everything: MCP is pull-based

The AI client calls tools on the Unity bridge; the bridge cannot push into the chat.
So **AI → HUD** is a simple tool call, but **HUD → AI** requires the AI to *fetch* what
the user sends. Chosen mechanism: **auto-piggyback + poll** (most seamless that is still
robust across Claude Code / Cursor / Claude Desktop):
- User actions enqueue structured items in an **outbox**.
- Every bridge tool response is wrapped with `hudOutbox: { pending: N }`, so the AI
  notices pending items as it works.
- `hud_poll` drains and returns the items (idempotent). The AI calls it when
  `pending > 0` or when the user nudges it.

## Architecture

Three parts, each independently testable:

- **`AdvisorStore`** (C#, Editor) — the single source of truth. Holds the **advice feed**
  (AI → user) and the **outbox queue** (user → AI), both as bounded ring buffers
  (advice ~100, outbox ~50). Persisted to `SessionState` so it survives the domain
  reloads recompiles trigger (same pattern as `AutonomousMcpTestJobs`). No UI, no network
  — pure state + serialization. Fully unit-testable without a bridge or keys.
- **Unity HUD** (C#, Editor):
  - `AdvisorHudWindow` — dockable `EditorWindow`: header (title + live mode badge +
    connected client), quick-ask buttons, advice feed (text bubbles + action cards),
    compose bar (text input + attach chips + Send).
  - `AdvisorHudOverlay` — a Scene-view overlay badge ("Advisor · N") showing unread
    count; flashes on new advice; click opens the window. (Hybrid placement.)
- **MCP tools** (Node relay `server/src/mcpServer.ts` → Unity bridge handlers) — the
  protocol below.

## The two-way protocol

**AI → HUD (advice):**
- `hud_post { text, level? }` — append a plain/markdown advice bubble (`level` =
  info|success|warning for tint).
- `hud_post_card { id, title, body, actions: [{ id, label }] }` — append an action card
  with the given buttons (default actions: a primary "Approve" + "Why?" + "Dismiss").

**HUD → AI (outbox):**
- `hud_poll {}` — drain + return all queued outbox items (idempotent; empty = no-op).
- Piggyback: `AutonomousMcpToolDispatcher.Dispatch` wraps **every** response with
  `hudOutbox: { pending: N }`.

**Outbox item shapes** (enqueued by user actions):
```
{ type:"note",        text }
{ type:"selection",   objects:[{ name, path, components }] }
{ type:"screenshot",  source, path }    // saved to a project temp path; AI reads the file (no base64 in the poll payload)
{ type:"console",     entries:[{ level, message }] }
{ type:"card_action", cardId, actionId }     // e.g. actionId "approve" | "dismiss"
{ type:"quick_ask",   key }                  // "whats_next" | "whats_wrong" | "upload_ready"
```

## Reuse (not rebuild)

The attach chips and the badge are thin wrappers over verified, existing capabilities:
- **Screenshot** → `capture_screenshot` (window / scene / editor).
- **Console errors** → `read_console`.
- **Selection summary** → `manage_selection` / existing hierarchy helpers.
- **Mode badge + connected client** → `PermissionStore` + transport's client registry.

## Quick-ask buttons (novice scaffolding)

Three one-click buttons that need no terminology: **What's next?**, **What's wrong?**,
**Upload-ready?**. Each enqueues a `quick_ask` outbox item; the AI answers by posting
advice/cards. This is the core "novice → pro" affordance.

## Error handling & edge cases

- Feed and outbox are bounded ring buffers (oldest dropped); a dropped-count note is kept.
- `hud_poll` is idempotent; concurrent posts are serialized on the editor main thread.
- Screenshots are size-capped (reuse capture limits); large console dumps are truncated.
- No client connected → sends simply queue until one polls.
- State survives domain reloads via `SessionState`; the window rebinds to the store on reload.
- Card actions reference a `cardId`; approving an already-resolved/expired card is a no-op
  with a feed note.

## Testing

- **Key-free EditMode tests** (`AdvisorStore`): enqueue/drain ordering, advice add +
  ring-buffer cap + dropped-count, card-action serialization, `SessionState` round-trip
  (reload survival), `hudOutbox.pending` count correctness.
- **Bridge round-trip:** `hud_post` → assert advice lands in the store; enqueue a note →
  `hud_poll` drains exactly it; confirm `hudOutbox.pending` piggybacks on an unrelated
  tool response (e.g. `health_check`).
- **Visual confirmation (mandatory):** `capture_screenshot { source:"window",
  window:"MCP Advisor" }` → read the PNG to confirm the panel renders correctly across
  states (empty, with advice, with a card, with queued attachments).

## Phasing (the implementation plans will follow this)

- **Phase 1 — backbone (first plan):** `AdvisorStore` + `AdvisorHudWindow`
  (feed + compose + text advice) + `hud_post` + `hud_poll` + the piggyback wrapper +
  note/selection/console attach. Usable end-to-end; AI can advise (text) and the user can
  send context.
- **Phase 2 — scaffolding:** action cards + Approve/Dismiss protocol + screenshot attach +
  quick-ask buttons.
- **Phase 3 — glanceability:** the Scene-view overlay badge with unread count.

## Out of scope (for now)

Persisting advice across editor sessions (SessionState is per-session by design); a full
chat transcript/threading model; the HUD executing fixes directly (it only relays consent);
multi-client fan-out (one connected client at a time is assumed). The deep domain
intelligence ("understand it all for them") is delivered by the AI + existing tools/skills,
not built into the HUD.

## Assembly placement

New tool handlers live with the dispatcher (Core); the HUD window/overlay and `AdvisorStore`
are Editor UI/state — place under `Editor/` (Core assembly, alongside the Settings window).
No new assembly needed.
