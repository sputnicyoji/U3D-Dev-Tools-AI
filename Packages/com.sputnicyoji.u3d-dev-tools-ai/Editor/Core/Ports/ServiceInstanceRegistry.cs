using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Yoji.EditorCore.Ports
{
    public sealed class ServiceInstanceRegistry
    {
        private readonly string m_FilePath;

        public ServiceInstanceRegistry(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("filePath is required", nameof(filePath));

            m_FilePath = Path.GetFullPath(filePath);
        }

        public static ServiceInstanceRegistry Default()
        {
            return new ServiceInstanceRegistry(DefaultFilePath);
        }

        public static string DefaultFilePath
        {
            get
            {
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(localAppData, "Yoji", "U3D-Dev-Tools-AI", "instances.json");
            }
        }

        public string FilePath
        {
            get { return m_FilePath; }
        }

        public List<ServiceInstanceRecord> Load()
        {
            var document = ReadDocument();
            return CloneInstances(document);
        }

        public void Register(ServiceInstanceRecord record)
        {
            if (record == null)
                throw new ArgumentNullException(nameof(record));
            if (string.IsNullOrWhiteSpace(record.InstanceId))
                throw new ArgumentException("record.InstanceId is required", nameof(record));

            using (PortPersistenceLock.Acquire(m_FilePath))
            {
                var document = ReadDocument();
                var instances = new List<ServiceInstanceRecordDto>();
                if (document.instances != null)
                {
                    for (var i = 0; i < document.instances.Length; i++)
                    {
                        var existing = document.instances[i];
                        if (existing != null && string.Equals(existing.instanceId, record.InstanceId, StringComparison.Ordinal))
                            continue;

                        if (existing != null)
                            instances.Add(existing);
                    }
                }

                instances.Add(ServiceInstanceRecordMapper.ToDto(record));
                document.updatedUtc = DateTime.UtcNow.ToString("o");
                document.instances = instances.ToArray();
                WriteDocument(document);
            }
        }

        public bool Unregister(string instanceId)
        {
            if (string.IsNullOrWhiteSpace(instanceId))
                return false;

            var removed = false;
            using (PortPersistenceLock.Acquire(m_FilePath))
            {
                var document = ReadDocument();
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

                document.updatedUtc = DateTime.UtcNow.ToString("o");
                document.instances = instances.ToArray();
                WriteDocument(document);
            }

            return true;
        }

        private ServiceInstanceCollectionDto ReadDocument()
        {
            var document = PortPersistenceIO.ReadJson<ServiceInstanceCollectionDto>(m_FilePath);
            if (document == null)
                document = new ServiceInstanceCollectionDto();
            if (document.instances == null)
                document.instances = new ServiceInstanceRecordDto[0];
            if (document.schemaVersion <= 0)
                document.schemaVersion = 1;
            if (string.IsNullOrWhiteSpace(document.updatedUtc))
                document.updatedUtc = string.Empty;
            return document;
        }

        private List<ServiceInstanceRecord> CloneInstances(ServiceInstanceCollectionDto document)
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

        private void WriteDocument(ServiceInstanceCollectionDto document)
        {
            PortPersistenceIO.WriteJsonAtomic(m_FilePath, JsonUtility.ToJson(document, true), false);
        }
    }
}
