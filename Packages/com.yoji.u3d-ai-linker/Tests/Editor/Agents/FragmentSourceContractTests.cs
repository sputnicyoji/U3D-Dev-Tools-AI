using System.Collections.Generic;
using NUnit.Framework;
using Yoji.U3DAILinker.Agents;

namespace Yoji.U3DAILinker.Tests.Agents
{
    /// <summary>Reusable in-memory IFragmentSource for EditMode tests.</summary>
    internal sealed class FakeFragmentSource : IFragmentSource
    {
        public string ToolId { get; set; } = "tool";
        public string SkillName { get; set; } = "skill";
        public int Order { get; set; }
        public string ClaudeFragment { get; set; }
        public string AgentsFragment { get; set; }
        public IReadOnlyList<string> ManagedSkillRelativePaths { get; set; } = new List<string>();
    }

    public class FragmentSourceContractTests
    {
        [Test]
        public void Fake_ExposesAllContractFields()
        {
            var src = new FakeFragmentSource
            {
                ToolId = "test-runner",
                SkillName = "test-runner-mcp",
                Order = 10,
                ClaudeFragment = "claude body",
                AgentsFragment = "agents body",
                ManagedSkillRelativePaths = new List<string> { ".claude/skills/test-runner-mcp" }
            };

            Assert.AreEqual("test-runner", src.ToolId);
            Assert.AreEqual("test-runner-mcp", src.SkillName);
            Assert.AreEqual(10, src.Order);
            Assert.AreEqual("claude body", src.ClaudeFragment);
            Assert.AreEqual("agents body", src.AgentsFragment);
            Assert.AreEqual(1, src.ManagedSkillRelativePaths.Count);
            Assert.AreEqual(ManagedBlockKind.Claude, ManagedBlockKind.Claude);
        }
    }
}
