using NUnit.Framework;
using Yoji.EditorDebug;

namespace Yoji.EditorDebug.Tests
{
    public sealed class EditorDebugMCPPortTests
    {
        [Test]
        public void PortDefinition_KeepsServiceIdentityOffsetAndLegacyPorts()
        {
            var definition = EditorDebugMCP.PortDefinitionForTests;

            Assert.AreEqual("unity-editor-debug-mcp", definition.ServiceId);
            Assert.AreEqual("EditorDebugMCP", definition.DisplayName);
            Assert.AreEqual(1, definition.Offset);
            CollectionAssert.AreEqual(new[] { 21891, 21892, 21893 }, definition.LegacyPorts);
        }
    }
}
