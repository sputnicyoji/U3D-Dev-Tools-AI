using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using NUnit.Framework;
using Yoji.EditorCore.Ports;

namespace Yoji.EditorCore.Tests
{
    public sealed class EditorServiceEndpointTests
    {
        [Test]
        public void Start_RegistersGlobalAndProjectRecord()
        {
            var registry = NewRegistry();
            var projectRecords = new Dictionary<string, ServiceInstanceRecord>();
            var disposed = false;

            var endpoint = EditorServiceEndpoint.StartForTests(
                Definition(),
                Policy(),
                Identity(),
                registry,
                assignment => new CallbackDisposable(() => disposed = true),
                (root, record) => projectRecords[record.InstanceId] = record,
                (root, id) => projectRecords.Remove(id));

            Assert.IsNotNull(endpoint.Record);
            Assert.AreEqual("endpoint-test", endpoint.Record.ServiceId);
            Assert.AreEqual(1, registry.Load().Count);
            Assert.IsTrue(projectRecords.ContainsKey(endpoint.Record.InstanceId));
            Assert.IsFalse(disposed);

            endpoint.Dispose();
        }

        [Test]
        public void Dispose_UnregistersGlobalAndProjectRecord()
        {
            var registry = NewRegistry();
            var projectRecords = new Dictionary<string, ServiceInstanceRecord>();

            var endpoint = EditorServiceEndpoint.StartForTests(
                Definition(),
                Policy(),
                Identity(),
                registry,
                assignment => new CallbackDisposable(() => { }),
                (root, record) => projectRecords[record.InstanceId] = record,
                (root, id) => projectRecords.Remove(id));
            var instanceId = endpoint.Record.InstanceId;

            endpoint.Dispose();

            Assert.AreEqual(0, registry.Load().Count);
            Assert.IsFalse(projectRecords.ContainsKey(instanceId));
        }

        [Test]
        public void Dispose_IsIdempotent()
        {
            var registry = NewRegistry();
            var disposeCount = 0;
            var projectRecords = new Dictionary<string, ServiceInstanceRecord>();

            var endpoint = EditorServiceEndpoint.StartForTests(
                Definition(),
                Policy(),
                Identity(),
                registry,
                assignment => new CallbackDisposable(() => disposeCount++),
                (root, record) => projectRecords[record.InstanceId] = record,
                (root, id) => projectRecords.Remove(id));

            endpoint.Dispose();
            endpoint.Dispose();

            Assert.AreEqual(1, disposeCount);
            Assert.AreEqual(0, registry.Load().Count);
        }

        [Test]
        public void Start_RetriesWhenFactoryThrowsAddressInUse()
        {
            var registry = NewRegistry();
            var projectRecords = new Dictionary<string, ServiceInstanceRecord>();
            var attempts = new List<int>();

            var endpoint = EditorServiceEndpoint.StartForTests(
                Definition(),
                Policy(),
                Identity(),
                registry,
                assignment =>
                {
                    attempts.Add(assignment.Port);
                    if (attempts.Count == 1)
                        throw AddressInUse();
                    return new CallbackDisposable(() => { });
                },
                (root, record) => projectRecords[record.InstanceId] = record,
                (root, id) => projectRecords.Remove(id));

            Assert.AreEqual(2, attempts.Count);
            Assert.AreNotEqual(attempts[0], endpoint.Record.Port);
            Assert.AreEqual(endpoint.Record.Port, attempts[1]);

            endpoint.Dispose();
        }

        [Test]
        public void Start_ThrowsAfterMaxAttempts()
        {
            var registry = NewRegistry();
            var projectRecords = new Dictionary<string, ServiceInstanceRecord>();
            var attempts = 0;

            var ex = Assert.Throws<InvalidOperationException>(() =>
            {
                EditorServiceEndpoint.StartForTests(
                    Definition(),
                    Policy(),
                    Identity(),
                    registry,
                    assignment =>
                    {
                        attempts++;
                        throw AddressInUse();
                    },
                    (root, record) => projectRecords[record.InstanceId] = record,
                    (root, id) => projectRecords.Remove(id),
                    2);
            });

            Assert.AreEqual(2, attempts);
            StringAssert.Contains("endpoint-test", ex.Message);
            StringAssert.Contains("attempted ports", ex.Message);
            Assert.AreEqual(0, registry.Load().Count);
            Assert.AreEqual(0, projectRecords.Count);
        }

