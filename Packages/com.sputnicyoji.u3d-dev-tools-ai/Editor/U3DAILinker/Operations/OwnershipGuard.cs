using System.IO;

namespace Yoji.U3DAILinker.Operations
{
    /// 目标目录的归属状态。
    internal enum OwnershipStatus
    {
        Missing,           // 目录不存在：可放心创建
        Foreign,           // 存在但无合法 ownership：用户目录，不覆盖
        ManagedMatch,      // Linker 管理且属于期望工具：可替换
        ManagedMismatch,   // Linker 管理但属于别的工具：不覆盖
    }

    /// 决定一个目标 skill 目录是否可被本工具的同步覆盖。
    /// 规则（spec 280/315/479）：缺合法 ownership 文件 = 用户目录，不动；ownership 工具 ID 与期望不符 = 别人的目录，不动。
    /// 纯逻辑：只读文件系统与 OwnershipFile，可在 EditMode 单测。
    internal static class OwnershipGuard
    {
        public static OwnershipStatus Inspect(string dir, string expectedToolId)
        {
            if (!Directory.Exists(dir)) return OwnershipStatus.Missing;

            var record = OwnershipFile.Read(dir);
            if (record == null || string.IsNullOrEmpty(record.ToolId))
                return OwnershipStatus.Foreign;

            return record.ToolId == expectedToolId
                ? OwnershipStatus.ManagedMatch
                : OwnershipStatus.ManagedMismatch;
        }

        /// 仅 Missing 与 ManagedMatch 允许覆盖；Foreign / ManagedMismatch 必须拒绝。
        public static bool MayOverwrite(string dir, string expectedToolId)
        {
            var status = Inspect(dir, expectedToolId);
            return status == OwnershipStatus.Missing || status == OwnershipStatus.ManagedMatch;
        }
    }
}
