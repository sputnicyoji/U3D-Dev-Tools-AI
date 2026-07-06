using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Yoji.EditorCore.Ports
{
    public static class ProjectPortsFile
    {
        public static List<ServiceInstanceRecord> Load(string projectRoot)
        {
            var normalizedRoot = PortPersistencePaths.NormalizeProjectRoot(projectRoot);
            var path = PortPersistencePaths.ProjectPortsPath(normalizedRoot);
            var document = ReadDocument(path);
            return CloneInstances(document);
        }

        public static void Upsert(string projectRoot, ServiceInstanceRecord record)
        {
            Upsert(projectRoot, record, null);
        }

        /// isProcessAlive 语义与 ServiceInstanceRegistry.Register(record, isProcessAlive) 一致:
        /// 剔除同 serviceId+processId 旧行与死进程残留行, 注册/心跳时自愈。
        public static void Upsert(string projectRoot, ServiceInstanceRecord record, Func<int, bool> isProcessAlive)
        {
            if (record == null)
                throw new ArgumentNullException(nameof(record));
            if (string.IsNullOrWhiteSpace(record.InstanceId))
                throw new ArgumentException("record.InstanceId is required", nameof(record));

            var normalizedRoot = PortPersistencePaths.NormalizeProjectRoot(projectRoot);
            var path = PortPersistencePaths.ProjectPortsPath(normalizedRoot);
            using (PortPersistenceLock.Acquire(path))
            {
                var document = ReadDocument(path);
                var documentProjectId = ResolveProjectId(normalizedRoot, record.ProjectId);
                var instances = new List<ServiceInstanceRecordDto>();
                if (document.instances != null)
                {
                    for (var i = 0; i < document.instances.Length; i++)
                    {
                        var existing = document.instances[i];
                        if (existing == null)
                            continue;
                        if (string.Equals(existing.instanceId, record.InstanceId, StringComparison.Ordinal))
                            continue;
                        if (isProcessAlive != null)
                        {
                            if (string.Equals(existing.serviceId, record.ServiceId, StringComparison.Ordinal)
                                && existing.processId == record.ProcessId)
                                continue;
                            if (!isProcessAlive(existing.processId))
                                continue;
                        }

                        instances.Add(existing);
                    }
                }

                var recordDto = ServiceInstanceRecordMapper.ToDto(record);
                if (string.IsNullOrWhiteSpace(recordDto.projectId))
                    recordDto.projectId = documentProjectId;
                instances.Add(recordDto);
                document.schemaVersion = 1;
                document.projectRoot = normalizedRoot;
                document.projectId = documentProjectId;
                document.updatedUtc = DateTime.UtcNow.ToString("o");
                document.instances = instances.ToArray();
                WriteDocument(path, document);
            }
        }

        public static bool Remove(string projectRoot, string instanceId)
        {
            if (string.IsNullOrWhiteSpace(instanceId))
                return false;

            var normalizedRoot = PortPersistencePaths.NormalizeProjectRoot(projectRoot);
            var path = PortPersistencePaths.ProjectPortsPath(normalizedRoot);
            var removed = false;

            using (PortPersistenceLock.Acquire(path))
            {
                var document = ReadDocument(path);
                var instances = new List<ServiceInstanceRecordDto>();
                if (document.instances != null)
                {
                    for (var i = 0; i < document.instances.Length; i++)
                    {
                        var existing = document.instances[i];
                        if (existing != null && string.Equals(existing.instanceId, instanceId, StringComparison.Ordinal))
                        {
                            removed = true;
                            continue;
                        }

                        if (existing != null)
                            instances.Add(existing);
                    }
                }

                if (!removed)
                    return false;

                document.schemaVersion = 1;
                document.projectRoot = normalizedRoot;
                if (string.IsNullOrEmpty(document.projectId) && instances.Count > 0)
                    document.projectId = instances[0].projectId ?? string.Empty;
                document.projectId = ResolveProjectId(normalizedRoot, document.projectId);
                document.updatedUtc = DateTime.UtcNow.ToString("o");
                document.instances = instances.ToArray();
                WriteDocument(path, document);
            }

            return true;
        }

        private static ProjectPortsDocumentDto ReadDocument(string path)
        {
            var document = PortPersistenceIO.ReadJson<ProjectPortsDocumentDto>(path);
            if (document == null)
                document = new ProjectPortsDocumentDto();
            if (document.instances == null)
                document.instances = new ServiceInstanceRecordDto[0];
            if (document.schemaVersion <= 0)
                document.schemaVersion = 1;
            if (string.IsNullOrWhiteSpace(document.updatedUtc))
                document.updatedUtc = string.Empty;
            return document;
        }

        private static List<ServiceInstanceRecord> CloneInstances(ProjectPortsDocumentDto document)
        {
            var result = new List<ServiceInstanceRecord>();
            if (document == null || document.instances == null)
                return result;

            for (var i = 0; i < document.instances.Length; i++)
            {
                var record = ServiceInstanceRecordMapper.FromDto(document.instances[i]);
                if (record != null)
                    result.Add(record);
            }

            return result;
        }

        private static void WriteDocument(string path, ProjectPortsDocumentDto document)
        {
            PortPersistenceIO.WriteJsonAtomic(path, JsonUtility.ToJson(document, true), false);
        }

        private static string ResolveProjectId(string normalizedRoot, string preferredProjectId)
        {
            if (!string.IsNullOrWhiteSpace(preferredProjectId))
                return preferredProjectId;

            if (string.IsNullOrWhiteSpace(normalizedRoot))
                return string.Empty;

            return ServicePortSettingsStore.LoadOrCreateProjectSettings(normalizedRoot).ProjectId ?? string.Empty;
        }

        [Serializable]
        private sealed class ProjectPortsDocumentDto
        {
            public int schemaVersion = 1;
            public string projectRoot = string.Empty;
            public string projectId = string.Empty;
            public string updatedUtc = string.Empty;
            public ServiceInstanceRecordDto[] instances = new ServiceInstanceRecordDto[0];
        }
    }
}
