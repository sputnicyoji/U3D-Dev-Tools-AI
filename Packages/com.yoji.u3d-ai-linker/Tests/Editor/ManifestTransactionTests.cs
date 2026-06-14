using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Yoji.U3DAILinker.Operations;

namespace Yoji.U3DAILinker.Tests
{
    public sealed class ManifestTransactionTests
    {
        private string m_Root;
        private string m_ManifestPath;
        private string m_StateDir;

        [SetUp]
        public void SetUp()
        {
            m_Root = Path.Combine(Path.GetTempPath(), "u3d-ai-linker-tests", Path.GetRandomFileName());
            Directory.CreateDirectory(Path.Combine(m_Root, "Packages"));
            m_ManifestPath = Path.Combine(m_Root, "Packages", "manifest.json");
            m_StateDir = Path.Combine(m_Root, "Library", "U3DAILinker");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(m_Root)) Directory.Delete(m_Root, true);
        }

        private void WriteManifest(string json) => File.WriteAllText(m_ManifestPath, json);

        private JObject ReadManifest() => JObject.Parse(File.ReadAllText(m_ManifestPath));

        private const string RepoUrl =
            "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/com.yoji.editor-debug#editor-debug-v1.2.0";

        [Test]
        public void OperationRecord_RoundTripsThroughJson()
        {
            var rec = new OperationRecord
            {
                OperationId = "op123",
                Channel = "stable",
                Revision = "editor-debug-v1.2.0",
                BackupPath = "Library/U3DAILinker/backups/manifest-op123.json",
                Status = "committed",
                DependencyChanges = new List<DependencyChange>
                {
                    new DependencyChange
                    {
                        PackageName = "com.yoji.editor-debug",
                        ChangeType = "Add",
                        OldValue = null,
                        NewValue = "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/com.yoji.editor-debug#editor-debug-v1.2.0",
                    },
                },
            };

            var json = JsonConvert.SerializeObject(rec);
            var back = JsonConvert.DeserializeObject<OperationRecord>(json);

            Assert.AreEqual("op123", back.OperationId);
            Assert.AreEqual("stable", back.Channel);
            Assert.AreEqual("committed", back.Status);
            Assert.AreEqual(1, back.DependencyChanges.Count);
            Assert.AreEqual("com.yoji.editor-debug", back.DependencyChanges[0].PackageName);
            Assert.IsNull(back.DependencyChanges[0].OldValue);
            Assert.AreEqual("Add", back.DependencyChanges[0].ChangeType);
        }

