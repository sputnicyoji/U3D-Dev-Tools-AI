using System;
using System.IO;
using NUnit.Framework;

namespace Yoji.TestRunner.Tests
{
    public class JobStoreTests
    {
        private string m_Dir;
        private long m_Now;

        [SetUp] public void SetUp()
        {
            m_Dir = Path.Combine(Path.GetTempPath(), "trmcp_" + Guid.NewGuid().ToString("N"));
            m_Now = 1_000_000;
        }

        [TearDown] public void TearDown()
        {
            try { if (Directory.Exists(m_Dir)) Directory.Delete(m_Dir, true); } catch { }
        }

        private JobStore New() => new JobStore(m_Dir, () => m_Now);

        [Test] public void NewJobId_Is32HexChars()
        {
            var id = JobStore.NewJobId();
            Assert.AreEqual(32, id.Length);
            StringAssert.IsMatch("^[0-9a-f]{32}$", id);
        }

        [Test] public void Fresh_IsIdle()
        {
            Assert.IsTrue(New().IsIdle);
            Assert.AreEqual(ServiceState.Idle, New().State);
        }

        [Test] public void StartJob_TransitionsToRunning()
        {
            var s = New();
            var rec = s.StartJob(JobStore.NewJobId());
            Assert.AreEqual("running", rec.Status);
            Assert.AreEqual(ServiceState.Running, s.State);
            Assert.IsFalse(s.IsIdle);
        }

        [Test] public void StartJob_WhenBusy_Throws()
        {
            var s = New();
            s.StartJob(JobStore.NewJobId());
            Assert.Throws<InvalidOperationException>(() => s.StartJob(JobStore.NewJobId()));
        }

        [Test] public void CompleteJob_TransitionsToIdle_AndCaches()
        {
            var s = New();
            var id = JobStore.NewJobId();
            s.StartJob(id);
            s.CompleteJob(id, 2, 1, 0, "Failed", "C:/x/result.xml");
            Assert.IsTrue(s.IsIdle);
            var found = s.Find(id);
            Assert.AreEqual("completed", found.Status);
            Assert.AreEqual(2, found.Passed);
            Assert.AreEqual(1, found.Failed);
            Assert.AreEqual("Failed", found.OverallResult);
        }

        [Test] public void SetCompiling_ReflectsState()
        {
            var s = New();
            s.SetCompiling(true);
            Assert.AreEqual(ServiceState.Compiling, s.State);
            s.SetCompiling(false);
            Assert.AreEqual(ServiceState.Idle, s.State);
        }

        [Test] public void Find_Unknown_ReturnsNull()
            => Assert.IsNull(New().Find("deadbeef"));

        [Test] public void Persistence_RoundTrip_AcrossInstances()
        {
            var id = JobStore.NewJobId();
            var s1 = New();
            s1.StartJob(id);
            s1.CompleteJob(id, 5, 0, 1, "Passed", "C:/r.xml");
            var s2 = New(); // 新实例同目录，模拟域重载后重建
            var found = s2.Find(id);
            Assert.IsNotNull(found);
            Assert.AreEqual("completed", found.Status);
            Assert.AreEqual(5, found.Passed);
            Assert.AreEqual(1, found.Skipped);
            Assert.AreEqual("Passed", found.OverallResult);
        }

        [Test] public void ActiveOrLast_PrefersActive()
        {
            var s = New();
            var a = JobStore.NewJobId(); s.StartJob(a); s.CompleteJob(a, 1, 0, 0, "Passed", "x");
            var b = JobStore.NewJobId(); s.StartJob(b);
            Assert.AreEqual(b, s.ActiveOrLast().JobId);
        }

        [Test] public void SweepStale_OrphansRunningJob()
        {
            var s = New();
            var id = JobStore.NewJobId();
            s.StartJob(id);
            m_Now += 60_000; // 推进时钟 60s
            s.SweepStale(30_000);
            Assert.IsTrue(s.IsIdle);
            Assert.AreEqual("error", s.Find(id).Status);
        }
    }
}
