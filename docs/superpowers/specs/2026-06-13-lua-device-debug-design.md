# Lua Device Debug 设计

## 目标

为以 Lua 承载业务、C# 仅提供稳定服务的 Unity 工程提供设备运行时诊断工具。

首版服务于 Windows 开发机、Unity Editor 和 Android Development Build。PC 通过 USB 与 `adb forward` 连接设备内的 HTTP 服务，执行项目显式注册的 Lua 白名单诊断命令。

工具不提供任意 Lua 表达式执行，不依赖 HybridCLR，不把通用传输层锁定到 xLua，也不允许调试能力进入 Release Build。

## 背景与决策

现有 `feval-runtime-debug` 依赖目标工程接入 Feval Service，主要面向 C# 表达式求值和 HybridCLR 场景。它不适合薄 C#、Lua 业务层工程，也无法直接诊断 Lua Module、DataCenter、RedDot 和 Config facade。

本设计放弃在当前工程接入 Feval，新增独立工具：

- Package：`com.yoji.lua-device-debug`
- Skill：`unity-lua-device-debug`
- 服务名：`LuaDeviceDebug`
- 默认端口：`21894`

采用“通用 C# 传输层 + 项目 Lua Debug Adapter”：

- UPM 包只负责 HTTP、协议、安全限制、主线程调度和 Agent 工具。
- 目标工程实现 Lua runtime adapter 和业务诊断命令。
- 通用包不引用 xLua、SLG 类型或业务模块。

## 首版范围

### 支持

- Windows 开发机。
- Unity Editor。
- Android Development Build。
- USB `adb forward`。
- HTTP + JSON 请求/响应。
- 白名单命令发现与执行。
- 默认只读命令。
- 显式声明的写命令双重授权机制。
- 项目注册的 Lua 诊断命令。

### 不支持

- 任意 Lua eval。
- 通用 Lua table 遍历。
- C# 反射或任意函数调用。
- 局域网直连和设备发现。
- WebSocket、实时日志和事件订阅。
- 切场景、打开 UI、FakeNetwork 操作等写命令。
- iOS、Windows Player、macOS 和 Linux。
- Release Build 调试服务。
- HybridCLR 和 Feval。

## 总体架构

```text
PC CLI / Agent Skill
  -> adb forward tcp:21894 tcp:21894
  -> Android Development Build / Unity Editor
  -> com.yoji.lua-device-debug
       HTTP + JSON
       request limits
       command metadata
       mutation gate
       main-thread dispatcher
  -> ILuaDeviceDebugHost
  -> project Lua runtime adapter
  -> Lua debug dispatcher
  -> registered whitelist commands
       system.info
       scene.current
       module.list
       module.dump
       data.get
       red_dot.get
       ui.stack
       config.get
```

连接方向为 PC 主动连接设备。Android 使用：

```powershell
adb forward tcp:21894 tcp:21894
```

不使用 `adb reverse`。设备服务仅监听设备回环地址。

## 仓库结构

```text
Packages/
  com.yoji.lua-device-debug/
    Runtime/
      ILuaDeviceDebugHost.cs
      LuaDeviceDebugRuntime.cs
      LuaDeviceDebugServer.cs
      MainThreadDispatcher.cs
      CommandDescriptor.cs
      JsonGuard.cs
      LuaDeviceDebugPlayerBootstrap.cs
    Editor/
      LuaDeviceDebugEditorBootstrap.cs
    Tests/
      Runtime/
    Agent~/
      skills/
        unity-lua-device-debug/
          SKILL.md
          client.py
          scripts/
          references/
      fragments/
        CLAUDE.md
        AGENTS.md
    package.json
```

目标工程结构：

```text
Assets/
  Scripts/Runtime/LuaDebug/
    XLuaDeviceDebugAdapter.cs
  Lua/debug/
    debug_dispatcher.lua
    debug_commands.lua
    debug_serializer.lua
```

`Lua/debug/` 是项目诊断层，不属于业务模块。`Lua/modules/` 不依赖调试 API。

## 通用包组件

### LuaDeviceDebugServer

负责：

- 监听 `127.0.0.1:21894`。
- 使用 `TcpListener` 实现首版所需的最小 HTTP/1.1 子集，不依赖 `HttpListener`，确保 Android IL2CPP 路径可控。
- 每个连接只处理一个请求并主动关闭，不实现 keep-alive、chunked encoding 或流式响应。
- 读取和限制 HTTP 请求体。
- 路由 `/ping`、`/commands`、`/execute`。
- 生成标准 HTTP 状态码和 JSON envelope。
- 将命令执行投递到 Unity 主线程。
- 在 Assembly Reload、应用退出和运行时关闭时停止服务。

