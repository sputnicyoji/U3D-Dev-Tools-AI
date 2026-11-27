using NUnit.Framework;
using Yoji.EditorCore;
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

        [TestCase("/recompile")]
        [TestCase("/run-tests")]
        public void Guard_GetOnMutatingRoute_IsRejected(string path)
        {
            Assert.AreEqual(path + " requires POST", RequestGuard.Check("GET", path, null, TestRunnerMCP.MutatingRoutes));
            Assert.IsNull(RequestGuard.Check("POST", path, null, TestRunnerMCP.MutatingRoutes));
        }

        [TestCase("/ping")]
        [TestCase("/test-status")]
        [TestCase("/list-tests")]
        public void Guard_GetOnReadOnlyRoute_IsAllowed(string path)
        {
            Assert.IsNull(RequestGuard.Check("GET", path, null, TestRunnerMCP.MutatingRoutes));
        }
    }
}
