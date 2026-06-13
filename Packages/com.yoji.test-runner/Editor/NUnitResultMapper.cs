namespace Yoji.TestRunner
{
    /// 测试结果归类。overallResult 口径：errored 优先 -> Error；否则 failed>0 -> Failed；否则 Passed。
    /// 计数（passed/failed/skipped，inconclusive 并入 skipped）由 TestRunService 直接读 ITestResultAdaptor，
    /// 不在此重复从 XML 解析。
    internal static class NUnitResultMapper
    {
        public static string OverallResult(int failed, bool errored)
            => errored ? "Error" : failed > 0 ? "Failed" : "Passed";
    }
}
