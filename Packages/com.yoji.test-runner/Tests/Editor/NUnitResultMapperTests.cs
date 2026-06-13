using NUnit.Framework;

namespace Yoji.TestRunner.Tests
{
    public class NUnitResultMapperTests
    {
        [Test] public void OverallResult_Passed_WhenNoFailNoError()
            => Assert.AreEqual("Passed", NUnitResultMapper.OverallResult(0, false));

        [Test] public void OverallResult_Failed_WhenFailures()
            => Assert.AreEqual("Failed", NUnitResultMapper.OverallResult(2, false));

        [Test] public void OverallResult_Error_WhenErrored()
            => Assert.AreEqual("Error", NUnitResultMapper.OverallResult(0, true));

        [Test] public void OverallResult_Error_TakesPrecedenceOverFail()
            => Assert.AreEqual("Error", NUnitResultMapper.OverallResult(3, true));
    }
}
