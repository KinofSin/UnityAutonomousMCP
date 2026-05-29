using System;
using System.Collections.Generic;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace AutonomousMcp.Editor.Core
{
    /// <summary>Result of a full text-to-3D generation attempt.</summary>
    public sealed class Model3DGenResult
    {
        public bool Success;
        public byte[] Bytes;
        public string Extension = ".glb";
        public string ProviderUsed;
        public string TaskId;
        public int PollCount;
        public string Error;
        public List<string> Attempts = new List<string>();
    }

    /// <summary>
    /// Text-to-3D client with owned-key rotation + rate-limit backoff. Default provider is Meshy
    /// (hosted API returning GLB). Async provider APIs are handled with bounded synchronous polling
    /// on the editor main thread — see generator docs for the blocking caveat.
    ///
    /// Env-var configuration (read at request time, never persisted):
    ///   GENERATOR_MESHY_API_KEY       — one or more Meshy API keys (rotated)
    ///   GENERATOR_MODEL_3D_PROVIDER   — provider id (default meshy)
    ///   GENERATOR_MODEL_3D_MODE       — preview | refine (default preview)
    ///   GENERATOR_MODEL_3D_TIMEOUT_SEC — max wait for task completion (default 300)
    /// </summary>
    public static class FreeTierModel3DClient
    {
        private const string MeshyBase = "https://api.meshy.ai/openapi/v2/text-to-3d";
        private const int DefaultTimeoutSec = 300;
        private const int PollIntervalMs = 4000;
        private const int MaxCreateAttempts = 5;

        public static bool AnyProviderAvailable()
        {
            return KeyPool().Count > 0;
        }

        public static string DescribeAvailability()
        {
            var n = KeyPool().Count;
            return n > 0
                ? $"meshy text-to-3d ({n} key{(n == 1 ? "" : "s")}, mode {Mode()})"
                : "No model provider available — set GENERATOR_MESHY_API_KEY to enable Meshy text-to-3D.";
        }

        public static Model3DGenResult Generate(string prompt)
        {
            var result = new Model3DGenResult();
            if (string.IsNullOrWhiteSpace(prompt))
            {
                result.Error = "Prompt is empty.";
                return result;
            }

            var provider = (Environment.GetEnvironmentVariable("GENERATOR_MODEL_3D_PROVIDER") ?? "meshy").Trim();
            if (!provider.Equals("meshy", StringComparison.OrdinalIgnoreCase))
            {
                result.Error = $"Unsupported model provider '{provider}'. Only 'meshy' is implemented.";
                return result;
            }

            var pool = KeyPool();
            if (pool.Count == 0)
            {
                result.Error = "No Meshy API key configured. Set GENERATOR_MESHY_API_KEY.";
                return result;
            }

            return GenerateMeshy(prompt, pool, result);
        }

        private static Model3DGenResult GenerateMeshy(string prompt, ProviderKeyPool pool, Model3DGenResult result)
        {
            string taskId = null;

            // ── 1) Create task ───────────────────────────────────────────────────────
            for (var attempt = 0; attempt < MaxCreateAttempts; attempt++)
            {
                var nowUtc = DateTime.UtcNow;
                if (!pool.TryLease(nowUtc, out var key))
                {
                    var next = pool.NextAvailableUtc();
                    result.Attempts.Add($"meshy-create: all {pool.Count} key(s) cooling" +
                                        (next.HasValue ? $" until {next.Value:HH:mm:ss}Z" : ""));
                    break;
                }

                var body = Newtonsoft.Json.JsonConvert.SerializeObject(new
                {
                    mode = Mode(),
                    prompt
                });

                var ar = FreeTierHttp.Attempt("POST", MeshyBase, body, "application/json", key, "application/json",
                    (data, _) => ValidateJson(data, _));

                switch (ar.Outcome)
                {
                    case HttpAttemptOutcome.Success:
                        pool.ReportSuccess(key);
                        taskId = ParseTaskId(ar.Bytes);
                        if (!string.IsNullOrEmpty(taskId))
                        {
                            result.Attempts.Add($"meshy-create: ok taskId={taskId}");
                            break;
                        }
                        result.Attempts.Add("meshy-create: missing task id in response");
                        continue;

                    case HttpAttemptOutcome.RateLimited:
                        pool.ReportRateLimited(key, ar.RetryAfter, nowUtc);
                        result.Attempts.Add("meshy-create: 429 (rotating key)");
                        continue;

                    case HttpAttemptOutcome.AuthFailure:
                        pool.ReportAuthFailure(key, nowUtc);
                        result.Attempts.Add($"meshy-create: auth failure ({ar.Detail})");
                        continue;

                    case HttpAttemptOutcome.Transient:
                        pool.ReportTransientError(key, nowUtc);
                        result.Attempts.Add($"meshy-create: transient ({ar.Detail})");
                        continue;

                    case HttpAttemptOutcome.Fatal:
                        result.Attempts.Add($"meshy-create: fatal ({ar.Detail})");
                        result.Error = "Meshy rejected the create request: " + ar.Detail;
                        return result;
                }

                if (!string.IsNullOrEmpty(taskId)) break;
            }

            if (string.IsNullOrEmpty(taskId))
            {
                result.Error = "Failed to create Meshy task. Trace: " + string.Join(" | ", result.Attempts);
                return result;
            }

            result.TaskId = taskId;

            // ── 2) Poll until SUCCEEDED / FAILED / timeout ─────────────────────────
            var deadline = DateTime.UtcNow.AddSeconds(TimeoutSec());
            var pollUrl = MeshyBase + "/" + taskId;

            while (DateTime.UtcNow < deadline)
            {
                var nowUtc = DateTime.UtcNow;
                if (!pool.TryLease(nowUtc, out var key))
                {
                    Thread.Sleep(PollIntervalMs);
                    continue;
                }

                var ar = FreeTierHttp.Attempt("GET", pollUrl, null, null, key, "application/json",
                    (data, _) => ValidateJson(data, _));

                switch (ar.Outcome)
                {
                    case HttpAttemptOutcome.Success:
                        pool.ReportSuccess(key);
                        result.PollCount++;
                        var status = ParseStatus(ar.Bytes);
                        if (status == "SUCCEEDED")
                        {
                            var glbUrl = ParseGlbUrl(ar.Bytes);
                            if (string.IsNullOrEmpty(glbUrl))
                            {
                                result.Error = "Meshy task succeeded but no GLB URL was returned.";
                                result.Attempts.Add("meshy-poll: SUCCEEDED but no glb url");
                                return result;
                            }
                            result.Attempts.Add($"meshy-poll: SUCCEEDED after {result.PollCount} poll(s)");
                            return DownloadGlb(glbUrl, pool, result);
                        }
                        if (status == "FAILED" || status == "CANCELED" || status == "EXPIRED")
                        {
                            result.Error = $"Meshy task {status.ToLowerInvariant()}.";
                            result.Attempts.Add($"meshy-poll: {status}");
                            return result;
                        }
                        // PENDING / IN_PROGRESS — wait and poll again.
                        result.Attempts.Add($"meshy-poll: {status ?? "unknown"} (#{result.PollCount})");
                        Thread.Sleep(PollIntervalMs);
                        continue;

                    case HttpAttemptOutcome.RateLimited:
                        pool.ReportRateLimited(key, ar.RetryAfter, nowUtc);
                        Thread.Sleep(PollIntervalMs);
                        continue;

                    case HttpAttemptOutcome.AuthFailure:
                        pool.ReportAuthFailure(key, nowUtc);
                        continue;

                    case HttpAttemptOutcome.Transient:
                        pool.ReportTransientError(key, nowUtc);
                        Thread.Sleep(PollIntervalMs);
                        continue;

                    case HttpAttemptOutcome.Fatal:
                        result.Attempts.Add($"meshy-poll: fatal ({ar.Detail})");
                        result.Error = "Meshy poll failed: " + ar.Detail;
                        return result;
                }
            }

            result.Error = $"Meshy task timed out after {TimeoutSec()}s (taskId={taskId}, polls={result.PollCount}).";
            return result;
        }

        private static Model3DGenResult DownloadGlb(string url, ProviderKeyPool pool, Model3DGenResult result)
        {
            for (var attempt = 0; attempt < MaxCreateAttempts; attempt++)
            {
                // Signed CDN URLs typically need no auth.
                var ar = FreeTierHttp.Attempt("GET", url, null, null, null, "*/*", ValidateGlb);

                switch (ar.Outcome)
                {
                    case HttpAttemptOutcome.Success:
                        result.Success = true;
                        result.Bytes = ar.Bytes;
                        result.Extension = ".glb";
                        result.ProviderUsed = "meshy";
                        result.Attempts.Add($"meshy-download: ok ({ar.Bytes.Length} bytes)");
                        return result;

                    case HttpAttemptOutcome.RateLimited:
                    case HttpAttemptOutcome.Transient:
                        Thread.Sleep(2000);
                        continue;

                    case HttpAttemptOutcome.Fatal:
                    case HttpAttemptOutcome.AuthFailure:
                        result.Error = "GLB download failed: " + ar.Detail;
                        result.Attempts.Add($"meshy-download: {ar.Detail}");
                        return result;
                }
            }

            result.Error = "GLB download failed after retries.";
            return result;
        }

        // ── JSON / GLB parsing ───────────────────────────────────────────────────────

        private static (bool ok, string ext) ValidateJson(byte[] data, string _)
        {
            if (data == null || data.Length == 0) return (false, ".json");
            try
            {
                JToken.Parse(FreeTierHttp.SafeText(data));
                return (true, ".json");
            }
            catch { return (false, ".json"); }
        }

        private static (bool ok, string ext) ValidateGlb(byte[] data, string contentType)
        {
            if (data == null || data.Length < 4) return (false, ".glb");
            // glTF binary magic: 'g' 'l' 'T' 'F'
            if (data[0] == 0x67 && data[1] == 0x6C && data[2] == 0x54 && data[3] == 0x46)
                return (true, ".glb");
            if (!string.IsNullOrEmpty(contentType) &&
                contentType.IndexOf("model/gltf", StringComparison.OrdinalIgnoreCase) >= 0)
                return (true, ".glb");
            return (false, ".glb");
        }

        private static string ParseTaskId(byte[] jsonBytes)
        {
            try
            {
                var j = JObject.Parse(FreeTierHttp.SafeText(jsonBytes));
                return (string)j["result"] ?? (string)j["id"];
            }
            catch { return null; }
        }

        private static string ParseStatus(byte[] jsonBytes)
        {
            try
            {
                var j = JObject.Parse(FreeTierHttp.SafeText(jsonBytes));
                return (string)j["status"];
            }
            catch { return null; }
        }

        private static string ParseGlbUrl(byte[] jsonBytes)
        {
            try
            {
                var j = JObject.Parse(FreeTierHttp.SafeText(jsonBytes));
                var urls = j["model_urls"];
                if (urls == null) return null;
                return (string)urls["glb"];
            }
            catch { return null; }
        }

        private static ProviderKeyPool KeyPool() =>
            ProviderKeyPool.FromEnv("GENERATOR_MESHY_API_KEY");

        private static string Mode()
        {
            var v = Environment.GetEnvironmentVariable("GENERATOR_MODEL_3D_MODE");
            if (string.IsNullOrWhiteSpace(v)) return "preview";
            v = v.Trim().ToLowerInvariant();
            return v == "refine" ? "refine" : "preview";
        }

        private static int TimeoutSec()
        {
            var v = Environment.GetEnvironmentVariable("GENERATOR_MODEL_3D_TIMEOUT_SEC");
            if (int.TryParse(v, out var sec) && sec > 0) return Math.Min(sec, 900);
            return DefaultTimeoutSec;
        }
    }
}
