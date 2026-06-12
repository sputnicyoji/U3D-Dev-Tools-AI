---
name: feval-runtime-debug
description: "通过 feval 连接 Unity/HybridCLR 运行时 endpoint（本地 Unity Editor 或 Android 真机），执行 C# 表达式、读状态、调项目方法。**首条消息若已含 endpoint/IP/调试目标则直接使用，不再追问**；带具体调试任务（X 报错 / 为什么 Y / Z 不生效 等）时按 systematic-debugging 流程取证定位根因。本地工程未启动时可通过 unity_bridge 自动启 Editor 并进 Play Mode。脚本优先 stdio CLI。自动化执行必须加 --no-console-fallback，禁止控制台/PowerShell 弹窗。输出 JSON。仅 Windows。若须改工程代码须事先征得开发者同意并说明需配合重编/重启后再验证。"
---

# Feval 运行时调试

## 启动流程（每个新会话首次调用必做）

调用任何 feval 命令前先定位 endpoint。**先解析用户首条消息**——能从中确定的就直接用，不要再问；只问真正缺的那一项。同一会话内复用结果，仅当用户明确说要换目标时再重走流程。

### 步骤 0：解析用户输入（先做这一步，再决定要不要问）

按以下规则从用户消息里抽信息（顺序优先，命中即停止往下匹配）：

| 用户消息中出现 | 解释 | 结果 |
|---|---|---|
| 形如 `\d+\.\d+\.\d+\.\d+(:\d+)?` 的 IP（如 `192.168.1.50` / `127.0.0.1:9999`） | 直接当 endpoint，缺端口补 `:9999`；IP 是 `127.0.0.1` / `localhost` 视为本地，否则视为 Android | endpoint 已定 |
| 仅含"本地 / Editor / 本机 / 当前工程"等词，无 IP | 本地 Unity 当前工程 | endpoint=`127.0.0.1:9999`，走 2A-是 |
| 仅含"Android / 真机 / 设备 / 手机"等词，无 IP | Android 真机但 IP 未知 | 仅追问 IP，**跳过其他问题** |
| 完全无 target / endpoint 信息 | 不确定 | 走步骤 1 原问答 |

判完上面，再独立判一次"用户是否带了具体调试问题"（"X 报错"、"为什么 Y"、"Z 不生效"、"卡住"、"读不到"等关键词）。**带了就在步骤 3 之后进入"调试任务流程"节**；没带就当查询/操控任务，按"使用示例"做。

**示例（说明步骤 0 该怎么落地，不是真要执行）：**
- "调试 127.0.0.1:9999 上 PlayerID 为什么是 0" → endpoint=`127.0.0.1:9999`，target=本地当前工程，**问题已带**，跳过步骤 1，直接走 2A-是 → 进入"调试任务流程"。
- "调试 Android 真机 192.168.1.50 的登录问题" → endpoint=`192.168.1.50:9999`，target=Android，**问题已带**，跳过步骤 1 与 2B 的 IP 提问，直接探活 → 进入"调试任务流程"。
- "调试一下本地 Unity，玩家信息读不到" → target=本地当前工程，endpoint=`127.0.0.1:9999`，**问题已带**，跳过步骤 1，直接走 2A-是 → 进入"调试任务流程"。
- "帮我看看 PlayerID 是多少" → target/endpoint 都没说 → 走步骤 1 完整问答；问题是"查询"，**不进调试任务流程**。
- "调试一下" → 全空 → 走步骤 1。

### 步骤 1：问 Target（仅当步骤 0 未确定 target）

向用户提问：

> 调试目标？
> A) 本地 Unity Editor
> B) Android 真机

### 步骤 2A：本地 Unity 分支

若步骤 0 已经判明"本地 + 当前工程"（关键词命中、或 IP=`127.0.0.1`），直接执行 2A-是；只有当用户明确说"已经在另一个本地 Unity 里"时才走 2A-否；语义不清才追问下面这一题：

> 是否调试当前 skill 所在的 Unity 工程（即当前工作目录）？
> A) 是 — 我会自动检测 / 启动 Unity 并进入 Play Mode
> B) 否 — 已经在另一个本地 Unity 里手动启动好了

