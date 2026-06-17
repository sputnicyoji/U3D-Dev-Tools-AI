using System;
using System.IO;
using System.Reflection;
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

        [Test]
        public void Unity2022Adapter_DoesNotSilentlySwallowAssemblyLoadFailure()
        {
            var packagePath = PackageInfo.FindForAssembly(typeof(TestRunnerMCP).Assembly).resolvedPath;
            var source = File.ReadAllText(Path.Combine(packagePath, "Editor", "UnityTestRunnerApiAdapter.cs"));

            Assert.False(source.Contains("catch (Exception) { }"));
            StringAssert.Contains("loadException", source);
        }

        [Test]
        public void ExecutionFailureDescription_UsesInnerExceptionForReflectionInvoke()
        {
            var serviceType = typeof(TestRunnerMCP).Assembly.GetType("Yoji.TestRunner.TestRunService");
            Assert.NotNull(serviceType, "TestRunService type should exist");

            var method = serviceType.GetMethod(
                "DescribeExecutionFailure",
                BindingFlags.Static | BindingFlags.NonPublic);
            var inner = new InvalidOperationException("inner execute failure");
            var wrapped = new TargetInvocationException(inner);

            Assert.NotNull(method, "DescribeExecutionFailure should exist for reflection invoke failures");

            var message = (string)method.Invoke(null, new object[] { wrapped });

            StringAssert.Contains("InvalidOperationException", message);
            StringAssert.Contains("inner execute failure", message);
            Assert.False(message.Contains("Exception has been thrown by the target of an invocation"));
        }
    }
}
