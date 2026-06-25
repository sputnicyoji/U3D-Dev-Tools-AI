using System;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace Yoji.LuaDeviceDebug
{
    internal static class MainThreadDispatcher
    {
        private const int k_MaxQueuedJobs = 16;
        private static int s_MainThreadId = -1;
        private static SynchronizationContext s_Context;
        private static int s_QueuedJobs;

        public static void Initialize()
        {
            s_MainThreadId = Thread.CurrentThread.ManagedThreadId;
            s_Context = SynchronizationContext.Current;
        }

        public static T Run<T>(Func<T> work, int timeoutMs, out long elapsedMs)
        {
            if (Thread.CurrentThread.ManagedThreadId == s_MainThreadId)
            {
                var directWatch = Stopwatch.StartNew();
                var direct = work();
                elapsedMs = directWatch.ElapsedMilliseconds;
                return direct;
            }

            if (s_Context == null)
                throw new LuaDeviceDebugException(500, "INTERNAL_ERROR", "main thread dispatcher is not initialized");

            if (Interlocked.Increment(ref s_QueuedJobs) > k_MaxQueuedJobs)
            {
                Interlocked.Decrement(ref s_QueuedJobs);
                throw new LuaDeviceDebugException(429, "QUEUE_FULL", "main thread queue is full");
            }

            var job = new Job<T>(work);
            try
            {
                s_Context.Post(delegate
                {
                    if (Interlocked.CompareExchange(ref job.State, Job<T>.StateRunning, Job<T>.StateQueued) != Job<T>.StateQueued)
                    {
                        job.Done.Set();
                        return;
                    }

                    Interlocked.Decrement(ref s_QueuedJobs);
                    var watch = Stopwatch.StartNew();
                    try { job.Result = job.Work(); }
                    catch (Exception e) { job.Error = e; }
                    job.ElapsedMs = watch.ElapsedMilliseconds;
                    Interlocked.Exchange(ref job.State, Job<T>.StateCompleted);
                    job.Done.Set();
                }, null);
            }
            catch
            {
                Interlocked.Decrement(ref s_QueuedJobs);
                throw;
            }

            if (!job.Done.Wait(timeoutMs))
            {
                if (Interlocked.CompareExchange(ref job.State, Job<T>.StateCancelled, Job<T>.StateQueued) == Job<T>.StateQueued)
                    Interlocked.Decrement(ref s_QueuedJobs);
                throw new LuaDeviceDebugException(408, "EXECUTION_TIMEOUT", "main thread execution timed out");
            }

            elapsedMs = job.ElapsedMs;
            if (job.Error != null)
                ExceptionDispatchInfo.Capture(job.Error).Throw();
            return job.Result;
        }

        private sealed class Job<T>
        {
            public const int StateQueued = 0;
            public const int StateRunning = 1;
            public const int StateCompleted = 2;
            public const int StateCancelled = 3;

            public readonly Func<T> Work;
            public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
            public T Result;
            public Exception Error;
            public long ElapsedMs;
            public int State;

            public Job(Func<T> work)
            {
                Work = work;
            }
        }
    }
}
