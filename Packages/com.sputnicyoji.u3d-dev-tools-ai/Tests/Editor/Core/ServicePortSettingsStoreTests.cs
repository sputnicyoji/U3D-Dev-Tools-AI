using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using Yoji.EditorCore.Ports;

namespace Yoji.EditorCore.Tests
{
    public sealed class ServicePortSettingsStoreTests
    {
        [Test]
        public void LoadOrCreateProjectSettings_CreatesFileWithStableProjectId()
        {
            var root = CreateTempRoot();

            var first = ServicePortSettingsStore.LoadOrCreateProjectSettings(root);
            var second = ServicePortSettingsStore.LoadOrCreateProjectSettings(root);
            var path = Path.Combine(root, "ProjectSettings", "YojiDevToolPorts.json");

            Assert.IsTrue(File.Exists(path));
            Assert.IsNotEmpty(first.ProjectId);
            Assert.AreEqual(first.ProjectId, second.ProjectId);
        }

        [Test]
        public void LoadOrCreateProjectSettings_PreservesExistingProjectId()
        {
            var root = CreateTempRoot();
            var path = Path.Combine(root, "ProjectSettings", "YojiDevToolPorts.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "{\"schemaVersion\":1,\"projectId\":\"abc123\",\"mode\":\"fixedProject\",\"preferredBasePort\":21900,\"serviceOverrides\":[]}");

            var settings = ServicePortSettingsStore.LoadOrCreateProjectSettings(root);

            Assert.AreEqual("abc123", settings.ProjectId);
            Assert.AreEqual("fixedProject", settings.Mode);
        }

        [Test]
        public void LoadOrCreateProjectSettings_MalformedJson_FallsBackAndRewrites()
        {
            var root = CreateTempRoot();
            var path = Path.Combine(root, "ProjectSettings", "YojiDevToolPorts.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "{not json");

            ServicePortSettings settings = null;
            Assert.DoesNotThrow(() => settings = ServicePortSettingsStore.LoadOrCreateProjectSettings(root));

            Assert.IsNotNull(settings);
            Assert.IsNotEmpty(settings.ProjectId);
            StringAssert.Contains("\"projectId\"", File.ReadAllText(path));
        }

        [Test]
        public void LoadUserSettings_MalformedJson_ReturnsDefault()
        {
            var root = CreateTempRoot();
            var path = Path.Combine(root, "UserSettings", "YojiDevToolPorts.user.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "{not json");

            ServicePortUserSettings settings = null;
            Assert.DoesNotThrow(() => settings = ServicePortSettingsStore.LoadUserSettings(root));

            Assert.IsNotNull(settings);
            Assert.AreEqual(0, settings.OverrideBasePort);
            Assert.IsTrue(settings.PreferLegacyPorts);
        }

        [Test]
        public void BuildPolicy_UsesProjectAndUserSettings()
        {
            var root = CreateTempRoot();
            ServicePortSettingsStore.SaveProjectSettings(root, new ServicePortSettings
            {
                SchemaVersion = 1,
                ProjectId = "project-01",
                Mode = "fixedProject",
                PreferredBasePort = 21900,
            });
            ServicePortSettingsStore.SaveUserSettings(root, new ServicePortUserSettings
            {
                SchemaVersion = 1,
                OverrideBasePort = 23000,
                PreferLegacyPorts = false,
            });

            var identity = new ProjectIdentity
            {
                ProjectRoot = root,
                ProjectId = "project-01",
            };
            var policy = ServicePortSettingsStore.BuildPolicy(root, identity);

            Assert.AreEqual(ServicePortMode.FixedProject, policy.Mode);
            Assert.AreEqual(21900, policy.PreferredBasePort);
            Assert.AreEqual(23000, policy.OverrideBasePort);
            Assert.IsFalse(policy.PreferLegacyPorts);
        }

        [TestCase(unchecked((int)0x80070020), true)]
        [TestCase(unchecked((int)0x80070497), true)]
        [TestCase(unchecked((int)0x80070498), true)]
        [TestCase(unchecked((int)0x80070499), false)]
        public void PortPersistenceIO_ClassifiesOnlyRecoverableReplaceFileErrors(
            int hResult,
            bool expectedTransient)
        {
            var ioType = typeof(ServicePortSettingsStore).Assembly.GetType(
                "Yoji.EditorCore.Ports.PortPersistenceIO",
                true);
            var classifier = ioType.GetMethod(
                "IsTransientWriteError",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.IsNotNull(classifier);
            var error = new HResultIOException(hResult);
            Assert.AreEqual(
                expectedTransient,
                (bool)classifier.Invoke(null, new object[] { error }));
        }

        private static string CreateTempRoot()
        {
            var root = Path.Combine(Path.GetTempPath(), "yoji-port-tests", Path.GetRandomFileName());
            Directory.CreateDirectory(root);
            return root;
        }

        private sealed class HResultIOException : IOException
        {
            public HResultIOException(int hResult)
                : base("synthetic ReplaceFile failure")
            {
                HResult = hResult;
            }
        }
    }
}
