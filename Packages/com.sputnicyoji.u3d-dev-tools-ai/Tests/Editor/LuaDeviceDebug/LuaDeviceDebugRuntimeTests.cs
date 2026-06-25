using System;
using NUnit.Framework;

namespace Yoji.LuaDeviceDebug.Tests
{
    public sealed class LuaDeviceDebugRuntimeTests
    {
        [TearDown]
        public void TearDown()
        {
            LuaDeviceDebugRuntime.UnregisterHost(LuaDeviceDebugRuntime.CurrentHost);
        }

        [Test]
        public void RegisterHost_StoresCurrentHost()
        {
            var host = new FakeHost();

            LuaDeviceDebugRuntime.RegisterHost(host);

            Assert.AreSame(host, LuaDeviceDebugRuntime.CurrentHost);
        }

        [Test]
        public void RegisterHost_RejectsDifferentHost()
        {
            var host = new FakeHost();
            LuaDeviceDebugRuntime.RegisterHost(host);

            Assert.Throws<InvalidOperationException>(() => LuaDeviceDebugRuntime.RegisterHost(new FakeHost()));
        }

        [Test]
        public void UnregisterHost_IgnoresDifferentHost()
        {
            var host = new FakeHost();
            LuaDeviceDebugRuntime.RegisterHost(host);

            LuaDeviceDebugRuntime.UnregisterHost(new FakeHost());

            Assert.AreSame(host, LuaDeviceDebugRuntime.CurrentHost);
        }
    }
}
