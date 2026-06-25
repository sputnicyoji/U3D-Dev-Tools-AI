using System.Collections.Generic;

namespace Yoji.U3DAILinker.Operations
{
    /// RemovePlanner 输入用的最小 Registry 条目投影：只取计算闭包必需字段。
    /// 上层把已解析的完整 Registry 条目映射成本类型传入，保持本包纯逻辑。
    public sealed class RegistryEntryInfo
    {
        public string Id;
        public string PackageName;
        public string Kind;        // tool | infra | linker
        public string[] DependsOn; // 工具 ID 列表，可空
    }

    /// Remove 第二步：算出可从 manifest 移除的包集合。
    /// 目标 tool 自身总移除；只有没有剩余 dependents 的 infra 可移除；linker 永不移除。
    /// 纯图算法，无 IO，可单测。
    public sealed class RemovePlanner
    {
        /// 返回 (removablePackages, blockedInfra)，均为 packageName 列表。
        /// removablePackages[0] 是目标 tool 的 package（若目标存在）。
        public static (List<string> removablePackages, List<string> blockedInfra) Compute(
            RegistryEntryInfo[] entries, ISet<string> enabledToolIds, string removeToolId)
        {
            var byId = new Dictionary<string, RegistryEntryInfo>();
            foreach (var e in entries)
                if (e != null && e.Id != null) byId[e.Id] = e;

            var removable = new List<string>();
            var blocked = new List<string>();

            // 目标 tool 自身总进 removable（首位）。linker 不允许作为目标在此移除。
            if (byId.TryGetValue(removeToolId, out var target)
                && target.Kind != "linker"
                && !string.IsNullOrEmpty(target.PackageName))
            {
                removable.Add(target.PackageName);
            }

            // 剩余启用工具 = enabled 去掉目标，仅保留 tool kind。
            var remainingTools = new List<RegistryEntryInfo>();
            foreach (var id in enabledToolIds)
            {
                if (id == removeToolId) continue;
                if (byId.TryGetValue(id, out var e) && e.Kind == "tool")
                    remainingTools.Add(e);
            }

            // 剩余工具的 dependsOn 传递闭包 = 仍需保留的 infra ID 集合。
            var keepInfra = new HashSet<string>();
            foreach (var t in remainingTools)
                CollectClosure(t, byId, keepInfra);

            // 遍历所有 infra：不在保留集 = 孤儿可删；在保留集 = blocked。
            foreach (var e in entries)
            {
                if (e == null || e.Kind != "infra" || string.IsNullOrEmpty(e.PackageName)) continue;
                if (keepInfra.Contains(e.Id)) blocked.Add(e.PackageName);
                else removable.Add(e.PackageName);
            }

            return (removable, blocked);
        }

        // 把 entry 的 dependsOn 中所有 infra 传递地加入 keep（tool/linker 不计入 infra 保留集）。
        private static void CollectClosure(RegistryEntryInfo entry,
            Dictionary<string, RegistryEntryInfo> byId, HashSet<string> keep)
        {
            if (entry.DependsOn == null) return;
            foreach (var depId in entry.DependsOn)
            {
                if (!byId.TryGetValue(depId, out var dep)) continue;
                // infra 仅在首次加入 keep 时下探（keep.Add 兼作环路守卫）；tool/linker 直接下探、不计入 infra 保留集。
                if (dep.Kind == "infra" && !keep.Add(dep.Id)) continue;
                CollectClosure(dep, byId, keep);
            }
        }
    }
}
