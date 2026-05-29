# Keyless Generation Throttle Fix — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make image generation fail *fast and legibly* when the keyless provider is per-IP throttled, and stop the bridge from killing legitimately-slow keyed (HuggingFace) generations.

**Architecture:** Three coordinated changes in `FreeTierImageClient` (per-provider request timeouts; classify HTTP 402 + keyless socket-timeout as rate-limited; one keyless attempt then a clear actionable error instead of 4 silent retries) plus a per-tool dispatch timeout in `AutonomousMcpToolDispatcher` so the main-thread `Invoke` budget exceeds the slowest *legitimate* keyed request. The request timeout always stays **below** the dispatch timeout, so the request — not the dispatcher — bounds how long the editor can freeze. The same 402-misclassification is mirrored into the shared `FreeTierHttp` (audio/model3d) for parity.

**Tech Stack:** Unity 2022.3 Editor C# (`AutonomousMcp.Editor` assembly), NUnit EditMode tests (`AutonomousMcp.Editor.Tests`), `System.Net.HttpWebRequest`. Verified live over the MCP bridge via `.claude/skills/run-autonomous-unity-mcp/driver.mjs`.

---

## Background (read first)

The root cause is **external** and confirmed (see `docs/superpowers/findings/2026-05-29-keyless-generation-throttle.md`): the keyless Pollinations endpoint serves the first request, **holds the second open until timeout, then returns HTTP 402** on subsequent ones. We cannot make a throttled request return data — we can only **fail fast with a useful message** and steer the user to the reliable BYOK HuggingFace path.

**Current behavior that this plan fixes (all verified by reading the code):**

1. `FreeTierImageClient.HttpAttempt` uses one `RequestTimeoutMs = 90_000` for **both** keyed and keyless requests (`com.autonomous-unity.mcp/Editor/Core/FreeTierImageClient.cs:51,287-288`).
2. `ClassifyWebException` maps HTTP **402 → `Fatal`** (the catch-all `return` at `FreeTierImageClient.cs:345`), so the throttle's defining status code is never recognized.
3. A keyless socket **timeout** (no HTTP response) → `Transient` (`FreeTierImageClient.cs:348`), and `TryProvider` retries `Transient` up to `MaxAttemptsPerProvider = 4` times with `Thread.Sleep` (`FreeTierImageClient.cs:180-184`) → a multi-minute hang on a provider that will never answer.
4. `AutonomousMcpToolDispatcher.Dispatch` wraps **every** tool in `AutonomousMcpMainThread.Invoke(..., timeoutMs = 10000)` (`AutonomousMcpToolDispatcher.cs:38`, default at `AutonomousMcpMainThread.cs:20`). A legitimate keyed HuggingFace FLUX generation takes ~20–40s, so it is **killed at 10s** and can never succeed over the bridge.

**Explicitly OUT OF SCOPE** (do not attempt here — they are separate follow-ups):
- Moving generation off the main thread (the finding proves threading is a red herring for *this* symptom; it only stops the editor *freezing*, not the throttle).
- `FreeTierModel3DClient`'s synchronous Meshy polling (up to `GENERATOR_MODEL_3D_TIMEOUT_SEC`, default **300s**) — it exceeds any sane dispatch timeout and needs the off-thread redesign. This plan does not make model3d reliable; note it and move on.

**Test-suite note for every verification step:** `run_tests {mode:"editmode"}` runs **all** EditMode tests in the Unity project, including a foreign package (`YUCP`/VPM installer, package-signing, `GuardianTransaction`) that has **17 pre-existing failures unrelated to this repo**. There is also one pre-existing failure in our own suite, `Generate_animation_spin_writes_anim_clip_with_curves` (an Animation-preset bug tracked separately). Treat the baseline as **18 failing**. Success for a task = *your new tests pass* and the failing count does **not rise above 18** (i.e. you added no new failures).

---

## File Structure

**Created:**
- `com.autonomous-unity.mcp/Editor/AssemblyInfo.cs` — single `[assembly: InternalsVisibleTo("AutonomousMcp.Editor.Tests")]` so EditMode tests can call the `internal` classification/timeout helpers and see the `internal AttemptOutcome` enum. One responsibility: test visibility.
- `com.autonomous-unity.mcp/Editor/Tests/McpThrottleTests.cs` — pure NUnit unit tests for the new helpers (`ClassifyHttpStatus`, `RequestTimeoutMsFor`, `ComposeFailureMessage`, `DispatchTimeoutMsFor`, and `FreeTierHttp` status classification). No network, no bridge — fast and deterministic.

