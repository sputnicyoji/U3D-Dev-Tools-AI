using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor.PackageManager;

namespace Yoji.TestRunner.Tests
{
    public class TestRunnerCompatibilityTests
    {
        [Test]
        public void Unity2022Path_DoesNotDirectlyUseUnityTestRunnerApiTypes()
        {
            var packagePath = PackageInfo.FindForAssembly(typeof(TestRunnerMCP).Assembly).resolvedPath;

            var editorDir = Path.Combine(packagePath, "Editor");
            foreach (var path in Directory.GetFiles(editorDir, "*.cs", SearchOption.AllDirectories))
            {
                if (Path.GetFileName(path) == "UnityTestRunnerApiAdapter.Unity6000.cs")
                    continue;

                var source = File.ReadAllText(path);
                Assert.False(
                    source.Contains("using UnityEditor.TestTools.TestRunner.Api;"),
                    "Unity 2022 path directly uses Unity TestRunner API in " + Path.GetFileName(path));
            }
        }

        [Test]
        public void PackageDependency_UsesUnity2022ResolvableTestFrameworkVersion()
        {
            var packagePath = PackageInfo.FindForAssembly(typeof(TestRunnerMCP).Assembly).resolvedPath;
            var packageJson = JObject.Parse(File.ReadAllText(Path.Combine(packagePath, "package.json")));
            var version = (string)packageJson["dependencies"]["com.unity.test-framework"];

            Assert.AreEqual("1.1.33", version);
        }
    }
}
