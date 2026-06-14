# Unity AI Dev-Tools 增强设计（editor-debug / test-runner + editor-core 抽取）

## 背景与动机

`com.yoji.editor-debug` 与 `com.yoji.test-runner` 已经落地并被 AI agent 在真实工程 `Core_Client`（Unity 6.3 LTS + Lua/xLua SLG 客户端）中实际使用。两者构成 agent 的「在线编辑器」操作面：editor-debug 通过 HTTP+JSON 反射读编辑器状态（`/invoke`、`/describe`、`/eval`、`/recompile`、`/ping`），test-runner 通过 HTTP 在不开 GUI 的情况下触发重编译、跑 EditMode 测试、轮询结果（`/recompile`、`/run-tests`、`/test-status`、`/ping`）。它们服务于 tdd-workflow 的「执行测试」和「读编译错误」环节。

在真实使用中暴露出的关键缺口，全部已对照源码核实：

- **test-runner 只能验证纯 C# helper，碰不到运行时行为。** PlayMode 在 `TestRunnerMCP.cs:157-158` 被硬拒（返 400），`TestRunService.BuildFilter`（`TestRunService.cs:56`）写死 `TestMode.EditMode` 并完全忽略 `spec.TestMode`。对一个 SLG 客户端，MonoBehaviour 生命周期、协程、物理、场景加载、UI 控制器这些「最该测」的集成形测试全部落在 PlayMode，agent 现在完全够不到。SKILL.md:22 已明确把 PlayMode 标为「阶段 2」承诺。
- **测试失败后 agent 拿不到「为什么失败」。** `/test-status` 只回聚合计数 + `resultFilePath`（`TestRunnerMCP.cs:189-207`）；`ICallbacks.TestFinished` 是空实现（`TestRunService.cs:68`），每个叶子的 `Message`/`StackTrace`/`FullName` 被丢弃。agent 要知道哪个用例、哪一行挂了，必须自己去 parse NUnit XML。这正是 TDD 闭环里决定「下一步改哪」的信息。
- **拼错 testName 会假绿。** `BuildFilter` 把 `spec.TestNames` 原样转给 Unity，命中 0 个用例时 Unity 跑空，`RunFinished` 算出 `passed=0 failed=0` -> `NUnitResultMapper` 判为 `Passed`。agent 以为测试跑过了，实际什么都没跑——自动化测试门禁最危险的失败模式。
- **agent 读不到它自己制造的编译/运行时错误。** editor-debug 只能调到 `LogEntries.GetCount`，读不到条目本身。`/recompile` 只回 `hasErrors:bool`，不回是哪条 `CS0103 in Foo.cs:42`。这是 agent 当前最大的盲区。
- **agent 不知道编辑器当前处于什么状态。** `/ping`（`EditorDebugMCP.cs:126-133`）不暴露 `isPlaying`/`isPaused`/`isCompiling`，agent 无法判断它读到的是运行时态还是编辑态，也无法在编译中途退避轮询。
- **两个包跨包抢端口。** test-runner 的 fallback 端口数组 `{ 21890, 21894, 21895 }`（`TestRunnerMCP.cs:20`）与 lua-device-debug 的固定端口 `DefaultPort = 21894`（`LuaDeviceDebugPackage.cs:7`，且 lua-device-debug 无 fallback、硬钉死）冲突。本 monorepo 同时交付这三个包，一旦 21890 被占就静默撞车。
- **基础设施在三个包里重复维护。** `MainThreadDispatcher.cs` 在 editor-debug 与 test-runner 之间实质字节一致（经 diff 核实，仅 namespace + 2 行注释不同）；HTTP listener 外壳与 recompile 编译事件脚手架近乎逐行复制，约 150–200 行并行维护——而这恰恰是并发竞态最微妙、改一处另一处会静默腐化的代码。

本设计对上述缺口逐项做价值/成本评估，给出值得做项的设计草案、分阶段路线图和明确的非目标。所有结论以源码为准，不夸大成本与风险。

