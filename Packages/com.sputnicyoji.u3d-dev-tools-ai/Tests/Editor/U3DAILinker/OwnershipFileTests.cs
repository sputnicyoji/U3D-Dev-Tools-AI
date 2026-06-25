using System;
using System.IO;
using NUnit.Framework;
using Yoji.U3DAILinker.Operations;

namespace Yoji.U3DAILinker.Tests
{
    public class OwnershipFileTests
    {
        private string m_Dir;

        [SetUp] public void SetUp()
        {
            m_Dir = Path.Combine(Path.GetTempPath(), "u3dlink_own_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_Dir);
        }

        [TearDown] public void TearDown()
        {
            try { if (Directory.Exists(m_Dir)) Directory.Delete(m_Dir, true); } catch { }
        }

        [Test] public void FileName_IsStable()
        {
            Assert.AreEqual(".u3d-ai-owner.json", OwnershipFile.FileName);
        }

        [Test] public void Write_ThenExists_AndReadRoundTrips()
        {
            var rec = new OwnershipRecord
            {
                ToolId = "test-runner",
                SourceRevision = "abc123",
                ContentHash = new string('a', 64),
            };
            OwnershipFile.Write(m_Dir, rec);
            Assert.IsTrue(OwnershipFile.Exists(m_Dir));

            var back = OwnershipFile.Read(m_Dir);
            Assert.IsNotNull(back);
            Assert.AreEqual("test-runner", back.ToolId);
            Assert.AreEqual("abc123", back.SourceRevision);
            Assert.AreEqual(new string('a', 64), back.ContentHash);
        }

        [Test] public void Write_StampsSchemaVersion()
        {
            OwnershipFile.Write(m_Dir, new OwnershipRecord { ToolId = "t", SourceRevision = "r", ContentHash = "h" });
            Assert.AreEqual(OwnershipFile.SchemaVersion, OwnershipFile.Read(m_Dir).SchemaVersion);
        }

        [Test] public void Write_PlacesFileAtExpectedPath()
        {
            OwnershipFile.Write(m_Dir, new OwnershipRecord { ToolId = "t", SourceRevision = "r", ContentHash = "h" });
            Assert.IsTrue(File.Exists(Path.Combine(m_Dir, ".u3d-ai-owner.json")));
        }

        [Test] public void Exists_FalseWhenAbsent()
        {
            Assert.IsFalse(OwnershipFile.Exists(m_Dir));
        }

        [Test] public void Read_MissingFile_ReturnsNull()
        {
            Assert.IsNull(OwnershipFile.Read(m_Dir));
        }

        [Test] public void Read_CorruptJson_ReturnsNull()
        {
            File.WriteAllText(Path.Combine(m_Dir, ".u3d-ai-owner.json"), "{ this is not json ");
            Assert.IsNull(OwnershipFile.Read(m_Dir)); // 坏文件按"无合法 ownership"处理，不抛
        }

        [Test] public void Read_EmptyJson_HasNullFields()
        {
            File.WriteAllText(Path.Combine(m_Dir, ".u3d-ai-owner.json"), "{}");
            var back = OwnershipFile.Read(m_Dir);
            Assert.IsNotNull(back);     // 合法 JSON 但字段空：解析成功，字段为 null
            Assert.IsNull(back.ToolId);
        }
    }
}
