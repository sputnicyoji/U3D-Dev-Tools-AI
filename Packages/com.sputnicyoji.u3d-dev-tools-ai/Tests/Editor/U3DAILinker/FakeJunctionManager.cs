using System.Collections.Generic;
using NUnit.Framework;
using Yoji.U3DAILinker.Operations;

namespace Yoji.U3DAILinker.Tests
{
    /// IJunctionManager 的内存假实现：记录 link->target 映射，不触碰真实文件系统。
    /// 供 AgentSyncService 的 EditMode 单测使用；真实 reparse-point 由 WindowsJunctionManager 手动验证。
    internal sealed class FakeJunctionManager : IJunctionManager
    {
        private readonly Dictionary<string, string> m_Links = new Dictionary<string, string>();

        public int CreateCalls { get; private set; }
        public int DeleteCalls { get; private set; }

        public bool IsJunction(string linkPath) => m_Links.ContainsKey(linkPath);

        public string GetTarget(string linkPath)
            => m_Links.TryGetValue(linkPath, out var t) ? t : null;

        public void Create(string linkPath, string targetDir)
        {
            CreateCalls++;
            m_Links[linkPath] = targetDir; // 覆盖 = 修复指向
        }

        public void Delete(string linkPath)
        {
            DeleteCalls++;
            m_Links.Remove(linkPath);
        }
    }

    public class FakeJunctionManagerTests
    {
        [Test] public void Create_ThenIsJunction_AndGetTarget()
        {
            var j = new FakeJunctionManager();
            j.Create("C:/link", "C:/target");
            Assert.IsTrue(j.IsJunction("C:/link"));
            Assert.AreEqual("C:/target", j.GetTarget("C:/link"));
            Assert.AreEqual(1, j.CreateCalls);
        }

        [Test] public void Unknown_IsNotJunction_TargetNull()
        {
            var j = new FakeJunctionManager();
            Assert.IsFalse(j.IsJunction("C:/nope"));
            Assert.IsNull(j.GetTarget("C:/nope"));
        }

        [Test] public void Create_Again_RepointsTarget()
        {
            var j = new FakeJunctionManager();
            j.Create("C:/link", "C:/old");
            j.Create("C:/link", "C:/new");
            Assert.AreEqual("C:/new", j.GetTarget("C:/link"));
        }

        [Test] public void Delete_RemovesLink()
        {
            var j = new FakeJunctionManager();
            j.Create("C:/link", "C:/target");
            j.Delete("C:/link");
            Assert.IsFalse(j.IsJunction("C:/link"));
            Assert.AreEqual(1, j.DeleteCalls);
        }
    }
}
