using System;
using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Yoji.U3DAILinker.Operations;
using Yoji.U3DAILinker.Registry;
using Yoji.U3DAILinker.Settings;
using Yoji.U3DAILinker.Tests.Operations;

namespace Yoji.U3DAILinker.Tests.Settings
{
    public sealed class U3DAILinkerActionServiceTests
    {
        private const string DevSha = "0123456789abcdef0123456789abcdef01234567";

        private string m_ProjectRoot;
        private string m_ManifestPath;
        private FakeUpmClient m_Upm;
        private FakeInstalledPackageProbe m_Probe;

        [SetUp]
        public void SetUp()
        {
            m_ProjectRoot = Path.Combine(Path.GetTempPath(), "u3dlinker_action_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(m_ProjectRoot, "Packages"));
            Directory.CreateDirectory(Path.Combine(m_ProjectRoot, "Library"));
            m_ManifestPath = Path.Combine(m_ProjectRoot, "Packages", "manifest.json");
            File.WriteAllText(m_ManifestPath, "{ \"dependencies\": {} }");
            m_Upm = new FakeUpmClient();
            m_Probe = new FakeInstalledPackageProbe();
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(m_ProjectRoot))
                    Directory.Delete(m_ProjectRoot, true);
            }
            catch
            {
                // Best effort cleanup for Windows file locks in Unity tests.
            }
        }

        [Test]
        public void InstallOrUpdateAll_WritesManifestAndRequestsFirstDependency()
        {
            var service = new U3DAILinkerActionService(
                m_ProjectRoot,
                m_Upm,
                m_Probe,
                devSha: null);

            var settings = ScriptableObjectFactory.CreateSettings(LinkerChannel.Local, "editor-debug");
            var user = ScriptableObjectFactory.CreateUserSettings("E:/Yoji/U3D-Dev-Tools-AI");

            var result = service.InstallOrUpdateAll(
                RegistryWithReadyEditorDebug(),
                settings,
                user,
                acceptConflicts: false);

            Assert.IsTrue(result.Success, result.Error);

            var deps = (JObject)JObject.Parse(File.ReadAllText(m_ManifestPath))["dependencies"];
            Assert.AreEqual(
                "file:E:/Yoji/U3D-Dev-Tools-AI/Packages/com.yoji.editor-core",
                (string)deps["com.yoji.editor-core"]);
            Assert.AreEqual(
                "file:E:/Yoji/U3D-Dev-Tools-AI/Packages/com.yoji.editor-debug",
                (string)deps["com.yoji.editor-debug"]);

            Assert.AreEqual(1, m_Upm.AddCalls.Count);
            Assert.AreEqual(
                "file:E:/Yoji/U3D-Dev-Tools-AI/Packages/com.yoji.editor-core",
                m_Upm.AddCalls[0]);

            var logPath = Path.Combine(m_ProjectRoot, "Library", "U3DAILinker", "operation.json");
            Assert.IsTrue(File.Exists(logPath));
            var log = JObject.Parse(File.ReadAllText(logPath));
            Assert.AreEqual(OperationPhase.PackageRequested, (string)log["Phase"]);
            Assert.AreEqual("install-all", (string)log["Action"]);
        }

        [Test]
        public void InstallOrUpdateAll_WhenNoReadyTools_FailsWithoutPretendingSuccess()
        {
            var service = new U3DAILinkerActionService(
                m_ProjectRoot,
                m_Upm,
                m_Probe,
                devSha: null);
            var settings = ScriptableObjectFactory.CreateSettings(LinkerChannel.Stable, "editor-debug");
            var user = ScriptableObjectFactory.CreateUserSettings("E:/Yoji/U3D-Dev-Tools-AI");

            var result = service.InstallOrUpdateAll(
                RegistryWithNoReadyTools(),
                settings,
                user,
                acceptConflicts: false);

            Assert.IsFalse(result.Success);
            StringAssert.Contains("no installable ready tools", result.Error);
            Assert.AreEqual(0, m_Upm.AddCalls.Count);
            var deps = (JObject)JObject.Parse(File.ReadAllText(m_ManifestPath))["dependencies"];
            Assert.AreEqual(0, deps.Count);
        }

