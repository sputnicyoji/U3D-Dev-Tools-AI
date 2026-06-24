---
name: test-runner-mcp
description: 在 Unity 工程中通过 HTTP 调用 Unity Test Runner——触发重编译、发起 EditMode/PlayMode 测试、轮询拿结果，全程不开 Unity GUI。用于 AI 在编辑器在线时执行单元测试、判断改动是否影响测试通过率。触发表述：跑测试、执行单元测试、跑一下 xx 用例、触发重编译、recompile、run tests。当 tdd-workflow 进到「执行测试」环节也会调用本 skill 的接口。
---

# TestRunnerMCP（Unity Editor 在线时执行测试）

`com.yoji.test-runner` 是 Unity Editor 内的轻量 HTTP 服务，让 AI 不开 GUI 就能：触发重编译 → 等域重载 → 发起测试 → 轮询拿结果。`tdd-workflow` 在「执行测试」阶段调用的就是本接口。

> [!IMPORTANT]
> 本工具已具备公开的 Unity 侧实现（UPM 包 `com.yoji.test-runner`，EditMode +
> PlayMode via DisableDomainReload），随包附带 `client.py` 与 `references/run-e2e.py`。
> 无需私有仓库权限即可独立工作。
>
> 测试代码怎么写、目录怎么放、断言怎么选，参考 `tdd-workflow`。

## 契约扩展（本实现相对原规范）

本实现在原 HTTP 接口规范基础上做了以下扩展，调用时需注意：

- **run-all**：`/run-tests` 请求体新增可选 `assemblyNames` / `categoryNames` / `groupNames`；当 `testNames` 与这些字段全空时，跑该 `testMode` 的全套件（原规范空 `testNames` 返 400，本实现放宽为 run-all）。
- **PlayMode（TR-1a，已支持）**：`testMode: "PlayMode"` 经临时叠加 `EnterPlayModeOptions.DisableDomainReload` 跑——**不触发域重载**，服务全程在线；run 结束完整还原用户的 enterPlayMode 设置。前置守卫：已在 Play 中或有未存脏场景时返 409。**caveat**：依赖静态状态跨域重置的测试在 DisableDomainReload 下行为不同（test-authoring 问题，非本工具 bug）。
- **真 HTTP 状态码**：本实现用 200/202/400/404/409 真实状态码（非恒 200）；错误 body 形为 `{success: false, error: "..."}`。

## 服务地址

- 端口：项目感知分配；首个工程优先 `21890`，回退 `21896/21897`；多工程使用 `base + 0`
- 绑定：`127.0.0.1`
- 自动启动：Editor 加载后通过 `[InitializeOnLoadMethod]` 自动跑起来
- 域重载 / 退出 Editor 时会停服，重载完成后自动重启


## Agent 端口解析

```powershell
python client.py --project <unity-project-root> ping
python client.py --pid 76792 ping
python client.py --port 21890 ping
```

解析顺序：

1. 显式 `--port` 优先。
2. `--project` 或当前目录向上查找 `.u3d-ai-linker/ports.json`。
3. 机器 registry。
4. 健康扫描 legacy ports：`21890`，再回退 `21896/21897`。

Use `--project` when available. The client resolves the endpoint from the project ports file and global registry before falling back to legacy ports. Use `--port` only to force a specific debugging endpoint.

同一项目存在多个在线 Editor 实例时，未传 `--pid` 或 `--port` 会返回 ambiguity。

## 接口一览

| 路径 | 方法 | 说明 | 形态 |
|---|---|---|---|
| `/ping` | GET | 连通性 + state | 同步 |
| `/recompile` | GET/POST | 触发重编译，**域重载前**回响应 | 同步（挂 HTTP 连接到编译完成） |
| `/run-tests` | POST | 发起测试，**立即**返回 `jobId` + 202 | 异步 |
| `/test-status` | GET | 轮询任务状态 / 取最近一次结果（completed 含 `failures[]`） | 同步 |
| `/list-tests` | GET | 列出可发现的 EditMode/PlayMode 测试用例全名 | 同步 |