## 价值/成本评估

下表 value 与 effort 均为 1–5（5 最高/最贵）。结论分 do-now / do-next / later / skip。

| 项 | 标题 | value | effort | 结论 |
|---|---|---|---|---|
| TR-2 | 捕获每用例失败详情，`/test-status` 直出 `failures[]` | 5 | 2 | do-now |
| TR-4 | 移除 21894，消除与 lua-device-debug 撞端口 | 3 | 1 | do-now |
| ED-1 | `/console` 端点：读真实日志条目（message/type/stack） | 5 | 2 | do-now |
| ED-5 | `/ping` 暴露 `isPlaying`/`isPaused`/`isCompiling` | 4 | 1 | do-now |
| ED-3* | `--usings` 死参数诚实化（实现或删除），拒绝 Roslyn | 2 | 1 | do-now（仅清理）|
| ARCH-1a | `MainThreadDispatcher` 移入 editor-core（字节一致，零风险）| 3 | 1 | do-now |
| TR-3 | 0 命中判错 + `/list-tests` 发现端点 | 4 | 3 | do-next |
| ED-2 | `/batch`：N 个 invoke 一次主线程跳 | 3 | 2 | do-next |
| TR-1a | 强制 `EnterPlayModeOptions.DisableDomainReload` 走通 PlayMode | 4 | 2 | do-next（先 spike）|
| ARCH-1b | 抽 `LoopbackHttpHost` + `RecompilePrimitive`（保留 per-tool 信封）| 3 | 3 | do-next（重 TR-1b/c 之前）|
| TR-1b | `SessionState` 跨域重载保存 active jobId | 3 | 3 | later（条件触发）|
| TR-1c | 域重载后重挂回调 + 初始化超时收尾 | 3 | 3 | later（条件触发，confidence 低）|
| ED-4 | 只读模式 / 方法 denylist（opt-in，默认关）| 3 | 2 | later |
| ED-6 | `/describe` 成员过滤 + 多类型批量 describe | 3 | 2 | later |
| TR-5 | `/cancel` 中止在跑任务 | 2 | 3 | skip |
| ED-3 | Roslyn 脚本化 `/eval` | 2 | 5 | skip |

### 评分理由（基于落地分析）

**TR-2（5/2，do-now）** —— agent 消费方最高杠杆。TDD 循环里下一步动作完全由「为什么挂」决定：断言信息 + 栈帧 -> 改哪个文件哪一行。数据已经在手里被丢掉（`TestFinished` 空实现 + `RunFinished` 只读聚合），补回是纯加法、向后兼容。effort 低：3 个文件、asmdef 已引用 `UnityEngine.TestRunner`，无新依赖；唯一坑是 suite/leaf 双计数（按 `HasChildren==false` 过滤）。

**TR-4（3/1，do-now）** —— 一行修正消除真实的跨包静默撞车，成本可忽略。lua-device-debug 钉死 21894 且无 fallback，冲突是非对称的，必修。

**ED-1（5/2，do-now）** —— 移除 agent 最大盲区：它读不到自己造的错误。通过 `/invoke` 泛化路径真的够不到（`ReflectionInvoker.DoCall` 按值 coerce 参数，无法构造并 by-ref 回填 `LogEntry`），所以必须专端点。effort 低，自包含，复用现有信封与主线程 dispatch。

**ED-5（4/1，do-now）** —— 约 15 行换来 agent 的「态势感知」。知道 `isPlaying` 改变对后续每次读的解释；知道 `isCompiling` 让 agent 退避而非拿到编译中途的脏反射结果。

**TR-3（4/3，do-next）** —— 杀假绿，重要性仅次于 TR-2，且与之共用 e2e seam。比 TR-2 多一点管线（`/list-tests` 的 `RetrieveTestList` 是异步主线程回调，需 marshalling 回 HTTP 线程），故排其后。

