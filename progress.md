# 工程进度 (Progress)

> U3D-Dev-Tools-AI - 面向 AI 辅助工作流的 Unity3D 开发工具集
>
> 最后更新: 2026-06-14 (main 在 2c3ba9a 之上追加一组 HTTP body 硬化 / `/eval` 默认关闭 / linker 面板未接线门控；三工具已在活 Editor + 本地 `file:` 源码端到端逐端点验证)

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
| test-runner-mcp | 21890 (回退 21896/21897) | HTTP | 已迁入包 | 服务包 (`com.yoji.test-runner`) | 可用 (仅 EditMode，PlayMode 计划中)；6000.3.16f1 验证 |
| unity-editor-debug-mcp | 21891 (回退 21892/21893) | HTTP+JSON | 已迁入包 | 服务包 (`com.yoji.editor-debug`) | 可用；6000.3.16f1 验证 |
| unity-lua-device-debug | 21894 | HTTP+JSON | 已迁入包 | 服务包 (`com.yoji.lua-device-debug`) | 传输层就绪；已在 SLG_Prototype 真实 xLua 适配器端到端验证 (Play 模式活 VM 执行 5 只读命令) |
| **u3d-ai-linker** | - | - (Editor 编排) | 同步 Skills/规则 | 编排包 (`com.yoji.u3d-ai-linker`) | **实现完成 (8 子系统 / 239 EditMode 绿)；已提交+推送 (2c3ba9a)；面板/安装/Junction 等真实副作用待 live-editor 手验** |

> 端口 21894 冲突已修复: test-runner 的回退端口由 `21894/21895` 改为 `21896/21897`
> (`TestRunnerMCP.cs:22`), 不再与 lua-device-debug 的固定端口 21894 重叠。lua-device-debug
> 的 21894 仍为单一固定端口、无回退, 但现已无兄弟包争用。

## 各工具进度

### 1. test-runner-mcp (可用，EditMode 阶段)

不开 Unity GUI 即可触发重编译并跑 EditMode 测试，结果回传给 AI agent。

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
- 阶段边界: `testMode:"PlayMode"` 仍返回 400；空测试过滤即运行整个套件。
- 下一步: PlayMode 执行 (设计见增强文档 TR-1)；推荐先做 TR-1a spike
  (`EnterPlayModeOptions.DisableDomainReload`，绕开域重载难题，live e2e 验证收尾)。

### 2. unity-editor-debug-mcp (可用)

端口 21891 上的 HTTP+JSON 反射服务，供 AI 检视与调用 Unity 类型/成员。

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

端口 21894 上的 HTTP+JSON 传输层，用于 Editor 与 Android Development Build 中的
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
- 项目侧适配器已落地并端到端验证 (2026-06-14): `SLG_Prototype/Core_Client` 的
  `XLuaRuntime` 实现 `ILuaDeviceDebugHost`，`GameBootstrap.Awake` 在 Play 模式
  `Start("boot.lua")` 注册 host (退出 Play 时 `Dispose → UnregisterHost`)；boot.lua
  提供 `__slg_debug_describe_commands`/`__slg_debug_execute` 与 5 个只读命令。活 Editor
  里 Play 模式实测: system.info / module.list / config.tables / red_dot.dump /
  data_center.dump 在真实 xLua VM 执行返回真实游戏状态；EditMode 无 host 时
  `/commands` `/execute` 正确 409 HOST_NOT_READY，未知命令 404 COMMAND_NOT_FOUND。
- 包边界不变: 通用包仍不放置 SLG 专用全局 (如 `__slg_debug_execute`)，这些由目标项目提供。

### 4. u3d-ai-linker (实现完成，已提交+推送)

Editor-only UPM 编排包 `Packages/com.yoji.u3d-ai-linker`：从 `Edit > Project Settings >
U3D AI Linker` 一键把本仓库工具按白名单批量装入目标工程，并把项目级 Skills/规则同步给
Claude Code 与 Codex。首版仅 Windows、仅 Unity 2022.3+，不涉及 HybridCLR。

- 由根目录 `Registry/` 白名单 (stable/dev/local 三通道) 驱动安装决策；用 manifest 事务
  把工具写成顶层 Git-URL 依赖以解决"git 子包不自动解析同级依赖"的阻断；Agent 资产用
  事务式目录复制 + Windows Junction 落地到 `.u3d-ai-linker/skills/<tool>` 并链到
  `.claude`/`.agents`；跨域重载的安装/同步以持久化 `operation.json` 记账、`[InitializeOnLoad]`
  域重载恢复。
- **本次实现 (mission-runner，2026-06-14)：8 个子系统 / 50 Task 中 48 个 headless 落地，
  batchmode EditMode `total=239 passed=239 failed=0`：**
  - LINK-0 资产探针决策门 (directory vs zip-fallback)
  - LINK-1 包骨架 + 包常量 + Project Settings 入口
  - LINK-2 Registry 解析/白名单校验/URL 生成/拓扑排序
  - LINK-2b manifest 七步原子事务 (冲突预检 + 回滚 + 手动改动检测)
  - LINK-3 UPM 安装队列 + 域重载恢复决策器
  - LINK-4 内容哈希 / ownership / Windows Junction / 事务式 Agent 同步
  - LINK-5 Fragment 确定性合并 + CLAUDE/AGENTS/.gitignore 托管区块
  - LINK-6 Project Settings 面板 + 三通道 URL + 状态分离 + RefreshDev/Restore
  - LINK-7 工具 fragments + `Registry/{stable,dev}.json` (根与包内快照逐字节一致) + README 修正
- 资产模式: SLG_Prototype 现有 git 缓存实测 `Agent~/SKILL.md` 可读 → Unity 6000.3 的
  `git?path=` 保留 `~` 目录，directory 模式已经验预确认 2/3 探针目标。