state 取值：`Idle | Running | Compiling`。`/run-tests` 与 `/recompile` 在非 Idle 时返 409。
`/list-tests` 支持 `mode=EditMode` 与 `mode=PlayMode`（仅枚举，不进 Play）。

## 调用顺序（标准流程）

```
/ping  ──→ 确认服务在线、state=Idle
  ↓
/recompile  ──→ 编译；hasErrors=true 停止流程
  ↓
轮询 /ping  ──→ 等待 state 由 Compiling 回到 Idle（域重载完成）
  ↓
POST /run-tests  ──→ 拿 jobId（202）
  ↓
轮询 /test-status?jobId=...  ──→ status=completed 时取结果
```

## /ping

```bash
curl -s http://127.0.0.1:21890/ping
```

```json
{
  "state": "Idle",
  "listening": true,
  "timestamp": "2026-05-11T11:00:00.000+08:00",
  "unityVersion": "6000.3.16f1",
  "projectName": "test-runner"
}
```

## /recompile

```bash
curl -s http://127.0.0.1:21890/recompile
```

行为：调 `AssetDatabase.Refresh` + `CompilationPipeline.RequestScriptCompilation`，通过 `compilationFinished` 回调在**域重载之前**返回响应。响应返回后服务会因域重载短暂不可用（5-10 秒），后续需要轮 `/ping` 等恢复。

```json
{
  "success": true,
  "message": "编译成功，耗时 2.26s",
  "compilationTime": 2.26,
  "hasErrors": false
}
```

- `hasErrors: true` → 编译失败，停止后续测试发起
- 无代码改动约 2 秒秒回；有改动 30-40 秒

## /run-tests（异步，立即返）

POST JSON：

```json
{
  "testMode": "EditMode",
  "testNames": [
    "Yoji.Sample.Tests.SampleTests.Passes"
  ]
}
```

- `testMode`：`EditMode` | `PlayMode`
- `testNames`：完整测试方法名，格式 `Namespace.ClassName.MethodName`

**立即响应 202**，不等测试完成：

```json
{
  "success": true,
  "jobId": "a3f9bc12e4d07e81b6c25f3a9d0e1234",
  "status": "running",
  "message": "测试已发起，请轮询 /test-status?jobId=a3f9bc12e4d07e81b6c25f3a9d0e1234"
}
```

- `jobId`：32 字符 GUID，用于轮 `/test-status`
- 状态码 `409`：上一个任务还在跑，拒收
- 状态码 `400`：`testNames` 为空 / 请求体非法

## /test-status

```
GET /test-status?jobId=<jobId>
```

运行中：

```json
{
  "jobId": "a3f9bc12e4d07e81b6c25f3a9d0e1234",
  "status": "running",
  "message": "测试运行中"
}
```

完成：

```json
{
  "jobId": "a3f9bc12e4d07e81b6c25f3a9d0e1234",
  "status": "completed",
  "message": "测试完成，耗时 8.19s",
  "resultFilePath": "<projectPath>/Temp/TestRunnerMCP/result_a3f9bc12....xml",
  "overallResult": "Failed",
  "passed": 1,
  "failed": 1,
  "skipped": 0,
  "failures": [
    { "name": "Foo.Tests.BarTests.Baz", "message": "Expected: 1  But was: 2", "stackTrace": "at ..." }
  ]
}
```

- `status`：`running | completed | error`
- `overallResult`：`Passed | Failed | Error`
- `failures`：仅 `completed` 且有失败时出现，每条 `{name,message,stackTrace}`（仅叶子用例，不含 suite）；
  最多 50 条、字段截断 4000 字。agent 据此直接定位「哪个用例、为什么挂」，无需 parse XML。
- **0 命中判错（TR-3a）**：非 run-all 的过滤（`testNames`/`assemblyNames`/`categoryNames`/`groupNames`）
  若命中 0 个用例，返 `status=error` / `overallResult=Error`（而非假绿 Passed）。拼 `testNames` 前可先 `/list-tests` 核对。
- 省略 `jobId` 时：优先返回当前活跃任务状态，无活跃任务则返回最近一次缓存结果
- jobId 不匹配当前任务也不匹配上一次任务时返 404
- 服务只缓存**最近一次**结果，重启 Editor / 域重载后内存丢失

