using System;
using System.Collections;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Yoji.EditorDebug.Tests
{
    public class ConsoleHandlerTests
    {
        // 实跑 LogEntries 反射路径：记一条 Error 再读回，验证捕获 + mode 位掩码分类为 Error。
        // 直接调 handler（不走 HTTP），不触发 in-process HTTP 死锁。
        [UnityTest]
        public IEnumerator Read_CapturesLoggedError_ClassifiesAsError()
        {
            string marker = "CONSOLE_MCP_MARKER_" + Guid.NewGuid().ToString("N");
            LogAssert.Expect(LogType.Error, marker); // 消费该 Error，避免它判挂测试
            Debug.LogError(marker);

            JObject match = null;
            for (int frame = 0; frame < 30 && match == null; frame++)
            {
                yield return null;
                var result = ConsoleHandler.Read(new JObject { ["count"] = 1000, ["filter"] = "error" }) as JObject;
                Assert.IsNotNull(result, "Read 未返回 JObject");
                Assert.IsNull(result["__unavailable"], "LogEntries 反射不可用: " + result["__unavailable"]);
                if (!(result["entries"] is JArray entries)) continue;
                foreach (var e in entries)
                {
                    var msg = (string)e["message"];
                    if (msg != null && msg.Contains(marker)) { match = (JObject)e; break; }
                }
            }
            Assert.IsNotNull(match, "记录的 marker 未在 console 条目中找到");
            Assert.AreEqual("Error", (string)match["type"], "mode 位掩码应分类为 Error");
        }
    }
}
