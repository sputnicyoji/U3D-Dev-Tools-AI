using System;
using System.Collections.Generic;
using NUnit.Framework;
using Yoji.EditorCore.Ports;

namespace Yoji.EditorCore.Tests
{
    public sealed class ServicePortAllocatorTests
    {
        [Test]
        public void Allocate_LegacyFree_UsesLegacyPort()
        {
            var definition = ServicePortDefinition.Create(
                "test-runner-mcp", "TestRunnerMCP", 0, new[] { 21890, 21896, 21897 });
            var policy = new ServicePortPolicy
            {
                ProjectRoot = "G:/A",
                ProjectId = "project-a",
                Mode = ServicePortMode.Auto,
                PreferLegacyPorts = true,
            };
            var probe = new FakePortProbe(Array.Empty<int>());

            var result = ServicePortAllocator.Allocate(definition, policy, probe);

            Assert.AreEqual(21890, result.Port);
            Assert.AreEqual("legacy", result.Source);
        }

        [Test]
        public void Allocate_LegacyOccupied_UsesProjectBase()
        {
            var definition = ServicePortDefinition.Create(
                "test-runner-mcp", "TestRunnerMCP", 0, new[] { 21890, 21896, 21897 });
            var policy = new ServicePortPolicy
            {
                ProjectRoot = "G:/A",
                ProjectId = "project-a",
                Mode = ServicePortMode.Auto,
                PreferLegacyPorts = true,
            };
            var probe = new FakePortProbe(new[] { 21890, 21896, 21897 });

            var result = ServicePortAllocator.Allocate(definition, policy, probe);

            Assert.AreNotEqual(21890, result.Port);
            Assert.GreaterOrEqual(result.Port, 21900);
            Assert.LessOrEqual(result.Port, 29999);
            Assert.AreEqual("project-auto", result.Source);
        }

        [Test]
        public void Allocate_FixedProject_UsesOverrideInsteadOfPreferred()
        {
            var definition = ServicePortDefinition.Create(
                "unity-editor-debug-mcp", "EditorDebugMCP", 1, new[] { 21891, 21892, 21893 });
            var policy = new ServicePortPolicy
            {
                ProjectRoot = "G:/A",
                ProjectId = "project-a",
                Mode = ServicePortMode.FixedProject,
                PreferredBasePort = 21900,
                OverrideBasePort = 21910,
            };
            var probe = new FakePortProbe(Array.Empty<int>());

            var result = ServicePortAllocator.Allocate(definition, policy, probe);

            Assert.AreEqual(21911, result.Port);
            Assert.AreEqual("user-override", result.Source);
        }

        [Test]
        public void Allocate_FixedProject_OverrideOccupied_FailsFastWithoutPreferredFallback()
        {
            var definition = ServicePortDefinition.Create(
                "unity-editor-debug-mcp", "EditorDebugMCP", 1, new[] { 21891, 21892, 21893 });
            var policy = new ServicePortPolicy
            {
                ProjectRoot = "G:/A",
                ProjectId = "project-a",
                Mode = ServicePortMode.FixedProject,
                PreferredBasePort = 21900,
                OverrideBasePort = 21910,
            };
            var probe = new FakePortProbe(new[] { 21911 });

            var result = ServicePortAllocator.TryAllocate(definition, policy, probe, out var assignment, out var error);

            Assert.IsFalse(result);
            Assert.IsNull(assignment);
            StringAssert.Contains("21911", error);
        }

        [Test]
        public void TryAllocate_InvalidOverrideBase_ReturnsFalse()
        {
            var definition = ServicePortDefinition.Create(
                "unity-editor-debug-mcp", "EditorDebugMCP", 1, new[] { 21891, 21892, 21893 });
            var policy = new ServicePortPolicy
            {
                ProjectRoot = "G:/A",
                ProjectId = "project-a",
                Mode = ServicePortMode.FixedProject,
                PreferredBasePort = 21900,
                OverrideBasePort = 21903,
            };
            var probe = new FakePortProbe(Array.Empty<int>());

            Assert.DoesNotThrow(() =>
            {
                var ok = ServicePortAllocator.TryAllocate(definition, policy, probe, out var assignment, out var error);
                Assert.IsFalse(ok);
                Assert.IsNull(assignment);
                StringAssert.Contains("invalid base port", error);
            });
        }

