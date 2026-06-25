using NUnit.Framework;
using UnityEngine;
using Yoji.U3DAILinker;
using Yoji.U3DAILinker.Settings;

namespace Yoji.U3DAILinker.Tests
{
    public sealed class U3DAILinkerSettingsTests
    {
        private static U3DAILinkerSettings NewProjectSettings()
        {
            var s = ScriptableObject.CreateInstance<U3DAILinkerSettings>();
            s.Channel = LinkerChannel.Stable;
            s.EnabledToolIds.Add("editor-debug");
            s.EnabledToolIds.Add("test-runner");
            s.DesiredVersions.Add(new DesiredVersion { ToolId = "editor-debug", Revision = "editor-debug-v1.2.0" });
            return s;
        }

        [Test]
        public void CleanProjectSettings_HasNoAbsolutePath()
        {
            var s = NewProjectSettings();
            Assert.IsFalse(s.ContainsAbsolutePath());
            Assert.IsTrue(s.Validate(out var error), error);
            Object.DestroyImmediate(s);
        }

        [Test]
        public void WindowsAbsolutePathInRevision_IsDetectedAndRejected()
        {
            var s = NewProjectSettings();
            s.DesiredVersions.Add(new DesiredVersion
            {
                ToolId = "local-hack",
                Revision = "file:C:/Example/U3D-Dev-Tools-AI/Packages/com.sputnicyoji.u3d-dev-tools-ai",
            });
            Assert.IsTrue(s.ContainsAbsolutePath());
            Assert.IsFalse(s.Validate(out var error));
            StringAssert.Contains("local-hack", error);
            Object.DestroyImmediate(s);
        }

        [Test]
        public void DriveLetterAbsolutePath_IsDetected()
        {
            var s = NewProjectSettings();
            s.DesiredVersions.Add(new DesiredVersion { ToolId = "x", Revision = @"E:\repo\pkg" });
            Assert.IsTrue(s.ContainsAbsolutePath());
            Object.DestroyImmediate(s);
        }

        [Test]
        public void UnixAbsolutePath_IsDetected()
        {
            var s = NewProjectSettings();
            s.DesiredVersions.Add(new DesiredVersion { ToolId = "x", Revision = "/home/me/repo" });
            Assert.IsTrue(s.ContainsAbsolutePath());
            Object.DestroyImmediate(s);
        }

        [Test]
        public void UserSettings_HoldsLocalRepoRoot()
        {
            var u = ScriptableObject.CreateInstance<U3DAILinkerUserSettings>();
            u.LocalRepoRoot = @"C:\Example\U3D-Dev-Tools-AI";
            Assert.AreEqual(@"C:\Example\U3D-Dev-Tools-AI", u.LocalRepoRoot);
            Object.DestroyImmediate(u);
        }
    }
}
