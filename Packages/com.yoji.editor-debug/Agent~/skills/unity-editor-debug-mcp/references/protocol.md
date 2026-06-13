# EditorDebugMCP 协议参考

## 请求结构（/invoke）

```json
{
  "type":      "UnityEditor.X" | "FullName, AssemblyName",
  "target":    null | { "instanceID": <int|string> }, // 6.4+ 的 EntityId 使用 UInt64 十进制字符串
  "member":    "MemberName",                        // 单步用
  "kind":      "get" | "set" | "call" | "index",   // 单步用
  "args":      [ ... ],                             // 调用实参
  "argTypes":  [ "System.Type", ... ],              // 重载决议；按参数个数无歧义时可省略
  "steps":     [ {member,kind,args,argTypes}, ... ] // 链式优先于单步
}
```

`steps` 中每一步把上一步的返回值作为下一步的 currentTarget，自动按返回值类型继续访问。

## 响应信封（所有端点统一）

```json
{
  "ok":        true | false,
  "result":    <任意 JSON>,                         // ok=true 时
  "error":     {                                    // ok=false 时
    "type":    "System.XxxException",
    "message": "...",
    "stack":   "...",
    "inner":   { ... }                              // 嵌套异常
  },
  "elapsedMs": 12,
  "void":      true,                                // 仅 void 方法返回时
  "__truncated": true                               // 仅响应 body 超 4MB 被截断时（附 fullSize）
}
```

注：`__pending` 不是信封字段。返回值是 3s 内未完成的 Task 时，`result` 的值为 `{"__pending":true}`（见下方特殊占位符表）。

**HTTP 状态码恒为 200**。错误走 body 的 `error` 字段 —— 这样 Bash/Python 客户端不会因 4xx/5xx 状态码丢掉 body 内容。

## UnityEngine.Object 摘要

所有 `UnityEngine.Object` 派生（GameObject / Component / Texture / Material / ScriptableObject / EditorWindow / VisualTreeAsset / ...）自动降级为 ref：

```json
{
  "__ref":      true,
  "instanceID": -2978,
  "type":       "UnityEngine.GameObject",
  "name":       "Player",
  "path":       "Root/Player",                      // 仅 GameObject/Component
  "guid":       "abcdef01..."                       // 仅 asset，场景对象没有
}
```

拿到 `instanceID` 后用 `target:{instanceID}` 反查实例继续访问。Unity 6.3 及更早版本使用有符号整数；Unity 6.4+ 使用 `EntityId.ToULong` 产生的十进制字符串，服务端通过 `EntityId.FromULong` 还原。字段名保留为 `instanceID` 以兼容现有客户端。Unity fake null（destroyed object）会被检测并写 `null`。

## VisualElement 树摘要

VisualElement 派生类不走 ref 摘要，输出树状结构：

```json
{
  "name":        "GamePlayRoot",
  "type":        "VisualElement",
  "classList":   ["ux-td-gameplay"],
  "pickingMode": "Position",
  "visible":     true,
  "enabledSelf": true,
  "layout":      {"x":0,"y":0,"w":514,"h":926},     // 未 attach 到 panel 时 w/h="NaN"
  "text":        "...",                              // TextElement 派生才有
  "viewDataKey": "...",                              // 有值才出
  "bindingPath": "...",                              // IBindable 才有
  "childCount":  5,
  "children":    [ ... 递归同结构 ... ]
}
```

熔断：VisualElement 树独立计数，`maxDepth=16`、`maxNodes=500`；其余所有类型走全局熔断 `maxDepth=12`、`maxNodes=2000`。超限节点替换为 `{__truncated:true,reason:"maxDepth"|"maxNodes"}`。循环引用替换为 `{__cycle:"name"}`。

## 特殊占位符

| 占位 | 出现场景 |
|------|----------|
| `{"__skipped":"IntPtr"}` | IntPtr / SafeHandle 等 native 句柄 |
| `{"__delegate":true,"target":"TypeName.Method"}` | Action / Func / 事件 |
| `{"__enumerator":"TypeName"}` | IEnumerator / IAsyncEnumerable（不迭代避免副作用） |
| `{"__pending":true}` | Task 在 3s 超时内未完成 |
| `{"__error":"...","message":"..."}` | 单字段反射读取失败（其他字段不影响） |

## 类型名解析

`type` 字段的解析顺序：

1. `Type.GetType(typeName)`（含 AQN）
2. 遍历常用 Unity 程序集（UnityEngine.dll / UnityEditor.dll / UnityEngine.CoreModule.dll / UnityEditor.CoreModule.dll / 其它 UI / Animation / Profiling 模块）
3. 兜底全程序集扫描

internal 类型直接按 FullName 写即可（如 `UnityEditor.LogEntries`），无需手工指定 assembly —— 但写 `UnityEditor.LogEntries, UnityEditor` 也支持。

## 重载决议

`/invoke` 调用方法时：

- 提供 `argTypes` → 走严格签名匹配
- 不提供且方法名只有一个重载 → 直接用
- 不提供但方法名有多个重载 → 按 `args.Count` 与每个重载的参数个数过滤（考虑可选参数 `HasDefaultValue`）；过滤后唯一即用
- 仍歧义 → 抛 `AmbiguousMatchException`，错误消息列出所有候选签名

## 端口与服务

- 默认 21891
- 被占时 fallback 21892 → 21893
- `/ping` 的 `result.port` 反映当前实际端口
- `client.py --port` 显式指定（默认 21891）
