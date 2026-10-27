using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Yoji.LuaDeviceDebug
{
    public sealed class LuaDeviceDebugServiceMetadata
    {
        private static readonly LuaDeviceDebugServiceMetadata s_Empty = new LuaDeviceDebugServiceMetadata();

        public string ServiceId { get; private set; }
        public string InstanceId { get; private set; }
        public int ProcessId { get; private set; }
        public string ProjectId { get; private set; }
        public string ProjectRoot { get; private set; }
        public string PortSource { get; private set; }

        private LuaDeviceDebugServiceMetadata()
        {
            ServiceId = LuaDeviceDebugPackage.ServiceId;
            InstanceId = string.Empty;
            ProjectId = string.Empty;
            ProjectRoot = string.Empty;
            PortSource = "unknown";
        }

        public static LuaDeviceDebugServiceMetadata Empty
        {
            get { return s_Empty; }
        }

        public static LuaDeviceDebugServiceMetadata Create(
            string serviceId,
            string instanceId,
            int processId,
            string projectId,
            string projectRoot,
            string portSource)
        {
            return new LuaDeviceDebugServiceMetadata
            {
                ServiceId = serviceId ?? string.Empty,
                InstanceId = instanceId ?? string.Empty,
                ProcessId = processId > 0 ? processId : 0,
                ProjectId = projectId ?? string.Empty,
                ProjectRoot = projectRoot ?? string.Empty,
                PortSource = portSource ?? string.Empty,
            };
        }
    }

    public sealed class LuaDeviceDebugServer : IDisposable
    {
        private readonly int m_Port;
        private readonly int m_TimeoutMs;
        private const int k_MaxHeaderLineBytes = 8192;
        private TcpListener m_Listener;
        private Thread m_Thread;
        private volatile bool m_Running;
        private string m_UnityVersion;
        private string m_AppVersion;
        private string m_Platform;
        private bool m_IsEditor;
        private bool m_IsDevelopmentBuild;
        private LuaDeviceDebugServiceMetadata m_Metadata = LuaDeviceDebugServiceMetadata.Empty;

        public LuaDeviceDebugServer(int port)
            : this(port, 5000)
        {
        }

        public LuaDeviceDebugServer(int port, int timeoutMs)
        {
            m_Port = port;
            m_TimeoutMs = timeoutMs;
        }

        public bool IsRunning
        {
            get { return m_Running; }
        }

        public int Port
        {
            get { return m_Port; }
        }

        public void SetServiceMetadata(LuaDeviceDebugServiceMetadata metadata)
        {
            m_Metadata = metadata ?? LuaDeviceDebugServiceMetadata.Empty;
        }

        public void Start()
        {
            if (m_Running) return;

            MainThreadDispatcher.Initialize();
            m_UnityVersion = Application.unityVersion;
            m_AppVersion = Application.version;
            m_Platform = Application.platform.ToString();
            m_IsEditor = Application.isEditor;
            m_IsDevelopmentBuild = Debug.isDebugBuild;

            m_Listener = new TcpListener(IPAddress.Loopback, m_Port);
            m_Listener.Start();
            m_Running = true;
            m_Thread = new Thread(AcceptLoop);
            m_Thread.IsBackground = true;
            m_Thread.Name = "LuaDeviceDebugServer";
            m_Thread.Start();
        }

        public void Stop()
        {
            if (!m_Running) return;
            m_Running = false;
            try { m_Listener.Stop(); } catch (Exception) { }
            m_Listener = null;
            if (m_Thread != null && m_Thread.IsAlive && Thread.CurrentThread != m_Thread)
                m_Thread.Join(1000);
            m_Thread = null;
        }

        public void Dispose()
        {
            Stop();
        }

        private void AcceptLoop()
        {
            while (m_Running && m_Listener != null)
            {
                try
                {
                    var client = m_Listener.AcceptTcpClient();
                    ThreadPool.QueueUserWorkItem(_ => HandleClient(client));
                }
                catch (SocketException)
                {
                    if (m_Running) Debug.LogWarning("[LuaDeviceDebug] listener socket interrupted");
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
            }
        }

        private void HandleClient(TcpClient client)
        {
            using (client)
            {
                try
                {
                    client.ReceiveTimeout = m_TimeoutMs;
                    client.SendTimeout = m_TimeoutMs;
                    var request = ReadRequest(client.GetStream());
                    var response = Route(request);
                    WriteResponse(client.GetStream(), response.Status, response.Body);
                }
                catch (LuaDeviceDebugException e)
                {
                    WriteResponseSafe(client, e.HttpStatus, ErrorEnvelope(null, e.Code, e.Message));
                }
                catch (JsonException e)
                {
                    WriteResponseSafe(client, 400, ErrorEnvelope(null, "INVALID_REQUEST", e.Message));
                }
                catch (Exception e)
                {
                    WriteResponseSafe(client, 500, ErrorEnvelope(null, "INTERNAL_ERROR", e.Message));
                }
            }
        }

        private DebugHttpResponse Route(DebugHttpRequest request)
        {
            if (request.Method != "POST" && request.Method != "GET")
                return Error(400, null, "INVALID_REQUEST", "unsupported method: " + request.Method);

            JObject body = string.IsNullOrWhiteSpace(request.Body) ? new JObject() : JObject.Parse(request.Body);
            JsonGuard.EnsureWithinLimits(body);
            var requestId = body.Value<string>("requestId");

            switch (request.Path)
            {
                case "/ping":
                    return Ok(requestId, PingJson());
                case "/commands":
                    return Commands(requestId);
                case "/execute":
                    return Execute(requestId, body);
                default:
                    return Error(404, requestId, "INVALID_REQUEST", "unknown endpoint: " + request.Path);
            }
        }

        private JObject PingJson()
        {
            var host = LuaDeviceDebugRuntime.CurrentHost;
            var metadata = m_Metadata ?? LuaDeviceDebugServiceMetadata.Empty;
            return new JObject
            {
                ["service"] = LuaDeviceDebugPackage.ServiceId,
                ["serviceId"] = metadata.ServiceId ?? string.Empty,
                ["instanceId"] = metadata.InstanceId ?? string.Empty,
                ["processId"] = metadata.ProcessId,
                ["projectId"] = metadata.ProjectId ?? string.Empty,
                ["projectRoot"] = metadata.ProjectRoot ?? string.Empty,
                ["portSource"] = metadata.PortSource ?? string.Empty,
                ["version"] = LuaDeviceDebugPackage.Version,
                ["port"] = m_Port,
                ["unityVersion"] = m_UnityVersion,
                ["appVersion"] = m_AppVersion,
                ["platform"] = m_Platform,
                ["isEditor"] = m_IsEditor,
                ["isDevelopmentBuild"] = m_IsDevelopmentBuild,
                ["hostRegistered"] = host != null,
                ["hostReady"] = host != null && host.IsReady,
                ["hostType"] = host != null ? host.GetType().FullName : string.Empty,
            };
        }

        private DebugHttpResponse Commands(string requestId)
        {
            var host = RequireReadyHost();
            var result = MainThreadDispatcher.Run(
                delegate { return ParseJson(host.DescribeCommands()); },
                m_TimeoutMs,
                out var elapsedMs);
            return Ok(requestId, result, elapsedMs);
        }

        private DebugHttpResponse Execute(string requestId, JObject body)
        {
            var command = body.Value<string>("command");
            if (string.IsNullOrEmpty(command))
                return Error(400, requestId, "INVALID_ARGUMENT", "missing command");

            var args = body["args"] ?? new JObject();
            var allowMutation = body.Value<bool?>("allowMutation") ?? false;
            var host = RequireReadyHost();

            var commands = MainThreadDispatcher.Run(
                delegate { return ParseJson(host.DescribeCommands()); },
                m_TimeoutMs,
                out _);
            var mutating = FindMutatingFlag(commands, command);
            if (mutating == null)
                return Error(404, requestId, "COMMAND_NOT_FOUND", "unknown command: " + command);
            if (mutating.Value && !allowMutation)
                return Error(403, requestId, "MUTATION_DENIED", "command requires allowMutation=true: " + command);

            var sw = Stopwatch.StartNew();
            var result = MainThreadDispatcher.Run(
                delegate { return ParseJson(host.Execute(command, args.ToString(Formatting.None), allowMutation)); },
                m_TimeoutMs,
                out _);
            return Ok(requestId, result, sw.ElapsedMilliseconds);
        }

        private ILuaDeviceDebugHost RequireReadyHost()
        {
            var host = LuaDeviceDebugRuntime.CurrentHost;
            if (host == null || !host.IsReady)
                throw new LuaDeviceDebugException(409, "HOST_NOT_READY", "Lua Device Debug host is not ready");
            return host;
        }

        private static JToken ParseJson(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return new JObject();
            var token = JToken.Parse(raw);
            JsonGuard.EnsureWithinLimits(token);
            return token;
        }

        private static bool? FindMutatingFlag(JToken commands, string command)
        {
            var list = commands.Type == JTokenType.Array ? commands : commands["commands"];
            if (list == null || list.Type != JTokenType.Array)
                throw new LuaDeviceDebugException(500, "INVALID_COMMAND_DESCRIPTOR", "host command descriptor must be an array or contain a commands array");

            foreach (var item in list.Children<JObject>())
            {
                var name = item.Value<string>("name");
                if (string.IsNullOrEmpty(name))
                    throw new LuaDeviceDebugException(500, "INVALID_COMMAND_DESCRIPTOR", "command descriptor is missing name");
                if (name == command)
                    return item.Value<bool?>("mutating") ?? false;
            }
            return null;
        }

        private DebugHttpResponse Ok(string requestId, JToken result)
        {
            return Ok(requestId, result, 0);
        }

        private DebugHttpResponse Ok(string requestId, JToken result, long elapsedMs)
        {
            return new DebugHttpResponse(200, new JObject
            {
                ["ok"] = true,
                ["requestId"] = requestId,
                ["elapsedMs"] = elapsedMs,
                ["result"] = result,
            });
        }

        private DebugHttpResponse Error(int status, string requestId, string code, string message)
        {
            return new DebugHttpResponse(status, ErrorEnvelope(requestId, code, message));
        }

        private static JObject ErrorEnvelope(string requestId, string code, string message)
        {
            return new JObject
            {
                ["ok"] = false,
                ["requestId"] = requestId,
                ["elapsedMs"] = 0,
                ["error"] = new JObject
                {
                    ["code"] = code,
                    ["message"] = message,
                },
            };
        }

        private static DebugHttpRequest ReadRequest(Stream stream)
        {
            var requestLine = ReadAsciiLine(stream);
            if (string.IsNullOrEmpty(requestLine))
                throw new LuaDeviceDebugException(400, "INVALID_REQUEST", "missing request line");

            var parts = requestLine.Split(' ');
            if (parts.Length < 2)
                throw new LuaDeviceDebugException(400, "INVALID_REQUEST", "invalid request line");

            var contentLength = 0;
            string line;
            while (!string.IsNullOrEmpty(line = ReadAsciiLine(stream)))
            {
                var colon = line.IndexOf(':');
                if (colon <= 0) continue;
                var key = line.Substring(0, colon).Trim();
                var value = line.Substring(colon + 1).Trim();
                if (string.Equals(key, "Content-Length", StringComparison.OrdinalIgnoreCase))
                {
                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out contentLength) ||
                        contentLength < 0)
                        throw new LuaDeviceDebugException(400, "INVALID_REQUEST", "invalid Content-Length");
                }
            }

            if (contentLength > JsonGuard.MaxRequestBytes)
                throw new LuaDeviceDebugException(413, "INVALID_REQUEST", "request body exceeds limit");

            var body = string.Empty;
            if (contentLength > 0)
            {
                var bytes = ReadExactBytes(stream, contentLength);
                body = Encoding.UTF8.GetString(bytes);
            }

            return new DebugHttpRequest(parts[0], parts[1], body);
        }

        private static string ReadAsciiLine(Stream stream)
        {
            var bytes = new MemoryStream();
            while (true)
            {
                var value = stream.ReadByte();
                if (value < 0)
                {
                    if (bytes.Length == 0) return null;
                    break;
                }
                if (value == '\n')
                    break;
                if (value != '\r')
                {
                    bytes.WriteByte((byte)value);
                    if (bytes.Length > k_MaxHeaderLineBytes)
                        throw new LuaDeviceDebugException(400, "INVALID_REQUEST", "HTTP header line exceeds limit");
                }
            }
            return Encoding.ASCII.GetString(bytes.ToArray());
        }

        private static byte[] ReadExactBytes(Stream stream, int byteCount)
        {
            var bytes = new byte[byteCount];
            var offset = 0;
            while (offset < byteCount)
            {
                var read = stream.Read(bytes, offset, byteCount - offset);
                if (read <= 0)
                    throw new LuaDeviceDebugException(400, "INVALID_REQUEST", "request body ended before Content-Length");
                offset += read;
            }
            return bytes;
        }

        private static void WriteResponseSafe(TcpClient client, int status, JObject body)
        {
            try
            {
                DrainAvailable(client);
                WriteResponse(client.GetStream(), status, body);
            }
            catch (Exception) { }
        }

        private static void DrainAvailable(TcpClient client)
        {
            var stream = client.GetStream();
            var buffer = new byte[1024];
            var remaining = 8192;
            while (client.Available > 0 && remaining > 0)
            {
                var read = stream.Read(buffer, 0, Math.Min(buffer.Length, remaining));
                if (read <= 0) break;
                remaining -= read;
            }
        }

        private static void WriteResponse(Stream stream, int status, JObject body)
        {
            var bytes = Encoding.UTF8.GetBytes(body.ToString(Formatting.None));
            if (bytes.Length > JsonGuard.MaxResponseBytes)
            {
                status = 413;
                body = ErrorEnvelope(null, "RESULT_TOO_LARGE", "response body exceeds limit");
                bytes = Encoding.UTF8.GetBytes(body.ToString(Formatting.None));
            }

            var header =
                "HTTP/1.1 " + status + " " + ReasonPhrase(status) + "\r\n" +
                "Content-Type: application/json; charset=utf-8\r\n" +
                "Content-Length: " + bytes.Length.ToString(CultureInfo.InvariantCulture) + "\r\n" +
                "Connection: close\r\n\r\n";
            var headerBytes = Encoding.ASCII.GetBytes(header);
            stream.Write(headerBytes, 0, headerBytes.Length);
            stream.Write(bytes, 0, bytes.Length);
        }

        private static string ReasonPhrase(int status)
        {
            switch (status)
            {
                case 200: return "OK";
                case 400: return "Bad Request";
                case 403: return "Forbidden";
                case 404: return "Not Found";
                case 408: return "Request Timeout";
                case 409: return "Conflict";
                case 413: return "Payload Too Large";
                case 429: return "Too Many Requests";
                default: return "Internal Server Error";
            }
        }

        private sealed class DebugHttpRequest
        {
            public readonly string Method;
            public readonly string Path;
            public readonly string Body;

            public DebugHttpRequest(string method, string path, string body)
            {
                Method = method;
                Path = path;
                Body = body;
            }
        }

        private sealed class DebugHttpResponse
        {
            public readonly int Status;
            public readonly JObject Body;

            public DebugHttpResponse(int status, JObject body)
            {
                Status = status;
                Body = body;
            }
        }
    }
}
