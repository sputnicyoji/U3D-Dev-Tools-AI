using System;
using System.Collections.Specialized;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Yoji.EditorCore.Ports;
using Yoji.EditorCore;
using Process = System.Diagnostics.Process;

namespace Yoji.TestRunner
{
    /// TestRunnerMCP HTTP 服务。端口 21890（fallback 21896/21897），只绑 127.0.0.1。
    /// 真 HTTP 状态码 + 每端点独立 JSON 形状（与 editor-debug 恒 200 flat envelope 不同）。
    /// EditMode 直接跑；PlayMode 经临时叠加 EnterPlayModeOptions.DisableDomainReload 跑（TR-1a），run 结束还原用户设置。
    [InitializeOnLoad]
    internal static class TestRunnerMCP
    {
        private const int k_StaleJobMs = 600_000; // 孤儿任务清扫阈值
        private static readonly ServicePortDefinition k_PortDefinition = ServicePortDefinition.Create(
            "test-runner-mcp",
            "TestRunnerMCP",
            0,
            new int[] { 21890, 21896, 21897 });

        private static HttpListener s_Listener;
        private static int s_Port;
        private static string s_UnityVersion;
        private static string s_ProjectName;
        private static JobStore s_Jobs;
        private static TestRunService s_Service;
        private static ServiceInstanceRecord s_Record;
        private static double s_NextHeartbeatAt;
        private static double s_NextHeartbeatErrorLogAt;

        static TestRunnerMCP()
        {
            EditorServiceLifecycle.Bind(Start, Stop);
        }

        private static void Start()
        {
            if (s_Listener != null) return;
            try
            {
                var identity = ProjectIdentityProvider.Current();
                var policy = ServicePortSettingsStore.BuildPolicy(identity.ProjectRoot, identity);
                var assignment = ServicePortAllocator.Allocate(k_PortDefinition, policy, new TcpPortProbe());

                s_UnityVersion = Application.unityVersion;
                s_ProjectName = Application.productName;
                var projectPath = Directory.GetParent(Application.dataPath).FullName;
                var resultDir = Path.Combine(projectPath, "Temp", "TestRunnerMCP");

                s_Jobs = new JobStore(resultDir, () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                s_Service = new TestRunService(s_Jobs, resultDir);
                s_Service.EnsureRegistered(); // 域重载后重建时重注册 TestRunnerApi 回调

                var listener = new HttpListener();
                listener.Prefixes.Add("http://127.0.0.1:" + assignment.Port + "/");
                listener.Start();
                s_Listener = listener;
                s_Port = assignment.Port;

                s_Record = ServiceInstanceRecord.Create(
                    assignment.ServiceId,
                    assignment.DisplayName,
                    assignment.ProjectRoot,
                    assignment.ProjectId,
                    Process.GetCurrentProcess().Id,
                    assignment.Port,
                    assignment.Source);
                ServiceInstanceRegistry.Default().Register(s_Record);
                ProjectPortsFile.Upsert(s_Record.ProjectRoot, s_Record);

                var thread = new Thread(Loop) { IsBackground = true, Name = "TestRunnerMCP" };
                thread.Start();

                s_NextHeartbeatAt = EditorApplication.timeSinceStartup + 10.0;
                s_NextHeartbeatErrorLogAt = 0.0;
                EditorApplication.update += Heartbeat;
                Debug.Log("[TestRunnerMCP] 服务已启动，监听 http://127.0.0.1:" + s_Port + "/");
            }
            catch (Exception e)
            {
                Stop();
                Debug.LogError("[TestRunnerMCP] 服务启动失败: " + e.Message);
            }
        }

        private static void Stop()
        {
            EditorApplication.update -= Heartbeat;

            var record = s_Record;
            if (record != null)
            {
                try { ServiceInstanceRegistry.Default().Unregister(record.InstanceId); }
                catch (Exception e) { Debug.LogError("[TestRunnerMCP] 注销全局实例失败: " + e.Message); }

                try { ProjectPortsFile.Remove(record.ProjectRoot, record.InstanceId); }
                catch (Exception e) { Debug.LogError("[TestRunnerMCP] 移除项目端口记录失败: " + e.Message); }
            }

            if (s_Listener != null)
                RecompileHandler.WaitForPendingResponse(2000);

            if (s_Listener != null)
            {
                try { s_Listener.Stop(); s_Listener.Close(); } catch (Exception) { }
                s_Listener = null;
            }

            s_Record = null;
            s_Jobs = null;
            s_Service = null;
            s_Port = 0;
            s_UnityVersion = null;
            s_ProjectName = null;
            s_NextHeartbeatAt = 0.0;
            s_NextHeartbeatErrorLogAt = 0.0;
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
                    var raw = RequestBodyReader.ReadUtf8(ctx.Request.InputStream, ctx.Request.ContentLength64);
                    req = string.IsNullOrWhiteSpace(raw) ? new JObject() : JObject.Parse(raw);
                }
                var jobs = s_Jobs;
                var service = s_Service;
                (status, body) = Route(path, method, req, ctx.Request.QueryString, jobs, service);
            }
            catch (JsonException e)
            {
                status = 400;
                body = Err("bad JSON: " + e.Message);
            }
            catch (InvalidDataException e)
            {
                status = 413;
                body = Err(e.Message);
            }
            catch (Exception e)
            {
                status = 500;
                body = Err(e.GetType().Name + ": " + e.Message);
            }
            WriteResponse(ctx, status, body);
        }

