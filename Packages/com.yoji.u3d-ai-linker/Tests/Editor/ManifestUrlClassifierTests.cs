using NUnit.Framework;
using Yoji.U3DAILinker.Operations;

namespace Yoji.U3DAILinker.Tests
{
    public sealed class ManifestUrlClassifierTests
    {
        private const string RepoGit =
            "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/com.yoji.editor-debug#editor-debug-v1.2.0";

        [Test]
        public void RepoGitUrl_OnYojiPackage_IsGitRepo()
        {
            Assert.AreEqual(DependencyOwnership.GitRepo,
                ManifestUrlClassifier.Classify("com.yoji.editor-debug", RepoGit));
        }

        [Test]
        public void RepoGitUrl_WithCaseInsensitiveHostAndSlug_IsGitRepo()
        {
            var url = "https://github.com/SputnicYoji/U3D-Dev-Tools-AI.git?path=/Packages/com.yoji.editor-debug#x";
            Assert.AreEqual(DependencyOwnership.GitRepo,
                ManifestUrlClassifier.Classify("com.yoji.editor-debug", url));
        }

        [Test]
        public void FileUrl_OnYojiPackage_IsLocalFile()
        {
            var url = "file:E:/Yoji/U3D-Dev-Tools-AI/Packages/com.yoji.editor-debug";
            Assert.AreEqual(DependencyOwnership.LocalFile,
                ManifestUrlClassifier.Classify("com.yoji.editor-debug", url));
        }

        [Test]
        public void NullOrEmptyValue_IsAbsent()
        {
            Assert.AreEqual(DependencyOwnership.Absent,
                ManifestUrlClassifier.Classify("com.yoji.editor-debug", null));
            Assert.AreEqual(DependencyOwnership.Absent,
                ManifestUrlClassifier.Classify("com.yoji.editor-debug", "   "));
        }

        [Test]
        public void SemverVersion_IsUnmanaged()
        {
            Assert.AreEqual(DependencyOwnership.Unmanaged,
                ManifestUrlClassifier.Classify("com.yoji.editor-debug", "1.2.0"));
        }

        [Test]
        public void ForeignGitUrl_IsUnmanaged()
        {
            var url = "https://github.com/someoneelse/other-repo.git?path=/Packages/com.yoji.editor-debug#v1";
            Assert.AreEqual(DependencyOwnership.Unmanaged,
                ManifestUrlClassifier.Classify("com.yoji.editor-debug", url));
        }

        [Test]
        public void NonYojiPackageName_IsUnmanaged_EvenWithRepoUrl()
        {
            Assert.AreEqual(DependencyOwnership.Unmanaged,
                ManifestUrlClassifier.Classify("com.thirdparty.thing", RepoGit));
        }
    }
}
