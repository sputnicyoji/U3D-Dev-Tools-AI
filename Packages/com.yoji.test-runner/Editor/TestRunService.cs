using System;
using System.IO;
using System.Xml;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Yoji.TestRunner
{
    /// TestRunnerApi 持有 + 单一 ICallbacks 收集器 + Execute + RunFinished 收尾。
    /// 阶段 1 仅 EditMode（PlayMode 在 HTTP handler 层已拒）。
    /// RunFinished 是唯一可靠完成信号：在此写 NUnit XML + 通过 JobStore 落 JSON 状态。
    internal sealed class TestRunService : ICallbacks
    {
        private readonly JobStore m_Jobs;
        private readonly string m_ResultDir;
        private TestRunnerApi m_Api;
        private string m_ActiveJobId;

        public TestRunService(JobStore jobs, string resultDir)
        {
            m_Jobs = jobs;
            m_ResultDir = resultDir;
            Directory.CreateDirectory(m_ResultDir);
        }

        /// 持有 TestRunnerApi 于实例字段防 GC；注册一次回调。域重载后由服务重建时再次调用。
        public void EnsureRegistered()
        {
            if (m_Api != null) return;
            m_Api = ScriptableObject.CreateInstance<TestRunnerApi>();
            m_Api.RegisterCallbacks(this);
        }

        /// 主线程调用：占坑 + Execute，立即返回 jobId（不等结果）。非 Idle 时 StartJob 抛 -> 调用方 409。
        public string StartRun(FilterSpec spec)
        {
            EnsureRegistered();
            var jobId = JobStore.NewJobId();
            m_Jobs.StartJob(jobId);
            m_ActiveJobId = jobId;
            try
            {
                m_Api.Execute(new ExecutionSettings(BuildFilter(spec)));
            }
            catch (Exception e)
            {
                m_ActiveJobId = null;
                m_Jobs.FailJob(jobId, "execute failed: " + e.Message);
                throw;
            }
            return jobId;
        }

        private static Filter BuildFilter(FilterSpec spec)
        {
            var filter = new Filter { testMode = TestMode.EditMode };
            if (spec.IsRunAll) return filter; // 全空 = 跑全套件
            if (spec.TestNames.Length > 0) filter.testNames = spec.TestNames;
            if (spec.AssemblyNames.Length > 0) filter.assemblyNames = spec.AssemblyNames;
            if (spec.CategoryNames.Length > 0) filter.categoryNames = spec.CategoryNames;
            if (spec.GroupNames.Length > 0) filter.groupNames = spec.GroupNames;
            return filter;
        }

        // ===== ICallbacks =====
        public void RunStarted(ITestAdaptor testsToRun) { }
        public void TestStarted(ITestAdaptor test) { }
        public void TestFinished(ITestResultAdaptor result) { }

        public void RunFinished(ITestResultAdaptor result)
        {
            var jobId = m_ActiveJobId;
            if (string.IsNullOrEmpty(jobId)) return;
            m_ActiveJobId = null;

            string xmlPath = Path.Combine(m_ResultDir, "result_" + jobId + ".xml");
            try
            {
                using (var w = XmlWriter.Create(xmlPath, new XmlWriterSettings { Indent = true }))
                    result.ToXml().WriteTo(w);
            }
            catch (Exception e)
            {
                Debug.LogError("[TestRunnerMCP] 写结果 XML 失败: " + e);
                xmlPath = null;
            }

            int passed = result.PassCount;
            int failed = result.FailCount;
            int skipped = result.SkipCount + result.InconclusiveCount;
            bool errored = result.TestStatus == TestStatus.Failed && failed == 0;
            string overall = NUnitResultMapper.OverallResult(failed, errored);
            m_Jobs.CompleteJob(jobId, passed, failed, skipped, overall, xmlPath);
        }
    }
}
