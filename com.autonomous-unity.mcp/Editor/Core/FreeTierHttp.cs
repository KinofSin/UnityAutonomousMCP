using System;
using System.IO;
using System.Net;
using System.Text;

namespace AutonomousMcp.Editor.Core
{
    /// <summary>Classification of a single HTTP attempt, shared by the free-tier clients.</summary>
    public enum HttpAttemptOutcome
    {
        Success,
        RateLimited,    // 429 / quota — park key, rotate, retry
        AuthFailure,    // 401 / 403   — park key for a long rest, rotate
        Transient,      // 5xx / 408 / network / model-loading — backoff + retry
        Fatal           // 4xx (bad request) — don't retry this provider for this input
    }

    public sealed class HttpAttemptResult
    {
        public HttpAttemptOutcome Outcome;
        public byte[] Bytes;
        public string Extension;
        public TimeSpan? RetryAfter;
        public string Detail;
    }

    /// <summary>
    /// Synchronous HTTP helper shared by the free-tier generation clients. Centralizes request
    /// construction, payload validation, and the status→retry classification so the audio/image/etc.
    /// clients can focus on provider specifics. Runs on the editor main thread (IGenerator.Generate
    /// is invoked there); requests are bounded by <see cref="TimeoutMs"/>.
    /// </summary>
    public static class FreeTierHttp
    {
        public const int TimeoutMs = 120_000;

        /// <summary>
        /// Perform one attempt. <paramref name="validate"/> inspects the response payload + content
        /// type and returns whether it is the expected media and, if so, the file extension to use.
        /// </summary>
        public static HttpAttemptResult Attempt(
            string method,
            string url,
            string body,
            string contentType,
            string bearerKey,
            string acceptHeader,
            Func<byte[], string, (bool ok, string ext)> validate)
        {
            HttpWebRequest req;
            try
            {
                req = (HttpWebRequest)WebRequest.Create(url);
            }
            catch (Exception ex)
            {
                return new HttpAttemptResult { Outcome = HttpAttemptOutcome.Fatal, Detail = "bad url: " + ex.Message };
            }

            req.Method = method;
            req.Timeout = TimeoutMs;
            req.ReadWriteTimeout = TimeoutMs;
            req.UserAgent = "AutonomousMCP-Generator/1.0";
            if (!string.IsNullOrEmpty(acceptHeader)) req.Accept = acceptHeader;
            if (!string.IsNullOrEmpty(bearerKey)) req.Headers["Authorization"] = "Bearer " + bearerKey;

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

                    var (ok, ext) = validate(data, ct);
                    if (ok)
                        return new HttpAttemptResult { Outcome = HttpAttemptOutcome.Success, Bytes = data, Extension = ext };

                    // Some inference backends return 200 + JSON while a model cold-starts.
                    var text = SafeText(data);
                    if (text.IndexOf("estimated_time", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        text.IndexOf("currently loading", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        text.IndexOf("is loading", StringComparison.OrdinalIgnoreCase) >= 0)
                        return new HttpAttemptResult { Outcome = HttpAttemptOutcome.Transient, Detail = "model loading" };

                    return new HttpAttemptResult { Outcome = HttpAttemptOutcome.Transient, Detail = "unexpected payload: " + Truncate(text, 160) };
                }
            }
            catch (WebException we)
            {
                return Classify(we);
            }
            catch (Exception ex)
            {
                return new HttpAttemptResult { Outcome = HttpAttemptOutcome.Transient, Detail = ex.Message };
            }
        }

        private static HttpAttemptResult Classify(WebException we)
        {
            if (we.Response is HttpWebResponse er)
            {
                var status = (int)er.StatusCode;
                var retryAfter = ParseRetryAfter(er.Headers?["Retry-After"]);
                string detail;
                using (var rs = er.GetResponseStream())
                    detail = Truncate(SafeText(ReadAll(rs)), 160);

                if (status == 429) return new HttpAttemptResult { Outcome = HttpAttemptOutcome.RateLimited, RetryAfter = retryAfter, Detail = "429 " + detail };
                if (status == 401 || status == 403) return new HttpAttemptResult { Outcome = HttpAttemptOutcome.AuthFailure, Detail = status + " " + detail };
                if (status == 503 || status == 502 || status == 504 || status == 500 || status == 408)
                    return new HttpAttemptResult { Outcome = HttpAttemptOutcome.Transient, RetryAfter = retryAfter, Detail = status + " " + detail };
                return new HttpAttemptResult { Outcome = HttpAttemptOutcome.Fatal, Detail = status + " " + detail };
            }
            return new HttpAttemptResult { Outcome = HttpAttemptOutcome.Transient, Detail = we.Status + ": " + we.Message };
        }

        // ── shared utilities ───────────────────────────────────────────────────────

        public static byte[] ReadAll(Stream s)
        {
            if (s == null) return Array.Empty<byte>();
            using (var ms = new MemoryStream())
            {
                s.CopyTo(ms);
                return ms.ToArray();
            }
        }

        public static TimeSpan? ParseRetryAfter(string header)
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

        public static string SafeText(byte[] data)
        {
            try { return data == null ? "" : Encoding.UTF8.GetString(data); }
            catch { return ""; }
        }

        public static string Truncate(string s, int max) =>
            string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s.Substring(0, max) + "…");
    }
}
