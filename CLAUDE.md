# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 仓库定位

Unity3D AI 自动化开发工具 monorepo。四个 UPM 包让 AI agent 不经 GUI 操作运行中的 Unity Editor，并把工具批量接入目标工程：

- `com.yoji.test-runner`：headless 跑 EditMode 测试 + 触发重编译（HTTP 21890）。
- `com.yoji.editor-debug`：任意 C# 反射调用 / 检视 Unity 类型与成员（HTTP+JSON 21891）。
- `com.yoji.lua-device-debug`：Unity Lua 运行时诊断的通用传输层（HTTP+JSON 21894）。
- `com.yoji.u3d-ai-linker`：Editor-only 编排包，按 Registry 白名单把上述工具批量装入目标工程，并同步 Skills/规则到 Claude Code / Codex。

目标环境：Windows + Unity 2022.3+ + Python 3.8+。`lua-device-debug` 已在 Unity 2022.3.62f2c1 Git UPM 安装和 EditMode 中验证，但目标工程必须注册 `ILuaDeviceDebugHost`。所有调试服务只绑定 127.0.0.1。本工具集仅面向**非 HybridCLR** 工程。

迁移工作区背景：三个调试工具的 Agent 侧资产与 Unity 侧服务已整合为可安装 UPM 包；旧的 client-only feval 运行时调试资产已移除，由 lua-device-debug 取代。

## 仓库结构

```text
Packages/
  com.yoji.test-runner/        # HTTP 测试服务 + Agent~/skills/test-runner-mcp/
  com.yoji.editor-debug/       # HTTP+JSON 反射服务 + Agent~/skills/unity-editor-debug-mcp/
  com.yoji.lua-device-debug/   # Lua 诊断传输层 + Agent~/skills/unity-lua-device-debug/
  com.yoji.u3d-ai-linker/      # Editor-only 编排包（Editor/ + Registry/ 内嵌快照）
Registry/
  stable.json / dev.json       # 双通道分发清单（linker 消费）
TestProjects/
  {test-runner,editor-debug,lua-device-debug,u3d-ai-linker}/   # 各包独立 headless 测试宿主
tools/
  run-editmode.ps1             # 唯一 headless EditMode 测试入口
docs/superpowers/{specs,plans}/  # 设计与实现文档
```

各包 `Agent~/skills/<skill>/` 放 SKILL.md + client.py + references。Unity 打包忽略 `~` 结尾目录，Agent 侧资产不进 Unity 编译。

## 工具与端口

| 包 | skill | 端口 | 协议 | minUnity |
|----|-------|------|------|----------|
| com.yoji.test-runner | test-runner-mcp | 21890（回退 21896/21897）| HTTP，真实状态码 | 2022.3 |
| com.yoji.editor-debug | unity-editor-debug-mcp | 21891（回退 21892/21893）| HTTP+JSON，恒 200 | 2022.3 |
| com.yoji.lua-device-debug | unity-lua-device-debug | 21894（无回退）| HTTP+JSON，真实状态码 | 6000.3 |
| com.yoji.u3d-ai-linker | -（Project Settings 面板）| -（Editor 编排）| - | 2022.3 |

## 常用命令

无统一构建。按模块验证：

```powershell
python -m py_compile <script.py>      # 改 Python 至少执行一次
python -m json.tool <file.json>       # JSON 校验

# headless EditMode（不需活 Editor，需本机装 Unity 6000.3.16f1）
pwsh -File tools/run-editmode.ps1 -Project test-runner
pwsh -File tools/run-editmode.ps1 -Project <tool|绝对路径> -Unity <unity.exe> -TimeoutSec 600
# 退出码：0 全绿 / 2 测试失败 / 3 非 Unity 工程或 Unity 未找到 / 4 超时
# -runTests 自动退出 Editor，不要加 -quit；不要对已被另一 Editor 打开的工程跑

# E2E 冒烟（需活 Editor，服务在线）
python Packages/com.yoji.test-runner/Agent~/skills/test-runner-mcp/references/run-e2e.py
python Packages/com.yoji.editor-debug/Agent~/skills/unity-editor-debug-mcp/references/run-e2e.py
```

run-editmode.ps1 的 `-Unity` 默认硬编码 `E:\Unity\Unity Editor\6000.3.16f1\Editor\Unity.exe`，换机器必须显式传 `-Unity`。`-Project` 接受 `TestProjects/` 下的目录名或绝对路径。

client.py（在各自 skill 目录下执行，或用全路径）：