        [Test]
        public void Apply_AddsManagedDep_PreservesUnknownFieldsAndForeignDeps()
        {
            WriteManifest(@"{
  ""dependencies"": {
    ""com.unity.modules.ai"": ""1.0.0"",
    ""com.thirdparty.cool"": ""2.3.4""
  },
  ""scopedRegistries"": [ { ""name"": ""npm"", ""url"": ""https://example.com"" } ],
  ""enableLockFile"": true
}");
            var tx = new ManifestTransaction(m_ManifestPath, m_StateDir);
            var plan = new ManifestPlan("op1", "stable", "editor-debug-v1.2.0");
            plan.Edits.Add(new ManifestEdit("com.yoji.editor-debug", ManifestChangeType.Add, RepoUrl));

            var result = tx.Apply(plan, acceptConflicts: false);

            Assert.IsTrue(result.Committed, result.FailureReason);
            var m = ReadManifest();
            var deps = (JObject)m["dependencies"];
            Assert.AreEqual(RepoUrl, (string)deps["com.yoji.editor-debug"]);
            // 第三方依赖与未知顶层字段原样保留
            Assert.AreEqual("2.3.4", (string)deps["com.thirdparty.cool"]);
            Assert.AreEqual("1.0.0", (string)deps["com.unity.modules.ai"]);
            Assert.IsNotNull(m["scopedRegistries"]);
            Assert.AreEqual(true, (bool)m["enableLockFile"]);
        }

        [Test]
        public void Apply_WritesBackupAndOperationLog_AndCleansTmp()
        {
            WriteManifest(@"{ ""dependencies"": { } }");
            var tx = new ManifestTransaction(m_ManifestPath, m_StateDir);
            var plan = new ManifestPlan("opBackup", "stable", "editor-debug-v1.2.0");
            plan.Edits.Add(new ManifestEdit("com.yoji.editor-debug", ManifestChangeType.Add, RepoUrl));

            var result = tx.Apply(plan, acceptConflicts: false);

            Assert.IsTrue(result.Committed, result.FailureReason);
            var backup = Path.Combine(m_StateDir, "backups", "manifest-opBackup.json");
            Assert.IsTrue(File.Exists(backup), "backup must exist");
            Assert.AreEqual(backup, result.Record.BackupPath);
            var opLog = Path.Combine(m_StateDir, "operation.json");
            Assert.IsTrue(File.Exists(opLog), "operation.json must exist");
            Assert.AreEqual("committed", result.Record.Status);
            Assert.AreEqual(1, result.Record.DependencyChanges.Count);
            Assert.AreEqual("Add", result.Record.DependencyChanges[0].ChangeType);
            Assert.IsNull(result.Record.DependencyChanges[0].OldValue);
            Assert.AreEqual(RepoUrl, result.Record.DependencyChanges[0].NewValue);
            // tmp 不残留
            Assert.IsFalse(File.Exists(m_ManifestPath + ".u3d-ai-linker.tmp"));
        }

        [Test]
        public void Apply_UpdatesExistingManagedDep_RecordsOldValue()
        {
            var oldUrl = "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/com.yoji.editor-debug#editor-debug-v1.0.0";
            WriteManifest("{ \"dependencies\": { \"com.yoji.editor-debug\": \"" + oldUrl + "\" } }");
            var tx = new ManifestTransaction(m_ManifestPath, m_StateDir);
            var plan = new ManifestPlan("opUpd", "stable", "editor-debug-v1.2.0");
            plan.Edits.Add(new ManifestEdit("com.yoji.editor-debug", ManifestChangeType.Update, RepoUrl));

            var result = tx.Apply(plan, acceptConflicts: false);

            Assert.IsTrue(result.Committed, result.FailureReason);
            Assert.AreEqual(RepoUrl, (string)((JObject)ReadManifest()["dependencies"])["com.yoji.editor-debug"]);
            var change = result.Record.DependencyChanges[0];
            Assert.AreEqual("Update", change.ChangeType);
            Assert.AreEqual(oldUrl, change.OldValue);
            Assert.AreEqual(RepoUrl, change.NewValue);
        }

        [Test]
        public void Apply_RemovesManagedDep_RecordsRemove()
        {
            WriteManifest("{ \"dependencies\": { \"com.yoji.editor-debug\": \"" + RepoUrl + "\", \"com.unity.x\": \"1.0.0\" } }");
            var tx = new ManifestTransaction(m_ManifestPath, m_StateDir);
            var plan = new ManifestPlan("opRem", "stable", null);
            plan.Edits.Add(new ManifestEdit("com.yoji.editor-debug", ManifestChangeType.Remove, null));

            var result = tx.Apply(plan, acceptConflicts: false);

            Assert.IsTrue(result.Committed, result.FailureReason);
            var deps = (JObject)ReadManifest()["dependencies"];
            Assert.IsNull(deps["com.yoji.editor-debug"]);
            Assert.IsNotNull(deps["com.unity.x"]);   // 非托管依赖不动
            Assert.AreEqual("Remove", result.Record.DependencyChanges[0].ChangeType);
            Assert.AreEqual(RepoUrl, result.Record.DependencyChanges[0].OldValue);
            Assert.IsNull(result.Record.DependencyChanges[0].NewValue);
        }

        [Test]
        public void Apply_RejectsWhenExistingDepIsUnmanaged_AndDoesNotTouchAnything()
        {
            WriteManifest("{ \"dependencies\": { \"com.yoji.editor-debug\": \"9.9.9\" } }");
            var before = File.ReadAllText(m_ManifestPath);
            var tx = new ManifestTransaction(m_ManifestPath, m_StateDir);
            var plan = new ManifestPlan("opConf", "stable", "editor-debug-v1.2.0");
            plan.Edits.Add(new ManifestEdit("com.yoji.editor-debug", ManifestChangeType.Update, RepoUrl));

            var result = tx.Apply(plan, acceptConflicts: false);

            Assert.IsFalse(result.Committed);
            Assert.AreEqual(1, result.Conflicts.Count);
            Assert.AreEqual("com.yoji.editor-debug", result.Conflicts[0].PackageName);
            Assert.AreEqual("9.9.9", result.Conflicts[0].ExistingValue);
            // 冲突在写任何文件前就拒绝：manifest 原样、无 backup、无 op log、无 tmp
            Assert.AreEqual(before, File.ReadAllText(m_ManifestPath));
            Assert.IsFalse(Directory.Exists(Path.Combine(m_StateDir, "backups")));
            Assert.IsFalse(File.Exists(Path.Combine(m_StateDir, "operation.json")));
            Assert.IsFalse(File.Exists(m_ManifestPath + ".u3d-ai-linker.tmp"));
        }

        [Test]
        public void Apply_AcceptConflicts_TakesOverUnmanagedDep()
        {
            WriteManifest("{ \"dependencies\": { \"com.yoji.editor-debug\": \"9.9.9\" } }");
            var tx = new ManifestTransaction(m_ManifestPath, m_StateDir);
            var plan = new ManifestPlan("opTake", "stable", "editor-debug-v1.2.0");
            plan.Edits.Add(new ManifestEdit("com.yoji.editor-debug", ManifestChangeType.Update, RepoUrl));

            var result = tx.Apply(plan, acceptConflicts: true);

            Assert.IsTrue(result.Committed, result.FailureReason);
            Assert.AreEqual(RepoUrl, (string)((JObject)ReadManifest()["dependencies"])["com.yoji.editor-debug"]);
            Assert.AreEqual("9.9.9", result.Record.DependencyChanges[0].OldValue);
        }

        [Test]
        public void Apply_RemoveAbsentPackage_IsNoOpButStillCommitsOthers()
        {
            WriteManifest("{ \"dependencies\": { \"com.yoji.editor-debug\": \"" + RepoUrl + "\" } }");
            var tx = new ManifestTransaction(m_ManifestPath, m_StateDir);
            var plan = new ManifestPlan("opNoop", "stable", null);
            plan.Edits.Add(new ManifestEdit("com.yoji.not-installed", ManifestChangeType.Remove, null));
            plan.Edits.Add(new ManifestEdit("com.yoji.editor-debug", ManifestChangeType.Remove, null));

            var result = tx.Apply(plan, acceptConflicts: false);

            Assert.IsTrue(result.Committed, result.FailureReason);
            Assert.AreEqual(1, result.Record.DependencyChanges.Count);   // 只记真正发生的移除
            Assert.AreEqual("com.yoji.editor-debug", result.Record.DependencyChanges[0].PackageName);
        }

        [Test]
        public void Apply_RemoveUnmanagedDep_IsConflict()
        {
            WriteManifest("{ \"dependencies\": { \"com.yoji.editor-debug\": \"9.9.9\" } }");
            var tx = new ManifestTransaction(m_ManifestPath, m_StateDir);
            var plan = new ManifestPlan("opRemConf", "stable", null);
            plan.Edits.Add(new ManifestEdit("com.yoji.editor-debug", ManifestChangeType.Remove, null));

            var result = tx.Apply(plan, acceptConflicts: false);

            Assert.IsFalse(result.Committed);
            Assert.AreEqual(1, result.Conflicts.Count);
            Assert.AreEqual("9.9.9", result.Conflicts[0].ExistingValue);
        }
    }
}
