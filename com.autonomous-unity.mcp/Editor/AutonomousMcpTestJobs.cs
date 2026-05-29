using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace AutonomousMcp.Editor
{
    internal sealed class AutonomousMcpTestCaseResult
    {
        public string Name { get; set; } = string.Empty;
        public string Outcome { get; set; } = "unknown";
        public double DurationSeconds { get; set; }
        public string Message { get; set; } = string.Empty;
        public string StackTrace { get; set; } = string.Empty;
    }

    internal sealed class AutonomousMcpTestJobState
    {
        private readonly object _gate = new object();

        public AutonomousMcpTestJobState(string jobId, string mode)
        {
            JobId = jobId;
            Mode = mode;
            Status = "queued";
            QueuedAtUtc = DateTime.UtcNow.ToString("O");
        }

        public string JobId { get; private set; }
        public string Mode { get; private set; }
        public string Status { get; private set; }
        public string Error { get; private set; } = string.Empty;
        public string QueuedAtUtc { get; private set; }
        public string StartedAtUtc { get; private set; } = string.Empty;
        public string FinishedAtUtc { get; private set; } = string.Empty;
        public int TotalTests { get; private set; }
        public int CompletedTests { get; private set; }
        public int Passed { get; private set; }
        public int Failed { get; private set; }
        public int Skipped { get; private set; }
        public List<AutonomousMcpTestCaseResult> Tests { get; } = new List<AutonomousMcpTestCaseResult>();

        public void MarkStarted(int total)
        {
            lock (_gate)
            {
                Status = "running";
                StartedAtUtc = DateTime.UtcNow.ToString("O");
                TotalTests = Math.Max(total, 0);
            }
            AutonomousMcpTestJobs.Persist(this);
        }

        public void AddResult(AutonomousMcpTestCaseResult result)
        {
            lock (_gate)
            {
                Tests.Add(result);
                CompletedTests += 1;

                switch (result.Outcome)
                {
                    case "passed":
                        Passed += 1;
                        break;
                    case "failed":
                    case "error":
                        Failed += 1;
                        break;
                    case "skipped":
                    case "inconclusive":
                        Skipped += 1;
                        break;
                }
            }
            AutonomousMcpTestJobs.Persist(this);
        }

        public void MarkCompleted()
        {
            lock (_gate)
            {
                if (Status != "failed")
                {
                    Status = "completed";
                }
                FinishedAtUtc = DateTime.UtcNow.ToString("O");
            }
            AutonomousMcpTestJobs.Persist(this);
        }

        public void MarkFailed(string error)
        {
            lock (_gate)
            {
                Status = "failed";
                Error = error ?? "Unknown test-runner failure.";
                FinishedAtUtc = DateTime.UtcNow.ToString("O");
            }
            AutonomousMcpTestJobs.Persist(this);
        }

        public object Snapshot()
        {
            lock (_gate)
            {
                return new
                {
                    jobId = JobId,
                    mode = Mode,
                    status = Status,
                    error = Error,
                    queuedAtUtc = QueuedAtUtc,
                    startedAtUtc = StartedAtUtc,
                    finishedAtUtc = FinishedAtUtc,
                    totalTests = TotalTests,
                    completedTests = CompletedTests,
                    passed = Passed,
                    failed = Failed,
                    skipped = Skipped,
                    tests = Tests
                };
            }
        }

        /// <summary>Rebuild a (read-only) job state from a previously persisted snapshot JSON.</summary>
        internal static AutonomousMcpTestJobState FromSnapshotJson(string json)
        {
            var j = JObject.Parse(json);
            var state = new AutonomousMcpTestJobState((string)j["jobId"] ?? string.Empty, (string)j["mode"] ?? string.Empty)
            {
                Status = (string)j["status"] ?? "unknown",
                Error = (string)j["error"] ?? string.Empty,
                StartedAtUtc = (string)j["startedAtUtc"] ?? string.Empty,
                FinishedAtUtc = (string)j["finishedAtUtc"] ?? string.Empty,
                TotalTests = (int?)j["totalTests"] ?? 0,
                CompletedTests = (int?)j["completedTests"] ?? 0,
                Passed = (int?)j["passed"] ?? 0,
                Failed = (int?)j["failed"] ?? 0,
                Skipped = (int?)j["skipped"] ?? 0,
            };
            state.QueuedAtUtc = (string)j["queuedAtUtc"] ?? state.QueuedAtUtc;
            if (j["tests"] is JArray arr)
            {
                foreach (var t in arr)
                {
                    state.Tests.Add(new AutonomousMcpTestCaseResult
                    {
                        Name = (string)t["Name"] ?? string.Empty,
                        Outcome = (string)t["Outcome"] ?? "unknown",
                        DurationSeconds = (double?)t["DurationSeconds"] ?? 0,
                        Message = (string)t["Message"] ?? string.Empty,
                        StackTrace = (string)t["StackTrace"] ?? string.Empty,
                    });
                }
            }
            return state;
        }
    }

    internal static class AutonomousMcpTestJobs
    {
        private static readonly ConcurrentDictionary<string, AutonomousMcpTestJobState> Jobs =
            new ConcurrentDictionary<string, AutonomousMcpTestJobState>();

        private const string KeyPrefix = "AutonomousMcp.TestJob.";

        public static AutonomousMcpTestJobState Create(string mode)
        {
            var jobId = Guid.NewGuid().ToString("N");
            var state = new AutonomousMcpTestJobState(jobId, mode);
            Jobs[jobId] = state;
            Persist(state);
            return state;
        }

        /// <summary>
        /// Look up a job: in-memory first, then fall back to SessionState (which survives the
        /// domain reloads that an EditMode run / recompile triggers — the in-memory dict does not).
        /// </summary>
        public static bool TryGet(string jobId, out AutonomousMcpTestJobState state)
        {
            if (Jobs.TryGetValue(jobId, out state))
            {
                return true;
            }
            var json = SessionState.GetString(KeyPrefix + jobId, string.Empty);
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    state = AutonomousMcpTestJobState.FromSnapshotJson(json);
                    Jobs[jobId] = state; // re-cache for subsequent polls this domain
                    return true;
                }
                catch
                {
                    // fall through
                }
            }
            state = null;
            return false;
        }

        /// <summary>Persist a job snapshot to SessionState so it survives domain reloads.</summary>
        public static void Persist(AutonomousMcpTestJobState state)
        {
            if (state == null) return;
            try
            {
                SessionState.SetString(KeyPrefix + state.JobId, JsonConvert.SerializeObject(state.Snapshot()));
            }
            catch
            {
                // best-effort; persistence failure must not break the run
            }
        }
    }
}
