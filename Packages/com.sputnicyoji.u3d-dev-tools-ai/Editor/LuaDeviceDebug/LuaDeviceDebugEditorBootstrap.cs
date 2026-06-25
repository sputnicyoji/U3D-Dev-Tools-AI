using System;
using UnityEditor;
using UnityEngine;
using Yoji.EditorCore;
using Yoji.EditorCore.Ports;

namespace Yoji.LuaDeviceDebug.Editor
{
    [InitializeOnLoad]
    internal static class LuaDeviceDebugEditorBootstrap
    {
        private static readonly ServicePortDefinition k_PortDefinition = ServicePortDefinition.Create(
            LuaDeviceDebugPackage.ServiceId,
            LuaDeviceDebugPackage.ServiceName,
            LuaDeviceDebugPackage.EditorPortOffset,
            LuaDeviceDebugPackage.CreateEditorLegacyPorts());

        private static LuaDeviceDebugServer s_Server;
        private static EditorServiceEndpoint s_Endpoint;

        internal static ServicePortDefinition PortDefinitionForTests
        {
            get { return k_PortDefinition; }
        }

        static LuaDeviceDebugEditorBootstrap()
        {
            EditorServiceLifecycle.Bind(Start, Stop);
        }

        private static void Start()
        {
            if (s_Endpoint != null)
            {
                s_Endpoint.Heartbeat();
                return;
            }

            try
            {
                s_Endpoint = EditorServiceEndpoint.Start(k_PortDefinition, StartServer);
                var record = s_Endpoint.Record;
                if (s_Server != null)
                {
                    s_Server.SetServiceMetadata(LuaDeviceDebugServiceMetadata.Create(
                        LuaDeviceDebugPackage.ServiceId,
                        record.InstanceId,
                        record.ProcessId,
                        record.ProjectId,
                        record.ProjectRoot,
                        record.PortSource));
                }

                Debug.Log("[LuaDeviceDebug] 服务已启动，监听 http://127.0.0.1:" + record.Port + "/");
            }
            catch (Exception e)
            {
                Stop();
                Debug.LogError("[LuaDeviceDebug] 服务启动失败: " + e.Message);
            }
        }

        private static IDisposable StartServer(ServicePortAssignment assignment)
        {
            s_Server = new LuaDeviceDebugServer(assignment.Port);
            s_Server.Start();
            return new CallbackDisposable(() =>
            {
                if (s_Server != null)
                {
                    s_Server.Dispose();
                    s_Server = null;
                }
            });
        }

        private static void Stop()
        {
            if (s_Endpoint != null)
            {
                s_Endpoint.Dispose();
                s_Endpoint = null;
            }

            s_Server = null;
        }
    }
}