它不解析 Lua 业务数据，不持有 xLua 类型。

### LuaDeviceDebugRuntime

提供通用包唯一的 Host 生命周期入口：

```csharp
public static class LuaDeviceDebugRuntime
{
    public static void RegisterHost(ILuaDeviceDebugHost host);
    public static void UnregisterHost(ILuaDeviceDebugHost host);
}
```

- 同时只允许注册一个 Host。
- 重复注册不同实例直接报错。
- `UnregisterHost` 只解除当前同一实例，避免旧 runtime 误清理新 Host。
- Server 只通过该注册表读取 Host，不直接持有项目 adapter。

### 启动器

- `LuaDeviceDebugEditorBootstrap` 使用 Editor 初始化钩子启动服务，因此未进入 Play Mode 时 `/ping` 仍可用。
- `LuaDeviceDebugPlayerBootstrap` 使用 runtime 初始化钩子，仅在 Android Development Build 启动服务。
- Editor 和 Player 启动器复用同一个 `LuaDeviceDebugServer`，协议和限制完全一致。
- Assembly Reload、Editor 退出和 Player 退出时停止 listener。

### MainThreadDispatcher

- HTTP 工作线程只负责收发和基础校验。
- Lua Host 调用必须进入 Unity 主线程。
- 命令串行执行。
- 队列上限 16。
- 默认等待超时 5 秒。
- 超时只停止 HTTP 等待，不尝试强杀正在运行的 Lua。

命令必须是短操作，禁止循环等待、资源加载和网络请求。

### ILuaDeviceDebugHost

```csharp
public interface ILuaDeviceDebugHost
{
    bool IsReady { get; }
    string DescribeCommands();
    string Execute(string command, string argsJson, bool allowMutation);
}
```

接口以 JSON 字符串作为边界，避免通用包依赖具体 Lua runtime 类型。未来从 xLua 切换到其他 runtime 时只替换项目 adapter。

### CommandDescriptor

每个命令至少描述：

- `name`
- `description`
- 参数 Schema
- 返回值说明
- `mutating`
- 可选敏感字段声明

命令名称在 Host 内必须唯一。

### JsonGuard

在 Lua serializer 之后执行第二次保护：

- 请求体最大 1 MB。
- 响应最大 4 MB。
- 限制对象递归深度。
- 限制数组和对象成员数量。
- 只允许 JSON null、boolean、number、string、array、object。
- 禁止 `UnityEngine.Object`、Lua userdata、delegate 和 runtime handle。

超限结果不做部分返回，统一返回明确错误。

## 项目 Lua Adapter

`XLuaDeviceDebugAdapter` 是 SLG 工程对通用包的唯一适配点。

职责：

- 判断 Lua VM 是否启动。
- 调用 Lua 全局调试入口。
- 传入 `command`、`argsJson` 和 `allowMutation`。
- 获取 Lua 返回的 JSON 字符串。
- 捕获 Lua 异常并将完整上下文写入现有 `LogService`。

项目 adapter 不向通用包暴露 `LuaEnv`。

Lua 全局入口固定为：

```text
__slg_debug_describe_commands()
__slg_debug_execute(command, args_json, allow_mutation)
```

这些全局函数仅由 adapter 缓存和调用。业务模块不直接使用。

## Lua Debug 层

### debug_dispatcher.lua

负责：

- 注册命令。
- 拒绝重复命令名。
- 输出命令描述。
- 验证请求参数。
- 检查 `mutating` 与 `allowMutation`。
- 调用命令实现。
- 为错误附加 command 上下文。

注册形式：

```lua
Debug.Register({
  name = "config.get",
  description = "读取一条最终合并配置",
  mutating = false,
  args = {
    table = { type = "string", required = true },
    id = { type = "number", required = true },
  },
  execute = function(args)
    return Config.Get(args.table, args.id)
  end,
})
```

### debug_commands.lua

包含项目注册的白名单命令。命令只能：

- 使用现有 Lua facade。
- 使用 `Lua/debug/` 内专用只读探针。
- 调用模块显式提供的 `DebugSnapshot()`。

禁止：

- `CS.*`
- `xlua.*`
- 读取 C# service 单例。
- 通用遍历任意 module/table。
- 返回 Unity 对象或 userdata。

