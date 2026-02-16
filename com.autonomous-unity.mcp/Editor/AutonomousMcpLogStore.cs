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

    [UnityEditor.InitializeOnLoad]
    internal static class AutonomousMcpLogStore
    {
        private const int MaxEntries = 1500;
        private static readonly List<AutonomousMcpLogEntry> Entries = new List<AutonomousMcpLogEntry>(MaxEntries);
        private static readonly object Gate = new object();

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
