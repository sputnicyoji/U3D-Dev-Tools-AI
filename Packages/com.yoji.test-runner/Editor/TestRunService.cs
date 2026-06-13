using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
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
        private const int k_MaxFailures = 50;     // 失败详情上限，防 run-all 大面积失败撑爆响应
        private const int k_MaxFieldChars = 4000;  // 单条 message/stackTrace 截断长度

        private readonly JobStore m_Jobs;
        private readonly string m_ResultDir;
        private TestRunnerApi m_Api;
        private string m_ActiveJobId;
        private bool m_ActiveIsRunAll;           // 当前 run 是否 run-all（决定 0 命中是否判错）
        private List<FailureDetail> m_Failures;  // 当前 run 的失败详情，StartRun 重置、TestFinished 累积

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
            m_ActiveIsRunAll = spec.IsRunAll;
            m_Failures = new List<FailureDetail>();
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
        public void RunStarted(ITestAdaptor testsToRun)
        {
            // 0 命中守卫：非 run-all 却没有任何实际用例被计划执行，几乎必是 testNames/assembly/
            // category/group 拼错。判为 error，避免「跑空 -> passed=0 -> 报 Passed」的假绿。
            var jobId = m_ActiveJobId;
            if (jobId == null || m_ActiveIsRunAll) return;
            var planned = new List<string>();
            CollectTestCaseNames(testsToRun, planned); // 计划树里的真实用例（非 suite）
            if (planned.Count > 0) return;
            // 关键：清掉 m_ActiveJobId，使随后必到的 RunFinished 走早退路径，不会用同 jobId 调
            // CompleteJob 复用 m_Last 把 error 改回 completed/Passed（第二层假绿）。
            m_ActiveJobId = null;
            m_Failures = null;
            m_Jobs.FailJob(jobId, "filter matched 0 tests (check testNames/assemblyNames/categoryNames/groupNames)");
        }

        public void TestStarted(ITestAdaptor test) { }

        public void TestFinished(ITestResultAdaptor result)
        {
            // 仅收集本服务发起的 run（m_ActiveJobId 非空），忽略用户手点 Test Runner 触发的 run。
            if (m_ActiveJobId == null || m_Failures == null) return;
            // 仅叶子用例：suite 失败会冒泡复报 Failed，按 HasChildren 过滤避免双计数。
            if (result.TestStatus != TestStatus.Failed || result.HasChildren) return;
            if (m_Failures.Count >= k_MaxFailures) return;
            m_Failures.Add(new FailureDetail
            {
                Name = result.Test != null ? result.Test.FullName : result.Name,
                Message = Truncate(result.Message, k_MaxFieldChars),
                StackTrace = Truncate(result.StackTrace, k_MaxFieldChars),
            });
        }

        private static string Truncate(string s, int max)
            => string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max) + "...(truncated)";

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
            m_Jobs.CompleteJob(jobId, passed, failed, skipped, overall, xmlPath, m_Failures);
            m_Failures = null;
        }

        /// 列出可发现的测试用例全名。HTTP 线程调用：经 dispatcher 在主线程发起 RetrieveTestList，
        /// 其回调亦在主线程触发，本线程 wait 直到回调完成。主线程持续泵 update，不会死锁。
        public List<string> ListTests(string mode)
        {
            EnsureRegistered();
            var testMode = mode == "PlayMode" ? TestMode.PlayMode : TestMode.EditMode;
            var names = new List<string>();
            var done = new ManualResetEventSlim(false); // 不 dispose：避免超时后晚到回调 Set 已释放对象
            Exception err = null;
            MainThreadDispatcher.Run(() =>
            {
                try
                {
                    m_Api.RetrieveTestList(testMode, root =>
                    {
                        try { CollectTestCaseNames(root, names); }
                        catch (Exception e) { err = e; }
                        finally { done.Set(); }
                    });
                }
                catch (Exception e) { err = e; done.Set(); }
                return null;
            }, out _);
            if (!done.Wait(30000)) throw new TimeoutException("RetrieveTestList timed out");
            if (err != null) throw err;
            return names;
        }

        private static void CollectTestCaseNames(ITestAdaptor t, List<string> outNames)
        {
            if (t == null) return;
            if (!t.IsSuite) { outNames.Add(t.FullName); return; }
            if (t.HasChildren)
                foreach (var c in t.Children) CollectTestCaseNames(c, outNames);
        }
    }
}
