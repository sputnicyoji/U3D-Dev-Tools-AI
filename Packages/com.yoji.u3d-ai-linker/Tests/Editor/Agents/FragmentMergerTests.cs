using System.Collections.Generic;
using NUnit.Framework;
using Yoji.U3DAILinker.Agents;

namespace Yoji.U3DAILinker.Tests.Agents
{
    public class FragmentMergerTests
    {
        private static FakeFragmentSource Src(string toolId, string skill, int order, string claude, string agents = null)
            => new FakeFragmentSource
            {
                ToolId = toolId,
                SkillName = skill,
                Order = order,
                ClaudeFragment = claude,
                AgentsFragment = agents
            };

        [Test]
        public void Merge_OrdersByOrderAscending()
        {
            var sources = new List<IFragmentSource>
            {
                Src("b", "skill-b", 20, "BODY_B"),
                Src("a", "skill-a", 10, "BODY_A")
            };

            var r = FragmentMerger.Merge(sources, ManagedBlockKind.Claude);

            Assert.IsTrue(r.Succeeded);
            Assert.Less(r.Body.IndexOf("BODY_A"), r.Body.IndexOf("BODY_B"));
        }

        [Test]
        public void Merge_SameOrder_TieBrokenByToolId()
        {
            var sources = new List<IFragmentSource>
            {
                Src("zeta", "skill-z", 5, "BODY_Z"),
                Src("alpha", "skill-al", 5, "BODY_AL")
            };

            var r = FragmentMerger.Merge(sources, ManagedBlockKind.Claude);

            Assert.IsTrue(r.Succeeded);
            Assert.Less(r.Body.IndexOf("BODY_AL"), r.Body.IndexOf("BODY_Z"));
        }

        [Test]
        public void Merge_NullFragment_ProducesNoSection()
        {
            var sources = new List<IFragmentSource>
            {
                Src("a", "skill-a", 1, null),
                Src("b", "skill-b", 2, "BODY_B")
            };

            var r = FragmentMerger.Merge(sources, ManagedBlockKind.Claude);

            Assert.IsTrue(r.Succeeded);
            StringAssert.DoesNotContain("skill-a", r.Body);
            StringAssert.Contains("BODY_B", r.Body);
        }

        [Test]
        public void Merge_WritesSourceCommentBeforeEachSection()
        {
            var sources = new List<IFragmentSource>
            {
                Src("test-runner", "test-runner-mcp", 1, "BODY")
            };

            var r = FragmentMerger.Merge(sources, ManagedBlockKind.Claude);

            Assert.IsTrue(r.Succeeded);
            StringAssert.Contains("<!-- source: test-runner-mcp (tool: test-runner) -->", r.Body);
            Assert.Less(
                r.Body.IndexOf("<!-- source: test-runner-mcp"),
                r.Body.IndexOf("BODY"));
        }

        [Test]
        public void Merge_DuplicateSkillName_FailsPreflight_NoBody()
        {
            var sources = new List<IFragmentSource>
            {
                Src("a", "dup-skill", 1, "BODY_A"),
                Src("b", "dup-skill", 2, "BODY_B")
            };

            var r = FragmentMerger.Merge(sources, ManagedBlockKind.Claude);

            Assert.IsFalse(r.Succeeded);
            Assert.IsNull(r.Body);
            StringAssert.Contains("dup-skill", r.Error);
        }

        [Test]
        public void Merge_NegativeOrder_FailsPreflight()
        {
            var sources = new List<IFragmentSource>
            {
                Src("a", "skill-a", -1, "BODY_A")
            };

            var r = FragmentMerger.Merge(sources, ManagedBlockKind.Claude);

            Assert.IsFalse(r.Succeeded);
            Assert.IsNull(r.Body);
            StringAssert.Contains("order", r.Error);
        }

        [Test]
        public void Merge_AgentsKind_UsesAgentsFragment()
        {
            var sources = new List<IFragmentSource>
            {
                Src("a", "skill-a", 1, claude: "CLAUDE_ONLY", agents: "AGENTS_ONLY")
            };

            var r = FragmentMerger.Merge(sources, ManagedBlockKind.Agents);

            Assert.IsTrue(r.Succeeded);
            StringAssert.Contains("AGENTS_ONLY", r.Body);
            StringAssert.DoesNotContain("CLAUDE_ONLY", r.Body);
        }

        [Test]
        public void Merge_EmptyInput_SucceedsWithEmptyBody()
        {
            var r = FragmentMerger.Merge(new List<IFragmentSource>(), ManagedBlockKind.Claude);

            Assert.IsTrue(r.Succeeded);
            Assert.AreEqual(string.Empty, r.Body);
        }
    }
}
