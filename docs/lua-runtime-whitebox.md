# Lua 运行时白盒化设计方案 (Lua Runtime Whitebox)

Date: 2026-10-14  
Repo: `G:\Side_Projects\U3D-Dev-Tools-AI`  
Package: `com.sputnicyoji.u3d-dev-tools-ai`  
Status: Approved for planning, ready for implementation  

## 1. 背景与核心价值

在 Cargo 这类以 **C# 为引擎底层、Lua 为重度业务逻辑** 的 U3D 项目中，AI Agent 与 Pipeline CLI 的调试闭环存在显著痛点：
1. **代码修改缺乏免重启反馈**：Agent 修改 `.lua` 代码后，Unity Editor 无法感知并热重载，必须重启 Play 模式或重新导航进入游戏场景，单次调试耗时 30~60 秒。
2. **错误定位与真实源文件割裂**：运行时抛出 Lua 异常时，控制台通常被 C# pcall 包装异常淹没，缺少结构化的源文件路径与行号。
3. **业务状态不可见（黑盒）**：金币、内城建筑等级、大地图坐标、任务进度等核心业务数据存放在 Lua 内存中，Agent 缺少只读状态探针，无法在修改后断言因果。

**定位与互补性**：
- **Pipeline CLI**：负责静态代码检查、编译门禁、批处理测试。
- **Agent MCP**：负责交互式指令发起、代码分析与改写。
- **U3D Dev Tools AI (本工具集)**：作为驻留 Editor 的白盒桥梁，提供**免重启热载、真实 Traceback 结构化提取与业务状态只读探针**。

---

## 2. 接口契约设计 (Runtime/LuaDeviceDebug)

保持现有 `ILuaDeviceDebugHost` 兼容，引入两个可选的能力扩展接口：

```csharp
namespace Yoji.LuaDeviceDebug
{
    /// <summary>
    /// 可选能力接口：提供 Lua 模块免重启热更
    /// </summary>
    public interface ILuaHotReloadHost
    {
        /// <summary>
        /// 热更指定模块或相对文件路径
        /// </summary>
        /// <param name="moduleOrPath">如 "logic.city.city_view" 或 "logic/city/city_view.lua"</param>
        LuaReloadResult Reload(string moduleOrPath);
    }

    /// <summary>
    /// 可选能力接口：提供游戏业务 Model/Table 的受限只读探针
    /// </summary>
    public interface ILuaInspectHost
    {
        /// <summary>
        /// 对只读表达式进行求值并输出精简 JSON
        /// </summary>
        /// <param name="queryPath">如 "DataCenter.player.gold"</param>
        /// <param name="maxDepth">最大遍历深度，防止环引用与数据过大</param>
        string Inspect(string queryPath, int maxDepth);
    }

    public sealed class LuaReloadResult
    {
        public bool Ok { get; set; }
        public string Module { get; set; }
        public bool Reloaded { get; set; }
        public long ElapsedMs { get; set; }
        public string Error { get; set; }
    }
}
```

---

## 3. 服务端端点扩充 (LuaDeviceDebugServer)

在现有 `LuaDeviceDebugServer`（默认端口 21894）中新增三个 HTTP 端点：

| 端点 | 请求方法 | 参数 | 职责与响应格式 |
|---|---|---|---|
| `/reload` | POST | `{"target": "logic/city/city_view.lua"}` | 规范化模块名并触发 `ILuaHotReloadHost.Reload`。返回 `{ "ok": true, "module": "...", "reloaded": true, "elapsedMs": 4 }` |
| `/inspect` | POST | `{"path": "PlayerModel.gold", "maxDepth": 3}` | 经由只读词法检查后调用 `ILuaInspectHost.Inspect`。返回 `{ "ok": true, "path": "...", "value": 1500 }` |
| `/errors` | GET | `?count=5&sinceFrame=N` | 筛选带有 `[string "..."]:line` 的日志，提取真实文件、行号与 Traceback |

### 主线程派发与防爆保护
1. **主线程执行**：所有针对 Lua 虚拟机的操作必须通过 `MainThreadDispatcher` 调度在主线程帧内执行，防止跨线程崩溃。
2. **深度限制**：`maxDepth` 默认 3，最大 5；循环引用输出 `"[Circular]"`。
3. **只读保护**：`inspect` 严格限定点语法路径（如 `A.B.C`），拒绝赋值号（`=`）及危险系统库调用（`os.execute`, `io` 等）。

---

## 4. 客户端 CLI 与 Agent 操作循环

在 `Agent~/skills/unity-lua-device-debug/client.py` 扩充对应命令：

```powershell
# 模块热更
python client.py reload --path logic/city/city_view.lua

# 状态探针
python client.py inspect --path DataCenter.player.gold --depth 2

# 结构化错误提取
python client.py errors --count 5
```

### Agent 标准调试循环
1. **改写代码**：修改 `.lua` 源码。
2. **局部热载**：运行 `client.py reload --path <file>`。若语法错误直接返回行号，保持旧模块正常运行。
3. **因果断言**：运行 `client.py inspect --path <query>` 直读数据层验证业务正确性。
4. **异常复盘**：若逻辑未按预期流转，调用 `client.py errors` 查看深层 Lua 调用栈。

---

## 5. 错误处理矩阵与退出码规范

| 错误原因 (`reason`) | 退出码 | 含义与应对 |
|---|---|---|
| `not_playing` | 2 | 编辑器不在 Play 模式，无法执行热更或动态探针 |
| `no_host` | 2 | 游戏工程未注册 `ILuaDeviceDebugHost` |
| `no_capability` | 2 | Host 未实现 `ILuaHotReloadHost` 或 `ILuaInspectHost` |
| `syntax_error` | 2 | 目标代码语法错误，返回具体语法解析行与错误文本 |
| `invalid_path` | 2 | `inspect` 语法包含非法字符或非法操作 |
| `network_error` | 1 | 无法连接到指定端口或服务未就绪 |

---

## 6. 测试与验证策略

1. **EditMode 单元测试**：在 `Tests/Editor/LuaDeviceDebug/` 编写测试用例，结合 `MockLuaHost` 验证：
   - 正常热更与语法报错分支。
   - 深度截断（超限深度截断与循环引用检测）。
   - 路径自动规范化（`logic/a/b.lua` -> `logic.a.b`）。
2. **CLI E2E 脚本**：编写 `references/run-e2e.py` 保证各子命令与退出码的端到端正确性。
3. **静态合规检查**：
   ```powershell
   python -m py_compile Packages/com.sputnicyoji.u3d-dev-tools-ai/Agent~/skills/unity-lua-device-debug/client.py
   pwsh -File tools/run-editmode.ps1 -Project lua-device-debug
   ```