**TR-1 / PlayMode（umbrella，5/2~5，do-next）** —— Unity 测试面的约一半，是「能验纯 C#」与「能验真实运行时游戏行为」的分水岭，合同已承诺。但它是最难的项：核心未知是「pre-reload 发起的 PlayMode run，其 `RunFinished` 是否会在 post-reload 的新 domain 上触发」，这无法单测（runner-runs-itself），必须 live e2e 验证。两条路：TR-1a（DisableDomainReload，便宜、绕开整个重载难题，工程离它只差一个 flag，effort 2）vs TR-1b+TR-1c（SessionState + 重挂，effort 4–5，且赌上述未验证假设）。

**ED-2（3/2，do-next）** —— 把多读延迟从 N 次往返压到 1 次，编辑器里每跳主线程要等一帧。真实便利，但 invoke-chain 已覆盖最常见的「逐层下钻」，batch 主要帮 fan-out 场景，故优先级低于 ED-1。

**ARCH-1（3/3，do-next，拆分）** —— 家族变大后的可维护性，集中在几条断层线（dispatcher 重入、recompile 竞态窗口、端口 fallback）。dispatcher 部分（字节一致）零风险，应作为独立小提交立刻做；host 外壳 + recompile primitive 是真正的活，约 1–1.5 天含测试回归。**绝不能把信封并进 core**——editor-debug 恒 200 flat 与 test-runner 真状态码是刻意的协议契约。

**ED-4（3/2，later）** —— 防御纵深，不是关键漏洞（仅绑 127.0.0.1）。autonomous agent 误调破坏性 API 的概率确实高于人，但 denylist 天然有漏。opt-in 默认关，镜像现有 `c_AllowEval` kill-switch。

**ED-6（3/2，later）** —— discovery 阶段的 token/延迟优化。describe 使用频率低于 invoke，故 later。若 ED-2 落地，batch-describe 几乎免费附带。

**TR-5（2/3，skip）** —— `TestRunnerApi` 无受支持的 abort，忠实 cancel 基本不可实现；state-only shim 会对 agent 撒谎（引擎仍在跑）。EditMode run 很短，409 阻塞窗口小，stale sweeper 已能回收。真要降低楔死延迟，把 `k_StaleJobMs` 调小/可配即可。PlayMode 落地后再议。

**ED-3 Roslyn（2/5，skip）** —— 缺口真实但 80% 已被 invoke-chain 覆盖。Roslyn 是多 MB 重依赖 + 编辑器 assembly-resolution 痛点 + 域重载重入 + 把调试端口变成任意代码执行面。对一个面向读的本地工具，性价比差。只做诚实化：`--usings`（`client.py:182` 当前是 no-op）实现或删除，停止宣传死参数。

## 设计草案（值得做项）

### TR-2 每用例失败详情

目标：`/test-status` 在 `status==completed` 时直出 `failures:[{name,message,stackTrace}]`，agent 无需读 XML。

1. `JobStore.cs`：`JobRecord` 加 `public List<FailureDetail> Failures;`，新增可序列化 `FailureDetail { Name; Message; StackTrace; }`，随现有 `JsonConvert` Persist/Load 自动 round-trip。
2. `TestRunService.cs`：实现当前为空的 `TestFinished(ITestResultAdaptor r)`（line 68），收集 `r.TestStatus==TestStatus.Failed && r.HasChildren==false`（仅叶子，suite 也报 Failed 会双计数）的 `r.Test.FullName / r.Message / r.StackTrace` 进 per-run list；`StartRun` 重置该 list。
3. 扩 `JobStore.CompleteJob(...)` 签名带入该 list，落到 `JobRecord`（涉及 `JobStoreTests.cs` 约 5 处调用点）。
4. `TestRunnerMCP.JobJson`：`completed` 且 `Failures` 非空时 emit `o["failures"]`。
5. 上限保护：数组截前 50 条、`stackTrace` 截断长度，避免 run-all 大面积失败撑爆响应。

