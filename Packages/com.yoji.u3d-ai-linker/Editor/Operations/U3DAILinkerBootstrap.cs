using UnityEditor;
using UnityEditor.PackageManager;

namespace Yoji.U3DAILinker.Operations
{
    /// 纯决策：给定日志，是否应在本次域加载后接续队列。
    /// 只在有"进行中"日志（pending / package-requested）且当前 index 合法时恢复；
    /// completed / failed / null / 空队列 / 越界一律不动（交由用户在面板 Retry/Cancel）。
    internal static class RecoveryReconciler
    {
        public static bool ShouldResume(OperationLog log)
        {
            if (log == null) return false;
            if (log.DependencyChanges == null || log.DependencyChanges.Count == 0) return false;
            if (log.CurrentIndex < 0 || log.CurrentIndex >= log.DependencyChanges.Count) return false;
            return log.Phase == OperationPhase.Pending || log.Phase == OperationPhase.PackageRequested;
        }

        public static bool ShouldScheduleFollowUp(QueueStepResult result)
        {
            return false;
        }
    }

    /// 域重载恢复入口。[InitializeOnLoad] 在每次域加载后运行静态构造，
    /// 挂 EditorApplication.delayCall 到下一帧，等编译结束、Package Manager 空闲后再恢复。
    /// 由于旧 AddRequest 无法跨域保留，恢复完全依赖持久化日志 + 真实 probe 核对（spec 229/449）。
    /// 静态构造与 delayCall 的真实触发列为手动验证；可被测的决策在 RecoveryReconciler。
    [InitializeOnLoad]
    internal static class U3DAILinkerBootstrap
    {
        static U3DAILinkerBootstrap()
        {
            EditorApplication.delayCall += TryResume;
        }

        private static void TryResume()
        {
            // Package Manager 还在解析时让出，下一帧再试，避免与进行中的 UPM 请求竞争。
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += TryResume;
                return;
            }

            var store = new OperationLogStore(LibraryRoot());
            var log = store.Load();
            if (!RecoveryReconciler.ShouldResume(log))
                return;

            var runner = new UpmQueueRunner(store, new UnityUpmClient(), new UnityInstalledPackageProbe());
            var result = runner.Advance(log);

            // Requested 表示已经发出 Add。不要下一帧自旋探测；UPM 请求仍可能进行中。
            // 域重载后静态构造会重新进入恢复路径，或由后续显式 Retry/Resume 接续。
            if (RecoveryReconciler.ShouldScheduleFollowUp(result))
                EditorApplication.delayCall += TryResume;
        }

        // 工程根的 Library 目录：Application.dataPath 去掉末尾 /Assets 再拼 /Library。
        private static string LibraryRoot()
        {
            var assets = UnityEngine.Application.dataPath;            // <proj>/Assets
            var projectRoot = System.IO.Directory.GetParent(assets).FullName;
            return System.IO.Path.Combine(projectRoot, "Library");
        }
    }
}
