using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace Yoji.TestRunner
{
    internal enum ServiceState { Idle, Running, Compiling }

    /// 单个失败用例的详情。随 JobRecord 经 JsonConvert 持久化 round-trip。
    internal sealed class FailureDetail
    {
        public string Name;        // 叶子用例全名（ITestResultAdaptor.Test.FullName）
        public string Message;     // 断言/异常信息
        public string StackTrace;  // 栈（已截断长度）
    }

    /// 一次测试任务的可序列化记录。Status 用字符串以直接对外输出。
    internal sealed class JobRecord
    {
        public string JobId;
        public string Status;          // running | completed | error
        public string Message;
        public string ResultFilePath;
        public string OverallResult;   // Passed | Failed | Error，running 时为 null
        public int Passed;
        public int Failed;
        public int Skipped;
        public long StartedMs;
        public long UpdatedMs;
        public List<FailureDetail> Failures;  // 仅 completed 且有失败时非空；passing run 省略
    }

    /// 状态机 + jobId 生成 + 任务持久化（Temp JSON）+ 最近一次结果缓存 + 孤儿任务清扫。
    /// 纯 C# 可单测：目录与时钟从构造注入。线程安全（HTTP 线程与主线程都会访问）。
    internal sealed class JobStore
    {
        private readonly string m_Dir;
        private readonly Func<long> m_NowMs;
        private readonly object m_Lock = new object();

        private bool m_Compiling;
        private JobRecord m_Current;   // 活跃任务（status=running）
        private JobRecord m_Last;      // 最近一次完成/出错的缓存

        public JobStore(string dir, Func<long> nowMs)
        {
            m_Dir = dir;
            m_NowMs = nowMs;
            Directory.CreateDirectory(m_Dir);
        }

        public static string NewJobId() => Guid.NewGuid().ToString("N");

        public ServiceState State
        {
            get
            {
                lock (m_Lock)
                {
                    if (m_Compiling) return ServiceState.Compiling;
                    if (m_Current != null && m_Current.Status == "running") return ServiceState.Running;
                    return ServiceState.Idle;
                }
            }
        }

        public bool IsIdle => State == ServiceState.Idle;

        public void SetCompiling(bool compiling)
        {
            lock (m_Lock) m_Compiling = compiling;
        }

        /// 占坑发起 running 任务；非 Idle 抛 InvalidOperationException（调用方据此返 409）。
        public JobRecord StartJob(string jobId)
        {
            lock (m_Lock)
            {
                if (!IsIdle) throw new InvalidOperationException("service busy: " + State);
                var now = m_NowMs();
                m_Current = new JobRecord
                {
                    JobId = jobId, Status = "running", Message = "测试运行中",
                    StartedMs = now, UpdatedMs = now,
                };
                Persist(m_Current);
                return m_Current;
            }
        }

        public void CompleteJob(string jobId, int passed, int failed, int skipped, string overallResult, string resultFilePath, List<FailureDetail> failures)
        {
            lock (m_Lock)
            {
                var rec = Target(jobId);
                rec.Status = "completed";
                rec.Passed = passed; rec.Failed = failed; rec.Skipped = skipped;
                rec.OverallResult = overallResult; rec.ResultFilePath = resultFilePath;
                rec.Failures = failures != null && failures.Count > 0 ? failures : null;
                rec.Message = "测试完成"; rec.UpdatedMs = m_NowMs();
                Finish(rec);
            }
        }

        public void FailJob(string jobId, string message)
        {
            lock (m_Lock)
            {
                var rec = Target(jobId);
                rec.Status = "error"; rec.OverallResult = "Error";
                rec.Message = message; rec.UpdatedMs = m_NowMs();
                Finish(rec);
            }
        }

        public JobRecord Find(string jobId)
        {
            lock (m_Lock)
            {
                if (m_Current != null && m_Current.JobId == jobId) return m_Current;
                if (m_Last != null && m_Last.JobId == jobId) return m_Last;
            }
            return LoadFromDisk(jobId);
        }

        public JobRecord ActiveOrLast()
        {
            lock (m_Lock) return m_Current ?? m_Last;
        }

        /// 孤儿清扫：活跃任务超过 staleMs 没更新，置 error（域重载/崩溃导致 RunFinished 没回）。
        public void SweepStale(long staleMs)
        {
            lock (m_Lock)
            {
                if (m_Current != null && m_Current.Status == "running" && m_NowMs() - m_Current.UpdatedMs > staleMs)
                {
                    m_Current.Status = "error";
                    m_Current.OverallResult = "Error";
                    m_Current.Message = "orphaned: no result within " + staleMs + "ms";
                    m_Current.UpdatedMs = m_NowMs();
                    Finish(m_Current);
                }
            }
        }

        // 已持锁：取当前任务；晚到的 complete/fail 复用已落缓存的同 id 记录（保留 StartedMs）；都不匹配才新建
        private JobRecord Target(string jobId)
        {
            if (m_Current != null && m_Current.JobId == jobId) return m_Current;
            if (m_Last != null && m_Last.JobId == jobId) return m_Last;
            return new JobRecord { JobId = jobId, StartedMs = m_NowMs() };
        }

        // 已持锁：落缓存、清活跃、持久化
        private void Finish(JobRecord rec)
        {
            m_Last = rec;
            m_Current = null;
            Persist(rec);
        }

        private string PathFor(string jobId) => Path.Combine(m_Dir, "job_" + jobId + ".json");

        public void Persist(JobRecord rec)
        {
            try { File.WriteAllText(PathFor(rec.JobId), JsonConvert.SerializeObject(rec)); }
            catch { /* 持久化失败不致命，内存仍可用 */ }
        }

        private JobRecord LoadFromDisk(string jobId)
        {
            var path = PathFor(jobId);
            if (!File.Exists(path)) return null;
            try { return JsonConvert.DeserializeObject<JobRecord>(File.ReadAllText(path)); }
            catch { return null; }
        }
    }
}
