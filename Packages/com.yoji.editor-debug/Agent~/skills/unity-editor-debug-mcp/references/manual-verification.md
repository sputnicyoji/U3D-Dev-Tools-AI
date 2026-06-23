# 手动端到端验证 Checklist

单元测试位于 `Packages/com.yoji.editor-debug/Tests/Editor/`（73 条 EditMode 测试）。以下步骤验证 HTTP 服务在真实 Editor 中跑通。

## 前置

1. Unity Hub 打开工程：`C:\Example\U3D-Dev-Tools-AI\TestProjects\editor-debug`（或任意装了本包的工程），Unity 6000.3.16f1
2. 等编译完成（首次会编译 `com.yoji.editor-debug` 包）
3. 看 Console 出现：`[EditorDebugMCP] 服务已启动，监听 http://127.0.0.1:21891/`

如果没看到，说明：
- 包未被识别 → 检查 `Packages/com.yoji.editor-debug/package.json` 是否存在
- 端口被占 → 看日志是否提示端口冲突，已自动 fallback 到 21892/21893
- asmdef 编译失败 → 看 Console 是否有红字

## Phase A 验证：/ping 健康检查

```bash
cd Packages/com.yoji.editor-debug/Agent~/skills/unity-editor-debug-mcp
python client.py ping
```

预期：
```json
{
  "ok": true,
  "result": {
    "service": "EditorDebugMCP",
    "version": "0.1.0",
    "port": 21891,
    "unityVersion": "6000.3.16f1",
    "projectName": "editor-debug"
  },
  "elapsedMs": 0
}
```

## Phase B 验证：/invoke 反射调度

### 取常量属性
```bash
python client.py invoke --type UnityEditor.EditorApplication --member applicationPath --kind get
```
预期：返回 Editor 安装路径字符串。

### 取 internal API（Console 日志数）
```bash
python client.py invoke --type UnityEditor.LogEntries --member GetCount --kind call
```
预期：返回当前 Console 中日志条目总数（int）。

### 链式：当前选中物体的子节点数
（先在 Hierarchy 中选中任一个 GameObject）
```bash
python client.py invoke-chain --type UnityEditor.Selection \
  --steps 'activeGameObject:get' 'transform:get' 'childCount:get'
```
预期：返回该物体的子节点数（int）。

### 实例方法 + InstanceID 反查
（先 Selection.activeGameObject 拿到 instanceID）
```bash
python client.py invoke --type UnityEngine.GameObject \
  --target-instance-id <你拿到的 ID> --member name --kind get
```
预期：返回该 GameObject 的名字。

## Phase E 验证：/describe

```bash
python client.py describe --type UnityEditorInternal.ProfilerDriver
```
预期：返回该 internal 类型的方法/属性/字段清单。

```bash
python client.py describe --type Foo.NotExist
```
预期：错误信封 `error.type=TypeAccessException`。

## Phase E 验证：/recompile

随便改一个工程脚本（加个空格也算）后：
```bash
python client.py --timeout 120 recompile
```
预期：挂住 30-60 秒 → 返回 `result.success=true`，紧接着服务重启（再次 ping 可能短暂 connection refused，几秒后恢复）。

## Phase C 验证：/eval

`/eval` 默认关闭。若要验证轻量表达式解析器，先在 `EditorDebugMCP.cs` 临时改
`c_AllowEval = true` 并重编译；验证后改回 `false`。

```bash
python client.py eval --code "UnityEditor.EditorApplication.applicationPath"
python client.py eval --code "UnityEditor.LogEntries.GetCount()"
python client.py eval --code "UnityEditor.Selection.activeGameObject.name"
```
预期：分别返回字符串/int/字符串。

枚举字面量：
```bash
python client.py eval --code "UnityEditor.Selection.GetFiltered(typeof(UnityEngine.GameObject), UnityEditor.SelectionMode.Deep)"
```
（注：当前轻量解析器**不支持** `typeof(...)`，复杂场景请用 `client.py invoke-chain`）

## 故障排查

| 现象 | 原因 |
|------|------|
| `URLError: [WinError 10061] No connection could be made` | Editor 没开 / 包未加载 |
| 一直挂住不返回 | Editor 主线程被卡（看 Editor 是否在跑测试/编译/导入资源） |
| `error.type=TypeAccessException` | 类型名拼错或不在常用 Unity 程序集 |
| `result` 含 `__truncated:true` | 响应被熔断（深度/节点数超限），收紧调用范围 |
| `error.type=Conflict` 仅 /recompile | 已有进行中的重编译，等几秒重试 |
