using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Yoji.U3DAILinker.Agents;

namespace Yoji.U3DAILinker.Tests.Agents
{
    public class GitignoreBlockWriterTests
    {
        private string m_Dir;
        private string m_File;

        [SetUp]
        public void SetUp()
        {
            m_Dir = Path.Combine(Path.GetTempPath(), "u3dgi_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_Dir);
            m_File = Path.Combine(m_Dir, ".gitignore");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(m_Dir)) Directory.Delete(m_Dir, true);
        }

        private static string Norm(string s) => s.Replace("\r\n", "\n");

        private static List<string> Paths(params string[] p) => new List<string>(p);

        [Test]
        public void Sync_MissingFile_CreatesBlockWithInfraAndSkillPaths()
        {
            var r = GitignoreBlockWriter.Sync(m_File,
                Paths(".claude/skills/test-runner-mcp", ".agents/skills/test-runner-mcp"));

            Assert.AreEqual(BlockWriteResult.Status.Written, r.Outcome);
            var text = Norm(File.ReadAllText(m_File));
            StringAssert.Contains("# >>> u3d-ai-linker >>>", text);
            StringAssert.Contains("# <<< u3d-ai-linker <<<", text);
            StringAssert.Contains("/.u3d-ai-linker/", text);
            StringAssert.Contains("/.claude/skills/test-runner-mcp", text);
            StringAssert.Contains("/.agents/skills/test-runner-mcp", text);
            // Must NOT ignore the whole agent dirs.
            StringAssert.DoesNotContain("/.claude/\n", text);
        }

        [Test]
        public void Sync_PreservesUserLines()
        {
            File.WriteAllText(m_File, "Library/\nTemp/\n*.csproj\n");

            GitignoreBlockWriter.Sync(m_File, Paths(".claude/skills/skill-a"));

            var text = Norm(File.ReadAllText(m_File));
            StringAssert.StartsWith("Library/\nTemp/\n*.csproj\n", text);
            StringAssert.Contains("/.claude/skills/skill-a", text);
        }

        [Test]
        public void Sync_SameContentTwice_Unchanged()
        {
            GitignoreBlockWriter.Sync(m_File, Paths(".claude/skills/skill-a"));
            var before = File.ReadAllText(m_File);

            var r = GitignoreBlockWriter.Sync(m_File, Paths(".claude/skills/skill-a"));

            Assert.AreEqual(BlockWriteResult.Status.Unchanged, r.Outcome);
            Assert.AreEqual(before, File.ReadAllText(m_File));
        }

        [Test]
        public void Sync_PathsChanged_UpdatesBlock()
        {
            GitignoreBlockWriter.Sync(m_File, Paths(".claude/skills/old"));

            var r = GitignoreBlockWriter.Sync(m_File, Paths(".claude/skills/new"));

            Assert.AreEqual(BlockWriteResult.Status.Written, r.Outcome);
            var text = Norm(File.ReadAllText(m_File));
            StringAssert.Contains("/.claude/skills/new", text);
            StringAssert.DoesNotContain("/.claude/skills/old", text);
        }

        [Test]
        public void Sync_CorruptMarker_ConflictAndUntouched()
        {
            var corrupt = "Library/\n# >>> u3d-ai-linker >>>\n/.u3d-ai-linker/\n";
            File.WriteAllText(m_File, corrupt);

            var r = GitignoreBlockWriter.Sync(m_File, Paths(".claude/skills/skill-a"));

            Assert.AreEqual(BlockWriteResult.Status.Conflict, r.Outcome);
            Assert.AreEqual(corrupt, File.ReadAllText(m_File));
        }

        [Test]
        public void Remove_DeletesOnlyMatchingLines_KeepsOthers()
        {
            GitignoreBlockWriter.Sync(m_File,
                Paths(".claude/skills/keep", ".claude/skills/drop"));

            var r = GitignoreBlockWriter.Remove(m_File, Paths(".claude/skills/drop"));

            Assert.AreEqual(BlockWriteResult.Status.Written, r.Outcome);
            var text = Norm(File.ReadAllText(m_File));
            StringAssert.Contains("/.claude/skills/keep", text);
            StringAssert.DoesNotContain("/.claude/skills/drop", text);
            StringAssert.Contains("/.u3d-ai-linker/", text);
        }

        [Test]
        public void Remove_LastManagedSkill_DropsBlockButKeepsUserLines()
        {
            File.WriteAllText(m_File, "Library/\n");
            GitignoreBlockWriter.Sync(m_File, Paths(".claude/skills/only"));

            var r = GitignoreBlockWriter.Remove(m_File, Paths(".claude/skills/only"));

            Assert.AreEqual(BlockWriteResult.Status.Written, r.Outcome);
            var text = Norm(File.ReadAllText(m_File));
            StringAssert.StartsWith("Library/\n", text);
            StringAssert.DoesNotContain("# >>> u3d-ai-linker >>>", text);
            StringAssert.DoesNotContain("/.u3d-ai-linker/", text);
        }

        [Test]
        public void Remove_CorruptMarker_ConflictAndUntouched()
        {
            var corrupt = "# <<< u3d-ai-linker <<<\n/.u3d-ai-linker/\n";
            File.WriteAllText(m_File, corrupt);

            var r = GitignoreBlockWriter.Remove(m_File, Paths(".claude/skills/x"));

            Assert.AreEqual(BlockWriteResult.Status.Conflict, r.Outcome);
            Assert.AreEqual(corrupt, File.ReadAllText(m_File));
        }
    }
}