```bash
# test-runner（子命令名与端点不同：status 对应 /test-status）
python .../test-runner-mcp/client.py ping
python .../test-runner-mcp/client.py recompile
python .../test-runner-mcp/client.py run-tests --mode EditMode --names Namespace.Class.Method
python .../test-runner-mcp/client.py list-tests --mode EditMode
python .../test-runner-mcp/client.py status --job-id <jobId>

# editor-debug（全局 flag --host/--port/--timeout 必须在子命令前）
python .../unity-editor-debug-mcp/client.py ping
python .../unity-editor-debug-mcp/client.py invoke --type <Type> --member <name> --kind get|set|call|index [--args ...] [--arg-types ...] [--target-instance-id <id>]
python .../unity-editor-debug-mcp/client.py invoke-chain --type <Type> --steps 'member:kind' 'member:kind:arg1,arg2'
python .../unity-editor-debug-mcp/client.py describe --type <Type>
python .../unity-editor-debug-mcp/client.py console --count 30 --filter error
python .../unity-editor-debug-mcp/client.py batch --file requests.json
python .../unity-editor-debug-mcp/client.py --timeout 120 recompile

# lua-device-debug（需目标工程注册 host）
python .../unity-lua-device-debug/client.py ping
python .../unity-lua-device-debug/client.py commands
python .../unity-lua-device-debug/client.py execute <command> --arg key=value [--allow-mutation]
python .../unity-lua-device-debug/client.py adb-forward [--serial <serial>]
```

改 editor-debug 的 SKILL.md / client.py / references 后必须重跑 `Packages/com.yoji.editor-debug/Agent~/skills/unity-editor-debug-mcp/install.ps1`，同步到 `~/.claude/skills/`。

## 架构

### 三服务共同模式（test-runner / editor-debug / lua-device-debug）

- Unity 侧服务由 `[InitializeOnLoad]` 随 Editor 自启，domain reload 时停止（`beforeAssemblyReload`）、完成后 `delayCall` 重启。
- recompile、UPM 安装、进 PlayMode 都触发 domain reload，服务下线 5-10s。recompile 的响应在下线之前返回——调用方必须随后轮询 ping 直到恢复，不能立即发下一个请求。
- recompile 后若存在编译错误，`[InitializeOnLoad]` 不再触发，服务永不恢复。先修编译错误。
- 编程触发的 domain reload 在 Editor 窗口失焦时被 Unity 节流（重启延迟可达 ~140s）；自动化需给足轮询超时，或让用户聚焦 Editor 窗口。

### test-runner（com.yoji.test-runner）

- 用**真实 HTTP 状态码**（200/202/400/404/409/413/500），与 editor-debug 不同——必须看状态码，不能只看 body。错误 body 形如 `{success:false, error}`。
- 状态机 `Idle | Running | Compiling`（`JobStore.cs:8`）；非 Idle 时 `/run-tests` 与 `/recompile` 返回 409。先 ping 看状态再派发。
- `/run-tests` 异步返回 202 + jobId，轮询 `/test-status`；结果 XML 写 `<project>/Temp/TestRunnerMCP/result_<jobId>.xml`（NUnit XML，与 `-batchmode -runTests` 同格式），job JSON 写 `job_<jobId>.json`。
- `/test-status` 的 `failures[]` 直出每个失败叶子用例（name/message/stackTrace，上限 50 条、字段截断 4000 字符），不必自己 parse XML。结果缓存 domain reload 即丢，jobId 失配返 404，需重跑。
- testNames 必须全限定 `Namespace.Class.Method`；拼错触发 0 命中守卫 → `status=error`（不假绿）。先 `/list-tests` 校名。
- testMode 严格 `EditMode` / `PlayMode`；PlayMode 当前返 400（仅 EditMode 阶段）。空过滤（testNames/assemblies/categories/groups 全空）= 跑整套。
- POST body > 1MB 返 413。JSON 请求一律写 Python 脚本到 `<project>/Temp/` 执行，不用 shell/curl 拼 JSON（Windows 转义陷阱）。

### editor-debug（com.yoji.editor-debug）

