using System;
using UnityEditor;

namespace Yoji.EditorCore
{
    /// 编辑器内常驻服务的统一生命周期接线：加载/域重载完成后启动，域重载前与退出时停止。
    /// afterAssemblyReload 在域重载结束时同步主线程触发，不受窗口失焦节流影响（delayCall 失焦下被拖慢），
    /// 所以两者都挂——delayCall 兜首次加载，afterAssemblyReload 兜失焦即时恢复。
    /// 共享基础设施（editor-core）：editor-debug / test-runner / lua-device-debug 的服务都经此接线。
    public static class EditorServiceLifecycle
    {
        /// 把一个常驻服务的 start/stop 挂到编辑器生命周期事件上。
        /// start 须自身幂等（重复调用应早退）——delayCall 与 afterAssemblyReload 可能就同一次重载各触发一次。
        public static void Bind(Action start, Action stop)
        {
            if (!ShouldBind(AssetDatabase.IsAssetImportWorkerProcess()))
            {
                return;
            }

            EditorApplication.delayCall += () => start();
            AssemblyReloadEvents.afterAssemblyReload += () => start();
            AssemblyReloadEvents.beforeAssemblyReload += () => stop();
            EditorApplication.quitting += stop;
        }

        internal static bool ShouldBind(bool isAssetImportWorkerProcess)
        {
            return !isAssetImportWorkerProcess;
        }
    }
}
