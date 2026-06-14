using System.IO;
using System.Reflection;
using System.Text;
using NUnit.Framework;

namespace Yoji.EditorDebug.Tests
{
    public sealed class EditorDebugMCPTests
    {
        [Test]
        public void Eval_IsDisabledByDefault()
        {
            var field = typeof(EditorDebugMCP).GetField("c_AllowEval", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(field);
            Assert.IsFalse((bool)field.GetValue(null));
        }

        [Test]
        public void RequestBodyReader_RejectsContentLengthOverLimit()
        {
            var stream = new MemoryStream(Encoding.UTF8.GetBytes("{}"));

            Assert.Throws<InvalidDataException>(
                () => RequestBodyReader.ReadUtf8(stream, RequestBodyReader.MaxBodyBytes + 1));
        }
    }
}
