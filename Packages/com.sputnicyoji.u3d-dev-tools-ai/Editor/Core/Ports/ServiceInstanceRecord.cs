using System;
using System.Diagnostics;

namespace Yoji.EditorCore.Ports
{
    [Serializable]
    public sealed class ServiceInstanceRecord
    {
        public string ServiceId = string.Empty;
        public string DisplayName = string.Empty;
        public string InstanceId = string.Empty;
        public int ProcessId = 0;
        public string ProjectRoot = string.Empty;
        public string ProjectId = string.Empty;
        public string Host = "127.0.0.1";
        public int Port = 0;
        public string PortSource = string.Empty;
        public string StartedUtc = string.Empty;
        public string LastSeenUtc = string.Empty;

        public static ServiceInstanceRecord Create(
            string serviceId,
            string displayName,
            string projectRoot,
            string projectId,
            int processId,
            int port,
            string portSource)
        {
            var now = DateTime.UtcNow.ToString("o");
            return new ServiceInstanceRecord
            {
                ServiceId = serviceId ?? string.Empty,
                DisplayName = displayName ?? string.Empty,
                InstanceId = Guid.NewGuid().ToString("N"),
                ProcessId = processId > 0 ? processId : Process.GetCurrentProcess().Id,
                ProjectRoot = PortPersistencePaths.NormalizeProjectRoot(projectRoot),
                ProjectId = projectId ?? string.Empty,
                Host = "127.0.0.1",
                Port = port,
                PortSource = portSource ?? string.Empty,
                StartedUtc = now,
                LastSeenUtc = now,
            };
        }

        public void Touch()
        {
            LastSeenUtc = DateTime.UtcNow.ToString("o");
        }
    }

    [Serializable]
    internal sealed class ServiceInstanceRecordDto
    {
        public string serviceId = string.Empty;
        public string displayName = string.Empty;
        public string instanceId = string.Empty;
        public int processId = 0;
        public string projectRoot = string.Empty;
        public string projectId = string.Empty;
        public string host = string.Empty;
        public int port = 0;
        public string portSource = string.Empty;
        public string startedUtc = string.Empty;
        public string lastSeenUtc = string.Empty;
    }

    [Serializable]
    internal sealed class ServiceInstanceCollectionDto
    {
        public int schemaVersion = 1;
        public string updatedUtc = string.Empty;
        public ServiceInstanceRecordDto[] instances = new ServiceInstanceRecordDto[0];
    }

    internal static class ServiceInstanceRecordMapper
    {
        public static ServiceInstanceRecordDto ToDto(ServiceInstanceRecord record)
        {
            if (record == null)
                return null;

            return new ServiceInstanceRecordDto
            {
                serviceId = record.ServiceId ?? string.Empty,
                displayName = record.DisplayName ?? string.Empty,
                instanceId = record.InstanceId ?? string.Empty,
                processId = record.ProcessId,
                projectRoot = record.ProjectRoot ?? string.Empty,
                projectId = record.ProjectId ?? string.Empty,
                host = record.Host ?? string.Empty,
                port = record.Port,
                portSource = record.PortSource ?? string.Empty,
                startedUtc = record.StartedUtc ?? string.Empty,
                lastSeenUtc = record.LastSeenUtc ?? string.Empty,
            };
        }

        public static ServiceInstanceRecord FromDto(ServiceInstanceRecordDto dto)
        {
            if (dto == null)
                return null;

            return new ServiceInstanceRecord
            {
                ServiceId = dto.serviceId ?? string.Empty,
                DisplayName = dto.displayName ?? string.Empty,
                InstanceId = dto.instanceId ?? string.Empty,
                ProcessId = dto.processId,
                ProjectRoot = PortPersistencePaths.NormalizeProjectRoot(dto.projectRoot),
                ProjectId = dto.projectId ?? string.Empty,
                Host = dto.host ?? string.Empty,
                Port = dto.port,
                PortSource = dto.portSource ?? string.Empty,
                StartedUtc = dto.startedUtc ?? string.Empty,
                LastSeenUtc = dto.lastSeenUtc ?? string.Empty,
            };
        }
    }
}