#### 2A-是：当前工程

调用 `scripts/unity_bridge.py`：

```python
import sys; sys.path.insert(0, ".claude/skills/feval-runtime-debug/scripts")
from unity_bridge import is_feval_ready, is_unity_running, launch_unity, start_play_mode, wait_for_feval_ready

if not is_feval_ready():
    if not is_unity_running():
        launch_unity(".")
    start_play_mode(".")
    if not wait_for_feval_ready(timeout=300):
        raise RuntimeError("feval 未在 5 分钟内就绪；请检查 AppInfos.IsDebug 是否为 true")
```

resolved endpoint = `127.0.0.1:9999`

#### 2A-否：其他本地工程

直接探活 `127.0.0.1:9999`（`is_feval_ready()`），不通则提示用户在那个 Unity Editor 里手动进 Play Mode。**不要**用 `start_play_mode` / `launch_unity`，文件 IPC 仅对当前工程目录有效。

resolved endpoint = `127.0.0.1:9999`

### 步骤 2B：Android 真机分支

若步骤 0 已经从消息里抽到 IP，**直接进入下面的探活**，不要再问 IP；否则：

> Android 真机的内网 IP？（端口默认 9999）

resolved endpoint = `<IP>:9999`

连接前先 `is_feval_ready(host=<IP>)` 探一下；不通则告诉用户检查真机 `EvaluationService` 是否启动、网络是否连通（同一内网或已配置端口转发）。

### 步骤 3：所有 feval 调用统一带 endpoint

把上面定位的 endpoint 作为 `--endpoint` 参数传给 `feval_runtime_debug.ps1` / `.py`。本会话内不再重复询问。

若步骤 0 判到用户**带了具体调试问题**，进入下面的"调试任务流程"；否则按需要直接看"使用示例"。

## 调试任务流程（用户带了具体问题时）

如果用户首条消息里描述的是一个具体运行时问题（"X 报错"、"为什么 Y"、"Z 不生效"、"卡住"、"读不到"、"内存涨了"、"登录进不去"等），**按 `systematic-debugging` 流程定位根因**——本仓库已装该 skill，请按其四阶段推进，feval 在每个阶段当作取证与最小验证工具。先做证据收集、再下结论；不要直接给"应该是 X，改一下"。

### Phase 0 — 远端运行时版本对齐（endpoint 不是本机时必做）

endpoint 不是 `127.0.0.1` / `localhost`（典型：Android 真机、其他人的机器），**Phase 1 取证前**先把"设备上跑的代码版本"与"本地工作区参考代码版本"对齐。跨版本时字段名/命名空间/私有成员、甚至热更补丁里的类都可能对不上——本地源码再去解释设备读出来的值，会得出空中楼阁式的假设。endpoint 是 `127.0.0.1` / `localhost` 时跳过本节。

**最小取证三条**（feval 表达式，依次发）：

```text
UnityEngine.Application.isEditor
UnityEngine.Application.platform
UnityEngine.Application.version
```

- `isEditor == True` → 远端 endpoint 但仍是 Editor 进程（罕见，但开发机互联会出现），跳过下一步。
- `isEditor == False` → 真机/打包后进程，**继续读热更版本**。

**热更版本号读法**（项目各异，**命名空间补全**；不确定哪条对应当前工程，先问开发者）：

```text
# 走 tfw.resource + HybridCLR 通用方案的项目可先试
<X.Ns>.BundleSourceMgr.Cached

# 其它项目通常会有自己的升级/热更管理类，按工程实际类名替换
<X.Ns>.LaunchUpgrader.HotUgradeCacheVersion
```

不确定当前项目用的是哪种热更方案、或哪个类持版本号时，**直接问开发者**——别凭工程名/目录结构臆测。（注意 feval 不支持 `?.` / `??` 等空安全运算符，直接裸字段访问；为 null 会显式抛错，正好顺便确认"是否已落热更"。）

**对齐流程**：