        [Test]
        public void Start_DoesNotRetryConfiguredPortConflict()
        {
            var registry = NewRegistry();
            var projectRecords = new Dictionary<string, ServiceInstanceRecord>();
            var policy = Policy();
            policy.Mode = ServicePortMode.FixedProject;
            var attempts = 0;

            var ex = Assert.Throws<InvalidOperationException>(() =>
            {
                EditorServiceEndpoint.StartForTests(
                    Definition(),
                    policy,
                    Identity(),
                    registry,
                    assignment =>
                    {
                        attempts++;
                        throw AddressInUse();
                    },
                    (root, record) => projectRecords[record.InstanceId] = record,
                    (root, id) => projectRecords.Remove(id),
                    3);
            });

            Assert.AreEqual(1, attempts);
            StringAssert.Contains("configured endpoint port is occupied", ex.Message);
            Assert.AreEqual(0, registry.Load().Count);
            Assert.AreEqual(0, projectRecords.Count);
        }

        [Test]
        public void Heartbeat_RefreshesRecord()
        {
            var registry = NewRegistry();
            var projectRecords = new Dictionary<string, ServiceInstanceRecord>();

            var endpoint = EditorServiceEndpoint.StartForTests(
                Definition(),
                Policy(),
                Identity(),
                registry,
                assignment => new CallbackDisposable(() => { }),
                (root, record) => projectRecords[record.InstanceId] = record,
                (root, id) => projectRecords.Remove(id));
            var before = endpoint.Record.LastSeenUtc;

            Thread.Sleep(2);
            endpoint.Heartbeat(true);

            Assert.AreNotEqual(before, endpoint.Record.LastSeenUtc);
            Assert.AreEqual(endpoint.Record.LastSeenUtc, registry.Load()[0].LastSeenUtc);

            endpoint.Dispose();
        }

        [Test]
        public void Start_DisposesListenerWhenProjectUpsertFails()
        {
            var registry = NewRegistry();
            var disposed = false;

            Assert.Throws<InvalidOperationException>(() =>
            {
                EditorServiceEndpoint.StartForTests(
                    Definition(),
                    Policy(),
                    Identity(),
                    registry,
                    assignment => new CallbackDisposable(() => disposed = true),
                    (root, record) => { throw new InvalidOperationException("upsert failed"); },
                    (root, id) => { });
            });

            Assert.IsTrue(disposed);
            Assert.AreEqual(0, registry.Load().Count);
        }

        private static ServicePortDefinition Definition()
        {
            return ServicePortDefinition.Create("endpoint-test", "EndpointTest", 7, new int[0]);
        }

        private static ServicePortPolicy Policy()
        {
            return new ServicePortPolicy
            {
                ProjectRoot = Path.Combine(Path.GetTempPath(), "yoji-endpoint-project"),
                ProjectId = "endpoint-project",
                Mode = ServicePortMode.Auto,
                PreferredBasePort = 29980,
                PreferLegacyPorts = false,
            };
        }

        private static ProjectIdentity Identity()
        {
            return new ProjectIdentity
            {
                ProjectRoot = Path.Combine(Path.GetTempPath(), "yoji-endpoint-project"),
                ProjectId = "endpoint-project",
                ProjectName = "EndpointProject",
            };
        }

        private static ServiceInstanceRegistry NewRegistry()
        {
            var file = Path.Combine(Path.GetTempPath(), "yoji-endpoint-instances-" + Path.GetRandomFileName() + ".json");
            return new ServiceInstanceRegistry(file);
        }

        private static Exception AddressInUse()
        {
            return new InvalidOperationException("bind failed", new SocketException((int)SocketError.AddressAlreadyInUse));
        }
    }
}
