using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Xml;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using Yoji.EditorCore;

namespace Yoji.TestRunner
{
    /// TestRunnerApi 持有 + 单一 ICallbacks 收集器 + Execute + RunFinished 收尾。
    /// 运行 EditMode/PlayMode 测试，并把 Unity Test Runner 回调映射为 JobStore 状态。
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
        // TR-1a PlayMode：临时置 DisableDomainReload 的设置快照，run 结束无条件还原
        private bool m_PlayModeSettingsSaved;
        private bool m_SavedEnterPlayModeOptionsEnabled;
        private EnterPlayModeOptions m_SavedEnterPlayModeOptions;

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
            bool playMode = spec.TestMode == "PlayMode";
            if (playMode)
            {
                // 前置守卫：已在 Play 中、或有未存脏场景时拒（进 Play 会重载场景丢未存改动）。映射 409。
                if (EditorApplication.isPlaying)
                    throw new InvalidOperationException("editor is already in play mode; exit play mode before a PlayMode test run");
                if (HasDirtyLoadedScene())
                    throw new InvalidOperationException("save open scene(s) first: entering play mode reloads the scene and would discard unsaved changes");
            }

            var jobId = JobStore.NewJobId();
            m_Jobs.StartJob(jobId);
            m_ActiveJobId = jobId;
            m_ActiveIsRunAll = spec.IsRunAll;
            m_Failures = new List<FailureDetail>();
            if (playMode) ApplyPlayModeSettings(); // 临时 DisableDomainReload：PlayMode run 不触发域重载，沿用 field-based 收尾路径
            try
            {
                m_Api.Execute(new ExecutionSettings(BuildFilter(spec)));
            }
            catch (Exception e)
            {
                m_ActiveJobId = null;
                RestorePlayModeSettings();
                m_Jobs.FailJob(jobId, "execute failed: " + e.Message);
                throw;
            }
            return jobId;
        }

        private static Filter BuildFilter(FilterSpec spec)
        {
            var testMode = spec.TestMode == "PlayMode" ? TestMode.PlayMode : TestMode.EditMode;
            var filter = new Filter { testMode = testMode };
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
            // PlayMode 设置在 run 结束无条件还原（no-op if 未置；也覆盖 0 命中 RunStarted 早退、jobId 已清的路径）。
            RestorePlayModeSettings();
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

        // ===== TR-1a PlayMode 设置作用域 =====
        // 临时叠加 EnterPlayModeOptions.DisableDomainReload：PlayMode run 期间不发生域重载，
        // 现有 field-based m_ActiveJobId / RunFinished 路径无需 SessionState 即可可靠收尾（见设计 TR-1a）。
        // 用户原有 enterPlayModeOptionsEnabled / enterPlayModeOptions 完整快照、run 结束还原。
        private void ApplyPlayModeSettings()
        {
            m_SavedEnterPlayModeOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            m_SavedEnterPlayModeOptions = EditorSettings.enterPlayModeOptions;
            m_PlayModeSettingsSaved = true;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            // 叠加而非覆盖：保留用户已选项（如 DisableSceneReload），只追加 DisableDomainReload。
            EditorSettings.enterPlayModeOptions = m_SavedEnterPlayModeOptions | EnterPlayModeOptions.DisableDomainReload;
        }

        private void RestorePlayModeSettings()
        {
            if (!m_PlayModeSettingsSaved) return;
            m_PlayModeSettingsSaved = false;
            EditorSettings.enterPlayModeOptionsEnabled = m_SavedEnterPlayModeOptionsEnabled;
            EditorSettings.enterPlayModeOptions = m_SavedEnterPlayModeOptions;
        }

        private static bool HasDirtyLoadedScene()
        {
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                if (EditorSceneManager.GetSceneAt(i).isDirty) return true;
            return false;
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
