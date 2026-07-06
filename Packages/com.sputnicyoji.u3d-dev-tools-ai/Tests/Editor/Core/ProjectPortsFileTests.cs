using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using Yoji.EditorCore.Ports;

namespace Yoji.EditorCore.Tests
{
    public sealed class ProjectPortsFileTests
    {
        [Test]
        public void Upsert_WritesMultipleInstances_WithLowerCamelJsonNames()
        {
            var root = Path.Combine(Path.GetTempPath(), "yoji-ports-" + Path.GetRandomFileName());
            var a = ServiceInstanceRecord.Create("test-runner-mcp", "TestRunnerMCP", root, "p", 100, 21900, "project-auto");
            var b = ServiceInstanceRecord.Create("unity-editor-debug-mcp", "EditorDebugMCP", root, "p", 100, 21901, "project-auto");

            ProjectPortsFile.Upsert(root, a);
            ProjectPortsFile.Upsert(root, b);

            var path = Path.Combine(root, ".u3d-ai-linker", "ports.json");
            var json = File.ReadAllText(path);

            StringAssert.Contains("\"updatedUtc\"", json);
            StringAssert.Contains("\"schemaVersion\"", json);
            StringAssert.Contains("\"projectRoot\"", json);
            StringAssert.Contains("\"projectId\"", json);
            StringAssert.Contains("\"instances\"", json);
            StringAssert.Contains("\"serviceId\"", json);
            StringAssert.Contains("\"instanceId\"", json);
            StringAssert.DoesNotContain("ServiceId", json);
            StringAssert.DoesNotContain("InstanceId", json);

            var loaded = ProjectPortsFile.Load(root);
            Assert.AreEqual(2, loaded.Count);
        }

        // 与 ServiceInstanceRegistry 同语义的 stale 自愈: 死 pid 行与同 serviceId+pid 前身行在带谓词 Upsert 时清除.
        [Test]
        public void Upsert_WithLiveness_PrunesDeadAndSupersedesSameServicePid()
        {
            var root = Path.Combine(Path.GetTempPath(), "yoji-ports-" + Path.GetRandomFileName());
            var dead = ServiceInstanceRecord.Create("test-runner-mcp", "TestRunnerMCP", root, "p", 111, 21900, "project-auto");
            var alivePeer = ServiceInstanceRecord.Create("unity-lua-device-debug", "LuaDeviceDebug", root, "p", 222, 21894, "project-auto");
            var predecessor = ServiceInstanceRecord.Create("unity-editor-debug-mcp", "EditorDebugMCP", root, "p", 333, 21891, "project-auto");
            ProjectPortsFile.Upsert(root, dead);
            ProjectPortsFile.Upsert(root, alivePeer);
            ProjectPortsFile.Upsert(root, predecessor);

            var reborn = ServiceInstanceRecord.Create("unity-editor-debug-mcp", "EditorDebugMCP", root, "p", 333, 21892, "project-auto");
            ProjectPortsFile.Upsert(root, reborn, pid => pid != 111);

            var loaded = ProjectPortsFile.Load(root);
            Assert.AreEqual(2, loaded.Count);
            foreach (var record in loaded)
            {
                Assert.AreNotEqual(111, record.ProcessId, "dead pid row must be pruned");
                if (record.ProcessId == 333)
                    Assert.AreEqual(reborn.InstanceId, record.InstanceId, "same service+pid predecessor must be superseded");
            }
        }

        [Test]
        public void Remove_DeletesOneInstanceAndKeepsOthers()
        {
            var root = Path.Combine(Path.GetTempPath(), "yoji-ports-" + Path.GetRandomFileName());
            var a = ServiceInstanceRecord.Create("test-runner-mcp", "TestRunnerMCP", root, "p", 100, 21900, "project-auto");
            var b = ServiceInstanceRecord.Create("unity-editor-debug-mcp", "EditorDebugMCP", root, "p", 100, 21901, "project-auto");

            ProjectPortsFile.Upsert(root, a);
            ProjectPortsFile.Upsert(root, b);

            Assert.IsTrue(ProjectPortsFile.Remove(root, a.InstanceId));

            var loaded = ProjectPortsFile.Load(root);
            Assert.AreEqual(1, loaded.Count);
            Assert.AreEqual(b.InstanceId, loaded[0].InstanceId);
        }

        [Test]
        public void Upsert_EmptyRecordProjectId_BackfillsStableProjectId()
        {
            var root = Path.Combine(Path.GetTempPath(), "yoji-ports-" + Path.GetRandomFileName());
            var record = ServiceInstanceRecord.Create("test-runner-mcp", "TestRunnerMCP", root, "", 100, 21900, "project-auto");

            ProjectPortsFile.Upsert(root, record);

            var path = Path.Combine(root, ".u3d-ai-linker", "ports.json");
            var compactJson = File.ReadAllText(path).Replace(" ", "").Replace("\r", "").Replace("\n", "");
            StringAssert.DoesNotContain("\"projectId\":\"\"", compactJson);
            Assert.IsNotEmpty(ProjectPortsFile.Load(root)[0].ProjectId);

            Assert.IsTrue(ProjectPortsFile.Remove(root, record.InstanceId));

            compactJson = File.ReadAllText(path).Replace(" ", "").Replace("\r", "").Replace("\n", "");
            StringAssert.DoesNotContain("\"projectId\":\"\"", compactJson);
        }

        [Test]
        public void Load_MalformedJson_ReturnsEmpty()
        {
            var root = Path.Combine(Path.GetTempPath(), "yoji-ports-" + Path.GetRandomFileName());
            var path = Path.Combine(root, ".u3d-ai-linker", "ports.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "{not json");

            Assert.DoesNotThrow(() =>
            {
                var loaded = ProjectPortsFile.Load(root);
                Assert.AreEqual(0, loaded.Count);
            });
        }

        [Test]
        public void Upsert_ConcurrentWrites_PreservesAllInstances()
        {
            var root = Path.Combine(Path.GetTempPath(), "yoji-ports-" + Path.GetRandomFileName());
            var tasks = new Task[8];
            for (var i = 0; i < tasks.Length; i++)
            {
                var index = i;
                tasks[i] = Task.Run(() =>
                {
                    var record = ServiceInstanceRecord.Create(
                        "test-runner-mcp",
                        "TestRunnerMCP",
                        root,
                        "p",
                        100 + index,
                        21900 + index,
                        "project-auto");
                    ProjectPortsFile.Upsert(root, record);
                });
            }

            Task.WaitAll(tasks);

            Assert.AreEqual(tasks.Length, ProjectPortsFile.Load(root).Count);
        }
    }
}
