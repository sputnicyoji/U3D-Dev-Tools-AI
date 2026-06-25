using NUnit.Framework;
using Yoji.U3DAILinker.Operations;

namespace Yoji.U3DAILinker.Tests
{
    public sealed class ManifestUrlClassifierTests
    {
        private const string RepoGit =
            "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/com.sputnicyoji.u3d-dev-tools-ai#editor-debug-v1.2.0";

        [Test]
        public void RepoGitUrl_OnYojiPackage_IsGitRepo()
        {
            Assert.AreEqual(DependencyOwnership.GitRepo,
                ManifestUrlClassifier.Classify("com.sputnicyoji.u3d-dev-tools-ai", RepoGit));
        }

        [Test]
        public void RepoGitUrl_WithCaseInsensitiveHostAndSlug_IsGitRepo()
        {
            var url = "https://github.com/SputnicYoji/U3D-Dev-Tools-AI.git?path=/Packages/com.sputnicyoji.u3d-dev-tools-ai#x";
            Assert.AreEqual(DependencyOwnership.GitRepo,
                ManifestUrlClassifier.Classify("com.sputnicyoji.u3d-dev-tools-ai", url));
        }

        [Test]
        public void FileUrl_OnYojiPackage_IsLocalFile()
        {
            var url = "file:C:/Example/U3D-Dev-Tools-AI/Packages/com.sputnicyoji.u3d-dev-tools-ai";
            Assert.AreEqual(DependencyOwnership.LocalFile,
                ManifestUrlClassifier.Classify("com.sputnicyoji.u3d-dev-tools-ai", url));
        }

        [Test]
        public void NullOrEmptyValue_IsAbsent()
        {
            Assert.AreEqual(DependencyOwnership.Absent,
                ManifestUrlClassifier.Classify("com.sputnicyoji.u3d-dev-tools-ai", null));
            Assert.AreEqual(DependencyOwnership.Absent,
                ManifestUrlClassifier.Classify("com.sputnicyoji.u3d-dev-tools-ai", "   "));
        }

        [Test]
        public void SemverVersion_IsUnmanaged()
        {
            Assert.AreEqual(DependencyOwnership.Unmanaged,
                ManifestUrlClassifier.Classify("com.sputnicyoji.u3d-dev-tools-ai", "1.2.0"));
        }

        [Test]
        public void ForeignGitUrl_IsUnmanaged()
        {
            var url = "https://github.com/someoneelse/other-repo.git?path=/Packages/com.sputnicyoji.u3d-dev-tools-ai#v1";
            Assert.AreEqual(DependencyOwnership.Unmanaged,
                ManifestUrlClassifier.Classify("com.sputnicyoji.u3d-dev-tools-ai", url));
        }

        [Test]
        public void NonYojiPackageName_IsUnmanaged_EvenWithRepoUrl()
        {
            Assert.AreEqual(DependencyOwnership.Unmanaged,
                ManifestUrlClassifier.Classify("com.thirdparty.thing", RepoGit));
        }
    }
}
