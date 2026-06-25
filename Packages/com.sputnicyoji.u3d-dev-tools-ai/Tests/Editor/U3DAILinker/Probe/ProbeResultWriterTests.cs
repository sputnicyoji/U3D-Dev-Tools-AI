using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using Yoji.U3DAILinker.Probe;

namespace Yoji.U3DAILinker.Tests.Probe
{
    public sealed class ProbeResultWriterTests
    {
        private string _tempRoot;

        [SetUp]
        public void SetUp()
        {
            _tempRoot = Path.Combine(
                Path.GetTempPath(),
                "U3DAILinkerProbeTest_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }

        [Test]
        public void ResultPathFor_BuildsLibraryU3DAILinkerPath()
        {
            string path = ProbeResultWriter.ResultPathFor(_tempRoot);

            string expected = Path.Combine(_tempRoot, "Library", "U3DAILinker", "probe-result.json");
            Assert.AreEqual(Path.GetFullPath(expected), Path.GetFullPath(path));
        }

        [Test]
        public void Write_CreatesFileWithExpectedJsonFields()
        {
            var result = ProbeEvaluator.Evaluate(new List<ProbeTarget>
            {
                new ProbeTarget("editor-debug.SKILL.md", "/x/Agent~/skills/a/SKILL.md", "File", true),
                new ProbeTarget("linker.BundledSkills~", "/y/BundledSkills~", "Directory", false),
            });

            string path = ProbeResultWriter.Write(_tempRoot, result);

            Assert.IsTrue(File.Exists(path));
            string json = File.ReadAllText(path);
            JObject root = JObject.Parse(json);

            Assert.AreEqual(1, (int)root["schemaVersion"]);
            Assert.AreEqual(false, (bool)root["allTargetsReadable"]);
            Assert.AreEqual("zip-fallback", (string)root["recommendedMode"]);
            Assert.IsNotNull(root["probedAtUtc"]);

            JArray targets = (JArray)root["targets"];
            Assert.AreEqual(2, targets.Count);
            Assert.AreEqual("editor-debug.SKILL.md", (string)targets[0]["id"]);
            Assert.AreEqual(true, (bool)targets[0]["exists"]);
            Assert.AreEqual("File", (string)targets[0]["kind"]);
            Assert.AreEqual(false, (bool)targets[1]["exists"]);
        }

        [Test]
        public void Write_CreatesMissingDirectories()
        {
            // _tempRoot 下尚无 Library/U3DAILinker,Write 必须自建。
            var result = ProbeEvaluator.Evaluate(new List<ProbeTarget>
            {
                new ProbeTarget("only", "/z", "Directory", true),
            });

            string path = ProbeResultWriter.Write(_tempRoot, result);

            Assert.IsTrue(File.Exists(path));
            Assert.IsTrue(Directory.Exists(Path.Combine(_tempRoot, "Library", "U3DAILinker")));
        }

        [Test]
        public void Write_IsIdempotent_OverwritesExisting()
        {
            var first = ProbeEvaluator.Evaluate(new List<ProbeTarget>
            {
                new ProbeTarget("a", "/a", "File", true),
            });
            string path = ProbeResultWriter.Write(_tempRoot, first);

            var second = ProbeEvaluator.Evaluate(new List<ProbeTarget>
            {
                new ProbeTarget("a", "/a", "File", false),
            });
            string path2 = ProbeResultWriter.Write(_tempRoot, second);

            Assert.AreEqual(Path.GetFullPath(path), Path.GetFullPath(path2));
            JObject root = JObject.Parse(File.ReadAllText(path2));
            Assert.AreEqual("zip-fallback", (string)root["recommendedMode"]);
        }
    }
}
