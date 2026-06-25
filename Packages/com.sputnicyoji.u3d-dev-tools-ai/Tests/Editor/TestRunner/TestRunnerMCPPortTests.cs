using NUnit.Framework;
using Yoji.TestRunner;

namespace Yoji.TestRunner.Tests
{
    public sealed class TestRunnerMCPPortTests
    {
        [Test]
        public void PortDefinition_KeepsServiceIdentityOffsetAndLegacyPorts()
        {
            var definition = TestRunnerMCP.PortDefinitionForTests;

            Assert.AreEqual("test-runner-mcp", definition.ServiceId);
            Assert.AreEqual("TestRunnerMCP", definition.DisplayName);
            Assert.AreEqual(0, definition.Offset);
            CollectionAssert.AreEqual(new[] { 21890, 21896, 21897 }, definition.LegacyPorts);
        }
    }
}
