using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace AutonomousMcp.Editor.Core
{
    /// <summary>Outcome of a single HTTP attempt, classified for retry/rotation decisions.</summary>
    internal enum AttemptOutcome
    {
        Success,
        RateLimited,    // 429 / quota — park key, rotate, retry
        AuthFailure,    // 401 / 403   — park key for a long rest, rotate
        Transient,      // 5xx / 408 / network / model-loading — backoff + retry
        Fatal           // 4xx (bad request) — don't retry this provider for this prompt
    }

    /// <summary>Result of a full generation attempt across all providers.</summary>
    public sealed class ImageGenResult
    {
        public bool Success;
        public byte[] Bytes;
        public string Extension = ".png";
        public string ProviderUsed;
        public string Model;
        public string Error;
        public List<string> Attempts = new List<string>(); // human-readable per-provider trace
    }

    /// <summary>
    /// Multi-provider, rate-limit-resilient image generator. Tries providers in priority order;
    /// within a keyed provider it rotates your owned keys (<see cref="ProviderKeyPool"/>) and
    /// applies exponential backoff on 429/5xx; when all keyed providers are exhausted it falls
    /// back to a keyless free provider so generation still succeeds.
    ///
    /// All providers used here are legitimate free tiers driven by keys you own (or keyless
    /// public endpoints). No scraped or third-party secrets — those get revoked and are not legal
    /// to use anyway.
    ///
    /// Env-var configuration (read at request time, never persisted):
    ///   GENERATOR_HF_TOKEN / HUGGINGFACE_API_KEY  — one or more HF tokens (comma/space separated)
    ///   GENERATOR_HF_IMAGE_MODEL                   — HF model id (default FLUX.1-schnell)
    ///   GENERATOR_IMAGE_PROVIDER_ORDER             — CSV priority override, e.g. "huggingface,pollinations"
    ///   GENERATOR_DISABLE_KEYLESS                  — set to "1"/"true" to forbid the keyless fallback
    /// </summary>
    public static class FreeTierImageClient
    {
        private const int MaxAttemptsPerProvider = 4;
        private const int RequestTimeoutMs = 90_000;
        private const string DefaultHfModel = "black-forest-labs/FLUX.1-schnell";

        private sealed class Provider
        {
            public string Id;
            public bool RequiresKey;
            public ProviderKeyPool KeyPool;                                   // null when keyless
            public string Model;
            // Performs one HTTP attempt; classifies the outcome.
            public Func<string /*prompt*/, int /*w*/, int /*h*/, string /*key*/, AttemptResult> Attempt;
        }

        private struct AttemptResult
        {
            public AttemptOutcome Outcome;
            public byte[] Bytes;
            public string Extension;
            public TimeSpan? RetryAfter;
            public string Detail;
        }

        /// <summary>True if any provider (keyed or keyless) is currently usable.</summary>
        public static bool AnyProviderAvailable()
        {
            return BuildProviders().Any(p => !p.RequiresKey || (p.KeyPool != null && p.KeyPool.Count > 0));
        }

        /// <summary>Short status line for the Settings UI / tool 'list' output.</summary>
        public static string DescribeAvailability()
        {
            var providers = BuildProviders();
            var parts = new List<string>();
            foreach (var p in providers)
            {
                if (!p.RequiresKey) { parts.Add($"{p.Id} (keyless)"); continue; }
                var n = p.KeyPool?.Count ?? 0;
                if (n > 0) parts.Add($"{p.Id} ({n} key{(n == 1 ? "" : "s")})");
            }
            return parts.Count == 0
                ? "No image providers available."
                : "Providers: " + string.Join(", ", parts);
        }

        public static ImageGenResult Generate(string prompt, int width, int height, string seedOpt = null)
        {
            var result = new ImageGenResult();
            if (string.IsNullOrWhiteSpace(prompt))
            {
                result.Error = "Prompt is empty.";
                return result;
            }

            width = Mathf.ClampSize(width, 64, 2048, 1024);
            height = Mathf.ClampSize(height, 64, 2048, 1024);

            var providers = BuildProviders();
            if (providers.Count == 0)
            {
                result.Error = "No image providers configured. Set GENERATOR_HF_TOKEN for HuggingFace, " +
                               "or rely on the keyless fallback (unset GENERATOR_DISABLE_KEYLESS).";
                return result;
            }

            foreach (var provider in providers)
            {
                var providerTrace = TryProvider(provider, prompt, width, height, result);
                result.Attempts.Add(providerTrace);
                if (result.Success)
                {
                    result.ProviderUsed = provider.Id;
                    result.Model = provider.Model;
                    return result;
                }
            }

            result.Error = "All image providers failed. Trace: " + string.Join(" | ", result.Attempts);
            return result;
        }

        // Returns a human-readable trace string for this provider; sets result.Bytes/Success on win.
        private static string TryProvider(Provider provider, string prompt, int width, int height, ImageGenResult result)
        {
            if (provider.RequiresKey && (provider.KeyPool == null || provider.KeyPool.Count == 0))
                return $"{provider.Id}: skipped (no keys)";

            for (var attempt = 0; attempt < MaxAttemptsPerProvider; attempt++)
            {

                var nowUtc = DateTime.UtcNow;
                string key = null;

                if (provider.RequiresKey)
                {
                    if (!provider.KeyPool.TryLease(nowUtc, out key))
                    {
                        var next = provider.KeyPool.NextAvailableUtc();
                        return $"{provider.Id}: all {provider.KeyPool.Count} key(s) cooling" +
                               (next.HasValue ? $" until {next.Value:HH:mm:ss}Z" : "");
                    }
                }

                AttemptResult ar;
                try
                {
                    ar = provider.Attempt(prompt, width, height, key);
                }
                catch (Exception ex)
                {
                    ar = new AttemptResult { Outcome = AttemptOutcome.Transient, Detail = ex.Message };
                }

                switch (ar.Outcome)
                {
                    case AttemptOutcome.Success:
                        if (provider.RequiresKey) provider.KeyPool.ReportSuccess(key);
                        result.Success = true;
                        result.Bytes = ar.Bytes;
                        result.Extension = string.IsNullOrEmpty(ar.Extension) ? ".png" : ar.Extension;
                        return $"{provider.Id}: ok ({ar.Bytes.Length} bytes)";

                    case AttemptOutcome.RateLimited:
                        if (provider.RequiresKey) provider.KeyPool.ReportRateLimited(key, ar.RetryAfter, nowUtc);
                        continue; // rotate to next key / retry

                    case AttemptOutcome.AuthFailure:
                        if (provider.RequiresKey) provider.KeyPool.ReportAuthFailure(key, nowUtc);
                        continue;

                    case AttemptOutcome.Transient:
                        if (provider.RequiresKey) provider.KeyPool.ReportTransientError(key, nowUtc);
                        // brief local pause for keyless transient (no pool to gate cadence)
                        if (!provider.RequiresKey) System.Threading.Thread.Sleep(Math.Min(1500 * (attempt + 1), 5000));
                        continue;

                    case AttemptOutcome.Fatal:
                        return $"{provider.Id}: fatal ({ar.Detail})";
                }
            }

            return $"{provider.Id}: exhausted {MaxAttemptsPerProvider} attempts";
        }

        // ── Provider catalog ─────────────────────────────────────────────────────────

        private static List<Provider> BuildProviders()
        {
            var hfModel = NonEmptyEnv("GENERATOR_HF_IMAGE_MODEL") ?? DefaultHfModel;
            var keylessDisabled = IsTruthy(Environment.GetEnvironmentVariable("GENERATOR_DISABLE_KEYLESS"));

            var catalog = new Dictionary<string, Provider>(StringComparer.OrdinalIgnoreCase)
            {
                ["huggingface"] = new Provider
                {
                    Id = "huggingface",
                    RequiresKey = true,
                    KeyPool = ProviderKeyPool.FromEnv("GENERATOR_HF_TOKEN", "HUGGINGFACE_API_KEY", "HF_TOKEN"),
                    Model = hfModel,
                    Attempt = (prompt, w, h, key) => HuggingFaceAttempt(hfModel, prompt, w, h, key)
                }
            };

            if (!keylessDisabled)
            {
                catalog["pollinations"] = new Provider
                {
                    Id = "pollinations",
                    RequiresKey = false,
                    KeyPool = null,
                    Model = "pollinations",
                    Attempt = (prompt, w, h, _) => PollinationsAttempt(prompt, w, h)
                };
            }

            // Priority order: explicit override, else keyed providers first then keyless fallback.
            var order = ParseOrder(NonEmptyEnv("GENERATOR_IMAGE_PROVIDER_ORDER"))
                        ?? new List<string> { "huggingface", "pollinations" };

            var ordered = new List<Provider>();
            foreach (var id in order)
                if (catalog.TryGetValue(id, out var p) && !ordered.Contains(p))
                    ordered.Add(p);
            // Append any catalog entries not named in the order so nothing is silently dropped.
            foreach (var p in catalog.Values)
                if (!ordered.Contains(p))
                    ordered.Add(p);

            return ordered;
        }

        private static List<string> ParseOrder(string csv)
        {
            if (string.IsNullOrWhiteSpace(csv)) return null;
            return csv.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                      .Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        }

        // ── Concrete provider attempts (verified response shapes return raw image bytes) ──

        private static AttemptResult HuggingFaceAttempt(string model, string prompt, int w, int h, string key)
        {
            var url = $"https://api-inference.huggingface.co/models/{model}";
            var bodyObj = new
            {
                inputs = prompt,
                parameters = new { width = w, height = h }
            };
            var body = Newtonsoft.Json.JsonConvert.SerializeObject(bodyObj);

            return HttpAttempt("POST", url, body, "application/json", key, hf: true);
        }

        private static AttemptResult PollinationsAttempt(string prompt, int w, int h)
        {
            var seed = unchecked((uint)Guid.NewGuid().GetHashCode());
            var url = $"https://image.pollinations.ai/prompt/{Uri.EscapeDataString(prompt)}" +
                      $"?width={w}&height={h}&nologo=true&seed={seed}";
            return HttpAttempt("GET", url, null, null, key: null, hf: false);
        }

        // ── HTTP core (synchronous; IGenerator.Generate runs on the editor main thread) ──

        private static AttemptResult HttpAttempt(string method, string url, string body,
                                                 string contentType, string key, bool hf)
        {
            HttpWebRequest req;
            try
            {
                req = (HttpWebRequest)WebRequest.Create(url);
            }
            catch (Exception ex)
            {
                return new AttemptResult { Outcome = AttemptOutcome.Fatal, Detail = "bad url: " + ex.Message };
            }

            req.Method = method;
            req.Timeout = RequestTimeoutMs;
            req.ReadWriteTimeout = RequestTimeoutMs;
            req.UserAgent = "AutonomousMCP-Generator/1.0";
            req.Accept = "image/*";
            if (!string.IsNullOrEmpty(key))
                req.Headers["Authorization"] = "Bearer " + key;

            try
            {
                if (method == "POST" && body != null)
                {
                    var bytes = Encoding.UTF8.GetBytes(body);
                    req.ContentType = contentType ?? "application/json";
                    req.ContentLength = bytes.Length;
                    using (var s = req.GetRequestStream()) s.Write(bytes, 0, bytes.Length);
                }

                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var rs = resp.GetResponseStream())
                {
                    var data = ReadAll(rs);
                    var ct = resp.ContentType ?? string.Empty;

                    if (LooksLikeImage(data, ct, out var ext))
                        return new AttemptResult { Outcome = AttemptOutcome.Success, Bytes = data, Extension = ext };

                    // HF returns 200 + JSON when the model is still loading ("estimated_time").
                    var text = SafeText(data);
                    if (hf && text.IndexOf("estimated_time", StringComparison.OrdinalIgnoreCase) >= 0)
                        return new AttemptResult { Outcome = AttemptOutcome.Transient, Detail = "model loading" };

                    return new AttemptResult { Outcome = AttemptOutcome.Transient, Detail = "non-image response: " + Truncate(text, 160) };
                }
            }
            catch (WebException we)
            {
                return ClassifyWebException(we);
            }
            catch (Exception ex)
            {
                return new AttemptResult { Outcome = AttemptOutcome.Transient, Detail = ex.Message };
            }
        }

        private static AttemptResult ClassifyWebException(WebException we)
        {
            if (we.Response is HttpWebResponse er)
            {
                var status = (int)er.StatusCode;
                TimeSpan? retryAfter = ParseRetryAfter(er.Headers?["Retry-After"]);
                string detail;
                using (var rs = er.GetResponseStream())
                    detail = Truncate(SafeText(ReadAll(rs)), 160);

                if (status == 429) return new AttemptResult { Outcome = AttemptOutcome.RateLimited, RetryAfter = retryAfter, Detail = "429 " + detail };
                if (status == 401 || status == 403) return new AttemptResult { Outcome = AttemptOutcome.AuthFailure, Detail = status + " " + detail };
                if (status == 503 || status == 502 || status == 504 || status == 500 || status == 408)
                    return new AttemptResult { Outcome = AttemptOutcome.Transient, RetryAfter = retryAfter, Detail = status + " " + detail };
                return new AttemptResult { Outcome = AttemptOutcome.Fatal, Detail = status + " " + detail };
            }
            // No HTTP response → network/timeout → transient.
            return new AttemptResult { Outcome = AttemptOutcome.Transient, Detail = we.Status + ": " + we.Message };
        }

        // ── small utilities ──────────────────────────────────────────────────────────

        private static byte[] ReadAll(Stream s)
        {
            if (s == null) return Array.Empty<byte>();
            using (var ms = new MemoryStream())
            {
                s.CopyTo(ms);
                return ms.ToArray();
            }
        }

        private static bool LooksLikeImage(byte[] data, string contentType, out string ext)
        {
            ext = ".png";
            if (data == null || data.Length < 12) return false;

            if (!string.IsNullOrEmpty(contentType) &&
                contentType.IndexOf("image/", StringComparison.OrdinalIgnoreCase) < 0 &&
                contentType.IndexOf("application/octet-stream", StringComparison.OrdinalIgnoreCase) < 0)
            {
                // Content-Type explicitly says non-image; trust it only if magic bytes also fail below.
            }

            // PNG
            if (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47) { ext = ".png"; return true; }
            // JPEG
            if (data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) { ext = ".jpg"; return true; }
            // GIF
            if (data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46) { ext = ".gif"; return true; }
            // WEBP (RIFF....WEBP)
            if (data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46 &&
                data[8] == 0x57 && data[9] == 0x45 && data[10] == 0x42 && data[11] == 0x50) { ext = ".webp"; return true; }

            return false;
        }

        private static TimeSpan? ParseRetryAfter(string header)
        {
            if (string.IsNullOrWhiteSpace(header)) return null;
            if (int.TryParse(header.Trim(), out var secs) && secs >= 0)
                return TimeSpan.FromSeconds(Math.Min(secs, 600));
            if (DateTime.TryParse(header, out var when))
            {
                var delta = when.ToUniversalTime() - DateTime.UtcNow;
                if (delta > TimeSpan.Zero) return delta;
            }
            return null;
        }

        private static string SafeText(byte[] data)
        {
            try { return data == null ? "" : Encoding.UTF8.GetString(data); }
            catch { return ""; }
        }

        private static string Truncate(string s, int max) =>
            string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s.Substring(0, max) + "…");

        private static string NonEmptyEnv(string name)
        {
            var v = Environment.GetEnvironmentVariable(name);
            return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
        }

        private static bool IsTruthy(string v) =>
            !string.IsNullOrWhiteSpace(v) &&
            (v.Trim() == "1" || v.Trim().Equals("true", StringComparison.OrdinalIgnoreCase) ||
             v.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase));

        /// <summary>Tiny clamp helper kept local to avoid a UnityEngine.Mathf dependency in this file.</summary>
        private static class Mathf
        {
            public static int ClampSize(int value, int min, int max, int fallback)
            {
                if (value <= 0) value = fallback;
                if (value < min) value = min;
                if (value > max) value = max;
                return value;
            }
        }
    }
}
