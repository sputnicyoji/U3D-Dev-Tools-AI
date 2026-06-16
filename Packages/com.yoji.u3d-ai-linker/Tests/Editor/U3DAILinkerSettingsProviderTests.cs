using NUnit.Framework;
using UnityEditor;
using Yoji.U3DAILinker.Settings;

namespace Yoji.U3DAILinker.Tests
{
    // 验证 SettingsProvider 的注册元数据正确:路径 = "Project/U3D AI Linker"、
    // 作用域 = Project。直接调用 CreateProvider() 取回 provider 对象断言,
    // 不打开 Project Settings 窗口、不触发 IMGUI 渲染,EditMode 安全。
    public sealed class U3DAILinkerSettingsProviderTests
    {
        [Test]
        public void CreateProvider_ReturnsNonNull()
        {
            SettingsProvider provider = U3DAILinkerSettingsProvider.CreateProvider();
            Assert.IsNotNull(provider, "CreateProvider() 不应返回 null");
        }

        [Test]
        public void CreateProvider_UsesPackageSettingsPath()
        {
            SettingsProvider provider = U3DAILinkerSettingsProvider.CreateProvider();
            Assert.AreEqual(U3DAILinkerPackage.SettingsPath, provider.settingsPath);
        }

        [Test]
        public void CreateProvider_IsProjectScoped()
        {
            SettingsProvider provider = U3DAILinkerSettingsProvider.CreateProvider();
            Assert.AreEqual(SettingsScope.Project, provider.scope);
        }

        [Test]
        public void CreateProvider_LabelIsDisplayName()
        {
            // SettingsProvider 的 label 默认取路径末段;显式断言它等于 DisplayName,
            // 防止有人改了路径却忘了同步显示名。
            SettingsProvider provider = U3DAILinkerSettingsProvider.CreateProvider();
            Assert.AreEqual(U3DAILinkerPackage.DisplayName, provider.label);
        }

        [Test]
        public void Actions_AreEnabledWhenIdle()
        {
            Assert.IsTrue(U3DAILinkerSettingsProvider.ActionsWired);
            Assert.IsTrue(U3DAILinkerSettingsProvider.AreActionButtonsEnabled(OperationState.Idle));
            Assert.IsFalse(U3DAILinkerSettingsProvider.AreActionButtonsEnabled(OperationState.Running));
        }
    }
}
