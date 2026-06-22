using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Yoji.EditorCore;
using Process = System.Diagnostics.Process;

namespace Yoji.LuaDeviceDebug.Editor
{
    [InitializeOnLoad]
    internal static class LuaDeviceDebugEditorBootstrap
    {
        private static LuaDeviceDebugServer s_Server;

        static LuaDeviceDebugEditorBootstrap()
        {
            EditorServiceLifecycle.Bind(Start, Stop);
        }

        private static void Start()
        {
            if (s_Server != null && s_Server.IsRunning) return;
            try
            {
                var projectRoot = Directory.GetParent(Application.dataPath).FullName;
                var instanceId = Guid.NewGuid().ToString("N");
                s_Server = new LuaDeviceDebugServer(LuaDeviceDebugPackage.DefaultPort);
                s_Server.SetServiceMetadata(LuaDeviceDebugServiceMetadata.Create(
                    LuaDeviceDebugPackage.ServiceId,
                    instanceId,
                    Process.GetCurrentProcess().Id,
                    string.Empty,
                    projectRoot,
                    "fixed"));
                s_Server.Start();
                Debug.Log("[LuaDeviceDebug] 服务已启动，监听 http://127.0.0.1:" + LuaDeviceDebugPackage.DefaultPort + "/");
            }
            catch (System.Exception e)
            {
                Stop();
                Debug.LogError("[LuaDeviceDebug] 服务启动失败: " + e.Message);
            }
        }

        private static void Stop()
        {
            if (s_Server != null)
            {
                s_Server.Stop();
                s_Server = null;
            }
        }
    }
}