风险低，纯加法；passing run 不变（空数组省略）。e2e：扩 `run-e2e.py` case 03（已跑 `FixtureTests.AlwaysFails`）断言 `failures[0].message` 含预期串。

### TR-3 0 命中判错 + `/list-tests`

(a) **0 命中守卫**：实现当前为空的 `RunStarted(ITestAdaptor testsToRun)`（`TestRunService.cs:66`），遍历 `testsToRun` 数实际叶子用例数；在 active run 上记 `IsRunAll` 以区分「故意空（run-all）」与「意外空（typo）」。若 `spec` 非 run-all（`TestNames/Assembly/Category/Group` 任一非空）但计划叶子数为 0，则 `m_Jobs.FailJob(jobId, "testNames matched 0 tests")`，`/test-status` 返 `status=error / overallResult=Error`。**必须同时阻止后续 `RunFinished` 覆盖该 terminal error**：`RunStarted` 判 0 命中后清掉 `m_ActiveJobId` 或记录 `m_IgnoreRunFinishedJobId`，使晚到的 `RunFinished` 直接 return；否则现有 `JobStore.Target(jobId)` 会复用同 id 的 `m_Last`，`CompleteJob` 把 error 改回 completed/Passed，反而制造第二层假绿。比在 `StartRun` 里先 `RetrieveTestList` 预校验简单（后者异步，复杂化同步 `StartRun`）。注意 `[Explicit]` fixture 的枚举语义需快速核（本包 `FixtureTests` 即 `[Explicit]`）。

(b) **`/list-tests` 发现端点**：`Route`（`TestRunnerMCP.cs:124-136`）加 `case "/list-tests"`，从 QueryString 取 `mode`（默认 EditMode，PlayMode 像 `/run-tests` 一样拒），在 `TestRunService` 上包一层调 `m_Api.RetrieveTestList(mode, root => ...)`，主线程跑、用 `ManualResetEvent` 或现有 `MainThreadDispatcher` 模式 marshal 回 HTTP 线程，扁平化叶子 `FullName` 成 `JArray`，返 `200 {tests:[...], count:N}`。大套件只回名字、不回 metadata。

设计文档须记一条相邻语义：`NUnitResultMapper.OverallResult` 把 `failed==0` 无条件判 `Passed`，所以「合法空 run（全 `[Explicit]`）」也会报 Passed——TR-3 守卫修的是 typo 情形，agent 仍应把「非 run-all 却 `passed==0`」视为可疑。

### TR-4 端口修正

`TestRunnerMCP.cs:20` 改为 `{ 21890, 21896, 21897 }`（确认 21896/21897 无第三个兄弟包占用；editor-debug 用 21891-21893）。同步改所有人面字符串：`TestRunnerMCP.cs:63` 的 LogError、class 顶部 XML doc（line 13）、`Agent~/skills/test-runner-mcp/client.py` 顶部示例里误导性的 `--port 21894`、包内 `SKILL.md` 服务地址说明、repo 根 `README.md` 端口表。否则 agent 文档仍会把人引向冲突端口。lua-device-debug 的固定 21894 不动。无测试依赖 fallback 值。

### TR-1 PlayMode（分路径）

**先 spike TR-1a。** `EditorSettings.asset` 已是 `m_EnterPlayModeOptionsEnabled: 1` 但 `m_EnterPlayModeOptions: 0`（None），即 fast-enter-playmode 已开、DisableDomainReload flag 未置。做法：`spec.TestMode == "PlayMode"` 时，进 run 前同时 snapshot 用户当前 `EditorSettings.enterPlayModeOptionsEnabled` 与 `EditorSettings.enterPlayModeOptions`，临时设 `enterPlayModeOptionsEnabled = true`，并用 `oldOptions | EnterPlayModeOptions.DisableDomainReload` 叠加 flag（不要直接赋值丢掉用户已有选项；`DisableSceneReload` 只作为可选 spike 变量）。`RunFinished` 时完整还原两个设置；同时 `BuildFilter` 按 `spec.TestMode` 选 `TestMode.PlayMode`；移除 `TestRunnerMCP.cs:157-158` 的 400。因为不发生重载，现有 field-based `m_ActiveJobId` 与 `EnsureRegistered/RunFinished` 路径原样工作。务必 per-run scoped 改设置（非项目级），并对「`RunFinished` 不触发」加超时还原，避免污染用户 Editor 配置。前置守卫：`EditorApplication.isPlaying` 或脏未存场景时拒（409/400）。SKILL.md 注明 caveat：依赖静态状态跨域重置的测试在 DisableDomainReload 下行为不同（test-authoring 问题）。

