using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Yoji.U3DAILinker.Operations;

namespace Yoji.U3DAILinker.Tests.Operations
{
    public class InstallQueueBuilderTests
    {
        private static ResolvedTool Tool(string id, string kind, string status, string[] deps = null, bool isLinker = false)
        {
            return new ResolvedTool
            {
                ToolId = id,
                PackageName = "com.yoji." + id,
                Kind = kind,
                Status = status,
                DependsOn = deps ?? System.Array.Empty<string>(),
                IsLinker = isLinker,
                PackageUrl = "url:" + id,
            };
        }

        private static List<ResolvedTool> Registry()
        {
            return new List<ResolvedTool>
            {
                Tool("u3d-ai-linker", "infra", "ready", isLinker: true),
                Tool("editor-core", "infra", "ready"),
                Tool("editor-debug", "tool", "ready", new[] { "editor-core" }),
                Tool("test-runner", "tool", "ready", new[] { "editor-core" }),
                Tool("lua-device-debug", "tool", "planned"),
            };
        }

        private static InstallQueueBuilder New() => new InstallQueueBuilder();
        private static string[] Ids(IReadOnlyList<QueueItem> q) => q.Select(i => i.ToolId).ToArray();

        [Test] public void Build_EnabledTool_PullsInfraDependency()
        {
            var q = New().Build(Registry(), new[] { "editor-debug" });
            CollectionAssert.Contains(Ids(q), "editor-core");
            CollectionAssert.Contains(Ids(q), "editor-debug");
        }

        [Test] public void Build_InfraBeforeDependentTool()
        {
            var ids = Ids(New().Build(Registry(), new[] { "editor-debug" }));
            Assert.Less(System.Array.IndexOf(ids, "editor-core"), System.Array.IndexOf(ids, "editor-debug"));
        }

        [Test] public void Build_SharedInfra_AppearsOnce()
        {
            var ids = Ids(New().Build(Registry(), new[] { "editor-debug", "test-runner" }));
            Assert.AreEqual(1, ids.Count(x => x == "editor-core"));
        }

        [Test] public void Build_SkipsPlannedStatus()
        {
            var ids = Ids(New().Build(Registry(), new[] { "lua-device-debug" }));
            CollectionAssert.DoesNotContain(ids, "lua-device-debug");
        }

        [Test] public void Build_LinkerNotEnabled_NotIncluded()
        {
            var ids = Ids(New().Build(Registry(), new[] { "editor-debug" }));
            CollectionAssert.DoesNotContain(ids, "u3d-ai-linker");
        }

        [Test] public void Build_LinkerEnabled_AlwaysLast()
        {
            var ids = Ids(New().Build(Registry(), new[] { "u3d-ai-linker", "editor-debug" }));
            Assert.AreEqual("u3d-ai-linker", ids[ids.Length - 1]);
            CollectionAssert.Contains(ids, "editor-debug");
        }

        [Test] public void Build_LinkerLast_EvenIfInfraDepends()
        {
            // linker 是 infra，但即便被当作依赖闭包拉入也必须排最后一项
            var reg = Registry();
            var q = New().Build(reg, new[] { "u3d-ai-linker", "test-runner" });
            var ids = Ids(q);
            Assert.AreEqual("u3d-ai-linker", ids[ids.Length - 1]);
        }

        [Test] public void Build_NothingEnabled_EmptyQueue()
        {
            Assert.AreEqual(0, New().Build(Registry(), System.Array.Empty<string>()).Count);
        }

        [Test] public void Build_QueueItemCarriesUrlAndPackageName()
        {
            var q = New().Build(Registry(), new[] { "editor-debug" });
            var item = q.First(i => i.ToolId == "editor-debug");
            Assert.AreEqual("com.yoji.editor-debug", item.PackageName);
            Assert.AreEqual("url:editor-debug", item.PackageUrl);
            Assert.IsFalse(item.IsLinker);
        }

        [Test] public void Build_UnknownEnabledId_Ignored()
        {
            var ids = Ids(New().Build(Registry(), new[] { "does-not-exist", "editor-debug" }));
            CollectionAssert.Contains(ids, "editor-debug");
            CollectionAssert.DoesNotContain(ids, "does-not-exist");
        }
    }
}
