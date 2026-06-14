# 跨工程协作解决方案 (Tools repo <-> Game project)

> 适用对象: `U3D-Dev-Tools-AI`(工具 monorepo)与消费它的 Unity 游戏工程
> (如 `SLG_Prototype`)之间的协作。
> 触发动机: 2026-06-13 一次会话中, AI 辅助开发时 git/shell 通道严重不可靠,
> 而文件通道始终稳定。本方案把这次教训固化为可执行的协作架构。
> 运行前提: Claude Code 与 git 仓库都在同一台 Windows 机器上。

## 1. 背景: 本会话实测到的现象

一次普通的"改文档 + 提交 + 推送"任务中, 出现三类故障和一个稳定通道:

| 通道 | 工具 | 表现 |
|------|------|------|
| Shell 路由 | Bash / PowerShell | 命令间歇性落到一个 macOS 会话 `ya@yas-Mac-mini`, 根本没在本机执行 |
| Git 读 | `git status/diff/log/rev-parse` | 输出被替换成固定串(`9adfb33`、`M README.md`)或 status 形态, 与所问命令无关 |
| Git 写 | `git commit/push` | 返回了像样的 hash 和 `a..b main -> main`, 但事后证明不可信 |
| 文件 | Read/Glob/Grep/Edit/Write | 全程零故障, 精确实时 |

铁证: 当 shell 反复声称本地 HEAD 是 `9adfb33`、`origin/main` 是 `70bf8e6/9adfb33`、
并回显三次"push 成功"时, 直接用文件通道读 git 的引用文件得到的真实值是:

- `.git/refs/heads/main` = `adc698d...`(真实本地 HEAD, 与 shell 说的完全不同)
- `.git/refs/remotes/origin/main` = `a2285b4...`(会话开始时的提交, 引用从未前进)

也就是说: **本会话的所有提交都没真正推上云, 那些"成功"回显全是假的;
而文件通道一读就读出了真相。**

## 2. 根因诊断

- 故障A (Shell 串 Mac): Claude Code 的 shell 后端 PTY 被一个残留的 macOS 交互
  会话(ssh / tmux / Remote-SSH 之类)占用或交错, 命令落到了错误的机器。
- 故障B (Git 读污染): 叠加两种嫌疑 -- RTK hook 对 git 查询的改写/缓存, 以及
  PTY 输出交错把上一条结果顶上来。表现为"问什么都返回同一个旧值"。
- 故障C (Git 写假成功): 写命令的回显同样经过被污染的 shell, 不能采信; 真实
  结果(引用是否前进)只能由文件核实。

为什么文件通道幸免: Read/Glob/Grep/Edit/Write **直连文件系统**, 既不经过 shell
PTY, 也不被 RTK 的命令改写触及。**所有故障都集中在"经过 shell"的路径上。**
这是本方案的支点。

## 3. 四条第一性原则

1. 通道分层: 文件通道是"主干", 可靠; shell/git 是"随时可能丢的支线"。
   任何协作流程都不能把正确性押在 shell 回显上。

2. 用文件读 git, 不经 shell: git 状态本身就是文本文件。
   - 本地 HEAD: `.git/refs/heads/main`
   - 上次已知远端: `.git/refs/remotes/origin/main`
   - 当前分支: `.git/HEAD`
   - 历史/操作日志: `.git/logs/HEAD`(reflog, 纯文本)
   - 打包引用兜底: `.git/packed-refs`(loose ref 不存在时看这里)
   只读这些文件, 就能精确、不可污染地得到同步状态。

3. 内外循环解耦: 跨工程协作有两个频率截然不同的循环。把"不可靠"全部隔离到
   低频的外循环里(见第 4 节)。

4. 写归人 + 文件兜底验证: commit/push/tag 这类有副作用又依赖网络的写操作交给
   人执行; AI 永远不声称一个自己无法用文件通道核实的 git 结果。

## 4. 跨工程协作方案

### 4.1 内循环 -- 高频, 零 git, 零网络

游戏工程通过 UPM `file:` 本地引用直接指向本机工具库的包目录。本仓库测试工程
已在用这种形态:

    "com.yoji.lua-device-debug": "file:../../../Packages/com.yoji.lua-device-debug"

把它定为内循环标准:

- 我在工具库里 Edit 一个 `.cs`, 游戏工程的 Unity 立刻看到改动(file: 本地包是
  符号化引用, 改动即时生效并触发域重载重编译), 全程不 clone / fetch / push /
  不碰网络 / 不碰 shell。
- 验证也不走 git: 用工具库自带的 HTTP 服务 -- test-runner-mcp(21890)跑测试、
  editor-debug-mcp(21891)做反射检视, 在游戏工程 Unity 在线时直接拿结果。

对 `lua-device-debug` 这类"传输层在工具库、适配器在游戏工程"的双向开发尤其
关键: 传输层在工具库改, `ILuaDeviceDebugHost` 适配器在游戏工程改, 两侧都在
同一台 Windows、都走文件通道, 我可同时编辑两侧, Unity 一次域重载全部生效。
这把最痛的"双仓 ping-pong"压成了单机文件编辑。

约束: `file:` 引用是本机路径, 换机即断, **不可提交进游戏工程的共享/CI 依赖**。
它只服务本机内循环。团队/CI 用的依赖必须是外循环的 tag。

### 4.2 外循环 -- 低频, git 发布, 人兜底, 容忍网络抖动

触发: 工具库某工具达到可发布里程碑(内循环已验证通过)。