### debug_serializer.lua

负责把命令结果转换为受限 JSON 数据结构，并在 Lua 层提前执行：

- 深度限制。
- 集合大小限制。
- 循环引用检测。
- 非字符串 table key 拒绝或显式数组识别。
- 不支持类型报错。
- 敏感字段脱敏。

C# `JsonGuard` 仍需二次验证，不能只信任 Lua serializer。

## HTTP 协议

### POST /ping

返回：

- 服务名和版本。
- Unity 版本。
- 应用版本。
- 平台。
- Editor/Development Build 标志。
- Lua Host 是否注册和就绪。

### POST /commands

返回 Host 注册的命令及参数 Schema。Host 未注册或 Lua 未启动时返回 `409 HOST_NOT_READY`。

### POST /execute

请求：

```json
{
  "requestId": "uuid",
  "command": "config.get",
  "args": {
    "table": "Activity",
    "id": 1001
  },
  "allowMutation": false
}
```

成功响应：

```json
{
  "ok": true,
  "requestId": "uuid",
  "elapsedMs": 3,
  "result": {
    "found": true,
    "value": {}
  }
}
```

失败响应：

```json
{
  "ok": false,
  "requestId": "uuid",
  "elapsedMs": 2,
  "error": {
    "code": "COMMAND_NOT_FOUND",
    "message": "unknown command: foo",
    "context": {
      "command": "foo"
    }
  }
}
```

### HTTP 状态码

| 状态码 | 含义 |
|---|---|
| 200 | 成功 |
| 400 | 请求或参数非法 |
| 403 | 写操作未授权 |
| 404 | 命令不存在 |
| 408 | 等待执行超时 |
| 409 | Lua Host 未就绪 |
| 413 | 请求或响应超限 |
| 429 | 主线程队列已满 |
| 500 | Lua 或服务内部执行失败 |

## 错误码

- `HOST_NOT_READY`
- `INVALID_REQUEST`
- `COMMAND_NOT_FOUND`
- `INVALID_ARGUMENT`
- `MUTATION_DENIED`
- `QUEUE_FULL`
- `EXECUTION_TIMEOUT`
- `LUA_EXECUTION_FAILED`
- `RESULT_NOT_SERIALIZABLE`
- `RESULT_TOO_LARGE`
- `INTERNAL_ERROR`

Lua 错误必须携带 command 和参数摘要，并把完整堆栈写入项目 `LogService`。HTTP 响应不返回完整 Lua/C# 堆栈，只返回错误码、消息和安全上下文。

## 安全设计

- 服务代码仅在 `UNITY_EDITOR || DEVELOPMENT_BUILD` 下可用。
- Release Build 不注册、不启动服务，且构建验证必须确认调试类型未进入 Player。
- 服务只监听 `127.0.0.1`，禁止 `0.0.0.0`。
- 首版只支持 USB `adb forward`，不支持局域网。
- 不提供 eval、文件读写、反射和任意函数调用。
- 命令必须预注册。
- 写命令必须同时满足：
  - Descriptor 声明 `mutating=true`。
  - 请求携带 `allowMutation=true`。
  - CLI 只有显式传入 `--allow-mutation` 才发送授权。
- 首版项目命令全部为只读。
- 禁止返回账号 token、设备标识、本地存档和其他敏感信息。

首版不增加会话令牌。USB 转发与设备回环监听共同构成连接边界。未来增加局域网模式时，必须先设计随机会话令牌和配对流程。

## 首版 SLG 命令

### system.info

返回：

- Unity 版本。
- 应用版本。
- 平台。
- Editor/Development Build 状态。
- Lua runtime 状态。
- 当前配置版本、语言和赛季等非敏感摘要。

### scene.current

返回当前逻辑场景 ID。

### module.list

返回已注册模块：

- 名称。
- 生命周期状态。
- 是否启用 Tick。

### module.dump

参数：模块名。

模块必须显式实现 `DebugSnapshot()`。命令不遍历任意模块 table。未提供 snapshot 的模块返回明确的“不支持诊断摘要”结果。

### data.get

参数：允许公开的 DataCenter domain。

只返回该 domain 的只读快照。允许列表由项目显式注册，不接受任意 key 路径遍历。

### red_dot.get

参数可选：

- 指定 key 时返回 count。
- key 为空时返回受限的已注册摘要。

### ui.stack

返回 UI 层级中的 viewId：

- Background
- Normal
- Top
- Popup
- System

不返回 GameObject 或组件引用。

### config.get

参数：