**Modified:**
- `com.autonomous-unity.mcp/Editor/Core/FreeTierImageClient.cs` — extract `ClassifyHttpStatus`; add `RequestTimeoutMsFor`; thread per-provider timeout into `HttpAttempt`; keyless fast-bail + keyless-timeout→RateLimited; `ComposeFailureMessage` for the actionable error.
- `com.autonomous-unity.mcp/Editor/AutonomousMcpToolDispatcher.cs` — `DispatchTimeoutMsFor(toolName)` and use it in `Dispatch`.
- `com.autonomous-unity.mcp/Editor/Core/FreeTierHttp.cs` — extract `ClassifyHttpStatus` and map 402 → `RateLimited` for parity (public enum, no IVT needed).
- `docs/superpowers/findings/2026-05-29-keyless-generation-throttle.md` — flip Status to "applied".
- `CLAUDE.md` — one line documenting the per-tool dispatch timeout + per-provider request timeouts.

---

## Task 1: Enable internal visibility for tests + test scaffold

**Files:**
- Create: `com.autonomous-unity.mcp/Editor/AssemblyInfo.cs`
- Create: `com.autonomous-unity.mcp/Editor/Tests/McpThrottleTests.cs`

- [ ] **Step 1: Confirm the two assembly names**

Run:
```bash
cd UnityAutonomousMCP && grep -m1 '"name"' com.autonomous-unity.mcp/Editor/AutonomousMcp.Editor.asmdef && grep -m1 '"name"' com.autonomous-unity.mcp/Editor/Tests/AutonomousMcp.Editor.Tests.asmdef
```
Expected: `"name": "AutonomousMcp.Editor"` (main) and `"name": "AutonomousMcp.Editor.Tests"` (tests). The `InternalsVisibleTo` target below is the **test** assembly name.

- [ ] **Step 2: Create the AssemblyInfo with InternalsVisibleTo**

Create `com.autonomous-unity.mcp/Editor/AssemblyInfo.cs`:
```csharp
using System.Runtime.CompilerServices;

// Lets the EditMode self-test assembly call internal classification/timeout helpers in
// FreeTierImageClient and AutonomousMcpToolDispatcher (and see the internal AttemptOutcome enum)
// without widening the package's public API.
[assembly: InternalsVisibleTo("AutonomousMcp.Editor.Tests")]
```

- [ ] **Step 3: Create a minimal failing test that proves visibility**

Create `com.autonomous-unity.mcp/Editor/Tests/McpThrottleTests.cs`:
```csharp
using NUnit.Framework;
using AutonomousMcp.Editor.Core;

namespace AutonomousMcp.SelfTest
{
    // Pure unit tests for the throttle-handling helpers. No network, no MCP bridge — these run
    // in-process under the EditMode runner. The whole project's EditMode suite has 18 known
    // pre-existing failures (17 from an unrelated package, 1 Animation-preset bug); these tests
    // must not add to that count.
    public sealed class McpThrottleTests
    {
        [Test]
        public void InternalsAreVisible_smoke()
        {
            // Compiles only if InternalsVisibleTo is wired and the helper exists.
            Assert.AreEqual(AttemptOutcome.RateLimited, FreeTierImageClient.ClassifyHttpStatus(429));
        }
    }
}
```

- [ ] **Step 4: Run the suite to verify it FAILS to compile (helper not yet present)**

Run:
```bash
cd UnityAutonomousMCP && node .claude/skills/run-autonomous-unity-mcp/driver.mjs call get_compilation_errors '{}'
```
First trigger a recompile so the new files are picked up:
```bash
node .claude/skills/run-autonomous-unity-mcp/driver.mjs call refresh_unity '{}' && sleep 8 && node .claude/skills/run-autonomous-unity-mcp/driver.mjs call get_compilation_errors '{}'
```
Expected: a compile error — `'FreeTierImageClient' does not contain a definition for 'ClassifyHttpStatus'`. (This is the TDD "red": the test references a helper Task 2 adds.)

- [ ] **Step 5: Commit**

```bash
git add com.autonomous-unity.mcp/Editor/AssemblyInfo.cs com.autonomous-unity.mcp/Editor/AssemblyInfo.cs.meta \
        com.autonomous-unity.mcp/Editor/Tests/McpThrottleTests.cs com.autonomous-unity.mcp/Editor/Tests/McpThrottleTests.cs.meta
git commit -m "test(generators): InternalsVisibleTo + throttle test scaffold (red)"
```
(`.meta` files are generated by Unity on import; if absent, `refresh_unity` first, then add them.)

