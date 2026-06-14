using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Yoji.U3DAILinker.Agents;

namespace Yoji.U3DAILinker.Tests.Agents
{
    public class ManagedFileSyncTests
    {
        private string m_Dir;
        private string m_Claude;
        private string m_Agents;

        [SetUp]
        public void SetUp()
        {
            m_Dir = Path.Combine(Path.GetTempPath(), "u3dsync_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_Dir);
            m_Claude = Path.Combine(m_Dir, "CLAUDE.md");
            m_Agents = Path.Combine(m_Dir, "AGENTS.md");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(m_Dir)) Directory.Delete(m_Dir, true);
        }

        private static FakeFragmentSource Src(string toolId, string skill, int order)
            => new FakeFragmentSource
            {
                ToolId = toolId,
                SkillName = skill,
                Order = order,
                ClaudeFragment = "C_" + toolId,
                AgentsFragment = "A_" + toolId
            };

        [Test]
        public void Sync_TwoTools_WritesBothManagedFiles()
        {
            var sources = new List<IFragmentSource>
            {
                Src("b", "skill-b", 2),
                Src("a", "skill-a", 1)
            };

            var ok = ManagedFileSync.Sync(m_Claude, m_Agents, sources, out var error);

            Assert.IsTrue(ok, error);
            Assert.IsTrue(File.Exists(m_Claude));
            Assert.IsTrue(File.Exists(m_Agents));
            var claude = File.ReadAllText(m_Claude).Replace("\r\n", "\n");
            StringAssert.Contains("C_a", claude);
            StringAssert.Contains("C_b", claude);
            Assert.Less(claude.IndexOf("C_a"), claude.IndexOf("C_b"));
        }

        [Test]
        public void Sync_DuplicateSkillName_WritesNothing()
        {
            var sources = new List<IFragmentSource>
            {
                Src("a", "dup", 1),
                Src("b", "dup", 2)
            };

            var ok = ManagedFileSync.Sync(m_Claude, m_Agents, sources, out var error);

            Assert.IsFalse(ok);
            StringAssert.Contains("dup", error);
            Assert.IsFalse(File.Exists(m_Claude), "CLAUDE.md must not be created on preflight failure");
            Assert.IsFalse(File.Exists(m_Agents), "AGENTS.md must not be created on preflight failure");
        }

        [Test]
        public void Sync_CorruptExistingFile_ReportsConflictAndLeavesItUntouched()
        {
            var corrupt = "<!-- u3d-ai-linker:start -->\nstray\n";
            File.WriteAllText(m_Claude, corrupt);
            var sources = new List<IFragmentSource> { Src("a", "skill-a", 1) };

            var ok = ManagedFileSync.Sync(m_Claude, m_Agents, sources, out var error);

            Assert.IsFalse(ok);
            StringAssert.Contains("CLAUDE.md", error);
            Assert.AreEqual(corrupt, File.ReadAllText(m_Claude));
        }
    }
}
