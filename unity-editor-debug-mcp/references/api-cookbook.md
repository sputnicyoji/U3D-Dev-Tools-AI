# EditorDebugMCP API Cookbook

8 个常见调试场景的端到端示例。每个示例同时给出 **`client.py` 调用** 与 **裸 HTTP/JSON** 两种形式。

---

## 1. 取当前选中物体的名字

```bash
python client.py invoke --type UnityEditor.Selection --member activeObject --kind get
```

```json
POST /invoke
{ "type":"UnityEditor.Selection", "member":"activeObject", "kind":"get" }
```

返回 `result` 是 ref 摘要：
```json
{ "__ref":true, "instanceID":-2978, "type":"UnityEngine.GameObject",
  "name":"Player", "path":"Root/Player" }
```

直接读 name 字段即可。

---

## 2. Console 日志总数（internal API）

```bash
python client.py invoke --type UnityEditor.LogEntries --member GetCount --kind call
```

返回 `result` 为数字：
```json
{ "ok": true, "result": 12, "elapsedMs": 3 }
```

要进一步取条目，参考 `LogEntries.GetEntryInternal(int row, LogEntry outputEntry)` —— 需要 instance + ref 参数，建议改用 describe 探查实际签名后再 invoke。

---

## 3. 当前 Hierarchy 根节点列表

```bash
python client.py invoke-chain --type UnityEngine.SceneManagement.SceneManager \
  --steps 'GetActiveScene:call' 'GetRootGameObjects:call'
```

```json
POST /invoke
{ "type":"UnityEngine.SceneManagement.SceneManager",
  "steps":[
    {"member":"GetActiveScene","kind":"call"},
    {"member":"GetRootGameObjects","kind":"call"}
  ]
}
```

返回 `result` 是 GameObject ref 数组。

---

## 4. 按 instanceID 反查并取 transform.childCount

```bash
python client.py invoke-chain --type UnityEngine.GameObject \
  --target-instance-id -2978 \
  --steps 'transform:get' 'childCount:get'
```

或单步：
```bash
python client.py invoke --type UnityEngine.GameObject \
  --target-instance-id -2978 --member name --kind get
```

---

## 5. 拿当前 Selection 的所有 Components

```bash
python client.py invoke-chain --type UnityEditor.Selection \
  --steps 'activeGameObject:get' 'GetComponents:call:UnityEngine.Component'
```

`steps` 中第三步：`member=GetComponents`、`kind=call`、`args=["UnityEngine.Component"]`。
但 `GetComponents` 是泛型方法，纯命令行 args 写不下泛型实参。改用裸 JSON：

```json
POST /invoke
{ "type":"UnityEditor.Selection",
  "steps":[
    {"member":"activeGameObject","kind":"get"},
    {"member":"GetComponents","kind":"call",
     "args":["UnityEngine.Component"],
     "argTypes":["System.Type"]}
  ]
}
```

---

## 6. 取所有 EditorWindow 标题

```bash
python client.py invoke --type UnityEngine.Resources --member FindObjectsOfTypeAll --kind call \
  --args '"UnityEditor.EditorWindow"' --arg-types System.Type
```

返回 EditorWindow ref 数组。要拿 title，再 invoke-chain 每个 ref 的 `titleContent.text`。

---

## 7. 触发重编译并等待完成

```bash
python client.py recompile --timeout 120
```

```json
POST /recompile
```

响应（编译成功）：
```json
{ "ok":true, "result":{ "success":true, "compilationTime":12.3, "hasErrors":false }, "elapsedMs":12300 }
```

**注意**：编译会触发 Domain Reload，HTTP 服务在响应发出后短暂不可用。客户端要做的就是收到响应后**轮询 `/ping`** 等服务恢复（通常 5-10s）。

---

## 8. 不知道 API 怎么调？先 describe

```bash
python client.py describe --type UnityEditorInternal.ProfilerDriver
```

