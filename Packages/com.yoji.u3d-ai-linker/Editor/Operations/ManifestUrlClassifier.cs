using System;

namespace Yoji.U3DAILinker.Operations
{
    /// 受管依赖归属：GitRepo/LocalFile 由 Linker 管理（可覆盖、可删）；
    /// Unmanaged 是用户或第三方手写值（冲突，不自动动）；Absent 表示该包不在 dependencies。
    public enum DependencyOwnership
    {
        GitRepo,
        LocalFile,
        Unmanaged,
        Absent,
    }

    /// 判定 manifest dependencies 中某条目当前值是否由 Linker 管理。
    /// 纯字符串逻辑，无 IO，可单测。
    public static class ManifestUrlClassifier
    {
        public const string RepoSlug = "sputnicyoji/U3D-Dev-Tools-AI";
        private const string YojiPrefix = "com.yoji.";

        public static DependencyOwnership Classify(string packageName, string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return DependencyOwnership.Absent;

            // Linker 只接管自己命名空间下的包；非 com.yoji.* 一律不动。
            if (packageName == null || !packageName.StartsWith(YojiPrefix, StringComparison.Ordinal))
                return DependencyOwnership.Unmanaged;

            var value = url.Trim();

            if (value.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                return DependencyOwnership.LocalFile;

            if (IsThisRepoGitUrl(value))
                return DependencyOwnership.GitRepo;

            return DependencyOwnership.Unmanaged;
        }

        // 必须是 github 上本仓的 .git URL，slug 与 host 大小写不敏感；其它 Git 仓库判 Unmanaged。
        private static bool IsThisRepoGitUrl(string value)
        {
            if (value.IndexOf(".git", StringComparison.OrdinalIgnoreCase) < 0)
                return false;
            return value.IndexOf("github.com/" + RepoSlug, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
