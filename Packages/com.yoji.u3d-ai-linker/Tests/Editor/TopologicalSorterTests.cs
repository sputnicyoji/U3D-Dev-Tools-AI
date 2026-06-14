using System.Linq;
using NUnit.Framework;
using Yoji.U3DAILinker.Registry;

namespace Yoji.U3DAILinker.Registry.Tests
{
    public class TopologicalSorterTests
    {
        private static RegistryEntry E(string id, string kind, int order, bool defaultEnabled, params string[] dependsOn)
        {
            return new RegistryEntry
            {
                Id = id,
                Status = "ready",
                Kind = kind,
                Order = order,
                PackageName = "com.yoji." + id,
                PackagePath = "Packages/com.yoji." + id,
                Revision = id + "-v1.0.0",
                DefaultEnabled = defaultEnabled,
                UserToggle = kind == "tool",
                AgentAssets = null,
                MinUnity = "2022.3",
                DependsOn = dependsOn
            };
        }

        private static string[] SortIds(params RegistryEntry[] entries)
        {
            return TopologicalSorter.Sort(entries).Select(x => x.Id).ToArray();
        }

        [Test] public void Sort_DependencyComesBeforeDependent()
        {
            var core = E("editor-core", "infra", 10, false);
            var tr = E("test-runner", "tool", 20, true, "editor-core");
            var ids = SortIds(tr, core);
            Assert.Less(System.Array.IndexOf(ids, "editor-core"), System.Array.IndexOf(ids, "test-runner"));
        }

        [Test] public void Sort_TiesBrokenByOrder()
        {
            var a = E("a", "tool", 30, true);
            var b = E("b", "tool", 10, true);
            var c = E("c", "tool", 20, true);
            var ids = SortIds(a, b, c);
            CollectionAssert.AreEqual(new[] { "b", "c", "a" }, ids);
        }

        [Test] public void Sort_DeterministicAcrossInputOrder()
        {
            var a = E("a", "tool", 30, true);
            var b = E("b", "tool", 10, true);
            var c = E("c", "tool", 20, true);
            CollectionAssert.AreEqual(SortIds(a, b, c), SortIds(c, b, a));
        }

        [Test] public void Sort_CyclicDependency_Throws()
        {
            var x = E("x", "tool", 10, true, "y");
            var y = E("y", "tool", 20, true, "x");
            var ex = Assert.Throws<TopologicalSortException>(() => TopologicalSorter.Sort(new[] { x, y }));
            StringAssert.Contains("cycle", ex.Message.ToLowerInvariant());
        }

        [Test] public void Sort_UnknownDependency_Throws()
        {
            var tr = E("test-runner", "tool", 20, true, "nonexistent");
            var ex = Assert.Throws<TopologicalSortException>(() => TopologicalSorter.Sort(new[] { tr }));
            StringAssert.Contains("nonexistent", ex.Message);
        }

        [Test] public void Sort_InfraDirectlyEnabled_Throws()
        {
            var core = E("editor-core", "infra", 10, true);
            var ex = Assert.Throws<TopologicalSortException>(() => TopologicalSorter.Sort(new[] { core }));
            StringAssert.Contains("infra", ex.Message.ToLowerInvariant());
        }

        [Test] public void Sort_InfraAsDependency_NotDirectlyEnabled_Passes()
        {
            var core = E("editor-core", "infra", 10, false);
            var tr = E("test-runner", "tool", 20, true, "editor-core");
            Assert.DoesNotThrow(() => TopologicalSorter.Sort(new[] { core, tr }));
        }

        [Test] public void Sort_ChainedDependencies_OrderedCorrectly()
        {
            var core = E("editor-core", "infra", 5, false);
            var dbg = E("editor-debug", "tool", 20, true, "editor-core");
            var tr = E("test-runner", "tool", 10, true, "editor-debug");
            var ids = SortIds(tr, dbg, core);
            Assert.Less(System.Array.IndexOf(ids, "editor-core"), System.Array.IndexOf(ids, "editor-debug"));
            Assert.Less(System.Array.IndexOf(ids, "editor-debug"), System.Array.IndexOf(ids, "test-runner"));
        }
    }
}
