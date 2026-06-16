using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Yoji.U3DAILinker;
using Yoji.U3DAILinker.Operations;
using Yoji.U3DAILinker.Registry;
using Yoji.U3DAILinker.Settings;

namespace Yoji.U3DAILinker.Tests
{
    public sealed class PanelStateModelTests
    {
        private static LinkerRegistry SampleRegistry(LinkerChannel channel)
        {
            return new LinkerRegistry
            {
                SchemaVersion = 2,
                Channel = channel,
                Branch = channel == LinkerChannel.Dev ? "main" : null,
                Entries = new List<RegistryEntryView>
                {
                    new RegistryEntryView
                    {
                        Id = "editor-core", DisplayName = "Editor Core", Kind = ToolKind.Infra,
                        Status = ToolStatus.Ready, Order = 10,
                        PackageName = "com.yoji.editor-core", PackagePath = "Packages/com.yoji.editor-core",
                        Revision = "editor-core-v1.0.0", DefaultEnabled = false, UserToggle = false,
                        MinUnity = "2022.3",
                    },
                    new RegistryEntryView
                    {
                        Id = "test-runner", DisplayName = "Test Runner", Kind = ToolKind.Tool,
                        Status = ToolStatus.Ready, Order = 20,
                        PackageName = "com.yoji.test-runner", PackagePath = "Packages/com.yoji.test-runner",
                        Revision = "test-runner-v1.1.0", DefaultEnabled = true, UserToggle = true,
                        MinUnity = "2022.3", DependsOn = new List<string> { "editor-core" },
                    },
                },
            };
        }

        private static Dictionary<string, InstalledPackageInfo> NoneInstalled()
        {
            return new Dictionary<string, InstalledPackageInfo>();
        }

        private static ToolRow Row(ToolRow[] rows, string id)
        {
            return rows.First(r => r.Id == id);
        }

        [Test]
        public void ToolWithUserToggle_ShowsEnabledColumn_InfraDoesNot()
        {
            var rows = PanelStateModel.Build(
                SampleRegistry(LinkerChannel.Stable), NoneInstalled(),
                new[] { "test-runner" }, new Dictionary<string, AgentState>(),
                LinkerChannel.Stable, null);

            Assert.IsTrue(Row(rows, "test-runner").EnabledVisible);
            Assert.IsTrue(Row(rows, "test-runner").Enabled);
            Assert.IsFalse(Row(rows, "editor-core").EnabledVisible);
            Assert.AreEqual(ToolStatus.Ready, Row(rows, "test-runner").Status);
        }

        [Test]
        public void DesiredOnStable_UsesTag()
        {
            var rows = PanelStateModel.Build(
                SampleRegistry(LinkerChannel.Stable), NoneInstalled(),
                new[] { "test-runner" }, new Dictionary<string, AgentState>(),
                LinkerChannel.Stable, null);

            Assert.AreEqual(
                "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/com.yoji.test-runner#test-runner-v1.1.0",
                Row(rows, "test-runner").Desired);
        }

        [Test]
        public void DesiredOnLocal_UsesFileUrlFromLocalRoot()
        {
            var rows = PanelStateModel.Build(
                SampleRegistry(LinkerChannel.Local), NoneInstalled(),
                new[] { "test-runner" }, new Dictionary<string, AgentState>(),
                LinkerChannel.Local, @"E:\Yoji\U3D-Dev-Tools-AI");

            Assert.AreEqual(
                "file:E:/Yoji/U3D-Dev-Tools-AI/Packages/com.yoji.test-runner",
                Row(rows, "test-runner").Desired);
        }

        [Test]
        public void InstalledManagedMatchingDesired_IsInstalled()
        {
            var installed = new Dictionary<string, InstalledPackageInfo>
            {
                ["com.yoji.test-runner"] = new InstalledPackageInfo(
                    "com.yoji.test-runner",
                    "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/com.yoji.test-runner#test-runner-v1.1.0",
                    true),
            };
            var rows = PanelStateModel.Build(
                SampleRegistry(LinkerChannel.Stable), installed,
                new[] { "test-runner" }, new Dictionary<string, AgentState>(),
                LinkerChannel.Stable, null);

            var r = Row(rows, "test-runner");
            Assert.AreEqual(InstallState.Installed, r.Installed);
            Assert.AreEqual(r.Desired, r.Current);
        }

