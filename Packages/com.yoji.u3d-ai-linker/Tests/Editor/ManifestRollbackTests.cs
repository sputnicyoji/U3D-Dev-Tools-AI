using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Yoji.U3DAILinker.Operations;

namespace Yoji.U3DAILinker.Tests
{
    public sealed class ManifestRollbackTests
    {
        private string m_Root;
        private string m_ManifestPath;
        private string m_StateDir;

        private const string RepoUrl =
            "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/com.yoji.editor-debug#editor-debug-v1.2.0";

        [SetUp]
        public void SetUp()
        {
            m_Root = Path.Combine(Path.GetTempPath(), "u3d-ai-linker-rb", Path.GetRandomFileName());
            Directory.CreateDirectory(Path.Combine(m_Root, "Packages"));
            m_ManifestPath = Path.Combine(m_Root, "Packages", "manifest.json");
            m_StateDir = Path.Combine(m_Root, "Library", "U3DAILinker");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(m_Root)) Directory.Delete(m_Root, true);
        }

        private JObject Deps() =>
            (JObject)JObject.Parse(File.ReadAllText(m_ManifestPath))["dependencies"];

        private OperationRecord CommitAdd()
        {
            File.WriteAllText(m_ManifestPath, "{ \"dependencies\": { \"com.unity.x\": \"1.0.0\" } }");
            var tx = new ManifestTransaction(m_ManifestPath, m_StateDir);
            var plan = new ManifestPlan("opRB", "stable", "editor-debug-v1.2.0");
            plan.Edits.Add(new ManifestEdit("com.yoji.editor-debug", ManifestChangeType.Add, RepoUrl));
            var r = tx.Apply(plan, acceptConflicts: false);
            Assert.IsTrue(r.Committed, r.FailureReason);
            return r.Record;
        }

        [Test]
        public void Rollback_WhenUnchanged_RestoresManifestToBackup()
        {
            var rec = CommitAdd();

            var result = ManifestRollback.Rollback(m_ManifestPath, m_StateDir, rec);

            Assert.IsTrue(result.Committed, result.FailureReason);
            // 回到改前：托管包不在，第三方仍在
            Assert.IsNull(Deps()["com.yoji.editor-debug"]);
            Assert.IsNotNull(Deps()["com.unity.x"]);
        }

        [Test]
        public void Rollback_WhenManagedDepManuallyChanged_RefusesAndKeepsUserEdit()
        {
            var rec = CommitAdd();
            // 用户/UPM 把该包值改成别的（与事务写入的 newValue 不同）
            File.WriteAllText(m_ManifestPath,
                "{ \"dependencies\": { \"com.unity.x\": \"1.0.0\", \"com.yoji.editor-debug\": \"file:somewhere\" } }");

            var result = ManifestRollback.Rollback(m_ManifestPath, m_StateDir, rec);

            Assert.IsFalse(result.Committed);
            Assert.IsNotNull(result.FailureReason);
            // 手改值保留，未被 backup 覆盖
            Assert.AreEqual("file:somewhere", (string)Deps()["com.yoji.editor-debug"]);
        }

        [Test]
        public void Rollback_WhenBackupMissing_RefusesWithReason()
        {
            var rec = CommitAdd();
            File.Delete(rec.BackupPath);

            var result = ManifestRollback.Rollback(m_ManifestPath, m_StateDir, rec);

            Assert.IsFalse(result.Committed);
            Assert.IsNotNull(result.FailureReason);
        }
    }
}