**仅当 DisableDomainReload 被判不可接受、且 live spike 证明 `RunFinished` 跨重载存活，才建 TR-1b + TR-1c：**

- **TR-1b**：单一 `SessionState` key（如 `"Yoji.TestRunner.ActiveJobId"`）在 `StartRun` 写、`RunFinished` 读、收尾 erase（SessionState 跨域重载存活、Editor 重启自清，生命周期正好）。镜像进 `JobStore`：构造时若 SessionState 有 jobId 且 `LoadFromDisk(jobId)` 显示 `status==running`，重新认领为 `m_Current`，使 `State==Running` 且 `SweepStale` 能看见它（现 `SweepStale` 只看内存 `m_Current`，重载后为 null 故对孤儿全盲）。`RunFinished` 从 SessionState 读 jobId 而非丢失的 field。为可单测，把 SessionState 包成可注入接口。
- **TR-1c**：`TestRunnerMCP.Start` 已在每次 `[InitializeOnLoad]` 调 `EnsureRegistered()`（line 46），fresh `ICallbacks` 已注册——缺的只是「它没有存活 jobId 可收尾」。TR-1b 认领后，已注册的回调位于可接 post-reload `RunFinished` 的位置（**必须 live e2e 验证，这是整条 workstream 的成败未知**）。加初始化超时：重挂时记时间戳，若 30–60s 内无 `RunStarted/RunFinished`，`FailJob(jobId, "orphaned by domain reload")`，保证 agent 轮询循环有界终止。现有 600s `k_StaleJobMs` 作最后兜底。

### ED-1 `/console`

`EditorDebugMCP.Route`（line 105）加 `case "/console"` -> `RunOnMain(() => ConsoleHandler.Read(req))`。新建 `ConsoleHandler.cs`（主线程，`LogEntries` 是 editor-internal、仅主线程）：`int count = LogEntries.StartGettingEntries();` -> `for i in [max(0,count-N), count): var entry = new LogEntry(); LogEntries.GetEntryInternal(i, entry);` 读 `entry.message / entry.mode（int bitmask -> Error/Warning/Log）/ entry.line / entry.file / entry.instanceID` -> `LogEntries.EndGettingEntries();`，组 `JArray of {message,type,file,line,instanceID,stackTrace?}`。请求参数 `{count?:int=50, filter?:'error'|'warning'|'all'='all', includeStack?:bool}`。复用现有 `{ok,elapsedMs,result}` 信封，零协议改动。

风险点：内部 API 形状（`StartGettingEntries/GetEntryInternal/EndGettingEntries` 与 `LogEntry` 字段名）随 Unity 版本敏感、无文档。Unity 6000.3.16f1 反射可见 `LogEntry` 有 `message/file/line/column/mode/instanceID/identifier/globalLineIndex/callstackTextStart*`，**没有稳定的 `stackTrace` 字段**，所以 `includeStack` 只能 best-effort：找得到可读 callstack 时填 `stackTrace`，找不到则省略或置 `null`，不得因栈不可用让 `/console` 整体失败。缓解：全程经反射解析（不硬类型引用）、单条不可读时返 `{__unavailable:reason}` 而非抛、`Start/End` 必须 `try/finally` 配对否则 Console UI 会楔死。配套替换 `api-cookbook.md` #2 里那段「awkward dance」笔记。

