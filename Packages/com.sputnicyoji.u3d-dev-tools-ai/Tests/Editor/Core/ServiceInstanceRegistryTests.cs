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
