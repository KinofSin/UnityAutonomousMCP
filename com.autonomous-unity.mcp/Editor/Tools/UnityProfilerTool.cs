using System.Collections.Generic;
using AutonomousMcp.Editor.Core;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;

namespace AutonomousMcp.Editor.Tools
{
    /// <summary>
    /// unity_profiler — sampler reads + frame timing.
    /// </summary>
    public static class UnityProfilerTool
    {
        [InitializeOnLoadMethod]
        private static void Register()
        {
            ToolRegistry.Register("unity_profiler", ToolMode.Read, ToolCategory.Profiler,
                "Profiler sampler reads + frame timing snapshots. " +
                "Actions: read_sampler, frame_timing, memory_snapshot, list_recorders.",
                Handle);
        }

        private static AutonomousMcpToolResponse Handle(JObject args)
        {
            var action = args.Value<string>("action") ?? "frame_timing";
            switch (action)
            {
                case "read_sampler": return ReadSampler(args);
                case "frame_timing": return FrameTiming(args);
                case "memory_snapshot": return MemorySnapshot(args);
                case "list_recorders": return ListRecorders(args);
                default:
                    return Err($"Unsupported unity_profiler action '{action}'.");
            }
        }

        private static AutonomousMcpToolResponse ReadSampler(JObject args)
        {
            var name = args.Value<string>("name");
            if (string.IsNullOrEmpty(name)) return Err("name required (e.g. 'Camera.Render').");

            var sampler = Sampler.Get(name);
            if (sampler == null || !sampler.isValid) return Err($"Sampler '{name}' not found / not valid.");

            var recorder = sampler.GetRecorder();
            return Ok(new
            {
                action = "read_sampler",
                name,
                isValid = sampler.isValid,
                elapsedNanos = recorder?.elapsedNanoseconds ?? 0,
                sampleBlockCount = recorder?.sampleBlockCount ?? 0
            });
        }

        private static AutonomousMcpToolResponse FrameTiming(JObject args)
        {
            return Ok(new
            {
                action = "frame_timing",
                fps = 1f / Time.smoothDeltaTime,
                deltaTime = Time.deltaTime,
                smoothDeltaTime = Time.smoothDeltaTime,
                fixedDeltaTime = Time.fixedDeltaTime,
                realtimeSinceStartup = Time.realtimeSinceStartup,
                profilerEnabled = Profiler.enabled,
                profilerSupported = Profiler.supported
            });
        }

        private static AutonomousMcpToolResponse MemorySnapshot(JObject args)
        {
            return Ok(new
            {
                action = "memory_snapshot",
                totalAllocatedMemoryMB = Profiler.GetTotalAllocatedMemoryLong() / 1024.0 / 1024.0,
                totalReservedMemoryMB = Profiler.GetTotalReservedMemoryLong() / 1024.0 / 1024.0,
                totalUnusedReservedMemoryMB = Profiler.GetTotalUnusedReservedMemoryLong() / 1024.0 / 1024.0,
                monoUsedMB = Profiler.GetMonoUsedSizeLong() / 1024.0 / 1024.0,
                monoHeapMB = Profiler.GetMonoHeapSizeLong() / 1024.0 / 1024.0
            });
        }

        private static AutonomousMcpToolResponse ListRecorders(JObject args)
        {
            // Provide a curated set of commonly-watched samplers (Sampler.GetNames is internal).
            var common = new[]
            {
                "Camera.Render",
                "Render.OpaqueGeometry",
                "Render.TransparentGeometry",
                "BehaviourUpdate",
                "Animator.Update",
                "Physics.Simulate",
                "UI.UpdateBatches",
                "GC.Collect"
            };
            var results = new List<object>();
            foreach (var n in common)
            {
                var s = Sampler.Get(n);
                results.Add(new
                {
                    name = n,
                    isValid = s != null && s.isValid
                });
            }
            return Ok(new { action = "list_recorders", count = results.Count, recorders = results });
        }

        private static AutonomousMcpToolResponse Ok(object d) =>
            new AutonomousMcpToolResponse { success = true, data = JToken.FromObject(d), error = string.Empty };
        private static AutonomousMcpToolResponse Err(string m) =>
            new AutonomousMcpToolResponse { success = false, data = null, error = m };
    }
}
