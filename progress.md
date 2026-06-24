# 工程进度 (Progress)

> U3D-Dev-Tools-AI - 面向 AI 辅助工作流的 Unity3D 开发工具集
>
> 最后更新: 2026-06-24 (service port hardening: stable registry 目标五个 ready 条目为 editor-core-v0.1.1 / editor-debug-v0.1.1 / test-runner-v0.1.5 / lua-device-debug-v0.1.1 / u3d-ai-linker-v0.1.5；根目录与 linker 包内 registry snapshot 一致；JSON/Python/manual editor-core+lua compile 已过；Unity batchmode 当前被 UPM `path` undefined 启动错误挡在脚本编译前)

本仓库是一个迁移工作区 (migration workspace)，把三个 Unity 调试工具的 Agent 侧资产与
Unity 侧服务整合为可安装的 UPM 包，并新增一个 Editor-only 编排包 `com.yoji.u3d-ai-linker`：
从 Project Settings 一键把这些工具批量装入目标工程，并把项目级 Skills/规则同步给
Claude Code 与 Codex。本文件汇总各组件的整体进度、验证基线与下一步计划；逐迭代的执行
记录见 `_planning/mission_plan.md` 与 `_planning/mission_notes.md`。

范围边界: 本仓库仅面向非 HybridCLR Unity 工程；旧 client-only 运行时表达式调试资产已移除。
编排包首版仅 Windows、仅 Unity 2022.3+。

## 状态总览

| 组件 | 端口 | 协议 | Agent 侧 | Unity 侧 | 状态 |
|------|------|------|----------|----------|------|
| test-runner-mcp | legacy 21890/21896/21897；project offset +0 | HTTP | 已迁入包 | 服务包 (`com.yoji.test-runner`) | 可用 (EditMode + PlayMode via DisableDomainReload, TR-1a)；stable 0.1.5；6000.3.16f1 与 2022.3.62f2c1 验证 |
| unity-editor-debug-mcp | legacy 21891/21892/21893；project offset +1 | HTTP+JSON | 已迁入包 | 服务包 (`com.yoji.editor-debug`) | 可用；stable 0.1.1；6000.3.16f1 验证 |
| unity-lua-device-debug | legacy 21894；project offset +4 | HTTP+JSON | 已迁入包 | 服务包 (`com.yoji.lua-device-debug`) | stable 0.1.1；Unity 2022.3+；传输层就绪；目标工程必须注册 `ILuaDeviceDebugHost`；已在 SLG_Prototype 真实 xLua 适配器端到端验证 |
| **u3d-ai-linker** | - | - (Editor 编排) | 同步 Skills/规则 | 编排包 (`com.yoji.u3d-ai-linker`) | **stable 0.1.5 目标发布；stable registry 指向 editor-core-v0.1.1 / editor-debug-v0.1.1 / test-runner-v0.1.5 / lua-device-debug-v0.1.1 / u3d-ai-linker-v0.1.5；根目录与包内 snapshot 一致** |
| com.yoji.editor-core | - | - (共享基础设施) | - | Editor-only 包 (`com.yoji.editor-core`) | 新增 (ARCH-1a)：`MainThreadDispatcher` + `RequestBodyReader` 去重宿主 + `EditorServiceLifecycle` 服务生命周期接线；editor-debug/test-runner/lua-device-debug 依赖之。活 Editor 编译加载验证 |

> 2026-06-24 端口生命周期硬化：test-runner / editor-debug / lua-device-debug 均通过 editor-core 的 shared endpoint 分配端口、写 registry/project ports、心跳与清理。`21894` 现在是 lua-device-debug 的 legacy/default 端口，不再是唯一 Editor 入口。套接字地址被占用时先查 registry 与 `portSource`，不要改 `Library/PackageCache` 或在单包内硬塞新固定端口。
> 本轮已验证：`git diff --check`、package/registry JSON parse、三套 Python client/resolver `py_compile`、`tools/test-agent-port-resolvers.py` 14 项、manual `Yoji.EditorCore.Editor` / `Yoji.EditorCore.Editor.Tests` / `Yoji.LuaDeviceDebug.Runtime` / `Yoji.LuaDeviceDebug.Editor` compile。Unity EditMode 未完成，原因是 TestProjects 在 Package Manager resolve 阶段报 `The "path" argument must be of type string. Received undefined`。

