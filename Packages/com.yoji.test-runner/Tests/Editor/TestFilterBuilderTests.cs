using System;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Yoji.TestRunner.Tests
{
    public class TestFilterBuilderTests
    {
        private static FilterSpec P(string json) => TestFilterBuilder.Parse(JObject.Parse(json));

        [Test] public void Parse_EditModeWithTestNames()
        {
            var s = P("{\"testMode\":\"EditMode\",\"testNames\":[\"A.B.C\",\"A.B.D\"]}");
            Assert.AreEqual("EditMode", s.TestMode);
            Assert.AreEqual(2, s.TestNames.Length);
            Assert.IsFalse(s.IsRunAll);
        }

        [Test] public void Parse_MissingTestMode_Throws()
            => Assert.Throws<ArgumentException>(() => P("{\"testNames\":[\"A.B.C\"]}"));

        [Test] public void Parse_InvalidTestMode_Throws()
            => Assert.Throws<ArgumentException>(() => P("{\"testMode\":\"Banana\"}"));

        [Test] public void Parse_EmptyEverything_IsRunAll()
        {
            var s = P("{\"testMode\":\"EditMode\"}");
            Assert.IsTrue(s.IsRunAll);
            Assert.AreEqual(0, s.TestNames.Length);
        }

        [Test] public void Parse_EmptyTestNamesArray_IsRunAll()
        {
            var s = P("{\"testMode\":\"EditMode\",\"testNames\":[]}");
            Assert.IsTrue(s.IsRunAll);
        }

        [Test] public void Parse_WithAssemblyNames_NotRunAll()
        {
            var s = P("{\"testMode\":\"EditMode\",\"assemblyNames\":[\"MyGame.Tests\"]}");
            Assert.IsFalse(s.IsRunAll);
            Assert.AreEqual("MyGame.Tests", s.AssemblyNames[0]);
        }

        [Test] public void Parse_IgnoresBlankNames()
        {
            var s = P("{\"testMode\":\"EditMode\",\"testNames\":[\"\",\"  \",\"A.B.C\"]}");
            Assert.AreEqual(1, s.TestNames.Length);
            Assert.AreEqual("A.B.C", s.TestNames[0]);
        }

        [Test] public void Parse_PlayMode_Accepted_AtParseLayer()
        {
            // 解析层接受 PlayMode；阶段 1 的拒绝在 HTTP handler 层做
            var s = P("{\"testMode\":\"PlayMode\",\"testNames\":[\"A.B.C\"]}");
            Assert.AreEqual("PlayMode", s.TestMode);
        }
    }
}
