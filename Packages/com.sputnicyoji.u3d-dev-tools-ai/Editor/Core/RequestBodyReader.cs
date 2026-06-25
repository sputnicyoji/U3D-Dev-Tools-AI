using System.IO;
using System.Text;

namespace Yoji.EditorCore
{
    /// 有界读取 HTTP 请求体：按 Content-Length 早拒超限，流式读入时再次守卫，
    /// 避免无界 StreamReader.ReadToEnd。共享基础设施（editor-core），供各 dev-tool 的 HTTP 服务复用。
    public static class RequestBodyReader
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
