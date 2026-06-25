using System;
using System.Diagnostics;
using System.Threading;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using Yoji.EditorCore;

namespace Yoji.EditorDebug
{
    /// /recompile：主线程触发编译，HTTP 线程轮询完成标志后写响应。
    /// 响应发出后 Editor 进入 domain reload，服务短暂下线（客户端职责：轮询 /ping 等恢复）。
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

        public static JObject Run()
        {
            Interlocked.Increment(ref s_Pending);
            try
            {
                bool alreadyCompiling = false;
                MainThreadDispatcher.Run(() =>
                {
                    if (EditorApplication.isCompiling) { alreadyCompiling = true; return null; }
                    s_CompilationStarted = false;
                    s_CompilationFinished = false;
                    s_HasErrors = false;
                    CompilationPipeline.RequestScriptCompilation();
                    return null;
                }, out _);

                if (alreadyCompiling)
                    return new JObject
                    {
                        ["ok"] = false,
                        ["elapsedMs"] = 0,
                        ["error"] = new JObject
                        {
                            ["type"] = "Conflict",
                            ["message"] = "a script compilation is already in progress",
                        },
                    };

                var sw = Stopwatch.StartNew();
                // 5s 内没进入编译说明没有可编译的变更，直接视为成功
                while (!s_CompilationStarted && sw.ElapsedMilliseconds < 5000) Thread.Sleep(100);
                if (s_CompilationStarted)
                    while (!s_CompilationFinished && sw.ElapsedMilliseconds < 180000) Thread.Sleep(100);

                return new JObject
                {
                    ["ok"] = true,
                    ["elapsedMs"] = sw.ElapsedMilliseconds,
                    ["result"] = new JObject
                    {
                        ["success"] = !s_HasErrors,
                        ["compilationTime"] = Math.Round(sw.Elapsed.TotalSeconds, 1),
                        ["hasErrors"] = s_HasErrors,
                    },
                };
            }
            finally { Interlocked.Decrement(ref s_Pending); }
        }

        /// beforeAssemblyReload 时给尚未写完的 recompile 响应留时间窗。
        public static void WaitForPendingResponse(int maxMs)
        {
            var sw = Stopwatch.StartNew();
            while (Volatile.Read(ref s_Pending) > 0 && sw.ElapsedMilliseconds < maxMs)
                Thread.Sleep(50);
            Thread.Sleep(200); // Run 返回到响应写出之间的微小窗口
        }
    }
}