## 各工具进度

### 1. test-runner-mcp (可用，EditMode + PlayMode)

不开 Unity GUI 即可触发重编译并跑 EditMode/PlayMode 测试，结果回传给 AI agent。

- Unity 侧服务已作为 UPM 包 `Packages/com.yoji.test-runner` 迁入，含 `client.py`
  与 `references/run-e2e.py`。
- 基础能力: NUnit 结果 XML 映射与 overallResult 分桶、request-to-FilterSpec 解析
  (含 run-all 扩展)、HTTP 主机与主线程派发、真实 HTTP 状态码 (200/202/400/404/409)、
  e2e fixture 与全量用例通过、`/recompile` 409 规范化 error key。
- 近期增强 (已落地 main 1ebe62c):
  - TR-2 `/test-status` 直出 `failures[]` (每用例 name/message/stackTrace)，agent 无需
    自己 parse NUnit XML 即可知道哪个用例、哪一行挂了。
  - TR-3 0 命中守卫 + `/list-tests` 发现端点：拼错 testName 时不再"假绿"，改返
    `status=error`；`/list-tests` 可枚举某 mode 下的全部叶子用例。
  - TR-4 端口修正 `{21890,21896,21897}`，消除与 lua-device-debug 的 21894 撞车。
- TR-1a PlayMode (已落地，2026-06-15): `testMode:"PlayMode"` 经临时叠加
  `EnterPlayModeOptions.DisableDomainReload` 跑（不触发域重载，沿用 field-based RunFinished
  收尾，无需 SessionState），前置守卫 isPlaying/脏场景 (409)，run 结束完整还原用户设置；
  `/list-tests` 同步开放 PlayMode 枚举。活 Editor e2e 已验证：202 → 设置叠加
  DisableDomainReload → 真实进 Play (isPlaying=True) → 完成 → 设置/Play 完整还原（无域重载，
  editor-debug 全程在线）。
- Unity 2022 兼容修复 (2026-06-17，stable `test-runner-v0.1.5`):
  - `com.unity.test-framework` 依赖下调并锁定为 `1.1.33`，避免 Unity 2022 registry 无法解析
    `1.5.1` 导致整项目 `UnityEditor.TestTools` 缺失。
  - `UnityTestRunnerApiAdapter.cs` 仅在 `!UNITY_6000_0_OR_NEWER` 编译，使用反射 + `DispatchProxy`
    访问 `TestRunnerApi` / `ICallbacks` / `Filter` / adaptor 类型，2022 编译单元不再直接引用
    `UnityEditor.TestTools.TestRunner.Api`。
  - `UnityTestRunnerApiAdapter.Unity6000.cs` 仅在 `UNITY_6000_0_OR_NEWER` 编译，Unity 6000 继续走强类型
    `TestRunnerApi` 路径。
  - Unity 2022 Mono 要求 `DispatchProxy` 的 TProxy 可继承且构造可访问；最终形态为公开顶层
    `UnityTestRunnerCallbackProxy : DispatchProxy`，修复 sealed/private proxy 的运行时失败。
  - HD2D-Demo Unity `2022.3.62f2c1` Git UPM smoke 已通过：`ping`、`list-tests`、`recompile`
    均成功；不存在的测试名返回受控 `status=error` / `overallResult=Error`，不是假绿。
- 阶段边界: 空测试过滤即运行整个套件；run-all PlayMode 在无 PlayMode 测试时正常完成 0/0。
- 下一步: TR-1b/c（SessionState 跨重载存活）仅当 DisableDomainReload 被判不可接受时才需（见 TR-1）。

### 2. unity-editor-debug-mcp (可用)

legacy 21891/21892/21893 / project offset +1 的 HTTP+JSON 反射服务，供 AI 检视与调用 Unity 类型/成员。

- Unity 侧服务已作为 UPM 包 `Packages/com.yoji.editor-debug` 迁入，含 `client.py`、
  skill 与 references (协议规范、API cookbook、排错指南)。
- 基础能力: `/invoke` `/describe` `/eval` `/recompile` `/ping`；Unity 6.4+ entity id
  支持 (object 引用以全宽 `EntityId` 十进制字符串表示，同时保留既有 `instanceID`)。
