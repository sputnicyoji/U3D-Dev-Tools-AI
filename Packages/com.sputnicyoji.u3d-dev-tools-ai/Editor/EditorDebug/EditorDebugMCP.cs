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
using Yoji.EditorDebug.Ui;
using Process = System.Diagnostics.Process;

namespace Yoji.EditorDebug
{
    /// HTTP+JSON 反射调试服务。只绑 127.0.0.1；HTTP 状态码恒 200，错误走 body.error。
    [InitializeOnLoad]
    internal static class EditorDebugMCP
    {
        private const string k_Version = "0.1.4";
        private static readonly bool c_AllowEval = false; // 需要 /eval 时显式改成 true
        private const int k_MaxBodyBytes = 4 * 1024 * 1024;
        private static readonly ServicePortDefinition k_PortDefinition = ServicePortDefinition.Create(
            "unity-editor-debug-mcp",
            "EditorDebugMCP",
            1,
            new int[] { 21891, 21892, 21893 });

        private static HttpListener s_Listener;
        private static Thread s_Thread;
        private static int s_Port;
        private static string s_UnityVersion;
        private static string s_ProjectName;
        private static EditorServiceEndpoint s_Endpoint;
        private static double s_NextHeartbeatAt;
        private static double s_NextHeartbeatErrorLogAt;

        internal static ServicePortDefinition PortDefinitionForTests
        {
            get { return k_PortDefinition; }
        }

        static EditorDebugMCP()
        {
            EditorServiceLifecycle.Bind(Start, Stop);
        }

