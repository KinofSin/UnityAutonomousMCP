using System;
using System.Collections.Generic;
using System.Linq;

namespace AutonomousMcp.Editor.Core
{
    /// <summary>Result of a full audio generation attempt across all providers.</summary>
    public sealed class AudioGenResult
    {
        public bool Success;
        public byte[] Bytes;
        public string Extension = ".wav";
        public string ProviderUsed;
        public string Model;
        public string Error;
        public List<string> Attempts = new List<string>();
    }

    /// <summary>
    /// Text-to-audio client with the same owned-key rotation + rate-limit backoff as the image
    /// client, via <see cref="ProviderKeyPool"/> and <see cref="FreeTierHttp"/>. Currently wired to
    /// Hugging Face Inference text-to-audio models (e.g. MusicGen for music, MMS/Bark for speech).
    ///
    /// Unlike images, there is no reliable keyless audio provider, so this requires one of your own
    /// HF tokens — without one the Audio generator reports unconfigured and the stub is used instead.
    ///
    /// Env-var configuration (read at request time, never persisted):
    ///   GENERATOR_HF_TOKEN / HUGGINGFACE_API_KEY / HF_TOKEN  — one or more HF tokens (rotated)
    ///   GENERATOR_HF_AUDIO_MODEL                              — HF model id (default MusicGen small)
    /// </summary>
    public static class FreeTierAudioClient
    {
        private const int MaxAttempts = 5;
        private const string DefaultHfModel = "facebook/musicgen-small";

        public static bool AnyProviderAvailable()
        {
            return KeyPool().Count > 0;
        }

        public static string DescribeAvailability()
        {
            var n = KeyPool().Count;
            return n > 0
                ? $"huggingface text-to-audio ({n} key{(n == 1 ? "" : "s")}, model {Model()})"
                : "No audio provider available — set GENERATOR_HF_TOKEN to enable HuggingFace text-to-audio.";
        }

        public static AudioGenResult Generate(string prompt)
        {
            var result = new AudioGenResult { Model = Model() };
            if (string.IsNullOrWhiteSpace(prompt))
            {
                result.Error = "Prompt is empty.";
                return result;
            }

            var pool = KeyPool();
            if (pool.Count == 0)
            {
                result.Error = "No HuggingFace token configured. Set GENERATOR_HF_TOKEN (or HUGGINGFACE_API_KEY).";
                return result;
            }

            var model = Model();
            var url = $"https://api-inference.huggingface.co/models/{model}";
            var body = Newtonsoft.Json.JsonConvert.SerializeObject(new { inputs = prompt });

            for (var attempt = 0; attempt < MaxAttempts; attempt++)
            {
                var nowUtc = DateTime.UtcNow;
                if (!pool.TryLease(nowUtc, out var key))
                {
                    var next = pool.NextAvailableUtc();
                    result.Attempts.Add($"huggingface: all {pool.Count} key(s) cooling" +
                                        (next.HasValue ? $" until {next.Value:HH:mm:ss}Z" : ""));
                    break;
                }

                var ar = FreeTierHttp.Attempt("POST", url, body, "application/json", key, "audio/*", ValidateAudio);

                switch (ar.Outcome)
                {
                    case HttpAttemptOutcome.Success:
                        pool.ReportSuccess(key);
                        result.Success = true;
                        result.Bytes = ar.Bytes;
                        result.Extension = string.IsNullOrEmpty(ar.Extension) ? ".wav" : ar.Extension;
                        result.ProviderUsed = "huggingface";
                        result.Attempts.Add($"huggingface: ok ({ar.Bytes.Length} bytes)");
                        return result;

                    case HttpAttemptOutcome.RateLimited:
                        pool.ReportRateLimited(key, ar.RetryAfter, nowUtc);
                        result.Attempts.Add("huggingface: 429 (rotating key)");
                        continue;

                    case HttpAttemptOutcome.AuthFailure:
                        pool.ReportAuthFailure(key, nowUtc);
                        result.Attempts.Add($"huggingface: auth failure ({ar.Detail})");
                        continue;

                    case HttpAttemptOutcome.Transient:
                        pool.ReportTransientError(key, nowUtc);
                        result.Attempts.Add($"huggingface: transient ({ar.Detail})");
                        continue;

                    case HttpAttemptOutcome.Fatal:
                        result.Attempts.Add($"huggingface: fatal ({ar.Detail})");
                        result.Error = "Audio provider rejected the request: " + ar.Detail;
                        return result;
                }
            }

            result.Error = "Audio generation failed. Trace: " + string.Join(" | ", result.Attempts);
            return result;
        }

        // ── helpers ──────────────────────────────────────────────────────────────────

        private static ProviderKeyPool KeyPool() =>
            ProviderKeyPool.FromEnv("GENERATOR_HF_TOKEN", "HUGGINGFACE_API_KEY", "HF_TOKEN");

        private static string Model()
        {
            var v = Environment.GetEnvironmentVariable("GENERATOR_HF_AUDIO_MODEL");
            return string.IsNullOrWhiteSpace(v) ? DefaultHfModel : v.Trim();
        }

        /// <summary>Accept the payload only if it is recognizable audio (content-type or magic bytes).</summary>
        private static (bool ok, string ext) ValidateAudio(byte[] data, string contentType)
        {
            if (data == null || data.Length < 12) return (false, ".wav");

            if (!string.IsNullOrEmpty(contentType))
            {
                if (contentType.IndexOf("audio/wav", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    contentType.IndexOf("audio/x-wav", StringComparison.OrdinalIgnoreCase) >= 0) return (true, ".wav");
                if (contentType.IndexOf("audio/flac", StringComparison.OrdinalIgnoreCase) >= 0) return (true, ".flac");
                if (contentType.IndexOf("audio/mpeg", StringComparison.OrdinalIgnoreCase) >= 0) return (true, ".mp3");
                if (contentType.IndexOf("audio/ogg", StringComparison.OrdinalIgnoreCase) >= 0) return (true, ".ogg");
            }

            // RIFF....WAVE
            if (data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46 &&
                data[8] == 0x57 && data[9] == 0x41 && data[10] == 0x56 && data[11] == 0x45) return (true, ".wav");
            // fLaC
            if (data[0] == 0x66 && data[1] == 0x4C && data[2] == 0x61 && data[3] == 0x43) return (true, ".flac");
            // OggS
            if (data[0] == 0x4F && data[1] == 0x67 && data[2] == 0x67 && data[3] == 0x53) return (true, ".ogg");
            // MP3: ID3 tag or MPEG frame sync
            if (data[0] == 0x49 && data[1] == 0x44 && data[2] == 0x33) return (true, ".mp3");
            if (data[0] == 0xFF && (data[1] & 0xE0) == 0xE0) return (true, ".mp3");

            return (false, ".wav");
        }
    }
}
