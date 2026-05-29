# Finding: rapid sequential image generation hangs (keyless throttle)

## Symptom
`manage_generator generate` works for a single asset (~1–3s, Texture/Sprite/Material/Cubemap),
but a **2nd generation fired soon after the first hangs until timeout**; `health_check` is
instant between them (so the editor main-thread pump is fine — the hang is in the request itself).

## Root cause — CONFIRMED, and it's external
Three back-to-back requests to the keyless Pollinations endpoint from **plain Node, no Unity**:
```
grey stone tile → 200, 22154 bytes, 0.9s
rusted metal    → TimeoutError after 60.0s   (held open, never answered)
blue sky        → 402 Payment Required, 0.4s
```
So it is **100% Pollinations-side per-IP throttling** of the free keyless tier — not our C#, not
the main-thread Invoke, not Mono `HttpWebRequest`. It serves the first request, *holds* the second
until timeout, and returns **HTTP 402** on subsequent ones. (Matches pre-fix evidence: the held 2nd
gen *eventually* completed and wrote assets — queued, not dead.)

## Why three attempted fixes failed (all reverted to known-good)
- **Dispatch main-thread Invoke 10s→150s** — wrong layer; only changed *when* the client gives up.
  Strictly worsened the symptom (150s freeze vs 10s fail). Reverted.
- **Total wall-clock budget guard** in `FreeTierImageClient` — a single held request blocks the full
  per-request timeout *before* the between-attempts budget check runs, so it never fires. Reverted.
- **`req.KeepAlive = false`** — not a connection-reuse problem (the node test reproduced it with a
  fresh client each time). Reverted.

The advisor's reframe holds: "move the network off the main thread" only stops the editor
*freezing*; it does **not** make a throttled request return data. Threading is a red herring for
*this* symptom.

## Recommended fix (deliberate; not yet applied — FreeTier subsystem is parallel-owned)
1. **Per-provider request timeout.** Keyless (Pollinations) short (~15–20s) so it fails fast; keyed
   (HF FLUX) longer (~45–60s — it is legitimately slow). Today both share one timeout.
2. **Classify keyless timeout + HTTP 402 as "provider rate-limited/unavailable"** → return a fast,
   clear, actionable error: *"keyless image provider is rate-limited (Pollinations 402/timeout); set
   `GENERATOR_HF_TOKEN` for reliable generation, or retry shortly."* (402 currently falls through to
   a generic "fatal"; the hold isn't surfaced at all.)
3. **Tune the dispatch main-thread Invoke timeout** to exceed the slowest *legitimate keyed* gen
   (HF FLUX ~20–40s) — ~60s. The default 10s would drop legit HF gens; 150s over-freezes. Left at the
   known-good default for now; set this when the per-provider timeouts land.
4. Optional: a min-interval/backoff between keyless requests.

## Reliable path for the user (account-based, legitimate)
- **BYOK HuggingFace:** sign into your HF account in the browser → mint a token → set
  `GENERATOR_HF_TOKEN`. This uses your account's free inference quota (an account credit pool), with
  the `ProviderKeyPool` rotation + proper 429 backoff already built. Reliable for repeated/volume
  generation; sidesteps the shared-keyless 402/throttle.
- **Local Stable Diffusion** (AUTOMATIC1111 / ComfyUI on your GPU): $0, unlimited, no account — best
  for volume. Add it to the provider catalog as a `127.0.0.1` endpoint.
- The consumer subscription credit pool (ChatGPT Plus / Claude Pro) is **not** API-accessible and is
  out of scope (web-app only; driving it = session scraping). Anthropic has no image generation.

## Status — FIX APPLIED (2026-05-29)
Per-provider request timeouts (keyless 20s / keyed 60s), 402 + keyless-socket-timeout classified as
rate-limited, keyless fast-bail with an actionable "set GENERATOR_HF_TOKEN" error, and a 75s
main-thread dispatch budget for `manage_generator` (others stay at 10s) are all implemented and
unit-tested (`McpThrottleTests`, 13 green). Commit `e2f254c`.

Verified live: single-shot keyless still works (`provider=pollinations`, ~1s); a rapid 2nd keyless gen
now fails in ~20s (the held request gives up at the keyless timeout) and subsequent ones in <1s (402),
all returning the actionable HF-token message instead of hanging. Off-main-thread generation and
model3d's 300s Meshy poll remain separate follow-ups.