        private static void Start()
        {
            if (s_Endpoint != null)
            {
                s_Endpoint.Heartbeat();
                return;
            }

            try
            {
                s_UnityVersion = Application.unityVersion;
                s_ProjectName = Application.productName;

                s_Endpoint = EditorServiceEndpoint.Start(k_PortDefinition, StartHttpListener);

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

        private static IDisposable StartHttpListener(ServicePortAssignment assignment)
        {
            var listener = new HttpListener();
            listener.Prefixes.Add("http://127.0.0.1:" + assignment.Port + "/");
            listener.Start();
            s_Listener = listener;
            s_Port = assignment.Port;

            s_Thread = new Thread(Loop) { IsBackground = true, Name = "EditorDebugMCP" };
            s_Thread.Start();

            return new CallbackDisposable(StopHttpListenerOnly);
        }

        private static void Stop()
        {
            EditorApplication.update -= Heartbeat;

            if (s_Endpoint != null)
            {
                s_Endpoint.Dispose();
                s_Endpoint = null;
            }

            s_Port = 0;
            s_UnityVersion = null;
            s_ProjectName = null;
            s_NextHeartbeatAt = 0.0;
            s_NextHeartbeatErrorLogAt = 0.0;
        }

        private static void StopHttpListenerOnly()
        {
            if (s_Listener != null)
                RecompileHandler.WaitForPendingResponse(2000);

            if (s_Listener != null)
            {
                try { s_Listener.Stop(); s_Listener.Close(); }
                catch (Exception) { }
                s_Listener = null;
            }

            s_Thread = null;
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
                // 浏览器跨站请求必带 Origin；本服务只给本机 CLI/agent 用，带 Origin 一律拒（防网页 CSRF 打 loopback）。
                // 不校验 Content-Type：现有调用方有 curl -d（form 编码）的用法。
                // DNS rebinding 由 HttpListener 前缀兜住：Host 不是 127.0.0.1 时监听器直接回 400 Invalid host（实测）。
                if (!string.IsNullOrEmpty(ctx.Request.Headers["Origin"]))
                    throw new UnauthorizedAccessException("cross-origin request rejected");
                var path = ctx.Request.Url.AbsolutePath;
                GuardUiWrite(path, ctx.Request.HttpMethod, ctx.Request.ContentType);
                var body = RequestBodyReader.ReadUtf8(ctx.Request.InputStream, ctx.Request.ContentLength64);
                var req = string.IsNullOrWhiteSpace(body) ? new JObject() : JObject.Parse(body);
                // 只给只读的 /ui/ 端点开 GET 查询参数：跨站 <img> GET 不带 Origin，对 /invoke、/ui/act 开放就能触发调用或点击
                if (path == "/ui/snapshot" || path == "/ui/hit")
                    MergeQuery(ctx.Request.QueryString, req);
                envelope = Route(path, req);
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
                    if (req.Value<bool?>("defer") == true)
                        return RunDeferred(req);
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
                case "/ui/snapshot":
                    return RunOnMain(() => UiObserver.Snapshot(req));
                case "/ui/hit":
                    return RunOnMain(() => UiObserver.Hit(req));
                case "/ui/act":
                    if (UiWorldActor.IsWorldRequest(req))
                        return RunWorldAct(req);
                    return RunOnMain(() => UiActor.Act(req, live: true));
                default:
                    return ErrorEnvelope(new ArgumentException("unknown endpoint: " + path));
            }
        }

        /// /ui/act 是本服务第一个 UI 写操作，只收 POST + application/json：跨站页面发 JSON 必先 CORS 预检，
        /// 预检带 Origin 已被拒；form/text 这类免预检的简单请求在这里挡掉。
        internal static void GuardUiWrite(string path, string method, string contentType)
        {
            if (path != "/ui/act")
                return;
            if (!string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("ui/act only accepts POST");
            if (contentType == null || !contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("ui/act requires Content-Type: application/json");
        }

        /// GET 查询参数并入请求体（body 同名字段优先），让 /ui/hit?x=&y= 与 POST JSON 两种写法等价。
        internal static void MergeQuery(System.Collections.Specialized.NameValueCollection query, JObject req)
        {
            foreach (string key in query)
            {
                if (key != null && req[key] == null)
                    req[key] = query[key];
            }
        }

        private static JObject Ping()
        {
            var record = s_Endpoint != null ? s_Endpoint.Record : null;
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

        private const int k_WorldActTimeoutMs = 20000;

        /// 世界目标的 act 跨多帧：主线程上排好计划并接管指针，HTTP 线程等会话跑完（编辑器 update 逐帧推进），
        /// 再回主线程取结果。超时（如 Play 被暂停）就取消会话、交还指针并回 reason=timeout。
        private static JObject RunWorldAct(JObject req)
        {
            UiWorldActor.Session session = null;
            var started = RunOnMain(() =>
                (object)UiWorldActor.Start(req, live: true, ScriptedPointerRegistry.Current, null, out session) ?? new JObject());
            if (session == null)
                return started;
            if (!session.Done.Wait(k_WorldActTimeoutMs))
            {
                return RunOnMain(() =>
                {
                    session.Cancel("timeout", "the gesture did not finish within " + k_WorldActTimeoutMs + "ms (is Play paused?)");
                    return session.Result;
                });
            }
            return RunOnMain(() => session.Result);
        }
        /// defer=true：主线程跳内只把执行排进 delayCall（下一 editor tick），响应先写回、动作后执行。
        /// 用于会触发 domain reload / play transition 的调用（EnterPlaymode / ExitPlaymode / 刷新导入等），
        /// 避免"响应写回"与 reload 撕裂互等。结果不回传；失败 LogError 落 Console，客户端用 /console 或 /ping 追认。
        private static JObject RunDeferred(JObject req)
        {
            try
            {
                MainThreadDispatcher.Run(() =>
                {
                    EditorApplication.delayCall += () =>
                    {
                        try { ReflectionInvoker.Execute(req); }
                        catch (Exception e) { Debug.LogError("[EditorDebugMCP] deferred invoke failed: " + e.Message); }
                    };
                    return null;
                }, out var elapsed);
                return new JObject
                {
                    ["ok"] = true,
                    ["elapsedMs"] = elapsed,
                    ["deferred"] = true,
                    ["result"] = new JObject { ["queued"] = true },
                };
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
            if (s_Endpoint == null)
                return;

            var now = EditorApplication.timeSinceStartup;
            if (now < s_NextHeartbeatAt)
                return;

            s_NextHeartbeatAt = now + 10.0;

            try
            {
                s_Endpoint.Heartbeat(true);
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