        [Test]
        public void InstallOrUpdateAll_DevChannelUsesResolvedShaInsteadOfRegistryPlaceholder()
        {
            var service = new U3DAILinkerActionService(
                m_ProjectRoot,
                m_Upm,
                m_Probe,
                devSha: DevSha);
            var settings = ScriptableObjectFactory.CreateSettings(LinkerChannel.Dev, "editor-debug");
            var user = ScriptableObjectFactory.CreateUserSettings("E:/Yoji/U3D-Dev-Tools-AI");

            var result = service.InstallOrUpdateAll(
                RegistryWithReadyEditorDebug(LinkerChannel.Dev, PlaceholderSha),
                settings,
                user,
                acceptConflicts: false);

            Assert.IsTrue(result.Success, result.Error);
            var deps = (JObject)JObject.Parse(File.ReadAllText(m_ManifestPath))["dependencies"];
            StringAssert.EndsWith("#" + DevSha, (string)deps["com.yoji.editor-core"]);
            StringAssert.EndsWith("#" + DevSha, (string)deps["com.yoji.editor-debug"]);
            StringAssert.DoesNotContain(PlaceholderSha, (string)deps["com.yoji.editor-debug"]);
        }

        [Test]
        public void InstallOrUpdateAll_DevChannelWithoutResolvedSha_FailsBeforeManifestWrite()
        {
            var service = new U3DAILinkerActionService(
                m_ProjectRoot,
                m_Upm,
                m_Probe,
                devSha: null);
            var settings = ScriptableObjectFactory.CreateSettings(LinkerChannel.Dev, "editor-debug");
            var user = ScriptableObjectFactory.CreateUserSettings("E:/Yoji/U3D-Dev-Tools-AI");

            var result = service.InstallOrUpdateAll(
                RegistryWithReadyEditorDebug(LinkerChannel.Dev, PlaceholderSha),
                settings,
                user,
                acceptConflicts: false);

            Assert.IsFalse(result.Success);
            StringAssert.Contains("Dev channel requires a resolved commit SHA", result.Error);
            Assert.AreEqual(0, m_Upm.AddCalls.Count);
            var deps = (JObject)JObject.Parse(File.ReadAllText(m_ManifestPath))["dependencies"];
            Assert.AreEqual(0, deps.Count);
        }

        [Test]
        public void RollbackManifest_FallsBackToLastManifestRecordAfterQueueLogIsCleared()
        {
            var service = new U3DAILinkerActionService(
                m_ProjectRoot,
                m_Upm,
                m_Probe,
                devSha: null);
            var settings = ScriptableObjectFactory.CreateSettings(LinkerChannel.Local, "editor-debug");
            var user = ScriptableObjectFactory.CreateUserSettings("E:/Yoji/U3D-Dev-Tools-AI");

            var install = service.InstallOrUpdateAll(
                RegistryWithReadyEditorDebug(),
                settings,
                user,
                acceptConflicts: false);
            Assert.IsTrue(install.Success, install.Error);

            var queueLogPath = Path.Combine(m_ProjectRoot, "Library", "U3DAILinker", "operation.json");
            File.Delete(queueLogPath); // Simulates queue completion clearing the transient log.

            var rollback = service.RollbackManifest();

            Assert.IsTrue(rollback.Success, rollback.Error);
            var deps = (JObject)JObject.Parse(File.ReadAllText(m_ManifestPath))["dependencies"];
            Assert.AreEqual(0, deps.Count);
        }

        private static LinkerRegistry RegistryWithReadyEditorDebug()
            => RegistryWithReadyEditorDebug(LinkerChannel.Local, "editor-core-v0.1.0");

        private const string PlaceholderSha = "0000000000000000000000000000000000000000";

        private static LinkerRegistry RegistryWithReadyEditorDebug(LinkerChannel channel, string revision)
        {
            return new LinkerRegistry
            {
                SchemaVersion = 1,
                Channel = channel,
                Branch = "main",
                Entries =
                {
                    new RegistryEntryView
                    {
                        Id = "editor-core",
                        Status = ToolStatus.Ready,
                        Kind = ToolKind.Infra,
                        Order = 10,
                        PackageName = "com.yoji.editor-core",
                        PackagePath = "Packages/com.yoji.editor-core",
                        Revision = revision,
                        DefaultEnabled = false,
                        UserToggle = false,
                        MinUnity = "2022.3",
                    },
                    new RegistryEntryView
                    {
                        Id = "editor-debug",
                        Status = ToolStatus.Ready,
                        Kind = ToolKind.Tool,
                        Order = 20,
                        PackageName = "com.yoji.editor-debug",
                        PackagePath = "Packages/com.yoji.editor-debug",
                        Revision = revision,
                        DefaultEnabled = true,
                        UserToggle = true,
                        MinUnity = "2022.3",
                        DependsOn = { "editor-core" },
                    },
                },
            };
        }

        private static LinkerRegistry RegistryWithNoReadyTools()
        {
            var registry = RegistryWithReadyEditorDebug(LinkerChannel.Stable, "editor-debug-v0.1.0");
            foreach (var entry in registry.Entries)
                entry.Status = entry.Kind == ToolKind.Infra ? ToolStatus.Planned : ToolStatus.SkillOnly;
            return registry;
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
