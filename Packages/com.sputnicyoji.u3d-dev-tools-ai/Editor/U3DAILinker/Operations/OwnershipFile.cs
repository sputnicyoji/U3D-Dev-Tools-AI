using System.IO;
using Newtonsoft.Json;

namespace Yoji.U3DAILinker.Operations
{
    /// 一个被 Linker 管理的 skill 目录的归属记录。随 .u3d-ai-owner.json 经 Newtonsoft round-trip。
    internal sealed class OwnershipRecord
    {
        public string ToolId;          // Registry 中的工具 ID，例如 "test-runner"
        public string SourceRevision;  // 来源 revision（Git SHA / tag / local 标记）
        public string ContentHash;     // 同步时刻 ContentHash.OfDirectory 的结果
        public string SchemaVersion;   // ownership 文件结构版本，便于后续迁移
    }

    /// .u3d-ai-owner.json 的读写。读坏文件返回 null（按"无合法 ownership"处理），不抛异常。
    /// 纯逻辑：仅 System.IO + Newtonsoft，可在 EditMode 无副作用单测。
    internal static class OwnershipFile
    {
        public const string FileName = ".u3d-ai-owner.json";
        public const string SchemaVersion = "1";

        private static string PathFor(string dir) => Path.Combine(dir, FileName);

        public static bool Exists(string dir) => File.Exists(PathFor(dir));

        public static void Write(string dir, OwnershipRecord record)
        {
            Directory.CreateDirectory(dir);
            record.SchemaVersion = SchemaVersion;
            File.WriteAllText(PathFor(dir), JsonConvert.SerializeObject(record, Formatting.Indented));
        }

        public static OwnershipRecord Read(string dir)
        {
            var path = PathFor(dir);
            if (!File.Exists(path)) return null;
            try { return JsonConvert.DeserializeObject<OwnershipRecord>(File.ReadAllText(path)); }
            catch { return null; }
        }
    }
}
