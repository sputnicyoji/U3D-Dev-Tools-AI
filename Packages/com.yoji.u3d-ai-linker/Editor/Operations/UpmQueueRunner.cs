namespace Yoji.U3DAILinker.Operations
{
    /// Advance 的单步结果。
    internal enum QueueStepResult
    {
        Requested,        // 发出了一次 Add（当前项或推进后的下一项），等待下次回来核对
        AlreadySatisfied, // 整队已完成（无需再 Add）
        Faulted,          // UPM 报错或超重试上限，队列停在失败态
    }

    /// 串行 UPM 安装队列推进器。单步状态机，被首次发起与域重载恢复共用。
    /// 不变式：每发一次 Add 之前，必先把 phase=package-requested 与 currentIndex 原子落盘，
    /// 保证域重载后 Bootstrap 能从日志接续。恢复核对以 IInstalledPackageProbe 的实际结果为准，
    /// 日志只表达意图（spec 449-454）。
    internal sealed class UpmQueueRunner
    {
        public const int MaxRetries = 2;

        private readonly OperationLogStore m_Store;
        private readonly IUpmClient m_Upm;
        private readonly IInstalledPackageProbe m_Probe;

        public UpmQueueRunner(OperationLogStore store, IUpmClient upm, IInstalledPackageProbe probe)
        {
            m_Store = store;
            m_Upm = upm;
            m_Probe = probe;
        }

        /// 推进一步。修改传入 log（就地）并落盘。语义：
        /// - completed/failed：直接返回，不再动队列。
        /// - pending：发当前项 Add，转 package-requested。
        /// - package-requested：核对当前项是否已安装。
        ///     达成 -> 计入 Completed、index++、retry 归零；
        ///       若是最后一项则整队 completed、清日志、返回 AlreadySatisfied；
        ///       否则发下一项 Add、返回 Requested。
        ///     未达成 -> retry++；超上限标 failed 返回 Faulted；否则重发当前项、返回 Requested。
        public QueueStepResult Advance(OperationLog log)
        {
            if (log.Phase == OperationPhase.Completed) return QueueStepResult.AlreadySatisfied;
            if (log.Phase == OperationPhase.Failed) return QueueStepResult.Faulted;

            if (log.Phase == OperationPhase.Pending)
                return RequestCurrent(log);

            // package-requested：核对当前项
            var current = CurrentChange(log);
            var installed = m_Probe.GetInstalledUrl(current.PackageName);
            if (installed == current.NewValue)
            {
                if (!log.Completed.Contains(current.PackageName))
                    log.Completed.Add(current.PackageName);
                log.RetryCount = 0;
                log.CurrentIndex++;

                if (log.CurrentIndex >= log.DependencyChanges.Count)
                {
                    log.Phase = OperationPhase.Completed;
                    m_Store.Clear();
                    return QueueStepResult.AlreadySatisfied;
                }
                return RequestCurrent(log); // 发下一项
            }

            // 未达成：重试或失败
            if (log.RetryCount >= MaxRetries)
            {
                log.Phase = OperationPhase.Failed;
                m_Store.Save(log);
                return QueueStepResult.Faulted;
            }
            log.RetryCount++;
            return RequestCurrent(log); // 重发当前项
        }

        // 先原子落盘 package-requested + 当前 revision，再 Add；Add 报错则转 failed。
        private QueueStepResult RequestCurrent(OperationLog log)
        {
            var change = CurrentChange(log);
            log.Phase = OperationPhase.PackageRequested;
            log.ResolvedRevision = change.NewValue;
            m_Store.Save(log); // 不变式：落盘在 Add 之前

            var handle = m_Upm.Add(change.NewValue);
            if (handle.IsError)
            {
                log.Phase = OperationPhase.Failed;
                m_Store.Save(log);
                return QueueStepResult.Faulted;
            }
            return QueueStepResult.Requested;
        }

        private static DependencyChange CurrentChange(OperationLog log)
            => log.DependencyChanges[log.CurrentIndex];
    }
}
