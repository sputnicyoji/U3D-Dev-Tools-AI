using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Yoji.LuaDeviceDebug.Tests
{
    public sealed class PackageShapeTests
    {
        [Test]
        public void Constants_MatchDesign()
        {
            Assert.AreEqual("unity-lua-device-debug", LuaDeviceDebugPackage.ServiceId);
            Assert.AreEqual("LuaDeviceDebug", LuaDeviceDebugPackage.ServiceName);
            Assert.AreEqual(21894, LuaDeviceDebugPackage.DefaultPort);
            Assert.AreEqual("YojiLuaDeviceDebugRuntimeConfig", LuaDeviceDebugRuntimeConfig.ResourcesPath);
        }

        [Test]
        public void RuntimeConfig_ValidatesPortRange()
        {
            var method = typeof(LuaDeviceDebugRuntimeConfig).GetMethod(
                "ResolveRemotePort",
                BindingFlags.Static | BindingFlags.NonPublic,
                null,
                new[] { typeof(LuaDeviceDebugRuntimeConfig) },
                null);

            Assert.IsNotNull(method);

            var config = ScriptableObject.CreateInstance<LuaDeviceDebugRuntimeConfig>();
            try
            {
                Assert.AreEqual(LuaDeviceDebugPackage.DefaultPort, (int)method.Invoke(null, new object[] { null }));

                config.RemotePort = 1024;
                Assert.AreEqual(1024, (int)method.Invoke(null, new object[] { config }));

                config.RemotePort = 1023;
                Assert.AreEqual(LuaDeviceDebugPackage.DefaultPort, (int)method.Invoke(null, new object[] { config }));

                config.RemotePort = 65535;
                Assert.AreEqual(65535, (int)method.Invoke(null, new object[] { config }));

                config.RemotePort = 65536;
                Assert.AreEqual(LuaDeviceDebugPackage.DefaultPort, (int)method.Invoke(null, new object[] { config }));
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }
    }
}
