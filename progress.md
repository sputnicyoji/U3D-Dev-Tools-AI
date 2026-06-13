# 工程进度 (Progress)

> U3D-Dev-Tools-AI - 面向 AI 辅助工作流的 Unity3D 开发工具集
>
> 最后更新: 2026-06-13 (main = cdab72f, 已与 origin/main 同步)

本仓库目前是一个迁移工作区 (migration workspace)，正在把三个 Unity 调试工具的
Agent 侧资产与 Unity 侧服务逐步整合为可安装的 UPM 包。本文件汇总各工具的整体
进度、验证基线与下一步计划；逐迭代的执行记录见 `_planning/mission_plan.md` 与
`_planning/mission_notes.md`。

范围边界: 本仓库仅面向非 HybridCLR Unity 工程；旧 client-only 运行时表达式调试资产已移除。

## 状态总览

| 工具 | 端口 | 协议 | Agent 侧 | Unity 侧服务 | 状态 |
|------|------|------|----------|--------------|------|
| test-runner-mcp | 21890 (回退 21896/21897) | HTTP | 已迁入包 | 已迁入包 (`com.yoji.test-runner`) | 可用 (仅 EditMode，PlayMode 计划中)；已在 6000.3.16f1 验证 |
| unity-editor-debug-mcp | 21891 (回退 21892/21893) | HTTP+JSON | 已迁入包 | 已迁入包 (`com.yoji.editor-debug`) | 可用；已在 6000.3.16f1 验证 |
| unity-lua-device-debug | 21894 | HTTP+JSON | 已迁入包 | 已迁入包 (`com.yoji.lua-device-debug`) | 传输层包已就绪 (transport-only)；项目侧 Lua 适配器待实现 |

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
- 近期增强 (已落地 main cdab72f):
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
- 近期增强 (已落地 main cdab72f):
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
- 下一步: 目标项目 (如 SLG_Prototype) 需注册 xLua 适配器、Lua dispatcher 与
  SLG 专用命令；通用包不放置 SLG 专用全局 (如 `__slg_debug_execute`)。

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

| 工具 | 验证环境 | 结果 |
|------|----------|------|
| unity-editor-debug-mcp | Unity 6000.3.16f1 | 基线 74 EditMode + 13 HTTP e2e 通过 (增强端点前) |
| test-runner-mcp | Unity 6000.3.16f1 | EditMode 套件通过 (增强端点前) |
| unity-lua-device-debug | Unity 6000.3.16f1 | 8/8 fake-host EditMode 测试通过 |

注: 近期增强 (`/console` `/batch` `/list-tests` `failures[]` 等) 已扩展对应 e2e
fixture (见各包 `references/run-e2e.py`)，但上表通过计数为增强落地前的基线；增强后的
完整通过数需在 Unity 在线时重跑一遍确认后更新。测试工程位于
`TestProjects/{editor-debug,test-runner,lua-device-debug}`，其 `Library/` 与
`TestResults/` 为构建产物；另已加入 headless batchmode EditMode runner 作为离线
执行路径。

## 待办与下一步

- [ ] test-runner-mcp: 实现 PlayMode 测试执行 (先 TR-1a spike: DisableDomainReload + live e2e)。
- [ ] unity-editor-debug-mcp: 在 Unity 6.4 安装上编译并验证 entity id 分支后发布。
- [ ] unity-lua-device-debug: 在目标项目实现 xLua 适配器与 SLG 命令 (本仓库范围外)。
- [ ] ARCH-1: 抽取 `com.yoji.editor-core` (先做 ARCH-1a dispatcher 移动，再做 ARCH-1b host/recompile 原语)。
- [ ] 重跑增强端点的完整 EditMode + e2e 套件，更新验证基线计数。

## 关键设计文档

- [U3D AI Linker 设计](docs/superpowers/specs/2026-06-12-u3d-ai-linker-design.md)
  - 规划中的公开 UPM 包与迁移约束。
- [dev-tools 增强设计](docs/superpowers/specs/2026-06-13-dev-tools-enhancements-design.md)
  - editor-debug/test-runner 增强与 editor-core 抽取的价值/优先级/路线图。
- [跨工程协作解决方案](docs/superpowers/specs/2026-06-13-cross-project-collaboration-solution.md)
  - 工具库 <-> 游戏工程协作; 文件通道优先、内外循环解耦、git 写归人。
- [lua-device-debug 设计](docs/superpowers/specs/2026-06-13-lua-device-debug-design.md)
- [editor-debug-mcp server 计划](docs/superpowers/plans/2026-06-12-editor-debug-mcp-server.md)
- [test-runner-mcp server 计划](docs/superpowers/plans/2026-06-13-test-runner-mcp-server.md)