1. 把读到的 `Application.version`（静态包）+ 热更版本号回报给开发者；
2. 明确询问：

   > 当前本地工作区是这套静态+热更版本吗？不是的话需要 checkout 到对应分支/Tag 或拉对应热更包再排查。

3. 开发者确认对齐（或主动切到对应版本）后，再进入 Phase 1。**版本未确认前不要基于本地源码下结论**——读到字段名对不上时别急着改 feval 表达式绕过，先回这一步。

| systematic-debugging 阶段 | 在 feval-runtime-debug 里怎么落地 |
|---|---|
| **Phase 1 — 根因调查** | 先把用户给的现象/报错原文读一遍。再用 feval 在**组件边界**取状态：玩家数据 / 关键单例字段 / 网络层 / UI 层各发一条 `--command` 读关键字段，对照预期。**没看到证据前不提改动方案。** |
| **Phase 2 — 模式分析** | 用 feval 比对"工作态 vs 坏态"：正常账号 vs 出问题账号读同一字段；或两台真机比同一全局状态。差异点就是下一步要钉死的方向。 |
| **Phase 3 — 假设与最小验证** | 写**最小**一条 feval 表达式只验一个假设（如 `Some.Ns.LoginMgr.I._token == null`、`Some.Ns.PlayerService.PlayerID > 0`）。一次只动一个变量；不通就换假设，不要在原假设上加补丁。 |
| **Phase 4 — 实施修复** | 若需改工程代码，**先按"代码修改与工程配合"节征得开发者同意**，再请开发者重编/重启，最后用 feval 复测同一表达式确认不再复现。3 次尝试仍失败 → 停下质疑架构（systematic-debugging 的 Phase 4.5）。 |

**反模式（看到自己在做就回到 Phase 1）：**
- 没读字段值就给"应该是 X 的问题，改一下"。
- 一条命令同时改多个状态。
- 跳过取证直接让开发者改代码。
- 同一假设连续打补丁仍不通。

如果用户没带具体问题（例如"我想看看 PlayerID 当前是多少"），属于**查询/操控**而非**调试**，跳过本节，直接按"使用示例"做。

## 前提

- **Feval.Cli 自动安装与版本缓存**（Agent 必须遵守）：
  - 缓存文件：**`%TEMP%\feval-runtime-debug\.cli-version`**，纯文本一行记录已确认的 Feval.Cli 版本号（如 `1.7.0`）。落 `%TEMP%` 是因为 skill 装在用户级目录（如 `%USERPROFILE%\.claude\skills\...`）时往 `<技能根>` 写文件会被 harness 拒；目录不存在先 `mkdir`。
  - **缓存文件已存在** → 跳过所有检测，直接用 PATH 上的 `feval`。
  - **缓存文件不存在** → 跑 `dotnet tool list --global Feval.Cli`，按结果分支：
    - **未列出**（用户机器上根本没装）→ **直接 `dotnet tool install --global Feval.Cli` 装上，不要问用户**。安装成功后再 `dotnet tool list --global Feval.Cli` 拿到版本号写入缓存文件。
    - 已列出但版本 **低于 1.5** → `dotnet tool update --global Feval.Cli`（若 `install` 提示已存在则改用 `update`）。完成后写缓存文件。
    - 已列出且版本 ≥ 1.5 → 直接把版本号写入缓存文件，不再升级。
  - 用户自己升级过 Feval.Cli 想强制重新检测时，删掉缓存文件即可。
- 仅 **Windows**。`feval` 一般装好后在 PATH 或 `dotnet tool` 路径里；按上一条，缺失会自动装。
- 游戏进程已接入 feval 并监听 **`endpoint`**（如 `127.0.0.1:9999`，以实际为准）。
- 仓库：<https://git.tap4fun.com/tfw/com.tfw.feval>
- **`<技能根>`**：本技能目录（含本 `SKILL.md` 的文件夹，例如仓库内 `.cursor/skills/feval-runtime-debug`）。

## 代码修改与工程配合（Agent 必须遵守）

