using System.Collections.Generic;
using NUnit.Framework;
using Yoji.U3DAILinker.Operations;

namespace Yoji.U3DAILinker.Tests.Operations
{
    public class RecoveryReconcilerTests
    {
        private static OperationLog Log(string phase)
        {
            return new OperationLog
            {
                OperationId = "op-1",
                Phase = phase,
                CurrentIndex = 0,
                DependencyChanges = new List<DependencyChange>
                {
                    new DependencyChange { PackageName = "com.yoji.editor-core", NewValue = "url-core" },
                },
                Completed = new List<string>(),
                RetryCount = 0,
            };
        }

        [Test] public void ShouldResume_NullLog_False()
            => Assert.IsFalse(RecoveryReconciler.ShouldResume(null));

        [Test] public void ShouldResume_PackageRequested_True()
            => Assert.IsTrue(RecoveryReconciler.ShouldResume(Log(OperationPhase.PackageRequested)));

        [Test] public void ShouldResume_Pending_True()
            => Assert.IsTrue(RecoveryReconciler.ShouldResume(Log(OperationPhase.Pending)));

        [Test] public void ShouldResume_Completed_False()
            => Assert.IsFalse(RecoveryReconciler.ShouldResume(Log(OperationPhase.Completed)));

        [Test] public void ShouldResume_Failed_False()
            => Assert.IsFalse(RecoveryReconciler.ShouldResume(Log(OperationPhase.Failed)));

        [Test] public void ShouldResume_EmptyDependencyChanges_False()
        {
            var log = Log(OperationPhase.PackageRequested);
            log.DependencyChanges = new List<DependencyChange>();
            Assert.IsFalse(RecoveryReconciler.ShouldResume(log));
        }

        [Test] public void ShouldResume_IndexOutOfRange_False()
        {
            var log = Log(OperationPhase.PackageRequested);
            log.CurrentIndex = 5; // 越界（不应恢复，交由完成/失败处理）
            Assert.IsFalse(RecoveryReconciler.ShouldResume(log));
        }

        [Test] public void ShouldScheduleFollowUp_Requested_False()
            => Assert.IsFalse(RecoveryReconciler.ShouldScheduleFollowUp(QueueStepResult.Requested));

        [Test] public void ShouldScheduleFollowUp_Faulted_False()
            => Assert.IsFalse(RecoveryReconciler.ShouldScheduleFollowUp(QueueStepResult.Faulted));

        [Test] public void ShouldScheduleFollowUp_AlreadySatisfied_False()
            => Assert.IsFalse(RecoveryReconciler.ShouldScheduleFollowUp(QueueStepResult.AlreadySatisfied));
    }
}
