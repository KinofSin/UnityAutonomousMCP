using System;
using System.Collections.Concurrent;
using System.Threading;
using UnityEditor;

namespace AutonomousMcp.Editor
{
    [InitializeOnLoad]
    internal static class AutonomousMcpMainThread
    {
        private static readonly ConcurrentQueue<Action> Queue = new ConcurrentQueue<Action>();
        private static readonly int MainThreadId;

        static AutonomousMcpMainThread()
        {
            MainThreadId = Thread.CurrentThread.ManagedThreadId;
            EditorApplication.update += Pump;
        }

        public static T Invoke<T>(Func<T> func, int timeoutMs = 10000)
        {
            if (Thread.CurrentThread.ManagedThreadId == MainThreadId)
            {
                return func();
            }

            using var waitHandle = new ManualResetEventSlim(false);
            T result = default;
            Exception captured = null;

            Queue.Enqueue(() =>
            {
                try
                {
                    result = func();
                }
                catch (Exception ex)
                {
                    captured = ex;
                }
                finally
                {
                    waitHandle.Set();
                }
            });

            if (!waitHandle.Wait(timeoutMs))
            {
                throw new TimeoutException($"Main-thread invocation timed out after {timeoutMs}ms.");
            }

            if (captured != null)
            {
                throw captured;
            }

            return result;
        }

        private static void Pump()
        {
            while (Queue.TryDequeue(out var action))
            {
                action();
            }
        }
    }
}
