using System.IO;
using Newtonsoft.Json;

namespace Yoji.U3DAILinker.Operations
{
    /// operation.json 的原子读写。tmp + File.Replace 保证域重载/崩溃下日志不被写半截。
    /// libraryRoot 从构造注入（真实运行传工程 Library 目录，测试传临时目录）。
    internal sealed class OperationLogStore
    {
        private readonly string m_Dir;
        public string LogPath { get; }

        public OperationLogStore(string libraryRoot)
        {
            m_Dir = Path.Combine(libraryRoot, "U3DAILinker");
            LogPath = Path.Combine(m_Dir, "operation.json");
        }

        /// 原子落盘：先写 tmp，再 File.Replace 覆盖正式日志；目标不存在时直接 Move。
        public void Save(OperationLog log)
        {
            Directory.CreateDirectory(m_Dir);
            var tmp = LogPath + ".tmp";
            File.WriteAllText(tmp, JsonConvert.SerializeObject(log, Formatting.Indented));
            if (File.Exists(LogPath))
                File.Replace(tmp, LogPath, null);
            else
                File.Move(tmp, LogPath);
        }

        /// 读取日志；无文件或损坏 JSON 返回 null（恢复逻辑据此判定"无进行中操作"）。
        public OperationLog Load()
        {
            if (!File.Exists(LogPath)) return null;
            try { return JsonConvert.DeserializeObject<OperationLog>(File.ReadAllText(LogPath)); }
            catch { return null; }
        }

        /// 操作完成或取消后清除日志，避免下次启动误恢复。
        public void Clear()
        {
            try { if (File.Exists(LogPath)) File.Delete(LogPath); } catch { }
        }
    }
}
