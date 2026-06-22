using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Yoji.U3DAILinker.Registry;

namespace Yoji.U3DAILinker.Tests
{
    public class RegistryFixtureTests
    {
        // 从当前测试程序集位置向上查找仓库根（含 Registry/stable.json 的目录）。
        // 找不到则用 U3D_LINKER_REPO_ROOT 环境变量兜底（headless 跑用）。
        private static string FindRepoRoot()
        {
            var envRoot = System.Environment.GetEnvironmentVariable("U3D_LINKER_REPO_ROOT");
            if (!string.IsNullOrEmpty(envRoot) &&
                File.Exists(Path.Combine(envRoot, "Registry", "stable.json")))
            {
                return envRoot.Replace('\\', '/');
            }

            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Registry", "stable.json")))
                {
                    return dir.FullName.Replace('\\', '/');
                }
                dir = dir.Parent;
            }
            return null;
        }

        private static string ReadRegistry(string repoRoot, string relative)
        {
            var full = Path.Combine(repoRoot, relative);
            Assert.IsTrue(File.Exists(full), "registry file missing: " + full);
            return File.ReadAllText(full);
        }

        [Test]
        public void RootRegistry_RepoRootResolves()
        {
            var root = FindRepoRoot();
            Assert.IsNotNull(
                root,
                "could not locate repo root containing Registry/stable.json; " +
                "set U3D_LINKER_REPO_ROOT env var when running headless");
        }

        [Test]
        public void StableRegistry_PassesValidation()
        {
            var root = FindRepoRoot();
            Assume.That(root, Is.Not.Null);
            var doc = RegistryParser.Parse(ReadRegistry(root, "Registry/stable.json"));
            Assert.DoesNotThrow(() => RegistryValidator.Validate(doc, RegistryChannel.Stable));
        }

        [Test]
        public void DevRegistry_PassesValidation()
        {
            var root = FindRepoRoot();
            Assume.That(root, Is.Not.Null);
            var doc = RegistryParser.Parse(ReadRegistry(root, "Registry/dev.json"));
            Assert.DoesNotThrow(() => RegistryValidator.Validate(doc, RegistryChannel.Dev));
        }

        [Test]
        public void BundledSnapshot_StableMatchesRoot()
        {
            var root = FindRepoRoot();
            Assume.That(root, Is.Not.Null);
            var rootJson = ReadRegistry(root, "Registry/stable.json");
            var snapJson = ReadRegistry(
                root, "Packages/com.yoji.u3d-ai-linker/Registry/stable.json");
            Assert.AreEqual(
                Normalize(rootJson), Normalize(snapJson),
                "bundled stable snapshot drifted from root Registry/stable.json");
        }

        [Test]
        public void BundledSnapshot_DevMatchesRoot()
        {
            var root = FindRepoRoot();
            Assume.That(root, Is.Not.Null);
            var rootJson = ReadRegistry(root, "Registry/dev.json");
            var snapJson = ReadRegistry(
                root, "Packages/com.yoji.u3d-ai-linker/Registry/dev.json");
            Assert.AreEqual(
                Normalize(rootJson), Normalize(snapJson),
                "bundled dev snapshot drifted from root Registry/dev.json");
        }

        [Test]
        public void StableRegistry_ReleaseScopeIsReady()
        {
            var root = FindRepoRoot();
            Assume.That(root, Is.Not.Null);
            var doc = RegistryParser.Parse(ReadRegistry(root, "Registry/stable.json"));

            var expectedReady = new HashSet<string>
            {
                "editor-core",
                "editor-debug",
                "test-runner",
                "lua-device-debug",
                "u3d-ai-linker",
            };
            foreach (var entry in doc.Entries)
            {
                if (expectedReady.Contains(entry.Id))
                    Assert.AreEqual("ready", entry.Status, "tool '" + entry.Id + "' must be stable ready");
                else
                    Assert.Fail("unexpected stable entry: " + entry.Id);
            }
        }

        [Test]
        public void StableRegistry_EditorCoreIsInfra()
        {
            var root = FindRepoRoot();
            Assume.That(root, Is.Not.Null);
            var doc = RegistryParser.Parse(ReadRegistry(root, "Registry/stable.json"));
            RegistryEntry core = null;
            foreach (var entry in doc.Entries)
            {
                if (entry.Id == "editor-core") { core = entry; break; }
            }
            Assert.IsNotNull(core, "editor-core entry missing from stable.json");
            Assert.AreEqual("infra", core.Kind);
            Assert.IsFalse(core.UserToggle, "infra editor-core must not be user-toggleable");
        }

        // 归一化换行,避免 CRLF/LF 差异导致快照逐字节比较误判。
        private static string Normalize(string s)
        {
            return s.Replace("\r\n", "\n").Replace("\r", "\n").TrimEnd('\n');
        }
    }
}
