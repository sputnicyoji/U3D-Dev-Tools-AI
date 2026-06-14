using System.Collections.Generic;

namespace Yoji.U3DAILinker.Operations
{
    /// operation.json 中单条依赖变更记账：旧值 -> 新值。Remove 时 NewValue 为 null；Add 时 OldValue 为 null。
    public sealed class DependencyChange
    {
        public string PackageName;
        public string ChangeType;   // Add | Update | Remove（字符串，便于面板/日志直接输出）
        public string OldValue;
        public string NewValue;
    }

    /// 写入 Library/U3DAILinker/operation.json 的事务记录。Status: committed | failed | rolledback。
    public sealed class OperationRecord
    {
        public string OperationId;
        public string Channel;
        public string Revision;
        public string BackupPath;
        public string Status;
        public List<DependencyChange> DependencyChanges = new List<DependencyChange>();
    }

    /// 受管包当前值非 Linker 管理（第三方/手写）时的冲突描述。
    public sealed class ManifestConflict
    {
        public string PackageName;
        public string ExistingValue;
    }

    /// 事务结果。Committed=false 时看 Conflicts（非空=因冲突拒绝）或 FailureReason（IO/解析失败）。
    public sealed class ManifestTransactionResult
    {
        public bool Committed;
        public OperationRecord Record;
        public List<ManifestConflict> Conflicts = new List<ManifestConflict>();
        public string FailureReason;
    }

    // --- LINK-3 队列态(Task 22)：与 LINK-2b 事务记账类型并入同一文件。复用上方权威 DependencyChange，不再另定义。 ---

    /// 操作 phase 取值。字符串以便直接读日志、跨版本宽容。
    internal static class OperationPhase
    {
        public const string Pending = "pending";                    // 已落盘待发请求
        public const string PackageRequested = "package-requested"; // 已 Client.Add，等域重载/解析
        public const string Completed = "completed";                // 整队完成
        public const string Failed = "failed";                      // 超重试上限或 UPM 报错
    }

    /// 持久化的当前操作日志，与 spec 状态示例字段一一对应。
    /// 写入 Library/U3DAILinker/operation.json，跨域重载存活；只表达意图，恢复以实际 manifest 为准。
    internal sealed class OperationLog
    {
        public string OperationId;
        public string Action;                    // install-all | update | remove ...
        public string[] ToolIds;
        public int CurrentIndex;                 // 当前处理到队列第几项
        public string Phase;                     // 见 OperationPhase
        public string Channel;                   // stable | dev | local
        public string ResolvedRevision;          // 当前项解析出的 revision（tag / SHA）
        public string ManifestBackupPath;        // 由 LINK-2b manifest 事务写入
        public List<DependencyChange> DependencyChanges;
        public List<string> Completed;           // 已成功的 packageName
        public int RetryCount;                   // 当前项已重试次数（恢复核对失败 +1）
    }
}
