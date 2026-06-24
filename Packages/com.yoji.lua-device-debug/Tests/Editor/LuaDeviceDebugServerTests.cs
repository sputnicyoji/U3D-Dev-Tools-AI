using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Yoji.LuaDeviceDebug.Tests
{
    public sealed class LuaDeviceDebugServerTests
    {
        private LuaDeviceDebugServer m_Server;
        private FakeHost m_Host;
        private int m_Port;

        [SetUp]
        public void SetUp()
        {
            m_Port = FindFreePort();
            m_Host = new FakeHost();
            LuaDeviceDebugRuntime.RegisterHost(m_Host);
            m_Server = new LuaDeviceDebugServer(m_Port);
            m_Server.Start();
        }

        [TearDown]
        public void TearDown()
        {
            if (m_Server != null)
            {
                m_Server.Stop();
                m_Server = null;
            }
            LuaDeviceDebugRuntime.UnregisterHost(m_Host);
        }

        [Test]
        public void Ping_ReturnsServiceInfo()
        {
            var response = Post("/ping", "{}");

            Assert.AreEqual(200, response.Status);
            Assert.IsTrue(response.Body.Value<bool>("ok"));
            Assert.AreEqual(LuaDeviceDebugPackage.ServiceId, response.Body["result"].Value<string>("service"));
            Assert.AreEqual(m_Port, response.Body["result"].Value<int>("port"));
        }

        [UnityTest]
        public IEnumerator Commands_ReturnsHostDescriptors()
        {
            HttpResult response = null;
            yield return PostAsync("/commands", "{}", delegate(HttpResult value) { response = value; });

            Assert.AreEqual(200, response.Status);
            Assert.AreEqual("system.info", response.Body["result"]["commands"][0].Value<string>("name"));
        }

        [UnityTest]
        public IEnumerator Execute_ReadOnlyCommand_ReturnsHostResult()
        {
            HttpResult response = null;
            yield return PostAsync("/execute", "{\"command\":\"system.info\",\"args\":{\"id\":1001}}", delegate(HttpResult value) { response = value; });

            Assert.AreEqual(200, response.Status);
            Assert.IsTrue(response.Body.Value<bool>("ok"));
            Assert.AreEqual("system.info", response.Body["result"].Value<string>("command"));
            Assert.AreEqual(1001, response.Body["result"]["args"].Value<int>("id"));
        }

        [UnityTest]
        public IEnumerator Execute_NonAsciiArgs_UsesUtf8ContentLengthBytes()
        {
            HttpResult response = null;
            yield return PostAsync("/execute", "{\"command\":\"system.info\",\"args\":{\"text\":\"中文\"}}", delegate(HttpResult value) { response = value; });

            Assert.AreEqual(200, response.Status);
            Assert.IsTrue(response.Body.Value<bool>("ok"));
            Assert.AreEqual("中文", response.Body["result"]["args"].Value<string>("text"));
        }

        [UnityTest]
        public IEnumerator Execute_MutatingCommandWithoutAllow_ReturnsForbidden()
        {
            HttpResult response = null;
            yield return PostAsync("/execute", "{\"command\":\"state.reset\",\"args\":{}}", delegate(HttpResult value) { response = value; });

            Assert.AreEqual(403, response.Status);
            Assert.IsFalse(response.Body.Value<bool>("ok"));
            Assert.AreEqual("MUTATION_DENIED", response.Body["error"].Value<string>("code"));
        }

        [UnityTest]
        public IEnumerator Execute_InvalidCommandDescriptor_ReturnsErrorWithoutExecuting()
        {
            m_Host.CommandsJson = "{}";
            HttpResult response = null;
            yield return PostAsync("/execute", "{\"command\":\"system.info\",\"args\":{}}", delegate(HttpResult value) { response = value; });

            Assert.AreEqual(500, response.Status);
            Assert.IsFalse(response.Body.Value<bool>("ok"));
            Assert.AreEqual("INVALID_COMMAND_DESCRIPTOR", response.Body["error"].Value<string>("code"));
            Assert.AreEqual(0, m_Host.ExecuteCount);
        }

        [UnityTest]
        public IEnumerator Commands_TimeoutBeforeMainThreadRuns_SkipsQueuedWork()
        {
            m_Server.Stop();
            m_Server = new LuaDeviceDebugServer(m_Port, 50);
            m_Server.Start();

            var response = Post("/commands", "{}");
            Assert.AreEqual(408, response.Status);

            yield return null;
            yield return null;

            Assert.AreEqual(0, m_Host.DescribeCount);
        }

        [Test]
        public void MalformedContentLength_ReturnsBadRequest()
        {
            var response = RawRequest(
                "POST /ping HTTP/1.1\r\n" +
                "Host: 127.0.0.1\r\n" +
                "Content-Length: nope\r\n" +
                "\r\n");

            Assert.AreEqual(400, response.Status);
            Assert.AreEqual("INVALID_REQUEST", response.Body["error"].Value<string>("code"));
        }

        [Test]
        public void OversizedHeaderLine_ReturnsBadRequest()
        {
            var response = RawRequest(
                "POST /ping HTTP/1.1\r\n" +
                "Host: 127.0.0.1\r\n" +
                "X-Large: " + new string('x', 9000) + "\r\n" +
                "Content-Length: 2\r\n" +
                "\r\n{}");

            Assert.AreEqual(400, response.Status);
            Assert.AreEqual("INVALID_REQUEST", response.Body["error"].Value<string>("code"));
        }

        private IEnumerator PostAsync(string path, string json, Action<HttpResult> onResult)
        {
            Exception error = null;
            var done = false;
            var thread = new Thread(delegate()
            {
                try { onResult(Post(path, json)); }
                catch (Exception e) { error = e; }
                done = true;
            });
            thread.IsBackground = true;
            thread.Start();

            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!done && DateTime.UtcNow < deadline)
                yield return null;

            if (!done)
                Assert.Fail("HTTP request did not complete");
            if (error != null)
                throw error;
        }

        private HttpResult Post(string path, string json)
        {
            var request = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + m_Port + path);
            request.Method = "POST";
            request.ContentType = "application/json; charset=utf-8";
            var bytes = Encoding.UTF8.GetBytes(json);
            request.ContentLength = bytes.Length;
            using (var stream = request.GetRequestStream())
                stream.Write(bytes, 0, bytes.Length);

            try
            {
                using (var response = (HttpWebResponse)request.GetResponse())
                    return ReadResponse(response);
            }
            catch (WebException e)
            {
                using (var response = (HttpWebResponse)e.Response)
                    return ReadResponse(response);
            }
        }

        private HttpResult RawRequest(string raw)
        {
            using (var client = new TcpClient())
            {
                client.Connect(IPAddress.Loopback, m_Port);
                client.ReceiveTimeout = 5000;
                client.SendTimeout = 5000;
                var bytes = Encoding.ASCII.GetBytes(raw);
                using (var stream = client.GetStream())
                {
                    stream.Write(bytes, 0, bytes.Length);
                    client.Client.Shutdown(SocketShutdown.Send);
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        var statusLine = reader.ReadLine();
                        Assert.IsNotNull(statusLine);
                        var parts = statusLine.Split(' ');
                        var status = int.Parse(parts[1]);

                        string line;
                        while (!string.IsNullOrEmpty(line = reader.ReadLine())) { }

                        var body = reader.ReadToEnd();
                        return new HttpResult(status, JObject.Parse(body));
                    }
                }
            }
        }

        private static HttpResult ReadResponse(HttpWebResponse response)
        {
            using (var reader = new StreamReader(response.GetResponseStream()))
            {
                return new HttpResult((int)response.StatusCode, JObject.Parse(reader.ReadToEnd()));
            }
        }

        private static int FindFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private sealed class HttpResult
        {
            public readonly int Status;
            public readonly JObject Body;

            public HttpResult(int status, JObject body)
            {
                Status = status;
                Body = body;
            }
        }
    }
}
