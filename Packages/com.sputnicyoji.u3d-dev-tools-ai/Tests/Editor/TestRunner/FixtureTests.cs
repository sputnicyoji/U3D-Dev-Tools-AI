using NUnit.Framework;

namespace Yoji.TestRunner.Tests
{
    /// e2e 夹具。[Explicit]：无过滤的全套件运行会跳过它们（不污染包单测）；
    /// 服务以完整 testNames 精确选择时才执行。全名见 run-e2e.py 顶部常量。
    public class FixtureTests
    {
        [Test, Explicit] public void AlwaysPasses() => Assert.Pass();
        [Test, Explicit] public void AlwaysFails() => Assert.Fail("intentional failure for e2e");
    }
}
