using NUnit.Framework;

namespace Yoji.EditorDebug.Tests
{
    public class EditorStateCacheTests
    {
        [Test] public void Cache_NotPlaying_NotCompiling_InTestContext()
        {
            Assert.IsFalse(EditorStateCache.IsPlaying);
            Assert.IsFalse(EditorStateCache.IsCompiling);
        }

        // Install() 在 [InitializeOnLoadMethod] 即刷新一次，timeSinceStartup 应已 > 0。
        [Test] public void Cache_TimeSinceStartup_IsPositive()
            => Assert.Greater(EditorStateCache.TimeSinceStartup, 0.0);
    }
}
