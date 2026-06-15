using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading;
using UnityEditor;

namespace Yoji.EditorCore
{
    /// 把 HTTP 线程的工作派发到 Editor 主线程执行并阻塞等待结果。
    /// 注意：不能在主线程上经队列调用（update 无法重入）；主线程调用直接执行。
    /// 共享基础设施（editor-core）：editor-debug 与 test-runner 的 HTTP 服务都经此跳主线程。
    public static class MainThreadDispatcher
    {
        private sealed class Job
        {
            public Func<object> Work;
            public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
            public object Result;
            public Exception Error;
            public long ElapsedMs;
        }

        private static readonly ConcurrentQueue<Job> s_Queue = new ConcurrentQueue<Job>();
        private static int s_MainThreadId = -1;

        [InitializeOnLoadMethod]
        private static void Install()
        {
            s_MainThreadId = Thread.CurrentThread.ManagedThreadId;
            EditorApplication.update += Pump;
        }

        private static void Pump()
        {
            while (s_Queue.TryDequeue(out var job))
            {
                var sw = Stopwatch.StartNew();
                try { job.Result = job.Work(); }
                catch (Exception e) { job.Error = e; }
                job.ElapsedMs = sw.ElapsedMilliseconds;
                job.Done.Set();
            }
        }

        /// 返回主线程执行结果；主线程抛出的异常原栈重抛。elapsedMs 是主线程内的真实耗时。
        public static object Run(Func<object> work, out long elapsedMs, int timeoutMs = 100000)
        {
            if (Thread.CurrentThread.ManagedThreadId == s_MainThreadId)
            {
                var sw = Stopwatch.StartNew();
                var direct = work();
                elapsedMs = sw.ElapsedMilliseconds;
                return direct;
            }

            var job = new Job { Work = work };
            s_Queue.Enqueue(job);
            if (!job.Done.Wait(timeoutMs))
                throw new TimeoutException("editor main thread did not respond within " + timeoutMs + "ms");
            elapsedMs = job.ElapsedMs;
            if (job.Error != null) ExceptionDispatchInfo.Capture(job.Error).Throw();
            return job.Result;
        }
    }
}
