using System;
using System.Diagnostics;
using System.Threading;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Compilation;

namespace Yoji.TestRunner
{
    /// /recompile：主线程 AssetDatabase.Refresh + RequestScriptCompilation，HTTP 线程轮询完成后写响应。
    /// 有意复制自 com.yoji.editor-debug 的 RecompileHandler 并改动：(1) 增加 AssetDatabase.Refresh；
    /// (2) 响应为 flat {success,message,compilationTime,hasErrors}（无 ok/elapsedMs 信封）；
    /// (3) 接 JobStore 状态机，非 Idle 返 409。未来抽 com.yoji.editor-core 时需先统一响应契约。
    internal static class RecompileHandler
    {
        private static volatile bool s_CompilationStarted;
        private static volatile bool s_CompilationFinished;
        private static volatile bool s_HasErrors;
        private static int s_Pending;

        [InitializeOnLoadMethod]
        private static void Install()
        {
            CompilationPipeline.compilationStarted += _ => s_CompilationStarted = true;
            CompilationPipeline.compilationFinished += _ => s_CompilationFinished = true;
            CompilationPipeline.assemblyCompilationFinished += (_, messages) =>
            {
                foreach (var m in messages)
                    if (m.type == CompilerMessageType.Error) s_HasErrors = true;
            };
        }

        public static (int status, JObject body) Run(JobStore jobs)
        {
            Interlocked.Increment(ref s_Pending);
            try
            {
                bool busy = false;
                MainThreadDispatcher.Run(() =>
                {
                    if (!jobs.IsIdle || EditorApplication.isCompiling) { busy = true; return null; }
                    jobs.SetCompiling(true);
                    s_CompilationStarted = false;
                    s_CompilationFinished = false;
                    s_HasErrors = false;
                    AssetDatabase.Refresh();
                    CompilationPipeline.RequestScriptCompilation();
                    return null;
                }, out _);

                if (busy)
                    return (409, new JObject
                    {
                        ["success"] = false,
                        ["message"] = "a compilation or test run is already in progress",
                        ["compilationTime"] = 0,
                        ["hasErrors"] = false,
                    });

                var sw = Stopwatch.StartNew();
                // 5s 内没进入编译说明没有可编译的变更，直接视为成功
                while (!s_CompilationStarted && sw.ElapsedMilliseconds < 5000) Thread.Sleep(100);
                if (s_CompilationStarted)
                    while (!s_CompilationFinished && sw.ElapsedMilliseconds < 180000) Thread.Sleep(100);

                // 无变更/无重载路径：复位 Compiling（有重载时域会被整体重建，无需复位）
                jobs.SetCompiling(false);

                var seconds = Math.Round(sw.Elapsed.TotalSeconds, 2);
                return (200, new JObject
                {
                    ["success"] = !s_HasErrors,
                    ["message"] = s_HasErrors ? "编译失败" : "编译成功，耗时 " + seconds + "s",
                    ["compilationTime"] = seconds,
                    ["hasErrors"] = s_HasErrors,
                });
            }
            finally { Interlocked.Decrement(ref s_Pending); }
        }

        /// beforeAssemblyReload 时给尚未写完的 recompile 响应留时间窗。
        public static void WaitForPendingResponse(int maxMs)
        {
            var sw = Stopwatch.StartNew();
            while (Volatile.Read(ref s_Pending) > 0 && sw.ElapsedMilliseconds < maxMs)
                Thread.Sleep(50);
            Thread.Sleep(200);
        }
    }
}
