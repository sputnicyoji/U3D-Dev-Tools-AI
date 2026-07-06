using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using Yoji.EditorCore.Ports;

namespace Yoji.EditorCore.Tests
{
    public sealed class ServiceInstanceRegistryTests
    {
        [Test]
        public void Register_ReplacesSameInstanceId_AndUnregisterRemoves()
        {
            var file = Path.Combine(Path.GetTempPath(), "yoji-instances-" + Path.GetRandomFileName() + ".json");
            var registry = new ServiceInstanceRegistry(file);
            var record = ServiceInstanceRecord.Create("test-runner-mcp", "TestRunnerMCP", "G:/A", "p", 123, 21900, "project-auto");

            registry.Register(record);
            record.Port = 21910;
            registry.Register(record);

            var loaded = registry.Load();
            Assert.AreEqual(1, loaded.Count);
            Assert.AreEqual(21910, loaded[0].Port);
            StringAssert.Contains("\"schemaVersion\"", File.ReadAllText(file));

            Assert.IsTrue(registry.Unregister(record.InstanceId));
            Assert.AreEqual(0, registry.Load().Count);
        }

        // stale 注册自愈回归: 带 liveness 谓词的 Register 必须清掉 (a) 死进程残留行 (b) 同 serviceId+pid 前身行,
        // 同时保留其它服务的活进程行. 崩溃/强杀不走 Unregister, 这条路径是注册表唯一的清理点.
        [Test]
        public void Register_WithLiveness_PrunesDeadAndSupersedesSameServicePid()
        {
            var file = Path.Combine(Path.GetTempPath(), "yoji-instances-" + Path.GetRandomFileName() + ".json");
            var registry = new ServiceInstanceRegistry(file);

            var deadOther = ServiceInstanceRecord.Create("test-runner-mcp", "TestRunnerMCP", "G:/A", "p", 111, 21900, "project-auto");
            var alivePeer = ServiceInstanceRecord.Create("unity-editor-debug-mcp", "EditorDebugMCP", "G:/A", "p", 222, 21891, "project-auto");
            var stalePredecessor = ServiceInstanceRecord.Create("unity-editor-debug-mcp", "EditorDebugMCP", "G:/A", "p", 333, 21892, "project-auto");
            registry.Register(deadOther);
            registry.Register(alivePeer);
            registry.Register(stalePredecessor);
            Assert.AreEqual(3, registry.Load().Count);

            // pid 333 重新注册 (模拟 domain reload 后换 instanceId/port), pid 111 已死, pid 222 存活.
            var reborn = ServiceInstanceRecord.Create("unity-editor-debug-mcp", "EditorDebugMCP", "G:/A", "p", 333, 21893, "project-auto");
            registry.Register(reborn, pid => pid != 111);

            var loaded = registry.Load();
            Assert.AreEqual(2, loaded.Count);
            foreach (var record in loaded)
            {
                Assert.AreNotEqual(111, record.ProcessId, "dead pid row must be pruned");
                if (record.ProcessId == 333)
                {
                    Assert.AreEqual(reborn.InstanceId, record.InstanceId, "same service+pid predecessor must be superseded");
                    Assert.AreEqual(21893, record.Port);
                }
            }
        }

        [Test]
        public void Register_WithoutLiveness_KeepsForeignRows()
        {
            var file = Path.Combine(Path.GetTempPath(), "yoji-instances-" + Path.GetRandomFileName() + ".json");
            var registry = new ServiceInstanceRegistry(file);
            registry.Register(ServiceInstanceRecord.Create("test-runner-mcp", "TestRunnerMCP", "G:/A", "p", 111, 21900, "project-auto"));

            registry.Register(ServiceInstanceRecord.Create("test-runner-mcp", "TestRunnerMCP", "G:/A", "p", 111, 21901, "project-auto"));

            // 无谓词 = 旧语义: 只按 instanceId 去重, 不做任何清理.
            Assert.AreEqual(2, registry.Load().Count);
        }

        [Test]
        public void Load_MalformedJson_ReturnsEmpty()
        {
            var file = Path.Combine(Path.GetTempPath(), "yoji-instances-" + Path.GetRandomFileName() + ".json");
            File.WriteAllText(file, "{not json");
            var registry = new ServiceInstanceRegistry(file);

            Assert.DoesNotThrow(() =>
            {
                var loaded = registry.Load();
                Assert.AreEqual(0, loaded.Count);
            });
        }

        [Test]
        public void Register_ConcurrentWrites_PreservesAllInstances()
        {
            var file = Path.Combine(Path.GetTempPath(), "yoji-instances-" + Path.GetRandomFileName() + ".json");
            var registry = new ServiceInstanceRegistry(file);
            var tasks = new Task[8];
            for (var i = 0; i < tasks.Length; i++)
            {
                var index = i;
                tasks[i] = Task.Run(() =>
                {
                    var record = ServiceInstanceRecord.Create(
                        "test-runner-mcp",
                        "TestRunnerMCP",
                        "G:/A",
                        "p",
                        100 + index,
                        21900 + index,
                        "project-auto");
                    registry.Register(record);
                });
            }

            Task.WaitAll(tasks);

            Assert.AreEqual(tasks.Length, registry.Load().Count);
        }
    }
}
