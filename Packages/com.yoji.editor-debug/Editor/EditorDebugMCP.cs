using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Yoji.EditorDebug
{
    /// HTTP+JSON 反射调试服务。只绑 127.0.0.1；HTTP 状态码恒 200，错误走 body.error。
    [InitializeOnLoad]
    internal static class EditorDebugMCP
    {
        private const string k_Version = "0.1.0";
        private static readonly bool c_AllowEval = true; // 关闭 /eval 改成 false
        private const int k_MaxBodyBytes = 4 * 1024 * 1024;
        private static readonly int[] k_Ports = { 21891, 21892, 21893 };

        private static HttpListener s_Listener;
        private static int s_Port;
        private static string s_UnityVersion;
        private static string s_ProjectName;

        static EditorDebugMCP()
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
                catch (Exception)
                {
                    // 端口被占，尝试下一个
                }
            }
            if (s_Listener == null)
            {
                Debug.LogError("[EditorDebugMCP] 21891-21893 全部被占，服务未启动");
                return;
            }
            var thread = new Thread(Loop) { IsBackground = true, Name = "EditorDebugMCP" };
            thread.Start();
            Debug.Log("[EditorDebugMCP] 服务已启动，监听 http://127.0.0.1:" + s_Port + "/");
        }

        private static void Stop()
        {
            if (s_Listener == null) return;
            RecompileHandler.WaitForPendingResponse(2000);
            try { s_Listener.Stop(); s_Listener.Close(); }
            catch (Exception) { }
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
            JObject envelope;
            try
            {
                string body;
                using (var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
                    body = reader.ReadToEnd();
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

        private static JObject Ping() => new JObject
        {
            ["service"] = "EditorDebugMCP",
            ["version"] = k_Version,
            ["port"] = s_Port,
            ["unityVersion"] = s_UnityVersion,
            ["projectName"] = s_ProjectName,
        };

        /// 反射执行与结果序列化都必须发生在主线程（结果可能触碰 Unity API）。
        private static JObject RunOnMain(Func<object> work)
        {
            try
            {
                var payload = (JObject)MainThreadDispatcher.Run(() =>
                {
                    var raw = work();
                    var p = new JObject();
                    if (raw is VoidResult) { p["void"] = true; p["result"] = null; }
                    else if (raw is JToken jt) p["result"] = jt; // describe 等已是 JSON
                    else p["result"] = ResultSerializer.ToJson(raw);
                    return p;
                }, out var elapsed);
                payload["ok"] = true;
                payload["elapsedMs"] = elapsed;
                return payload;
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
