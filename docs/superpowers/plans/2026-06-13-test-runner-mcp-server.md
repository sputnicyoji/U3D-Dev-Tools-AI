# TestRunnerMCP Unity 服务端实现计划（EditMode-only 阶段 1）

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为 test-runner-mcp 实现缺失的 Unity 侧 C# HTTP 服务（UPM 包 `com.yoji.test-runner`，端口 21890），按 `test-runner-mcp/SKILL.md` 契约提供 `/ping` `/recompile` `/run-tests` `/test-status`，用 TestRunnerApi 跑 EditMode 测试，自带 client.py + run-e2e.py 验收。

**Architecture:** `[InitializeOnLoad]` 起 `HttpListener`（127.0.0.1:21890，fallback 21894/21895），后台线程收请求；改写 editor-debug 的监听器外壳以发**真 HTTP 状态码**（200/202/400/404/409）+ 每端点独立 JSON 形状。`/run-tests` 经 `MainThreadDispatcher` 在主线程构建 `TestRunnerApi.Filter` 并 `Execute`，**立即返回 202 + jobId**；`ICallbacks` 收集器在 `RunFinished` 写 NUnit XML + JSON 状态到 `Temp/TestRunnerMCP/`，更新状态机。磁盘是持久真相、内存只是缓存。EditMode 在进程内跑、自身不触发域重载，**绕开 PlayMode 域重载存活这个硬骨头**（留阶段 2）。

**Tech Stack:** Unity 6000.3.16f1（`E:\Unity\Unity Editor\6000.3.16f1\Editor\Unity.exe`）、C#、`UnityEditor.TestTools.TestRunner.Api`（com.unity.test-framework）、Newtonsoft.Json、Unity Test Framework（EditMode 单测）、Python 3（e2e）。

---

## 锁定决策（执行者必读，不要重新讨论）

1. **独立包 `com.yoji.test-runner`**（不并入 editor-debug，不抽 `com.yoji.editor-core`）。复用 editor-debug 基建：`MainThreadDispatcher.cs` **原样照搬**；监听器外壳与 `RecompileHandler.cs` **复制后改用**，并在两边文件头注释标注"有意重复、契约不同，未来抽 core 时先统一响应契约"。
2. **EditMode-only 阶段 1**：`/run-tests` 的 `testMode` 仍收 `EditMode|PlayMode`，但 PlayMode 路径在阶段 1 直接返 `400`（`message` 说明 PlayMode 阶段 2 才支持），不实现域重载存活。EditMode 在一个 domain 内起跑收尾，无需 SessionState 跨重载。
3. **扩展契约支持 run-all**（用户已批准，是对 SKILL.md 已发布契约的修改）：`/run-tests` 请求体扩展为 `{ testMode, testNames?, assemblyNames?, categoryNames?, groupNames? }`。当 `testNames` 与其它 filter 字段**全空**时 = 跑该 testMode 全套件（不再返 400）。`400` 只在 body 非法 JSON / `testMode` 缺失或非 `EditMode|PlayMode` 时触发。Task 9 在 SKILL.md 写明这条扩展。
4. **端口**：21890 主，fallback `21894/21895`（避开 editor-debug 的 21891-21893）。两个监听器在同一 Editor 共存无冲突。
5. **持久化**：每个 job 的结果写 `<projectPath>/Temp/TestRunnerMCP/result_<jobId>.xml`（NUnit3 XML）+ `<projectPath>/Temp/TestRunnerMCP/job_<jobId>.json`（紧凑状态）。内存保留"最近一次"缓存；`/test-status` 优先内存、回退磁盘。
6. **响应契约**（real status codes，与 editor-debug 的恒 200 flat envelope 不同）：
   - `/ping` GET → 200 `{state, listening, timestamp, unityVersion, projectName}`
   - `/recompile` GET/POST → 200 `{success, message, compilationTime, hasErrors}`；非 Idle → 409
   - `/run-tests` POST → 202 `{success, jobId, status:"running", message}`；body 非法 → 400；非 Idle → 409
   - `/test-status` GET `?jobId=` → 200 `{jobId, status, ...}`；未知 jobId → 404
7. **状态机** `Idle | Running | Compiling`，`/recompile` 与 `/run-tests` 在非 Idle 时 409；`/ping` `/test-status` 任何状态可读。

## 命名口径

- 服务名 `TestRunnerMCP`（`/ping` 不直接校验服务名，但日志用 `[TestRunnerMCP] 服务已启动`）。
- 包名 `com.yoji.test-runner`，asmdef `Yoji.TestRunner.Editor` / `Yoji.TestRunner.Editor.Tests`，rootNamespace `Yoji.TestRunner`。

## 通用命令（PowerShell）

