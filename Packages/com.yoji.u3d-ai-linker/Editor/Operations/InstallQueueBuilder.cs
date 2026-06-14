using System.Collections.Generic;

namespace Yoji.U3DAILinker.Operations
{
    /// Registry 解析后的单个工具视图（本子系统消费 LINK-2 的解析结果）。
    /// 组装时若 LINK-2 已导出等价类型，可用其替换并删除本类。
    internal sealed class ResolvedTool
    {
        public string ToolId;
        public string PackageName;
        public string Kind;        // "tool" | "infra"
        public string Status;      // "ready" | "skill-only" | "planned"
        public string[] DependsOn;
        public bool IsLinker;      // linker 自身（自更新必须排队尾）
        public string PackageUrl;  // 已由 Registry 校验字段生成的安装 URL
    }

    /// 安装队列单项：串行 Client.Add 的目标。
    internal sealed class QueueItem
    {
        public string ToolId;
        public string PackageName;
        public string PackageUrl;
        public bool IsLinker;
    }

    /// 把"用户启用的 tool 集合"展开成有序安装队列：
    /// 1) 取启用工具 + 其 dependsOn 递归闭包（拉入 infra）；
    /// 2) 只保留 status=="ready" 的项；
    /// 3) 依赖在前（infra 先于 dependent），用确定性后序遍历得到拓扑序；
    /// 4) linker 自身强制排到最后（spec 229：自更新替换 Editor Assembly）。
    /// 不在此做环依赖/未知 ID 拒绝（那是 LINK-2 Registry 校验职责）；未知启用 ID 静默跳过。
    internal sealed class InstallQueueBuilder
    {
        public IReadOnlyList<QueueItem> Build(
            IReadOnlyList<ResolvedTool> resolvedTools,
            IReadOnlyCollection<string> enabledToolIds)
        {
            var byId = new Dictionary<string, ResolvedTool>();
            foreach (var t in resolvedTools)
                byId[t.ToolId] = t;

            var ordered = new List<ResolvedTool>(); // 依赖在前的确定性拓扑序
            var visited = new HashSet<string>();

            foreach (var id in enabledToolIds)
                Visit(id, byId, visited, ordered);

            // 拆出 linker，过滤非 ready，linker 排尾
            var items = new List<QueueItem>();
            QueueItem linkerItem = null;
            foreach (var t in ordered)
            {
                if (t.Status != "ready") continue;
                var item = new QueueItem
                {
                    ToolId = t.ToolId,
                    PackageName = t.PackageName,
                    PackageUrl = t.PackageUrl,
                    IsLinker = t.IsLinker,
                };
                if (t.IsLinker) linkerItem = item;
                else items.Add(item);
            }
            if (linkerItem != null) items.Add(linkerItem);
            return items;
        }

        // 后序遍历：先递归依赖，再加自身 -> 依赖天然排在前面。HashSet 去重保证共享 infra 只出现一次。
        private static void Visit(
            string id,
            Dictionary<string, ResolvedTool> byId,
            HashSet<string> visited,
            List<ResolvedTool> ordered)
        {
            if (!byId.TryGetValue(id, out var tool)) return; // 未知启用 ID 跳过
            if (!visited.Add(id)) return;                    // 已访问（环或共享）
            if (tool.DependsOn != null)
            {
                foreach (var dep in tool.DependsOn)
                    Visit(dep, byId, visited, ordered);
            }
            ordered.Add(tool);
        }
    }
}
