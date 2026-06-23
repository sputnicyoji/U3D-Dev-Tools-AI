using NUnit.Framework;
using Yoji.U3DAILinker.Registry;

namespace Yoji.U3DAILinker.Registry.Tests
{
    public class ToolUrlBuilderTests
    {
        private static RegistryEntry Entry(string id, string packageName, string packagePath, string revision)
        {
            return new RegistryEntry
            {
                Id = id,
                Status = "ready",
                Kind = "tool",
                Order = 20,
                PackageName = packageName,
                PackagePath = packagePath,
                Revision = revision,
                DefaultEnabled = true,
                UserToggle = true,
                AgentAssets = "Agent~",
                MinUnity = "2022.3",
                DependsOn = new string[0]
            };
        }

        [Test] public void BuildStable_UsesTagRevision()
        {
            var e = Entry("editor-debug", "com.yoji.editor-debug", "Packages/com.yoji.editor-debug", "editor-debug-v1.2.0");
            var url = ToolUrlBuilder.BuildStable(e.PackagePath, e.Revision);
            Assert.AreEqual(
                "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/com.yoji.editor-debug#editor-debug-v1.2.0",
                url);
        }

        [Test] public void BuildDevSha_UsesShaRevision()
        {
            var e = Entry("editor-debug", "com.yoji.editor-debug", "Packages/com.yoji.editor-debug",
                "0123456789abcdef0123456789abcdef01234567");
            var url = ToolUrlBuilder.BuildDevSha(e.PackagePath, e.Revision);
            Assert.AreEqual(
                "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/com.yoji.editor-debug#0123456789abcdef0123456789abcdef01234567",
                url);
        }

        [Test] public void BuildLocalFile_UsesAbsoluteProjectRoot()
        {
            var e = Entry("editor-debug", "com.yoji.editor-debug", "Packages/com.yoji.editor-debug", "editor-debug-v1.2.0");
            var url = ToolUrlBuilder.BuildLocalFile("C:/Example/U3D-Dev-Tools-AI", e.PackagePath);
            Assert.AreEqual("file:C:/Example/U3D-Dev-Tools-AI/Packages/com.yoji.editor-debug", url);
        }

        [Test] public void BuildLocalFile_NormalizesBackslashesAndTrailingSlash()
        {
            var e = Entry("editor-debug", "com.yoji.editor-debug", "Packages/com.yoji.editor-debug", "editor-debug-v1.2.0");
            var url = ToolUrlBuilder.BuildLocalFile(@"C:\Example\U3D-Dev-Tools-AI\", e.PackagePath);
            Assert.AreEqual("file:C:/Example/U3D-Dev-Tools-AI/Packages/com.yoji.editor-debug", url);
        }

        [Test] public void BuildStable_UsesPackagePathFromEntry()
        {
            var e = Entry("test-runner", "com.yoji.test-runner", "Packages/com.yoji.test-runner", "test-runner-v1.1.0");
            var url = ToolUrlBuilder.BuildStable(e.PackagePath, e.Revision);
            StringAssert.Contains("?path=/Packages/com.yoji.test-runner#", url);
        }
    }
}