```powershell
$UNITY = "E:\Unity\Unity Editor\6000.3.16f1\Editor\Unity.exe"
$REPO  = "E:\Yoji\U3D-Dev-Tools-AI"
$TP    = "$REPO\TestProjects\test-runner"

function Wait-UnityGone { while (Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" | Where-Object { $_.CommandLine -match "TestProjects.test-runner" }) { Start-Sleep 2 } }

function Run-EditModeTests {
    & $UNITY -batchmode -projectPath $TP -runTests -testPlatform EditMode `
        -testResults "$TP\TestResults\results.xml" -logFile "$TP\TestResults\unity.log" | Out-Null
    "exit=$LASTEXITCODE"
}
```

测试运行同步前台、timeout 600000ms。测试通过 = 退出码 0 且 results.xml 根 `result="Passed"`。跑前确认没有 Unity 占着 test-runner 工程（有无关的 SLG_Prototype / editor-debug Unity 进程，**勿动勿杀**）。

## 文件结构（最终形态）

```
Packages/com.yoji.test-runner/
├── package.json
├── Editor/
│   ├── Yoji.TestRunner.Editor.asmdef        # 引用 UnityEditor/UnityEngine.TestRunner
│   ├── AssemblyInfo.cs                       # InternalsVisibleTo 测试程序集
│   ├── TestRunnerMCP.cs                      # HttpListener 生命周期 + 路由 + real status codes（改自 EditorDebugMCP）
│   ├── MainThreadDispatcher.cs               # 原样照搬 editor-debug
│   ├── JobStore.cs                           # 状态机 + jobId + 持久化（SessionState + Temp JSON）+ 缓存 + sweeper
│   ├── NUnitResultMapper.cs                  # ITestResultAdaptor/NUnit XML -> {passed,failed,skipped,overallResult} + 写 XML
│   ├── TestFilterBuilder.cs                  # 请求 -> TestRunnerApi.Filter（含 run-all 扩展）
│   ├── TestRunService.cs                     # TestRunnerApi 持有 + ICallbacks 收集器 + Execute + RunFinished 收尾
│   └── RecompileHandler.cs                   # 改自 editor-debug：加 AssetDatabase.Refresh + flat 响应 + 409/Compiling
├── Tests/Editor/
│   ├── Yoji.TestRunner.Editor.Tests.asmdef
│   ├── JobStoreTests.cs
│   ├── NUnitResultMapperTests.cs
│   └── TestFilterBuilderTests.cs
└── Agent~/skills/test-runner-mcp/            # Task 9 迁入：SKILL.md + client.py + references/run-e2e.py
TestProjects/test-runner/                     # 最小验证工程 + 故意通过/失败的 EditMode 夹具
docs/superpowers/plans/2026-06-13-test-runner-mcp-server.md
```

可单测的：JobStore / NUnitResultMapper / TestFilterBuilder（纯 C#，不碰 HttpListener 与活 Editor 运行测试）。集成验证的：TestRunService + HTTP 端点（靠 Task 8 e2e）。

---

### Task 1: 脚手架 — UPM 包 + 测试工程

**Files:**
- Create: `Packages/com.yoji.test-runner/package.json`
- Create: `Packages/com.yoji.test-runner/Editor/Yoji.TestRunner.Editor.asmdef`
- Create: `Packages/com.yoji.test-runner/Editor/AssemblyInfo.cs`
- Create: `Packages/com.yoji.test-runner/Tests/Editor/Yoji.TestRunner.Editor.Tests.asmdef`
- Create: `TestProjects/test-runner/Packages/manifest.json`
- Create: `TestProjects/test-runner/ProjectSettings/ProjectVersion.txt`

- [ ] **Step 1: package.json**

```json
{
  "name": "com.yoji.test-runner",
  "version": "0.1.0",
  "displayName": "Test Runner MCP",
  "description": "HTTP service to trigger Unity recompilation and run EditMode tests headlessly for AI agents.",
  "unity": "2022.3",
  "dependencies": {
    "com.unity.test-framework": "1.5.1",
    "com.unity.nuget.newtonsoft-json": "3.2.2"
  }
}
```

- [ ] **Step 2: Editor asmdef + AssemblyInfo**

`Editor/Yoji.TestRunner.Editor.asmdef`:
```json
{
  "name": "Yoji.TestRunner.Editor",
  "rootNamespace": "Yoji.TestRunner",
  "references": ["UnityEditor.TestRunner", "UnityEngine.TestRunner"],
  "includePlatforms": ["Editor"],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": true,
  "precompiledReferences": ["nunit.framework.dll", "Newtonsoft.Json.dll"],
  "autoReferenced": true,
  "defineConstraints": [],
  "versionDefines": [],
  "noEngineReferences": false
}
```

`Editor/AssemblyInfo.cs`:
```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Yoji.TestRunner.Editor.Tests")]
```

- [ ] **Step 3: Tests asmdef**

`Tests/Editor/Yoji.TestRunner.Editor.Tests.asmdef`:
```json
{
  "name": "Yoji.TestRunner.Editor.Tests",
  "rootNamespace": "Yoji.TestRunner.Tests",
  "references": ["Yoji.TestRunner.Editor", "UnityEngine.TestRunner", "UnityEditor.TestRunner"],
  "includePlatforms": ["Editor"],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": true,
  "precompiledReferences": ["nunit.framework.dll", "Newtonsoft.Json.dll"],
  "autoReferenced": false,
  "defineConstraints": ["UNITY_INCLUDE_TESTS"],
  "versionDefines": [],
  "noEngineReferences": false
}
```

- [ ] **Step 4: 最小测试工程**

`TestProjects/test-runner/Packages/manifest.json`:
```json
{
  "dependencies": {
    "com.unity.test-framework": "1.5.1",
    "com.yoji.test-runner": "file:../../../Packages/com.yoji.test-runner",
    "com.unity.modules.imgui": "1.0.0",
    "com.unity.modules.jsonserialize": "1.0.0"
  },
  "testables": ["com.yoji.test-runner"]
}
```

`TestProjects/test-runner/ProjectSettings/ProjectVersion.txt`（冒号后一空格，无 BOM，末尾换行）:
```
m_EditorVersion: 6000.3.16f1
```

仓库根 `.gitignore` 已有 `TestProjects/*/...` 规则覆盖本工程，无需改动。

- [ ] **Step 5: batchmode 冒烟验证包解析**

```powershell
Wait-UnityGone
& $UNITY -batchmode -quit -projectPath $TP -logFile "$TP\TestResults\bootstrap.log" | Out-Null
"exit=$LASTEXITCODE"
```
Expected: 退出码 0。失败时 `Select-String -Path "$TP\TestResults\bootstrap.log" -Pattern "error CS|Failed to|cannot" | Select-Object -First 10`。常见坑：asmdef 引用名错（应为 `UnityEditor.TestRunner` / `UnityEngine.TestRunner`）、manifest file: 路径深度错。

- [ ] **Step 6: Commit**

```powershell
git -C $REPO add -A
git -C $REPO commit -m "feat(test-runner): scaffold UPM package com.yoji.test-runner and test project"
```
提交信息末尾加 `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`。提交前 `git status --short` 确认只含本任务文件 + Unity 生成的 .meta/ProjectSettings。

---

### Task 2: JobStore — 状态机 + jobId + 持久化（TDD）

**Files:**
- Create: `Packages/com.yoji.test-runner/Editor/JobStore.cs`
- Test: `Packages/com.yoji.test-runner/Tests/Editor/JobStoreTests.cs`

- [ ] **Step 1: 桩 + 失败测试**

`Editor/JobStore.cs`（桩）:
```csharp
using System;

namespace Yoji.TestRunner
{
    internal enum ServiceState { Idle, Running, Compiling }

    internal sealed class JobRecord
    {
        public string JobId;
        public string Status;
        public string Message;
        public string ResultFilePath;
        public string OverallResult;
        public int Passed;
        public int Failed;
        public int Skipped;
        public long StartedMs;
        public long UpdatedMs;
    }

    internal sealed class JobStore
    {
        public JobStore(string dir, Func<long> nowMs) => throw new NotImplementedException();
        public static string NewJobId() => throw new NotImplementedException();
    }
}
```

`Tests/Editor/JobStoreTests.cs`:
```csharp
using System;
using System.IO;
using NUnit.Framework;

namespace Yoji.TestRunner.Tests
{
    public class JobStoreTests
    {
        private string m_Dir;
        private long m_Now;

        [SetUp] public void SetUp()
        {
            m_Dir = Path.Combine(Path.GetTempPath(), "trmcp_" + Guid.NewGuid().ToString("N"));
            m_Now = 1_000_000;
        }

        [TearDown] public void TearDown()
        {
            try { if (Directory.Exists(m_Dir)) Directory.Delete(m_Dir, true); } catch { }
        }

        private JobStore New() => new JobStore(m_Dir, () => m_Now);

        [Test] public void NewJobId_Is32HexChars()
        {
            var id = JobStore.NewJobId();
            Assert.AreEqual(32, id.Length);
            StringAssert.IsMatch("^[0-9a-f]{32}$", id);
        }

        [Test] public void Fresh_IsIdle()
        {
            Assert.IsTrue(New().IsIdle);
            Assert.AreEqual(ServiceState.Idle, New().State);
        }

        [Test] public void StartJob_TransitionsToRunning()
        {
            var s = New();
            var rec = s.StartJob(JobStore.NewJobId());
            Assert.AreEqual("running", rec.Status);
            Assert.AreEqual(ServiceState.Running, s.State);
            Assert.IsFalse(s.IsIdle);
        }

        [Test] public void StartJob_WhenBusy_Throws()
        {
            var s = New();
            s.StartJob(JobStore.NewJobId());
            Assert.Throws<InvalidOperationException>(() => s.StartJob(JobStore.NewJobId()));
        }

        [Test] public void CompleteJob_TransitionsToIdle_AndCaches()
        {
            var s = New();
            var id = JobStore.NewJobId();
            s.StartJob(id);
            s.CompleteJob(id, 2, 1, 0, "Failed", "C:/x/result.xml");
            Assert.IsTrue(s.IsIdle);
            var found = s.Find(id);
            Assert.AreEqual("completed", found.Status);
            Assert.AreEqual(2, found.Passed);
            Assert.AreEqual(1, found.Failed);
            Assert.AreEqual("Failed", found.OverallResult);
        }

        [Test] public void SetCompiling_ReflectsState()
        {
            var s = New();
            s.SetCompiling(true);
            Assert.AreEqual(ServiceState.Compiling, s.State);
            s.SetCompiling(false);
            Assert.AreEqual(ServiceState.Idle, s.State);
        }

        [Test] public void Find_Unknown_ReturnsNull()
            => Assert.IsNull(New().Find("deadbeef"));

        [Test] public void Persistence_RoundTrip_AcrossInstances()
        {
            var id = JobStore.NewJobId();
            var s1 = New();
            s1.StartJob(id);
            s1.CompleteJob(id, 5, 0, 1, "Passed", "C:/r.xml");
            var s2 = New(); // 新实例同目录，模拟域重载后重建
            var found = s2.Find(id);
            Assert.IsNotNull(found);
            Assert.AreEqual("completed", found.Status);
            Assert.AreEqual(5, found.Passed);
            Assert.AreEqual(1, found.Skipped);
            Assert.AreEqual("Passed", found.OverallResult);
        }

        [Test] public void ActiveOrLast_PrefersActive()
        {
            var s = New();
            var a = JobStore.NewJobId(); s.StartJob(a); s.CompleteJob(a, 1, 0, 0, "Passed", "x");
            var b = JobStore.NewJobId(); s.StartJob(b);
            Assert.AreEqual(b, s.ActiveOrLast().JobId);
        }

        [Test] public void SweepStale_OrphansRunningJob()
        {
            var s = New();
            var id = JobStore.NewJobId();
            s.StartJob(id);
            m_Now += 60_000; // 推进时钟 60s
            s.SweepStale(30_000);
            Assert.IsTrue(s.IsIdle);
            Assert.AreEqual("error", s.Find(id).Status);
        }
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

```powershell
Wait-UnityGone; Run-EditModeTests
```
Expected: JobStore 各用例 FAIL（NotImplementedException）。

- [ ] **Step 3: 实现**

`Editor/JobStore.cs`（完整替换）:
```csharp
using System;
using System.IO;
using Newtonsoft.Json;

namespace Yoji.TestRunner
{
    internal enum ServiceState { Idle, Running, Compiling }

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

        public void CompleteJob(string jobId, int passed, int failed, int skipped, string overallResult, string resultFilePath)
        {
            lock (m_Lock)
            {
                var rec = Target(jobId);
                rec.Status = "completed";
                rec.Passed = passed; rec.Failed = failed; rec.Skipped = skipped;
                rec.OverallResult = overallResult; rec.ResultFilePath = resultFilePath;
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

        // 已持锁：取当前任务或为该 jobId 新建一条
        private JobRecord Target(string jobId)
            => (m_Current != null && m_Current.JobId == jobId)
                ? m_Current
                : new JobRecord { JobId = jobId, StartedMs = m_NowMs() };

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
```

- [ ] **Step 4: 跑测试确认通过**

```powershell
Wait-UnityGone; Run-EditModeTests
```
Expected: 退出码 0，全部 PASS。

- [ ] **Step 5: Commit**

```powershell
git -C $REPO add -A; git -C $REPO commit -m "feat(test-runner): JobStore state machine, jobId, persistence, stale sweeper"
```

---

### Task 3: NUnitResultMapper — XML 解析 + overallResult 归类（TDD）

**Files:**
- Create: `Packages/com.yoji.test-runner/Editor/NUnitResultMapper.cs`
- Test: `Packages/com.yoji.test-runner/Tests/Editor/NUnitResultMapperTests.cs`

纯 C# 用 `System.Xml.Linq` 解析 NUnit3 `<test-run>` 根属性，不依赖 nunit.framework。`ITestResultAdaptor.ToXml` 写盘在 Task 5 服务里做（碰 NUnit）。

- [ ] **Step 1: 桩 + 失败测试**

`Editor/NUnitResultMapper.cs`（桩）:
```csharp
using System;

namespace Yoji.TestRunner
{
    internal struct TestSummary
    {
        public int Passed;
        public int Failed;
        public int Skipped;
        public string OverallResult; // Passed | Failed | Error
    }

    internal static class NUnitResultMapper
    {
        public static string OverallResult(int failed, bool errored) => throw new NotImplementedException();
        public static TestSummary ParseResultXml(string xmlPath) => throw new NotImplementedException();
    }
}
```

`Tests/Editor/NUnitResultMapperTests.cs`:
```csharp
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
```

- [ ] **Step 2: 跑测试确认失败**

```powershell
Wait-UnityGone; Run-EditModeTests
```
Expected: Mapper 用例 FAIL，Task 2 用例 PASS。

- [ ] **Step 3: 实现**

`Editor/NUnitResultMapper.cs`（完整替换）:
```csharp
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
```

- [ ] **Step 4: 跑测试确认通过 / Step 5: Commit**

```powershell
Wait-UnityGone; Run-EditModeTests
git -C $REPO add -A; git -C $REPO commit -m "feat(test-runner): NUnit result XML mapping and overallResult bucketing"
```

---

### Task 4: TestFilterBuilder — 请求解析 + run-all 扩展（TDD）

**Files:**
- Create: `Packages/com.yoji.test-runner/Editor/TestFilterBuilder.cs`
- Test: `Packages/com.yoji.test-runner/Tests/Editor/TestFilterBuilderTests.cs`

产出中间 `FilterSpec`（不碰 TestRunnerApi 类型，纯可测）；映射到真 `Filter` 在 Task 5 服务里做。这里落地 run-all 扩展：所有 filter 字段全空 = 跑全套件（不再 400）。

- [ ] **Step 1: 桩 + 失败测试**

`Editor/TestFilterBuilder.cs`（桩）:
```csharp
using System;
using Newtonsoft.Json.Linq;

namespace Yoji.TestRunner
{
    internal sealed class FilterSpec
    {
        public string TestMode;
        public string[] TestNames = Array.Empty<string>();
        public string[] AssemblyNames = Array.Empty<string>();
        public string[] CategoryNames = Array.Empty<string>();
        public string[] GroupNames = Array.Empty<string>();
        public bool IsRunAll;
    }

    internal static class TestFilterBuilder
    {
        public static FilterSpec Parse(JObject req) => throw new NotImplementedException();
    }
}
```

`Tests/Editor/TestFilterBuilderTests.cs`:
```csharp
using System;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Yoji.TestRunner.Tests
{
    public class TestFilterBuilderTests
    {
        private static FilterSpec P(string json) => TestFilterBuilder.Parse(JObject.Parse(json));

        [Test] public void Parse_EditModeWithTestNames()
        {
            var s = P("{\"testMode\":\"EditMode\",\"testNames\":[\"A.B.C\",\"A.B.D\"]}");
            Assert.AreEqual("EditMode", s.TestMode);
            Assert.AreEqual(2, s.TestNames.Length);
            Assert.IsFalse(s.IsRunAll);
        }

        [Test] public void Parse_MissingTestMode_Throws()
            => Assert.Throws<ArgumentException>(() => P("{\"testNames\":[\"A.B.C\"]}"));

        [Test] public void Parse_InvalidTestMode_Throws()
            => Assert.Throws<ArgumentException>(() => P("{\"testMode\":\"Banana\"}"));

        [Test] public void Parse_EmptyEverything_IsRunAll()
        {
            var s = P("{\"testMode\":\"EditMode\"}");
            Assert.IsTrue(s.IsRunAll);
            Assert.AreEqual(0, s.TestNames.Length);
        }

        [Test] public void Parse_EmptyTestNamesArray_IsRunAll()
        {
            var s = P("{\"testMode\":\"EditMode\",\"testNames\":[]}");
            Assert.IsTrue(s.IsRunAll);
        }

        [Test] public void Parse_WithAssemblyNames_NotRunAll()
        {
            var s = P("{\"testMode\":\"EditMode\",\"assemblyNames\":[\"MyGame.Tests\"]}");
            Assert.IsFalse(s.IsRunAll);
            Assert.AreEqual("MyGame.Tests", s.AssemblyNames[0]);
        }

        [Test] public void Parse_IgnoresBlankNames()
        {
            var s = P("{\"testMode\":\"EditMode\",\"testNames\":[\"\",\"  \",\"A.B.C\"]}");
            Assert.AreEqual(1, s.TestNames.Length);
            Assert.AreEqual("A.B.C", s.TestNames[0]);
        }

        [Test] public void Parse_PlayMode_Accepted_AtParseLayer()
        {
            // 解析层接受 PlayMode；阶段 1 的拒绝在 HTTP handler 层做
            var s = P("{\"testMode\":\"PlayMode\",\"testNames\":[\"A.B.C\"]}");
            Assert.AreEqual("PlayMode", s.TestMode);
        }
    }
}
```

- [ ] **Step 2: 跑测试确认失败 / Step 3: 实现**

`Editor/TestFilterBuilder.cs`（完整替换）:
```csharp
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Yoji.TestRunner
{
    internal sealed class FilterSpec
    {
        public string TestMode;
        public string[] TestNames = Array.Empty<string>();
        public string[] AssemblyNames = Array.Empty<string>();
        public string[] CategoryNames = Array.Empty<string>();
        public string[] GroupNames = Array.Empty<string>();
        public bool IsRunAll;
    }

    /// 解析 /run-tests 请求体为 FilterSpec。非法 -> ArgumentException（调用方返 400）。
    /// run-all 扩展：testNames/assemblyNames/categoryNames/groupNames 全空 = 跑该 testMode 全套件。
    internal static class TestFilterBuilder
    {
        public static FilterSpec Parse(JObject req)
        {
            var mode = (string)req["testMode"];
            if (mode != "EditMode" && mode != "PlayMode")
                throw new ArgumentException("testMode must be EditMode or PlayMode");

            var spec = new FilterSpec
            {
                TestMode = mode,
                TestNames = ToArray(req["testNames"]),
                AssemblyNames = ToArray(req["assemblyNames"]),
                CategoryNames = ToArray(req["categoryNames"]),
                GroupNames = ToArray(req["groupNames"]),
            };
            spec.IsRunAll = spec.TestNames.Length == 0 && spec.AssemblyNames.Length == 0
                && spec.CategoryNames.Length == 0 && spec.GroupNames.Length == 0;
            return spec;
        }

        private static string[] ToArray(JToken t)
        {
            if (t == null || t.Type != JTokenType.Array) return Array.Empty<string>();
            var list = new List<string>();
            foreach (var x in (JArray)t)
            {
                var s = (string)x;
                if (!string.IsNullOrWhiteSpace(s)) list.Add(s);
            }
            return list.ToArray();
        }
    }
}
```

- [ ] **Step 4: 跑测试确认通过 / Step 5: Commit**

```powershell
Wait-UnityGone; Run-EditModeTests
git -C $REPO add -A; git -C $REPO commit -m "feat(test-runner): request-to-FilterSpec parsing with run-all extension"
```

---

### Task 5: TestRunService — TestRunnerApi 集成（编译验证，行为留 Task 7 e2e）

TestRunnerApi 跑测试无法在 EditMode 单测里可靠自测（测试里跑测试），本任务只做编译验证；行为由 Task 7 e2e 验收。

**Files:**
- Create: `Packages/com.yoji.test-runner/Editor/TestRunService.cs`

- [ ] **Step 1: 实现**

`Editor/TestRunService.cs`:
```csharp
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
                    result.ToXml(true).WriteTo(w);
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
```

- [ ] **Step 2: 编译验证**

```powershell
Wait-UnityGone; Run-EditModeTests
```
Expected: 退出码 0，Task 2-4 单测仍全 PASS（新增文件零编译错误）。
注意：`m_Api.Execute` / `RegisterCallbacks` 在 Unity 6.3 可能带 "will become obsolete" 警告——仅警告可接受。若是硬编译错误（API 已移除），改用静态 `TestRunnerApi.RegisterTestCallback` / `TestRunnerApi.ExecuteTestRun(new ExecutionSettings(...))`，并在报告中给出 diff。`result.ToXml(true)` 返回 NUnit `TNode`（需 nunit.framework，asmdef 已加）。

- [ ] **Step 3: Commit**

```powershell
git -C $REPO add -A; git -C $REPO commit -m "feat(test-runner): TestRunnerApi runner with RunFinished result capture"
```

---

### Task 6: HTTP 服务（MainThreadDispatcher + RecompileHandler + TestRunnerMCP host）

HTTP 生命周期不可单测，本任务编译验证 + 看日志确认监听器起来；端点行为由 Task 7 e2e 验收。

**Files:**
- Create: `Packages/com.yoji.test-runner/Editor/MainThreadDispatcher.cs`
- Create: `Packages/com.yoji.test-runner/Editor/RecompileHandler.cs`
- Create: `Packages/com.yoji.test-runner/Editor/TestRunnerMCP.cs`

- [ ] **Step 1: MainThreadDispatcher（原样照搬 editor-debug，仅改命名空间）**

`Editor/MainThreadDispatcher.cs`:
```csharp
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading;
using UnityEditor;

namespace Yoji.TestRunner
{
    /// 把 HTTP 线程的工作派发到 Editor 主线程执行并阻塞等待结果。
    /// 与 com.yoji.editor-debug 的同名文件有意保持一致（未来抽 com.yoji.editor-core 时统一）。
    internal static class MainThreadDispatcher
    {
        private sealed class Job
        {
            public Func<object> Work;
            public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
            public object Result;
            public Exception Error;
            public long ElapsedMs;
        }

        private static readonly ConcurrentQueue<Job> s_Queue = new ConcurrentQueue<Job>();
        private static int s_MainThreadId = -1;

        [InitializeOnLoadMethod]
        private static void Install()
        {
            s_MainThreadId = Thread.CurrentThread.ManagedThreadId;
            EditorApplication.update += Pump;
        }

        private static void Pump()
        {
            while (s_Queue.TryDequeue(out var job))
            {
                var sw = Stopwatch.StartNew();
                try { job.Result = job.Work(); }
                catch (Exception e) { job.Error = e; }
                job.ElapsedMs = sw.ElapsedMilliseconds;
                job.Done.Set();
            }
        }

        public static object Run(Func<object> work, out long elapsedMs, int timeoutMs = 100000)
        {
            if (Thread.CurrentThread.ManagedThreadId == s_MainThreadId)
            {
                var sw = Stopwatch.StartNew();
                var direct = work();
                elapsedMs = sw.ElapsedMilliseconds;
                return direct;
            }

            var job = new Job { Work = work };
            s_Queue.Enqueue(job);
            if (!job.Done.Wait(timeoutMs))
                throw new TimeoutException("editor main thread did not respond within " + timeoutMs + "ms");
            elapsedMs = job.ElapsedMs;
            if (job.Error != null) ExceptionDispatchInfo.Capture(job.Error).Throw();
            return job.Result;
        }
    }
}
```

- [ ] **Step 2: RecompileHandler（改自 editor-debug：加 AssetDatabase.Refresh + flat 响应 + 409/Compiling）**

`Editor/RecompileHandler.cs`:
```csharp
using System;
using System.Diagnostics;
using System.Threading;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Compilation;

namespace Yoji.TestRunner
{
    /// /recompile：主线程 AssetDatabase.Refresh + RequestScriptCompilation，HTTP 线程轮询完成后写响应。
    /// 有意复制自 com.yoji.editor-debug 的 RecompileHandler 并改动：(1) 增加 AssetDatabase.Refresh；
    /// (2) 响应为 flat {success,message,compilationTime,hasErrors}（无 ok/elapsedMs 信封）；
    /// (3) 接 JobStore 状态机，非 Idle 返 409。未来抽 com.yoji.editor-core 时需先统一响应契约。
    internal static class RecompileHandler
    {
        private static volatile bool s_CompilationStarted;
        private static volatile bool s_CompilationFinished;
        private static volatile bool s_HasErrors;
        private static int s_Pending;

        [InitializeOnLoadMethod]
        private static void Install()
        {
            CompilationPipeline.compilationStarted += _ => s_CompilationStarted = true;
            CompilationPipeline.compilationFinished += _ => s_CompilationFinished = true;
            CompilationPipeline.assemblyCompilationFinished += (_, messages) =>
            {
                foreach (var m in messages)
                    if (m.type == CompilerMessageType.Error) s_HasErrors = true;
            };
        }

        public static (int status, JObject body) Run(JobStore jobs)
        {
            Interlocked.Increment(ref s_Pending);
            try
            {
                bool busy = false;
                MainThreadDispatcher.Run(() =>
                {
                    if (!jobs.IsIdle || EditorApplication.isCompiling) { busy = true; return null; }
                    jobs.SetCompiling(true);
                    s_CompilationStarted = false;
                    s_CompilationFinished = false;
                    s_HasErrors = false;
                    AssetDatabase.Refresh();
                    CompilationPipeline.RequestScriptCompilation();
                    return null;
                }, out _);

                if (busy)
                    return (409, new JObject
                    {
                        ["success"] = false,
                        ["message"] = "a compilation or test run is already in progress",
                        ["compilationTime"] = 0,
                        ["hasErrors"] = false,
                    });

                var sw = Stopwatch.StartNew();
                // 5s 内没进入编译说明没有可编译的变更，直接视为成功
                while (!s_CompilationStarted && sw.ElapsedMilliseconds < 5000) Thread.Sleep(100);
                if (s_CompilationStarted)
                    while (!s_CompilationFinished && sw.ElapsedMilliseconds < 180000) Thread.Sleep(100);

                // 无变更/无重载路径：复位 Compiling（有重载时域会被整体重建，无需复位）
                jobs.SetCompiling(false);

                var seconds = Math.Round(sw.Elapsed.TotalSeconds, 2);
                return (200, new JObject
                {
                    ["success"] = !s_HasErrors,
                    ["message"] = s_HasErrors ? "编译失败" : "编译成功，耗时 " + seconds + "s",
                    ["compilationTime"] = seconds,
                    ["hasErrors"] = s_HasErrors,
                });
            }
            finally { Interlocked.Decrement(ref s_Pending); }
        }

        /// beforeAssemblyReload 时给尚未写完的 recompile 响应留时间窗。
        public static void WaitForPendingResponse(int maxMs)
        {
            var sw = Stopwatch.StartNew();
            while (Volatile.Read(ref s_Pending) > 0 && sw.ElapsedMilliseconds < maxMs)
                Thread.Sleep(50);
            Thread.Sleep(200);
        }
    }
}
```

- [ ] **Step 3: TestRunnerMCP host**

`Editor/TestRunnerMCP.cs`:
```csharp
using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Yoji.TestRunner
{
    /// TestRunnerMCP HTTP 服务。端口 21890（fallback 21894/21895），只绑 127.0.0.1。
    /// 真 HTTP 状态码 + 每端点独立 JSON 形状（与 editor-debug 恒 200 flat envelope 不同）。
    /// 阶段 1 仅 EditMode；PlayMode 在 /run-tests 被拒（400）。
    [InitializeOnLoad]
    internal static class TestRunnerMCP
    {
        private const int k_StaleJobMs = 600_000; // 孤儿任务清扫阈值
        private static readonly int[] k_Ports = { 21890, 21894, 21895 };

        private static HttpListener s_Listener;
        private static int s_Port;
        private static string s_UnityVersion;
        private static string s_ProjectName;
        private static JobStore s_Jobs;
        private static TestRunService s_Service;

        static TestRunnerMCP()
        {
            EditorApplication.delayCall += Start;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.quitting += Stop;
        }

        private static void Start()
        {
            if (s_Listener != null) return;
            s_UnityVersion = Application.unityVersion;
            s_ProjectName = Application.productName;
            var projectPath = Directory.GetParent(Application.dataPath).FullName;
            var resultDir = Path.Combine(projectPath, "Temp", "TestRunnerMCP");

            s_Jobs = new JobStore(resultDir, () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            s_Service = new TestRunService(s_Jobs, resultDir);
            s_Service.EnsureRegistered(); // 域重载后重建时重注册 TestRunnerApi 回调

            foreach (var port in k_Ports)
            {
                try
                {
                    var listener = new HttpListener();
                    listener.Prefixes.Add("http://127.0.0.1:" + port + "/");
                    listener.Start();
                    s_Listener = listener;
                    s_Port = port;
                    break;
                }
                catch (Exception) { }
            }
            if (s_Listener == null)
            {
                Debug.LogError("[TestRunnerMCP] 21890/21894/21895 全部被占，服务未启动");
                return;
            }

            var thread = new Thread(Loop) { IsBackground = true, Name = "TestRunnerMCP" };
            thread.Start();
            Debug.Log("[TestRunnerMCP] 服务已启动，监听 http://127.0.0.1:" + s_Port + "/");
        }

        private static void Stop()
        {
            if (s_Listener == null) return;
            RecompileHandler.WaitForPendingResponse(2000);
            try { s_Listener.Stop(); s_Listener.Close(); } catch (Exception) { }
            s_Listener = null;
        }

        private static void Loop()
        {
            var listener = s_Listener;
            while (listener != null && listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = listener.GetContext(); }
                catch (Exception) { break; }
                ThreadPool.QueueUserWorkItem(_ => Handle(ctx));
            }
        }

        private static void Handle(HttpListenerContext ctx)
        {
            int status;
            JObject body;
            try
            {
                var path = ctx.Request.Url.AbsolutePath;
                var method = ctx.Request.HttpMethod;
                JObject req = null;
                if (method == "POST")
                {
                    using (var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
                    {
                        var raw = reader.ReadToEnd();
                        req = string.IsNullOrWhiteSpace(raw) ? new JObject() : JObject.Parse(raw);
                    }
                }
                var routed = Route(path, method, req, ctx.Request.Url.Query);
                status = routed.Item1;
                body = routed.Item2;
            }
            catch (JsonException e)
            {
                status = 400;
                body = Err("bad JSON: " + e.Message);
            }
            catch (Exception e)
            {
                status = 500;
                body = Err(e.GetType().Name + ": " + e.Message);
            }
            WriteResponse(ctx, status, body);
        }

        private static Tuple<int, JObject> Route(string path, string method, JObject req, string query)
        {
            switch (path)
            {
                case "/ping": return T(200, Ping());
                case "/recompile":
                    var rc = RecompileHandler.Run(s_Jobs);
                    return T(rc.status, rc.body);
                case "/run-tests":
                    if (method != "POST") return T(400, Err("/run-tests requires POST"));
                    return RunTests(req);
                case "/test-status": return TestStatus(query);
                default: return T(404, Err("unknown endpoint: " + path));
            }
        }

        private static Tuple<int, JObject> T(int s, JObject b) => Tuple.Create(s, b);

        private static JObject Ping()
        {
            s_Jobs.SweepStale(k_StaleJobMs);
            return new JObject
            {
                ["state"] = s_Jobs.State.ToString(),
                ["listening"] = true,
                ["timestamp"] = DateTimeOffset.Now.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz"),
                ["unityVersion"] = s_UnityVersion,
                ["projectName"] = s_ProjectName,
            };
        }

        private static Tuple<int, JObject> RunTests(JObject req)
        {
            FilterSpec spec;
            try { spec = TestFilterBuilder.Parse(req ?? new JObject()); }
            catch (ArgumentException e) { return T(400, Err(e.Message)); }

            if (spec.TestMode == "PlayMode")
                return T(400, Err("PlayMode is not supported in phase 1 (EditMode only)"));

            try
            {
                var jobId = (string)MainThreadDispatcher.Run(() => s_Service.StartRun(spec), out _);
                return T(202, new JObject
                {
                    ["success"] = true,
                    ["jobId"] = jobId,
                    ["status"] = "running",
                    ["message"] = "测试已发起，请轮询 /test-status?jobId=" + jobId,
                });
            }
            catch (InvalidOperationException)
            {
                return T(409, Err("a test run or compilation is already in progress"));
            }
            catch (Exception e)
            {
                return T(400, Err("failed to start run: " + e.Message));
            }
        }

        private static Tuple<int, JObject> TestStatus(string query)
        {
            s_Jobs.SweepStale(k_StaleJobMs);
            var jobId = QueryParam(query, "jobId");
            var rec = string.IsNullOrEmpty(jobId) ? s_Jobs.ActiveOrLast() : s_Jobs.Find(jobId);
            if (rec == null) return T(404, Err("no such job: " + (jobId ?? "<none active or cached>")));
            return T(200, JobJson(rec));
        }

        private static JObject JobJson(JobRecord r)
        {
            var o = new JObject
            {
                ["jobId"] = r.JobId,
                ["status"] = r.Status,
                ["message"] = r.Message,
            };
            if (r.Status == "completed")
            {
                o["resultFilePath"] = r.ResultFilePath;
                o["overallResult"] = r.OverallResult;
                o["passed"] = r.Passed;
                o["failed"] = r.Failed;
                o["skipped"] = r.Skipped;
            }
            return o;
        }

        private static string QueryParam(string query, string key)
        {
            if (string.IsNullOrEmpty(query)) return null;
            var q = query.StartsWith("?") ? query.Substring(1) : query;
            foreach (var pair in q.Split('&'))
            {
                var kv = pair.Split(new[] { '=' }, 2);
                if (kv.Length == 2 && Uri.UnescapeDataString(kv[0]) == key)
                    return Uri.UnescapeDataString(kv[1]);
            }
            return null;
        }

        private static JObject Err(string message) => new JObject { ["success"] = false, ["error"] = message };

        private static void WriteResponse(HttpListenerContext ctx, int status, JObject body)
        {
            var bytes = Encoding.UTF8.GetBytes(body.ToString(Formatting.None));
            try
            {
                ctx.Response.StatusCode = status;
                ctx.Response.ContentType = "application/json; charset=utf-8";
                ctx.Response.ContentLength64 = bytes.Length;
                ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
                ctx.Response.OutputStream.Close();
            }
            catch (Exception) { }
        }
    }
}
```

- [ ] **Step 4: 编译验证**

```powershell
Wait-UnityGone; Run-EditModeTests
```
Expected: 退出码 0，全部单测 PASS；unity.log 应见 `[TestRunnerMCP] 服务已启动`（batchmode 下 delayCall 也会触发，无害）。grep `warning CS` 看新文件是否有警告，报告。

- [ ] **Step 5: Commit**

```powershell
git -C $REPO add -A; git -C $REPO commit -m "feat(test-runner): HTTP host, main-thread dispatch, recompile, real status codes"
```

---

### Task 7: e2e 验收（夹具 + client.py + run-e2e.py + 实机跑）

**Files:**
- Create: `Packages/com.yoji.test-runner/Tests/Editor/FixtureTests.cs`
- Create: `Packages/com.yoji.test-runner/Agent~/skills/test-runner-mcp/client.py`
- Create: `Packages/com.yoji.test-runner/Agent~/skills/test-runner-mcp/references/run-e2e.py`

- [ ] **Step 1: e2e 夹具测试（[Explicit] 使全套件跳过、服务按名精确选才跑）**

`Tests/Editor/FixtureTests.cs`:
```csharp
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
```

- [ ] **Step 2: client.py**

`Agent~/skills/test-runner-mcp/client.py`:
```python
#!/usr/bin/env python3
"""TestRunnerMCP 客户端 CLI。

通过 HTTP 调本地 Unity Editor 内的 TestRunnerMCP 服务。全局 flag（--host/--port/--timeout）
必须放在子命令之前，例如：python client.py --port 21894 ping。
"""
from __future__ import annotations

import argparse
import json
import sys
import urllib.error
import urllib.request

DEFAULT_HOST = "127.0.0.1"
DEFAULT_PORT = 21890


def http_get(url: str, timeout: float):
    try:
        with urllib.request.urlopen(url, timeout=timeout) as r:
            return r.getcode(), json.loads(r.read().decode("utf-8"))
    except urllib.error.HTTPError as e:
        try:
            return e.code, json.loads(e.read().decode("utf-8"))
        except Exception:
            return e.code, {"error": str(e)}
    except urllib.error.URLError as e:
        return 0, {"error": str(e.reason)}


def http_post(url: str, payload: dict, timeout: float):
    body = json.dumps(payload).encode("utf-8")
    req = urllib.request.Request(
        url, data=body,
        headers={"Content-Type": "application/json; charset=utf-8"}, method="POST")
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            return r.getcode(), json.loads(r.read().decode("utf-8"))
    except urllib.error.HTTPError as e:
        try:
            return e.code, json.loads(e.read().decode("utf-8"))
        except Exception:
            return e.code, {"error": str(e)}
    except urllib.error.URLError as e:
        return 0, {"error": str(e.reason)}


def base(a) -> str:
    return f"http://{a.host}:{a.port}"


def cmd_ping(a):
    return http_get(f"{base(a)}/ping", a.timeout)


def cmd_recompile(a):
    return http_get(f"{base(a)}/recompile", a.timeout)


def cmd_run(a):
    payload = {"testMode": a.mode}
    if a.names:
        payload["testNames"] = a.names
    if a.assemblies:
        payload["assemblyNames"] = a.assemblies
    if a.categories:
        payload["categoryNames"] = a.categories
    return http_post(f"{base(a)}/run-tests", payload, a.timeout)


def cmd_status(a):
    url = f"{base(a)}/test-status"
    if a.job_id:
        url += f"?jobId={a.job_id}"
    return http_get(url, a.timeout)


def build_parser():
    p = argparse.ArgumentParser(prog="client.py", description="TestRunnerMCP CLI client")
    p.add_argument("--host", default=DEFAULT_HOST)
    p.add_argument("--port", type=int, default=DEFAULT_PORT)
    p.add_argument("--timeout", type=float, default=30.0)
    sub = p.add_subparsers(dest="cmd", required=True)

    sub.add_parser("ping", help="连通性 + state").set_defaults(func=cmd_ping)
    sub.add_parser("recompile", help="触发重编译并等待完成").set_defaults(func=cmd_recompile)

    r = sub.add_parser("run-tests", help="发起测试（异步，立即返回 jobId）")
    r.add_argument("--mode", default="EditMode", choices=["EditMode", "PlayMode"])
    r.add_argument("--names", nargs="*", help="完整测试名 Namespace.Class.Method；全空 = 跑全套件（run-all 扩展）")
    r.add_argument("--assemblies", nargs="*", help="按程序集名跑（run-all 扩展）")
    r.add_argument("--categories", nargs="*", help="按 NUnit Category 跑（run-all 扩展）")
    r.set_defaults(func=cmd_run)

    s = sub.add_parser("status", help="轮询任务状态 / 取最近结果")
    s.add_argument("--job-id", help="省略则返回活跃任务或最近一次缓存")
    s.set_defaults(func=cmd_status)
    return p


def main() -> int:
    args = build_parser().parse_args()
    status, body = args.func(args)
    json.dump({"httpStatus": status, "body": body}, sys.stdout, ensure_ascii=False, indent=2)
    sys.stdout.write("\n")
    return 0 if 200 <= status < 300 else 1


if __name__ == "__main__":
    sys.exit(main())
```

- [ ] **Step 3: run-e2e.py**

`Agent~/skills/test-runner-mcp/references/run-e2e.py`:
```python
#!/usr/bin/env python3
"""TestRunnerMCP 端到端冒烟。

前提：Unity Editor 已打开 TestProjects/test-runner 工程，服务在 21890。
用法：python run-e2e.py [--port 21890] [--include-recompile] [-v]
"""
from __future__ import annotations

import argparse
import json
import sys
import time
import urllib.error
import urllib.request

PASS_FIX = "Yoji.TestRunner.Tests.FixtureTests.AlwaysPasses"
FAIL_FIX = "Yoji.TestRunner.Tests.FixtureTests.AlwaysFails"


def get(base, path, timeout=10):
    try:
        with urllib.request.urlopen(f"{base}{path}", timeout=timeout) as r:
            return r.getcode(), json.loads(r.read().decode("utf-8"))
    except urllib.error.HTTPError as e:
        try:
            return e.code, json.loads(e.read().decode("utf-8"))
        except Exception:
            return e.code, {}
    except urllib.error.URLError as e:
        return 0, {"error": str(e.reason)}


def post(base, path, payload, timeout=15):
    data = json.dumps(payload).encode("utf-8")
    req = urllib.request.Request(
        f"{base}{path}", data=data,
        headers={"Content-Type": "application/json"}, method="POST")
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            return r.getcode(), json.loads(r.read().decode("utf-8"))
    except urllib.error.HTTPError as e:
        try:
            return e.code, json.loads(e.read().decode("utf-8"))
        except Exception:
            return e.code, {}
    except urllib.error.URLError as e:
        return 0, {"error": str(e.reason)}


def run_and_wait(base, payload, timeout_s=120):
    code, body = post(base, "/run-tests", payload)
    if code != 202:
        return code, body, None
    job = body.get("jobId")
    for _ in range(timeout_s):
        _, s = get(base, f"/test-status?jobId={job}")
        if s.get("status") != "running":
            return code, body, s
        time.sleep(1)
    return code, body, {"status": "timeout"}


def main(argv):
    ap = argparse.ArgumentParser()
    ap.add_argument("--port", type=int, default=21890)
    ap.add_argument("--include-recompile", action="store_true")
    ap.add_argument("-v", "--verbose", action="store_true")
    args = ap.parse_args(argv[1:])
    base = f"http://127.0.0.1:{args.port}"

    code, ping = get(base, "/ping", timeout=5)
    if code != 200:
        print(f"[FATAL] /ping failed: {ping}")
        return 2
    print(f"service state={ping.get('state')} unity={ping.get('unityVersion')} project={ping.get('projectName')}\n")

    results = []

    def check(name, ok, detail=""):
        results.append(ok)
        print(f"  [{'PASS' if ok else 'FAIL'}] {name}" + (f"  -> {detail}" if not ok else ""))

    check("01 /ping state==Idle", ping.get("state") == "Idle", f"state={ping.get('state')}")

    _, _, st = run_and_wait(base, {"testMode": "EditMode", "testNames": [PASS_FIX]})
    check("02 run passing test -> completed passed==1",
          bool(st) and st.get("status") == "completed" and st.get("passed") == 1 and st.get("failed") == 0,
          f"st={st}")

    _, _, st = run_and_wait(base, {"testMode": "EditMode", "testNames": [FAIL_FIX]})
    check("03 run failing test -> failed==1 overall Failed",
          bool(st) and st.get("status") == "completed" and st.get("failed") == 1 and st.get("overallResult") == "Failed",
          f"st={st}")

    code, _ = post(base, "/run-tests", {"testNames": [PASS_FIX]})
    check("04 missing testMode -> 400", code == 400, f"code={code}")

    code, _ = post(base, "/run-tests", {"testMode": "PlayMode", "testNames": [PASS_FIX]})
    check("05 PlayMode -> 400 (phase 1)", code == 400, f"code={code}")

    code, _ = get(base, "/test-status?jobId=deadbeefdeadbeefdeadbeefdeadbeef")
    check("06 unknown jobId -> 404", code == 404, f"code={code}")

    _, _, st = run_and_wait(base, {"testMode": "EditMode"}, timeout_s=180)
    check("07 run-all EditMode -> completed passed>0 failed==0",
          bool(st) and st.get("status") == "completed" and (st.get("passed") or 0) > 0 and st.get("failed") == 0,
          f"st={st}")

    if args.include_recompile:
        code, body = get(base, "/recompile", timeout=200)
        check("R1 /recompile -> success", code == 200 and body.get("success") is True, f"code={code} body={body}")

    passed = sum(1 for ok in results if ok)
    print(f"\n== result: {passed} passed, {len(results) - passed} failed ==")
    return 0 if passed == len(results) else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv))
```

- [ ] **Step 4: 全套件单测仍绿 + 夹具被排除**

```powershell
Wait-UnityGone; Run-EditModeTests
```
Expected: 退出码 0；results.xml 中 **AlwaysFails 不出现**（[Explicit] 被全套件跳过）。若 AlwaysFails 竟被运行并失败 -> [Explicit] 在本版未生效，回退方案：把夹具移到 `TestProjects/test-runner/Assets/Fixture/`（独立 asmdef，配 `!TestProjects/test-runner/Assets/` 反 ignore），并在报告说明。

- [ ] **Step 5: 启交互式 Editor + 轮 /ping**

```powershell
Wait-UnityGone
Start-Process $UNITY -ArgumentList "-projectPath", $TP, "-logFile", "$TP\TestResults\e2e-editor.log"
$skill = "$REPO\Packages\com.yoji.test-runner\Agent~\skills\test-runner-mcp"
$deadline = (Get-Date).AddMinutes(6); $ok = $false
do { Start-Sleep 5; try { $ok = (python "$skill\client.py" ping 2>$null | ConvertFrom-Json).body.state -eq "Idle" } catch { $ok = $false } } until ($ok -or (Get-Date) -gt $deadline)
"ping idle=$ok"
```
超时则读 `e2e-editor.log` 尾 60 行排查（许可弹窗 / 编译错 / 端口冲突 / `[TestRunnerMCP]` 日志）。

- [ ] **Step 6: 跑 e2e（7 用例）**

```powershell
python "$skill\references\run-e2e.py" --verbose
```
Expected: `== result: 7 passed, 0 failed ==`。失败逐条对照 SKILL.md 契约修 C#（改完 `python "$skill\client.py" recompile` 让 Editor 重编译，等 ping 恢复再重跑），不改 Python 用例。

- [ ] **Step 7: recompile 用例**

```powershell
python "$skill\references\run-e2e.py" --include-recompile --verbose
```
Expected: `8 passed, 0 failed`（R1 触发域重载，服务短暂下线属预期）。

- [ ] **Step 8: 关 Editor**

```powershell
Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" | Where-Object { $_.CommandLine -match "TestProjects.test-runner" } | ForEach-Object { Stop-Process -Id $_.ProcessId }
```
（test-runner 服务无 EditorApplication.Exit 端点，直接结束该工程的 Unity 进程；只杀匹配 test-runner 工程的，勿动其它。）

- [ ] **Step 9: Commit（含 e2e 期间的 C# 修正）**

```powershell
git -C $REPO add -A; git -C $REPO commit -m "test(test-runner): e2e fixture, client.py, run-e2e.py; pass full suite"
```

---

### Task 8: 文档与 skill 迁移

**Files:**
- Move: `test-runner-mcp/SKILL.md` -> `Packages/com.yoji.test-runner/Agent~/skills/test-runner-mcp/SKILL.md`
- Create: `Packages/com.yoji.test-runner/Agent~/skills/test-runner-mcp/install.ps1`
- Create: `Packages/com.yoji.test-runner/Agent~/fragments/CLAUDE.md`
- Create: `Packages/com.yoji.test-runner/Agent~/fragments/AGENTS.md`
- Modify: `README.md`

- [ ] **Step 1: 迁移 SKILL.md**

```powershell
git -C $REPO mv test-runner-mcp/SKILL.md "Packages/com.yoji.test-runner/Agent~/skills/test-runner-mcp/SKILL.md"
Remove-Item -Recurse -Force "$REPO\test-runner-mcp" -ErrorAction SilentlyContinue
```
（client.py 与 references/run-e2e.py 已在 Task 7 落到同一 skill 目录。）

- [ ] **Step 2: 改写 SKILL.md 的「安装」「契约扩展」「现状」三处**

SKILL.md 中：
1. 顶部 IMPORTANT 块（现说"仓库无 client.py / 无 Unity 侧源码 / 指向 Tap4fun 私有仓库"）改为：本工具已具备公开 Unity 侧实现（`com.yoji.test-runner`，阶段 1 仅 EditMode），随包附 client.py + references/run-e2e.py。
2. 「安装」节：把 `"com.tfw.test-runner-mcp": "ssh://...tap4fun..."` 改为
   ```json
   "com.yoji.test-runner": "file:<path-to-repo>/Packages/com.yoji.test-runner"
   ```
   或拷 `Packages/com.yoji.test-runner/` 进目标工程 `Packages/`。Console 出现 `[TestRunnerMCP] 服务已启动` 即就绪。
3. 新增「契约扩展（本实现相对原规范）」小节，写明三条：
   - **run-all**：`/run-tests` 请求体新增可选 `assemblyNames` / `categoryNames` / `groupNames`；当 testNames 与这些字段全空时跑该 testMode 全套件（原规范空 testNames 返 400，本实现放宽）。
   - **PlayMode 阶段 2**：阶段 1 仅支持 EditMode，`testMode:"PlayMode"` 返 400 并在 message 说明；PlayMode（含域重载存活）留阶段 2。
   - **真 HTTP 状态码**：本实现用 200/202/400/404/409 真状态码（非恒 200），错误 body 形为 `{success:false, error:"..."}`。

- [ ] **Step 3: install.ps1（镜像 editor-debug，把 skill 同步到 ~/.claude/skills）**

`Agent~/skills/test-runner-mcp/install.ps1`:
```powershell
# 把本 Skill 同步到 ~/.claude/skills/test-runner-mcp/，让 Claude Code 加载。
# 每次改完 SKILL.md / client.py / references 后跑一次。用法：PowerShell 跑 .\install.ps1
$ErrorActionPreference = "Stop"
$srcDir = $PSScriptRoot
$dstDir = Join-Path $env:USERPROFILE ".claude\skills\test-runner-mcp"
Write-Host "src: $srcDir"
Write-Host "dst: $dstDir"
if (Test-Path $dstDir) { Remove-Item -Recurse -Force $dstDir }
New-Item -ItemType Directory -Path $dstDir | Out-Null
$refDst = Join-Path $dstDir "references"
New-Item -ItemType Directory -Path $refDst | Out-Null
Copy-Item -Path (Join-Path $srcDir "SKILL.md") -Destination $dstDir
Copy-Item -Path (Join-Path $srcDir "client.py") -Destination $dstDir
$refSrc = Join-Path $srcDir "references"
if (Test-Path $refSrc) {
    Get-ChildItem -Path $refSrc -File | ForEach-Object { Copy-Item $_.FullName -Destination $refDst }
}
Write-Host "done."
Get-ChildItem -Path $dstDir -Recurse -File | ForEach-Object { Write-Host ("  " + $_.FullName) }
```

- [ ] **Step 4: fragments（按 Linker 设计，最小内容）**

`Agent~/fragments/CLAUDE.md`:
```markdown
## test-runner-mcp

Unity Editor 在线时跑 EditMode 测试的 HTTP 服务（端口 21890）。流程：/ping 确认 Idle
-> /recompile 等编译 -> POST /run-tests 拿 jobId -> 轮询 /test-status。client.py 全局
flag 放子命令前。阶段 1 仅 EditMode（PlayMode 返 400）。空 testNames = 跑全套件。
```

`Agent~/fragments/AGENTS.md`: 同 CLAUDE.md 内容（镜像）。

- [ ] **Step 5: 更新 README 状态表与小节**

`README.md`：test-runner-mcp 行的「Unity-side service in this repo」改为 Yes（`Packages/com.yoji.test-runner`），状态改为 "Usable (EditMode only; PlayMode planned)"。小节里把"仅接口规范 / 不含 client.py 与服务"改为已含公开 UPM 包 + client.py + run-e2e.py，端口路径同步为 `Packages/com.yoji.test-runner/Agent~/skills/test-runner-mcp/...`。保留 feval-runtime-debug 仍 client-only 的说明。

- [ ] **Step 6: 跑 install.ps1 验证 + Commit**

```powershell
& "$REPO\Packages\com.yoji.test-runner\Agent~\skills\test-runner-mcp\install.ps1"
Test-Path "$env:USERPROFILE\.claude\skills\test-runner-mcp\SKILL.md"
git -C $REPO add -A; git -C $REPO commit -m "docs(test-runner): migrate skill into package, document run-all + EditMode-only, README"
```

---

## 验收总清单

1. `Run-EditModeTests` 全 PASS（JobStore + NUnitResultMapper + TestFilterBuilder 单测；AlwaysFails 不被全套件运行）
2. `run-e2e.py` 7 用例全 PASS；`--include-recompile` 8 用例全 PASS
3. `git grep "com.tfw.test-runner"` 仅在历史 plans/specs 命中（README 与 SKILL 已改 com.yoji）
4. 服务与 editor-debug 共存：21890 vs 21891 不冲突
5. 仓库根无散落新 .md（计划在 docs/superpowers/plans/）

## 已知限制与阶段 2

| 项 | 阶段 1 状态 | 阶段 2 |
|----|-----------|--------|
| PlayMode 测试 | /run-tests 返 400 | DisableDomainReload + SessionState 任务存储 + [InitializeOnLoad] 重注册 + RunFinished 收尾 |
| 域重载存活 | EditMode 进程内跑无需 | PlayMode/recompile 跨重载的 job 取回 |
| 并发 409 去歧义 | state 区分 Running/Compiling | 卡死自动复位/超时 |
| testNames 精确匹配 0 命中 | 静默 0 测试 | RetrieveTestList 预校验 + groupNames 正则兜底 |
| /recompile 响应竞态 socket flush | 复用 WaitForPendingResponse 排空窗 | sentinel 文件 + client 轮 ping 兜底 |

## 已知风险与回退

| 风险 | 信号 | 处理 |
|------|------|------|
| Unity 6.3 移除实例版 Execute/RegisterCallbacks | Task 5 硬编译错误 | 改静态 `TestRunnerApi.ExecuteTestRun`/`RegisterTestCallback`（见 Task 5 注） |
| [Explicit] 测试被全套件运行 | Task 7 Step 4 AlwaysFails 失败 | 夹具移到 TestProjects/test-runner/Assets/Fixture + 反 ignore（见 Task 7 注） |
| testNames 选不中 [Explicit] 测试 | e2e case 02/03 跑出 0 测试 | 去掉 [Explicit] 改 [Category] + 包单测用 category 过滤；或夹具独立工程 |
| ToXml 需 nunit.framework 未解析 | Task 5 编译错 type TNode | 确认 Editor asmdef precompiledReferences 含 nunit.framework.dll |
| com.unity.test-framework 版本不符 | Task 1 包解析失败 | 对齐测试工程实际拉取的 test-framework 版本号 |