- 近期增强 (已落地 main 1ebe62c):
  - ED-1 `/console` 端点：读真实日志条目 (message/type/file/line/stack)，移除 agent
    "读不到自己造的编译/运行时错误"这一最大盲区。
  - ED-5 `/ping` 暴露编辑器态 (`isPlaying`/`isPaused`/`isCompiling` 等，经
    `EditorStateCache` 缓存、HTTP 线程零跳读取)。
  - ED-2 `/batch`：N 个 invoke 合并到一次主线程跳。
  - ED-3 清理：`--usings` 死参数诚实化。
- 下一步: Unity 6.4 分支需在 6.4 安装上完成编译后再发布。

### 3. unity-lua-device-debug (传输层已就绪)

legacy 21894 / project offset +4 的 HTTP+JSON 传输层，用于 Editor 与 Android Development Build 中的
Unity Lua 运行时诊断。

- 已作为通用 UPM 包 `Packages/com.yoji.lua-device-debug` 迁入，含 `client.py` 与 skill。
- 已完成:
  - Unity 6.3 LTS 包骨架，Runtime/Editor/Tests/Agent 分层与独立 asmdef。
  - 通用传输层 `/ping` `/commands` `/execute`，基于 `TcpListener` 绑定 loopback。
  - 安全闸: editor/development-only 启动、host 就绪检查、请求/响应大小限制、
    队列上限、执行超时、命令未找到、变更拒绝路径 (mutation gate)。
  - `ILuaDeviceDebugHost` 主机契约 + host registry + 主线程派发器。
  - Agent CLI: ping/commands/execute、adb forward/remove、serial 消歧、
    `--allow-mutation`、`--arg key=value` 标量解析。
  - fake-host EditMode 测试，6000.3.16f1 上 8/8 通过 (修复了主线程派发死锁)。
- 边界: 通用传输层不含任意 Lua eval、C# 反射 eval 或 HybridCLR 集成。
- 项目侧适配器已落地并端到端验证 (2026-06-14/2026-06-17): `SLG_Prototype/Core_Client` 的
  `XLuaRuntime` 实现 `ILuaDeviceDebugHost`，`GameBootstrap.Awake` 在 Play 模式
  `Start("boot.lua")` 注册 host (退出 Play 时 `Dispose → UnregisterHost`)；boot.lua
  提供 `__slg_debug_describe_commands`/`__slg_debug_execute` 与 5 个只读命令。活 Editor
  里 Play 模式实测: system.info / module.list / config.tables / red_dot.dump /
  data_center.dump 在真实 xLua VM 执行返回真实游戏状态；EditMode 无 host 时
  `/commands` `/execute` 正确 409 HOST_NOT_READY，未知命令 404 COMMAND_NOT_FOUND。
- Android Development Build 真机验证 (2026-06-17): `SLG_Prototype/Core_Client` 生成
  `Builds/Android/LuaDeviceDebugDev.apk` (Development + AllowDebugging，195,882,204 bytes)，
  安装到 `SM_S9180/R5CW40351EF` 后经 `adb forward tcp:21994 tcp:21894` 连通 `/ping`：
  `platform=Android`、`isDevelopmentBuild=true`、`hostRegistered=true`、`hostReady=true`；
  `commands` 与 5 个只读 `execute` 命令全 200。
- 包边界不变: 通用包仍不放置 SLG 专用全局 (如 `__slg_debug_execute`)，这些由目标项目提供。

### 4. u3d-ai-linker (stable 0.1.4，已提交+推送+打 tag)

Editor-only UPM 编排包 `Packages/com.yoji.u3d-ai-linker`：从 `Edit > Project Settings >
U3D AI Linker` 一键把本仓库工具按白名单批量装入目标工程，并把项目级 Skills/规则同步给
Claude Code 与 Codex。首版仅 Windows、仅 Unity 2022.3+，不涉及 HybridCLR。

- 由根目录 `Registry/` 白名单 (stable/dev/local 三通道) 驱动安装决策；用 manifest 事务
  把工具写成顶层 Git-URL 依赖以解决"git 子包不自动解析同级依赖"的阻断；Agent 资产用
  事务式目录复制 + Windows Junction 落地到 `.u3d-ai-linker/skills/<tool>` 并链到
  `.claude`/`.agents`；跨域重载的安装/同步以持久化 `operation.json` 记账、`[InitializeOnLoad]`
  域重载恢复。
