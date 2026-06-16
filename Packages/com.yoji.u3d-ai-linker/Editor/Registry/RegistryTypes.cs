using System.Collections.Generic;

namespace Yoji.U3DAILinker
{
    /// 当前通道。Local 仅本机内循环,绝对路径只入 UserSettings。
    internal enum LinkerChannel
    {
        Stable,
        Dev,
        Local,
    }

    /// Registry 数据来源,用于面板顶部状态区显示。
    internal enum RegistrySource
    {
        Remote,
        BundledSnapshot,
        LocalFile,
    }

    /// 当前操作态,用于面板顶部状态区显示。
    internal enum OperationState
    {
        Idle,
        Running,
        Failed,
        NeedsRecovery,
    }
}

namespace Yoji.U3DAILinker.Registry
{
    /// Registry 中单个包条目的已校验视图(LINK-6 消费所需的最小字段)。
    /// 与 LINK-2 权威 RegistryEntry(string Status/Kind + JsonProperty)分名共存。
    internal sealed class RegistryEntryView
    {
        public string Id;
        public ToolStatus Status;
        public ToolKind Kind;
        public int Order;
        public string PackageName;
        public string PackagePath;
        public string Revision;
        public bool DefaultEnabled;
        public bool UserToggle;
        public string AgentAssets;
        public string MinUnity;
        public List<string> DependsOn = new List<string>();
        public string DisplayName;
    }

    /// 单通道 Registry 的已校验视图。
    internal sealed class LinkerRegistry
    {
        public int SchemaVersion;
        public LinkerChannel Channel;
        public string Branch;
        public List<RegistryEntryView> Entries = new List<RegistryEntryView>();
    }
}
