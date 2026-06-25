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

            var provider = new FakeResolvedPathProvider("com.sputnicyoji.u3d-dev-tools-ai", packageRoot);
            var result = AgentPackageSourceResolver.Resolve(
                Registry("test-runner", "com.sputnicyoji.u3d-dev-tools-ai", agentAssets: "Agent~"),
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
        public void Resolve_SinglePackageWithMultipleSkills_ReturnsOneTargetPerSkill()
        {
            var packageRoot = Path.Combine(m_Root, "pkg");
            CreateSkillWithFragments(packageRoot, "test-runner-mcp", "tr claude", "tr agents");
            CreateSkillWithFragments(packageRoot, "unity-editor-debug-mcp", "ed claude", "ed agents");
            CreateSkillWithFragments(packageRoot, "unity-lua-device-debug", "lua claude", "lua agents");

            var provider = new FakeResolvedPathProvider("com.sputnicyoji.u3d-dev-tools-ai", packageRoot);
            var result = AgentPackageSourceResolver.Resolve(
                Registry("u3d-dev-tools-ai", "com.sputnicyoji.u3d-dev-tools-ai", agentAssets: "Agent~"),
                new[] { "u3d-dev-tools-ai" },
                provider);

            Assert.AreEqual(3, result.Count);
            Assert.AreEqual("test-runner-mcp", result[0].ToolId);
            Assert.AreEqual("test-runner-mcp", result[0].SkillName);
            Assert.AreEqual("tr claude", result[0].ClaudeFragment);
            Assert.AreEqual("tr agents", result[0].AgentsFragment);
            Assert.AreEqual("unity-editor-debug-mcp", result[1].ToolId);
            Assert.AreEqual("unity-editor-debug-mcp", result[1].SkillName);
            Assert.AreEqual("ed claude", result[1].ClaudeFragment);
            Assert.AreEqual("ed agents", result[1].AgentsFragment);
            Assert.AreEqual("unity-lua-device-debug", result[2].ToolId);
            Assert.AreEqual("unity-lua-device-debug", result[2].SkillName);
            Assert.AreEqual("lua claude", result[2].ClaudeFragment);
            Assert.AreEqual("lua agents", result[2].AgentsFragment);
        }

        [Test]
        public void Resolve_MissingSkillMarker_FailsWithToolId()
        {
            var packageRoot = Path.Combine(m_Root, "pkg");
            Directory.CreateDirectory(Path.Combine(packageRoot, "Agent~", "skills", "test-runner-mcp"));
            var provider = new FakeResolvedPathProvider("com.sputnicyoji.u3d-dev-tools-ai", packageRoot);

            var ex = Assert.Throws<System.InvalidOperationException>(() =>
                AgentPackageSourceResolver.Resolve(
                    Registry("test-runner", "com.sputnicyoji.u3d-dev-tools-ai", agentAssets: "Agent~"),
                    new[] { "test-runner" },
                    provider));

            StringAssert.Contains("test-runner", ex.Message);
            StringAssert.Contains("SKILL.md", ex.Message);
        }

        [Test]
        public void Resolve_SkipsDisabledTool()
        {
            var provider = new FakeResolvedPathProvider("com.sputnicyoji.u3d-dev-tools-ai", "C:/unused");
            var result = AgentPackageSourceResolver.Resolve(
                Registry("test-runner", "com.sputnicyoji.u3d-dev-tools-ai", agentAssets: "Agent~"),
                new string[0],
                provider);

            Assert.AreEqual(0, result.Count);
        }


        private static void CreateSkillWithFragments(string packageRoot, string skillName, string claude, string agents)
        {
            var skillDir = Path.Combine(packageRoot, "Agent~", "skills", skillName);
            Directory.CreateDirectory(skillDir);
            File.WriteAllText(Path.Combine(skillDir, "SKILL.md"), "skill");
            var fragmentDir = Path.Combine(packageRoot, "Agent~", "fragments", skillName);
            Directory.CreateDirectory(fragmentDir);
            File.WriteAllText(Path.Combine(fragmentDir, "CLAUDE.md"), claude);
            File.WriteAllText(Path.Combine(fragmentDir, "AGENTS.md"), agents);
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
