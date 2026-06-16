using NUnit.Framework;
using UnityEditor;
using Yoji.U3DAILinker.Operations;
using Yoji.U3DAILinker.Registry;
using Yoji.U3DAILinker.Tests.Operations;
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

        [Test]
        public void AgentButtons_AreEnabledWhenRegistryLoadedAndIdle()
        {
            Assert.IsFalse(U3DAILinkerSettingsProvider.AreAgentButtonsEnabled(OperationState.Running, registryLoaded: true));
            Assert.IsFalse(U3DAILinkerSettingsProvider.AreAgentButtonsEnabled(OperationState.Idle, registryLoaded: false));
            Assert.IsTrue(U3DAILinkerSettingsProvider.AreAgentButtonsEnabled(OperationState.Idle, registryLoaded: true));
        }

        [Test]
        public void RegistryChannelForProject_UsesDevSnapshotForLocalAndDev()
        {
            Assert.AreEqual(Registry.RegistryChannel.Stable,
                U3DAILinkerSettingsProvider.RegistryChannelForProject(LinkerChannel.Stable));
            Assert.AreEqual(Registry.RegistryChannel.Dev,
                U3DAILinkerSettingsProvider.RegistryChannelForProject(LinkerChannel.Dev));
            Assert.AreEqual(Registry.RegistryChannel.Dev,
                U3DAILinkerSettingsProvider.RegistryChannelForProject(LinkerChannel.Local));
        }

        [Test]
        public void BuildInstalledSnapshot_ReadsPackageProbeAndMarksManagedUrls()
        {
            var registry = new LinkerRegistry
            {
                Entries =
                {
                    Entry("editor-debug", "com.yoji.editor-debug"),
                    Entry("foreign", "com.yoji.foreign"),
                    Entry("missing", "com.yoji.missing"),
                }
            };
            var probe = new FakeInstalledPackageProbe();
            probe.Set("com.yoji.editor-debug",
                "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/com.yoji.editor-debug#editor-debug-v0.1.0");
            probe.Set("com.yoji.foreign", "https://github.com/other/repo.git?path=/Packages/com.yoji.foreign#v1");

            var snapshot = U3DAILinkerSettingsProvider.BuildInstalledSnapshot(registry, probe);

            Assert.AreEqual(2, snapshot.Count);
            Assert.IsTrue(snapshot["com.yoji.editor-debug"].IsManaged);
            Assert.IsFalse(snapshot["com.yoji.foreign"].IsManaged);
            Assert.IsFalse(snapshot.ContainsKey("com.yoji.missing"));
        }

        private static RegistryEntryView Entry(string id, string packageName)
        {
            return new RegistryEntryView
            {
                Id = id,
                PackageName = packageName,
                PackagePath = "Packages/" + packageName,
                Status = ToolStatus.Ready,
                Kind = ToolKind.Tool,
                Revision = id + "-v0.1.0",
            };
        }
    }
}