        [Test]
        public void InstalledManagedDifferentDesired_IsOutdated()
        {
            var installed = new Dictionary<string, InstalledPackageInfo>
            {
                ["com.yoji.test-runner"] = new InstalledPackageInfo(
                    "com.yoji.test-runner",
                    "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/com.yoji.test-runner#test-runner-v1.0.0",
                    true),
            };
            var rows = PanelStateModel.Build(
                SampleRegistry(LinkerChannel.Stable), installed,
                new[] { "test-runner" }, new Dictionary<string, AgentState>(),
                LinkerChannel.Stable, null);

            Assert.AreEqual(InstallState.Outdated, Row(rows, "test-runner").Installed);
        }

        [Test]
        public void InstalledLocalUrl_NormalizesSlashesBeforeComparingDesired()
        {
            var installed = new Dictionary<string, InstalledPackageInfo>
            {
                ["com.yoji.test-runner"] = new InstalledPackageInfo(
                    "com.yoji.test-runner",
                    @"file:E:\Yoji\U3D-Dev-Tools-AI\Packages\com.yoji.test-runner",
                    true),
            };
            var rows = PanelStateModel.Build(
                SampleRegistry(LinkerChannel.Local), installed,
                new[] { "test-runner" }, new Dictionary<string, AgentState>(),
                LinkerChannel.Local, @"E:\Yoji\U3D-Dev-Tools-AI");

            Assert.AreEqual(InstallState.Installed, Row(rows, "test-runner").Installed);
        }

        [Test]
        public void InstalledUnmanagedDependency_IsConflict()
        {
            var installed = new Dictionary<string, InstalledPackageInfo>
            {
                ["com.yoji.test-runner"] = new InstalledPackageInfo(
                    "com.yoji.test-runner", "1.0.0-foreign", false),
            };
            var rows = PanelStateModel.Build(
                SampleRegistry(LinkerChannel.Stable), installed,
                new[] { "test-runner" }, new Dictionary<string, AgentState>(),
                LinkerChannel.Stable, null);

            Assert.AreEqual(InstallState.Conflict, Row(rows, "test-runner").Installed);
        }

        [Test]
        public void NotInstalled_IsMissing()
        {
            var rows = PanelStateModel.Build(
                SampleRegistry(LinkerChannel.Stable), NoneInstalled(),
                new[] { "test-runner" }, new Dictionary<string, AgentState>(),
                LinkerChannel.Stable, null);

            Assert.AreEqual(InstallState.Missing, Row(rows, "test-runner").Installed);
            Assert.IsNull(Row(rows, "test-runner").Current);
        }

        [Test]
        public void InfraRequiredBy_ListsEnabledDependents()
        {
            var rows = PanelStateModel.Build(
                SampleRegistry(LinkerChannel.Stable), NoneInstalled(),
                new[] { "test-runner" }, new Dictionary<string, AgentState>(),
                LinkerChannel.Stable, null);

            CollectionAssert.AreEqual(new[] { "test-runner" }, Row(rows, "editor-core").RequiredBy);
        }

        [Test]
        public void AgentState_DefaultsToNotApplicableWhenAbsent()
        {
            var rows = PanelStateModel.Build(
                SampleRegistry(LinkerChannel.Stable), NoneInstalled(),
                new[] { "test-runner" }, new Dictionary<string, AgentState>(),
                LinkerChannel.Stable, null);

            Assert.AreEqual(AgentState.NotApplicable, Row(rows, "test-runner").Agent);
        }

        [Test]
        public void AgentState_ReadFromMapWhenPresent()
        {
            var agents = new Dictionary<string, AgentState> { ["test-runner"] = AgentState.Stale };
            var rows = PanelStateModel.Build(
                SampleRegistry(LinkerChannel.Stable), NoneInstalled(),
                new[] { "test-runner" }, agents,
                LinkerChannel.Stable, null);

            Assert.AreEqual(AgentState.Stale, Row(rows, "test-runner").Agent);
        }

        [Test]
        public void Rows_SortedByOrderThenId()
        {
            var rows = PanelStateModel.Build(
                SampleRegistry(LinkerChannel.Stable), NoneInstalled(),
                new[] { "test-runner" }, new Dictionary<string, AgentState>(),
                LinkerChannel.Stable, null);

            Assert.AreEqual("editor-core", rows[0].Id);
            Assert.AreEqual("test-runner", rows[1].Id);
        }
    }
}