- 所有响应 HTTP 恒 200，错误在 body `error` 字段，envelope `{ok, result|error, elapsedMs}`。永远检查 `ok`/`error`，不看状态码。
- 21891 被占静默落 21892/21893，从 `/ping` 的 `result.port` 取实际端口，client.py 用 `--port` 指定；全部被占则不启动并报错。
- 全局 flag `--host`/`--port`/`--timeout` 必须在子命令前：`client.py --port 21892 ping`。
- `UnityEngine.Object` 派生类降级为 ref 摘要 `{__ref, instanceID, type, name, ...}`，用 `--target-instance-id`（亦接受 `--target-entity-id`）续接访问。Unity 6.4+ 用 `EntityId`（UInt64 十进制字符串），线上字段名仍是 `instanceID`。
- 多步访问优先 `invoke-chain`（一次 HTTP 链式，中间值留服务端）；`/eval` 仅简单场景兜底。
- `/eval` **默认关闭**（`EditorDebugMCP.cs` `c_AllowEval=false`），需改源码 + 重编才开；不支持 lambda / new / typeof。
- 重载歧义（AmbiguousMatchException）用 `--arg-types` 消解；类型解析失败（TypeAccessException）给类型串追加 `, AssemblyName`。
- `/console` 读真实日志条目（message/type/file/line），取代旧的 invoke LogEntries 模式。`/batch` 一次最多 64 个只读请求，单个失败不中断整批。
- Unity 2022.3 多数内置 Editor 窗口是 IMGUI，`rootVisualElement.childCount==0` 正常；仅 InspectorWindow 完整 UIToolkit 化。

### lua-device-debug（com.yoji.lua-device-debug）

- 通用传输层，**不含任意 Lua eval / C# 反射 eval / HybridCLR / xLua**。仅 `/ping` `/commands` `/execute`，真实 HTTP 状态码。端口 21894 单一无回退。
- 目标工程必须实现 `ILuaDeviceDebugHost`（IsReady/DescribeCommands/Execute）并 `LuaDeviceDebugRuntime.RegisterHost()` 注册；未注册时 `/commands` `/execute` 返 409 HOST_NOT_READY，未知命令返 404 COMMAND_NOT_FOUND。同一时刻只能注册一个 host。
- 变更类命令双重闸：descriptor 声明 `mutating=true` 且 CLI 传 `--allow-mutation`，缺一返 403 MUTATION_DENIED。
- 主线程队列上限 16（超返 429 QUEUE_FULL），单请求主线程等待默认 5s（超返 408）。请求体 1MB / 响应体 4MB 上限（413），JSON 深度 32 / 成员 4096。
- Editor 侧 `[InitializeOnLoad]` 自启；Player 侧仅 `DEVELOPMENT_BUILD && !UNITY_EDITOR` 且 Android 平台才起。Android 用 `adb-forward`（复用同目标转发不夺所有权，`adb-remove` 只删本 CLI 建的）；多设备需 `--serial`。
- 无 E2E 套件；EditMode 有 fake-host 测试。

### u3d-ai-linker（com.yoji.u3d-ai-linker，已实现）

Editor-only 编排包，8 子系统（LINK-0..LINK-7）全部落地，239/239 EditMode 绿。从 `Edit > Project Settings > U3D AI Linker` 按 Registry 白名单批量装工具到目标工程并同步 Agent 资产。

- **当前面板所有动作按钮被门控禁用**（`U3DAILinkerSettingsProvider.cs` `ActionsWired=false`），`Request*` 仅 `Debug.Log`，只展示 registry/settings 状态。真实安装/同步/Junction/域重载续跑等副作用为 live-editor 手验项，未接线。
- Registry 解析硬约束：`schemaVersion=1` 是硬门槛（`RegistryParser.cs`），未知值整体拒绝；`MissingMemberHandling.Error`，未知字段抛异常不静默跳过；只白名单 `com.yoji.*`，`packagePath` 必须等于 `Packages/<packageName>`。
- 双通道 revision：stable 为 `<id>-v<semver>` tag（如 `editor-debug-v0.1.0`，各工具独立版本）；dev 为 40 位 SHA（仓库内当前是全零占位，需 `git ls-remote` 解析 main HEAD 后才可用，禁止长期留占位 / `#main`）。
- manifest 七步原子事务：备份 → 只改 `com.yoji.*` 依赖 → 写 `.u3d-ai-linker.tmp` → parse 校验 → `File.Replace` → 写 `operation.json`。安装队列严格串行，`operation.json`（`Library/U3DAILinker/`）先写后 `Client.Add`，跨 domain reload 续跑；linker 自更新永远排队尾。
- Agent 同步：内容哈希 + 所有权 `.u3d-ai-owner.json` 校验，无所有权文件的目录视为用户内容（Foreign）不得覆盖；六步事务式目录复制 + Windows Junction（P/Invoke DeviceIoControl，非 mklink）落到 `.u3d-ai-linker/skills/<tool>` 链到 `.claude`/`.agents`。
- 目标工程 CLAUDE.md/AGENTS.md 托管块定界 `<!-- u3d-ai-linker:start -->` / `<!-- u3d-ai-linker:end -->`，.gitignore 用 `# >>> u3d-ai-linker >>>` / `# <<< u3d-ai-linker <<<`；标记重复或不平衡返 Conflict 不写入，块外用户内容不动。
- 仓库 URL 硬编码 `https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git`（`ToolUrlBuilder.cs`）。设置分两份：`ProjectSettings/U3DAILinkerSettings.asset`（可提交、禁绝对路径）vs `UserSettings/U3DAILinkerUserSettings.asset`（本地，存 LocalRepoRoot）。
- u3d-ai-linker 自身也在 Registry entries 里，用于诊断当前安装版本并确保自更新排队尾。