- **本次实现 (mission-runner，2026-06-14 至 2026-06-17)：8 个子系统 / 50 Task 中 48 个 headless 落地；
  当前 batchmode EditMode `total=266 passed=266 failed=0`（2026-06-17，含 Project Settings Install/Update、Agent Sync/Junction Repair、release gate/UI 状态补测）：**
  - LINK-0 资产探针决策门 (directory vs zip-fallback)
  - LINK-1 包骨架 + 包常量 + Project Settings 入口
  - LINK-2 Registry 解析/白名单校验/URL 生成/拓扑排序
  - LINK-2b manifest 七步原子事务 (冲突预检 + 回滚 + 手动改动检测)
  - LINK-3 UPM 安装队列 + 域重载恢复决策器
  - LINK-4 内容哈希 / ownership / Windows Junction / 事务式 Agent 同步
  - LINK-5 Fragment 确定性合并 + CLAUDE/AGENTS/.gitignore 托管区块
  - LINK-6 Project Settings 面板 + 三通道 URL + 状态分离 + RefreshDev/Restore
  - LINK-7 工具 fragments + `Registry/{stable,dev}.json` (根与包内快照逐字节一致) + README 修正
- 资产模式: stable ready 范围为 `editor-core` / `editor-debug` / `test-runner` /
  `lua-device-debug` / `u3d-ai-linker`。当前 stable tag URL 为 `editor-core-v0.1.1`、
  `editor-debug-v0.1.1`、`test-runner-v0.1.5`、`lua-device-debug-v0.1.1`、
  `u3d-ai-linker-v0.1.5`；消费者 lock 应解析为 `source=git`。
  `lua-device-debug` 只提供通用传输与 `ILuaDeviceDebugHost` 契约，不内置 Lua eval 或项目 host。
  `Tools > U3D AI Linker > Run Agent Asset Probe` 写出
  `Library/U3DAILinker/probe-result.json`（2026-06-16T17:48:00Z，3/3 readable，
  `recommendedMode=directory`，editor-debug/test-runner/linker 路径均在 Git PackageCache）。
- Project Settings 动作接线 (2026-06-17): `Refresh Registry` 读取包内快照；
  `Install/Update Selected` / `Install/Update All` 走 `ManifestTransaction` +
  `InstallQueueBuilder` + `UpmQueueRunner`；`Restore Stable/Dev` 与 `Rollback Manifest`
  接入现有 manifest 事务/回滚。`Sync Agent Assets` 与 `Repair Links` 已开放并在
  SLG_Prototype Dev 通道验证：生成 `.u3d-ai-linker/skills/*`，创建
  `.claude/skills/*` 与 `.agents/skills/*` Junction，删除一个 Junction 后
  `Repair Links` 可重建且目标 skill 内容保留。
- UI 状态深查 (2026-06-17): `Refresh Registry` 现在刷新 PackageInfo 安装快照；
  managed URL 与当前通道目标不一致时显示 `Outdated` 而非误报 `Installed`；工具行显示
  `status/current/target`；`u3d-ai-linker` 自身进入 Registry，便于诊断 linker 包缓存 hash；
  `Open Generated Folder` 与 `Copy Diagnostic Report` 不再只是日志空实现。
- Review 修复 (2026-06-17): Local 通道读取 dev snapshot 并生成 `file:` 依赖；Dev 通道必须
  通过 `git ls-remote` 解析远端 `main` 的真实 SHA 后才写 manifest；无 ready 工具时不再假成功；
  manifest 事务记录另存 `last-manifest-operation.json`，队列完成清理 `operation.json` 后仍可
  `Rollback Manifest`。
- Stable registry 热修复 (2026-06-17):
  - `u3d-ai-linker-v0.1.1` 修复旧 linker 包内嵌 stable snapshot 仍指向 `test-runner-v0.1.0` 的问题。
  - `u3d-ai-linker-v0.1.2` / `test-runner-v0.1.3` 修复 Unity 2022 `DispatchProxy` TProxy 被 sealed 的问题。
  - `u3d-ai-linker-v0.1.3` / `test-runner-v0.1.5` 修复 Unity 2022 生成代理无法访问 private nested proxy 构造的问题。
  - HD2D-Demo 已通过 Git stable 通道安装 `test-runner-v0.1.5` 与 `u3d-ai-linker-v0.1.3`，包 hash 为
    `f03b36fe0cf10d4a48fdede95a6e5d496df46fe8`。
