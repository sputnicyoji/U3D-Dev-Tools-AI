using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Yoji.U3DAILinker;
using Yoji.U3DAILinker.Operations;
using Yoji.U3DAILinker.Registry;
using Yoji.U3DAILinker.Settings;

namespace Yoji.U3DAILinker.Tests
{
    public sealed class RestorePlannerTests
    {
        private const string Sha = "0123456789abcdef0123456789abcdef01234567";

        private static LinkerRegistry StableRegistry()
        {
            return new LinkerRegistry
            {
                SchemaVersion = 2,
                Channel = LinkerChannel.Stable,
                Entries = new List<RegistryEntryView>
                {
                    new RegistryEntryView
                    {
                        Id = "test-runner", Kind = ToolKind.Tool, Status = ToolStatus.Ready,
                        Order = 20, PackageName = "com.yoji.test-runner",
                        PackagePath = "Packages/com.yoji.test-runner",
                        Revision = "test-runner-v1.1.0", MinUnity = "2022.3",
                    },
                },
            };
        }

        private static Dictionary<string, InstalledPackageInfo> WithLocalFile()
        {
            return new Dictionary<string, InstalledPackageInfo>
            {
                ["com.yoji.test-runner"] = new InstalledPackageInfo(
                    "com.yoji.test-runner",
                    "file:E:/Yoji/U3D-Dev-Tools-AI/Packages/com.yoji.test-runner",
                    true),
            };
        }

        [Test]
        public void HasLocalFileDependencies_TrueWhenManagedFileUrlPresent()
        {
            Assert.IsTrue(RestorePlanner.HasLocalFileDependencies(WithLocalFile()));
        }

        [Test]
        public void HasLocalFileDependencies_FalseForGitUrlOnly()
        {
            var installed = new Dictionary<string, InstalledPackageInfo>
            {
                ["com.yoji.test-runner"] = new InstalledPackageInfo(
                    "com.yoji.test-runner",
                    "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/com.yoji.test-runner#test-runner-v1.1.0",
                    true),
            };
            Assert.IsFalse(RestorePlanner.HasLocalFileDependencies(installed));
        }

        [Test]
        public void HasLocalFileDependencies_IgnoresUnmanagedFileUrl()
        {
            // 用户手写的 file: 不是 Linker 管理的 -> 不算 Linker 误提交风险目标。
            var installed = new Dictionary<string, InstalledPackageInfo>
            {
                ["com.thirdparty.x"] = new InstalledPackageInfo(
                    "com.thirdparty.x", "file:C:/somewhere/x", false),
            };
            Assert.IsFalse(RestorePlanner.HasLocalFileDependencies(installed));
        }

        [Test]
        public void BuildRestore_ToStable_UsesTagUrl()
        {
            var changes = RestorePlanner.BuildRestore(WithLocalFile(), StableRegistry(), null);
            Assert.AreEqual(1, changes.Length);
            Assert.AreEqual("com.yoji.test-runner", changes[0].PackageName);
            Assert.AreEqual("file:E:/Yoji/U3D-Dev-Tools-AI/Packages/com.yoji.test-runner", changes[0].OldValue);
            Assert.AreEqual(
                "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/com.yoji.test-runner#test-runner-v1.1.0",
                changes[0].NewValue);
        }

        [Test]
        public void BuildRestore_ToDev_UsesShaUrlWhenShaProvided()
        {
            var devReg = StableRegistry();
            devReg.Channel = LinkerChannel.Dev;
            devReg.Branch = "main";
            var changes = RestorePlanner.BuildRestore(WithLocalFile(), devReg, Sha);
            Assert.AreEqual(
                "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/com.yoji.test-runner#" + Sha,
                changes[0].NewValue);
        }

        [Test]
        public void BuildRestore_OnlyRewritesManagedFileDependencies()
        {
            var installed = WithLocalFile();
            installed["com.thirdparty.x"] = new InstalledPackageInfo(
                "com.thirdparty.x", "file:C:/somewhere/x", false);
            var changes = RestorePlanner.BuildRestore(installed, StableRegistry(), null);
            CollectionAssert.AreEquivalent(
                new[] { "com.yoji.test-runner" }, changes.Select(c => c.PackageName).ToArray());
        }

        [Test]
        public void BuildRestore_SkipsPackagesNotInTargetRegistry()
        {
            var installed = new Dictionary<string, InstalledPackageInfo>
            {
                ["com.yoji.ghost"] = new InstalledPackageInfo(
                    "com.yoji.ghost", "file:E:/Yoji/U3D-Dev-Tools-AI/Packages/com.yoji.ghost", true),
            };
            var changes = RestorePlanner.BuildRestore(installed, StableRegistry(), null);
            Assert.AreEqual(0, changes.Length);
        }
    }
}
