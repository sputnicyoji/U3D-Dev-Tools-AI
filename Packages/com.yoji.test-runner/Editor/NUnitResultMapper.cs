using System;
using System.Xml.Linq;

namespace Yoji.TestRunner
{
    internal struct TestSummary
    {
        public int Passed;
        public int Failed;
        public int Skipped;
        public string OverallResult; // Passed | Failed | Error
    }

    /// NUnit3 <test-run> 根属性 -> 计数与 overallResult。
    /// 归类口径：errored 优先 -> Error；否则 failed>0 -> Failed；否则 Passed。
    /// skipped = skipped + inconclusive（inconclusive 并入 skipped 桶）。
    internal static class NUnitResultMapper
    {
        public static string OverallResult(int failed, bool errored)
            => errored ? "Error" : failed > 0 ? "Failed" : "Passed";

        public static TestSummary ParseResultXml(string xmlPath)
        {
            var run = XDocument.Load(xmlPath).Root;
            int Attr(string name)
                => int.TryParse((string)run.Attribute(name), out var v) ? v : 0;

            int passed = Attr("passed");
            int failed = Attr("failed");
            int skipped = Attr("skipped") + Attr("inconclusive");
            var resultAttr = (string)run.Attribute("result") ?? "";
            bool errored = resultAttr.IndexOf("Error", StringComparison.OrdinalIgnoreCase) >= 0;

            return new TestSummary
            {
                Passed = passed,
                Failed = failed,
                Skipped = skipped,
                OverallResult = OverallResult(failed, errored),
            };
        }
    }
}