- Stable registry lua 发布 (2026-06-22):
  - `lua-device-debug` 从 planned 升为 stable ready，`minUnity=2022.3`，显式依赖 `editor-core`。
  - `u3d-ai-linker-v0.1.5` 更新包内 stable/dev registry snapshot，确保 Project Settings 面板能看到 lua stable 项。
  - PurgeDemo Unity `2022.3.62f2c1` Git UPM smoke 通过：`EditorDebugMCP`、`TestRunnerMCP`、
    `LuaDeviceDebug` 均在线，lua 包 `14/14` EditMode 通过。
- **发布证明 (2026-06-17 / 2026-06-23 当前仓库):**
  - `tools/check-u3d-linker-stable.ps1` 已覆盖五个 required ready 条目：
    `editor-core` / `editor-debug` / `test-runner` / `lua-device-debug` / `u3d-ai-linker`。
  - release gate 校验根目录 `Registry/stable.json` 与
    `Packages/com.yoji.u3d-ai-linker/Registry/stable.json` 逐字节一致，且每个
    `revision` 等于 `<id>-v<package.version>`。
  - 本地 tag 与远端 tag 均已确认：`editor-core-v0.1.1` (`d5bf2e0`)、
    `editor-debug-v0.1.1` (`d5bf2e0`)、`test-runner-v0.1.5` (`f03b36f`)、
    `lua-device-debug-v0.1.1` (`2635d48`)、`u3d-ai-linker-v0.1.5` (`67b8e31`)。
- 实现细节与权威类型收敛见
  [u3d-ai-linker 实现计划](docs/superpowers/plans/2026-06-14-u3d-ai-linker-implementation.md)
  (含 Appendix B 类型/文件收敛权威表)。

### 近期硬化 (2026-06-14)

一组横跨三包的 HTTP/安全硬化 + linker 面板门控:

- **editor-debug**: `/eval` 默认关闭 (`c_AllowEval = false`，需显式打开并重编)；请求体改由
  `RequestBodyReader.ReadUtf8` 读取，带 1MB 上限 (取代无界 `StreamReader.ReadToEnd`)；
  skill / troubleshooting / run-e2e 同步改为 eval-disabled 预期。
- **test-runner**: 请求体同样走 `RequestBodyReader` (1MB 上限)；超限 `InvalidDataException`
  映射 HTTP 413。
- **lua-device-debug**: `Content-Length` 解析硬化 (`TryParse` + 负值 → 400)、HTTP 头行
  8KB 上限 (→ 400)、响应前 `DrainAvailable` 排空挂起请求字节；新增 malformed/oversized
  头部测试。
- **u3d-ai-linker**: Project Settings 面板曾以 `ActionsWired = false` 禁用未接线动作；
  2026-06-17 已开放 Install/Update、Restore、Rollback、Agent Sync/Junction Repair；
  域重载恢复不再在 `Requested` 时自调度续跑 (`ShouldScheduleFollowUp`)，改由域重载静态构造或显式
  Retry/Resume 接续。
- `RequestBodyReader` 与 `MainThreadDispatcher` 原在 editor-debug / test-runner 各存一份
  (asmdef 隔离)，**已由 ARCH-1a 去重移入 `com.yoji.editor-core` (2026-06-15)**；`MemoryStream`
  已按 `contentLength` 预分配 (/simplify)。
- 三服务 (editor-debug/test-runner/lua-device-debug) 的生命周期接线 (`delayCall` +
  `afterAssemblyReload` + `beforeAssemblyReload` + `quitting`) 原各自手挂一份并各带一段重复注释，
  **已收口为 editor-core 的 `EditorServiceLifecycle.Bind(Start, Stop)` (/simplify，2026-06-15)**：
  三处各缩为一行，editor-core 从此真正"拥有" package.json 宣称的 lifecycle 原语；lua-device-debug
  据此新增对 editor-core 的依赖。活 Editor `/recompile` 0 编译错误、三服务经新接线全部恢复验证。

## 增强路线图 (dev-tools-enhancements)

editor-debug 与 test-runner 已在真实工程中被 AI agent 使用，由此暴露的缺口与
价值/成本评估、分阶段路线图见
[dev-tools 增强设计](docs/superpowers/specs/2026-06-13-dev-tools-enhancements-design.md)。

