using NUnit.Framework;

namespace Yoji.LuaDeviceDebug.Tests
{
    public sealed class PackageShapeTests
    {
        [Test]
        public void Constants_MatchDesign()
        {
            Assert.AreEqual("LuaDeviceDebug", LuaDeviceDebugPackage.ServiceName);
            Assert.AreEqual(21894, LuaDeviceDebugPackage.DefaultPort);
        }
    }
}
