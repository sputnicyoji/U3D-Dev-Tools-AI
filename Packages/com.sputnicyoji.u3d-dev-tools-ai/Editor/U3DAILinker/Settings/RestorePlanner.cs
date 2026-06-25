using System;
using System.Collections.Generic;
using System.Linq;
using Yoji.U3DAILinker.Operations;
using Yoji.U3DAILinker.Registry;

namespace Yoji.U3DAILinker.Settings
{
    /// 把 Local 通道写入 manifest 的托管 file: 依赖恢复为可提交的 tag/SHA Git URL。
    /// 只处理 Linker 管理的 file: 依赖,且只处理目标 Registry 已知的包(设计 192/395)。
    internal static class RestorePlanner
    {
        private const string FilePrefix = "file:";

        /// 当前 manifest 是否存在 Linker 管理的 file: 依赖(误提交风险,面板须告警)。
        public static bool HasLocalFileDependencies(
            IReadOnlyDictionary<string, InstalledPackageInfo> installed)
        {
            if (installed == null)
                return false;
            return installed.Values.Any(IsManagedFile);
        }

        /// 生成把所有托管 file: 依赖换回 targetRegistry 通道 URL 的依赖变更集。
        /// targetRegistry.Channel == Dev 时使用 commitSha;否则用各条目的 Stable tag。
        public static DependencyChange[] BuildRestore(
            IReadOnlyDictionary<string, InstalledPackageInfo> installed,
            LinkerRegistry targetRegistry,
            string commitSha)
        {
            if (installed == null)
                return Array.Empty<DependencyChange>();

            var byPackageName = targetRegistry.Entries.ToDictionary(e => e.PackageName, e => e);
            var result = new List<DependencyChange>();

            foreach (var info in installed.Values
                         .Where(IsManagedFile)
                         .OrderBy(i => i.PackageName, StringComparer.Ordinal))
            {
                if (!byPackageName.TryGetValue(info.PackageName, out var entry))
                    continue;

                var newValue = targetRegistry.Channel == LinkerChannel.Dev
                    ? ToolUrlBuilder.BuildDevSha(entry.PackagePath, RequireSha(commitSha))
                    : ToolUrlBuilder.BuildStable(entry.PackagePath, entry.Revision);

                result.Add(new DependencyChange
                {
                    PackageName = info.PackageName,
                    OldValue = info.ResolvedUrl,
                    NewValue = newValue,
                });
            }

            return result.ToArray();
        }

        private static bool IsManagedFile(InstalledPackageInfo info)
        {
            return info != null
                   && info.IsManaged
                   && !string.IsNullOrEmpty(info.ResolvedUrl)
                   && info.ResolvedUrl.StartsWith(FilePrefix, StringComparison.Ordinal);
        }

        private static string RequireSha(string commitSha)
        {
            if (string.IsNullOrEmpty(commitSha))
                throw new InvalidOperationException(
                    "Restore to Dev requires a resolved commit SHA; run Refresh Dev first.");
            return commitSha;
        }
    }
}