### ED-5 `/ping` 编辑器态

扩 `Ping()`（`EditorDebugMCP.cs:126-133`）加 `isPlaying / isPaused / isCompiling / isUpdating / timeSinceStartup`。注意 `EditorApplication.is*` 与 `EditorApplication.timeSinceStartup` 都按主线程 API 处理，而 `/ping` 当前在 HTTP 线程内联构信封（Route line 110，非经 `RunOnMain`）。**采用缓存方案**（零跳、线程安全，镜像 `RecompileHandler` 缓存 `s_CompilationStarted` 的做法）：在静态 ctor 订阅 `EditorApplication.update` 与 `playModeStateChanged`，把 bool 与 `timeSinceStartup` 写进 volatile/locked 字段，`Ping` 只读缓存；或把时间改为 `DateTimeOffset.UtcNow` 这类线程安全时间源，但不要在 HTTP 线程直读 `EditorApplication.timeSinceStartup`。约 15 行，`protocol.md` 补字段说明。

### ED-2 `/batch`

`Route` 加 `case "/batch"`，请求 `{requests:[<invoke-req>,...]}`。在**单个** `MainThreadDispatcher.Run` 内循环：每个 sub-req `try { raw = ReflectionInvoker.Execute(subReq); results.Add(serializeOne(raw)) } catch (e) { results.Add(ErrorJson(e)) }`，单条失败不中止整批（per-item `ok`）。信封 `{ok:true, elapsedMs, results:[{ok,result|error,void?}...]}`。**前置重构**：把 `RunOnMain`（lines 144-146：`VoidResult->void:true` / `JToken` 透传 / 否则 `ResultSerializer.ToJson`）的 per-result 整形抽成共享 `SerializeRaw` helper，供 `/invoke` 与 `/batch` 共用避免发散。注意 4MB `k_MaxBodyBytes`（line 181）：大批量会整批 `__truncated`，故 `requests.Count > 64` 拒绝，文档建议批量只放读。

### ARCH-1 抽 com.yoji.editor-core

**核心边界：core 拥有 lifecycle / transport / recompile 原语；tool 拥有 routing 与（刻意发散的）信封。**

放进 core：
- **`MainThreadDispatcher.cs`**（字节一致，逐字移入、改 namespace、删两份副本、两边加 using）。
- **`LoopbackHttpHost`**：拥有 `Start` / 端口 fallback / `Loop` / `Stop` / `WriteResponse`；入参 = 端口数组 + service name + 一个返回 `(status, body)` 的 handler delegate。每个工具保留自己的 `Route` 与信封构造器。**关键**：暴露一个干净的「on-start」hook，供 TR-1c 的 reattach 挂入，避免把 reattach 焊进单体 `Start()`。
- **`RecompilePrimitive`**：拥有编译事件脚手架（三 volatile flag、`compilationStarted/Finished/assemblyCompilationFinished` 接线、pending 计数、5s no-start + 180s finish 轮询、`WaitForPendingResponse`），暴露 `Trigger(preCompileAction, busyPredicate)` 返回中性结果 `{started, finished, hasErrors, elapsedMs}`。editor-debug 传空 preCompile + `isCompiling` busy；test-runner 传 `SetCompiling + AssetDatabase.Refresh` + `!IsIdle || isCompiling` busy，各自把中性结果整形成自己的信封。

**坚决留在 per-tool（不进 core）**：`JobStore`、`TestRunService`、`ReflectionInvoker`、`EvalParser`、`ResultSerializer`、以及一切信封整形 helper。把信封并进 core 是真正的错误——恒 200 vs 真状态码是刻意的协议契约（见两个包的文件头注释）。

**lua-device-debug 不并入**：它是 Runtime asmdef（含 Android player）、手写 `TcpListener` + 手 parse HTTP、`SynchronizationContext` dispatcher（16 任务队列上限 + 4 态生命周期 + 取消/背压/超时映射 429/408）、无 `RecompileHandler`（player 不能重编译）。它跑在 shipped player 而非 `EditorApplication.update`，无法加入 Editor-only core。