- 若方案**涉及修改游戏工程、本仓库或其它项目代码**（含 C#、配置、构建脚本等）：须**事先向开发者说明**改动范围、目的与风险，**征得明确同意**后再动手改文件。
- 代码变更后，验证往往依赖开发者**重新编译、重启进程/游戏或再次运行**；应主动说明这些步骤，**在对方配合完成前勿假定**已生效；若无法配合，优先采用**不改代码**的 feval 表达式或运行时手段。

## 无弹窗执行（Agent 必须遵守）

- **`run` / `exec` 必须带 `--no-console-fallback`**：stdio 失败时**不再**新建 `cmd.exe` 控制台（`CREATE_NEW_CONSOLE` 会闪出黑窗）。
- **禁止使用 `-StayOpen`**：会 `Start-Process` 再开一个 **PowerShell** 窗口，不符合无弹窗要求。
- 若 stdio 失败：根据 JSON 里 `error` / `stderr` 排查（endpoint、游戏是否接入 feval、`feval` 是否支持 `-e`），**不要**为「跑通」而去掉 `--no-console-fallback`。

## 入口

| 方式 | 说明 |
|------|------|
| **`scripts/feval_runtime_debug.ps1`** | 推荐：解决多层 PowerShell 传参、UTF-8；**传入 `--no-console-fallback` 会透传给 Python**。 |
| **`scripts/feval_runtime_debug.py`** | 实际逻辑；在**单层**终端里也可直接 `python feval_runtime_debug.py ...`。 |
| **`scripts/unity_bridge.py`** | 本地 Unity Editor 生命周期 + Play Mode 文件 IPC + feval 端口探活；用于「启动流程」步骤 2A-是。导出函数：`is_unity_running` / `launch_unity` / `start_play_mode` / `stop_play_mode` / `is_feval_ready` / `wait_for_feval_ready`。也可 `python unity_bridge.py --test` 自检。 |

## 常用命令

### 临时命令文件落点（Agent 必须遵守）

- **新生成的临时 `--commands-file` 一律写到 `%TEMP%\feval-runtime-debug\`**（系统 temp，无需用户级目录授权弹窗；目录不存在先 `mkdir`）。
- **不要**往 `<技能根>\examples\` 写新文件。该目录仅作**只读参考样例**，当 skill 装在用户级目录（如 `%USERPROFILE%\.claude\skills\...`）时往里写会触发权限审核。
- 文件名建议带任务名前缀（如 `probe_xxx.txt`、`chain_xxx.txt`），便于排障；执行完毕统一清理 `%TEMP%\feval-runtime-debug\` 下本次新增的文件。
- **必须 UTF-8 无 BOM**：feval 不识别 BOM，开头那 3 字节会被当成 `?`，导致首行报 `Illegal character: ?`（即使首行是 `#` 注释也会报）。PowerShell 默认的 `Out-File -Encoding UTF8` / `Set-Content -Encoding UTF8` 在 PS 5.x 写带 BOM，**不能用**；推荐写法二选一：
  - PowerShell：`[System.IO.File]::WriteAllText($path, $content, [System.Text.UTF8Encoding]::new($false))`
  - Python：`open(path, 'w', encoding='utf-8', newline='\n')`（默认就不写 BOM）
  - 命令少时直接用多个 `--command` 而不是 `--commands-file`，可彻底规避此问题。

**一次性执行（优先）** — `run`：仅 **stdio** `feval <endpoint> -e ...`；加上 **`--no-console-fallback`** 后失败即返回 `ok: false`，**不**回退控制台。

```powershell
powershell -ExecutionPolicy Bypass -File "<技能根>\scripts\feval_runtime_debug.ps1" run `
  --endpoint 127.0.0.1:9999 `
  --no-console-fallback `
  --commands-file "$env:TEMP\feval-runtime-debug\probe_state.txt"
```

- **`--commands-file`**：UTF-8，**每行一条**表达式，`#` 开头为注释。避免在嵌套 PowerShell 里用 `--command` 传带 `"` 的 C# 字符串时被剥引号。**生成路径见上方「临时命令文件落点」。**
- 与 **`--command`** 可混用：先按顺序执行所有 **`--command`**，再按文件行追加。
- **`run` / `exec`** 至少需要一条 **`--command`** 或文件中至少一行有效表达式。

