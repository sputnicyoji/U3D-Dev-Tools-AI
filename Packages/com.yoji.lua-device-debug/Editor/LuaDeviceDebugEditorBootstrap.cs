using UnityEditor;
using UnityEngine;

namespace Yoji.LuaDeviceDebug.Editor
{
    [InitializeOnLoad]
    internal static class LuaDeviceDebugEditorBootstrap
    {
        private static LuaDeviceDebugServer s_Server;

        static LuaDeviceDebugEditorBootstrap()
        {
            EditorApplication.delayCall += Start;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.quitting += Stop;
        }

        private static void Start()
        {
            if (s_Server != null && s_Server.IsRunning) return;
            try
            {
                s_Server = new LuaDeviceDebugServer(LuaDeviceDebugPackage.DefaultPort);
                s_Server.Start();
                Debug.Log("[LuaDeviceDebug] 服务已启动，监听 http://127.0.0.1:" + LuaDeviceDebugPackage.DefaultPort + "/");
            }
            catch (System.Exception e)
            {
                s_Server = null;
                Debug.LogError("[LuaDeviceDebug] 服务启动失败: " + e.Message);
            }
        }

        private static void Stop()
        {
            if (s_Server == null) return;
            s_Server.Stop();
            s_Server = null;
        }
    }
}
