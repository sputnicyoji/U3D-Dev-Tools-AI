using System;
using System.IO;
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

        private string WriteXml(string content)
        {
            var p = Path.Combine(Path.GetTempPath(), "trmcp_xml_" + Guid.NewGuid().ToString("N") + ".xml");
            File.WriteAllText(p, content);
            return p;
        }

        [Test] public void ParseResultXml_ReadsRootCounts()
        {
            var path = WriteXml(
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                "<test-run id=\"2\" testcasecount=\"3\" result=\"Failed\" total=\"3\" passed=\"2\" failed=\"1\" inconclusive=\"0\" skipped=\"0\"></test-run>");
            try
            {
                var s = NUnitResultMapper.ParseResultXml(path);
                Assert.AreEqual(2, s.Passed);
                Assert.AreEqual(1, s.Failed);
                Assert.AreEqual(0, s.Skipped);
                Assert.AreEqual("Failed", s.OverallResult);
            }
            finally { File.Delete(path); }
        }

        [Test] public void ParseResultXml_InconclusiveFoldsIntoSkipped()
        {
            var path = WriteXml(
                "<test-run result=\"Passed\" total=\"4\" passed=\"2\" failed=\"0\" inconclusive=\"1\" skipped=\"1\"></test-run>");
            try
            {
                var s = NUnitResultMapper.ParseResultXml(path);
                Assert.AreEqual(2, s.Passed);
                Assert.AreEqual(0, s.Failed);
                Assert.AreEqual(2, s.Skipped); // skipped 1 + inconclusive 1
                Assert.AreEqual("Passed", s.OverallResult);
            }
            finally { File.Delete(path); }
        }

        [Test] public void ParseResultXml_ErrorResult_MapsToError()
        {
            var path = WriteXml(
                "<test-run result=\"Error\" total=\"1\" passed=\"0\" failed=\"0\" inconclusive=\"0\" skipped=\"0\"></test-run>");
            try
            {
                Assert.AreEqual("Error", NUnitResultMapper.ParseResultXml(path).OverallResult);
            }
            finally { File.Delete(path); }
        }
    }
}