**多轮会话**：`start --endpoint ...` → 多次 `exec ... --no-console-fallback` → `stop`。若历史状态文件里是控制台会话，`exec --no-console-fallback` 会报错，需 `stop` 并删掉状态文件后重来。

**查看状态**：`status`（默认状态文件见下）。

**不经包装、直接 feval**（单层 shell、同样无额外 cmd 窗）：

```powershell
feval 127.0.0.1:9999 -e "expr1" "expr2"
```

### `-StayOpen`（仅人工本地调试）

会新开 PowerShell 且窗口保留；**Agent 不要用**。

### 表达式注意（feval 交互语法）

- **不要在一条表达式里写分号**（如 `using SomeNs; ...`），常见报错：`Illegal character: ;`。
- 改用 **完全限定名**，或拆成多条 **`--command`** / 多行 **`--commands-file`**。
- **必须使用完整命名空间**：feval 不会自动导入命名空间，符号解析失败时报 `Symbol 'Xxx' not found`。
  - 错误（缺前缀）：`WndMgr.Hide<UIXxx>()`
  - 正确（完全限定）：`Some.Ns.WndMgr.Hide<Other.Ns.UIXxx>()`
  - 遇到 `Symbol not found` 错误时，优先补全命名空间前缀，而非改写逻辑。
- **可以直接访问私有成员变量与方法**，不需要使用反射。
- **静态成员不能经实例链访问，必须用类名**：feval 解析 `instance.StaticMember` 时只查实例成员，会报 `Member 'XxxStaticMember' not found`。
  - 错误：`Logic.LLogin.I.CharacterToken`（`CharacterToken` 是 static）
  - 正确：`Logic.LLogin.CharacterToken`
  - 不确定一个成员是不是 static 时，先按类名访问试一次；报 `Member not found` 再退回实例链。
- **方法不是一等公民**：`obj.Method` 不带括号会报 `Member 'Method' not found`。MemberAccess 只查 field/property，**不识别 MethodInfo**——feval 里没法持有方法引用，只能直接调用 `obj.Method()`。要重复用同一段调用链，复制粘贴或 `var x = obj.Method()` 把**结果**存下来。
- **拼日志含可能为 null 的字段时永远用字符串内插，不要用 `+`**：`OperatorAdd` 入口先 `ThrowIfArgumentIsNull`，`"id=" + maybeNullField` 在 maybeNullField 为 null 时会抛 `ArgumentNullException`，不是拼成 `"id="`。改写：`$"id={maybeNullField}"` —— 走 `StringBuilder.Append(obj)`，对 null 写空串，永远不抛。

## Feval 表达式能力速查

> feval 是 **C# 表达式求值器**（基于 `com.tfw.feval@1.3.1` / `Feval.Core.dll`）——一行一个表达式，**没有**语句、控制流、语句块。详细每个细节、所有踩过的坑、Service 协议见 **[`references/feval-syntax.md`](references/feval-syntax.md)**（开头有「按症状速查」表，按报错/异常 行为反查最快；末尾有「源码定位与反编译」节，文档没覆盖时直接读 / 反编译 `Feval.Core.dll`）；下面只列日常最常用的能力与最爱误判的陷阱。

### 能用的（典型形态）

```text
# 字面量
123                          # int
12345678901                  # long（int 装不下时）
1.5                          # float（注意：不是 double，且 1.5f 后缀无意义）
"abc"   true   false   null

# 变量
var x = 42
x = 43                       # 也行，var 可省
ans                          # 上次表达式结果（process 全局共享）

# 字符串内插（{}里递归求值）
$"hi {x.ToUpper()}"

# 类型 / 静态 / 实例
UnityEngine.Time.frameCount             # FQN 静态字段
UnityEngine.Application.platform        # 静态枚举
typeof(int).Name                        # typeof + 链式

# 单例 / 私有字段（无需反射）
Logic.LLogin.I.IsLogin                  # instance prop
Logic.LLogin.CharacterToken             # static field（必须类名直访）
mgr._privateField

# 调用 / 泛型 / out
"abc".Substring(0, 3).ToUpper()         # 链式
UI.WndMgr.Hide<UI.UIFirstRecharge>()    # 静态泛型
dict.TryGetValue("k", out v)            # out 单 identifier

# 集合 / 索引（数组 / 字典）—— 仅"读"！
new System.Collections.Generic.List<int>()
list[0]    dict["k"]                    # 读 OK
# 写不行：list[0] = 99 / dict["k"] = "v" 会"返回 99/'v'"但容器没变（silent no-op）
# 数组写要用：arr.SetValue(value, index)

# 运算（仅 5 个二元）
a + b   a - b   a * b   a / b           # 数值；+ 对 string 是拼接（自动 ToString）
flagsA | flagsB                         # 枚举位或

# 内置
help()    usings()    vars()    assemblies()    version()
`obj                                    # 等价 dump(obj)