返回类型全成员清单（含 internal/private）：
```json
{ "ok": true,
  "result": {
    "FullName": "UnityEditorInternal.ProfilerDriver",
    "Assembly": "UnityEditor.CoreModule",
    "Methods": [
      "public static HierarchyFrameDataView GetHierarchyFrameDataView(Int32 frameIndex, Int32 threadIndex, ViewMode viewMode, Int32 sortColumn, Boolean sortAscending)",
      "public static void ClearAllFrames()",
      ...
    ],
    "Fields": [ ... ],
    "Properties": [ ... ]
  }
}
```

挑出感兴趣的方法签名再 `invoke` 调用。

---

## 9. dump UXML 资源的 VisualElement 树

把磁盘上的 .uxml 加载并展开成结构化 JSON。适合静态审阅 UI 布局、检查命名/class、抽查 binding。

```json
POST /invoke
{
  "type": "UnityEditor.AssetDatabase",
  "steps": [
    {"member":"LoadAssetAtPath","kind":"call",
     "args":["Assets/X1/Res/Uxml/Modules/RoguelikeTower/.../UxmlXxx.uxml",
             "UnityEngine.UIElements.VisualTreeAsset"],
     "argTypes":["System.String","System.Type"]},
    {"member":"CloneTree","kind":"call","args":[]}
  ]
}
```

返回 `result` 是 VisualElement 树状摘要：

```json
{ "name":"GamePlayRoot", "type":"VisualElement",
  "classList":["ux-td-gameplay"],
  "layout":{"x":0,"y":0,"w":"NaN","h":"NaN"},
  "childCount":5,
  "children":[
    { "name":"", "type":"VisualElement", "classList":["ux-td-gameplay__top-bar"], ... },
    { "name":"PauseBtn", "type":"Button", "text":"||", ... },
    ...
  ]
}
```

**坑**：`CloneTree()` 出来的元素未 attach 到 panel，所以 `layout.w/h = NaN`；要看真实尺寸用下面 #10 的 EditorWindow 路径。

---

## 10. dump 当前 Editor 窗口的实时 visualTree

读运行时 panel 上的 VisualElement 树，**含真实 layout**（已经布过局）。

### 当前焦点窗口

```json
POST /invoke
{ "type":"UnityEditor.EditorWindow",
  "steps":[
    {"member":"focusedWindow","kind":"get"},
    {"member":"rootVisualElement","kind":"get"}
  ]
}
```

### 指定窗口类型

```json
POST /invoke
{ "type":"UnityEngine.Resources",
  "steps":[
    {"member":"FindObjectsOfTypeAll","kind":"call",
     "args":["UnityEditor.InspectorWindow"],"argTypes":["System.Type"]},
    {"kind":"index","args":[0]},
    {"member":"rootVisualElement","kind":"get"}
  ]
}
```

返回带真实 layout 的树：

```json
{ "name":"rootVisualContainer2", "type":"VisualElement",
  "layout":{"x":0,"y":0,"w":514,"h":926},
  "childCount":2,
  "children":[ ... ]
}
```

### 哪些 EditorWindow 真用了 UIToolkit？

2022.3 内置窗口里只有少数真正 UIToolkit 化（其它仍是 IMGUI）：

- `UnityEditor.InspectorWindow` — UIToolkit 化完整，8+ 节点（ScrollView + editors-list）
- `UnityEditor.SceneView` — 仅外壳，1 个 IMGUI viewport 容器
- `UnityEditor.SceneHierarchyWindow` — IMGUI，rootVisualElement 空
- `UnityEditor.GameView` — IMGUI，rootVisualElement 空
- `UnityEditor.ConsoleWindow` — IMGUI，rootVisualElement 空
- `UnityEditor.ProjectBrowser` — IMGUI，rootVisualElement 空

如果目标窗口 `childCount=0`，就是 IMGUI 主导，得改用其他调试路径（比如反射读 internal 字段）。

---

## 调用结构速查

```
{
  "type":      "UnityEditor.X" | "FullName, Assembly",
  "target":    null | { "instanceID": <int> },
  "member":    "MemberName",                   // 单步用
  "kind":      "get" | "set" | "call" | "index", // 单步用
  "args":      [ ... ],                         // 调用实参
  "argTypes":  [ "System.Type", ... ],          // 重载决议用
  "steps":     [ {member,kind,args,argTypes}, ... ]  // 链式优先于单步
}
```
