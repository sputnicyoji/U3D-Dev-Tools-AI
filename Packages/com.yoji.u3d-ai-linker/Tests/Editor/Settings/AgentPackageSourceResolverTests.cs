using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Yoji.U3DAILinker.Registry;
using Yoji.U3DAILinker.Settings;

namespace Yoji.U3DAILinker.Tests.Settings
{
    public sealed class AgentPackageSourceResolverTests
    {
        private string m_Root;

        [SetUp]
        public void SetUp()
        {
            m_Root = Path.Combine(Path.GetTempPath(), "u3dlinker_resolver_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_Root);
        }

        [TearDown]
        public void TearDown()
        {
            try { if (Directory.Exists(m_Root)) Directory.Delete(m_Root, true); } catch { }
        }

        [Test]
        public void Resolve_ReadyToolWithAgentAssets_FindsSkillAndFragments()
        {
            var packageRoot = Path.Combine(m_Root, "pkg");
            var skillDir = Path.Combine(packageRoot, "Agent~", "skills", "test-runner-mcp");
            Directory.CreateDirectory(skillDir);
            File.WriteAllText(Path.Combine(skillDir, "SKILL.md"), "skill");
            Directory.CreateDirectory(Path.Combine(packageRoot, "Agent~", "fragments"));
            File.WriteAllText(Path.Combine(packageRoot, "Agent~", "fragments", "CLAUDE.md"), "claude body");
            File.WriteAllText(Path.Combine(packageRoot, "Agent~", "fragments", "AGENTS.md"), "agents body");

            var provider = new FakeResolvedPathProvider("com.yoji.test-runner", packageRoot);
            var result = AgentPackageSourceResolver.Resolve(
                Registry("test-runner", "com.yoji.test-runner", agentAssets: "Agent~"),
                new[] { "test-runner" },
                provider);

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("test-runner", result[0].ToolId);
            Assert.AreEqual("test-runner-mcp", result[0].SkillName);
            Assert.AreEqual(skillDir, result[0].SourceDir);
            Assert.AreEqual("claude body", result[0].ClaudeFragment);
            Assert.AreEqual("agents body", result[0].AgentsFragment);
            CollectionAssert.AreEqual(
                new[] { ".claude/skills/test-runner-mcp", ".agents/skills/test-runner-mcp" },
                result[0].ManagedSkillRelativePaths);
        }

        [Test]
        public void Resolve_MissingSkillMarker_FailsWithToolId()
        {
            var packageRoot = Path.Combine(m_Root, "pkg");
            Directory.CreateDirectory(Path.Combine(packageRoot, "Agent~", "skills", "test-runner-mcp"));
            var provider = new FakeResolvedPathProvider("com.yoji.test-runner", packageRoot);

            var ex = Assert.Throws<System.InvalidOperationException>(() =>
                AgentPackageSourceResolver.Resolve(
                    Registry("test-runner", "com.yoji.test-runner", agentAssets: "Agent~"),
                    new[] { "test-runner" },
                    provider));

            StringAssert.Contains("test-runner", ex.Message);
            StringAssert.Contains("SKILL.md", ex.Message);
        }

        [Test]
        public void Resolve_SkipsDisabledTool()
        {
            var provider = new FakeResolvedPathProvider("com.yoji.test-runner", "C:/unused");
            var result = AgentPackageSourceResolver.Resolve(
                Registry("test-runner", "com.yoji.test-runner", agentAssets: "Agent~"),
                new string[0],
                provider);

            Assert.AreEqual(0, result.Count);
        }

        private static LinkerRegistry Registry(string id, string packageName, string agentAssets)
        {
            return new LinkerRegistry
            {
                SchemaVersion = 1,
                Channel = LinkerChannel.Dev,
                Entries =
                {
                    new RegistryEntryView
                    {
                        Id = id,
                        PackageName = packageName,
                        PackagePath = "Packages/" + packageName,
                        Status = ToolStatus.Ready,
                        Kind = ToolKind.Tool,
                        Order = 20,
                        AgentAssets = agentAssets,
                    },
                },
            };
        }

        private sealed class FakeResolvedPathProvider : IResolvedPackagePathProvider
        {
            private readonly string m_PackageName;
            private readonly string m_Path;

            public FakeResolvedPathProvider(string packageName, string path)
            {
                m_PackageName = packageName;
                m_Path = path;
            }

            public string GetResolvedPath(string packageName)
                => packageName == m_PackageName ? m_Path : null;
        }
    }
}
