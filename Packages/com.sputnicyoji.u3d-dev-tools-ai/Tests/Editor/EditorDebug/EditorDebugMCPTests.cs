using System.IO;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using Yoji.EditorCore;

namespace Yoji.EditorDebug.Tests
{
    public sealed class EditorDebugMCPTests
    {
        [Test]
        public void Eval_IsEnabledByDefault()
        {
            var field = typeof(EditorDebugMCP).GetField("c_AllowEval", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(field);
            Assert.IsTrue((bool)field.GetValue(null));
        }

        [Test]
        public void RequestBodyReader_RejectsContentLengthOverLimit()
        {
            var stream = new MemoryStream(Encoding.UTF8.GetBytes("{}"));

            Assert.Throws<InvalidDataException>(
                () => RequestBodyReader.ReadUtf8(stream, RequestBodyReader.MaxBodyBytes + 1));
        }

        [TestCase("/invoke")]
        [TestCase("/batch")]
        [TestCase("/eval")]
        [TestCase("/recompile")]
        [TestCase("/ui/act")]
        public void Guard_GetOnMutatingRoute_IsRejected(string path)
        {
            Assert.AreEqual(path + " requires POST", RequestGuard.Check("GET", path, null, EditorDebugMCP.MutatingRoutes));
            Assert.IsNull(RequestGuard.Check("POST", path, null, EditorDebugMCP.MutatingRoutes));
        }

        [TestCase("/ping")]
        [TestCase("/describe")]
        [TestCase("/console")]
        [TestCase("/ui/snapshot")]
        [TestCase("/ui/hit")]
        public void Guard_GetOnReadOnlyRoute_IsAllowed(string path)
        {
            Assert.IsNull(RequestGuard.Check("GET", path, null, EditorDebugMCP.MutatingRoutes));
        }
    }
}
