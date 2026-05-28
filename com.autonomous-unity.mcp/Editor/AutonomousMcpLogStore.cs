using System;
using System.Collections.Generic;
using UnityEngine;

namespace AutonomousMcp.Editor
{
    internal sealed class AutonomousMcpLogEntry
    {
        public string Level { get; set; }
        public string Message { get; set; }
        public string StackTrace { get; set; }
        public string TimestampUtc { get; set; }
    }

    internal sealed class AutonomousMcpToolCallEntry
    {
        public string TimestampUtc { get; set; }
        public string Tool { get; set; }
        public string Category { get; set; }
        public long DurationMs { get; set; }
        public bool Success { get; set; }
        public string Error { get; set; }
    }

    [UnityEditor.InitializeOnLoad]
    internal static class AutonomousMcpLogStore
    {
        private const int MaxEntries = 1500;
        private const int MaxToolCalls = 200;

        private static readonly List<AutonomousMcpLogEntry> Entries = new List<AutonomousMcpLogEntry>(MaxEntries);
        private static readonly List<AutonomousMcpToolCallEntry> ToolCalls = new List<AutonomousMcpToolCallEntry>(MaxToolCalls);
        private static readonly object Gate = new object();
        private static readonly object ToolGate = new object();

        static AutonomousMcpLogStore()
        {
            Application.logMessageReceivedThreaded += OnLog;
        }

        public static IReadOnlyList<AutonomousMcpLogEntry> Read(string level, int limit)
        {
            lock (Gate)
            {
                var normalized = string.IsNullOrWhiteSpace(level) ? "all" : level.ToLowerInvariant();
                var output = new List<AutonomousMcpLogEntry>(Mathf.Clamp(limit, 1, 1000));

                for (var index = Entries.Count - 1; index >= 0 && output.Count < limit; index--)
                {
                    var item = Entries[index];
                    if (normalized != "all" && item.Level != normalized)
                    {
                        continue;
                    }

                    output.Add(item);
                }

                return output;
            }
        }

        public static void RecordToolCall(string tool, long ms, bool ok, string error, string category = null)
        {
            lock (ToolGate)
            {
                ToolCalls.Add(new AutonomousMcpToolCallEntry
                {
                    TimestampUtc = DateTime.UtcNow.ToString("O"),
                    Tool = tool ?? string.Empty,
                    Category = category ?? string.Empty,
                    DurationMs = ms,
                    Success = ok,
                    Error = error ?? string.Empty
                });

                if (ToolCalls.Count > MaxToolCalls)
                {
                    ToolCalls.RemoveAt(0);
                }
            }
        }

        public static IReadOnlyList<AutonomousMcpToolCallEntry> ReadToolCalls(int limit)
        {
            lock (ToolGate)
            {
                var capped = Mathf.Clamp(limit, 1, MaxToolCalls);
                var output = new List<AutonomousMcpToolCallEntry>(capped);
                for (var index = ToolCalls.Count - 1; index >= 0 && output.Count < capped; index--)
                {
                    output.Add(ToolCalls[index]);
                }
                return output;
            }
        }

        public static void ClearToolCalls()
        {
            lock (ToolGate)
            {
                ToolCalls.Clear();
            }
        }

        private static void OnLog(string condition, string stackTrace, LogType type)
        {
            lock (Gate)
            {
                Entries.Add(new AutonomousMcpLogEntry
                {
                    Level = type switch
                    {
                        LogType.Warning => "warning",
                        LogType.Error => "error",
                        LogType.Exception => "error",
                        LogType.Assert => "error",
                        _ => "log"
                    },
                    Message = condition,
                    StackTrace = stackTrace,
                    TimestampUtc = DateTime.UtcNow.ToString("O")
                });

                if (Entries.Count > MaxEntries)
                {
                    Entries.RemoveAt(0);
                }
            }
        }
    }
}