- `table`
- `id`

通过现有 Config facade 获取最终合并配置行，不直接读取 JSON 文件。

所有查询必须区分“存在但为空”和“不存在”：

```json
{
  "found": false
}
```

不以裸 `null` 表示所有空状态。

## Agent CLI

CLI 入口固定为：

```powershell
python client.py adb-forward
python client.py ping
python client.py commands
python client.py execute system.info
python client.py execute config.get --arg table=Activity --arg id=1001
python client.py adb-remove
```

CLI 职责：

- 检测 `adb`。
- 检测设备数量；零设备或多设备时明确报错，支持 `--serial`。
- 建立和移除 `adb forward`。
- 生成 UUID requestId。
- 解析 `--arg key=value` 为 JSON 标量。
- 默认不发送写授权。
- 只有 `--allow-mutation` 才设置 `allowMutation=true`。
- 输出稳定 JSON，进程退出码与 `ok` 一致。

默认不自动长期保留转发。Skill 应在任务完成后移除自己创建的 forward；若 forward 原本已存在且目标相同，不取得其 ownership，也不主动删除。

## 生命周期

### Editor

- Lua Host 注册后服务可执行命令。
- `LuaDeviceDebugEditorBootstrap` 在 Assembly Reload 前停止 listener。
- Reload 后重新启动并等待 Host 注册。
- 未进入 Play Mode或 Lua VM 未启动时 `/ping` 可用，`/commands` 和 `/execute` 返回 Host 未就绪。

### Android Development Build

- 应用启动后创建服务。
- Lua runtime 启动完成后注册 Host。
- Lua runtime Dispose 前解除 Host。
- 应用暂停不销毁服务；应用退出时停止服务。

是否在应用后台继续响应由 Android socket 行为决定，首版不承诺后台可用性。

## 测试

### 通用包测试

- `/ping`、`/commands`、`/execute` 路由。
- JSON envelope 和 HTTP 状态码。
- 请求体、响应体、深度和成员数量限制。
- Host 未注册和未就绪。
- 主线程调度。
- 执行超时。
- 队列上限。
- 命令不存在。
- 写命令双重授权。
- 客户端断开和响应写回失败。
- Editor/Development Build/Release 编译边界。

### Agent 测试

- `adb` 不存在。
- 零设备、多设备和 `--serial`。
- forward 建立、复用、冲突和清理。
- HTTP 超时与断线。
- `--arg` JSON 标量解析。
- `--allow-mutation` 默认关闭。
- CLI JSON 输出和退出码。

### 项目 adapter 测试

- Fake Lua Host 验证 adapter。
- Lua dispatcher 命令注册和重复拒绝。
- 参数 Schema 校验。
- 8 个首版命令的正常、空值和非法参数路径。
- Lua 异常包含 command 上下文并回流 `LogService`。
- `module.dump` 只能访问显式 `DebugSnapshot()`。
- 敏感字段不出现在结果中。
- `Lua/modules/` 下 `CS.` 和 `xlua.` 继续零命中。

## 端到端验收

1. 在 Unity Editor 中启动 Lua runtime。
2. `/ping`、`/commands` 和 8 个诊断命令全部通过。
3. 构建 Android Development Build 并安装到 S23 Ultra。
4. 建立 `adb forward tcp:21894 tcp:21894`。
5. `/ping` 确认设备、应用版本和 Lua Host 状态。
6. 依次执行 8 个诊断命令。
7. 验证非法命令、非法参数和写请求拒绝。
8. 断开 USB 后客户端请求失败，但游戏继续运行。
9. Release Build 确认 21894 无监听，并验证调试服务类型未进入构建。
10. 连续执行 500 次查询，确认没有 Lua 引用、线程和队列泄漏。

## 实施边界

实现分两个代码所有权：

1. `U3D-Dev-Tools-AI`：通用 UPM 包、CLI、Skill、协议和通用测试。
2. `SLG_Prototype`：xLua adapter、Lua debug dispatcher、SLG 命令和项目测试。

通用包应先以 Fake Host 完成全部传输层测试，再接入 SLG 工程。项目 adapter 不反向修改通用协议。

## 后续候选

首版验收后才评估：

- 写命令，例如切换逻辑场景或打开指定 UI。
- 带游标的日志拉取。
- 截图和性能采样。
- Windows Player。
- 局域网连接、会话令牌和设备发现。
- MobDebug 集成，用于人工断点、调用栈和局部变量调试。

这些能力不进入首版实施计划。