---

## Task 2: Classify HTTP 402 as rate-limited (extract `ClassifyHttpStatus`)

**Files:**
- Modify: `com.autonomous-unity.mcp/Editor/Core/FreeTierImageClient.cs` (`ClassifyWebException`, ~`331-349`)
- Test: `com.autonomous-unity.mcp/Editor/Tests/McpThrottleTests.cs`

- [ ] **Step 1: Write the failing tests**

Add to `McpThrottleTests`:
```csharp
        [Test]
        public void ClassifyHttpStatus_402_is_RateLimited()
        {
            // Pollinations returns 402 once the per-IP keyless tier is throttled.
            Assert.AreEqual(AttemptOutcome.RateLimited, FreeTierImageClient.ClassifyHttpStatus(402));
        }

        [Test]
        public void ClassifyHttpStatus_429_is_RateLimited()
        {
            Assert.AreEqual(AttemptOutcome.RateLimited, FreeTierImageClient.ClassifyHttpStatus(429));
        }

        [Test]
        public void ClassifyHttpStatus_401_403_are_AuthFailure()
        {
            Assert.AreEqual(AttemptOutcome.AuthFailure, FreeTierImageClient.ClassifyHttpStatus(401));
            Assert.AreEqual(AttemptOutcome.AuthFailure, FreeTierImageClient.ClassifyHttpStatus(403));
        }

        [Test]
        public void ClassifyHttpStatus_5xx_and_408_are_Transient()
        {
            foreach (var s in new[] { 500, 502, 503, 504, 408 })
                Assert.AreEqual(AttemptOutcome.Transient, FreeTierImageClient.ClassifyHttpStatus(s), $"status {s}");
        }

        [Test]
        public void ClassifyHttpStatus_other_4xx_is_Fatal()
        {
            Assert.AreEqual(AttemptOutcome.Fatal, FreeTierImageClient.ClassifyHttpStatus(400));
            Assert.AreEqual(AttemptOutcome.Fatal, FreeTierImageClient.ClassifyHttpStatus(404));
        }
```

- [ ] **Step 2: Run the suite to verify the new tests FAIL**

Run:
```bash
cd UnityAutonomousMCP && node .claude/skills/run-autonomous-unity-mcp/driver.mjs tests editmode
```
Expected: compile error (helper still missing) — same red as Task 1, now with more callers.

- [ ] **Step 3: Add the pure `ClassifyHttpStatus` helper and route `ClassifyWebException` through it**

In `FreeTierImageClient.cs`, replace the body of `ClassifyWebException` (currently `331-349`) so the status→outcome mapping lives in a pure, testable method. New code:
```csharp
        // Pure status→outcome mapping, unit-tested. 402 = keyless quota/throttle (Pollinations),
        // treated as rate-limited so callers can back off / surface a clear message rather than
        // mislabeling it a fatal bad-request.
        internal static AttemptOutcome ClassifyHttpStatus(int status)
        {
            if (status == 429 || status == 402) return AttemptOutcome.RateLimited;
            if (status == 401 || status == 403) return AttemptOutcome.AuthFailure;
            if (status == 500 || status == 502 || status == 503 || status == 504 || status == 408)
                return AttemptOutcome.Transient;
            return AttemptOutcome.Fatal;
        }

        private static AttemptResult ClassifyWebException(WebException we, bool keyed)
        {
            if (we.Response is HttpWebResponse er)
            {
                var status = (int)er.StatusCode;
                TimeSpan? retryAfter = ParseRetryAfter(er.Headers?["Retry-After"]);
                string detail;
                using (var rs = er.GetResponseStream())
                    detail = Truncate(SafeText(ReadAll(rs)), 160);

                var outcome = ClassifyHttpStatus(status);
                return new AttemptResult
                {
                    Outcome = outcome,
                    RetryAfter = (outcome == AttemptOutcome.RateLimited || outcome == AttemptOutcome.Transient) ? retryAfter : null,
                    Detail = status + " " + detail
                };
            }

            // No HTTP response → socket timeout / network drop. For the keyless provider this is the
            // signature "held request" of the per-IP throttle, so treat it as RateLimited (the caller
            // bails fast instead of retrying a provider that will not answer). For a keyed provider a
            // timeout is more likely a transient blip worth a backoff+retry.
            return new AttemptResult
            {
                Outcome = keyed ? AttemptOutcome.Transient : AttemptOutcome.RateLimited,
                Detail = we.Status + ": " + we.Message
            };
        }
```

