using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Yoji.U3DAILinker.Operations
{
    /// 对一个 skill 目录做确定性内容哈希。结果只取决于相对路径集合（正斜杠、忽略大小写无关地排序）
    /// 与各文件原始字节，和遍历顺序、卷、绝对路径无关。ownership 文件自身被排除（避免自指）。
    /// 纯逻辑：仅依赖 System.IO，可在 EditMode 无副作用单测。
    internal static class ContentHash
    {
        public static string OfDirectory(string root)
        {
            if (!Directory.Exists(root))
                throw new DirectoryNotFoundException("hash source not found: " + root);

            var files = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
            var entries = new System.Collections.Generic.List<(string rel, string full)>(files.Length);
            foreach (var full in files)
            {
                var rel = Relative(root, full);
                if (string.Equals(rel, OwnershipFile.FileName, StringComparison.OrdinalIgnoreCase))
                    continue; // 排除 ownership 文件本身
                entries.Add((rel, full));
            }
            entries.Sort((a, b) => string.CompareOrdinal(a.rel, b.rel));

            using (var sha = SHA256.Create())
            {
                foreach (var (rel, full) in entries)
                {
                    // 路径长度 + 路径字节 + 内容长度 + 内容字节，逐项喂入，防止边界歧义
                    var pathBytes = Encoding.UTF8.GetBytes(rel);
                    FeedLength(sha, pathBytes.Length);
                    sha.TransformBlock(pathBytes, 0, pathBytes.Length, null, 0);

                    var content = File.ReadAllBytes(full);
                    FeedLength(sha, content.Length);
                    sha.TransformBlock(content, 0, content.Length, null, 0);
                }
                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                return ToHex(sha.Hash);
            }
        }

        private static void FeedLength(HashAlgorithm sha, int length)
        {
            var lenBytes = BitConverter.GetBytes((long)length);
            if (!BitConverter.IsLittleEndian) Array.Reverse(lenBytes); // 跨平台统一小端
            sha.TransformBlock(lenBytes, 0, lenBytes.Length, null, 0);
        }

        private static string Relative(string root, string full)
        {
            var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fileFull = Path.GetFullPath(full);
            var rel = fileFull.Substring(rootFull.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return rel.Replace('\\', '/');
        }

        private static string ToHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
