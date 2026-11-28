using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;

namespace Yoji.TestRunner.Tests
{
    public class PlayModeSettingsScopeTests
    {
        private string m_Dir;
        private string m_BackupPath;
        private bool m_Enabled;
        private EnterPlayModeOptions m_Options;
        private int m_Writes;

        [SetUp] public void SetUp()
        {
            m_Dir = Path.Combine(Path.GetTempPath(), "trmcp_pm_" + Guid.NewGuid().ToString("N"));
            m_BackupPath = Path.Combine(m_Dir, "nested", "backup.json"); // Apply must create the folder
            m_Enabled = false;
            m_Options = EnterPlayModeOptions.DisableSceneReload;
            m_Writes = 0;
        }

        [TearDown] public void TearDown()
        {
            try { if (Directory.Exists(m_Dir)) Directory.Delete(m_Dir, true); } catch { }
        }

        // Fake EditorSettings: tests never touch the real project settings.
        private PlayModeSettingsScope New() => new PlayModeSettingsScope(
            m_BackupPath,
            () => new PlayModeSettings { Enabled = m_Enabled, Options = m_Options },
            s => { m_Enabled = s.Enabled; m_Options = s.Options; m_Writes++; });

        private void AssertOriginals()
        {
            Assert.IsFalse(m_Enabled);
            Assert.AreEqual(EnterPlayModeOptions.DisableSceneReload, m_Options);
        }

        [Test] public void Apply_AddsDisableDomainReload_RestorePutsOriginalsBack()
        {
            var scope = New();
            scope.Apply();
            Assert.IsTrue(m_Enabled);
            Assert.AreEqual(EnterPlayModeOptions.DisableSceneReload | EnterPlayModeOptions.DisableDomainReload, m_Options);
            Assert.IsTrue(File.Exists(m_BackupPath));

            scope.Restore();
            AssertOriginals();
            Assert.IsFalse(File.Exists(m_BackupPath));
        }

        [Test] public void Restore_InNewInstance_UsesBackup_AfterReloadOrCrash()
        {
            New().Apply();
            New().Restore(); // memory copy is gone, as after a domain reload or an editor restart
            AssertOriginals();
            Assert.IsFalse(File.Exists(m_BackupPath));
        }

        [Test] public void Restore_WithNothingPending_DoesNotWrite()
        {
            var scope = New();
            scope.Restore();
            Assert.AreEqual(0, m_Writes);
            scope.Apply();
            scope.Restore();
            var writes = m_Writes;
            scope.Restore();
            Assert.AreEqual(writes, m_Writes);
            AssertOriginals();
        }

        [Test] public void Apply_WithLeftoverBackup_KeepsUserOriginals_NotTheOverride()
        {
            New().Apply();      // cut off: override live, backup left behind
            var next = New();
            next.Apply();       // must not snapshot the override as the "original"
            next.Restore();
            AssertOriginals();
        }

        // Restore put the originals back but could not delete the backup; the user then changed the settings.
        private void LeaveStaleBackupThenChangeSettings()
        {
            var scope = New();
            scope.Apply();
            var leftover = File.ReadAllText(m_BackupPath);
            scope.Restore();
            File.WriteAllText(m_BackupPath, leftover);
            m_Enabled = true;
            m_Options = EnterPlayModeOptions.None;
        }

        [Test] public void Restore_WithStaleBackup_KeepsUserChange_AndDropsBackup()
        {
            LeaveStaleBackupThenChangeSettings();
            var writes = m_Writes;
            New().Restore(); // next service start
            Assert.AreEqual(writes, m_Writes);
            Assert.IsFalse(File.Exists(m_BackupPath));
        }

        [Test] public void Apply_WithStaleBackup_SnapshotsLiveSettings()
        {
            LeaveStaleBackupThenChangeSettings();
            var scope = New();
            scope.Apply();   // next PlayMode run
            scope.Restore();
            Assert.IsTrue(m_Enabled);
            Assert.AreEqual(EnterPlayModeOptions.None, m_Options);
        }
    }
}
