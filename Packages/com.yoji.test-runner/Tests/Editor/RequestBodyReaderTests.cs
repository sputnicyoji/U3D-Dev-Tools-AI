using System.IO;
using System.Text;
using NUnit.Framework;

namespace Yoji.TestRunner.Tests
{
    public sealed class RequestBodyReaderTests
    {
        [Test]
        public void ReadUtf8_RejectsContentLengthOverLimit()
        {
            var stream = new MemoryStream(Encoding.UTF8.GetBytes("{}"));

            Assert.Throws<InvalidDataException>(
                () => RequestBodyReader.ReadUtf8(stream, RequestBodyReader.MaxBodyBytes + 1));
        }
    }
}
