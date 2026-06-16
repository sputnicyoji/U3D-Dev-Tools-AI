namespace Yoji.U3DAILinker.Operations
{
    /// 目标工程当前已安装包的纯数据快照。
    /// 由 manifest dependencies 值 + PackageInfo 解析得到,但本类本身不触 UPM,
    /// 便于在 EditMode 用字典构造做纯逻辑测试。
    internal sealed class InstalledPackageInfo
    {
        /// UPM 包名,如 com.yoji.editor-debug。
        public string PackageName;

        /// manifest 中该依赖的当前值(Git URL 或 file: 路径);未安装为 null。
        public string ResolvedUrl;

        /// manifest dependencies 中声明的值。Unity 有时会把 PackageInfo.packageId 显示为锁定 SHA，
        /// 这里保留用户可提交的声明值，便于区分 tag URL 与实际锁定 hash。
        public string DeclaredUrl;

        /// PackageInfo.packageId 中解析出的来源值，通常反映 Unity 当前解析结果。
        public string PackageIdUrl;

        /// PackageInfo.git.hash。Git 包可用，用于发现 tag URL 锁在旧 hash 的情况。
        public string GitHash;

        /// PackageInfo.git.revision。Git 包可用，可能是 tag，也可能是具体 commit。
        public string GitRevision;

        /// 当前 registry 目标期望的 hash。Stable 来自远端 tag，Dev 来自锁定 commit。
        public string ExpectedHash;

        /// 该依赖是否由 Linker 管理(URL 匹配本仓库托管格式或本机 file:)。
        public bool IsManaged;

        public InstalledPackageInfo(string packageName, string resolvedUrl, bool isManaged)
        {
            PackageName = packageName;
            ResolvedUrl = resolvedUrl;
            DeclaredUrl = resolvedUrl;
            IsManaged = isManaged;
        }

        public InstalledPackageInfo(
            string packageName,
            string resolvedUrl,
            bool isManaged,
            string declaredUrl,
            string packageIdUrl,
            string gitHash,
            string gitRevision,
            string expectedHash)
            : this(packageName, resolvedUrl, isManaged)
        {
            DeclaredUrl = declaredUrl;
            PackageIdUrl = packageIdUrl;
            GitHash = gitHash;
            GitRevision = gitRevision;
            ExpectedHash = expectedHash;
        }
    }
}
