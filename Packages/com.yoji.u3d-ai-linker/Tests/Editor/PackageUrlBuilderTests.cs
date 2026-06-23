using NUnit.Framework;
using Yoji.U3DAILinker.Registry;

namespace Yoji.U3DAILinker.Tests
{
    public sealed class PackageUrlBuilderTests
    {
        private const string Path = "Packages/com.yoji.editor-debug";

        [Test]
        public void BuildStable_ProducesTagUrl()
        {
            var url = ToolUrlBuilder.BuildStable(Path, "editor-debug-v1.2.0");
            Assert.AreEqual(
                "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/com.yoji.editor-debug#editor-debug-v1.2.0",
                url);
        }

        [Test]
        public void BuildDevSha_ProducesShaUrl()
        {
            var sha = "0123456789abcdef0123456789abcdef01234567";
            var url = ToolUrlBuilder.BuildDevSha(Path, sha);
            Assert.AreEqual(
                "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/com.yoji.editor-debug#" + sha,
                url);
        }

        [Test]
        public void BuildLocalFile_NormalizesBackslashesToForwardSlashes()
        {
            var url = ToolUrlBuilder.BuildLocalFile(@"C:\Example\U3D-Dev-Tools-AI", Path);
            Assert.AreEqual("file:C:/Example/U3D-Dev-Tools-AI/Packages/com.yoji.editor-debug", url);
        }

        [Test]
        public void BuildLocalFile_TrimsTrailingSlashOnRoot()
        {
            var url = ToolUrlBuilder.BuildLocalFile("C:/Example/U3D-Dev-Tools-AI/", Path);
            Assert.AreEqual("file:C:/Example/U3D-Dev-Tools-AI/Packages/com.yoji.editor-debug", url);
        }

        [Test]
        public void IsManagedUrl_TrueForRepoGitUrl()
        {
            Assert.IsTrue(ToolUrlBuilder.IsManagedUrl(
                "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/com.yoji.editor-debug#editor-debug-v1.2.0"));
        }

        [Test]
        public void IsManagedUrl_TrueForFileScheme()
        {
            Assert.IsTrue(ToolUrlBuilder.IsManagedUrl("file:C:/Example/U3D-Dev-Tools-AI/Packages/com.yoji.editor-debug"));
        }

        [Test]
        public void IsManagedUrl_FalseForThirdPartyRegistryVersion()
        {
            Assert.IsFalse(ToolUrlBuilder.IsManagedUrl("3.2.1"));
        }

        [Test]
        public void IsManagedUrl_FalseForForeignGitRepo()
        {
            Assert.IsFalse(ToolUrlBuilder.IsManagedUrl(
                "https://github.com/someone-else/other-repo.git?path=/Packages/x#v1"));
        }

        [Test]
        public void IsManagedUrl_FalseForNull()
        {
            Assert.IsFalse(ToolUrlBuilder.IsManagedUrl(null));
        }
    }
}
