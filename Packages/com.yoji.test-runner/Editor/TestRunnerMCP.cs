using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Yoji.TestRunner
{
    /// TestRunnerMCP HTTP 服务。端口 21890（fallback 21894/21895），只绑 127.0.0.1。
    /// 真 HTTP 状态码 + 每端点独立 JSON 形状（与 editor-debug 恒 200 flat envelope 不同）。
    /// 阶段 1 仅 EditMode；PlayMode 在 /run-tests 被拒（400）。
    [InitializeOnLoad]
    internal static class TestRunnerMCP
    {
        private const int k_StaleJobMs = 600_000; // 孤儿任务清扫阈值
        private static readonly int[] k_Ports = { 21890, 21894, 21895 };

        private static HttpListener s_Listener;
        private static int s_Port;
        private static string s_UnityVersion;
        private static string s_ProjectName;
        private static JobStore s_Jobs;
        private static TestRunService s_Service;

        static TestRunnerMCP()
        {
            EditorApplication.delayCall += Start;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.quitting += Stop;
        }

        private static void Start()
        {
            if (s_Listener != null) return;
            s_UnityVersion = Application.unityVersion;
            s_ProjectName = Application.productName;
            var projectPath = Directory.GetParent(Application.dataPath).FullName;
            var resultDir = Path.Combine(projectPath, "Temp", "TestRunnerMCP");

            s_Jobs = new JobStore(resultDir, () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            s_Service = new TestRunService(s_Jobs, resultDir);
            s_Service.EnsureRegistered(); // 域重载后重建时重注册 TestRunnerApi 回调

            foreach (var port in k_Ports)
            {
                try
                {
                    var listener = new HttpListener();
                    listener.Prefixes.Add("http://127.0.0.1:" + port + "/");
                    listener.Start();
                    s_Listener = listener;
                    s_Port = port;
                    break;
                }
                catch (Exception) { }
            }
            if (s_Listener == null)
            {
                Debug.LogError("[TestRunnerMCP] 21890/21894/21895 全部被占，服务未启动");
                return;
            }

            var thread = new Thread(Loop) { IsBackground = true, Name = "TestRunnerMCP" };
            thread.Start();
            Debug.Log("[TestRunnerMCP] 服务已启动，监听 http://127.0.0.1:" + s_Port + "/");
        }

        private static void Stop()
        {
            if (s_Listener == null) return;
            RecompileHandler.WaitForPendingResponse(2000);
            try { s_Listener.Stop(); s_Listener.Close(); } catch (Exception) { }
            s_Listener = null;
        }

        private static void Loop()
        {
            var listener = s_Listener;
            while (listener != null && listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = listener.GetContext(); }
                catch (Exception) { break; }
                ThreadPool.QueueUserWorkItem(_ => Handle(ctx));
            }
        }

        private static void Handle(HttpListenerContext ctx)
        {
            int status;
            JObject body;
            try
            {
                var path = ctx.Request.Url.AbsolutePath;
                var method = ctx.Request.HttpMethod;
                JObject req = null;
                if (method == "POST")
                {
                    using (var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
                    {
                        var raw = reader.ReadToEnd();
                        req = string.IsNullOrWhiteSpace(raw) ? new JObject() : JObject.Parse(raw);
                    }
                }
                var routed = Route(path, method, req, ctx.Request.Url.Query);
                status = routed.Item1;
                body = routed.Item2;
            }
            catch (JsonException e)
            {
                status = 400;
                body = Err("bad JSON: " + e.Message);
            }
            catch (Exception e)
            {
                status = 500;
                body = Err(e.GetType().Name + ": " + e.Message);
            }
            WriteResponse(ctx, status, body);
        }

        private static Tuple<int, JObject> Route(string path, string method, JObject req, string query)
        {
            switch (path)
            {
                case "/ping": return T(200, Ping());
                case "/recompile":
                    var rc = RecompileHandler.Run(s_Jobs);
                    return T(rc.status, rc.body);
                case "/run-tests":
                    if (method != "POST") return T(400, Err("/run-tests requires POST"));
                    return RunTests(req);
                case "/test-status": return TestStatus(query);
                default: return T(404, Err("unknown endpoint: " + path));
            }
        }

        private static Tuple<int, JObject> T(int s, JObject b) => Tuple.Create(s, b);

        private static JObject Ping()
        {
            s_Jobs.SweepStale(k_StaleJobMs);
            return new JObject
            {
                ["state"] = s_Jobs.State.ToString(),
                ["listening"] = true,
                ["timestamp"] = DateTimeOffset.Now.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz"),
                ["unityVersion"] = s_UnityVersion,
                ["projectName"] = s_ProjectName,
            };
        }

        private static Tuple<int, JObject> RunTests(JObject req)
        {
            FilterSpec spec;
            try { spec = TestFilterBuilder.Parse(req ?? new JObject()); }
            catch (ArgumentException e) { return T(400, Err(e.Message)); }

            if (spec.TestMode == "PlayMode")
                return T(400, Err("PlayMode is not supported in phase 1 (EditMode only)"));

            try
            {
                var jobId = (string)MainThreadDispatcher.Run(() => s_Service.StartRun(spec), out _);
                return T(202, new JObject
                {
                    ["success"] = true,
                    ["jobId"] = jobId,
                    ["status"] = "running",
                    ["message"] = "测试已发起，请轮询 /test-status?jobId=" + jobId,
                });
            }
            catch (InvalidOperationException)
            {
                return T(409, Err("a test run or compilation is already in progress"));
            }
            catch (Exception e)
            {
                return T(400, Err("failed to start run: " + e.Message));
            }
        }

        private static Tuple<int, JObject> TestStatus(string query)
        {
            s_Jobs.SweepStale(k_StaleJobMs);
            var jobId = QueryParam(query, "jobId");
            var rec = string.IsNullOrEmpty(jobId) ? s_Jobs.ActiveOrLast() : s_Jobs.Find(jobId);
            if (rec == null) return T(404, Err("no such job: " + (jobId ?? "<none active or cached>")));
            return T(200, JobJson(rec));
        }

        private static JObject JobJson(JobRecord r)
        {
            var o = new JObject
            {
                ["jobId"] = r.JobId,
                ["status"] = r.Status,
                ["message"] = r.Message,
            };
            if (!string.IsNullOrEmpty(r.OverallResult))
                o["overallResult"] = r.OverallResult; // completed 与 error 都带（error -> "Error"）
            if (r.Status == "completed")
            {
                o["resultFilePath"] = r.ResultFilePath;
                o["passed"] = r.Passed;
                o["failed"] = r.Failed;
                o["skipped"] = r.Skipped;
            }
            return o;
        }

        private static string QueryParam(string query, string key)
        {
            if (string.IsNullOrEmpty(query)) return null;
            var q = query.StartsWith("?") ? query.Substring(1) : query;
            foreach (var pair in q.Split('&'))
            {
                var kv = pair.Split(new[] { '=' }, 2);
                if (kv.Length == 2 && Uri.UnescapeDataString(kv[0]) == key)
                    return Uri.UnescapeDataString(kv[1]);
            }
            return null;
        }

        private static JObject Err(string message) => new JObject { ["success"] = false, ["error"] = message };

        private static void WriteResponse(HttpListenerContext ctx, int status, JObject body)
        {
            var bytes = Encoding.UTF8.GetBytes(body.ToString(Formatting.None));
            try
            {
                ctx.Response.StatusCode = status;
                ctx.Response.ContentType = "application/json; charset=utf-8";
                ctx.Response.ContentLength64 = bytes.Length;
                ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
                ctx.Response.OutputStream.Close();
            }
            catch (Exception) { }
        }
    }
}
