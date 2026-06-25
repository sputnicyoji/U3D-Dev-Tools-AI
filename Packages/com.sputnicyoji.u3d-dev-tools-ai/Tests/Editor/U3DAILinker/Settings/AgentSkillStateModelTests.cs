using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Yoji.U3DAILinker.Operations;
using Yoji.U3DAILinker.Settings;

namespace Yoji.U3DAILinker.Tests.Settings
{
    public sealed class AgentSkillStateModelTests
    {
        private string m_Root;
        private string m_ProjectRoot;
        private string m_PackageRoot;
        private FakeJunctionManager m_Junctions;

        [SetUp]
        public void SetUp()
        {
            m_Root = Path.Combine(Path.GetTempPath(), "u3dlink_skill_state_" + Guid.NewGuid().ToString("N"));
            m_ProjectRoot = Path.Combine(m_Root, "project");
            m_PackageRoot = Path.Combine(m_Root, "package");
            Directory.CreateDirectory(m_ProjectRoot);
            Directory.CreateDirectory(m_PackageRoot);
            m_Junctions = new FakeJunctionManager();
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(m_Root))
                    Directory.Delete(m_Root, true);
            }
            catch
            {
            }
        }

        [Test]
        public void Build_SourceMissing_ReturnsSourceMissing()
        {
            var rows = AgentSkillStateModel.Build(m_ProjectRoot, m_PackageRoot, m_Junctions);

            AssertKnownSkills(rows);
            Assert.IsTrue(rows.All(r => r.State == AgentSkillSyncState.SourceMissing));
        }

        [Test]
        public void Build_EmptySkillsRoot_ReturnsKnownSourceMissing()
        {
            Directory.CreateDirectory(Path.Combine(m_PackageRoot, "Agent~", "skills"));

            var rows = AgentSkillStateModel.Build(m_ProjectRoot, m_PackageRoot, m_Junctions);

            AssertKnownSkills(rows);
            Assert.IsTrue(rows.All(r => r.State == AgentSkillSyncState.SourceMissing));
        }

        [Test]
        public void Build_KnownSkillMissing_ReturnsSourceMissingAlongsideExistingRows()
        {
            CreateSourceSkill("test-runner-mcp", "source v1");
            CreateSourceSkill("unity-editor-debug-mcp", "source v1");

            var rows = AgentSkillStateModel.Build(m_ProjectRoot, m_PackageRoot, m_Junctions);

            AssertKnownSkills(rows);
            Assert.AreEqual(AgentSkillSyncState.Missing, Row(rows, "test-runner-mcp").State);
            Assert.AreEqual(AgentSkillSyncState.Missing, Row(rows, "unity-editor-debug-mcp").State);
            Assert.AreEqual(AgentSkillSyncState.SourceMissing, Row(rows, "unity-lua-device-debug").State);
        }

        [Test]
        public void Build_KnownSkillDirectoryWithoutSkillFile_ReturnsSourceMissing()
        {
            Directory.CreateDirectory(Path.Combine(m_PackageRoot, "Agent~", "skills", "test-runner-mcp"));

            var rows = AgentSkillStateModel.Build(m_ProjectRoot, m_PackageRoot, m_Junctions);

            AssertKnownSkills(rows);
            Assert.AreEqual(AgentSkillSyncState.SourceMissing, Row(rows, "test-runner-mcp").State);
        }

        [Test]
        public void Build_GeneratedMissing_ReturnsMissing()
        {
            CreateSourceSkill("test-runner-mcp", "source v1");

            var row = Row("test-runner-mcp");

            Assert.AreEqual(AgentSkillSyncState.Missing, row.State);
            Assert.AreEqual(AgentSkillLinkState.Missing, row.ClaudeLinkState);
            Assert.AreEqual(AgentSkillLinkState.Missing, row.AgentsLinkState);
        }

        [Test]
        public void Build_ValidOwnershipButHashDiff_ReturnsStale()
        {
            var source = CreateSourceSkill("test-runner-mcp", "source v2");
            var generated = CreateGeneratedSkill("test-runner-mcp", "generated old");
            WriteOwnership(generated, "test-runner-mcp", "old-hash");
            CreateBothLinks("test-runner-mcp", generated);

            var row = Row("test-runner-mcp");

            Assert.AreEqual(ContentHash.OfDirectory(source), row.SourceHash);
            Assert.AreEqual("old-hash", row.OwnershipHash);
            Assert.AreEqual(AgentSkillSyncState.Stale, row.State);
        }

        [Test]
        public void Build_GeneratedContentChanged_ReturnsStale()
        {
            var source = CreateSourceSkill("test-runner-mcp", "source v2b");
            var generated = CreateGeneratedSkill("test-runner-mcp", "source v2b");
            var hash = ContentHash.OfDirectory(source);
            WriteOwnership(generated, "test-runner-mcp", hash);
            File.WriteAllText(Path.Combine(generated, "extra.txt"), "user edit");
            CreateBothLinks("test-runner-mcp", generated);

            var row = Row("test-runner-mcp");

            Assert.AreEqual(hash, row.SourceHash);
            Assert.AreEqual(hash, row.OwnershipHash);
            Assert.AreEqual(AgentSkillSyncState.Stale, row.State);
        }

        [Test]
        public void Build_ValidOwnershipAndLinks_ReturnsSynced()
        {
            var source = CreateSourceSkill("test-runner-mcp", "source v3");
            var generated = CreateGeneratedSkill("test-runner-mcp", "source v3");
            WriteOwnership(generated, "test-runner-mcp", ContentHash.OfDirectory(source));
            CreateBothLinks("test-runner-mcp", generated);

            var row = Row("test-runner-mcp");

            Assert.AreEqual(AgentSkillSyncState.Synced, row.State);
            Assert.AreEqual(AgentSkillLinkState.Linked, row.ClaudeLinkState);
            Assert.AreEqual(AgentSkillLinkState.Linked, row.AgentsLinkState);
        }

        [Test]
        public void Build_UserOwnedGeneratedDir_ReturnsConflict()
        {
            CreateSourceSkill("test-runner-mcp", "source v4");
            CreateGeneratedSkill("test-runner-mcp", "user data");

            var row = Row("test-runner-mcp");

            Assert.AreEqual(AgentSkillSyncState.Conflict, row.State);
            StringAssert.Contains("ownership", row.Message);
        }

        [Test]
        public void Build_UserDirectoryAtClaudeLink_ReturnsConflict()
        {
            var source = CreateSourceSkill("test-runner-mcp", "source v5");
            var generated = CreateGeneratedSkill("test-runner-mcp", "source v5");
            WriteOwnership(generated, "test-runner-mcp", ContentHash.OfDirectory(source));
            Directory.CreateDirectory(ClaudeLink("test-runner-mcp"));
            m_Junctions.Create(AgentsLink("test-runner-mcp"), generated);

            var row = Row("test-runner-mcp");

            Assert.AreEqual(AgentSkillSyncState.Conflict, row.State);
            Assert.AreEqual(AgentSkillLinkState.Conflict, row.ClaudeLinkState);
            Assert.AreEqual(AgentSkillLinkState.Linked, row.AgentsLinkState);
        }

        [Test]
        public void Build_JunctionWrongTarget_ReturnsConflict()
        {
            var source = CreateSourceSkill("test-runner-mcp", "source v5b");
            var generated = CreateGeneratedSkill("test-runner-mcp", "source v5b");
            WriteOwnership(generated, "test-runner-mcp", ContentHash.OfDirectory(source));
            var wrongTarget = Path.Combine(m_ProjectRoot, ".u3d-ai-linker", "skills", "wrong");
            m_Junctions.Create(ClaudeLink("test-runner-mcp"), wrongTarget);
            m_Junctions.Create(AgentsLink("test-runner-mcp"), generated);

            var row = Row("test-runner-mcp");

            Assert.AreEqual(AgentSkillSyncState.Conflict, row.State);
            Assert.AreEqual(AgentSkillLinkState.Conflict, row.ClaudeLinkState);
            Assert.AreEqual(AgentSkillLinkState.Linked, row.AgentsLinkState);
        }

        [Test]
        public void Build_TrailingSlashAndMixedSlashTarget_ReturnsSynced()
        {
            var source = CreateSourceSkill("test-runner-mcp", "source v5c");
            var generated = CreateGeneratedSkill("test-runner-mcp", "source v5c");
            WriteOwnership(generated, "test-runner-mcp", ContentHash.OfDirectory(source));
            var slashTarget = MixedSlashTarget(generated);
            m_Junctions.Create(ClaudeLink("test-runner-mcp"), slashTarget);
            m_Junctions.Create(AgentsLink("test-runner-mcp"), slashTarget);

            var row = Row("test-runner-mcp");

            Assert.AreEqual(AgentSkillSyncState.Synced, row.State);
            Assert.AreEqual(AgentSkillLinkState.Linked, row.ClaudeLinkState);
            Assert.AreEqual(AgentSkillLinkState.Linked, row.AgentsLinkState);
        }

        [Test]
        public void Build_OwnershipToolIdMismatch_ReturnsConflict()
        {
            var source = CreateSourceSkill("test-runner-mcp", "source v6");
            var generated = CreateGeneratedSkill("test-runner-mcp", "source v6");
            WriteOwnership(generated, "other-skill", ContentHash.OfDirectory(source));
            CreateBothLinks("test-runner-mcp", generated);

            var row = Row("test-runner-mcp");

            Assert.AreEqual(AgentSkillSyncState.Conflict, row.State);
            StringAssert.Contains("toolId", row.Message);
        }

        [Test]
        public void Build_OwnershipHashMissing_ReturnsConflict()
        {
            CreateSourceSkill("test-runner-mcp", "source v7");
            var generated = CreateGeneratedSkill("test-runner-mcp", "source v7");
            WriteOwnership(generated, "test-runner-mcp", null);
            CreateBothLinks("test-runner-mcp", generated);

            var row = Row("test-runner-mcp");

            Assert.AreEqual(AgentSkillSyncState.Conflict, row.State);
            StringAssert.Contains("hash", row.Message);
        }

        private AgentSkillStatusRow Row(string skillName)
        {
            var rows = AgentSkillStateModel.Build(m_ProjectRoot, m_PackageRoot, m_Junctions);
            return Row(rows, skillName);
        }

        private static AgentSkillStatusRow Row(AgentSkillStatusRow[] rows, string skillName)
        {
            var matches = rows.Where(r => r.SkillName == skillName).ToArray();
            Assert.AreEqual(1, matches.Length, "Expected one row for " + skillName);
            return matches[0];
        }

        private static void AssertKnownSkills(AgentSkillStatusRow[] rows)
        {
            Assert.AreEqual(3, rows.Length);
            Assert.That(rows.Select(r => r.SkillName), Is.EquivalentTo(new[]
            {
                "test-runner-mcp",
                "unity-editor-debug-mcp",
                "unity-lua-device-debug",
            }));
        }

        private string CreateSourceSkill(string skillName, string skillBody)
        {
            var dir = Path.Combine(m_PackageRoot, "Agent~", "skills", skillName);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "SKILL.md"), skillBody);
            return dir;
        }

        private string CreateGeneratedSkill(string skillName, string skillBody)
        {
            var dir = GeneratedPath(skillName);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "SKILL.md"), skillBody);
            return dir;
        }

        private void WriteOwnership(string generatedDir, string toolId, string hash)
        {
            OwnershipFile.Write(generatedDir, new OwnershipRecord
            {
                ToolId = toolId,
                SourceRevision = "test",
                ContentHash = hash,
            });
        }

        private void CreateBothLinks(string skillName, string generatedDir)
        {
            m_Junctions.Create(ClaudeLink(skillName), generatedDir);
            m_Junctions.Create(AgentsLink(skillName), generatedDir);
        }

        private static string MixedSlashTarget(string fullPath)
        {
            if (Path.DirectorySeparatorChar != '\\')
                return fullPath + "/";

            var parts = fullPath.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
            Assert.GreaterOrEqual(parts.Length, 3, "mixed slash path requires enough segments");
            var split = Math.Min(Math.Max(2, parts.Length / 2), parts.Length - 1);
            var target = string.Join("\\", parts.Take(split)) + "/" + string.Join("/", parts.Skip(split)) + "/";

            StringAssert.Contains("\\", target);
            StringAssert.Contains("/", target);
            return target;
        }

        private string GeneratedPath(string skillName)
            => Path.Combine(m_ProjectRoot, ".u3d-ai-linker", "skills", skillName);

        private string ClaudeLink(string skillName)
            => Path.Combine(m_ProjectRoot, ".claude", "skills", skillName);

        private string AgentsLink(string skillName)
            => Path.Combine(m_ProjectRoot, ".agents", "skills", skillName);
    }
}
