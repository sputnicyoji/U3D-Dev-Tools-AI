using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Yoji.EditorDebug.Tests
{
    public class DescribeHandlerTests
    {
        [Test]
        public void Describe_PublicType_HasFullNameAndMethods()
        {
            var r = DescribeHandler.Describe("UnityEditor.Selection");
            Assert.AreEqual("UnityEditor.Selection", (string)r["FullName"]);
            Assert.IsTrue(((JArray)r["Methods"]).Count > 0);
            Assert.IsNotNull(r["Properties"]);
            Assert.IsNotNull(r["Fields"]);
        }

        [Test]
        public void Describe_InternalType_Works()
        {
            var r = DescribeHandler.Describe("UnityEditorInternal.ProfilerDriver");
            Assert.AreEqual("UnityEditorInternal.ProfilerDriver", (string)r["FullName"]);
        }

        [Test]
        public void Describe_MethodSignature_ContainsAccessAndStatic()
        {
            var r = DescribeHandler.Describe("UnityEngine.Mathf");
            var methods = ((JArray)r["Methods"]).Select(x => (string)x).ToList();
            Assert.IsTrue(methods.Any(m => m.StartsWith("public static") && m.Contains("Min(")),
                "expect a 'public static ... Min(...)' signature, got: " + string.Join(" | ", methods.Take(5)));
        }

        [Test]
        public void Describe_Unknown_ThrowsTypeAccessException()
            => Assert.Throws<TypeAccessException>(() => DescribeHandler.Describe("Foo.Bar.Whatever.For.E2E"));
    }
}
