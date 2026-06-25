namespace Yoji.U3DAILinker.Registry
{
    /// 三通道 UPM 依赖值生成器。安装 URL 只能由本类用已校验字段拼装,
    /// 不能直接执行 Registry 提供的整串 URL(见设计 172 行)。仓库固定,不接受自定义仓库 URL。
    /// (Appendix B 决策4:LINK-2 ToolUrlBuilder 与 LINK-6 PackageUrlBuilder 统一为本类,采用后者 API surface。)
    internal static class ToolUrlBuilder
    {
        internal const string RepoUrl = "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git";

        private const string FilePrefix = "file:";

        /// Stable: tag 形如 u3d-dev-tools-ai-v0.2.2。packagePath 形如 Packages/com.sputnicyoji.u3d-dev-tools-ai。
        public static string BuildStable(string packagePath, string revision)
        {
            return RepoUrl + "?path=/" + Normalize(packagePath) + "#" + revision;
        }

        /// Dev: 锁定到 40 位 commit SHA。
        public static string BuildDevSha(string packagePath, string commitSha)
        {
            return RepoUrl + "?path=/" + Normalize(packagePath) + "#" + commitSha;
        }

        /// Local: file: 指向本机仓库工作区中的子包目录。
        /// localRepoRoot 是本机绝对路径(只来自 UserSettings),反斜杠归一为正斜杠。参数序 (root, packagePath)。
        public static string BuildLocalFile(string localRepoRoot, string packagePath)
        {
            var root = Normalize(localRepoRoot).TrimEnd('/');
            return FilePrefix + root + "/" + Normalize(packagePath);
        }

        /// 判断某 manifest 依赖值是否由 Linker 管理:本仓库 Git URL 或本机 file: 路径。
        public static bool IsManagedUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
                return false;
            if (url.StartsWith(FilePrefix, System.StringComparison.Ordinal))
                return true;
            return url.StartsWith(RepoUrl, System.StringComparison.Ordinal);
        }

        private static string Normalize(string path)
        {
            return path == null ? string.Empty : path.Replace('\\', '/');
        }
    }
}
