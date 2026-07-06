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
        private static volatile bool s_Draining;

        [InitializeOnLoadMethod]
        private static void Install()
        {
            s_MainThreadId = Thread.CurrentThread.ManagedThreadId;
            s_Draining = false;
            EditorApplication.update += Pump;
            AssemblyReloadEvents.beforeAssemblyReload += DrainForReload;
        }

        /// domain reload 前唤醒所有排队中的 waiter 并拒绝新入队。reload 后本域的 Pump 不复存在，
        /// 阻塞在 Done.Wait 的 HTTP 线程会让 Mono domain unload 反过来等它们——主线程与 HTTP 线程互等，
        /// 编辑器整体停摆（实测 EnterPlaymode 经 /invoke 触发 reload 时复现，轮询客户端还会串行放大）。
        internal static void DrainForReload()
        {
            s_Draining = true;
            while (s_Queue.TryDequeue(out var job))
            {
                job.Error = new OperationCanceledException(
                    "editor domain reload started before this job ran; retry after reload");
                job.Done.Set();
            }
        }

        /// 仅测试用：DrainForReload 置位的拒绝标志在真实 reload 中随域销毁自然复位，测试内需手动恢复。
        internal static void ResetDrainingForTests()
        {
            s_Draining = false;
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

            if (s_Draining)
                throw new OperationCanceledException("editor domain reload in progress; retry after reload");

            var job = new Job { Work = work };
            s_Queue.Enqueue(job);
            // 入队与 DrainForReload 清队之间的窗口：drain 先清完、随后才入队的 job 无人接手，补一次自清。
            if (s_Draining)
                DrainForReload();
            if (!job.Done.Wait(timeoutMs))
                throw new TimeoutException("editor main thread did not respond within " + timeoutMs + "ms");
            elapsedMs = job.ElapsedMs;
            if (job.Error != null) ExceptionDispatchInfo.Capture(job.Error).Throw();
            return job.Result;
        }
    }
}
