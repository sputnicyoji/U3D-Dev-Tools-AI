using NUnit.Framework;
using UnityEngine;
using Yoji.U3DAILinker;
using Yoji.U3DAILinker.Settings;

namespace Yoji.U3DAILinker.Tests
{
    public sealed class U3DAILinkerSettingsStoreTests
    {
        [Test]
        public void ProjectSettingsPath_IsUnderProjectSettingsFolder()
        {
            StringAssert.StartsWith("ProjectSettings/", U3DAILinkerSettingsStore.ProjectSettingsAssetPath);
            StringAssert.EndsWith("U3DAILinkerSettings.asset", U3DAILinkerSettingsStore.ProjectSettingsAssetPath);
        }

        [Test]
        public void UserSettingsPath_IsUnderUserSettingsFolder()
        {
            StringAssert.StartsWith("UserSettings/", U3DAILinkerSettingsStore.UserSettingsAssetPath);
            StringAssert.EndsWith("U3DAILinkerUserSettings.asset", U3DAILinkerSettingsStore.UserSettingsAssetPath);
        }

        [Test]
        public void SaveProjectSettings_RejectsAbsolutePathPollution()
        {
            var s = ScriptableObject.CreateInstance<U3DAILinkerSettings>();
            s.DesiredVersions.Add(new DesiredVersion
            {
                ToolId = "bad", Revision = "file:C:/Example/U3D-Dev-Tools-AI/Packages/com.sputnicyoji.x",
            });
            Assert.IsFalse(U3DAILinkerSettingsStore.TrySaveProjectSettings(s, out var error));
            StringAssert.Contains("bad", error);
            Object.DestroyImmediate(s);
        }
    }
}