        [Test]
        public void TryAllocate_NegativeOverrideBase_ReturnsFalse()
        {
            var definition = ServicePortDefinition.Create(
                "unity-editor-debug-mcp", "EditorDebugMCP", 1, new[] { 21891, 21892, 21893 });
            var policy = new ServicePortPolicy
            {
                ProjectRoot = "G:/A",
                ProjectId = "project-a",
                Mode = ServicePortMode.Auto,
                OverrideBasePort = -21900,
            };
            var probe = new FakePortProbe(Array.Empty<int>());

            Assert.DoesNotThrow(() =>
            {
                var ok = ServicePortAllocator.TryAllocate(definition, policy, probe, out var assignment, out var error);
                Assert.IsFalse(ok);
                Assert.IsNull(assignment);
                StringAssert.Contains("invalid override base port", error);
            });
        }

        [Test]
        public void TryAllocate_InvalidPreferredBase_ReturnsFalse()
        {
            var definition = ServicePortDefinition.Create(
                "unity-editor-debug-mcp", "EditorDebugMCP", 1, new[] { 21891, 21892, 21893 });
            var policy = new ServicePortPolicy
            {
                ProjectRoot = "G:/A",
                ProjectId = "project-a",
                Mode = ServicePortMode.FixedProject,
                PreferredBasePort = 21903,
            };
            var probe = new FakePortProbe(Array.Empty<int>());

            Assert.DoesNotThrow(() =>
            {
                var ok = ServicePortAllocator.TryAllocate(definition, policy, probe, out var assignment, out var error);
                Assert.IsFalse(ok);
                Assert.IsNull(assignment);
                StringAssert.Contains("invalid base port", error);
            });
        }

        [Test]
        public void TryAllocate_NegativePreferredBaseInAuto_ReturnsFalse()
        {
            var definition = ServicePortDefinition.Create(
                "unity-editor-debug-mcp", "EditorDebugMCP", 1, new[] { 21891, 21892, 21893 });
            var policy = new ServicePortPolicy
            {
                ProjectRoot = "G:/A",
                ProjectId = "project-a",
                Mode = ServicePortMode.Auto,
                PreferredBasePort = -21900,
                PreferLegacyPorts = true,
            };
            var probe = new FakePortProbe(Array.Empty<int>());

            Assert.DoesNotThrow(() =>
            {
                var ok = ServicePortAllocator.TryAllocate(definition, policy, probe, out var assignment, out var error);
                Assert.IsFalse(ok);
                Assert.IsNull(assignment);
                StringAssert.Contains("invalid preferred base port", error);
            });
        }

        [Test]
        public void TryAllocate_PreferredOverflow_ReturnsFalse()
        {
            var definition = ServicePortDefinition.Create(
                "unity-editor-debug-mcp", "EditorDebugMCP", 9, new[] { 21891, 21892, 21893 });
            var policy = new ServicePortPolicy
            {
                ProjectRoot = "G:/A",
                ProjectId = "project-a",
                Mode = ServicePortMode.FixedProject,
                PreferredBasePort = 65530,
            };
            var probe = new FakePortProbe(Array.Empty<int>());

            Assert.DoesNotThrow(() =>
            {
                var ok = ServicePortAllocator.TryAllocate(definition, policy, probe, out var assignment, out var error);
                Assert.IsFalse(ok);
                Assert.IsNull(assignment);
                StringAssert.Contains("final port out of range", error);
                StringAssert.Contains("65539", error);
            });
        }

        private sealed class FakePortProbe : IPortProbe
        {
            private readonly HashSet<int> m_Occupied;

            public FakePortProbe(IEnumerable<int> occupied)
            {
                m_Occupied = new HashSet<int>(occupied);
            }

            public bool IsAvailable(int port)
            {
                return !m_Occupied.Contains(port);
            }
        }
    }
}
