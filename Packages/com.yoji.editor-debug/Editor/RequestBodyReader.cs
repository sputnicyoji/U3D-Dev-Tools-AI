using System.IO;
using System.Text;

namespace Yoji.EditorDebug
{
    internal static class RequestBodyReader
    {
        public const int MaxBodyBytes = 1024 * 1024;

        public static string ReadUtf8(Stream stream, long contentLength)
        {
            if (contentLength > MaxBodyBytes)
                throw new InvalidDataException("request body exceeds limit");

            // contentLength is verified <= MaxBodyBytes above; pre-size so a known body fits without MemoryStream regrowth.
            using (var ms = new MemoryStream(contentLength > 0 ? (int)contentLength : 0))
            {
                var buffer = new byte[8192];
                while (true)
                {
                    var read = stream.Read(buffer, 0, buffer.Length);
                    if (read <= 0) break;
                    ms.Write(buffer, 0, read);
                    if (ms.Length > MaxBodyBytes)
                        throw new InvalidDataException("request body exceeds limit");
                }
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }
    }
}
