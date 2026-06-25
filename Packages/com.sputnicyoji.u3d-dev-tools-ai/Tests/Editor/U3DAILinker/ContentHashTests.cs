using System;
using System.IO;
using NUnit.Framework;
using Yoji.U3DAILinker.Operations;

namespace Yoji.U3DAILinker.Tests
{
    public class ContentHashTests
    {
        private string m_Root;

        [SetUp] public void SetUp()
        {
            m_Root = Path.Combine(Path.GetTempPath(), "u3dlink_hash_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_Root);
        }

        [TearDown] public void TearDown()
        {
            try { if (Directory.Exists(m_Root)) Directory.Delete(m_Root, true); } catch { }
        }

        private string Dir(string name)
        {
            var d = Path.Combine(m_Root, name);
            Directory.CreateDirectory(d);
            return d;
        }

        private static void WriteFile(string dir, string relative, string content)
        {
            var full = Path.Combine(dir, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllText(full, content);
        }

        [Test] public void Hash_IsLowercaseHex64()
        {
            var d = Dir("a");
            WriteFile(d, "SKILL.md", "hello");
            var h = ContentHash.OfDirectory(d);
            Assert.AreEqual(64, h.Length);
            StringAssert.IsMatch("^[0-9a-f]{64}$", h);
        }

        [Test] public void SameContent_SameHash()
        {
            var a = Dir("a"); var b = Dir("b");
            WriteFile(a, "SKILL.md", "x"); WriteFile(a, "scripts/run.py", "print(1)");
            WriteFile(b, "SKILL.md", "x"); WriteFile(b, "scripts/run.py", "print(1)");
            Assert.AreEqual(ContentHash.OfDirectory(a), ContentHash.OfDirectory(b));
        }

        [Test] public void DifferentFileBytes_DifferentHash()
        {
            var a = Dir("a"); var b = Dir("b");
            WriteFile(a, "SKILL.md", "x");
            WriteFile(b, "SKILL.md", "y");
            Assert.AreNotEqual(ContentHash.OfDirectory(a), ContentHash.OfDirectory(b));
        }

        [Test] public void DifferentRelativePath_DifferentHash()
        {
            // 内容相同但路径不同必须改变哈希，否则文件改名不可检测
            var a = Dir("a"); var b = Dir("b");
            WriteFile(a, "SKILL.md", "x");
            WriteFile(b, "OTHER.md", "x");
            Assert.AreNotEqual(ContentHash.OfDirectory(a), ContentHash.OfDirectory(b));
        }

        [Test] public void OwnershipFile_IsExcludedFromHash()
        {
            // .u3d-ai-owner.json 自身存了哈希，必须不参与哈希计算，否则自指
            var a = Dir("a"); var b = Dir("b");
            WriteFile(a, "SKILL.md", "x");
            WriteFile(b, "SKILL.md", "x");
            WriteFile(b, ".u3d-ai-owner.json", "{\"ContentHash\":\"whatever\"}");
            Assert.AreEqual(ContentHash.OfDirectory(a), ContentHash.OfDirectory(b));
        }

        [Test] public void MissingDirectory_Throws()
        {
            Assert.Throws<DirectoryNotFoundException>(
                () => ContentHash.OfDirectory(Path.Combine(m_Root, "nope")));
        }
    }
}
