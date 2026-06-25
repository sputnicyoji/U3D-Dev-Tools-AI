using System.Collections.Generic;

namespace Yoji.U3DAILinker.Operations
{
    /// 一条 manifest 依赖变更意图。Remove 时 NewValue 为 null。
    public enum ManifestChangeType
    {
        Add,
        Update,
        Remove,
    }

    /// 调用方（安装/移除子系统）算好的单条编辑：要把 packageName 设为 NewValue，或移除。
    /// 注意：ChangeType 是「意图」；事务执行时会按 manifest 现状重新判定 Add/Update 并做 ownership 校验。
    public sealed class ManifestEdit
    {
        public string PackageName;
        public ManifestChangeType ChangeType;
        public string NewValue;   // Remove 时为 null

        public ManifestEdit() { }

        public ManifestEdit(string packageName, ManifestChangeType changeType, string newValue)
        {
            PackageName = packageName;
            ChangeType = changeType;
            NewValue = newValue;
        }
    }

    /// 一次 manifest 事务的完整输入。OperationId 唯一标识本次操作（用于 backup 文件名）。
    public sealed class ManifestPlan
    {
        public string OperationId;
        public string Channel;    // stable | dev | local
        public string Revision;   // tag 或 40 位 SHA 或 file 路径描述；仅记账用
        public List<ManifestEdit> Edits = new List<ManifestEdit>();

        public ManifestPlan() { }

        public ManifestPlan(string operationId, string channel, string revision)
        {
            OperationId = operationId;
            Channel = channel;
            Revision = revision;
        }
    }
}
