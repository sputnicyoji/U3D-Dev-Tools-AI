#if DEVELOPMENT_BUILD && !UNITY_EDITOR
using UnityEngine;

namespace Yoji.LuaDeviceDebug
{
    internal static class LuaDeviceDebugPlayerBootstrap
    {
        private static LuaDeviceDebugServer s_Server;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Start()
        {
            if (Application.platform != RuntimePlatform.Android)
                return;

            if (s_Server != null && s_Server.IsRunning)
                return;

            try
            {
                var port = LuaDeviceDebugRuntimeConfig.ResolveRemotePort();
                s_Server = new LuaDeviceDebugServer(port);
                s_Server.Start();
                Application.quitting += Stop;
                Debug.Log("[LuaDeviceDebug] 服务已启动，监听 http://127.0.0.1:" + port + "/");
            }
            catch (System.Exception e)
            {
                s_Server = null;
                Debug.LogError("[LuaDeviceDebug] 服务启动失败: " + e.Message);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            Stop();
        }

        private static void Stop()
        {
            if (s_Server == null) return;
            s_Server.Stop();
            s_Server = null;
        }
    }
}
#endif
