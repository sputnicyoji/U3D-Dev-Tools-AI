using NUnit.Framework;

namespace Yoji.EditorCore.Tests
{
    public sealed class EditorServiceLifecycleTests
    {
        [TestCase(true, false)]
        [TestCase(false, true)]
        public void ShouldBind_SkipsAssetImportWorker(bool isAssetImportWorkerProcess, bool expected)
        {
            Assert.AreEqual(expected, EditorServiceLifecycle.ShouldBind(isAssetImportWorkerProcess));
        }
    }
}
