using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEditor;
using UnityEngine;
using Process = System.Diagnostics.Process;

namespace Yoji.EditorCore.Ports
{
    public sealed class EditorServiceEndpoint : IDisposable
    {
        private const double k_HeartbeatIntervalSeconds = 10.0;
        private const double k_HeartbeatErrorLogIntervalSeconds = 60.0;

        private readonly ServiceInstanceRegistry m_Registry;
        private readonly Action<string, ServiceInstanceRecord> m_ProjectUpsert;
        private readonly Action<string, string> m_ProjectRemove;
        private readonly IDisposable m_Listener;
        private readonly object m_Gate = new object();
        private double m_NextHeartbeatAt;
        private double m_NextHeartbeatErrorLogAt;
        private bool m_Disposed;

        public ServiceInstanceRecord Record { get; private set; }
        public ServicePortAssignment Assignment { get; private set; }

        private EditorServiceEndpoint(
            ServicePortAssignment assignment,
            ServiceInstanceRecord record,
            ServiceInstanceRegistry registry,
            Action<string, ServiceInstanceRecord> projectUpsert,
            Action<string, string> projectRemove,
            IDisposable listener)
        {
            Assignment = assignment;
            Record = record;
            m_Registry = registry;
            m_ProjectUpsert = projectUpsert;
            m_ProjectRemove = projectRemove;
            m_Listener = listener;
            m_NextHeartbeatAt = EditorApplication.timeSinceStartup + k_HeartbeatIntervalSeconds;
        }

        public static EditorServiceEndpoint Start(
            ServicePortDefinition definition,
            Func<ServicePortAssignment, IDisposable> listenerFactory,
            int maxStartAttempts = 8)
        {
            var identity = ProjectIdentityProvider.Current();
            var policy = ServicePortSettingsStore.BuildPolicy(identity.ProjectRoot, identity);
            return StartForTests(
                definition,
                policy,
                identity,
                ServiceInstanceRegistry.Default(),
                listenerFactory,
                ProjectPortsFile.Upsert,
                (root, instanceId) => { ProjectPortsFile.Remove(root, instanceId); },
                maxStartAttempts);
        }

