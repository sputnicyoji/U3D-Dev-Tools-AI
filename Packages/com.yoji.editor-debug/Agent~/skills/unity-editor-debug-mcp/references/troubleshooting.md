# EditorDebugMCP 排错速查

## 调用失败

| 现象 | 原因 | 处理 |
|------|------|------|
| `Connection refused` / 端口拒绝 | Editor 没开，或 UPM 包 `com.yoji.editor-debug` 没装 | 让用户启 Editor；包路径 `Packages/com.yoji.editor-debug/` |
| `Connection refused` 但 Editor 开着 | 21891 被占，服务 fallback 到 21892 / 21893 | 加 `--port 21892` 重试（全局参数放子命令前，如 `client.py --port 21892 ping`）探活 |
| `error.type=TypeAccessException` | 类型名拼错；或在非常用 dll，`Type.GetType` 找不到 | 给类型名加 `, AssemblyName` 后缀（如 `UnityEditor.LogEntries, UnityEditor`），或先 `describe` 父命名空间探查 |
| `error.type=AmbiguousMatchException` | 方法有多个重载，dispatcher 无法消歧 | 加 `--arg-types` 指定具体重载（按参数个数无歧义时会自动选） |
| `error.type=MissingMethodException` | 方法名拼错 / 重载不存在 | `describe` 该类型，挑出真实方法名再 invoke |
| `error.type=TargetInvocationException` 包 inner | API 内部抛错（如 `MissingReferenceException`、`NullReferenceException`） | 看 `error.inner.message`，处理被调 API 的真实问题 |
| `error.type=Conflict`（仅 recompile） | 已有编译进行中 | 等几秒重试，或先 `ping` 确认服务恢复 |
| `error.type=FormatException`（仅 eval） | 表达式语法错误 | lightweight parser 不支持 lambda / new / typeof，看 [api-cookbook.md](api-cookbook.md) 的 eval 用法 |

## 响应被截断

| 标记 | 含义 | 处理 |
|------|------|------|
| `result` 内出现 `{"__truncated":true,"reason":"maxDepth"}` | 嵌套超过 12 层 | 链式拆分调用，目标更具体的子节点 |
| `result` 内出现 `{"__truncated":true,"reason":"maxNodes"}` | 累计节点数超过 2000 | 用 `index` 取指定子项；或针对 VisualElement 树用更小的 root |
| 响应 body 末尾 `__truncated` + `fullSize` | 输出超过 4MB | 缩小 dump 范围 |
| `result` 出现 `__cycle` | 循环引用被切断（VisualElement 树） | 正常行为，不影响其他节点 |

## /recompile 后服务不可用

预期行为：编译触发 Domain Reload，HTTP 服务在 reload 期间下线 5-10s。

```bash
python client.py recompile        # 收到响应即说明编译已完成
# 然后轮询 /ping 等服务恢复
until python client.py ping; do sleep 1; done
```

如果 30s 后还连不通：

1. 看 Unity Console 有没有 `[EditorDebugMCP] 服务已启动` 的日志
2. 看是否有编译错误（`hasErrors:true`）—— 编译失败时 Editor 处于 broken state，[InitializeOnLoadMethod] 不会再触发
3. 修编译错误后服务会自动恢复

## 调用很慢

| 现象 | 排查 |
|------|------|
| 单次 invoke 耗时 > 1s | 主线程被 Editor 卡住（如正在播放动画 / Profile）；`elapsedMs` 字段反映真实主线程派发耗时 |
| /eval 超时 30s | 表达式涉及 Task 未完成（lightweight parser 不会等 await） |
| /recompile 数十秒 | 正常（取决于工程规模和 incremental cache 状态） |

## 安全

- 服务**只绑 127.0.0.1**，不监听外网
- 默认开放所有反射调用 —— 不要在共享 Editor / 远程桌面上让陌生进程访问 21891 端口
- /eval 默认开启，需要关闭可在 `EditorDebugMCP.cs` 改 `c_AllowEval = false`
