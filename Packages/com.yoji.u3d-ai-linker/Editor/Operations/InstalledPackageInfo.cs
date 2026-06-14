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

        /// 该依赖是否由 Linker 管理(URL 匹配本仓库托管格式或本机 file:)。
        public bool IsManaged;

        public InstalledPackageInfo(string packageName, string resolvedUrl, bool isManaged)
        {
            PackageName = packageName;
            ResolvedUrl = resolvedUrl;
            IsManaged = isManaged;
        }
    }
}