- P0 do-now (已落地 main): TR-2 失败详情、TR-4 端口修正、ED-1 `/console`、
  ED-5 `/ping` 编辑器态、ED-3 `--usings` 清理。
- P1 do-next: TR-3 0 命中守卫 + `/list-tests` (已落地)、ED-2 `/batch` (已落地)、
  **TR-1a PlayMode via DisableDomainReload (已落地，2026-06-15)**、**ARCH-1a 抽
  `com.yoji.editor-core` 并移入 `MainThreadDispatcher` + `RequestBodyReader` (已落地，
  2026-06-15)**；ARCH-1b 抽 `LoopbackHttpHost`/`RecompilePrimitive` 待做（lifecycle 接线已先期
  收口为 editor-core 的 `EditorServiceLifecycle`）。
- 待做 (路线图): ARCH-1b host 外壳 + recompile 原语抽取 (editor-debug 与 test-runner 仍各自
  维护 HTTP listener + recompile 脚手架)；TR-1b/c PlayMode 重载存活路径 (条件触发，仅当
  DisableDomainReload 不可接受时)；ED-4 只读/denylist、ED-6 `/describe` 过滤。

## 验证基线

| 组件 | 验证环境 | 结果 |
|------|----------|------|
| unity-editor-debug-mcp | Unity 6000.3.16f1 | **82/82 EditMode 通过**；基线 13 HTTP e2e 通过 (增强端点前) |
| test-runner-mcp | Unity 6000.3.16f1 | **29 total / 27 passed / 2 skipped / 0 failed** EditMode 通过 |
| unity-lua-device-debug | Unity 6000.3.16f1 | **14/14 EditMode 测试通过** |
| u3d-ai-linker | Unity 6000.3.16f1 (batchmode) | **266/266 EditMode 通过 (含 Install/Update、Agent Sync/Junction Repair、release gate、UI 状态补测)** |
| test-runner-mcp | Unity 2022.3.62f2c1 (HD2D-Demo Git UPM) | **通过**：`ping` / `list-tests` / `recompile`；不存在测试名受控返回 `overallResult=Error` |
| unity-editor-debug-mcp | Unity 2022.3.62f2c1 (HD2D-Demo Git UPM) | **通过**：fallback 端口 `21892` `ping` 正常 |
| unity-lua-device-debug | Unity 2022.3.62f2c1 (PurgeDemo Git UPM) | **通过**：`/ping` 正常，EditMode `14/14` 通过 |
| stable registry release gate | 本地仓库 + origin tags | **通过**：五个 ready 包 revision/package.version 对齐；根目录与 linker 包内 stable snapshot 一致；远端 tag 存在 |

注: 近期增强 (`/console` `/batch` `/list-tests` `failures[]` 等) 已扩展对应 e2e
fixture (见各包 `references/run-e2e.py`)；HTTP e2e 的完整通过数仍需在 Unity 在线时重跑一遍确认后更新。u3d-ai-linker 经独立宿主
`TestProjects/u3d-ai-linker` 跑 headless batchmode (`tools/run-editmode.ps1 -Project
u3d-ai-linker`)；各测试工程的 `Library/` 与 `TestResults/` 为构建产物。

### Unity 2022 stable Git UPM 验证 (2026-06-17, HD2D-Demo)

- Unity: `2022.3.62f2c1`。
- 安装源: `https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/...#tag`。
- 解析结果: `com.yoji.test-runner@0.1.4` 指向 `test-runner-v0.1.5`；
  `com.yoji.u3d-ai-linker@0.1.3` 指向 `u3d-ai-linker-v0.1.3`；两者 hash 均为
  `f03b36fe0cf10d4a48fdede95a6e5d496df46fe8`。
- `com.unity.test-framework` 使用 `1.1.33`，避免 `1.5.1` 在 Unity 2022 registry 不存在导致
  `UnityEditor.TestTools` 全项目缺失。
- `EditorDebugMCP ping` 在 fallback 端口 `21892` 通过。
- `TestRunnerMCP ping`、`list-tests --mode EditMode`、`recompile` 在 fallback 端口 `21896` 通过。
- `run-tests` 指定不存在测试名时进入受控失败：`status=error`、`overallResult=Error`、
  message 包含 `filter matched 0 tests`。
