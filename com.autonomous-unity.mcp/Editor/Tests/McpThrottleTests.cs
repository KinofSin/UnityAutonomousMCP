using System.Collections.Generic;
using NUnit.Framework;
using AutonomousMcp.Editor;
using AutonomousMcp.Editor.Core;

namespace AutonomousMcp.SelfTest
{
    // Pure unit tests for the throttle-handling helpers. No network, no MCP bridge — these run
    // in-process under the EditMode runner. The whole project's EditMode suite has pre-existing
    // failures from an unrelated package (YUCP/VPM installer, package-signing); these tests must
    // not add to that count.
    public sealed class McpThrottleTests
    {
        // ── FreeTierImageClient.ClassifyHttpStatus ───────────────────────────────────

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

        // ── FreeTierImageClient.RequestTimeoutMsFor ──────────────────────────────────

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

        // ── FreeTierImageClient.ComposeFailureMessage ────────────────────────────────

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

        // ── AutonomousMcpToolDispatcher.DispatchTimeoutMsFor ─────────────────────────

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

        // ── FreeTierHttp.ClassifyHttpStatus (audio/model3d parity) ───────────────────

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
    }
}