## /list-tests

```
GET /list-tests?mode=EditMode
```

```json
{ "tests": ["Foo.Tests.BarTests.Baz", "Foo.Tests.BarTests.Qux"], "count": 2 }
```

列出可发现的 EditMode 或 PlayMode（`?mode=PlayMode`）测试用例全名（含 `[Explicit]`）。用于拼 `/run-tests` 的 `testNames` 前核对，避免拼错导致 0 命中。仅枚举，不进 Play。

## 调用范式（重要）

bash 下 JSON 转义易错，AI 调用一律走 Python 脚本写到 `<projectPath>/Temp/`：

```python
# 写到 Temp/run_tests.py 再 python Temp/run_tests.py
import json, time, urllib.request

BASE = "http://127.0.0.1:21890"

def http_get(url):
    return json.loads(urllib.request.urlopen(url, timeout=10).read().decode("utf-8"))

def http_post(url, data):
    body = json.dumps(data).encode("utf-8")
    req = urllib.request.Request(url, data=body, headers={"Content-Type": "application/json"})
    return json.loads(urllib.request.urlopen(req, timeout=10).read().decode("utf-8"))

# 1. 探活
ping = http_get(f"{BASE}/ping")
assert ping["state"] == "Idle", f"服务非 Idle: {ping['state']}"

# 2. 重编译
recompile = http_get(f"{BASE}/recompile")
if recompile["hasErrors"]:
    raise SystemExit(f"编译失败: {recompile['message']}")

# 3. 等域重载完
for _ in range(60):
    try:
        if http_get(f"{BASE}/ping")["state"] == "Idle":
            break
    except Exception:
        pass
    time.sleep(1)

# 4. 发起测试
started = http_post(f"{BASE}/run-tests", {
    "testMode": "EditMode",
    "testNames": ["Yoji.Sample.Tests.SampleTests.Passes"]
})
job_id = started["jobId"]

# 5. 轮询结果（120s 上限）
for _ in range(120):
    s = http_get(f"{BASE}/test-status?jobId={job_id}")
    if s["status"] != "running":
        break
    time.sleep(1)

print(json.dumps(s, ensure_ascii=False, indent=2))
```

- Windows 下走 `<projectPath>/Temp/`，不要 `/tmp`
- 单测 jobId 流程总耗时 = 编译时间 + 域重载（5-10s）+ 测试时间

## 何时**不能**用 MCP

- `/ping` 不通：Editor 没开 / package 没装 / 端口记录过期或 legacy 端口被占用
- Editor 已挂 / 编译卡死

这时退回 `Unity.exe -batchmode -runTests`，详见 `tdd-workflow/references/test-execution.md` 中 batchmode 段落。两种模式结果都是 NUnit XML，解析方式一致。

## 故障排除

| 现象 | 处理 |
|---|---|
| `/ping` 超时 | Editor 没开 → 启 Editor；或 21890 端口被占（`netstat -ano \| findstr 21890`）|
| `/run-tests` 返 409 | 上个任务没完，先 `/test-status` 看进度，或等 state 回 Idle |
| `/test-status` 返 404 | jobId 写错；或 Editor 域重载冲掉了内存中的缓存结果，重发测试 |
| 编译后 `/ping` 久久不回 | 编译错误堵在 Editor 顶部弹窗，去 Unity 看 Console；或者域重载耗时长（首次加载大工程） |
| PlayMode 测试 | 本工具用 `DisableDomainReload` 跑 PlayMode——**不触发域重载**，服务全程在线，可连续轮 `/test-status`；run 结束自动退出 Play 并还原 enterPlayMode 设置。返 409 多因已在 Play 中或有未存脏场景（守卫） |

## 安装

`Packages/manifest.json` 加：

```json
"com.yoji.test-runner": "file:<path-to-repo>/Packages/com.yoji.test-runner"
```

或把 `Packages/com.yoji.test-runner/` 拷进目标工程 `Packages/`。

装好后 Editor 加载，服务自动启动，Console 出现 `[TestRunnerMCP] 服务已启动，监听 http://127.0.0.1:21890/` 即就绪。
