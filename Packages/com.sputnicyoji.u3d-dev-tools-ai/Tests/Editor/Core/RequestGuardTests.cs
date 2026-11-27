using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Yoji.EditorCore.Tests
{
    public sealed class RequestGuardTests
    {
        private static readonly HashSet<string> s_Mutating = new HashSet<string>(StringComparer.Ordinal) { "/recompile" };

        [TestCase("http://evil.example")]
        [TestCase("null")]
        public void Check_AnyOrigin_IsRejected(string origin)
        {
            Assert.AreEqual(RequestGuard.CrossOriginRejected, RequestGuard.Check("POST", "/recompile", origin, s_Mutating));
            Assert.AreEqual(RequestGuard.CrossOriginRejected, RequestGuard.Check("GET", "/ping", origin, s_Mutating));
        }

        [TestCase(null)]
        [TestCase("")]
        public void Check_NoOrigin_PostToMutatingRoute_IsAllowed(string origin)
        {
            Assert.IsNull(RequestGuard.Check("POST", "/recompile", origin, s_Mutating));
        }

        [TestCase("GET")]
        [TestCase("HEAD")]
        [TestCase("post")]
        public void Check_NonPostOnMutatingRoute_IsRejected(string method)
        {
            Assert.AreEqual("/recompile requires POST", RequestGuard.Check(method, "/recompile", null, s_Mutating));
        }

        [TestCase("GET")]
        [TestCase("POST")]
        public void Check_ReadOnlyRoute_AcceptsGetAndPost(string method)
        {
            Assert.IsNull(RequestGuard.Check(method, "/ping", null, s_Mutating));
        }
    }
}
