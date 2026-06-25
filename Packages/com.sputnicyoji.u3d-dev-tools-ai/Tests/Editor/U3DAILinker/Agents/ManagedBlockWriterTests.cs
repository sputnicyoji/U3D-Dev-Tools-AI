using System.IO;
using NUnit.Framework;
using Yoji.U3DAILinker.Agents;

namespace Yoji.U3DAILinker.Tests.Agents
{
    public class ManagedBlockWriterTests
    {
        private string m_Dir;
        private string m_File;

        [SetUp]
        public void SetUp()
        {
            m_Dir = Path.Combine(Path.GetTempPath(), "u3dlink_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_Dir);
            m_File = Path.Combine(m_Dir, "CLAUDE.md");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(m_Dir)) Directory.Delete(m_Dir, true);
        }

        private static string Norm(string s) => s.Replace("\r\n", "\n");

        [Test]
        public void Write_MissingFile_CreatesFileWithMarkers()
        {
            var r = ManagedBlockWriter.Write(m_File, "BODY");

            Assert.AreEqual(BlockWriteResult.Status.Written, r.Outcome);
            Assert.IsTrue(File.Exists(m_File));
            var text = Norm(File.ReadAllText(m_File));
            StringAssert.Contains("<!-- u3d-ai-linker:start -->", text);
            StringAssert.Contains("<!-- u3d-ai-linker:end -->", text);
            StringAssert.Contains("BODY", text);
        }

        [Test]
        public void Write_ExistingUserContent_PreservedVerbatimOutsideBlock()
        {
            File.WriteAllText(m_File, "# My Project\n\nUser notes line.\n");

            var r = ManagedBlockWriter.Write(m_File, "BODY");

            Assert.AreEqual(BlockWriteResult.Status.Written, r.Outcome);
            var text = Norm(File.ReadAllText(m_File));
            StringAssert.StartsWith("# My Project\n\nUser notes line.\n", text);
            StringAssert.Contains("BODY", text);
        }

        [Test]
        public void Write_SameContentTwice_SecondIsUnchanged()
        {
            ManagedBlockWriter.Write(m_File, "BODY");
            var before = File.ReadAllText(m_File);

            var r = ManagedBlockWriter.Write(m_File, "BODY");

            Assert.AreEqual(BlockWriteResult.Status.Unchanged, r.Outcome);
            Assert.AreEqual(before, File.ReadAllText(m_File));
        }

        [Test]
        public void Write_UpdateBody_ReplacesOnlyInsideBlock()
        {
            File.WriteAllText(m_File, "USER TOP\n");
            ManagedBlockWriter.Write(m_File, "OLD");

            var r = ManagedBlockWriter.Write(m_File, "NEW");

            Assert.AreEqual(BlockWriteResult.Status.Written, r.Outcome);
            var text = Norm(File.ReadAllText(m_File));
            StringAssert.StartsWith("USER TOP\n", text);
            StringAssert.Contains("NEW", text);
            StringAssert.DoesNotContain("OLD", text);
        }

        [Test]
        public void Write_OnlyStartMarker_ConflictAndFileUntouched()
        {
            var corrupt = "USER\n<!-- u3d-ai-linker:start -->\nstray\n";
            File.WriteAllText(m_File, corrupt);

            var r = ManagedBlockWriter.Write(m_File, "BODY");

            Assert.AreEqual(BlockWriteResult.Status.Conflict, r.Outcome);
            Assert.AreEqual(corrupt, File.ReadAllText(m_File));
        }

        [Test]
        public void Write_DuplicateStartMarker_Conflict()
        {
            var corrupt =
                "<!-- u3d-ai-linker:start -->\na\n<!-- u3d-ai-linker:end -->\n" +
                "<!-- u3d-ai-linker:start -->\nb\n<!-- u3d-ai-linker:end -->\n";
            File.WriteAllText(m_File, corrupt);

            var r = ManagedBlockWriter.Write(m_File, "BODY");

            Assert.AreEqual(BlockWriteResult.Status.Conflict, r.Outcome);
            Assert.AreEqual(corrupt, File.ReadAllText(m_File));
        }

        [Test]
        public void Write_EndBeforeStart_Conflict()
        {
            var corrupt =
                "<!-- u3d-ai-linker:end -->\nx\n<!-- u3d-ai-linker:start -->\n";
            File.WriteAllText(m_File, corrupt);

            var r = ManagedBlockWriter.Write(m_File, "BODY");

            Assert.AreEqual(BlockWriteResult.Status.Conflict, r.Outcome);
            Assert.AreEqual(corrupt, File.ReadAllText(m_File));
        }
    }
}
