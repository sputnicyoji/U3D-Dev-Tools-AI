namespace Yoji.U3DAILinker.Operations
{
    /// 读取目标工程当前已解析包的来源 URL（用于恢复时核对目标是否达成）。
    /// 抽象掉 PackageInfo / Client.List，使恢复核对逻辑可在 EditMode 用 fake 单测。
    internal interface IInstalledPackageProbe
    {
        /// 返回 packageName 当前已安装的来源标识（Git URL / file: 路径）；未安装返回 null。
        string GetInstalledUrl(string packageName);
    }
}
