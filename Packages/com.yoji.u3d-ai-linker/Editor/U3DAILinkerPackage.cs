namespace Yoji.U3DAILinker.Settings
{
    /// <summary>
    /// Linker 包的单一可信常量源。包名、显示名、Project Settings 路径与根命名空间
    /// 都集中在此,供 SettingsProvider 注册、Registry 加载、诊断报告等处引用,避免散落漂移。
    /// </summary>
    public static class U3DAILinkerPackage
    {
        /// <summary>UPM 包名,与 package.json 的 "name" 字段严格一致。</summary>
        public const string PackageName = "com.yoji.u3d-ai-linker";

        /// <summary>面板标题与菜单叶子名,与 package.json 的 "displayName" 一致。</summary>
        public const string DisplayName = "U3D AI Linker";

        /// <summary>Project Settings 注册路径(SettingsScope.Project)。</summary>
        public const string SettingsPath = "Project/U3D AI Linker";

        /// <summary>主/测试程序集的根命名空间前缀,与 asmdef 的 rootNamespace 一致。</summary>
        public const string RootNamespace = "Yoji.U3DAILinker";
    }
}
