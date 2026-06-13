using System;
using System.Threading;
using UnityEditor;

namespace Yoji.EditorDebug
{
    /// 缓存编辑器态供 /ping 在 HTTP 线程零跳读取。
    /// EditorApplication.is* 与 timeSinceStartup 仅主线程安全，故在 EditorApplication.update /
    /// playModeStateChanged 里刷新到 volatile/Interlocked 字段，HTTP 线程只读缓存。
    /// 镜像 RecompileHandler 的 volatile-flag 缓存做法。
    internal static class EditorStateCache
    {
        private static volatile bool s_IsPlaying;
        private static volatile bool s_IsPaused;
        private static volatile bool s_IsCompiling;
        private static volatile bool s_IsUpdating;
        private static long s_TimeBits; // double 不能 volatile：存 IEEE754 位模式，经 Interlocked 读写

        public static bool IsPlaying => s_IsPlaying;
        public static bool IsPaused => s_IsPaused;
        public static bool IsCompiling => s_IsCompiling;
        public static bool IsUpdating => s_IsUpdating;
        public static double TimeSinceStartup => BitConverter.Int64BitsToDouble(Interlocked.Read(ref s_TimeBits));

        [InitializeOnLoadMethod]
        private static void Install()
        {
            EditorApplication.update += Refresh;
            EditorApplication.playModeStateChanged += _ => Refresh();
            Refresh(); // 立即填一次，避免首个 /ping 在 update 触发前读到全 false
        }

        private static void Refresh()
        {
            s_IsPlaying = EditorApplication.isPlaying;
            s_IsPaused = EditorApplication.isPaused;
            s_IsCompiling = EditorApplication.isCompiling;
            s_IsUpdating = EditorApplication.isUpdating;
            Interlocked.Exchange(ref s_TimeBits, BitConverter.DoubleToInt64Bits(EditorApplication.timeSinceStartup));
        }
    }
}
