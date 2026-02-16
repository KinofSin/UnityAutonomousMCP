using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

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

        public string JobId { get; }
        public string Mode { get; }
        public string Status { get; private set; }
        public string Error { get; private set; } = string.Empty;
        public string QueuedAtUtc { get; }
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
        }

        public void MarkFailed(string error)
        {
            lock (_gate)
            {
                Status = "failed";
                Error = error ?? "Unknown test-runner failure.";
                FinishedAtUtc = DateTime.UtcNow.ToString("O");
            }
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
    }

    internal static class AutonomousMcpTestJobs
    {
        private static readonly ConcurrentDictionary<string, AutonomousMcpTestJobState> Jobs =
            new ConcurrentDictionary<string, AutonomousMcpTestJobState>();

        public static AutonomousMcpTestJobState Create(string mode)
        {
            var jobId = Guid.NewGuid().ToString("N");
            var state = new AutonomousMcpTestJobState(jobId, mode);
            Jobs[jobId] = state;
            return state;
        }

        public static bool TryGet(string jobId, out AutonomousMcpTestJobState state)
        {
            return Jobs.TryGetValue(jobId, out state);
        }
    }
}
