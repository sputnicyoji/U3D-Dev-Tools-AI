using System.IO;
using NUnit.Framework;
using Yoji.U3DAILinker.Settings;

namespace Yoji.U3DAILinker.Tests.Settings
{
    public sealed class AgentSyncOrchestratorTests
    {
        private string m_ProjectRoot;
        private string m_SourceDir;
        private FakeJunctionManager m_Junctions;

        [SetUp]
        public void SetUp()
        {
            m_ProjectRoot = Path.Combine(Path.GetTempPath(), "u3dlinker_orchestrator_" + System.Guid.NewGuid().ToString("N"));
            m_SourceDir = Path.Combine(m_ProjectRoot, "source-skill");
            Directory.CreateDirectory(m_SourceDir);
            File.WriteAllText(Path.Combine(m_SourceDir, "SKILL.md"), "skill body");
            m_Junctions = new FakeJunctionManager();
        }

        [TearDown]
        public void TearDown()
        {
            try { if (Directory.Exists(m_ProjectRoot)) Directory.Delete(m_ProjectRoot, true); } catch { }
        }

        [Test]
        public void Sync_CopiesSkillCreatesJunctionsAndWritesManagedFiles()
        {
            var target = Target("test-runner", "test-runner-mcp", "claude fragment", "agents fragment");
            var result = new AgentSyncOrchestrator(m_Junctions).Sync(
                m_ProjectRoot,
                new[] { target },
                operationId: "op1");

            Assert.IsTrue(result.Success, result.Error);
            Assert.IsTrue(File.Exists(Path.Combine(m_ProjectRoot, ".u3d-ai-linker", "skills", "test-runner", "SKILL.md")));
            Assert.IsTrue(m_Junctions.IsJunction(Path.Combine(m_ProjectRoot, ".claude", "skills", "test-runner-mcp")));
            Assert.IsTrue(m_Junctions.IsJunction(Path.Combine(m_ProjectRoot, ".agents", "skills", "test-runner-mcp")));
            StringAssert.Contains("claude fragment", File.ReadAllText(Path.Combine(m_ProjectRoot, "CLAUDE.md")));
            StringAssert.Contains("agents fragment", File.ReadAllText(Path.Combine(m_ProjectRoot, "AGENTS.md")));
            StringAssert.Contains("/.u3d-ai-linker/", File.ReadAllText(Path.Combine(m_ProjectRoot, ".gitignore")));
            StringAssert.Contains("/.claude/skills/test-runner-mcp", File.ReadAllText(Path.Combine(m_ProjectRoot, ".gitignore")));
        }

        [Test]
        public void Sync_WhenManagedFileConflict_FailsWithoutWritingGitignore()
        {
            File.WriteAllText(Path.Combine(m_ProjectRoot, "CLAUDE.md"), "<!-- u3d-ai-linker:start -->\n");

            var result = new AgentSyncOrchestrator(m_Junctions).Sync(
                m_ProjectRoot,
                new[] { Target("test-runner", "test-runner-mcp", "claude fragment", "agents fragment") },
                operationId: "op1");

            Assert.IsFalse(result.Success);
            StringAssert.Contains("CLAUDE.md", result.Error);
            Assert.IsFalse(File.Exists(Path.Combine(m_ProjectRoot, ".gitignore")));
        }

        [Test]
        public void Sync_WhenJunctionPathIsUserDirectory_FailsBeforeWritingManagedFiles()
        {
            Directory.CreateDirectory(Path.Combine(m_ProjectRoot, ".claude", "skills", "test-runner-mcp"));

            var result = new AgentSyncOrchestrator(m_Junctions).Sync(
                m_ProjectRoot,
                new[] { Target("test-runner", "test-runner-mcp", "claude fragment", "agents fragment") },
                operationId: "op1");

            Assert.IsFalse(result.Success);
            StringAssert.Contains("non-junction skill link", result.Error);
            Assert.IsFalse(File.Exists(Path.Combine(m_ProjectRoot, "CLAUDE.md")));
            Assert.IsFalse(File.Exists(Path.Combine(m_ProjectRoot, "AGENTS.md")));
            Assert.IsFalse(File.Exists(Path.Combine(m_ProjectRoot, ".gitignore")));
        }

        private AgentSyncTarget Target(string toolId, string skillName, string claude, string agents)
        {
            return new AgentSyncTarget
            {
                ToolId = toolId,
                SkillName = skillName,
                SourceDir = m_SourceDir,
                SourceRevision = "rev",
                ClaudeFragment = claude,
                AgentsFragment = agents,
                Order = 20,
                ManagedSkillRelativePaths = new[]
                {
                    ".claude/skills/" + skillName,
                    ".agents/skills/" + skillName,
                },
            };
        }
    }
}
