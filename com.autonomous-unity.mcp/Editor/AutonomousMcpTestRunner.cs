using System;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace AutonomousMcp.Editor
{
    internal static class AutonomousMcpTestRunner
    {
        private static readonly TestRunnerApi Api = ScriptableObject.CreateInstance<TestRunnerApi>();

        public static string Run(string mode)
        {
            var normalized = string.Equals(mode, "playmode", StringComparison.OrdinalIgnoreCase)
                ? "playmode"
                : "editmode";

            var job = AutonomousMcpTestJobs.Create(normalized);
            var callback = new AutonomousMcpTestCallbacks(job);
            Api.RegisterCallbacks(callback);

            var filter = new Filter
            {
                testMode = normalized == "playmode" ? TestMode.PlayMode : TestMode.EditMode
            };

            try
            {
                Api.Execute(new ExecutionSettings(filter));
            }
            catch (Exception ex)
            {
                job.MarkFailed($"Unity Test Runner failed to start: {ex.Message}");
                Api.UnregisterCallbacks(callback);
                throw;
            }

            return job.JobId;
        }

        private sealed class AutonomousMcpTestCallbacks : ICallbacks
        {
            private readonly AutonomousMcpTestJobState _job;

            public AutonomousMcpTestCallbacks(AutonomousMcpTestJobState job)
            {
                _job = job;
            }

            public void RunStarted(ITestAdaptor testsToRun)
            {
                var total = ReadIntProperty(testsToRun, "TestCaseCount");
                _job.MarkStarted(total);
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                _job.MarkCompleted();
                Api.UnregisterCallbacks(this);
            }

            public void TestStarted(ITestAdaptor test)
            {
                // No-op
            }

            public void TestFinished(ITestResultAdaptor result)
            {
                if (ReadBoolProperty(result, "HasChildren"))
                {
                    return;
                }

                var outcome = NormalizeOutcome(ReadPropertyAsString(result, "ResultState"));
                _job.AddResult(new AutonomousMcpTestCaseResult
                {
                    Name = ReadPropertyAsString(result, "Name"),
                    Outcome = outcome,
                    DurationSeconds = ReadDoubleProperty(result, "Duration"),
                    Message = ReadPropertyAsString(result, "Message"),
                    StackTrace = ReadPropertyAsString(result, "StackTrace")
                });
            }

            private static string NormalizeOutcome(string raw)
            {
                if (string.IsNullOrWhiteSpace(raw))
                {
                    return "unknown";
                }

                var lowered = raw.ToLowerInvariant();
                if (lowered.Contains("pass"))
                {
                    return "passed";
                }

                if (lowered.Contains("inconclusive") || lowered.Contains("skip"))
                {
                    return "skipped";
                }

                if (lowered.Contains("fail") || lowered.Contains("error"))
                {
                    return "failed";
                }

                return lowered;
            }

            private static string ReadPropertyAsString(object instance, string propertyName)
            {
                if (instance == null)
                {
                    return string.Empty;
                }

                var property = instance.GetType().GetProperty(propertyName);
                if (property == null)
                {
                    return string.Empty;
                }

                var value = property.GetValue(instance);
                return value?.ToString() ?? string.Empty;
            }

            private static int ReadIntProperty(object instance, string propertyName)
            {
                var raw = ReadPropertyAsString(instance, propertyName);
                return int.TryParse(raw, out var parsed) ? parsed : 0;
            }

            private static double ReadDoubleProperty(object instance, string propertyName)
            {
                var raw = ReadPropertyAsString(instance, propertyName);
                return double.TryParse(raw, out var parsed) ? parsed : 0d;
            }

            private static bool ReadBoolProperty(object instance, string propertyName)
            {
                var raw = ReadPropertyAsString(instance, propertyName);
                return bool.TryParse(raw, out var parsed) && parsed;
            }
        }
    }
}