- [ ] **Step 4: Update the one caller of `ClassifyWebException` to pass `keyed`**

In `HttpAttempt` (the `catch (WebException we)` at `FreeTierImageClient.cs:321-324`), change:
```csharp
            catch (WebException we)
            {
                return ClassifyWebException(we, keyed: hf);
            }
```
(`hf` is `true` for HuggingFace = keyed, `false` for Pollinations = keyless — see `HttpAttempt`'s `bool hf` parameter and the `PollinationsAttempt`/`HuggingFaceAttempt` callers.)

- [ ] **Step 5: Run the suite to verify the `ClassifyHttpStatus` tests PASS**

Run:
```bash
cd UnityAutonomousMCP && node .claude/skills/run-autonomous-unity-mcp/driver.mjs tests editmode
```
Expected: all `ClassifyHttpStatus_*` tests pass; failing count stays ≤ 18.

- [ ] **Step 6: Commit**

```bash
git add com.autonomous-unity.mcp/Editor/Core/FreeTierImageClient.cs com.autonomous-unity.mcp/Editor/Tests/McpThrottleTests.cs
git commit -m "fix(generators): classify HTTP 402 + keyless timeout as rate-limited"
```

---

## Task 3: Per-provider request timeout

**Files:**
- Modify: `com.autonomous-unity.mcp/Editor/Core/FreeTierImageClient.cs` (`51`, `273-289`)
- Test: `com.autonomous-unity.mcp/Editor/Tests/McpThrottleTests.cs`

- [ ] **Step 1: Write the failing tests**

Add to `McpThrottleTests`:
```csharp
        [Test]
        public void RequestTimeoutMsFor_keyless_is_shorter_than_keyed()
        {
            var keyless = FreeTierImageClient.RequestTimeoutMsFor(keyed: false);
            var keyed = FreeTierImageClient.RequestTimeoutMsFor(keyed: true);
            Assert.Less(keyless, keyed, "keyless should fail fast; keyed (HF FLUX) is legitimately slow");
        }

        [Test]
        public void RequestTimeoutMsFor_values_are_in_expected_bands()
        {
            // Keyless ~20s so a held request gives up quickly; keyed ~60s for slow FLUX gens.
            Assert.AreEqual(20_000, FreeTierImageClient.RequestTimeoutMsFor(keyed: false));
            Assert.AreEqual(60_000, FreeTierImageClient.RequestTimeoutMsFor(keyed: true));
        }
```

- [ ] **Step 2: Run the suite to verify the new tests FAIL**

Run:
```bash
cd UnityAutonomousMCP && node .claude/skills/run-autonomous-unity-mcp/driver.mjs tests editmode
```
Expected: compile error — `RequestTimeoutMsFor` not defined.

- [ ] **Step 3: Add the timeout constants + helper, and use them in `HttpAttempt`**

In `FreeTierImageClient.cs`, replace the single constant at line 51:
```csharp
        private const int MaxAttemptsPerProvider = 4;
```
…keeping `MaxAttemptsPerProvider` and **removing** `private const int RequestTimeoutMs = 90_000;`. Add directly below it:
```csharp
        private const int KeyedRequestTimeoutMs = 60_000;    // HF FLUX is legitimately slow
        private const int KeylessRequestTimeoutMs = 20_000;  // keyless throttle: fail fast

        // Keyed (owned-key) providers get a longer budget; keyless (public) gets a short one so a
        // throttled/held request gives up quickly. Both stay below the dispatch timeout (Task 5).
        internal static int RequestTimeoutMsFor(bool keyed) =>
            keyed ? KeyedRequestTimeoutMs : KeylessRequestTimeoutMs;
```

In `HttpAttempt`, replace the two timeout assignments (`FreeTierImageClient.cs:287-288`):
```csharp
            req.Timeout = RequestTimeoutMs;
            req.ReadWriteTimeout = RequestTimeoutMs;
```
with:
```csharp
            var timeoutMs = RequestTimeoutMsFor(hf);
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
```

- [ ] **Step 4: Run the suite to verify the timeout tests PASS**

Run:
```bash
cd UnityAutonomousMCP && node .claude/skills/run-autonomous-unity-mcp/driver.mjs tests editmode
```
Expected: `RequestTimeoutMsFor_*` tests pass; failing count ≤ 18.

- [ ] **Step 5: Commit**

```bash
git add com.autonomous-unity.mcp/Editor/Core/FreeTierImageClient.cs com.autonomous-unity.mcp/Editor/Tests/McpThrottleTests.cs
git commit -m "fix(generators): per-provider request timeout (keyless 20s, keyed 60s)"
```

---

## Task 4: Keyless fast-bail + actionable failure message

**Files:**
- Modify: `com.autonomous-unity.mcp/Editor/Core/FreeTierImageClient.cs` (`TryProvider` `163-188`; `Generate` `127`)
- Test: `com.autonomous-unity.mcp/Editor/Tests/McpThrottleTests.cs`

- [ ] **Step 1: Write the failing tests**

Add to `McpThrottleTests`:
```csharp
        using System.Collections.Generic;  // add to the top-of-file usings if not present

        [Test]
        public void ComposeFailureMessage_keyless_throttle_steers_to_HF_token()
        {
            var attempts = new List<string>
            {
                "huggingface: skipped (no keys)",
                "pollinations: keyless-throttled (402/timeout after 20s) — 402 Payment Required"
            };
            var msg = FreeTierImageClient.ComposeFailureMessage(attempts);
            StringAssert.Contains("GENERATOR_HF_TOKEN", msg);
            StringAssert.Contains("rate-limited", msg.ToLowerInvariant());
        }

        [Test]
        public void ComposeFailureMessage_generic_when_no_throttle_marker()
        {
            var attempts = new List<string> { "huggingface: fatal (400 bad request)" };
            var msg = FreeTierImageClient.ComposeFailureMessage(attempts);
            StringAssert.StartsWith("All image providers failed", msg);
            StringAssert.DoesNotContain("GENERATOR_HF_TOKEN", msg);
        }
```
(Put the `using System.Collections.Generic;` with the other usings at the top of `McpThrottleTests.cs`, not inside the class.)

- [ ] **Step 2: Run the suite to verify the new tests FAIL**

Run:
```bash
cd UnityAutonomousMCP && node .claude/skills/run-autonomous-unity-mcp/driver.mjs tests editmode
```
Expected: compile error — `ComposeFailureMessage` not defined.

- [ ] **Step 3: Add `ComposeFailureMessage` and the keyless fast-bail**

In `FreeTierImageClient.cs`, add the helper near the other utilities (e.g. just above `NonEmptyEnv`):
```csharp
        // The keyless provider emits a trace containing this stable marker when it is throttled,
        // so the final error can be made actionable without threading extra state through.
        private const string KeylessThrottleMarker = "keyless-throttled";

        internal static string ComposeFailureMessage(List<string> attempts)
        {
            var trace = string.Join(" | ", attempts);
            foreach (var a in attempts)
                if (a != null && a.IndexOf(KeylessThrottleMarker, StringComparison.OrdinalIgnoreCase) >= 0)
                    return "Keyless image provider is rate-limited (Pollinations returns HTTP 402 / holds " +
                           "the request on rapid repeats). Set GENERATOR_HF_TOKEN for reliable generation, " +
                           "or retry in a minute. Trace: " + trace;
            return "All image providers failed. Trace: " + trace;
        }
```

In `TryProvider`, replace the `RateLimited` case (`FreeTierImageClient.cs:172-174`) so the keyless provider bails after one throttled attempt instead of burning all 4:
```csharp
                    case AttemptOutcome.RateLimited:
                        if (provider.RequiresKey)
                        {
                            provider.KeyPool.ReportRateLimited(key, ar.RetryAfter, nowUtc);
                            continue; // rotate to next key / retry
                        }
                        // Keyless: a 402/timeout is a per-IP throttle. Retrying immediately is futile
                        // and just freezes the editor longer — bail with a marked trace.
                        return $"{provider.Id}: {KeylessThrottleMarker} " +
                               $"(402/timeout after {RequestTimeoutMsFor(false) / 1000}s) — {ar.Detail}";
```

In `Generate`, replace the final error line (`FreeTierImageClient.cs:127`):
```csharp
            result.Error = ComposeFailureMessage(result.Attempts);
```

- [ ] **Step 4: Run the suite to verify the `ComposeFailureMessage` tests PASS**

Run:
```bash
cd UnityAutonomousMCP && node .claude/skills/run-autonomous-unity-mcp/driver.mjs tests editmode
```
Expected: both `ComposeFailureMessage_*` tests pass; failing count ≤ 18.

- [ ] **Step 5: Commit**

```bash
git add com.autonomous-unity.mcp/Editor/Core/FreeTierImageClient.cs com.autonomous-unity.mcp/Editor/Tests/McpThrottleTests.cs
git commit -m "fix(generators): keyless throttle bails fast with actionable HF-token guidance"
```

---

## Task 5: Per-tool dispatch timeout

**Files:**
- Modify: `com.autonomous-unity.mcp/Editor/AutonomousMcpToolDispatcher.cs` (`36-39`)
- Test: `com.autonomous-unity.mcp/Editor/Tests/McpThrottleTests.cs`

- [ ] **Step 1: Write the failing tests**

Add to `McpThrottleTests` (add `using AutonomousMcp.Editor;` to the top-of-file usings):
```csharp
        [Test]
        public void DispatchTimeoutMsFor_generator_exceeds_keyed_request_timeout()
        {
            var dispatch = AutonomousMcpToolDispatcher.DispatchTimeoutMsFor("manage_generator");
            // Must be strictly greater than the slowest legitimate keyed request so the dispatcher
            // never kills a valid HF gen before its own request timeout fires.
            Assert.Greater(dispatch, FreeTierImageClient.RequestTimeoutMsFor(keyed: true));
        }

        [Test]
        public void DispatchTimeoutMsFor_default_tool_stays_responsive()
        {
            Assert.AreEqual(10_000, AutonomousMcpToolDispatcher.DispatchTimeoutMsFor("health_check"));
            Assert.AreEqual(10_000, AutonomousMcpToolDispatcher.DispatchTimeoutMsFor(null));
        }
```

- [ ] **Step 2: Run the suite to verify the new tests FAIL**

Run:
```bash
cd UnityAutonomousMCP && node .claude/skills/run-autonomous-unity-mcp/driver.mjs tests editmode
```
Expected: compile error — `DispatchTimeoutMsFor` not defined.

- [ ] **Step 3: Add `DispatchTimeoutMsFor` and use it in `Dispatch`**

In `AutonomousMcpToolDispatcher.cs`, add a constant beside `MaxBatchDepth` (line 19):
```csharp
        private const int DefaultDispatchTimeoutMs = 10_000;
        private const int GeneratorDispatchTimeoutMs = 75_000; // > keyed request timeout (60s)
```
Replace `Dispatch` (`36-39`):
```csharp
        public static AutonomousMcpToolResponse Dispatch(AutonomousMcpEnvelope envelope)
        {
            return AutonomousMcpMainThread.Invoke(
                () => DispatchOnMainThread(envelope, 0),
                DispatchTimeoutMsFor(envelope?.tool));
        }

        // Generation tools synchronously call image/audio providers on the editor main thread; a
        // keyed HuggingFace gen can legitimately take ~40s, so give manage_generator headroom past
        // its per-provider request timeouts. Every other tool keeps the snappy default so a wedged
        // call can't freeze the editor. (Note: model3d's long Meshy poll is a separate concern and
        // is NOT made reliable by this value — see the throttle finding's out-of-scope section.)
        internal static int DispatchTimeoutMsFor(string toolName) =>
            toolName == "manage_generator" ? GeneratorDispatchTimeoutMs : DefaultDispatchTimeoutMs;
```

- [ ] **Step 4: Run the suite to verify the dispatch-timeout tests PASS**

Run:
```bash
cd UnityAutonomousMCP && node .claude/skills/run-autonomous-unity-mcp/driver.mjs tests editmode
```
Expected: both `DispatchTimeoutMsFor_*` tests pass; failing count ≤ 18.

- [ ] **Step 5: Commit**

```bash
git add com.autonomous-unity.mcp/Editor/AutonomousMcpToolDispatcher.cs com.autonomous-unity.mcp/Editor/Tests/McpThrottleTests.cs
git commit -m "fix(dispatch): give manage_generator a 75s main-thread budget; others keep 10s"
```

---

## Task 6: Parity — map 402 → rate-limited in shared `FreeTierHttp`

**Files:**
- Modify: `com.autonomous-unity.mcp/Editor/Core/FreeTierHttp.cs` (`Classify`, `107-124`)
- Test: `com.autonomous-unity.mcp/Editor/Tests/McpThrottleTests.cs`

`FreeTierHttp` backs the audio (HF) and model3d (Meshy) clients. Its `HttpAttemptOutcome` enum is **public**, so these tests need no IVT. A 402 from a keyed provider means quota exhausted → should be `RateLimited` (park/rotate), not `Fatal`.

- [ ] **Step 1: Write the failing tests**

Add to `McpThrottleTests`:
```csharp
        [Test]
        public void FreeTierHttp_ClassifyHttpStatus_402_and_429_are_RateLimited()
        {
            Assert.AreEqual(HttpAttemptOutcome.RateLimited, FreeTierHttp.ClassifyHttpStatus(402));
            Assert.AreEqual(HttpAttemptOutcome.RateLimited, FreeTierHttp.ClassifyHttpStatus(429));
        }

        [Test]
        public void FreeTierHttp_ClassifyHttpStatus_400_is_Fatal()
        {
            Assert.AreEqual(HttpAttemptOutcome.Fatal, FreeTierHttp.ClassifyHttpStatus(400));
        }
```

- [ ] **Step 2: Run the suite to verify the new tests FAIL**

Run:
```bash
cd UnityAutonomousMCP && node .claude/skills/run-autonomous-unity-mcp/driver.mjs tests editmode
```
Expected: compile error — `FreeTierHttp.ClassifyHttpStatus` not defined.

- [ ] **Step 3: Extract `ClassifyHttpStatus` in `FreeTierHttp` and route `Classify` through it**

In `FreeTierHttp.cs`, add the public pure helper and replace the status mapping inside `Classify` (`117-121`):
```csharp
        public static HttpAttemptOutcome ClassifyHttpStatus(int status)
        {
            if (status == 429 || status == 402) return HttpAttemptOutcome.RateLimited;
            if (status == 401 || status == 403) return HttpAttemptOutcome.AuthFailure;
            if (status == 500 || status == 502 || status == 503 || status == 504 || status == 408)
                return HttpAttemptOutcome.Transient;
            return HttpAttemptOutcome.Fatal;
        }
```
Then inside `Classify`, replace the block from `if (status == 429) ...` through the final `return ... Fatal ...` (`117-121`) with:
```csharp
                var outcome = ClassifyHttpStatus(status);
                return new HttpAttemptResult
                {
                    Outcome = outcome,
                    RetryAfter = (outcome == HttpAttemptOutcome.RateLimited || outcome == HttpAttemptOutcome.Transient) ? retryAfter : null,
                    Detail = status + " " + detail
                };
```
(Keep the surrounding `if (we.Response is HttpWebResponse er) { ... }` and the no-response `return Transient` fallback unchanged.)

- [ ] **Step 4: Run the suite to verify the `FreeTierHttp` tests PASS**

Run:
```bash
cd UnityAutonomousMCP && node .claude/skills/run-autonomous-unity-mcp/driver.mjs tests editmode
```
Expected: both `FreeTierHttp_ClassifyHttpStatus_*` tests pass; failing count ≤ 18.

- [ ] **Step 5: Commit**

```bash
git add com.autonomous-unity.mcp/Editor/Core/FreeTierHttp.cs com.autonomous-unity.mcp/Editor/Tests/McpThrottleTests.cs
git commit -m "fix(generators): FreeTierHttp maps 402 to rate-limited (audio/model3d parity)"
```

---

## Task 7: Live bridge verification + docs

**Files:**
- Modify: `docs/superpowers/findings/2026-05-29-keyless-generation-throttle.md`
- Modify: `CLAUDE.md`

This is **runtime observation** (verify skill), not another unit test. Keyless single-shot must still work; a rapid second keyless gen must now fail *fast* with the actionable message instead of hanging.

- [ ] **Step 1: Confirm a clean compile + green new tests**

Run:
```bash
cd UnityAutonomousMCP && node .claude/skills/run-autonomous-unity-mcp/driver.mjs call refresh_unity '{}' && sleep 8 && \
node .claude/skills/run-autonomous-unity-mcp/driver.mjs call get_compilation_errors '{}' && \
node .claude/skills/run-autonomous-unity-mcp/driver.mjs tests editmode
```
Expected: `hasErrors: false`; all `McpThrottleTests` pass; total failing ≤ 18 (no new failures).

- [ ] **Step 2: Keyless single-shot still works (with no HF token set)**

Run:
```bash
cd UnityAutonomousMCP && node .claude/skills/run-autonomous-unity-mcp/driver.mjs \
  call manage_generator '{"action":"generate","kind":"Texture","prompt":"weathered stone tile","outputAssetPath":"Assets/Generated/ThrottleTest_A"}'
```
Expected: `success: true`, `provider: pollinations`, a non-null `assetPath`. (Single-shot keyless is unaffected.)

- [ ] **Step 3: Rapid second keyless gen fails fast + legibly**

Immediately run two more back-to-back:
```bash
cd UnityAutonomousMCP && for n in B C; do \
  node .claude/skills/run-autonomous-unity-mcp/driver.mjs \
    call manage_generator "{\"action\":\"generate\",\"kind\":\"Texture\",\"prompt\":\"rusted metal $n\",\"outputAssetPath\":\"Assets/Generated/ThrottleTest_$n\"}" ; done
```
Expected (observe and record the actual output): the throttled attempt returns `success: false` within **~20s** (not a multi-minute hang, and not the old 10s opaque `Main-thread invocation timed out`), with an `error` containing `GENERATOR_HF_TOKEN` and `rate-limited`. If the provider happens not to throttle on this run, re-fire a few times to trigger it; capture whichever response carries the marker.

- [ ] **Step 4: Flip the finding's Status to applied**

In `docs/superpowers/findings/2026-05-29-keyless-generation-throttle.md`, replace the `## Status` section's last paragraph with:
```markdown
## Status — FIX APPLIED (2026-05-29)
Per-provider request timeouts (keyless 20s / keyed 60s), 402 + keyless-timeout classified as
rate-limited, keyless fast-bail with an actionable "set GENERATOR_HF_TOKEN" error, and a 75s
main-thread dispatch budget for `manage_generator` (others stay at 10s) are all implemented and
unit-tested (`McpThrottleTests`). Verified live: keyless single-shot still works; a rapid second
keyless gen now fails in ~20s with the actionable message instead of hanging. Off-main-thread
generation and model3d's 300s Meshy poll remain separate follow-ups.
```

- [ ] **Step 5: Document the timeouts in CLAUDE.md**

In `CLAUDE.md`, under the `## Gotchas` section, add one bullet:
```markdown
- **Generator dispatch budget:** `manage_generator` runs on the editor main thread and gets a **75s** `Invoke` timeout (all other tools keep **10s**), set in `AutonomousMcpToolDispatcher.DispatchTimeoutMsFor`. Per-request timeouts are keyless **20s** / keyed **60s** (`FreeTierImageClient.RequestTimeoutMsFor`); request timeout always stays below the dispatch budget so the request, not the dispatcher, bounds the editor freeze.
```

- [ ] **Step 6: Commit**

```bash
git add docs/superpowers/findings/2026-05-29-keyless-generation-throttle.md CLAUDE.md
git commit -m "docs: keyless throttle fix applied + dispatch/request timeout notes"
```

---

## Self-Review

**1. Spec coverage** (against `2026-05-29-keyless-generation-throttle.md` "Recommended fix"):
- (1) Per-provider request timeout → **Task 3** ✓
- (2) Classify keyless timeout + HTTP 402 as rate-limited → **Task 2** (402 + keyless socket-timeout) ✓; actionable error → **Task 4** ✓
- (3) Tune dispatch main-thread Invoke timeout (~60s; not 10s, not 150s) → **Task 5** (75s, > 60s keyed request) ✓
- (4) Optional min-interval/backoff between keyless requests → **intentionally dropped (YAGNI):** with keyless now bailing after one attempt, there are no rapid in-process repeats left to space out; cross-call spacing is the user's retry cadence. Noted, not built.
- Parity for the shared audio/model3d HTTP path → **Task 6** ✓.

**2. Placeholder scan:** No TBD/TODO; every code step has complete code; every run step has an exact command + expected result. ✓

**3. Type consistency:** `ClassifyHttpStatus(int) → AttemptOutcome` (FreeTierImageClient) and `→ HttpAttemptOutcome` (FreeTierHttp) — distinct enums in distinct types, used consistently. `RequestTimeoutMsFor(bool keyed)`, `ComposeFailureMessage(List<string>)`, `DispatchTimeoutMsFor(string)` — signatures match between their definition tasks and their test tasks. `ClassifyWebException` gains a `bool keyed` param (Task 2) and its sole caller is updated in the same task. `KeylessThrottleMarker` is defined once and referenced by both `TryProvider` and `ComposeFailureMessage` (Task 4). ✓

**4. Ordering:** Task 1 establishes IVT before any internal-referencing test compiles; Task 5's assertion (`dispatch > keyed request timeout`) depends on Task 3's `RequestTimeoutMsFor` already existing — Task 5 runs after Task 3. ✓
