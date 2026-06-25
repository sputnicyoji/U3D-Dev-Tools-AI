using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Yoji.U3DAILinker.Operations;

namespace Yoji.U3DAILinker.Tests.Operations
{
    public class OperationLogStoreTests
    {
        private string m_LibraryRoot;

        [SetUp] public void SetUp()
        {
            m_LibraryRoot = Path.Combine(Path.GetTempPath(), "u3dlinker_" + System.Guid.NewGuid().ToString("N"));
        }

        [TearDown] public void TearDown()
        {
            try { if (Directory.Exists(m_LibraryRoot)) Directory.Delete(m_LibraryRoot, true); } catch { }
        }

        private OperationLogStore New() => new OperationLogStore(m_LibraryRoot);

        private static OperationLog Sample()
        {
            return new OperationLog
            {
                OperationId = "guid-1",
                Action = "install-all",
                ToolIds = new[] { "editor-debug" },
                CurrentIndex = 0,
                Phase = OperationPhase.PackageRequested,
                Channel = "stable",
                ResolvedRevision = "editor-debug-v1.2.0",
                ManifestBackupPath = "Library/U3DAILinker/backups/manifest-guid-1.json",
                DependencyChanges = new List<DependencyChange>
                {
                    new DependencyChange { PackageName = "com.sputnicyoji.u3d-dev-tools-ai", OldValue = null, NewValue = "url-1" },
                },
                Completed = new List<string>(),
                RetryCount = 0,
            };
        }

        [Test] public void LogPath_IsUnderLibraryOperationJson()
        {
            var store = New();
            Assert.AreEqual(Path.Combine(m_LibraryRoot, "U3DAILinker", "operation.json"), store.LogPath);
        }

        [Test] public void Load_WhenNoFile_ReturnsNull()
        {
            Assert.IsNull(New().Load());
        }

        [Test] public void Save_ThenLoad_RoundTripsAllFields()
        {
            var store = New();
            store.Save(Sample());
            var loaded = store.Load();

            Assert.IsNotNull(loaded);
            Assert.AreEqual("guid-1", loaded.OperationId);
            Assert.AreEqual("install-all", loaded.Action);
            Assert.AreEqual(1, loaded.ToolIds.Length);
            Assert.AreEqual("editor-debug", loaded.ToolIds[0]);
            Assert.AreEqual(0, loaded.CurrentIndex);
            Assert.AreEqual(OperationPhase.PackageRequested, loaded.Phase);
            Assert.AreEqual("stable", loaded.Channel);
            Assert.AreEqual("editor-debug-v1.2.0", loaded.ResolvedRevision);
            Assert.AreEqual("Library/U3DAILinker/backups/manifest-guid-1.json", loaded.ManifestBackupPath);
            Assert.AreEqual(1, loaded.DependencyChanges.Count);
            Assert.AreEqual("com.sputnicyoji.u3d-dev-tools-ai", loaded.DependencyChanges[0].PackageName);
            Assert.IsNull(loaded.DependencyChanges[0].OldValue);
            Assert.AreEqual("url-1", loaded.DependencyChanges[0].NewValue);
            Assert.AreEqual(0, loaded.Completed.Count);
            Assert.AreEqual(0, loaded.RetryCount);
        }

        [Test] public void Save_CreatesLibraryDirectory()
        {
            var store = New();
            store.Save(Sample());
            Assert.IsTrue(File.Exists(store.LogPath));
        }

        [Test] public void Save_OverwritesExistingLog_NoLeftoverTmp()
        {
            var store = New();
            store.Save(Sample());
            var second = Sample();
            second.CurrentIndex = 1;
            second.Phase = OperationPhase.Completed;
            store.Save(second);

            var loaded = store.Load();
            Assert.AreEqual(1, loaded.CurrentIndex);
            Assert.AreEqual(OperationPhase.Completed, loaded.Phase);
            Assert.IsFalse(File.Exists(store.LogPath + ".tmp"), "tmp 应在原子替换后消失");
        }

        [Test] public void Clear_RemovesLog()
        {
            var store = New();
            store.Save(Sample());
            store.Clear();
            Assert.IsFalse(File.Exists(store.LogPath));
            Assert.IsNull(store.Load());
        }

        [Test] public void Clear_WhenNoFile_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => New().Clear());
        }

        [Test] public void Load_WhenCorruptJson_ReturnsNull()
        {
            var store = New();
            Directory.CreateDirectory(Path.GetDirectoryName(store.LogPath));
            File.WriteAllText(store.LogPath, "{ not valid json");
            Assert.IsNull(store.Load());
        }
    }
}
