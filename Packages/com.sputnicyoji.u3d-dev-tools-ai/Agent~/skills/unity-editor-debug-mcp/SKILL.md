---
name: unity-editor-debug-mcp
description: 在 Unity Editor（含 Play 模式）内通过 HTTP+JSON 调用任意 Unity API，获取实时调试数据。覆盖 Hierarchy/Selection、UI Toolkit、Console（LogEntries）、Profiler、FrameDebugger、AssetDatabase 等。Agent 用 client.py 通过 Bash 子进程调用，避免 Bash 中 JSON 转义地狱。当用户提到「unity editor 调试」「读 console」「查 hierarchy」「ui toolkit 检查」「profiler 采样」「unity 反射」等时使用。
---

# Editor Debug MCP

让 Agent 在 Unity Editor 中通过反射调用任意 Unity API（含 internal/private），获取调试数据。

## 工作前置

- **Editor 必须开着**（服务跟随 Editor 进程）
- 首次确认：`python client.py ping`，看到 `{ok:true, result:{service:"EditorDebugMCP",...}}` 即正常
- 端口项目感知分配；首个工程优先 21891，被占时 fallback 到 21892 / 21893；多工程使用 base + 1


## Agent 端口解析

```powershell
python client.py --project <unity-project-root> ping
python client.py --pid 76792 ping
python client.py --port 21891 ping
```

解析顺序：

1. 显式 `--port` 优先。
2. `--project` 或当前目录向上查找 `.u3d-ai-linker/ports.json`。
3. 机器 registry。
4. 健康扫描 legacy ports：`21891`，再回退 `21892/21893`。

Use `--project` when available. The client resolves the endpoint from the project ports file and global registry before falling back to legacy ports. Use `--port` only to force a specific debugging endpoint.

同一项目存在多个在线 Editor 实例时，未传 `--pid` 或 `--port` 会返回 ambiguity。

## 核心端点

| 端点 | 用途 | 何时用 |
|------|------|--------|
| `/invoke` | 反射调用任意 Unity API | 80% 调试场景的主力 |
| `/describe` | 列出类型成员清单 | 不知道 API 长啥样时先 describe |
| `/eval` | 表达式求值（默认关闭，需源码打开 `c_AllowEval`） | 单步 invoke 写不顺时 |
| `/recompile` | 触发脚本重编译 | 改完代码要让 Editor 重新加载时 |

## 最小用法

```bash
# 读静态属性
python client.py invoke --type UnityEditor.Selection --member activeObject --kind get

# 链式访问
python client.py invoke-chain --type UnityEditor.Selection \
  --steps 'activeGameObject:get' 'transform:get' 'childCount:get'

# 不知道 API 怎么调？先 describe
python client.py describe --type UnityEditorInternal.ProfilerDriver
```

## 工作流程

1. **不确定 API 名字 / 参数 → 先 `describe`** 探查可用方法和签名
2. **简单取值 → `invoke`**（含 internal/private 自动可达）
3. **多步访问 → `invoke-chain`**，把多次往返合并成一次请求
4. **改了代码要生效 → `recompile`**，等响应回来再轮询 `/ping` 等服务恢复

## 关键约定

- HTTP 状态码恒为 200，错误走 body 的 `error` 字段（避免客户端丢 body）
- `UnityEngine.Object` 自动降级为 ref 摘要 `{__ref, instanceID, type, name, path?, guid?}`
- `VisualElement` 派生类自动展开为树状摘要（含 layout / classList / text / bindingPath）
- 响应熔断：maxDepth=12 / maxNodes=2000 / body=4MB，超限带 `__truncated`

## 详细文档

- [references/api-cookbook.md](references/api-cookbook.md) —— 10 个常见调试场景的端到端示例（Console / Hierarchy / Selection / UXML / EditorWindow / Profiler 等）
- [references/protocol.md](references/protocol.md) —— 完整请求/响应规范、ref 摘要格式、特殊占位符、类型解析规则
- [references/troubleshooting.md](references/troubleshooting.md) —— 常见错误排查（连不通 / 类型找不到 / 重载歧义 / 截断 / 编译卡住）
- [references/manual-verification.md](references/manual-verification.md) —— 手动验证清单
- [references/run-e2e.py](references/run-e2e.py) —— 13 条端到端测试用例（怀疑服务有问题时跑）
