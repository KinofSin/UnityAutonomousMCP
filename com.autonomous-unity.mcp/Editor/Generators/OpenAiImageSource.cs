using System;
using System.IO;
using System.Net;
using System.Text;
using AutonomousMcp.Editor.Core;
using Newtonsoft.Json.Linq;

namespace AutonomousMcp.Editor.Generators
{
    // BYOK OpenAI image generation. Reads GENERATOR_OPENAI_API_KEY (fallback GENERATOR_API_KEY).
    // POSTs /v1/images/generations and decodes data[0].b64_json -> PNG bytes. Never embeds a key.
    internal sealed class OpenAiImageSource : IImageSource
    {
        public static string Key() =>
            Environment.GetEnvironmentVariable("GENERATOR_OPENAI_API_KEY")
            ?? Environment.GetEnvironmentVariable("GENERATOR_API_KEY");

        public static bool HasKey() => !string.IsNullOrWhiteSpace(Key());

        public byte[] FetchPng(string prompt, GenerationRequest req, out string error)
        {
            error = null;
            var key = Key();
            if (string.IsNullOrWhiteSpace(key)) { error = "Set GENERATOR_OPENAI_API_KEY (BYOK)."; return null; }
            if (string.IsNullOrWhiteSpace(prompt)) { error = "Empty prompt."; return null; }

            var model = Opt(req, "model", "gpt-image-1");
            var size = Opt(req, "size", "1024x1024");
            var endpoint = Opt(req, "endpoint", "https://api.openai.com/v1/images/generations");

            var body = new JObject { ["model"] = model, ["prompt"] = prompt, ["size"] = size, ["n"] = 1 };
            // dall-e-* needs response_format; gpt-image-1 returns b64_json by default and REJECTS it.
            if (model.IndexOf("dall-e", StringComparison.OrdinalIgnoreCase) >= 0)
                body["response_format"] = "b64_json";

            try
            {
                var http = (HttpWebRequest)WebRequest.Create(endpoint);
                http.Method = "POST";
                http.ContentType = "application/json";
                http.Headers["Authorization"] = "Bearer " + key;
                http.Timeout = 60_000;
                http.ReadWriteTimeout = 60_000;
                var payload = Encoding.UTF8.GetBytes(body.ToString());
                http.ContentLength = payload.Length;
                using (var s = http.GetRequestStream()) s.Write(payload, 0, payload.Length);

                using (var resp = (HttpWebResponse)http.GetResponse())
                using (var rs = resp.GetResponseStream())
                using (var sr = new StreamReader(rs))
                {
                    var json = JObject.Parse(sr.ReadToEnd());
                    var b64 = (string)json["data"]?[0]?["b64_json"];
                    if (string.IsNullOrEmpty(b64)) { error = "OpenAI response had no image data."; return null; }
                    return Convert.FromBase64String(b64);
                }
            }
            catch (WebException we)
            {
                var detail = we.Message;
                try { using (var er = we.Response?.GetResponseStream()) if (er != null) detail = new StreamReader(er).ReadToEnd(); }
                catch { /* keep we.Message */ }
                error = "OpenAI request failed: " + Truncate(detail, 300);
                return null;
            }
            catch (Exception ex) { error = "OpenAI error: " + ex.Message; return null; }
        }

        private static string Opt(GenerationRequest req, string key, string fallback)
        {
            if (req?.ProviderOptions != null && req.ProviderOptions.TryGetValue(key, out var v) && v != null)
            {
                var s = v.ToString();
                if (!string.IsNullOrWhiteSpace(s)) return s;
            }
            return fallback;
        }

        private static string Truncate(string s, int max) =>
            string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s.Substring(0, max) + "…");
    }
}
