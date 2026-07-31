using System;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace AutonomousMcp.Editor
{
    internal static class AutonomousMcpTestRunner
    {
        private static readonly TestRunnerApi Api = ScriptableObject.CreateInstance<TestRunnerApi>();

        public static string Run(string mode, string testFilter = null, string category = null)
        {
            var normalized = string.Equals(mode, "playmode", StringComparison.OrdinalIgnoreCase)
                ? "playmode"
                : "editmode";

            var job = AutonomousMcpTestJobs.Create(normalized);
            var callback = new AutonomousMcpTestCallbacks(job, testFilter, category);
            Api.RegisterCallbacks(callback);

            var filter = new Filter
            {
                testMode = normalized == "playmode" ? TestMode.PlayMode : TestMode.EditMode
            };
            // groupNames is a regex matched against the full test name; categoryNames matches [Category].
            if (!string.IsNullOrWhiteSpace(testFilter)) filter.groupNames = new[] { testFilter };
            if (!string.IsNullOrWhiteSpace(category)) filter.categoryNames = new[] { category };

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
            private readonly Regex _groupRegex;
            private readonly string _category;

            public AutonomousMcpTestCallbacks(AutonomousMcpTestJobState job, string testFilter = null, string category = null)
            {
                _job = job;
                _category = category;
                if (!string.IsNullOrWhiteSpace(testFilter))
                {
                    // Same semantics as Filter.groupNames: a regex over the full test name.
                    try { _groupRegex = new Regex(testFilter); }
                    catch (ArgumentException) { _groupRegex = null; }
                }
            }

            public void RunStarted(ITestAdaptor testsToRun)
            {
                // Unity hands RunStarted the whole tree even when ExecutionSettings carries a
                // filter, so TestCaseCount is the entire suite and a filtered run reads as a
                // stalled "7/260". Count the leaves the filter actually selects instead.
                var total = CountSelected(testsToRun);
                _job.MarkStarted(total);
            }

            private int CountSelected(ITestAdaptor root)
            {
                var fallback = ReadIntProperty(root, "TestCaseCount");
                if (_groupRegex == null && string.IsNullOrWhiteSpace(_category)) return fallback;

                try
                {
                    var counted = CountMatching(root);
                    return counted > 0 ? counted : fallback;
                }
                catch
                {
                    // Adaptor shape differs across Test Framework versions; a wrong total is
                    // cosmetic, so never let counting break the run.
                    return fallback;
                }
            }

            private int CountMatching(ITestAdaptor node)
            {
                if (node == null) return 0;

                if (node.HasChildren)
                {
                    var sum = 0;
                    foreach (var child in node.Children) sum += CountMatching(child);
                    return sum;
                }

                if (_groupRegex != null && !_groupRegex.IsMatch(node.FullName ?? string.Empty)) return 0;

                if (!string.IsNullOrWhiteSpace(_category))
                {
                    var hit = false;
                    var categories = node.Categories;
                    if (categories != null)
                    {
                        foreach (var c in categories)
                        {
                            if (string.Equals(c, _category, StringComparison.OrdinalIgnoreCase)) { hit = true; break; }
                        }
                    }
                    if (!hit) return 0;
                }

                return 1;
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
