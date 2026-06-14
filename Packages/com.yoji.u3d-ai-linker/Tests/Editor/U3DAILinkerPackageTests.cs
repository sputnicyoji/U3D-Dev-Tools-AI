using NUnit.Framework;
using UnityEditor.PackageManager;
using Yoji.U3DAILinker.Settings;

namespace Yoji.U3DAILinker.Tests
{
    // 验证包级常量:这些值被 SettingsProvider、Registry 加载、诊断报告等多处引用,
    // 必须集中定义且不可漂移。纯编译期常量,EditMode 即可断言,无外部副作用。
    public sealed class U3DAILinkerPackageTests
    {
        [Test]
        public void PackageName_IsCanonicalUpmName()
        {
            Assert.AreEqual("com.yoji.u3d-ai-linker", U3DAILinkerPackage.PackageName);
        }

        [Test]
        public void DisplayName_MatchesSettingsLeaf()
        {
            // SettingsPath 的末段必须与 DisplayName 一致,保证面板标题与菜单项一致。
            Assert.AreEqual("U3D AI Linker", U3DAILinkerPackage.DisplayName);
        }

        [Test]
        public void SettingsPath_IsProjectScopedAndStable()
        {
            Assert.AreEqual("Project/U3D AI Linker", U3DAILinkerPackage.SettingsPath);
        }

        [Test]
        public void RootNamespace_MatchesAsmdef()
        {
            Assert.AreEqual("Yoji.U3DAILinker", U3DAILinkerPackage.RootNamespace);
        }

        [Test]
        public void PackageJson_NameMatchesConstant()
        {
            // 通过本程序集反查所属 UPM 包,断言 package.json 的 name 与常量一致。
            // PackageInfo.FindForAssembly 是离线本地查询,无网络副作用。
            PackageInfo info = PackageInfo.FindForAssembly(
                typeof(U3DAILinkerPackage).Assembly);
            Assert.IsNotNull(
                info,
                "应能通过程序集定位 com.yoji.u3d-ai-linker 包元数据");
            Assert.AreEqual(U3DAILinkerPackage.PackageName, info.name);
        }

        [Test]
        public void PackageJson_DisplayNameMatchesConstant()
        {
            PackageInfo info = PackageInfo.FindForAssembly(
                typeof(U3DAILinkerPackage).Assembly);
            Assert.IsNotNull(info);
            Assert.AreEqual(U3DAILinkerPackage.DisplayName, info.displayName);
        }
    }
}