- **未完成 (人工域，交用户)：**
  - 真实副作用 (真实安装 / 域重载续跑 / Windows Junction / 面板渲染 / Local 通道 /
    manifest 事务对真实 manifest) 为 e2e/手动验证，矩阵标 `live-editor-slg`，可经
    SLG_Prototype 本地 `file:` 路径 + editor-debug/test-runner 的 MCP 在活 Editor 里亲验。
  - Task 5 探针决策门 (需真实 git 装 linker 跑探针；残留仅空目录 `BundledSkills~`)。
  - Task 49 升 version 并打 `<tool-id>-v<semver>` tag (人工 git；首版不打 tag)。
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
- **u3d-ai-linker**: Project Settings 面板的 Install/Sync/Repair/Rollback 动作在未接线前以
  `ActionsWired = false` 统一禁用 + HelpBox 说明 (本版只暴露 registry/settings 状态)；域重载
  恢复不再在 `Requested` 时自调度续跑 (`ShouldScheduleFollowUp`)，改由域重载静态构造或显式
  Retry/Resume 接续。
- `RequestBodyReader` 当前在 editor-debug / test-runner 各存一份 (asmdef 隔离)，与
  `MainThreadDispatcher` 同属 ARCH-1 (抽 `com.yoji.editor-core`) 的待去重项；`MemoryStream`
  已按 `contentLength` 预分配 (/simplify)。

## 增强路线图 (dev-tools-enhancements)

editor-debug 与 test-runner 已在真实工程中被 AI agent 使用，由此暴露的缺口与
价值/成本评估、分阶段路线图见
[dev-tools 增强设计](docs/superpowers/specs/2026-06-13-dev-tools-enhancements-design.md)。

- P0 do-now (已落地 main): TR-2 失败详情、TR-4 端口修正、ED-1 `/console`、
  ED-5 `/ping` 编辑器态、ED-3 `--usings` 清理。
- P1 do-next (部分落地): TR-3 0 命中守卫 + `/list-tests` (已落地)、ED-2 `/batch`
  (已落地)；TR-1a PlayMode spike、ARCH-1b 抽 `LoopbackHttpHost`/`RecompilePrimitive`
  待做。
- 待做 (路线图): ARCH-1 抽取 `com.yoji.editor-core` (当前 editor-debug 与 test-runner
  仍各自维护 `MainThreadDispatcher.cs` + HTTP/recompile 脚手架，约 150-200 行并行)；
  TR-1b/c PlayMode 重载存活路径 (条件触发)；ED-4 只读/denylist、ED-6 `/describe` 过滤。

## 验证基线

| 组件 | 验证环境 | 结果 |
|------|----------|------|
| unity-editor-debug-mcp | Unity 6000.3.16f1 | 基线 74 EditMode + 13 HTTP e2e 通过 (增强端点前) |
| test-runner-mcp | Unity 6000.3.16f1 | EditMode 套件通过 (增强端点前) |
| unity-lua-device-debug | Unity 6000.3.16f1 | 8/8 fake-host EditMode 测试通过 |
| u3d-ai-linker | Unity 6000.3.16f1 (batchmode) | **239/239 EditMode 通过 (纯逻辑全套；真实副作用为 live-editor 手动)** |

注: 近期增强 (`/console` `/batch` `/list-tests` `failures[]` 等) 已扩展对应 e2e
fixture (见各包 `references/run-e2e.py`)，但前三行通过计数为增强落地前的基线；增强后的
完整通过数需在 Unity 在线时重跑一遍确认后更新。u3d-ai-linker 经独立宿主
`TestProjects/u3d-ai-linker` 跑 headless batchmode (`tools/run-editmode.ps1 -Project
u3d-ai-linker`)；各测试工程的 `Library/` 与 `TestResults/` 为构建产物。

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

注: 此为活 Editor 逐端点功能手验，非重跑 `run-e2e.py` 计数套件；增强端点的完整 e2e
通过数仍待在线重跑确认 (见上方注)。

运维注记: 编程触发 `/recompile` 或设 `isPlaying=true` 进 Play 引发的域重载，在 Unity Editor
**窗口失焦时会被节流** (服务重启延迟可达 ~140s，`hostRegistered` 长时间空)；需用户**聚焦
Editor 窗口**才即时完成转换并重启服务。自动化驱动脚本应给足轮询超时并预期这一人工聚焦步骤。

## 待办与下一步

- [x] u3d-ai-linker: 已提交并推送到云端 (2c3ba9a，用户执行 git push；origin 1ebe62c→2c3ba9a 已验)。
- [ ] u3d-ai-linker: 在真实 Editor (SLG_Prototype 本地 file: 路径) 验证面板 / 安装 /
      Junction / 域重载续跑 / Local 通道等 live-editor-slg 项 (9 条 Manual Verification)。
- [ ] u3d-ai-linker: 真实 git 装 linker 跑探针决策门 (Task 5)，确证 `BundledSkills~` 存活。
- [ ] test-runner-mcp: 实现 PlayMode 测试执行 (先 TR-1a spike: DisableDomainReload + live e2e)。
- [ ] unity-editor-debug-mcp: 在 Unity 6.4 安装上编译并验证 entity id 分支后发布。
- [x] unity-lua-device-debug: SLG_Prototype 已实现 xLua 适配器 (`XLuaRuntime: ILuaDeviceDebugHost`) 与 5 个 SLG 命令，活 Editor Play 模式端到端验证通过 (本仓库范围外，2026-06-14)。
- [ ] ARCH-1: 抽取 `com.yoji.editor-core` (先做 ARCH-1a dispatcher 移动，再做 ARCH-1b host/recompile 原语)。
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
