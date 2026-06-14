using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Yoji.U3DAILinker.Operations;

namespace Yoji.U3DAILinker.Tests
{
    public sealed class RemovePlannerTests
    {
        private static RegistryEntryInfo Tool(string id, params string[] deps) =>
            new RegistryEntryInfo { Id = id, PackageName = "com.yoji." + id, Kind = "tool", DependsOn = deps };

        private static RegistryEntryInfo Infra(string id, params string[] deps) =>
            new RegistryEntryInfo { Id = id, PackageName = "com.yoji." + id, Kind = "infra", DependsOn = deps };

        private static RegistryEntryInfo Linker() =>
            new RegistryEntryInfo { Id = "linker", PackageName = "com.yoji.u3d-ai-linker", Kind = "linker", DependsOn = new string[0] };

        [Test]
        public void Remove_Tool_KeepsInfraStillUsedByAnotherEnabledTool()
        {
            var entries = new[] { Tool("a", "core"), Tool("b", "core"), Infra("core"), Linker() };
            var enabled = new HashSet<string> { "a", "b" };

            var (removable, blocked) = RemovePlanner.Compute(entries, enabled, "a");

            CollectionAssert.Contains(removable, "com.yoji.a");      // 目标 tool 自身移除
            CollectionAssert.DoesNotContain(removable, "com.yoji.core"); // core 仍被 b 用
            CollectionAssert.Contains(blocked, "com.yoji.core");
            CollectionAssert.DoesNotContain(removable, "com.yoji.b");
        }

        [Test]
        public void Remove_LastTool_AlsoRemovesOrphanedInfra()
        {
            var entries = new[] { Tool("a", "core"), Tool("b", "core"), Infra("core"), Linker() };
            var enabled = new HashSet<string> { "a" };   // b 已不启用

            var (removable, blocked) = RemovePlanner.Compute(entries, enabled, "a");

            CollectionAssert.Contains(removable, "com.yoji.a");
            CollectionAssert.Contains(removable, "com.yoji.core");   // 无剩余 dependents
            CollectionAssert.IsEmpty(blocked);
        }

        [Test]
        public void Remove_NeverRemovesLinker_EvenIfNoDependents()
        {
            var entries = new[] { Tool("a"), Linker() };
            var enabled = new HashSet<string> { "a" };

            var (removable, _) = RemovePlanner.Compute(entries, enabled, "a");

            CollectionAssert.Contains(removable, "com.yoji.a");
            CollectionAssert.DoesNotContain(removable, "com.yoji.u3d-ai-linker");
        }

        [Test]
        public void Remove_TransitiveInfra_OnlyOrphansAreRemovable()
        {
            // a -> mid -> core ; b -> core 。移除 a 后 mid 成孤儿可删，但 core 仍被 b 用不可删。
            var entries = new[]
            {
                Tool("a", "mid"), Tool("b", "core"),
                Infra("mid", "core"), Infra("core"), Linker(),
            };
            var enabled = new HashSet<string> { "a", "b" };

            var (removable, blocked) = RemovePlanner.Compute(entries, enabled, "a");

            CollectionAssert.Contains(removable, "com.yoji.a");
            CollectionAssert.Contains(removable, "com.yoji.mid");
            CollectionAssert.DoesNotContain(removable, "com.yoji.core");
            CollectionAssert.Contains(blocked, "com.yoji.core");
        }

        [Test]
        public void Remove_TargetToolPackage_AlwaysFirstInRemovable()
        {
            var entries = new[] { Tool("a", "core"), Infra("core"), Linker() };
            var enabled = new HashSet<string> { "a" };

            var (removable, _) = RemovePlanner.Compute(entries, enabled, "a");

            Assert.AreEqual("com.yoji.a", removable.First());
        }
    }
}