- 结论: Unity 2022 使用反射 adapter；Unity 6000 使用强类型 adapter。两个路径由编译符号隔离，
  兼容修复原则上不影响 Unity 6000。

### 活 Editor 端到端验证 (2026-06-14，本地 file: 通道)

把 `SLG_Prototype/Core_Client` 的四个 `com.yoji.*` 依赖改为本地 `file:` 源码 (取代旧 git
缓存，确保跑的是最新版)，在活 Unity Editor (6000.3.16f1) 里对三个工具逐端点亲验，全部通过:

- **editor-debug (21891)**: `/invoke` get/call/链式/set + 重载消歧 (`argTypes`)、`/describe`、
  `/console` (error/all 过滤)、`/batch` (3 合 1)、`/eval`、`/recompile` (1.5s 无错 + 域重载后
  三服务自动重启)。
- **test-runner (21890)**: `/list-tests` (Edit/Play)、真实 `/run-tests`→`/test-status` 轮询
  (running→completed，passed=1 failed=0 Passed，结果 XML 落盘)、负路径 400/404/404。
- **lua-device-debug (21894)**: EditMode 守卫 (409 HOST_NOT_READY / 404 COMMAND_NOT_FOUND) +
  Play 模式活 host 下 5 只读命令端到端执行 (见工具 3 §)。
- **u3d-ai-linker (Project Settings / probe)**: SLG_Prototype 本地 `file:` 安装下
  `Edit > Project Settings > U3D AI Linker` 可打开；`Tools > U3D AI Linker >
  Run Agent Asset Probe` 返回 `recommendedMode=directory`，三目标均存在。

### Android 端到端验证 (2026-06-17, SLG_Prototype Development Build)

- 设备: `SM_S9180 / R5CW40351EF`。
- APK: `Builds/Android/LuaDeviceDebugDev.apk`，195,882,204 bytes，安装后 `pkgFlags`
  含 `DEBUGGABLE`。
- 转发: `adb forward tcp:21994 tcp:21894`。
- `/ping`: `platform=Android`、`isEditor=false`、`isDevelopmentBuild=true`、
  `hostRegistered=true`、`hostReady=true`。
- `/commands` 暴露 `system.info` / `module.list` / `config.tables` / `red_dot.dump` /
  `data_center.dump`；五个 `execute` 全部 HTTP 200，返回 xLua 真实状态。

注: 此为活 Editor 逐端点功能手验，非重跑 `run-e2e.py` 计数套件；增强端点的完整 e2e
通过数仍待在线重跑确认 (见上方注)。

运维注记: 编程触发 `/recompile` 引发的域重载，在 Unity Editor **窗口失焦时会被节流**
(早期服务重启延迟可达 ~140s)。**缓解 (2026-06-15)**：三服务重启已从 `EditorApplication.delayCall`
改挂 `AssemblyReloadEvents.afterAssemblyReload`（域重载结束同步主线程触发，不受失焦节流），
`/recompile` 后**失焦恢复从 ~140s 降到 ~2s**（活 Editor 实测）；配合 Preferences → General →
Interaction Mode = No Throttling，失焦下 recompile 循环基本无需手动聚焦。**例外**：向 manifest
新增包后的 package 重解析仍需聚焦（Unity 仅在聚焦时检测 manifest 变化）。

### 服务端口硬化 follow-up (2026-06-24)

- 审查反馈中的真实代码问题已回补到源码仓库 `G:\Side_Projects\U3D-Dev-Tools-AI`，不是只改目标工程缓存。
- Agent 端 resolver 保持三份 byte-identical，同步脚本 `tools/sync-agent-port-resolver.py` 已执行。
- resolver 单测从 14 条扩到 18 条，新增 keyword-only `explicit_port` 与 timeout legacy fallback 覆盖。
- Python `py_compile`、JSON parse、`git diff --check` 通过；Unity EditMode 仍被 UPM `path undefined` 启动问题挡在脚本编译前。
- `editor-core-v0.1.1` 现场安装暴露 `ServiceInstanceRecord.cs.meta` GUID 长度错误，Unity 忽略该源码文件。
  补丁发布版本提升为 `editor-core-v0.1.2`、`editor-debug-v0.1.2`、`test-runner-v0.1.6`、
  `lua-device-debug-v0.1.2`、`u3d-ai-linker-v0.1.6`。

## 待办与下一步

