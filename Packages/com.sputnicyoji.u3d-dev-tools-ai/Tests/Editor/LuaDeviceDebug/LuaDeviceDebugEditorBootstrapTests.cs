using NUnit.Framework;
using Yoji.EditorCore.Ports;
using Yoji.LuaDeviceDebug.Editor;

namespace Yoji.LuaDeviceDebug.Tests
{
    public sealed class LuaDeviceDebugEditorBootstrapTests
    {
        [Test]
        public void PortDefinition_UsesLuaServiceIdentityAndEditorOffset()
        {
            var definition = LuaDeviceDebugEditorBootstrap.PortDefinitionForTests;

            Assert.AreEqual(LuaDeviceDebugPackage.ServiceId, definition.ServiceId);
            Assert.AreEqual(LuaDeviceDebugPackage.ServiceName, definition.DisplayName);
            Assert.AreEqual(LuaDeviceDebugPackage.EditorPortOffset, definition.Offset);
            CollectionAssert.AreEqual(new[] { LuaDeviceDebugPackage.DefaultPort }, definition.LegacyPorts);
        }
    }
}
