using System;
using SysProcess = System.Diagnostics.Process;

namespace Yoji.EditorCore.Ports
{
    /// 注册表清理用的进程探活。崩溃/强杀的 Editor 不会走 Unregister，注册与心跳时
    /// 以此谓词剔除死进程残留行。不确定情形（权限等）从宽返回 true——最终由客户端 /ping 探活兜底。
    public static class ProcessLiveness
    {
        public static bool IsAlive(int processId)
        {
            if (processId <= 0)
                return false;

            try
            {
                using (var process = SysProcess.GetProcessById(processId))
                    return !process.HasExited;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (Exception)
            {
                return true;
            }
        }
    }
}
