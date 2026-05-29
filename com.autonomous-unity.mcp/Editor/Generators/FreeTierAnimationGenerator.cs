using System;
using System.Collections.Generic;
using AutonomousMcp.Editor.Core;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace AutonomousMcp.Editor.Generators
{
    /// <summary>
    /// Procedural Animation generator — fully offline, no external API. Builds an
    /// <see cref="AnimationClip"/> from prompt-inferred presets or explicit options and saves it
    /// as a .anim asset. Always configured (unlike network-backed generators).
    /// </summary>
    internal sealed class FreeTierAnimationGenerator : IGenerator
    {
        public string ProviderId => "free-tier";
        public GeneratorKind Kind => GeneratorKind.Animation;
        public string DisplayName => "Procedural animation (offline)";

        public bool IsConfigured() => true;
        public string GetStatus() => "procedural (offline) — spin, bob, pulse, blink presets";

        public GenerationResult Generate(GenerationRequest request)
        {
            if (request == null)
                return GenerationResult.Fail("Null request.", ProviderId);

            if (string.IsNullOrWhiteSpace(request.Prompt) &&
                (request.ProviderOptions == null || !request.ProviderOptions.ContainsKey("preset")))
                return GenerationResult.Fail("Prompt or options.preset is required.", ProviderId);

            try
            {
                var (clip, preset, curveCount) = BuildClip(request);
                var assetPath = SaveClip(request.OutputAssetPath, clip);

                return GenerationResult.Ok(assetPath, ProviderId, new Dictionary<string, object>
                {
                    ["preset"] = preset,
                    ["duration"] = clip.length,
                    ["loop"] = clip.isLooping,
                    ["curveCount"] = curveCount,
                    ["frameRate"] = clip.frameRate
                });
            }
            catch (Exception ex)
            {
                return GenerationResult.Fail($"Animation build failed: {ex.Message}", ProviderId);
            }
        }

        private static (AnimationClip clip, string preset, int curveCount) BuildClip(GenerationRequest request)
        {
            var opts = request.ProviderOptions ?? new Dictionary<string, object>();
            var duration = OptFloat(opts, "duration", 2f);
            var loop = OptBool(opts, "loop", true);
            var path = OptString(opts, "path", "");

            // Raw curves mode: options.curves = [{ path, type, property, keys:[{time,value}] }]
            if (opts.TryGetValue("curves", out var rawCurves) && rawCurves != null)
                return (BuildFromRawCurves(rawCurves, duration, loop), "custom", CountCurvesFromRaw(rawCurves));

            var preset = OptString(opts, "preset", null);
            if (string.IsNullOrEmpty(preset))
                preset = InferPreset(request.Prompt ?? "");

            var clip = new AnimationClip { name = "GeneratedClip" };
            clip.frameRate = 60f;
            ApplyLoop(clip, loop);

            var curveCount = ApplyPreset(clip, preset, path, duration, opts);
            return (clip, preset, curveCount);
        }

        private static void ApplyLoop(AnimationClip clip, bool loop)
        {
            clip.wrapMode = loop ? WrapMode.Loop : WrapMode.Default;
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
        }

        private static int ApplyPreset(AnimationClip clip, string preset, string path, float duration,
            Dictionary<string, object> opts)
        {
            switch ((preset ?? "").ToLowerInvariant())
            {
                case "spin":
                case "rotate":
                {
                    var axis = OptString(opts, "axis", "y").ToLowerInvariant();
                    // Use the *Raw* Euler binding: "localEulerAngles.*" lands only in the editor-only
                    // m_EulerEditorCurves channel (no runtime rotation, not returned by
                    // GetCurveBindings), whereas "localEulerAnglesRaw.*" authors the real runtime
                    // m_EulerCurves so the clip actually rotates the Transform.
                    var prop = axis == "x" ? "localEulerAnglesRaw.x"
                        : axis == "z" ? "localEulerAnglesRaw.z"
                        : "localEulerAnglesRaw.y";
                    var curve = AnimationCurve.Linear(0f, 0f, duration, 360f);
                    SetFloat(clip, path, prop, curve);
                    return 1;
                }
                case "bob":
                case "float":
                {
                    var amp = OptFloat(opts, "amplitude", 0.5f);
                    var freq = OptFloat(opts, "frequency", 1f);
                    var curve = SampleSine(0f, duration, amp, freq, 0f);
                    SetFloat(clip, path, "localPosition.y", curve);
                    return 1;
                }
                case "pulse":
                case "scale":
                {
                    var amp = OptFloat(opts, "amplitude", 0.2f);
                    var freq = OptFloat(opts, "frequency", 1f);
                    var baseScale = 1f;
                    var curve = SampleSine(0f, duration, amp, freq, baseScale);
                    SetFloat(clip, path, "localScale.x", curve);
                    SetFloat(clip, path, "localScale.y", CloneCurve(curve));
                    SetFloat(clip, path, "localScale.z", CloneCurve(curve));
                    return 3;
                }
                case "blink":
                {
                    // Quick scale-Y dip to suggest a blink/flash on a flat object.
                    var curve = new AnimationCurve(
                        new Keyframe(0f, 1f),
                        new Keyframe(duration * 0.45f, 1f),
                        new Keyframe(duration * 0.5f, 0.05f),
                        new Keyframe(duration * 0.55f, 1f),
                        new Keyframe(duration, 1f));
                    SetFloat(clip, path, "localScale.y", curve);
                    return 1;
                }
                default:
                    // Fallback: gentle bob so unknown prompts still produce something useful.
                    var fallback = SampleSine(0f, duration, 0.25f, 1f, 0f);
                    SetFloat(clip, path, "localPosition.y", fallback);
                    return 1;
            }
        }

        private static string InferPreset(string prompt)
        {
            var p = prompt.ToLowerInvariant();
            if (p.Contains("spin") || p.Contains("rotate") || p.Contains("turn")) return "spin";
            if (p.Contains("bob") || p.Contains("float") || p.Contains("hover")) return "bob";
            if (p.Contains("pulse") || p.Contains("scale") || p.Contains("breathe")) return "pulse";
            if (p.Contains("blink") || p.Contains("flash")) return "blink";
            return "bob";
        }

        private static AnimationCurve SampleSine(float t0, float t1, float amplitude, float frequency, float offset)
        {
            const int samples = 30;
            var keys = new Keyframe[samples + 1];
            for (var i = 0; i <= samples; i++)
            {
                var t = Mathf.Lerp(t0, t1, i / (float)samples);
                var v = offset + amplitude * Mathf.Sin(t * frequency * Mathf.PI * 2f);
                keys[i] = new Keyframe(t, v);
            }
            return new AnimationCurve(keys);
        }

        private static void SetFloat(AnimationClip clip, string path, string property, AnimationCurve curve)
        {
            var binding = EditorCurveBinding.FloatCurve(path, typeof(Transform), property);
            AnimationUtility.SetEditorCurve(clip, binding, curve);
        }

        private static AnimationCurve CloneCurve(AnimationCurve source) =>
            new AnimationCurve(source.keys);

        private static AnimationClip BuildFromRawCurves(object raw, float duration, bool loop)
        {
            var clip = new AnimationClip { name = "GeneratedClip" };
            clip.frameRate = 60f;
            ApplyLoop(clip, loop);

            var arr = raw as JArray ?? JArray.FromObject(raw);
            foreach (var entry in arr)
            {
                var path = (string)entry["path"] ?? "";
                var typeName = (string)entry["type"] ?? "Transform";
                var property = (string)entry["property"];
                if (string.IsNullOrEmpty(property)) continue;

                var type = typeName == "Transform" ? typeof(Transform) : typeof(Transform);
                var keys = entry["keys"] as JArray;
                if (keys == null || keys.Count == 0) continue;

                var keyframes = new Keyframe[keys.Count];
                for (var i = 0; i < keys.Count; i++)
                {
                    var k = keys[i];
                    keyframes[i] = new Keyframe(
                        (float?)k["time"] ?? 0f,
                        (float?)k["value"] ?? 0f);
                }
                var curve = new AnimationCurve(keyframes);
                var binding = EditorCurveBinding.FloatCurve(path, type, property);
                AnimationUtility.SetEditorCurve(clip, binding, curve);
            }

            if (clip.length <= 0f) clip.EnsureQuaternionContinuity();
            return clip;
        }

        private static int CountCurvesFromRaw(object raw)
        {
            try
            {
                var arr = raw as JArray ?? JArray.FromObject(raw);
                return arr?.Count ?? 0;
            }
            catch { return 0; }
        }

        private static string SaveClip(string requestedOutput, AnimationClip clip)
        {
            var rel = string.IsNullOrWhiteSpace(requestedOutput)
                ? $"{GeneratorConfig.Data.defaultOutputDirectory.TrimEnd('/')}/Animation_{DateTime.UtcNow:yyyyMMdd_HHmmss}"
                : requestedOutput.Replace('\\', '/');

            if (!rel.StartsWith("Assets/", StringComparison.Ordinal))
                rel = "Assets/" + rel.TrimStart('/');

            if (!rel.EndsWith(".anim", StringComparison.OrdinalIgnoreCase))
                rel += ".anim";

            EnsureFolder(ParentFolder(rel));

            rel = AssetDatabase.GenerateUniqueAssetPath(rel);
            AssetDatabase.CreateAsset(clip, rel);
            AssetDatabase.SaveAssets();
            return rel;
        }

        // Native assets (AnimationClip) must be created via AssetDatabase.CreateAsset, which
        // requires the parent to be a registered AssetDatabase folder — so we create the folder
        // chain here rather than the Directory.CreateDirectory path the binary generators use.
        private static string ParentFolder(string assetPath)
        {
            var dir = System.IO.Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            return string.IsNullOrEmpty(dir) ? "Assets" : dir;
        }

        private static void EnsureFolder(string assetFolder)
        {
            if (string.IsNullOrEmpty(assetFolder) || AssetDatabase.IsValidFolder(assetFolder)) return;
            var parent = ParentFolder(assetFolder);
            var name = System.IO.Path.GetFileName(assetFolder);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            if (!string.IsNullOrEmpty(name)) AssetDatabase.CreateFolder(parent, name);
        }

        private static string OptString(Dictionary<string, object> opts, string key, string fallback)
        {
            if (opts == null || !opts.TryGetValue(key, out var raw) || raw == null) return fallback;
            return raw.ToString();
        }

        private static float OptFloat(Dictionary<string, object> opts, string key, float fallback)
        {
            if (opts == null || !opts.TryGetValue(key, out var raw) || raw == null) return fallback;
            try
            {
                switch (raw)
                {
                    case float f: return f;
                    case double d: return (float)d;
                    case int i: return i;
                    case long l: return l;
                    default:
                        return float.TryParse(raw.ToString(), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;
                }
            }
            catch { return fallback; }
        }

        private static bool OptBool(Dictionary<string, object> opts, string key, bool fallback)
        {
            if (opts == null || !opts.TryGetValue(key, out var raw) || raw == null) return fallback;
            if (raw is bool b) return b;
            var s = raw.ToString();
            if (s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
            if (s == "0" || s.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
            return fallback;
        }
    }
}
