using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Yoji.U3DAILinker.Operations;

namespace Yoji.U3DAILinker.Tests
{
    public class AgentSyncServiceTests
    {
        private string m_Root;       // 模拟 project 根
        private string m_SourceDir;  // 模拟 Agent~ 源
        private string m_SkillsRoot; // <project>/.u3d-ai-linker
        private FakeJunctionManager m_Junctions;

        [SetUp] public void SetUp()
        {
            m_Root = Path.Combine(Path.GetTempPath(), "u3dlink_sync_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_Root);
            m_SkillsRoot = Path.Combine(m_Root, ".u3d-ai-linker");
            m_SourceDir = Path.Combine(m_Root, "src", "test-runner");
            Directory.CreateDirectory(m_SourceDir);
            File.WriteAllText(Path.Combine(m_SourceDir, "SKILL.md"), "# skill v1");
            Directory.CreateDirectory(Path.Combine(m_SourceDir, "scripts"));
            File.WriteAllText(Path.Combine(m_SourceDir, "scripts", "run.py"), "print(1)");
            m_Junctions = new FakeJunctionManager();
        }

        [TearDown] public void TearDown()
        {
            try { if (Directory.Exists(m_Root)) Directory.Delete(m_Root, true); } catch { }
        }

        private AgentSyncRequest Request(string op = "op1")
        {
            return new AgentSyncRequest
            {
                ToolId = "test-runner",
                SourceDir = m_SourceDir,
                SourceRevision = "rev-" + op,
                OperationId = op,
                SkillsRoot = m_SkillsRoot,
                RequiredSkillMarkers = new List<string> { "SKILL.md" },
                JunctionLinks = new List<string>
                {
                    Path.Combine(m_Root, ".claude", "skills", "test-runner"),
                    Path.Combine(m_Root, ".agents", "skills", "test-runner"),
                },
            };
        }

        private string ToolDir() => Path.Combine(m_SkillsRoot, "skills", "test-runner");

        [Test] public void Sync_FreshInstall_CreatesToolDirWithContentAndOwnership()
        {
            var result = new AgentSyncService(m_Junctions).Sync(Request());
            Assert.IsTrue(result.Success, result.Message);
            Assert.IsTrue(File.Exists(Path.Combine(ToolDir(), "SKILL.md")));
            Assert.IsTrue(File.Exists(Path.Combine(ToolDir(), "scripts", "run.py")));
            var owner = OwnershipFile.Read(ToolDir());
            Assert.IsNotNull(owner);
            Assert.AreEqual("test-runner", owner.ToolId);
            Assert.AreEqual("rev-op1", owner.SourceRevision);
            Assert.AreEqual(ContentHash.OfDirectory(ToolDir()), owner.ContentHash);
        }

        [Test] public void Sync_CreatesAllJunctions_PointingToToolDir()
        {
            var req = Request();
            var result = new AgentSyncService(m_Junctions).Sync(req);
            Assert.IsTrue(result.Success);
            foreach (var link in req.JunctionLinks)
            {
                Assert.IsTrue(m_Junctions.IsJunction(link), "missing junction: " + link);
                Assert.AreEqual(ToolDir(), m_Junctions.GetTarget(link));
            }
        }

        [Test] public void Sync_ExistingPlainSkillLinkDir_RefusesAndPreservesUserDir()
        {
            var req = Request();
            var link = req.JunctionLinks[0];
            Directory.CreateDirectory(link);
            File.WriteAllText(Path.Combine(link, "SKILL.md"), "USER SKILL");

            var result = new AgentSyncService(m_Junctions).Sync(req);

            Assert.IsFalse(result.Success);
            Assert.AreEqual("junction", result.FailureStage);
            Assert.AreEqual("USER SKILL", File.ReadAllText(Path.Combine(link, "SKILL.md")));
            Assert.IsFalse(m_Junctions.IsJunction(link));
        }

        [Test] public void Sync_SecondPlainSkillLinkDir_RefusesBeforeCreatingAnyJunction()
        {
            var req = Request();
            var firstLink = req.JunctionLinks[0];
            var secondLink = req.JunctionLinks[1];
            Directory.CreateDirectory(secondLink);
            File.WriteAllText(Path.Combine(secondLink, "SKILL.md"), "USER SKILL");

            var result = new AgentSyncService(m_Junctions).Sync(req);

            Assert.IsFalse(result.Success);
            Assert.AreEqual("junction", result.FailureStage);
            Assert.AreEqual("USER SKILL", File.ReadAllText(Path.Combine(secondLink, "SKILL.md")));
            Assert.IsFalse(m_Junctions.IsJunction(firstLink));
            Assert.IsFalse(m_Junctions.IsJunction(secondLink));
            Assert.AreEqual(0, m_Junctions.CreateCalls);
            Assert.IsFalse(Directory.Exists(ToolDir()));
        }

        [Test] public void Sync_JunctionFailureAfterFirstCreate_RemovesCreatedLink()
        {
            var req = Request();
            var firstLink = req.JunctionLinks[0];
            var secondLink = req.JunctionLinks[1];
            var throwing = new ThrowOnCreateLinkJunctionManager(m_Junctions, secondLink);

            var result = new AgentSyncService(throwing).Sync(req);

            Assert.IsFalse(result.Success);
            Assert.AreEqual("junction", result.FailureStage);
            Assert.IsFalse(m_Junctions.IsJunction(firstLink));
            Assert.IsFalse(m_Junctions.IsJunction(secondLink));
            Assert.IsFalse(Directory.Exists(ToolDir()));
            Assert.IsFalse(Directory.Exists(Path.Combine(m_SkillsRoot, ".staging", "test-runner-op1")));
        }

        [Test] public void Sync_LeavesNoStagingOrBackup_OnSuccess()
        {
            new AgentSyncService(m_Junctions).Sync(Request());
            Assert.IsFalse(Directory.Exists(Path.Combine(m_SkillsRoot, ".staging", "test-runner-op1")));
            Assert.IsFalse(Directory.Exists(Path.Combine(m_SkillsRoot, ".backup", "test-runner-op1")));
        }

        [Test] public void Sync_MissingSkillMarker_FailsBeforeTouchingTarget()
        {
            File.Delete(Path.Combine(m_SourceDir, "SKILL.md")); // 源缺 SKILL.md
            var result = new AgentSyncService(m_Junctions).Sync(Request());
            Assert.IsFalse(result.Success);
            Assert.AreEqual("validate", result.FailureStage);
            Assert.IsFalse(Directory.Exists(ToolDir()));          // 目标未被创建
            Assert.IsFalse(Directory.Exists(Path.Combine(m_SkillsRoot, ".staging", "test-runner-op1"))); // staging 已清
            Assert.AreEqual(0, m_Junctions.CreateCalls);
        }

        [Test] public void Sync_Reapply_IsIdempotent_AndRepointsJunction()
        {
            var svc = new AgentSyncService(m_Junctions);
            Assert.IsTrue(svc.Sync(Request("op1")).Success);

            // 第二次：源改了内容 + 新 operationId，应替换旧目标且 junction 仍指向同 toolDir
            File.WriteAllText(Path.Combine(m_SourceDir, "SKILL.md"), "# skill v2");
            var r2 = svc.Sync(Request("op2"));
            Assert.IsTrue(r2.Success, r2.Message);
            StringAssert.Contains("v2", File.ReadAllText(Path.Combine(ToolDir(), "SKILL.md")));
            Assert.AreEqual("rev-op2", OwnershipFile.Read(ToolDir()).SourceRevision);
            foreach (var link in Request("op2").JunctionLinks)
                Assert.AreEqual(ToolDir(), m_Junctions.GetTarget(link));
            // 重同步后不残留 backup
            Assert.IsFalse(Directory.Exists(Path.Combine(m_SkillsRoot, ".backup", "test-runner-op2")));
        }

        [Test] public void Sync_ForeignExistingTarget_RefusesAndPreservesUserDir()
        {
            // 目标已存在但没有 ownership = 用户目录：拒绝覆盖
            Directory.CreateDirectory(ToolDir());
            File.WriteAllText(Path.Combine(ToolDir(), "SKILL.md"), "USER OWNED");
            var result = new AgentSyncService(m_Junctions).Sync(Request());
            Assert.IsFalse(result.Success);
            Assert.AreEqual("ownership", result.FailureStage);
            Assert.AreEqual("USER OWNED", File.ReadAllText(Path.Combine(ToolDir(), "SKILL.md"))); // 原样保留
        }

        [Test] public void Sync_MismatchOwnedTarget_Refuses()
        {
            Directory.CreateDirectory(ToolDir());
            OwnershipFile.Write(ToolDir(), new OwnershipRecord { ToolId = "editor-debug", SourceRevision = "x", ContentHash = "h" });
            File.WriteAllText(Path.Combine(ToolDir(), "SKILL.md"), "other tool");
            var result = new AgentSyncService(m_Junctions).Sync(Request());
            Assert.IsFalse(result.Success);
            Assert.AreEqual("ownership", result.FailureStage);
            Assert.AreEqual("other tool", File.ReadAllText(Path.Combine(ToolDir(), "SKILL.md")));
        }

        [Test] public void Sync_JunctionFailureAfterReplace_RestoresBackup()
        {
            // 先成功装一版（managed），再让第二次的 junction 创建抛错，验证恢复旧目标
            var svc = new AgentSyncService(m_Junctions);
            Assert.IsTrue(svc.Sync(Request("op1")).Success);
            var v1Hash = ContentHash.OfDirectory(ToolDir());

            File.WriteAllText(Path.Combine(m_SourceDir, "SKILL.md"), "# skill v2");
            var throwing = new ThrowingJunctionManager();
            var r2 = new AgentSyncService(throwing).Sync(Request("op2"));

            Assert.IsFalse(r2.Success);
            Assert.AreEqual("junction", r2.FailureStage);
            // 旧版本必须被恢复（backup 回滚）
            Assert.IsTrue(Directory.Exists(ToolDir()));
            Assert.AreEqual(v1Hash, ContentHash.OfDirectory(ToolDir()));
            StringAssert.Contains("v1", File.ReadAllText(Path.Combine(ToolDir(), "SKILL.md")));
            // backup 已清回
            Assert.IsFalse(Directory.Exists(Path.Combine(m_SkillsRoot, ".backup", "test-runner-op2")));
        }

        [Test] public void Sync_MissingSourceDir_Fails()
        {
            var req = Request();
            req.SourceDir = Path.Combine(m_Root, "does-not-exist");
            var result = new AgentSyncService(m_Junctions).Sync(req);
            Assert.IsFalse(result.Success);
            Assert.AreEqual("staging", result.FailureStage);
        }

        /// 每次 Create 都抛，用于模拟 junction 阶段失败触发回滚。
        private sealed class ThrowingJunctionManager : IJunctionManager
        {
            public bool IsJunction(string linkPath) => false;
            public string GetTarget(string linkPath) => null;
            public void Create(string linkPath, string targetDir) => throw new IOException("simulated junction failure");
            public void Delete(string linkPath) { }
        }

        private sealed class ThrowOnCreateLinkJunctionManager : IJunctionManager
        {
            private readonly FakeJunctionManager m_Inner;
            private readonly string m_ThrowLink;

            public ThrowOnCreateLinkJunctionManager(FakeJunctionManager inner, string throwLink)
            {
                m_Inner = inner;
                m_ThrowLink = throwLink;
            }

            public bool IsJunction(string linkPath) => m_Inner.IsJunction(linkPath);
            public string GetTarget(string linkPath) => m_Inner.GetTarget(linkPath);

            public void Create(string linkPath, string targetDir)
            {
                if (linkPath == m_ThrowLink)
                    throw new IOException("simulated junction failure");
                m_Inner.Create(linkPath, targetDir);
            }

            public void Delete(string linkPath) => m_Inner.Delete(linkPath);
        }
    }
}
