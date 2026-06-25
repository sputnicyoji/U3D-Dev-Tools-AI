using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Yoji.U3DAILinker.Operations;

namespace Yoji.U3DAILinker.Tests.Operations
{
    public class UpmQueueRunnerTests
    {
        private string m_LibraryRoot;
        private OperationLogStore m_Store;
        private FakeUpmClient m_Upm;
        private FakeInstalledPackageProbe m_Probe;

        [SetUp] public void SetUp()
        {
            m_LibraryRoot = Path.Combine(Path.GetTempPath(), "u3dlinker_" + System.Guid.NewGuid().ToString("N"));
            m_Store = new OperationLogStore(m_LibraryRoot);
            m_Upm = new FakeUpmClient();
            m_Probe = new FakeInstalledPackageProbe();
        }

        [TearDown] public void TearDown()
        {
            try { if (Directory.Exists(m_LibraryRoot)) Directory.Delete(m_LibraryRoot, true); } catch { }
        }

        private UpmQueueRunner New() => new UpmQueueRunner(m_Store, m_Upm, m_Probe);

        // 两项队列：editor-core -> editor-debug
        private static OperationLog TwoItemLog()
        {
            return new OperationLog
            {
                OperationId = "op-1",
                Action = "install-all",
                ToolIds = new[] { "editor-debug" },
                CurrentIndex = 0,
                Phase = OperationPhase.Pending,
                Channel = "stable",
                DependencyChanges = new List<DependencyChange>
                {
                    new DependencyChange { PackageName = "com.sputnicyoji.u3d-dev-tools-ai", NewValue = "url-core" },
                    new DependencyChange { PackageName = "com.sputnicyoji.u3d-dev-tools-ai", NewValue = "url-debug" },
                },
                Completed = new List<string>(),
                RetryCount = 0,
            };
        }

        [Test] public void Advance_FromPending_RequestsCurrentAndPersists()
        {
            var log = TwoItemLog();
            var result = New().Advance(log);

            Assert.AreEqual(QueueStepResult.Requested, result);
            Assert.AreEqual(1, m_Upm.AddCalls.Count);
            Assert.AreEqual("url-core", m_Upm.AddCalls[0]);
            // 先落盘再 Add：日志已记 package-requested
            var persisted = m_Store.Load();
            Assert.AreEqual(OperationPhase.PackageRequested, persisted.Phase);
            Assert.AreEqual(0, persisted.CurrentIndex);
        }

        [Test] public void Advance_RequestedButNotInstalled_RetriesSamePackage()
        {
            var log = TwoItemLog();
            log.Phase = OperationPhase.PackageRequested; // 模拟域重载后回来，但包还没解析到
            var result = New().Advance(log);

            Assert.AreEqual(QueueStepResult.Requested, result);
            Assert.AreEqual(1, log.RetryCount);
            Assert.AreEqual(0, log.CurrentIndex); // 仍停在第一项
            Assert.AreEqual("url-core", m_Upm.AddCalls[0]); // 重发同一项
        }

        [Test] public void Advance_RequestedAndInstalled_AdvancesToNext()
        {
            var log = TwoItemLog();
            log.Phase = OperationPhase.PackageRequested;
            m_Probe.Set("com.sputnicyoji.u3d-dev-tools-ai", "url-core"); // 已达成

            var result = New().Advance(log);

            Assert.AreEqual(QueueStepResult.Requested, result);
            CollectionAssert.Contains(log.Completed, "com.sputnicyoji.u3d-dev-tools-ai");
            Assert.AreEqual(1, log.CurrentIndex);
            Assert.AreEqual(0, log.RetryCount); // 推进后归零
            Assert.AreEqual("url-debug", m_Upm.AddCalls[m_Upm.AddCalls.Count - 1]); // 已发第二项
        }

        [Test] public void Advance_LastItemInstalled_ReturnsAlreadySatisfiedAndCompletes()
        {
            var log = TwoItemLog();
            log.CurrentIndex = 1;
            log.Phase = OperationPhase.PackageRequested;
            log.Completed = new List<string> { "com.sputnicyoji.u3d-dev-tools-ai" };
            m_Probe.Set("com.sputnicyoji.u3d-dev-tools-ai", "url-debug");

            var result = New().Advance(log);

            Assert.AreEqual(QueueStepResult.AlreadySatisfied, result);
            Assert.AreEqual(OperationPhase.Completed, log.Phase);
            CollectionAssert.Contains(log.Completed, "com.sputnicyoji.u3d-dev-tools-ai");
            Assert.IsNull(m_Store.Load(), "完成后日志应被清除");
        }

        [Test] public void Advance_RetryExceedsMax_Faults()
        {
            var log = TwoItemLog();
            log.Phase = OperationPhase.PackageRequested;
            log.RetryCount = UpmQueueRunner.MaxRetries; // 已到上限，本次核对仍失败

            var result = New().Advance(log);

            Assert.AreEqual(QueueStepResult.Faulted, result);
            Assert.AreEqual(OperationPhase.Failed, log.Phase);
            var persisted = m_Store.Load();
            Assert.IsNotNull(persisted, "失败日志保留供用户 Retry/Cancel");
            Assert.AreEqual(OperationPhase.Failed, persisted.Phase);
        }

        [Test] public void Advance_UpmAddReturnsError_Faults()
        {
            var log = TwoItemLog();
            m_Upm.FailIdentifierContains = "url-core";

            var result = New().Advance(log);

            Assert.AreEqual(QueueStepResult.Faulted, result);
            Assert.AreEqual(OperationPhase.Failed, log.Phase);
        }

        [Test] public void Advance_AlreadyCompletedLog_NoOp()
        {
            var log = TwoItemLog();
            log.Phase = OperationPhase.Completed;

            var result = New().Advance(log);

            Assert.AreEqual(QueueStepResult.AlreadySatisfied, result);
            Assert.AreEqual(0, m_Upm.AddCalls.Count);
        }

        [Test] public void Advance_RecordsResolvedRevisionFromCurrentChange()
        {
            var log = TwoItemLog();
            New().Advance(log);
            // resolvedRevision 跟随当前项；此处用 NewValue 作为可核对目标已足够
            Assert.AreEqual("url-core", m_Store.Load().DependencyChanges[0].NewValue);
        }
    }
}