# using（持续到进程结束）
using UnityEngine
```

### 最爱踩的坑（Top 8）

1. **比较运算符 `==` 被悄悄丢掉**——返回左操作数。`1 == 2` → `1`，`true == false` → `True`。**根因**：`==` 不在 binary priority 表里，根 ParseBinaryExpression 循环 break，`==` 之后的 token 全部停留在 token 数组里、**不进 AST**。同样的命运还会落到 `<`、`>`、`is`、`as`、`await` 后面的 token 上（区别只是 `<` 会被当 generic 起始另起 syntax error）。判等改用 `a.Equals(b)`，**永远不要写 `==`**。
2. **括号不能分组**——`(1 + 2) * 3` 报 `Unexpected token (`。要分组用临时变量两步走。
3. **静态成员经实例链查不到**——`Logic.LLogin.I.CharacterToken` 报 `Member not found`。必须 `Logic.LLogin.CharacterToken`。先 grep 工程源码确认 `static` 关键字。
4. **`out var v` 不支持**——只能 `out v`（v 不存在时会自动创建）。
5. **`arr[k] = v` / `dict["k"] = v` 是 silent no-op**——返回值像成功了，但容器没变。Evaluator 的赋值 switch 没覆盖 IndexAccess 节点。要写数组用 `arr.SetValue(v, k)`；要写字典用 `dict.GetType().GetMethod("set_Item").Invoke(dict, args)`，**绝不要写 `dict[k] = v`**。
6. **字符串不识别转义**——`"a\nb"` 是 4 个字面字符，不是含换行；字符串里塞真换行直接 `Syntax Error`。
7. **`#`、`{`、`}`、`!`、`~`、`?`、`&`、`;` 都报 `Illegal character`**。`#` 注释只在本 skill 的 `--commands-file` 包装层有效，feval 本身不认。
8. **lambda / LINQ query / 三元 / `is`/`as`/`??`/`?.` / typeof(bool) / `new T[]{...}` 全不支持**。Lambda 不能用是因为 `>` 被当 close-angle-bracket，所以 `.Where(x => x>0)` 直接 `Syntax Error`——**不是因为找不到扩展方法**：`System.Linq.Where` 在 AppDomain 里其实存在，feval 扫描所有已加载 assembly。CHANGELOG/README 部分宣称的能力（`out var v`、按位与 `&`、`1.5f`）经反编译验证**未实现或写错**。

### Service 模式连接初始状态

通过 `feval <ip>:9999` 连接的是游戏进程的 `EvaluationService`：
- **using 列表为空**——所有类型必须 FQN 写全，或先发一条 `using SomeNs`。
- **Context 是 process 单例**：`var x`、`using`、`ans` 都跨调用、跨连接持续；变量名建议加前缀（如 `__probeX`）避免和别人冲突。
- **Evaluate 在 Unity 主线程跑**：探测无锁安全，但耗时操作会卡帧。

### IL2CPP 真机注意

包内带 `link.xml` 防 `Feval.Core.dll` 被裁，但**目标类型/成员**仍可能被链接器剥。真机访问到不存在的成员会报反射 `Method/Member not found`，需要在工程里给目标类型加 `[Preserve]` 或扩展 link.xml。

> 想看每个能力 / 反例 / 内部实现细节、Lexer token 全表、运算符优先级、字符串内插的正则、CLI 与 Service 协议——读 [`references/feval-syntax.md`](references/feval-syntax.md)。**碰到具体报错先翻该文档开头的「按症状速查」表**；该文档没覆盖时再用末尾「源码定位与反编译」节给的路径直接读 / 反编译 `Library/PackageCache/com.tfw.feval@1.3.1/` 下的源与 `Feval.Core.dll`。

## 使用示例（按场景）

> 以下示例用 `Some.Ns.XxxClass`、`Some.Ns.IXxxMgr` 等占位类型；接入具体项目时替换为该项目实际的命名空间与类名。

### 1) 一次性读状态（首选）

```powershell
powershell -ExecutionPolicy Bypass -File "<技能根>\scripts\feval_runtime_debug.ps1" run `
  --endpoint 127.0.0.1:9999 --no-console-fallback `
  --command "Some.Ns.PlayerService.PlayerID" `
  --command "Some.Ns.LoginMgr.I.IsLogin"
```

JSON 的 `output_lines` 即按命令顺序的每行求值结果。

### 2) 多步操作（用 `--commands-file`）

```text
# %TEMP%\feval-runtime-debug\probe_state.txt
using Some.Ns
var p = PlayerMgr.I.GetPlayerInfo()
p.someField
p._privateField
`p
```

```powershell
powershell -ExecutionPolicy Bypass -File "<技能根>\scripts\feval_runtime_debug.ps1" run `
  --endpoint 127.0.0.1:9999 --no-console-fallback `
  --commands-file "$env:TEMP\feval-runtime-debug\probe_state.txt"
```

### 3) 调用静态方法 / 单例方法 / 泛型方法

```text
# 单例 + 泛型
Some.Ns.SomeMgr.I.Show<Some.Ns.SomeWnd>()
Some.Ns.SomeMgr.I.Hide<Some.Ns.SomeWnd>()

# 直接读单例的私有字段
Some.Ns.SomeMgr.I._privateCollection.Count
```

### 4) 异步方法的解决办法

feval 不支持 `await`：直接调用返回 `Task`/`UniTask` 的方法只会得到任务对象本身。

```text
# 先存任务对象，再轮询
var t = Some.Ns.SomeMgr.LoadAsync("path")
t.Status
t.GetAwaiter().IsCompleted
```

如需更顺手，可在工程里临时加同步包装方法（须先与开发者沟通，遵循「代码修改与工程配合」节）。

### 5) 探边界（不确定语法是否支持时）

先发一条最小表达式做嗅探：

```powershell
feval 127.0.0.1:9999 -e "typeof(int)"
```

返回 `Illegal character` / `Unexpected token` → 不支持；返回正常值 → 支持。**别把多个不确定语法塞在一条复杂表达式里**，否则失败定位困难。

## 状态文件

未指定 **`--state-file`** 时默认：`%TEMP%\feval-runtime-debug.json`。多会话请传不同路径。

## JSON 里常用字段

- **`ok`**：是否成功（自动化请以此为准）。
- **`no_console_fallback`**：为 `true` 时表示未尝试（或不允许）控制台回退。
- **`transport`**：`stdio-cli` 或 `console`（仅未加 `--no-console-fallback` 且发生回退时）。
- **`fallback_from`**：是否从 stdio 回退到控制台（同上）。
- **`output` / `output_lines`**：stdio 模式整体输出与按行列表。
- **`results` / `raw_delta` / `screen`**：控制台模式有意义。
- **`error`** / **`stderr`**：失败时优先阅读。

## 注意

- 版本门槛以首次检测时 `dotnet tool list --global Feval.Cli` 或 `.feval-cli-version` 中记录为准；具体表达式行为仍以当前 PATH 上调到的 `feval` 为准。
- `output` 出现 **`(Pending)`** 多为异步未完成，可再发一轮命令观察。
- 缺 feval、endpoint 不通、游戏未接 feval 时，应明确提示用户，勿硬猜。
- 执行完毕后，删除新增的 `%TEMP%\feval-runtime-debug\` 下本次产生的临时命令文件；不要往 `<技能根>\examples\` 写新文件。