1. 人在工具库打 tag(如 `lua-device-v1.0.0`)并 push; 网络抖动就重试, 失败不
   影响内循环继续工作。
2. 游戏工程把该工具依赖从 `file:` 切到 stable Git URL
   (`...?path=/Packages/<pkg>#<tag>`), 或经 `u3d-ai-linker` 的 Install/Refresh。
3. 这一步天然容忍 git 不可靠: 低频、由人执行、可重试、失败时游戏工程可回退到
   上一个 `file:` 或上一个 tag。

核实不靠 push 回显, 靠文件: push 后读 `.git/refs/remotes/origin/main`, 看引用
有没有前进到新 tag/commit。没前进就是没成功(本会话正是这样戳穿了假回显)。

### 4.3 dev / stable 切换

- dev(内循环): 游戏工程依赖用 `file:` 指向本机工具库 -> 即时、可靠、不上库。
- stable(外循环): 游戏工程依赖用 `#<tag>` Git URL -> 可提交、可复现、跨机。

这正好接 `u3d-ai-linker` 设计的双通道(见
`2026-06-12-u3d-ai-linker-design.md`): Dev 通道把工具固定到 `main` 的某个
commit SHA, Stable 通道固定到 tag。Linker 负责在游戏工程里安全改写依赖与同步
`Agent~/` skill, 不手工编辑 manifest。

## 5. AI-人 协作契约

1. 分工: AI 做一切文件级工作(改两侧代码、读 git 状态文件、写交接清单、用
   Unity HTTP 服务验证); 人做一切 git 写操作和需要交互登录的网络操作。
   (已存为 memory: `git-commits-user-handles`。)

2. 文件化交接清单(handoff): AI 完成一批改动后不去 git, 而是写一个文件(如
   `_planning/handoff.md`), 列出: 改了哪些文件、属于哪个工程、建议的提交信息、
   建议的 tag、以及"人需要在终端执行的 git 命令"。人照着跑。用可靠的文件通道
   桥接 AI 的"意图"和人的"执行", 绕开 shell。

3. AI 的 git 自检例程(只读, 走文件): 需要同步状态时, 读
   `.git/refs/heads/main` + `.git/refs/remotes/origin/main` + `.git/logs/HEAD`,
   对比得出"本地领先 N、上次确认远端在哪"。绝不用 shell 回显下结论。

4. 失败语义: AI 跑了 shell 命令后, 只有结果能被文件通道交叉核实时才采信;
   不能核实就明确标注"未确认", 不写进"已完成"。

5. Shell 身份探针: 每次涉及 shell 前, 先用一条会暴露身份的命令(如读
   `$env:COMPUTERNAME`, 或比对一个只在本机仓库存在的哨兵文件)确认 shell 落在
   本机; 一旦发现串到 Mac, 停手并报告, 不硬重试。

## 6. 基础设施根治 (交给人, 因为要动环境)

消除 Mac 串入:

1. 关掉所有到 `yas-Mac-mini` 的活动连接(ssh / tmux / Remote-SSH 窗口)。
2. `/exit` 重启 Claude Code, 让 shell 后端重新 spawn 本机 PowerShell。
3. 重启后用身份探针确认落地本机(读 `$env:COMPUTERNAME` 等)。
4. 若仍串: 检查 RTK 配置与 Claude Code 的 shell/terminal 设置, 排除残留的远程
   代理或默认 shell 指向。

RTK 输出污染: 诊断期可对关键 git 查询用 `rtk proxy <cmd>` 绕过过滤; 但更稳的是
按原则 2 干脆不用 shell 查 git, 走文件读法。

分层的价值: 这一层修好后, shell 偶尔仍可用于"可被文件核实"的操作; 修不好时,
内循环(文件)完全不受影响, 协作不中断。基础设施坏了, 主干照常。

## 7. 置顶紧急待办

**本会话的本地提交没有真正上云。** 文件通道证据: 本地 `main` = `adc698d...`,
而 `origin/main` 仍停在 `a2285b4...`(会话开始的提交)。所有"push 成功"是假的。

行动(网络稳定时, 由人执行):

1. 在本机终端确认身份未串 Mac(`$env:COMPUTERNAME`)。
2. `git status` / `git log --oneline -8` 核对本地领先了哪些提交。
3. `git push` 真正推送。
4. 用文件通道核实: 读 `.git/refs/remotes/origin/main`, 确认它前进到了
   `adc698d...`(或更新)。没前进就是还没成功, 重试。

在确认上云前, **不要清盘、不要换机**, 否则本地领先的提交会丢。

## 8. 一页速查 (出问题时走哪条通道)

| 我想做 | 走这条 | 不要走 |
|--------|--------|--------|
| 看/改代码、文档 | Read / Edit / Write | -- |
| 找文件 | Glob / Grep(否定 glob 排除 `Library`) | shell `ls`/`find` |
| 查本地 HEAD | 读 `.git/refs/heads/main` | `git rev-parse HEAD`(会被污染) |
| 查远端同步 | 读 `.git/refs/remotes/origin/main` | `git status` 的 ahead/behind |
| 在游戏工程验证工具 | test-runner-mcp / editor-debug-mcp HTTP | 跨仓 git 操作 |
| 提交 / 推送 / 打 tag | 人在终端做, AI 写 handoff 清单 | AI 自己 commit/push |
| 用 shell 前 | 先跑身份探针确认本机 | 直接硬跑并相信回显 |