        private static (int status, JObject body) Route(
            string path,
            string method,
            JObject req,
            NameValueCollection query,
            JobStore jobs,
            TestRunService service)
        {
            switch (path)
            {
                case "/ping":
                    if (jobs == null) return (503, Err("service is stopping"));
                    return (200, Ping(jobs));
                case "/recompile":
                    if (jobs == null) return (503, Err("service is stopping"));
                    return RecompileHandler.Run(jobs);
                case "/run-tests":
                    if (method != "POST") return (400, Err("/run-tests requires POST"));
                    if (service == null) return (503, Err("service is stopping"));
                    return RunTests(req, service);
                case "/test-status":
                    if (jobs == null) return (503, Err("service is stopping"));
                    return TestStatus(query["jobId"], jobs);
                case "/list-tests":
                    if (service == null) return (503, Err("service is stopping"));
                    return ListTests(query["mode"], service);
                default: return (404, Err("unknown endpoint: " + path));
            }
        }

        private static JObject Ping(JobStore jobs)
        {
            jobs.SweepStale(k_StaleJobMs);
            var record = s_Record;
            var port = record != null ? record.Port : s_Port;
            var unityVersion = s_UnityVersion;
            var projectName = s_ProjectName;
            return new JObject
            {
                ["service"] = k_PortDefinition.DisplayName,
                ["state"] = jobs.State.ToString(),
                ["listening"] = true,
                ["timestamp"] = DateTimeOffset.Now.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz"),
                ["unityVersion"] = unityVersion,
                ["projectName"] = projectName,
                ["serviceId"] = record != null ? record.ServiceId : k_PortDefinition.ServiceId,
                ["instanceId"] = record != null ? record.InstanceId : string.Empty,
                ["processId"] = record != null ? record.ProcessId : Process.GetCurrentProcess().Id,
                ["projectId"] = record != null ? record.ProjectId : string.Empty,
                ["projectRoot"] = record != null ? record.ProjectRoot : string.Empty,
                ["port"] = port,
                ["portSource"] = record != null ? record.PortSource : "unknown",
            };
        }

        private static (int status, JObject body) RunTests(JObject req, TestRunService service)
        {
            FilterSpec spec;
            try { spec = TestFilterBuilder.Parse(req ?? new JObject()); }
            catch (ArgumentException e) { return (400, Err(e.Message)); }

            try
            {
                var jobId = (string)MainThreadDispatcher.Run(() => service.StartRun(spec), out _);
                return (202, new JObject
                {
                    ["success"] = true,
                    ["jobId"] = jobId,
                    ["status"] = "running",
                    ["message"] = "测试已发起，请轮询 /test-status?jobId=" + jobId,
                });
            }
            catch (InvalidOperationException e)
            {
                // 含：run/编译进行中（JobStore）、已在 Play 中、脏场景未存（PlayMode 前置守卫）。
                return (409, Err(e.Message));
            }
            catch (Exception e)
            {
                return (400, Err("failed to start run: " + e.Message));
            }
        }

        private static (int status, JObject body) TestStatus(string jobId, JobStore jobs)
        {
            jobs.SweepStale(k_StaleJobMs);
            var rec = string.IsNullOrEmpty(jobId) ? jobs.ActiveOrLast() : jobs.Find(jobId);
            if (rec == null) return (404, Err("no such job: " + (jobId ?? "<none active or cached>")));
            return (200, JobJson(rec));
        }

        // 发现端点：列出可跑的测试用例全名（EditMode 或 PlayMode；仅枚举，不进 Play）。
        private static (int status, JObject body) ListTests(string mode, TestRunService service)
        {
            var m = string.IsNullOrEmpty(mode) ? "EditMode" : mode;
            if (m != "EditMode" && m != "PlayMode")
                return (400, Err("mode '" + m + "' must be EditMode or PlayMode"));
            try
            {
                var names = service.ListTests(m);
                var arr = new JArray();
                foreach (var n in names) arr.Add(n);
                return (200, new JObject { ["tests"] = arr, ["count"] = names.Count });
            }
            catch (Exception e)
            {
                return (500, Err("list-tests failed: " + e.Message));
            }
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
                if (r.Failures != null && r.Failures.Count > 0)
                {
                    var arr = new JArray();
                    foreach (var f in r.Failures)
                        arr.Add(new JObject
                        {
                            ["name"] = f.Name,
                            ["message"] = f.Message,
                            ["stackTrace"] = f.StackTrace,
                        });
                    o["failures"] = arr;
                }
            }
            return o;
        }

        private static JObject Err(string message) => new JObject { ["success"] = false, ["error"] = message };

        private static void Heartbeat()
        {
            if (s_Record == null)
                return;

            var now = EditorApplication.timeSinceStartup;
            if (now < s_NextHeartbeatAt)
                return;

            s_NextHeartbeatAt = now + 10.0;

            try
            {
                s_Record.Touch();
                ServiceInstanceRegistry.Default().Register(s_Record);
                ProjectPortsFile.Upsert(s_Record.ProjectRoot, s_Record);
            }
            catch (Exception e)
            {
                if (now >= s_NextHeartbeatErrorLogAt)
                {
                    s_NextHeartbeatErrorLogAt = now + 60.0;
                    Debug.LogError("[TestRunnerMCP] 心跳刷新失败: " + e.Message);
                }
            }
        }

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
