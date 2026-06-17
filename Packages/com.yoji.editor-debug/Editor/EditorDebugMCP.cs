using System;
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

namespace Yoji.EditorDebug
{
    /// HTTP+JSON 反射调试服务。只绑 127.0.0.1；HTTP 状态码恒 200，错误走 body.error。
    [InitializeOnLoad]
    internal static class EditorDebugMCP
    {
        private const string k_Version = "0.1.0";
        private static readonly bool c_AllowEval = false; // 需要 /eval 时显式改成 true
        private const int k_MaxBodyBytes = 4 * 1024 * 1024;
        private static readonly ServicePortDefinition k_PortDefinition = ServicePortDefinition.Create(
            "unity-editor-debug-mcp",
            "EditorDebugMCP",
            1,
            new int[] { 21891, 21892, 21893 });

        private static HttpListener s_Listener;
        private static int s_Port;
        private static string s_UnityVersion;
        private static string s_ProjectName;
        private static ServiceInstanceRecord s_Record;
        private static double s_NextHeartbeatAt;
        private static double s_NextHeartbeatErrorLogAt;

        static EditorDebugMCP()
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

                var thread = new Thread(Loop) { IsBackground = true, Name = "EditorDebugMCP" };
                thread.Start();

                s_NextHeartbeatAt = EditorApplication.timeSinceStartup + 10.0;
                s_NextHeartbeatErrorLogAt = 0.0;
                EditorApplication.update += Heartbeat;
                Debug.Log("[EditorDebugMCP] 服务已启动，监听 http://127.0.0.1:" + s_Port + "/");
            }
            catch (Exception e)
            {
                Stop();
                Debug.LogError("[EditorDebugMCP] 服务启动失败: " + e.Message);
            }
        }

        private static void Stop()
        {
            EditorApplication.update -= Heartbeat;

            var record = s_Record;
            if (record != null)
            {
                try { ServiceInstanceRegistry.Default().Unregister(record.InstanceId); }
                catch (Exception e) { Debug.LogError("[EditorDebugMCP] 注销全局实例失败: " + e.Message); }

                try { ProjectPortsFile.Remove(record.ProjectRoot, record.InstanceId); }
                catch (Exception e) { Debug.LogError("[EditorDebugMCP] 移除项目端口记录失败: " + e.Message); }
            }

            if (s_Listener != null)
                RecompileHandler.WaitForPendingResponse(2000);

            if (s_Listener != null)
            {
                try { s_Listener.Stop(); s_Listener.Close(); }
                catch (Exception) { }
                s_Listener = null;
            }

            s_Record = null;
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
            JObject envelope;
            try
            {
                var body = RequestBodyReader.ReadUtf8(ctx.Request.InputStream, ctx.Request.ContentLength64);
                var req = string.IsNullOrWhiteSpace(body) ? new JObject() : JObject.Parse(body);
                envelope = Route(ctx.Request.Url.AbsolutePath, req);
            }
            catch (Exception e)
            {
                envelope = ErrorEnvelope(e);
            }
            WriteResponse(ctx, envelope);
        }

        private static JObject Route(string path, JObject req)
        {
            switch (path)
            {
                case "/ping":
                    return new JObject { ["ok"] = true, ["elapsedMs"] = 0, ["result"] = Ping() };
                case "/invoke":
                    return RunOnMain(() => ReflectionInvoker.Execute(req));
                case "/describe":
                    return RunOnMain(() => DescribeHandler.Describe(req.Value<string>("type")));
                case "/console":
                    return RunOnMain(() => ConsoleHandler.Read(req));
                case "/batch":
                    return RunBatch(req);
                case "/eval":
                    if (!c_AllowEval)
                        return ErrorEnvelope(new NotSupportedException("eval is disabled (c_AllowEval=false)"));
                    return RunOnMain(() => EvalParser.Evaluate(req.Value<string>("code")));
                case "/recompile":
                    return RecompileHandler.Run();
                default:
                    return ErrorEnvelope(new ArgumentException("unknown endpoint: " + path));
            }
        }

        private static JObject Ping()
        {
            var record = s_Record;
            var port = record != null ? record.Port : s_Port;
            var unityVersion = s_UnityVersion;
            var projectName = s_ProjectName;
            return new JObject
            {
                ["service"] = "EditorDebugMCP",
                ["serviceId"] = record != null ? record.ServiceId : k_PortDefinition.ServiceId,
                ["instanceId"] = record != null ? record.InstanceId : string.Empty,
                ["processId"] = record != null ? record.ProcessId : Process.GetCurrentProcess().Id,
                ["projectId"] = record != null ? record.ProjectId : string.Empty,
                ["projectRoot"] = record != null ? record.ProjectRoot : string.Empty,
                ["version"] = k_Version,
                ["port"] = port,
                ["portSource"] = record != null ? record.PortSource : "unknown",
                ["unityVersion"] = unityVersion,
                ["projectName"] = projectName,
                // 编辑器态从缓存读（EditorApplication.is* 仅主线程安全，/ping 在 HTTP 线程构信封）
                ["isPlaying"] = EditorStateCache.IsPlaying,
                ["isPaused"] = EditorStateCache.IsPaused,
                ["isCompiling"] = EditorStateCache.IsCompiling,
                ["isUpdating"] = EditorStateCache.IsUpdating,
                ["timeSinceStartup"] = EditorStateCache.TimeSinceStartup,
            };
        }

        /// 反射执行与结果序列化都必须发生在主线程（结果可能触碰 Unity API）。
        private static JObject RunOnMain(Func<object> work)
        {
            try
            {
                var payload = (JObject)MainThreadDispatcher.Run(() => ResultSerializer.ToPayload(work()), out var elapsed);
                payload["ok"] = true;
                payload["elapsedMs"] = elapsed;
                return payload;
            }
            catch (Exception e)
            {
                return ErrorEnvelope(e);
            }
        }

        /// /batch：N 个 invoke 子请求在单次主线程跳内顺序执行，逐项独立 ok，单条失败不中止整批。
        /// 仅放读类调用；>64 拒（4MB 信封上限下大批量会整体 __truncated，得不偿失）。
        private static JObject RunBatch(JObject req)
        {
            if (!(req["requests"] is JArray requests))
                return ErrorEnvelope(new ArgumentException("batch requires a 'requests' array"));
            if (requests.Count > 64)
                return ErrorEnvelope(new ArgumentException("batch too large (" + requests.Count + " > 64); split it"));
            try
            {
                var results = (JArray)MainThreadDispatcher.Run(() =>
                {
                    var arr = new JArray();
                    foreach (var sub in requests)
                    {
                        try
                        {
                            var payload = ResultSerializer.ToPayload(ReflectionInvoker.Execute((JObject)sub));
                            payload["ok"] = true;
                            arr.Add(payload);
                        }
                        catch (Exception e)
                        {
                            arr.Add(new JObject { ["ok"] = false, ["error"] = ErrorJson(e) });
                        }
                    }
                    return arr;
                }, out var elapsed);
                return new JObject { ["ok"] = true, ["elapsedMs"] = elapsed, ["results"] = results };
            }
            catch (Exception e)
            {
                return ErrorEnvelope(e);
            }
        }

        private static JObject ErrorEnvelope(Exception e) => new JObject
        {
            ["ok"] = false,
            ["elapsedMs"] = 0,
            ["error"] = ErrorJson(e),
        };

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
                    Debug.LogError("[EditorDebugMCP] 心跳刷新失败: " + e.Message);
                }
            }
        }

        private static JObject ErrorJson(Exception e)
        {
            var o = new JObject
            {
                ["type"] = e.GetType().FullName,
                ["message"] = e.Message,
                ["stack"] = e.StackTrace,
            };
            if (e.InnerException != null) o["inner"] = ErrorJson(e.InnerException);
            return o;
        }

        private static void WriteResponse(HttpListenerContext ctx, JObject envelope)
        {
            var bytes = Encoding.UTF8.GetBytes(envelope.ToString(Formatting.None));
            if (bytes.Length > k_MaxBodyBytes)
            {
                envelope["result"] = new JObject { ["__truncated"] = true, ["reason"] = "bodySize" };
                envelope["__truncated"] = true;
                envelope["fullSize"] = bytes.Length;
                bytes = Encoding.UTF8.GetBytes(envelope.ToString(Formatting.None));
            }
            try
            {
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = "application/json; charset=utf-8";
                ctx.Response.ContentLength64 = bytes.Length;
                ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
                ctx.Response.OutputStream.Close();
            }
            catch (Exception)
            {
                // 客户端已断开
            }
        }
    }
}