- [x] u3d-ai-linker: 初始 stable 已提交、推送并打 tag (`u3d-ai-linker-v0.1.0`)。
- [x] u3d-ai-linker: stable registry 已发布到 `u3d-ai-linker-v0.1.5`，内嵌 snapshot 指向
      `test-runner-v0.1.5` 与 `lua-device-debug-v0.1.1`，根目录与包内 `stable.json` 一致。
- [x] u3d-ai-linker: 真实 Git stable 通道在 HD2D-Demo Unity 2022.3.62f2c1 中已解析并运行；
      TestRunnerMCP 与 EditorDebugMCP smoke 均通过。
- [x] u3d-ai-linker: SLG_Prototype 已验证 Project Settings 入口、Agent asset probe、
      Agent Sync、Repair Links、Windows Junction smoke。
- [x] test-runner-mcp: TR-1a PlayMode 执行 (DisableDomainReload + 设置快照还原 + 守卫)，活 Editor e2e 验证通过 (2026-06-15)。
- [x] test-runner-mcp: Unity 2022 兼容热修复已发布到 `test-runner-v0.1.5`；反射 adapter + public `DispatchProxy`
      代理通过 HD2D-Demo Unity 2022 Git UPM smoke。
- [ ] unity-editor-debug-mcp: 在 Unity 6.4 安装上编译并验证 entity id 分支后发布。
- [x] unity-lua-device-debug: SLG_Prototype 已实现 xLua 适配器 (`XLuaRuntime: ILuaDeviceDebugHost`) 与 5 个 SLG 命令，活 Editor Play 模式端到端验证通过 (2026-06-14)；Android Development Build 真机端到端验证通过 (2026-06-17)。
- [x] unity-lua-device-debug: 兼容 Unity 2022.3.62f2c1 并纳入 stable registry (`lua-device-debug-v0.1.1`)，`u3d-ai-linker-v0.1.5` 内嵌 registry snapshot 已同步。
- [x] release gate: `tools/check-u3d-linker-stable.ps1 -RequireRemoteTags` 覆盖五个 stable ready 包，本地 tag 与 origin tag 均确认存在 (2026-06-23)。
- [x] ARCH-1a: 抽取 `com.yoji.editor-core`，`MainThreadDispatcher` + `RequestBodyReader` 去重移入，活 Editor 编译验证 (2026-06-15)。
- [x] 服务生命周期接线收口为 editor-core 的 `EditorServiceLifecycle.Bind`（三服务各缩一行，lua 新依赖 editor-core），活 Editor 重编译 0 错误验证 (/simplify，2026-06-15)。
- [x] 本批 dev-tools 硬化 (ARCH-1a + TR-1a + B 失焦恢复 + `EditorServiceLifecycle`) 已提交并推送 (`5a0f495`，用户执行 git；origin/main 经 ls-remote 验证同步)。
- [ ] ARCH-1b: 抽 `LoopbackHttpHost` + `RecompilePrimitive` (host 外壳 + recompile 原语) 入 editor-core。
- [ ] 重跑增强端点的完整 EditMode + e2e 套件，更新验证基线计数。

## 关键设计文档

- [U3D AI Linker 设计](docs/superpowers/specs/2026-06-12-u3d-ai-linker-design.md)
  - 编排包的架构、Registry 白名单、manifest 事务、Junction 与三通道。
- [U3D AI Linker 实现计划](docs/superpowers/plans/2026-06-14-u3d-ai-linker-implementation.md)
  - 50 Task 的 TDD 实现计划 + Appendix B 类型/文件收敛权威表 (本次 mission 依此落地)。
- [dev-tools 增强设计](docs/superpowers/specs/2026-06-13-dev-tools-enhancements-design.md)
  - editor-debug/test-runner 增强与 editor-core 抽取的价值/优先级/路线图。
- [跨工程协作解决方案](docs/superpowers/specs/2026-06-13-cross-project-collaboration-solution.md)
  - 工具库 <-> 游戏工程协作; 文件通道优先、内外循环解耦、git 写归人。
- [lua-device-debug 设计](docs/superpowers/specs/2026-06-13-lua-device-debug-design.md)
- [editor-debug-mcp server 计划](docs/superpowers/plans/2026-06-12-editor-debug-mcp-server.md)
- [test-runner-mcp server 计划](docs/superpowers/plans/2026-06-13-test-runner-mcp-server.md)
