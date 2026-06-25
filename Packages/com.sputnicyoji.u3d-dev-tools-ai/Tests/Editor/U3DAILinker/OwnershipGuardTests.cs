using System;
using System.IO;
using NUnit.Framework;
using Yoji.U3DAILinker.Operations;

namespace Yoji.U3DAILinker.Tests
{
    public class OwnershipGuardTests
    {
        private string m_Dir;

        [SetUp] public void SetUp()
        {
            m_Dir = Path.Combine(Path.GetTempPath(), "u3dlink_guard_" + Guid.NewGuid().ToString("N"));
        }

        [TearDown] public void TearDown()
        {
            try { if (Directory.Exists(m_Dir)) Directory.Delete(m_Dir, true); } catch { }
        }

        private void WriteOwner(string toolId)
        {
            OwnershipFile.Write(m_Dir, new OwnershipRecord
            {
                ToolId = toolId, SourceRevision = "r", ContentHash = "h",
            });
        }

        [Test] public void AbsentDirectory_IsMissing()
        {
            Assert.AreEqual(OwnershipStatus.Missing, OwnershipGuard.Inspect(m_Dir, "test-runner"));
        }

        [Test] public void DirectoryWithoutOwner_IsForeign()
        {
            Directory.CreateDirectory(m_Dir);
            File.WriteAllText(Path.Combine(m_Dir, "SKILL.md"), "user content");
            // 存在但没有 ownership 文件：视为用户目录
            Assert.AreEqual(OwnershipStatus.Foreign, OwnershipGuard.Inspect(m_Dir, "test-runner"));
        }

        [Test] public void CorruptOwner_IsForeign()
        {
            Directory.CreateDirectory(m_Dir);
            File.WriteAllText(Path.Combine(m_Dir, ".u3d-ai-owner.json"), "{ broken ");
            Assert.AreEqual(OwnershipStatus.Foreign, OwnershipGuard.Inspect(m_Dir, "test-runner"));
        }

        [Test] public void OwnerForSameTool_IsManagedMatch()
        {
            WriteOwner("test-runner");
            Assert.AreEqual(OwnershipStatus.ManagedMatch, OwnershipGuard.Inspect(m_Dir, "test-runner"));
        }

        [Test] public void OwnerForOtherTool_IsManagedMismatch()
        {
            WriteOwner("editor-debug");
            // 是 Linker 管理的目录，但属于别的工具：不能当作本工具目标覆盖
            Assert.AreEqual(OwnershipStatus.ManagedMismatch, OwnershipGuard.Inspect(m_Dir, "test-runner"));
        }

        [Test] public void OwnerWithEmptyToolId_IsForeign()
        {
            OwnershipFile.Write(m_Dir, new OwnershipRecord { ToolId = "", SourceRevision = "r", ContentHash = "h" });
            // 合法 JSON 但 toolId 空：不构成合法归属，按用户目录保护
            Assert.AreEqual(OwnershipStatus.Foreign, OwnershipGuard.Inspect(m_Dir, "test-runner"));
        }

        [Test] public void MayOverwrite_TrueOnMissing()
        {
            Assert.IsTrue(OwnershipGuard.MayOverwrite(m_Dir, "test-runner")); // 不存在=可放心创建
        }

        [Test] public void MayOverwrite_TrueOnManagedMatch()
        {
            WriteOwner("test-runner");
            Assert.IsTrue(OwnershipGuard.MayOverwrite(m_Dir, "test-runner"));
        }

        [Test] public void MayOverwrite_FalseOnForeign()
        {
            Directory.CreateDirectory(m_Dir);
            File.WriteAllText(Path.Combine(m_Dir, "x.txt"), "user");
            Assert.IsFalse(OwnershipGuard.MayOverwrite(m_Dir, "test-runner"));
        }

        [Test] public void MayOverwrite_FalseOnManagedMismatch()
        {
            WriteOwner("editor-debug");
            Assert.IsFalse(OwnershipGuard.MayOverwrite(m_Dir, "test-runner"));
        }
    }
}
