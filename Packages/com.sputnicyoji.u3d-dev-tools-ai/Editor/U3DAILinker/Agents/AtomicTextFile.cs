using System.IO;
using System.Text;

namespace Yoji.U3DAILinker.Agents
{
    /// <summary>
    /// 托管文本文件的原子写入：先写临时文件再替换，避免写半截。
    /// ManagedBlockWriter 与 GitignoreBlockWriter 共用，保证 .u3dtmp 后缀与无 BOM UTF-8 编码单一来源。
    /// </summary>
    internal static class AtomicTextFile
    {
        public static void Write(string filePath, string content)
        {
            string dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            string tmp = filePath + ".u3dtmp";
            File.WriteAllText(tmp, content, new UTF8Encoding(false));
            if (File.Exists(filePath)) File.Delete(filePath);
            File.Move(tmp, filePath);
        }
    }
}
