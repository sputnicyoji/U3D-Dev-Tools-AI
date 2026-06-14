using System.Collections.Generic;
using System.Linq;
using Yoji.U3DAILinker.Operations;
using Yoji.U3DAILinker.Registry;

namespace Yoji.U3DAILinker.Settings
{
    internal enum InstallState
    {
        Missing,
        Installed,
        Conflict,
    }

    internal enum AgentState
    {
        Synced,
        Stale,
        Missing,
        Conflict,
        NotApplicable,
    }

    /// 面板工具列表单行的纯数据视图(设计 371-383 列定义)。
    internal sealed class ToolRow
    {
        public string Id;
        public string DisplayName;
        public ToolKind Kind;
        public bool EnabledVisible;
        public bool Enabled;
        public InstallState Installed;
        public string Desired;
        public string Current;
        public AgentState Agent;
        public string[] RequiredBy;
    }

    /// 由 Registry + 当前安装快照 + 启用集 + Agent 状态 + 通道,
    /// 纯函数地算出面板工具列表。无 IMGUI、无 UPM 副作用,完全可单测。
    internal static class PanelStateModel
    {
        public static ToolRow[] Build(
            LinkerRegistry registry,
            IReadOnlyDictionary<string, InstalledPackageInfo> installed,
            IReadOnlyCollection<string> enabledToolIds,
            IReadOnlyDictionary<string, AgentState> agentStates,
            LinkerChannel channel,
            string localRepoRoot)
        {
            var enabled = new HashSet<string>(enabledToolIds ?? System.Array.Empty<string>());

            var rows = new List<ToolRow>(registry.Entries.Count);
            foreach (var entry in registry.Entries)
            {
                var desired = BuildDesired(entry, channel, localRepoRoot);
                var current = ResolveCurrent(installed, entry.PackageName);
                rows.Add(new ToolRow
                {
                    Id = entry.Id,
                    DisplayName = string.IsNullOrEmpty(entry.DisplayName) ? entry.Id : entry.DisplayName,
                    Kind = entry.Kind,
                    EnabledVisible = entry.Kind == ToolKind.Tool && entry.UserToggle,
                    Enabled = enabled.Contains(entry.Id),
                    Installed = ResolveInstallState(installed, entry.PackageName),
                    Desired = desired,
                    Current = current,
                    Agent = ResolveAgentState(agentStates, entry.Id),
                    RequiredBy = ResolveRequiredBy(registry, enabled, entry),
                });
            }

            return rows
                .OrderBy(r => OrderOf(registry, r.Id))
                .ThenBy(r => r.Id, System.StringComparer.Ordinal)
                .ToArray();
        }

        private static string BuildDesired(RegistryEntryView entry, LinkerChannel channel, string localRepoRoot)
        {
            switch (channel)
            {
                case LinkerChannel.Local:
                    return string.IsNullOrEmpty(localRepoRoot)
                        ? null
                        : ToolUrlBuilder.BuildLocalFile(localRepoRoot, entry.PackagePath);
                case LinkerChannel.Dev:
                    // Dev 的精确 SHA 由 RefreshDev 锁定;此处展示 branch 占位。
                    return ToolUrlBuilder.RepoUrl + "?path=/" +
                           entry.PackagePath.Replace('\\', '/') + "#main";
                default:
                    return ToolUrlBuilder.BuildStable(entry.PackagePath, entry.Revision);
            }
        }

        private static string ResolveCurrent(
            IReadOnlyDictionary<string, InstalledPackageInfo> installed, string packageName)
        {
            return installed != null && installed.TryGetValue(packageName, out var info)
                ? info.ResolvedUrl
                : null;
        }

        private static InstallState ResolveInstallState(
            IReadOnlyDictionary<string, InstalledPackageInfo> installed, string packageName)
        {
            if (installed == null || !installed.TryGetValue(packageName, out var info))
                return InstallState.Missing;
            return info.IsManaged ? InstallState.Installed : InstallState.Conflict;
        }

        private static AgentState ResolveAgentState(
            IReadOnlyDictionary<string, AgentState> agentStates, string toolId)
        {
            return agentStates != null && agentStates.TryGetValue(toolId, out var state)
                ? state
                : AgentState.NotApplicable;
        }

        private static string[] ResolveRequiredBy(
            LinkerRegistry registry, HashSet<string> enabled, RegistryEntryView entry)
        {
            if (entry.Kind != ToolKind.Infra)
                return System.Array.Empty<string>();

            return registry.Entries
                .Where(e => e.Kind == ToolKind.Tool
                            && enabled.Contains(e.Id)
                            && e.DependsOn != null
                            && e.DependsOn.Contains(entry.Id))
                .Select(e => e.Id)
                .OrderBy(id => id, System.StringComparer.Ordinal)
                .ToArray();
        }

        private static int OrderOf(LinkerRegistry registry, string id)
        {
            var e = registry.Entries.FirstOrDefault(x => x.Id == id);
            return e != null ? e.Order : int.MaxValue;
        }
    }
}