asmdef：core 为 Editor-only、仅依赖 newtonsoft-json；两工具 package.json 加 core 依赖、asmdef 加 core 引用。各包 InternalsVisibleTo 仅对自身 test asmdef，抽 core 不与任何跨工具 internal 访问冲突。

## 优先级与路线图

### P0 do-now（便宜、独立、立即收益，互不阻塞）
- **TR-4** 端口修正（一行）
- **TR-2** 失败详情（最高杠杆）
- **ED-1** `/console`（最大盲区）
- **ED-5** `/ping` 编辑器态
- **ED-3 清理** `--usings` 诚实化
- **ARCH-1a** `MainThreadDispatcher` 移入 editor-core（字节一致、零风险，作独立小提交）

### P1 do-next
- **TR-3** 0 命中守卫 + `/list-tests`（与 TR-2 共用 e2e seam）
- **ED-2** `/batch`（落地前先抽 `SerializeRaw`）
- **TR-1a spike**：置 `DisableDomainReload`，live PlayMode e2e（pass + fail 各一）证明能正确收尾。**这是决定后续路径的分叉点。**
- **ARCH-1b** 抽 `LoopbackHttpHost` + `RecompilePrimitive`

### P2 later（条件触发）
- **TR-1b + TR-1c**：**仅当** TR-1a 的 DisableDomainReload 被判不可接受 **且** live spike 证明 `RunFinished` 跨重载存活，才建。否则丢弃。
- **ED-4** 只读/denylist（opt-in 默认关）
- **ED-6** `/describe` 过滤 + 批量

### 排序理由（尤其 ARCH-1 与 TR-1 的关系）

用户的尖锐问题——「editor-core 抽取是否应在 TR-1 PlayMode 之前，以免长出第三份发散副本」——答案是**有条件的是**，比「无条件先做 ARCH-1」更站得住：

1. **TR-1 的 reload-survival 逻辑大部分是 per-tool 业务，不是共享 infra。** SessionState job 指针、reattach、init-timeout 都落在 `TestRunService`/`JobStore`/`TestRunnerMCP`，而 core 刻意不拥有 `JobStore/TestRunService`。所以「第三份发散副本」之说对 SessionState 部分略夸大。
2. **真正成立的、更窄的论点是关于 listener 外壳与 recompile primitive：** TR-1c 的 reattach 要改 `Start()` 路径。若外壳已抽成 `LoopbackHttpHost` 并暴露干净的 on-start hook，reattach 是挂进稳定 host 的回调；若未抽，reattach 被焊进单体 `Start()`，日后 ARCH-1 还得把它当 hook 重新撬出来——「先浇混凝土再钻孔」。所以 **ARCH-1b 应排在重 TR-1b/c 之前**。
3. **但 TR-1a（DisableDomainReload）是推荐的第一步，且它绕开重载——若 TR-1a 够用，TR-1 几乎不碰外壳**（只删 400、修 BuildFilter、scoped 设置），此时「先抽外壳给 TR-1 留干净 seam」的论点弱化。
4. 因此 **真正的第一动作是 TR-1a spike**（便宜、确定性、决定 TR-1b/c 是否需要），它不需要 ARCH-1。do-now 的小赢（TR-2/TR-4/ED-1/ED-5）与 dispatcher 移动都与 ARCH-1 无关，先发即可。

净结论：dispatcher 移动现在就做；host 外壳 + recompile primitive 在「重 TR-1b/c 路径」之前做；而是否走重路径由 TR-1a spike 决定。EditMode 路径在真实使用中证明稳定后，再把 TR-1 当独立 milestone 排。

## 开放问题（需用户决策）

