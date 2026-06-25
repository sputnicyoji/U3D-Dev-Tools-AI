using UnityEngine;

namespace Yoji.LuaDeviceDebug
{
    public sealed class LuaDeviceDebugRuntimeConfig : ScriptableObject
    {
        public const string ResourcesPath = "YojiLuaDeviceDebugRuntimeConfig";

        [Min(1024)]
        public int RemotePort = LuaDeviceDebugPackage.DefaultPort;

        public static int ResolveRemotePort()
        {
            return ResolveRemotePort(Resources.Load<LuaDeviceDebugRuntimeConfig>(ResourcesPath));
        }

        internal static int ResolveRemotePort(LuaDeviceDebugRuntimeConfig config)
        {
            if (config == null)
                return LuaDeviceDebugPackage.DefaultPort;

            return IsValidRemotePort(config.RemotePort) ? config.RemotePort : LuaDeviceDebugPackage.DefaultPort;
        }

        private static bool IsValidRemotePort(int port)
        {
            return port >= 1024 && port <= 65535;
        }
    }
}
