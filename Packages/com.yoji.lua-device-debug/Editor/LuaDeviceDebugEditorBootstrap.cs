using System;
using UnityEditor;
using UnityEngine;
using Yoji.EditorCore.Ports;
using Yoji.EditorCore;
using Process = System.Diagnostics.Process;

namespace Yoji.LuaDeviceDebug.Editor
{
    [InitializeOnLoad]
    internal static class LuaDeviceDebugEditorBootstrap
    {
        private static readonly ServicePortDefinition k_PortDefinition = ServicePortDefinition.Create(
            LuaDeviceDebugPackage.ServiceId,
            LuaDeviceDebugPackage.ServiceName,
            4,
            new int[] { LuaDeviceDebugPackage.DefaultPort });

        private static LuaDeviceDebugServer s_Server;
        private static ServiceInstanceRecord s_Record;
        private static double s_NextHeartbeatAt;
        private static double s_NextHeartbeatErrorLogAt;

        static LuaDeviceDebugEditorBootstrap()
        {
            EditorServiceLifecycle.Bind(Start, Stop);
        }

        private static void Start()
        {
            if (s_Server != null && s_Server.IsRunning) return;
            try
            {
                var identity = ProjectIdentityProvider.Current();
                var policy = ServicePortSettingsStore.BuildPolicy(identity.ProjectRoot, identity);
                var assignment = ServicePortAllocator.Allocate(k_PortDefinition, policy, new TcpPortProbe());

                s_Record = ServiceInstanceRecord.Create(
                    assignment.ServiceId,
                    assignment.DisplayName,
                    assignment.ProjectRoot,
                    assignment.ProjectId,
                    Process.GetCurrentProcess().Id,
                    assignment.Port,
                    assignment.Source);
                s_Server = new LuaDeviceDebugServer(assignment.Port);
                s_Server.SetServiceMetadata(LuaDeviceDebugServiceMetadata.Create(
                    s_Record.ServiceId,
                    s_Record.InstanceId,
                    s_Record.ProcessId,
                    s_Record.ProjectId,
                    s_Record.ProjectRoot,
                    s_Record.PortSource));
                s_Server.Start();
                ServiceInstanceRegistry.Default().Register(s_Record);
                ProjectPortsFile.Upsert(s_Record.ProjectRoot, s_Record);

                s_NextHeartbeatAt = EditorApplication.timeSinceStartup + 10.0;
                s_NextHeartbeatErrorLogAt = 0.0;
                EditorApplication.update += Heartbeat;
                Debug.Log("[LuaDeviceDebug] 服务已启动，监听 http://127.0.0.1:" + assignment.Port + "/");
            }
            catch (System.Exception e)
            {
                Stop();
                Debug.LogError("[LuaDeviceDebug] 服务启动失败: " + e.Message);
            }
        }

        private static void Stop()
        {
            EditorApplication.update -= Heartbeat;

            var record = s_Record;
            if (record != null)
            {
                try { ServiceInstanceRegistry.Default().Unregister(record.InstanceId); }
                catch (Exception e) { Debug.LogError("[LuaDeviceDebug] 注销全局实例失败: " + e.Message); }

                try { ProjectPortsFile.Remove(record.ProjectRoot, record.InstanceId); }
                catch (Exception e) { Debug.LogError("[LuaDeviceDebug] 移除项目端口记录失败: " + e.Message); }
            }

            if (s_Server != null)
            {
                s_Server.Stop();
                s_Server = null;
            }

            s_Record = null;
            s_NextHeartbeatAt = 0.0;
            s_NextHeartbeatErrorLogAt = 0.0;
        }

        private static void Heartbeat()
        {
            if (s_Record == null)
                return;

            var now = EditorApplication.timeSinceStartup;
            if (now < s_NextHeartbeatAt)
                return;

            s_NextHeartbeatAt = now + 10.0;

            try
            {
                s_Record.Touch();
                ServiceInstanceRegistry.Default().Register(s_Record);
                ProjectPortsFile.Upsert(s_Record.ProjectRoot, s_Record);
            }
            catch (Exception e)
            {
                if (now >= s_NextHeartbeatErrorLogAt)
                {
                    s_NextHeartbeatErrorLogAt = now + 60.0;
                    Debug.LogError("[LuaDeviceDebug] 心跳刷新失败: " + e.Message);
                }
            }
        }
    }
}