### Registry 与分发

- `Registry/stable.json` + `dev.json` 是分发源，linker 内嵌一份逐字节一致的快照（`Packages/com.yoji.u3d-ai-linker/Registry/`）。
- entry 字段：`id/status/kind/order/packageName/packagePath/revision/defaultEnabled/userToggle/agentAssets/minUnity/dependsOn`。
- 5 个 stable entry：editor-core、editor-debug、test-runner、lua-device-debug、u3d-ai-linker。kind `infra`（editor-core，agentAssets=null、userToggle=false）/ `tool`（其余）。当前 stable 五项均为 `ready`，revision 对应各自发布 tag。
- editor-core（`com.yoji.editor-core`）是已实现的共享基础包，承载主线程调度、HTTP 服务生命周期与端口分配等公共能力；editor-debug、test-runner、lua-device-debug 均声明 `dependsOn: [editor-core]`。

### TestProjects 与 headless 测试

- `TestProjects/<tool>/` 是各包独立的最小 Unity 宿主（6000.3.16f1），经 `file:../../../Packages/com.yoji.<tool>` 引用并放进 `testables[]`。Assets/ 不入 git（仅 Packages/ 与 ProjectSettings/ 跟踪）。
- `tools/run-editmode.ps1 -Project <tool>` 启 batchmode `-runTests`，parse NUnit3 XML，结果落 `TestProjects/<tool>/TestResults/editmode-<stamp>.{xml,log}`。

## 接入目标工程的注意

- 端口与 Tap4Fun 内部包完全重叠：`com.tfw.test-runner-mcp` 用 21890、`com.tfw.unity-editor-debug-mcp` 用 21891（+21892/21893）。目标工程若已装 `com.tfw` 这套，再装 `com.yoji` 会端口竞争、二者并存——同一工程二选一。
- 本工具集仅非 HybridCLR；`lua-device-debug` 支持 Unity 2022.3+，但目标工程必须注册 `ILuaDeviceDebugHost` 才能执行项目 Lua 诊断命令。

## 目录性质

- 交付物：`Packages/`、`Registry/`、`TestProjects/`、`tools/`、`docs/`、`README.md`、`progress.md`。
- 本地工具产物（非交付物，已 .gitignore）：`mcps/`、`.agents/`、`.claude/`；TestProjects 的 `Library/` `TestResults/`。
- `progress.md` 是逐迭代进度与验证基线的权威来源。根目录 `AGENTS.md`（本地未跟踪）与空 `changelog.md` 当前与新结构脱节——AGENTS.md 仍描述旧的 `test-runner-mcp/` `feval-runtime-debug/` 顶层目录，引用时注意。

## 约定

- 各包内 `Agent~/skills/<skill>/{SKILL.md, client.py, references/}`，统一小写 kebab-case；不依赖仓库外隐式相对路径，外部依赖在 SKILL.md 显式声明。
- Python：4 空格缩进、类型标注、snake_case。PowerShell：完整参数名、`-LiteralPath`。
- 协议、CLI 参数或传输逻辑变化时，同步更新 E2E、README 与对应 SKILL.md。文档与真实 CLI 不得漂移。
- 提交信息简短祈使句，可用 `docs:` / `fix:` / `feat:` 前缀，一个提交只处理一个工具或一个问题。
- 不提交凭据、内网地址、机器专属绝对路径；示例必须可复制执行。
- 无法运行 Unity 侧验证时，明确记录未验证边界，不要用"服务未启动"代替代码验证结论。
