using System;

namespace Yoji.LuaDeviceDebug
{
    public static class LuaDeviceDebugRuntime
    {
        private static readonly object s_Lock = new object();
        private static ILuaDeviceDebugHost s_Host;

        public static ILuaDeviceDebugHost CurrentHost
        {
            get
            {
                lock (s_Lock) return s_Host;
            }
        }

        public static void RegisterHost(ILuaDeviceDebugHost host)
        {
            if (host == null) throw new ArgumentNullException("host");
            lock (s_Lock)
            {
                if (s_Host != null && !ReferenceEquals(s_Host, host))
                    throw new InvalidOperationException("LuaDeviceDebug host is already registered");
                s_Host = host;
            }
        }

        public static void UnregisterHost(ILuaDeviceDebugHost host)
        {
            lock (s_Lock)
            {
                if (ReferenceEquals(s_Host, host))
                    s_Host = null;
            }
        }
    }
}
