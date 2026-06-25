using NUnit.Framework;
using UnityEditor;
using Yoji.U3DAILinker.Operations;
using Yoji.U3DAILinker.Registry;
using Yoji.U3DAILinker.Tests.Operations;
using Yoji.U3DAILinker.Settings;

namespace Yoji.U3DAILinker.Tests
{
    // 验证 SettingsProvider 的注册元数据正确:路径 = "Project/U3D Dev Tools AI"、
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
                    Entry("editor-debug", "com.sputnicyoji.u3d-dev-tools-ai"),
                    Entry("foreign", "com.sputnicyoji.foreign"),
                    Entry("missing", "com.sputnicyoji.missing"),
                }
            };
            var probe = new FakeInstalledPackageProbe();
            probe.Set("com.sputnicyoji.u3d-dev-tools-ai",
                "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/com.sputnicyoji.u3d-dev-tools-ai#editor-debug-v0.1.0");
            probe.Set("com.sputnicyoji.foreign", "https://github.com/other/repo.git?path=/Packages/com.sputnicyoji.foreign#v1");

            var expectedHashes = new System.Collections.Generic.Dictionary<string, string>
            {
                ["com.sputnicyoji.u3d-dev-tools-ai"] = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            };

            var snapshot = U3DAILinkerSettingsProvider.BuildInstalledSnapshot(registry, probe, expectedHashes);

            Assert.AreEqual(2, snapshot.Count);
            Assert.IsTrue(snapshot["com.sputnicyoji.u3d-dev-tools-ai"].IsManaged);
            Assert.AreEqual("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                snapshot["com.sputnicyoji.u3d-dev-tools-ai"].ExpectedHash);
            Assert.IsFalse(snapshot["com.sputnicyoji.foreign"].IsManaged);
            Assert.IsFalse(snapshot.ContainsKey("com.sputnicyoji.missing"));
        }

        [Test]
        public void BuildDiagnosticReport_IncludesRowsAndSelfStatusInputs()
        {
            var projectRoot = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "yoji-linker-ports-" + System.IO.Path.GetRandomFileName());
            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.Combine(projectRoot, ".u3d-ai-linker"));
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(projectRoot, ".u3d-ai-linker", "ports.json"),
                    @"{
  ""schemaVersion"": 1,
  ""projectRoot"": ""E:/Project"",
  ""projectId"": ""test-project"",
  ""updatedUtc"": ""2026-06-17T10:30:00.0000000Z"",
  ""instances"": [
    {
      ""serviceId"": ""test-runner-mcp"",
      ""displayName"": ""Test Runner MCP"",
      ""instanceId"": ""instance-1"",
      ""processId"": 1234,
      ""projectRoot"": ""E:/Project"",
      ""projectId"": ""test-project"",
      ""host"": ""127.0.0.1"",
      ""port"": 21900,
      ""portSource"": ""project-auto"",
      ""startedUtc"": ""2026-06-17T10:20:00.0000000Z"",
      ""lastSeenUtc"": ""2026-06-17T10:30:00.0000000Z""
    }
  ]
}");

                var project = ScriptableObjectFactory.CreateSettings(LinkerChannel.Stable, "editor-debug");
                var user = ScriptableObjectFactory.CreateUserSettings("C:/Example/U3D-Dev-Tools-AI");
                var registry = new LinkerRegistry
                {
                    Channel = LinkerChannel.Stable,
                    Entries =
                    {
                        Entry("editor-debug", "com.sputnicyoji.u3d-dev-tools-ai"),
                    }
                };
                var installed = new System.Collections.Generic.Dictionary<string, InstalledPackageInfo>
                {
                    ["com.sputnicyoji.u3d-dev-tools-ai"] = new InstalledPackageInfo(
                        "com.sputnicyoji.u3d-dev-tools-ai",
                        "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/com.sputnicyoji.u3d-dev-tools-ai#editor-debug-v0.1.0",
                        true,
                        "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/com.sputnicyoji.u3d-dev-tools-ai#editor-debug-v0.1.0",
                        "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/com.sputnicyoji.u3d-dev-tools-ai#editor-debug-v0.1.0",
                        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                        "editor-debug-v0.1.0",
                        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"),
                };

                var report = U3DAILinkerSettingsProvider.BuildDiagnosticReport(
                    project,
                    user,
                    registry,
                    installed,
                    new System.Collections.Generic.Dictionary<string, AgentState>(),
                    RegistrySource.BundledSnapshot,
                    OperationState.Idle,
                    "stable",
                    null,
                    projectRoot,
                    "E:/Project/Library/PackageCache/com.sputnicyoji.u3d-dev-tools-ai");

                StringAssert.Contains("U3D Dev Tools AI Diagnostic Report", report);
                StringAssert.Contains("Channel: Stable", report);
                StringAssert.Contains("editor-debug", report);
                StringAssert.Contains("install=Installed", report);
                StringAssert.Contains("currentHash=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", report);
                StringAssert.Contains("targetHash=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", report);
                StringAssert.Contains("SelfPackage:", report);
                StringAssert.Contains("Ports:", report);
                StringAssert.Contains("test-runner-mcp", report);
                StringAssert.Contains("21900", report);
            }
            finally
            {
                if (System.IO.Directory.Exists(projectRoot))
                    System.IO.Directory.Delete(projectRoot, true);
            }
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

        private static class ScriptableObjectFactory
        {
            public static U3DAILinkerSettings CreateSettings(LinkerChannel channel, params string[] enabled)
            {
                var settings = UnityEngine.ScriptableObject.CreateInstance<U3DAILinkerSettings>();
                settings.Channel = channel;
                settings.EnabledToolIds.AddRange(enabled);
                return settings;
            }

            public static U3DAILinkerUserSettings CreateUserSettings(string localRepoRoot)
            {
                var settings = UnityEngine.ScriptableObject.CreateInstance<U3DAILinkerUserSettings>();
                settings.LocalRepoRoot = localRepoRoot;
                return settings;
            }
        }
    }
}
