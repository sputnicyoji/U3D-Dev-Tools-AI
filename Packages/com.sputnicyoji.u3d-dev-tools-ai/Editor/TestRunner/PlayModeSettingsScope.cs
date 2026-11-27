using System;
using System.IO;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace Yoji.TestRunner
{
    /// The user's Enter Play Mode settings as a PlayMode run found them.
    internal sealed class PlayModeSettings
    {
        public bool Enabled;
        public EnterPlayModeOptions Options;
    }

    /// TR-1a: a PlayMode run temporarily adds DisableDomainReload to the persisted EditorSettings.
    /// The originals are kept in memory for the normal path and in a backup file, so a run cut off by a
    /// domain reload or an editor crash (both drop the memory copy) is undone when the service starts again.
    /// The file lives under Library, not Temp: Unity empties Temp when it opens the project, Library survives.
    internal sealed class PlayModeSettingsScope
    {
        private readonly string m_BackupPath;
        private readonly Func<PlayModeSettings> m_Read;
        private readonly Action<PlayModeSettings> m_Write;
        private PlayModeSettings m_Saved;

        public PlayModeSettingsScope(string backupPath, Func<PlayModeSettings> read, Action<PlayModeSettings> write)
        {
            m_BackupPath = backupPath;
            m_Read = read;
            m_Write = write;
        }

        public static PlayModeSettingsScope ForEditor(string backupPath) => new PlayModeSettingsScope(
            backupPath,
            () => new PlayModeSettings
            {
                Enabled = EditorSettings.enterPlayModeOptionsEnabled,
                Options = EditorSettings.enterPlayModeOptions,
            },
            s =>
            {
                EditorSettings.enterPlayModeOptionsEnabled = s.Enabled;
                EditorSettings.enterPlayModeOptions = s.Options;
            });

        /// Saves the originals (memory, then file) before overriding, so a crash between the two cannot lose them.
        public void Apply()
        {
            // A pending snapshot or backup already holds the user's value; the live settings may be our override.
            m_Saved = m_Saved ?? LoadBackup() ?? m_Read();
            WriteBackup(m_Saved);
            // Add to the user's options (e.g. keep DisableSceneReload) instead of replacing them.
            m_Write(new PlayModeSettings { Enabled = true, Options = m_Saved.Options | EnterPlayModeOptions.DisableDomainReload });
        }

        /// Puts the originals back and drops the backup; no-op when nothing is pending.
        public void Restore()
        {
            var original = m_Saved ?? LoadBackup();
            if (original == null) return;
            m_Saved = null;
            m_Write(original);
            try { File.Delete(m_BackupPath); }
            catch (Exception e) { Debug.LogWarning("[TestRunnerMCP] could not delete the Play Mode settings backup: " + e.Message); }
        }

        private void WriteBackup(PlayModeSettings settings)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(m_BackupPath));
                File.WriteAllText(m_BackupPath, JsonConvert.SerializeObject(settings));
            }
            catch (Exception e)
            {
                // Not fatal: the memory copy still restores on the normal path; only crash recovery is lost.
                Debug.LogWarning("[TestRunnerMCP] could not write the Play Mode settings backup: " + e.Message);
            }
        }

        private PlayModeSettings LoadBackup()
        {
            if (!File.Exists(m_BackupPath)) return null;
            try { return JsonConvert.DeserializeObject<PlayModeSettings>(File.ReadAllText(m_BackupPath)); }
            catch { return null; }
        }
    }
}
