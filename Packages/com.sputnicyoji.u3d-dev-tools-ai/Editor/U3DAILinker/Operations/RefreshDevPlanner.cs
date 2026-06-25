using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Yoji.U3DAILinker.Registry;

namespace Yoji.U3DAILinker.Operations
{
    /// Refresh Dev 的计划结果:锁定的 SHA + 全部依赖变更(同一 SHA)。
    internal sealed class RefreshDevPlan
    {
        public string CommitSha;
        public List<DependencyChange> Changes = new List<DependencyChange>();
    }

    /// 生成 Refresh Dev 计划:把启用工具及其 infra 依赖闭包的 URL 全部锁到同一个远端 main SHA。
    /// 真实 Client.Add 不在此处;此类只产出意图,便于单测。
    internal sealed class RefreshDevPlanner
    {
        private static readonly Regex Sha40 = new Regex("^[0-9a-f]{40}$", RegexOptions.Compiled);

        private readonly IGitRefResolver _resolver;

        public RefreshDevPlanner(IGitRefResolver resolver)
        {
            _resolver = resolver;
        }

        public RefreshDevPlan BuildPlan(LinkerRegistry devRegistry, IReadOnlyCollection<string> enabledToolIds)
        {
            if (devRegistry.Channel != LinkerChannel.Dev)
                throw new InvalidOperationException("RefreshDev requires a Dev-channel registry.");
            if (devRegistry.Branch != "main")
                throw new InvalidOperationException("Dev registry branch must be 'main', got: " + devRegistry.Branch);

            var sha = _resolver.ResolveBranchSha(devRegistry.Branch);
            if (!Sha40.IsMatch(sha))
                throw new InvalidOperationException("Resolver returned non-40-hex SHA: " + sha);

            var byId = devRegistry.Entries.ToDictionary(e => e.Id, e => e);
            var closure = ResolveClosure(byId, enabledToolIds);

            var plan = new RefreshDevPlan { CommitSha = sha };
            foreach (var entry in closure
                         .Where(e => e.Status == ToolStatus.Ready)
                         .OrderBy(e => e.Order)
                         .ThenBy(e => e.Id, StringComparer.Ordinal))
            {
                plan.Changes.Add(new DependencyChange
                {
                    PackageName = entry.PackageName,
                    OldValue = null,
                    NewValue = ToolUrlBuilder.BuildDevSha(entry.PackagePath, sha),
                });
            }
            return plan;
        }

        // 启用工具 + 其 dependsOn 的 infra 闭包。未知 ID 抛错(与设计 116 行一致)。
        private static List<RegistryEntryView> ResolveClosure(
            Dictionary<string, RegistryEntryView> byId, IReadOnlyCollection<string> enabledToolIds)
        {
            var result = new List<RegistryEntryView>();
            var seen = new HashSet<string>();
            var stack = new Stack<string>(enabledToolIds ?? Array.Empty<string>());
            while (stack.Count > 0)
            {
                var id = stack.Pop();
                if (!seen.Add(id))
                    continue;
                if (!byId.TryGetValue(id, out var entry))
                    throw new InvalidOperationException("Unknown tool id in enabled set: " + id);
                result.Add(entry);
                if (entry.DependsOn != null)
                {
                    foreach (var dep in entry.DependsOn)
                        stack.Push(dep);
                }
            }
            return result;
        }
    }
}
