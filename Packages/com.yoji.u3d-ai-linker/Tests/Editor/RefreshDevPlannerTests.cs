using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Yoji.U3DAILinker;
using Yoji.U3DAILinker.Operations;
using Yoji.U3DAILinker.Registry;

namespace Yoji.U3DAILinker.Tests
{
    public sealed class RefreshDevPlannerTests
    {
        private const string Sha = "0123456789abcdef0123456789abcdef01234567";

        private static LinkerRegistry DevRegistry()
        {
            return new LinkerRegistry
            {
                SchemaVersion = 2,
                Channel = LinkerChannel.Dev,
                Branch = "main",
                Entries = new List<RegistryEntryView>
                {
                    new RegistryEntryView
                    {
                        Id = "editor-core", Kind = ToolKind.Infra, Status = ToolStatus.Ready,
                        Order = 10, PackageName = "com.yoji.editor-core",
                        PackagePath = "Packages/com.yoji.editor-core", MinUnity = "2022.3",
                    },
                    new RegistryEntryView
                    {
                        Id = "test-runner", Kind = ToolKind.Tool, Status = ToolStatus.Ready,
                        Order = 20, PackageName = "com.yoji.test-runner",
                        PackagePath = "Packages/com.yoji.test-runner", MinUnity = "2022.3",
                        DependsOn = new List<string> { "editor-core" },
                    },
                    new RegistryEntryView
                    {
                        Id = "planned-tool", Kind = ToolKind.Tool, Status = ToolStatus.Planned,
                        Order = 30, PackageName = "com.yoji.planned",
                        PackagePath = "Packages/com.yoji.planned", MinUnity = "2022.3",
                    },
                },
            };
        }

        [Test]
        public void BuildPlan_LocksAllToSameSha()
        {
            var planner = new RefreshDevPlanner(new FakeGitRefResolver(Sha));
            var plan = planner.BuildPlan(DevRegistry(), new[] { "test-runner" });

            Assert.AreEqual(Sha, plan.CommitSha);
            Assert.IsTrue(plan.Changes.All(c => c.NewValue.EndsWith("#" + Sha, StringComparison.Ordinal)));
        }

        [Test]
        public void BuildPlan_IncludesInfraClosure()
        {
            var planner = new RefreshDevPlanner(new FakeGitRefResolver(Sha));
            var plan = planner.BuildPlan(DevRegistry(), new[] { "test-runner" });

            var names = plan.Changes.Select(c => c.PackageName).ToArray();
            CollectionAssert.Contains(names, "com.yoji.editor-core");
            CollectionAssert.Contains(names, "com.yoji.test-runner");
        }

        [Test]
        public void BuildPlan_OrdersByRegistryOrder()
        {
            var planner = new RefreshDevPlanner(new FakeGitRefResolver(Sha));
            var plan = planner.BuildPlan(DevRegistry(), new[] { "test-runner" });

            Assert.AreEqual("com.yoji.editor-core", plan.Changes[0].PackageName);
            Assert.AreEqual("com.yoji.test-runner", plan.Changes[1].PackageName);
        }

        [Test]
        public void BuildPlan_SkipsNonReadyEntries()
        {
            var planner = new RefreshDevPlanner(new FakeGitRefResolver(Sha));
            var plan = planner.BuildPlan(DevRegistry(), new[] { "test-runner" });

            CollectionAssert.DoesNotContain(
                plan.Changes.Select(c => c.PackageName).ToArray(), "com.yoji.planned");
        }

        [Test]
        public void BuildPlan_PassesBranchToResolver()
        {
            var fake = new FakeGitRefResolver(Sha);
            new RefreshDevPlanner(fake).BuildPlan(DevRegistry(), new[] { "test-runner" });
            Assert.AreEqual("main", fake.LastBranch);
        }

        [Test]
        public void BuildPlan_RejectsNonDevRegistry()
        {
            var reg = DevRegistry();
            reg.Channel = LinkerChannel.Stable;
            var planner = new RefreshDevPlanner(new FakeGitRefResolver(Sha));
            Assert.Throws<InvalidOperationException>(() => planner.BuildPlan(reg, new[] { "test-runner" }));
        }

        [Test]
        public void BuildPlan_RejectsNonMainBranch()
        {
            var reg = DevRegistry();
            reg.Branch = "develop";
            var planner = new RefreshDevPlanner(new FakeGitRefResolver(Sha));
            Assert.Throws<InvalidOperationException>(() => planner.BuildPlan(reg, new[] { "test-runner" }));
        }

        [Test]
        public void BuildPlan_RejectsNon40HexSha()
        {
            var planner = new RefreshDevPlanner(new FakeGitRefResolver("not-a-sha"));
            Assert.Throws<InvalidOperationException>(() => planner.BuildPlan(DevRegistry(), new[] { "test-runner" }));
        }

        [Test]
        public void BuildPlan_RejectsUnknownEnabledId()
        {
            var planner = new RefreshDevPlanner(new FakeGitRefResolver(Sha));
            Assert.Throws<InvalidOperationException>(
                () => planner.BuildPlan(DevRegistry(), new[] { "ghost-tool" }));
        }
    }
}