        internal static EditorServiceEndpoint StartForTests(
            ServicePortDefinition definition,
            ServicePortPolicy policy,
            ProjectIdentity identity,
            ServiceInstanceRegistry registry,
            Func<ServicePortAssignment, IDisposable> listenerFactory,
            Action<string, ServiceInstanceRecord> projectUpsert,
            Action<string, string> projectRemove,
            int maxStartAttempts = 8)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));
            if (policy == null)
                throw new ArgumentNullException(nameof(policy));
            if (listenerFactory == null)
                throw new ArgumentNullException(nameof(listenerFactory));
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));
            if (projectUpsert == null)
                throw new ArgumentNullException(nameof(projectUpsert));
            if (projectRemove == null)
                throw new ArgumentNullException(nameof(projectRemove));
            if (maxStartAttempts <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxStartAttempts), "maxStartAttempts must be positive");

            var blockedPorts = new HashSet<int>();
            var attemptedPorts = new List<int>();
            var probe = new ExcludingPortProbe(new TcpPortProbe(), blockedPorts);
            Exception lastException = null;
            string lastAllocationError = null;

            for (var attempt = 0; attempt < maxStartAttempts; attempt++)
            {
                ServicePortAssignment assignment;
                string allocationError;
                if (!ServicePortAllocator.TryAllocate(definition, policy, probe, out assignment, out allocationError))
                {
                    lastAllocationError = allocationError;
                    break;
                }

                attemptedPorts.Add(assignment.Port);
                IDisposable listener = null;
                ServiceInstanceRecord record = null;
                try
                {
                    listener = listenerFactory(assignment);
                    if (listener == null)
                        listener = NullDisposable.Instance;

                    record = ServiceInstanceRecord.Create(
                        assignment.ServiceId,
                        assignment.DisplayName,
                        assignment.ProjectRoot,
                        assignment.ProjectId,
                        Process.GetCurrentProcess().Id,
                        assignment.Port,
                        assignment.Source);

                    registry.Register(record);
                    projectUpsert(record.ProjectRoot, record);

                    return new EditorServiceEndpoint(
                        assignment,
                        record,
                        registry,
                        projectUpsert,
                        projectRemove,
                        listener);
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    if (record != null)
                    {
                        try { registry.Unregister(record.InstanceId); }
                        catch (Exception) { }
                        try { projectRemove(record.ProjectRoot, record.InstanceId); }
                        catch (Exception) { }
                    }

                    if (listener != null)
                    {
                        try { listener.Dispose(); }
                        catch (Exception) { }
                    }

                    var startConflict = IsStartConflict(ex);
                    if (!startConflict)
                        throw;
                    if (!CanRetryWithAnotherPort(policy))
                        throw new InvalidOperationException(
                            "configured endpoint port is occupied for " + definition.ServiceId + ": " + assignment.Port,
                            ex);

                    blockedPorts.Add(assignment.Port);
                }
            }

            var message = "no available endpoint for " + definition.ServiceId;
            if (attemptedPorts.Count > 0)
                message += "; attempted ports: " + string.Join(", ", attemptedPorts.ToArray());
            if (!string.IsNullOrEmpty(lastAllocationError))
                message += "; allocation error: " + lastAllocationError;
            if (lastException != null)
                message += "; last error: " + lastException.Message;
            throw new InvalidOperationException(message, lastException);
        }

        public void Heartbeat(bool force = false)
        {
            lock (m_Gate)
            {
                if (m_Disposed || Record == null)
                    return;

                var now = EditorApplication.timeSinceStartup;
                if (!force && now < m_NextHeartbeatAt)
                    return;

                m_NextHeartbeatAt = now + k_HeartbeatIntervalSeconds;
                try
                {
                    Record.Touch();
                    m_Registry.Register(Record);
                    m_ProjectUpsert(Record.ProjectRoot, Record);
                }
                catch (Exception e)
                {
                    if (now >= m_NextHeartbeatErrorLogAt)
                    {
                        m_NextHeartbeatErrorLogAt = now + k_HeartbeatErrorLogIntervalSeconds;
                        Debug.LogError("[EditorServiceEndpoint] 心跳刷新失败: " + e.Message);
                    }
                }
            }
        }

        public void Dispose()
        {
            ServiceInstanceRecord record;
            lock (m_Gate)
            {
                if (m_Disposed)
                    return;
                m_Disposed = true;
                record = Record;
                Record = null;
                Assignment = null;
            }

            try { m_Listener.Dispose(); }
            catch (Exception) { }

            if (record != null)
            {
                try { m_Registry.Unregister(record.InstanceId); }
                catch (Exception e) { Debug.LogError("[EditorServiceEndpoint] 注销全局实例失败: " + e.Message); }

                try { m_ProjectRemove(record.ProjectRoot, record.InstanceId); }
                catch (Exception e) { Debug.LogError("[EditorServiceEndpoint] 移除项目端口记录失败: " + e.Message); }
            }
        }

        private static bool CanRetryWithAnotherPort(ServicePortPolicy policy)
        {
            return policy.OverrideBasePort <= 0 && policy.Mode == ServicePortMode.Auto;
        }

        private static bool IsStartConflict(Exception exception)
        {
            var current = exception;
            while (current != null)
            {
                var socket = current as SocketException;
                if (socket != null && socket.SocketErrorCode == SocketError.AddressAlreadyInUse)
                    return true;

                var http = current as HttpListenerException;
                if (http != null && (http.ErrorCode == 32 || http.ErrorCode == 87 || http.ErrorCode == 183))
                    return true;

                current = current.InnerException;
            }

            return false;
        }

        private sealed class ExcludingPortProbe : IPortProbe
        {
            private readonly IPortProbe m_Inner;
            private readonly HashSet<int> m_Blocked;

            public ExcludingPortProbe(IPortProbe inner, HashSet<int> blocked)
            {
                m_Inner = inner;
                m_Blocked = blocked;
            }

            public bool IsAvailable(int port)
            {
                return !m_Blocked.Contains(port) && m_Inner.IsAvailable(port);
            }
        }

        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new NullDisposable();
            public void Dispose() { }
        }
    }

    public sealed class CallbackDisposable : IDisposable
    {
        private readonly Action m_Dispose;
        private int m_Disposed;

        public CallbackDisposable(Action dispose)
        {
            m_Dispose = dispose ?? delegate { };
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref m_Disposed, 1) == 0)
                m_Dispose();
        }
    }
}
