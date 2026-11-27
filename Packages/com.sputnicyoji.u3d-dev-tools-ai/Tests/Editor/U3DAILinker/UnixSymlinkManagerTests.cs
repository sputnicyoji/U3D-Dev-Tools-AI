using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Yoji.U3DAILinker.Operations;

namespace Yoji.U3DAILinker.Tests
{
    /// Real file-system checks for the macOS/Linux symlink manager.
    public class UnixSymlinkManagerTests
    {
        private string m_Root;
        private string m_Target;
        private string m_Link;
        private UnixSymlinkManager m_Links;

        [SetUp] public void SetUp()
        {
            if (Application.platform == RuntimePlatform.WindowsEditor)
                Assert.Ignore("UnixSymlinkManager is only used off Windows.");

            m_Root = Path.Combine(Path.GetTempPath(), "u3dlink_symlink_" + Guid.NewGuid().ToString("N"));
            m_Target = Path.Combine(m_Root, "target");
            m_Link = Path.Combine(m_Root, "link");
            Directory.CreateDirectory(Path.Combine(m_Target, "sub"));
            File.WriteAllText(Path.Combine(m_Target, "SKILL.md"), "skill");
            File.WriteAllText(Path.Combine(m_Target, "sub", "a.txt"), "a");
            m_Links = new UnixSymlinkManager();
        }

        [TearDown] public void TearDown()
        {
            try { if (m_Root != null && Directory.Exists(m_Root)) Directory.Delete(m_Root, true); } catch { }
        }

        [Test] public void Create_MakesLinkThatExposesTargetAndReportsAbsoluteTarget()
        {
            m_Links.Create(m_Link, m_Target);

            Assert.IsTrue(m_Links.IsJunction(m_Link));
            Assert.AreEqual(Path.GetFullPath(m_Target), m_Links.GetTarget(m_Link));
            Assert.AreEqual("skill", File.ReadAllText(Path.Combine(m_Link, "SKILL.md")));
            Assert.AreEqual("a", File.ReadAllText(Path.Combine(m_Link, "sub", "a.txt")));
        }

        [Test] public void IsJunction_FalseForRealDirectoryFileAndMissingPaths()
        {
            Assert.IsFalse(m_Links.IsJunction(m_Target));
            Assert.IsFalse(m_Links.IsJunction(Path.Combine(m_Target, "SKILL.md")));
            Assert.IsFalse(m_Links.IsJunction(Path.Combine(m_Root, "missing")));
            Assert.IsFalse(m_Links.IsJunction(Path.Combine(m_Root, "missing-parent", "missing")));
            Assert.IsNull(m_Links.GetTarget(m_Target));
            Assert.IsNull(m_Links.GetTarget(Path.Combine(m_Root, "missing")));
        }

        [Test] public void GetTarget_RoundTripsPathLongerThanInitialBuffer()
        {
            var longTarget = Path.Combine(m_Root, new string('a', 200), new string('b', 200));
            Directory.CreateDirectory(longTarget);

            m_Links.Create(m_Link, longTarget);

            Assert.AreEqual(Path.GetFullPath(longTarget), m_Links.GetTarget(m_Link));
        }

        [Test] public void Delete_RemovesOnlyLinkAndKeepsTargetContents()
        {
            m_Links.Create(m_Link, m_Target);

            m_Links.Delete(m_Link);

            Assert.IsFalse(m_Links.IsJunction(m_Link));
            Assert.IsFalse(Directory.Exists(m_Link) || File.Exists(m_Link));
            Assert.AreEqual("skill", File.ReadAllText(Path.Combine(m_Target, "SKILL.md")));
            Assert.AreEqual("a", File.ReadAllText(Path.Combine(m_Target, "sub", "a.txt")));
        }

        [Test] public void DanglingLink_IsStillALinkAndCanBeDeleted()
        {
            m_Links.Create(m_Link, m_Target);
            Directory.Delete(m_Target, true);

            Assert.IsTrue(m_Links.IsJunction(m_Link));
            Assert.AreEqual(Path.GetFullPath(m_Target), m_Links.GetTarget(m_Link));

            m_Links.Delete(m_Link);
            Assert.IsFalse(m_Links.IsJunction(m_Link));
        }

        [Test] public void Delete_OnRealDirectory_IsNoOp()
        {
            m_Links.Delete(m_Target);

            Assert.AreEqual("skill", File.ReadAllText(Path.Combine(m_Target, "SKILL.md")));
            Assert.AreEqual("a", File.ReadAllText(Path.Combine(m_Target, "sub", "a.txt")));
        }

        [Test] public void Create_WhenTargetMissing_Throws()
        {
            Assert.Throws<DirectoryNotFoundException>(() => m_Links.Create(m_Link, Path.Combine(m_Root, "missing")));
            Assert.IsFalse(m_Links.IsJunction(m_Link));
        }

        [Test] public void Create_WhenLinkPathOccupied_ThrowsIOException()
        {
            m_Links.Create(m_Link, m_Target);

            var e = Assert.Throws<IOException>(() => m_Links.Create(m_Link, m_Target));
            StringAssert.Contains("errno", e.Message);
        }
    }
}