- **DisableDomainReload 对目标 PlayMode 测试集是否可接受？** 依赖静态状态跨域重置的测试在此模式下行为不同。若不可接受，则 TR-1b/c 成为强制路径而非可选——这直接决定 PlayMode workstream 的体量与风险。
- **`RunFinished` 是否在 post-reload 的新 domain 上触发？** 整条重路径的成败未知，无法单测，必须 live e2e 验证。若不触发，重路径根本不可行。是否投入资源做这个 live spike？
- **editor-core 在 Registry（u3d-ai-linker）中如何建模？** 它是一个无 Skill、`defaultEnabled=false` 的纯 infra 包，需要 `planned-then-ready` 之外的语义，且 Registry schema 当前不表达「包间 UPM 依赖与安装顺序」。是否先扩 Registry schema，还是暂由工具 package.json 隐式携带 core 依赖、Registry 视其为 out-of-band？
- **ED-4 denylist 范围由谁定？** denylist（`Delete*/Destroy*/Save*/Exit/Quit/MoveAsset/ImportAsset/RequestScriptCompilation`…）天然不完整，是否接受「够用即可、显式 opt-in」的折中？
- **`failures[]` 与 `/console` 的响应上限策略？** 截断条数（如 50）与栈长度的具体阈值需用户拍板，以平衡信息量与响应体大小。
- **是否接受 ARCH-1 对独立性原则的让步？** 共享 core 会让两个工具的 semver tag 与回滚不再完全独立（工具只能与其 core 一起回滚）。这与 linker spec 的「每工具独立封装/独立版本」原则有张力。

## 非目标

- **不做 Roslyn / Microsoft.CodeAnalysis 脚本化 `/eval`。** 重依赖 + 编辑器 assembly 冲突 + 域重载重入 + 把本地调试端口变成任意代码执行面。invoke-chain 已覆盖 80%，仅做 `--usings` 死参数诚实化。
- **不加远程认证、不改绑定地址。** 维持仅绑 `127.0.0.1`。对本地 dev 工具，认证是过度工程。
- **不做 TR-5 假 `/cancel`。** `TestRunnerApi` 无受支持的 abort；state-only shim 会对 agent 撒谎。需要时把 `k_StaleJobMs` 调小/可配即可。
- **不把 lua-device-debug 迁入 editor-core。** 它是 Runtime player TcpListener + SynchronizationContext dispatcher，迁移等于重写。
- **不把响应信封并入 editor-core。** 恒 200 flat（editor-debug）与真状态码 + per-endpoint 形状（test-runner）是刻意的协议契约。
- **不在本轮触碰 `Core_Server` / 真实网络 / Lua 业务层。** 本设计只覆盖两个 dev-tool 包与 editor-core 抽取。

## 验证（落地后须覆盖）

- TR-2：单测 `FailureDetail` round-trip 与 leaf/suite 不双计数；e2e case 03 断言 `failures[0].message` 含预期串。
- TR-3：e2e 加「bogus testName -> `status=error`」用例，并断言后续轮询不会被晚到 `RunFinished` 改回 completed/Passed；核 `[Explicit]` fixture 的 `RunStarted` 枚举语义。
- TR-4：确认 21896/21897 无第三方占用；21890 主路径不变；C# 日志、client 示例、SKILL.md、README 端口说明全同步。
- TR-1a：live PlayMode e2e（pass + fail 各一）证明 DisableDomainReload 下正确收尾，且 `enterPlayModeOptionsEnabled` 与 `enterPlayModeOptions` 都被还原。
- ED-1：内部 API 经反射解析、不可用降级、`Start/End` 配对；Unity 6000.3.16f1 下 `stackTrace` 缺失时不失败；cookbook 替换旧笔记。
- ED-5：缓存 bool、`timeSinceStartup` 与 `playModeStateChanged` 订阅，HTTP 线程读不触碰主线程 API。
- ED-2：单批一跳验证、`SerializeRaw` 两路径不发散、`>64` 拒绝、4MB 截断行为。
- ARCH-1：两包 EditMode + e2e 全套在 6000.3.16f1 通过为无回归门禁；dispatcher 移动与 host/recompile 抽取分别验证。
