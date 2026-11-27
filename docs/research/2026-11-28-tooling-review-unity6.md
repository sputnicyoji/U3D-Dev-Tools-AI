# U3D Dev Tools AI 工具审查与 Unity 6 兼容性（2026-11-28）

对象：`Packages/com.sputnicyoji.u3d-dev-tools-ai` v0.2.23（`package.json` 声明 `"unity": "2022.3"`）。本文只做研究，没有改任何代码，文中“修复方向”都没有实施。

本文是 2026-11-28 的快照，文中的行号和状态都以 v0.2.23 为准。后续修复在之后的提交里跟踪，本文不随之更新。

约定：
- `<pkg>/` 指 `Packages/com.sputnicyoji.u3d-dev-tools-ai/`。
- “实测”指第 2 节的真机运行。
- 状态标签来自独立核验：已确认、部分确认、未验证。标“本文复核”的，是写报告时又重新打开代码或官方源码核对过的。

## 1. 结论摘要

### 1.1 Unity 6 能否正常使用

| 版本段 | 证据 | 结论 |
|---|---|---|
| 6000.3（实测 6000.3.13f1，macOS arm64） | 实测 | **可用。** 编译 0 error，包内只有 1 条 warning。EditMode 共 580 例：577 通过、1 失败、2 跳过，和 2022.3.62f3c1 基线逐项一致。在交互式 Editor 里，用随包 Python 客户端调用 test-runner、editor-debug、lua-device-debug，全部正常。EditMode 那 1 个失败和 PlayMode 的失败都是测试自身的缺陷，2022.3 上同样失败，不是 Unity 6 回归（见 2.3）。 |
| 6000.0-6000.2 | 只做了静态检查 | 没发现破坏点，但从没编译过。不确定的是：这一段内置的 Test Framework 版本（6000.3 解析为 1.6.0），以及 `<pkg>/Editor/TestRunner/UnityTestRunnerApiAdapter.Unity6000.cs` 在它上面的行为。 |
| 6000.4 | 只做了静态检查 | 预计能编译，但会产生过时警告，涉及 `GetInstanceID`、`InstanceIDToObject`、带 `FindObjectsSortMode` 的 `FindObjectsByType`。另外两个问题：`/console` 的 `instanceID` 会一直是 0；UI 协议的 `iid` 是 int，而 `/invoke` 的 `instanceID` 是 ulong 字符串，两边对不上。`UNITY_6000_4_OR_NEWER` 分支从没编译过。 |
| 6000.5+ | 只做了静态检查 | **大概率编译不过 `Yoji.EditorDebug.Editor`，整个 editor-debug 服务会失效。** 官方迁移手册写明 "Starting in Unity 6.5, obsolete `InstanceID` APIs cause compilation errors."，而 UI 代码里有 4 处调用没加版本分支（见 3.1）。 |

所以“Unity 6 能正常使用”目前只能说：**在 macOS 上、6000.3、限于已测路径**成立。

下面这些没有验证：
- Play Mode 下 live `/ui/act` 和 world/cell 手势；
- Unity 6 新建工程的默认配置（只有 Input System、URP）；
- 含 `Yoji.LuaDeviceDebug.Server` 的 IL2CPP Development Player；
- macOS 上 Linker agent sync 的实际运行；
- Windows 上的全部路径；
- 6000.4 及之后的任何版本。

### 1.2 有没有不合理设计

有，但都不阻断使用。按核验后的级别，中等的有 5 项：

1. 6.5 的 InstanceID 迁移缺口（3.1）。
2. 两个 Editor 服务的 `GET /recompile` 都能被不带 Origin 的跨站 GET 触发；TestRunner 服务还完全不检查 Origin，跨站 POST 能到达 run-tests（SEC-1，第二轮实测）。
3. Lua 调试服务只读 Content-Length 这一个头，对任意 Host 和 Origin 都放行，`allowMutation` 由请求方自己声明（SEC-2）。
4. 域重载后 job 记录永远停在 `running`；PlayMode 运行中如果崩溃，`DisableDomainReload` 会留在项目设置里（ROB-2）。
5. Linker agent sync 写死了 Windows junction，在 macOS 上不可用（L1）。

其余都是低级别。SEC-1 这类问题有一个共同根源：三个 HTTP 服务各自复制了一份监听和守卫代码（MAINT-1）。

### 1.3 改进空间

- 没有 CI。
- 测试套件里有 2 个测试在任何 Editor 下都失败。
- 服务版本号和包版本不同步。
- 文档里的“已验证版本号”有三处互相矛盾。
- 几个脚本只有 PowerShell 版本。

## 2. 实测记录

### 2.1 环境

- 平台：macOS arm64。
- Editor：6000.3.13f1 和 2022.3.62f3c1。注意 fixture 和 README 写的是 2022.3.62f2c1，和实测用的不是同一个版本。
- 本机没有安装 6000.0-6000.2，也没有 6000.4+。
- 全部在临时副本上运行：
  - 把包复制到仓库外；
  - fixture 的 manifest 用 `file:` 指向这个副本；
  - 按 `tools/run-editmode.ps1:131-132` 的做法建一个空的 `Assets/`。
- Unity 从没打开过仓库本身，`git status` 没有变化。

### 2.2 解析到的包版本

| 包 | 6000.3.13f1 | 2022.3.62f3c1 | 声明值（`<pkg>/package.json:19-23`） |
|---|---|---|---|
| com.unity.test-framework | 1.6.0 | 1.1.33 | 1.1.33 |
| com.unity.ugui | 2.0.0 | 1.0.0 | 1.0.0 |
| com.unity.ext.nunit | 2.0.5 | 1.0.6 | - |
| com.unity.nuget.newtonsoft-json | 3.2.2 | 3.2.2 | 3.2.2 |

### 2.3 运行结果

| 运行 | Editor | 结果 |
|---|---|---|
| Batch EditMode，`TestProjects/editor-debug`，设了 `U3D_LINKER_REPO_ROOT=<repo-root>` | 6000.3 | 580 例：577 通过 / 1 失败 / 2 跳过。跳过的是两个 `[Explicit]`：`FixtureTests.AlwaysPasses` 和 `AlwaysFails`。 |
| 同上 | 2022.3 | 和 6000.3 完全相同。 |
| 同上，但不设环境变量 | 6000.3 | 570 / 2 / 6 inconclusive / 2。多出来的失败和 inconclusive 都在 `RegistryFixtureTests`：副本在仓库外，找不到 `Registry/stable.json`。这是测试环境问题，不是代码问题。 |
| Batch EditMode，`TestProjects/editor-core`（没有 physics 和 uielements 模块） | 6000.3 | 编译 0 error。579 例：569 / 2 / 6 / 2，模式同上一行。说明不带 physics 的可选路径在 Unity 6 下能编译。 |
| Batch PlayMode，editor-debug（只有 1 例） | 6000.3 和 2022.3 | 两边都判 Failed，原因都只在 TearDown（见下面缺陷 2）。测试体里对 `/ui/snapshot`、`/ui/hit` 的断言没有失败。 |
| 交互式 Editor + `Agent~` 下的 Python 客户端 | 6000.3 | 约 10 s 后四个服务全部就绪。各项结果见下方列表。 |

交互式 Editor 下各项调用的结果：
- test-runner ping：Idle，端口 21890，portSource 为 legacy。
- editor-debug ping：version 0.1.5，端口 21891。
- `eval` 返回 `"6000.3.13f1"`。
- `console` 返回 4 条。
- Edit 模式下 `ui-observe` 的 guide 和 snapshot 正常（场景里没有节点）。
- lua ping 正常：0.1.1，端口 21894，`hostRegistered false`。
- `list-tests` 正常。
- `run-tests --mode EditMode --assemblies Yoji.EditorCore.Editor.Tests` 返回 202，轮询两次后变成 completed / Passed，43 例 0 失败。

6000.3 上 `UiWorldActor*` 的 76 例全部通过。这些数据来自本文对实测 XML 的统计。

physics 路径也确认生效了：受 `#if U3D_HAS_PHYSICS` 保护的 `UiWorldActorTests.Click_NoReport_FallsBackToPhysics`（`<pkg>/Tests/Editor/EditorDebug/UiWorldActorTests.cs:517-526`）在 editor-debug 的 6000.3 结果里是 Passed，在没有 physics 模块的 editor-core 结果里不存在。这说明 `versionDefines` 在 Unity 6 上能正确解析。product asmdef 用的是同一套 `versionDefines`。

### 2.4 两个原本就存在的测试缺陷

两个版本都失败，不是 Unity 6 回归。

**缺陷 1：`PackageShapeTests.ServerAssembly_IsMarkedAlwaysLinkAssembly`**
- 位置：`<pkg>/Tests/Editor/LuaDeviceDebug/PackageShapeTests.cs:59-61`。
- 现象：测试断言 Server 程序集里有 `LuaDeviceDebugPlayerBootstrap` 这个类型。
- 原因：`<pkg>/Runtime/LuaDeviceDebugServer/LuaDeviceDebugPlayerBootstrap.cs:1` 把整个文件包在 `#if DEVELOPMENT_BUILD && !UNITY_EDITOR` 里，所以在任何 Editor 程序集里这个类型都不存在。
- 前半个断言是通过的：`[assembly: AlwaysLinkAssembly]` 确实存在，见 `<pkg>/Runtime/LuaDeviceDebugServer/LuaDeviceDebugServer.cs:15`。
- 来历：c46d313 引入，随 0.2.23 发布。
- 修复方向：去掉“类型存在”这个断言。bootstrap 在 IL2CPP 下是否被保留，应该在 dev player 构建里验证（目前还没覆盖）。
- 本文复核。

**缺陷 2：`UiObserverHttpTests.HttpObservation_MatchesGameFrameViewportAndRaycast`**
- 现象：TearDown 报 `Invalid index 3 7 4`。
- 根因（本文对照 UnityCsReference 复核）：
  - `GameViewSizeGroup.RemoveCustomSize(int index)` 要的是**总索引**，它内部先调 `TotalIndexToCustomIndex`，也就是减去 builtin 的数量。2022.3 和 6000.3 的实现相同：[2022.3](https://github.com/Unity-Technologies/UnityCsReference/blob/2022.3/Editor/Mono/GameView/GameViewSizeGroup.cs)、[6000.3](https://github.com/Unity-Technologies/UnityCsReference/blob/6000.3/Editor/Mono/GameView/GameViewSizeGroup.cs)。
  - 测试传进去的却是 `GetCustomCount()` 得到的 custom 索引：在 `<pkg>/Tests/PlayMode/UiObserverHttpTests.cs:94` 取值，`:31` 调用。
  - 结果是 3 - 7 = -4，越界。日志里的 7 和 4 分别是 builtin 和 custom 的数量。
- 修复方向：把总索引存进字段，比如 `:96` 已经算出的 `count - 1`，TearDown 用它。
- 附带后果（已确认）：TearDown 失败时，本次加进去的 1080x1920 自定义尺寸不会被删掉。`GameViewSizes` 用 `FilePath(..., PreferencesFolder)` 持久化到用户级的 Unity 偏好目录（macOS 上是 `~/Library/Preferences/Unity/Editor-5.x/GameViewSizes.asset`），所有工程、所有 Editor 版本共用这份文件。本机这份文件里已经积累了 14 个 `HTTP viewport test`，其中 2 个来自这次实测。每跑一次这个 PlayMode 测试，所有工程的 Game 视图分辨率下拉列表里就多一条垃圾项。修复时要同时清理这些已有的残留。

### 2.5 6000.3 上唯一的编译警告

```
<pkg>/Editor/EditorDebug/Ui/UiWorldActor.cs(677,27): warning CS0618: 'EditorUtility.InstanceIDToObject(int)' is obsolete
```

这一处绕开了 `<pkg>/Editor/EditorDebug/ArgumentCoercer.cs:54-59` 里已有的版本分支。

其他情况：
- 2022.3 上包内没有任何 warning。
- 6000.3 上，`GetInstanceID`、`RetrieveTestList`、`FindObjectsByType(..., FindObjectsSortMode)` 都不报 warning。

### 2.6 第二轮：请求来源和 Host 实测

- 环境：6000.3.13f1，交互式 Editor，打开临时副本工程。用 curl 直接伪造 `Host`、`Origin`、`Content-Type` 请求头，不是在浏览器里发的，也没有做端到端的 DNS rebinding。2022.3 没有做这一轮。
- 端口：其中一次运行时，另一个 Unity 实例（别的工程）已经占了 legacy 端口 21890、21891、21894，本工程的服务自动换到了其他端口。所以请求只发给随包客户端用 `--project` 和 `--pid` 解析出的端口。测试和客户端不能假定 legacy 端口上就是自己的工程。

Host 和 Origin（`GET /ping`）：

| 请求头 | test-runner | editor-debug | lua-device-debug |
|---|---|---|---|
| `Host: 127.0.0.1:<port>` | 200 | 200 | 200 |
| `Host: localhost:<port>` | 400 `Bad Request (Invalid host)` | 400 `Invalid host` | 200 |
| `Host: evil.example` 或 `evil.example:<port>` | 400 `Invalid host` | 400 `Invalid host` | 200 |
| `Origin: http://evil.example` | 200，返回完整 ping | HTTP 200，body 是 `ok:false`，"cross-origin request rejected" | 200 |

两个 HttpListener 服务的 400 来自 HttpListener 本身：前缀是 `http://127.0.0.1:<port>/`，Host 不完全匹配就拒绝。代码里没有 Host 检查。

有副作用的路由：
- `GET /recompile`，不带 Origin：test-runner 和 editor-debug 都回 200，并且**真的执行了重编译**（两边都返回了 `compilationTime`）。`<img>` 或页面跳转发出的就是这种请求。
- test-runner `POST /run-tests`，带 `Origin: http://evil.example`、`Content-Type: text/plain`，body 是 `{"mode":"bogus"}`：回 400 "testMode must be EditMode or PlayMode"。也就是说 Origin 和 text/plain 都没有拦住，请求已经走到参数校验。合法的 body 会启动一次测试运行，这一步没有实际触发。
- editor-debug：带 Origin 的 `POST /eval`、`POST /recompile`、`GET /ui/snapshot` 都被拒绝，HTTP 状态是 200，body 是 `ok:false`。不带 Origin、`Content-Type: text/plain` 的 `POST /eval` 正常返回结果（不检查 Content-Type）；`GET /eval` 能走到处理函数（不检查方法）；`GET /ui/act` 被拒（"ui/act only accepts POST"）。
- lua-device-debug：带 Origin、`Content-Type: text/plain` 的 `POST /execute` 和 `GET /commands` 都回 409 `HOST_NOT_READY`，说明请求头没有拦截，请求已经走到 host 就绪检查。临时工程没有注册 host，所以命令执行本身没有跑到，那一段仍是读代码得出的。

浏览器和设备侧的事实（文档，不是实测）：
- 按 WHATWG Fetch 的 "append a request `Origin` header" 算法，跨站的非 GET/HEAD 请求一定会带 Origin 头，包括 no-cors 的 text/plain 表单 POST；值可能被降级成字符串 `null`。`<img>`、`<script>`、顶层导航、GET 表单这类 no-cors GET 不带 Origin。所以 Origin 检查只能保护非 GET 请求，挡不住有副作用的 GET 路由；守卫要拒绝任何非空 Origin，包括 `null`。来源：https://fetch.spec.whatwg.org/#append-a-request-origin-header
- Chrome 的 Local Network Access：Chrome 142 起，公网页面访问本地网络或 loopback 时会弹出权限提示，博文列出的覆盖范围是 `fetch()`、子资源加载和子框架导航。这是用户可以点“允许”的提示，不是服务端保证；旧版本、其他浏览器、被企业策略预先授权的站点都不受它约束。来源：https://developer.chrome.com/blog/local-network-access
- Android：设备上的 loopback 端口对同一设备上的其他 app 开放（CERT DRD23-J：https://cmu-sei.github.io/secure-coding-standards/android-secure-coding-standard/rules/network-ssltls-net/drd23-j/）。原生 app 不发 Origin，Host 也可以随便填，所以 Origin 和 Host 检查保护不了 Development Player 不被同设备的其他 app 访问。iOS 没有查到官方说明。

## 3. Unity 6 兼容性问题（按严重度排序）

### 3.1 [中][已确认] InstanceID API 迁移缺口：6000.5+ 编译失败（U6-01，合并 U6-02、U6-03）

**破坏点**（下列路径都在 `<pkg>/Editor/EditorDebug/` 下）：
- 调 `GetInstanceID()` 的有三处：`Ui/UiObserver.cs:332`、`Ui/UiActor.cs:55`、`Ui/UiWorldActor.cs:856`。
- 调 `EditorUtility.InstanceIDToObject(int)` 的有一处：`Ui/UiWorldActor.cs:677`。
- 调 `FindObjectsByType<T>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)` 的有三处：`Ui/UiRaycast.cs:24`、`Ui/UiObserver.cs:63`、`Ui/UiObserver.cs:170`。
- 以上都没有加 `#if`。全包只有 `ArgumentCoercer.cs:54-59` 和 `ResultSerializer.cs:131-135` 带 `UNITY_6000_3/4_OR_NEWER` 分支（本文 grep 复核）。

**各版本表现：**
- 6000.3：只有 `InstanceIDToObject` 报警告（实测）。
- 6000.4：`GetInstanceID` 和 `InstanceIDToObject` 都被标为 Obsolete；带 `FindObjectsSortMode` 的 `FindObjectsByType` 重载被标为 deprecated（本文复核了这三个 6000.4 API 页）。
- 6000.5：迁移手册说过时的 InstanceID API 变成编译错误，并把 `FindObjectsSortMode` 列为需要搜索替换的标识符（本文复核）。`FindObjectsByType` 在 6.5 是不是逐个 API 报错，单页里没写，但按手册的总体说法可能性很高。

**影响范围：**
- `Yoji.EditorDebug.Editor` 这一个程序集里也包含 `EditorDebugMCP`。它编译失败，editor-debug 服务就整体不可用。
- 测试程序集里也有没加分支的 `GetInstanceID`（下列路径都在 `<pkg>/Tests/Editor/EditorDebug/` 下）：
  - `ArgumentCoercerTests.cs:40`
  - `UiObserverTests.cs:513,516`
  - `UiWorldActorTests.cs:94,493,718`
  - `UiWorldActorBlockerTests.cs:120`
  - `UiWorldActorCellTests.cs:180`
  - `ReflectionInvokerTests.cs:82`

  这些只有在包被列进 `testables` 时才会编译。

**协议后果（U6-02，部分确认）：**
- 在 6000.4+ 上，`/invoke` 的 `instanceID` 是 ulong 十进制字符串（`ResultSerializer.cs:131-133`）。
- UI 端点的 `iid` 仍是 int：`UiActor.cs:344-354` 只接受 Int32，`<pkg>/Agent~/skills/unity-ui-observe/references/protocol.md:59` 也写的是 int。
- 迁移手册指出 EntityId 是 64 位，截断成 int 会悄悄丢掉高 32 位。
- 由于 6000.5 上这几处代码不改就编不过，这应当作为同一次迁移的设计跟进，而不是一个单独的缺陷。

**来源：**
- https://docs.unity.com/en-us/engine/6000.5/manual/scripting/fundamental-unity-types/instanceid-to-entityid-migration.md
- https://docs.unity3d.com/6000.4/Documentation/ScriptReference/Object.FindObjectsByType.html
- https://docs.unity3d.com/6000.4/Documentation/ScriptReference/Object.GetInstanceID.html
- https://docs.unity3d.com/6000.4/Documentation/ScriptReference/EditorUtility.InstanceIDToObject.html

**修复方向：**
- 抽一个内部 helper，复用已有的版本分支逻辑：`ArgumentCoercer.ResolveUnityObject` 有三段（6000.4+ / 6000.3 / 更早），`ResultSerializer.SerializeObjectId` 有两段。UI 代码和测试都改走这个 helper。
- `FindObjectsByType` 用 `#if UNITY_6000_4_OR_NEWER` 切到不带排序的重载。静态审查读文档后认为 6000.0-6000.3 没有这个重载，所以不能直接替换。这一点本文没有复核。
- 6000.4+ 上把 `iid` 改成字符串，让 `IntArg` 两种格式都接受，并同步更新 protocol.md。

**验证状态：** 6000.4 和 6000.5 都没有编译过。

### 3.2 [低][部分确认] `/console` 依赖内部 `LogEntry` 字段（U6-04）

**代码现状：**
- `<pkg>/Editor/EditorDebug/ConsoleHandler.cs:107-108` 用反射读 `instanceID` 和 `callstackText` 两个字段。
- `:109-114` 的必需成员检查只包含 message 和 mode。其他字段缺失时，静默返回默认值。

**UnityCsReference 对照（本文复核）：**
- 6000.3 的 `LogEntry` 有 `int instanceID`；6000.4 改成了 `EntityId entityId`。
- 2022.3、6000.3、6000.4 都没有 `callstackText` 字段，只有 `callstackTextStartUTF8` 和 `callstackTextStartUTF16`。

**后果：**
- 6000.4+ 上 `instanceID` 会一直是 0。这一点源码已确认，但没有实际运行。
- 在本文读过的三个版本（2022.3、6000.3、6000.4）上，`includeStack` 都拿不到单独的 stackTrace 字段。所以这不是 Unity 6 回归。实测 6000.3 时 `message` 里已经带了完整的堆栈行，信息并没有丢；不过 `includeStack` 本身没有做 live 测试。

**来源：**
- https://github.com/Unity-Technologies/UnityCsReference/blob/2022.3/Editor/Mono/LogEntries.bindings.cs
- https://github.com/Unity-Technologies/UnityCsReference/blob/6000.3/Editor/Mono/LogEntries.bindings.cs
- https://github.com/Unity-Technologies/UnityCsReference/blob/6000.4/Editor/Mono/LogEntries.bindings.cs

**修复方向：**
- 6000.4+ 读 `entityId`，按 `ResultSerializer` 的字符串格式输出。
- stackTrace 二选一：用 `callstackTextStartUTF16` 从 message 里切出来，或者在 protocol.md 里标为“不支持”。

**验证状态：** 6000.3 上 live `/console` 正常，返回 4 条，`instanceID` 都是 0。但普通日志如果没有 context 对象，这个值本来就是 0，所以这次运行区分不出问题。

### 3.3 [低][部分确认] `RetrieveTestList` 已过时（U6-06）

- 位置：`<pkg>/Editor/TestRunner/UnityTestRunnerApiAdapter.Unity6000.cs:37`。
- TF 1.4 和 1.6 的 API 页把它标为 obsolete，建议改用 `RetrieveTestTree`（https://docs.unity3d.com/Packages/com.unity.test-framework@1.6/api/UnityEditor.TestTools.TestRunner.Api.TestRunnerApi.html）。
- 实测：6000.3 + TF 1.6.0 下不报警告，live `list-tests` 正常。
- 剩下的风险是以后的 TF 把它删掉，这一点没有验证。
- 修复方向：`RetrieveTestTree` 存在时优先用它。

### 3.4 [低][已确认] 声明的 TF 1.1.33 不是 Unity 6 实际用的版本（U6-07）

- `<pkg>/package.json:21` 和各 fixture 的 manifest 都声明 1.1.33，而 6000.3 实际解析为 1.6.0。
- 这只是一个数据点。6000.0 和 6000.4+ 上的 TF 行为没有测过。
- 风险点：静态审查引用 TF changelog（https://docs.unity3d.com/Packages/com.unity.test-framework@1.6/changelog/CHANGELOG.html）说，1.4.0 改变了 `RunStarted` 收到的测试树，而 `<pkg>/Editor/TestRunner/TestRunService.cs:57-62` 的“0 命中”保护依赖这个行为。这一点本文没有复核。
- 修复方向：在 ping 结果里带上 TF 版本；有条件的话在 6000.0 上跑一遍。

### 3.5 [低][未验证] 只启用 Input System 的工程（U6-08）

- 代码不用旧的 Input，而是合成 `PointerEventData`，再用 `ExecuteEvents` 驱动（见 `<pkg>/Editor/EditorDebug/Ui/UiActor.cs`）。理论上和输入后端无关。
- 但所有 fixture 都是 `activeInputHandler: 0`（例如 `TestProjects/editor-debug/ProjectSettings/ProjectSettings.asset:775`），没有任何测试覆盖 Input System only 的情况。
- 旧的 Input Manager 已被标为过时；Unity 6 新建工程默认用什么输入后端，没有找到官方说明。参考：https://docs.unity.com/en-us/engine/6000.6/manual/input/legacy
- 建议：加一个 `activeInputHandler: 1` 的 fixture 变体，在上面跑 `/ui/act`。

### 3.6 [低][未验证] Editor 失焦时 `EditorApplication.update` 被节流（U6-09）

- 所有请求都排进 `EditorApplication.update` 执行（`<pkg>/Editor/Core/MainThreadDispatcher.cs:28-35,75-92`）。官方文档只说明了 Interaction Mode 的空闲行为（https://docs.unity3d.com/6000.1/Documentation/Manual/Preferences.html），没有说 Editor 失焦时会停止调用 update。
- 没有发现卡顿的证据。实测时服务由终端里的客户端驱动，响应都正常。
- 但当时没有记录 Editor 是否处于焦点状态，所以这不能算作反证。
- 建议：在 Editor 失焦时测一下 `/ping` 和 `/ui/snapshot` 的延迟。

## 4. 不合理设计和缺陷

### 4.1 安全：本地 HTTP 服务

#### SEC-1 [中][已确认] TestRunner 服务没有任何请求来源检查

**问题：**
- `TestRunnerMCP.Handle`（`<pkg>/Editor/TestRunner/TestRunnerMCP.cs:143-177`）不检查 Origin、Content-Type、Host。
- `/recompile` 不限 HTTP 方法（`:192-194`），内部会执行 `AssetDatabase.Refresh` 和 `RequestScriptCompilation`（`RecompileHandler.cs:47-48`）。
- `/run-tests` 只要求 POST（`:196`）。
- editor-debug 的 `/recompile`（`<pkg>/Editor/EditorDebug/EditorDebugMCP.cs:181-182`）同样不限方法。浏览器的 `<img>` GET 请求不带 Origin，所以那里的 Origin 检查挡不住。

**实测（2.6，6000.3.13f1）：**
- 不带 Origin 的 `GET /recompile` 在 test-runner 和 editor-debug 上都真的触发了重编译。
- test-runner 对带 Origin、`Content-Type: text/plain` 的 `POST /run-tests` 不做拦截，请求走到了参数校验。没有用合法 body 实际启动测试。
- editor-debug 拒绝带 Origin 的请求，但回的是 HTTP 200 加 `ok:false`，不是 4xx。

**后果：**
- 本机打开的恶意网页可以用 `<img>` 触发重编译和域重载。
- 也可以用 text/plain 的跨站 POST 启动测试运行；如果是 PlayMode，Editor 会进入 Play 模式。
- 从代码看，它拿不到任意代码执行。editor-debug 对 `/invoke` 也不检查方法，但有两道保护：
  - 带 Origin 的请求会被 `:143-144` 拒掉。按 WHATWG Fetch，跨站的非 GET/HEAD 请求一定带 Origin（值可能是字符串 `null`），所以这道检查对 POST 有效，对 GET 无效（见 2.6）。
  - GET 查询参数只会合并进 `/ui/snapshot` 和 `/ui/hit`（`:150-151`）。
- 浏览器侧的缓解不能替代服务端检查：Chrome 142 的 Local Network Access 是一个用户可以点“允许”的权限提示，不是服务端保证（见 2.6）。

**修复方向：**
- 两个 `/recompile` 和 `/run-tests` 都改成只接受 POST + `application/json`。
- 所有服务共用一个守卫，统一检查 Origin、Content-Type、Host。
- `<pkg>/Agent~/skills/test-runner-mcp/client.py:108` 现在用 GET 调 `/recompile`，要一起改。

#### SEC-2 [中][已确认] Lua 调试服务几乎不校验请求

**问题**（都在 `<pkg>/Runtime/LuaDeviceDebugServer/LuaDeviceDebugServer.cs`）：
- 用的是原始 `TcpListener`，绑定 loopback（`:116`）。
- `ReadRequest` 除了 Content-Length 之外忽略所有请求头（`:345-382`）。
- `Route` 对 GET 和 POST 一视同仁（`:189-196`）。
- `allowMutation` 直接取自请求体（`:254`）。
- 这个服务也会编进 Development Player：asmdef 的约束是 `UNITY_EDITOR || DEVELOPMENT_BUILD`。

**后果：**
- 如果项目注册了带可变命令的 Lua host，本机网页可以 POST 一个 text/plain 的 `{"allowMutation":true}` 来执行这条命令。
- 没有 Host 检查。实测（2.6）对任意 Host（包括 `evil.example`）和任意 Origin 都回 200，所以 DNS rebinding 没有任何阻挡，可以把结果读回去。端到端的 rebinding 没有演示。
- 实测时 `hostRegistered false`，也就是没有可执行的命令。带 Origin 的 text/plain `POST /execute` 走到了 host 就绪检查（409），命令执行这一段仍是读代码得出的。
- 在 Development Player 上，服务同样绑定设备的 loopback。Android 上同设备的其他 app 能连到这个端口（见 2.6），它们不发 Origin，所以下面的 Origin 和 Host 检查对它们无效。

**修复方向：**
- 拒绝带 Origin 的请求；要求 `application/json` 和 loopback 的 Host。adb forward 过来的请求，Host 里的端口是电脑上的转发端口，和设备端口不同，所以只比较主机名。
- `allowMutation` 只当作 UI 提示，真正的授权改用会话 token。要防同设备的其他 app，也只能靠 token 或握手。

#### SEC-3 [低][已确认] 没有认证

**现状：**
- `/invoke` 和 `/eval` 就是任意反射（`<pkg>/Editor/EditorDebug/ReflectionInvoker.cs`）。代码注释 `EditorDebugMCP.cs:21-23` 自己也承认两者攻击面相同。
- 防护只有 loopback 绑定和 Origin 检查。
- 防 DNS rebinding 依赖 HttpListener 自身的 Host 匹配（`EditorDebugMCP.cs:142` 的注释是这么说的），代码里没有 Host 检查。第二轮实测（2.6，6000.3.13f1）证实了这一点：test-runner 和 editor-debug 对 Host 不是 `127.0.0.1:<port>` 的请求一律回 400 `Invalid host`，连 `localhost:<port>` 也拒绝。所以在这两个服务上 DNS rebinding 被挡住了。2022.3 没有测，仓库里也没有对应的测试。
- 副作用：用 `http://localhost:<port>` 访问这两个服务会得到 400，客户端必须用 `127.0.0.1`。

**后果：** 同一台机器上的其他进程或用户可以完全控制 Editor。在单人开发机上，这是可以接受的设计取舍。

**修复方向（可选）：**
- 启动时生成一个 token，写进只有当前用户可读的文件，客户端通过 port_resolver 读取。
- 在 `Handle` 里显式做 Host 白名单。

### 4.2 健壮性

#### ROB-2 + U6-05 [中][部分确认] 测试运行器的状态无法从重载或崩溃中恢复

**(a) job 记录永远停在 running**
- `JobStore.StartJob` 会把 `{Status:"running"}` 写到磁盘（`<pkg>/Editor/TestRunner/JobStore.cs:76-90`）。
- 域重载后，新建的 `JobStore` 内存是空的。`Find` 找不到，就落到 `LoadFromDisk`（`:117-125`、`:188-194`），返回那条永远是 running 的旧记录。
- `SweepStale` 只看内存里的 `m_Current`（`:146-159`），回收不了磁盘上的这条。
- 后果：轮询这个 jobId 的客户端永远等不到完成。新的运行不受影响。

**(b) 项目设置被改动后无法还原**
- `ApplyPlayModeSettings`（`<pkg>/Editor/TestRunner/TestRunService.cs:168-176`）把 `DisableDomainReload` 写进会持久化的 `EditorSettings`，原值却只存在实例字段里。
- 还原只发生在两处：`:75`（异常路径）和 `:138`（`RunFinished`）。
- 重载时 `TestRunnerMCP.Stop` 会把 `s_Service` 置空（`TestRunnerMCP.cs:109`），原值随之丢失。
- 后果：PlayMode 运行中如果 Editor 崩溃或被强制重载，项目会一直开着 `DisableDomainReload`，原来的选项找不回来，只能手动去 Project Settings 改回。这个设置是否已经先写到磁盘，没有验证。这个问题和 Unity 6 无关。

**触发条件：** 运行中途发生重载或崩溃，比较少见。实测没有覆盖：只通过 HTTP 服务跑过 EditMode。

**修复方向：**
- `Start` 时扫描 `Temp/TestRunnerMCP/job_*.json`，把状态还是 running 的标成 error。
- 把原来的 Play Mode 选项存进 `SessionState` 或 Temp 下的文件；启动时如果没有活跃 job，就自动还原。

#### ROB-1 [低][部分确认] 主线程调度器超时后任务仍会执行

- `<pkg>/Editor/Core/MainThreadDispatcher.cs:75-93` 超时后会抛 `TimeoutException`，但任务还留在队列里。`Pump`（`:58-67`）之后照样会执行它。队列也没有上限。
- 后果：客户端拿到超时后如果重试，副作用会执行两次。
- 实际很少发生：重载前 `DrainForReload` 会清空队列，默认超时是 100 s。
- `<pkg>/Runtime/LuaDeviceDebugServer/MainThreadDispatcher.cs` 已经有 Cancelled 状态和 16 的上限，可以直接移植过来。

#### DES-1 [低][部分确认] act 和 snapshot 取屏幕尺寸的时机不一致

**代码现状：**
- `/ui/act` 走普通的 `RunOnMain`（`EditorDebugMCP.cs:187-190`）；`RunWorldAct` 的第一步也是（`:272-273`）。
- `/ui/snapshot` 和 `/ui/hit` 走的是 `gameFrame: true`（`:183-186`）。
- `UiContext` 在构造时读 `Screen.width` 和 `Screen.height`（`Ui/UiContext.cs:22-23`）。
- 作者自己在 `<pkg>/Agent~/skills/unity-ui-observe/references/troubleshooting.md:34` 写过：不在 player frame 里采样，会拿到 Editor 窗口的坐标。

**后果：** Play Mode 下，act 和 snapshot 可能用的是不同的屏幕尺寸，于是误报 offscreen 或 covered，或者点错位置。这个后果没有实测：live `/ui/act` 没有覆盖到。

**修复方向：** act 也改走 `UiFrameDispatcher`；再加一个 PlayMode 测试，在非默认的 GameView 尺寸下做点击。

#### DES-2 [低][未验证] UI 层的拒绝格式不统一

- not_playing 时返回 `ok:true` 加 reason；Editor 暂停时却抛异常，最后变成 `ok:false`。
- 每次请求都重新安装一遍 PlayerLoop（`<pkg>/Editor/EditorDebug/Ui/UiFrameDispatcher.cs:28-61`），这样做有什么影响还没确定。
- 修复方向：所有“预期中的不可执行状态”（包括暂停和超时）统一返回 `ok:true` 加 reason；PlayerLoop 在每次 Play 会话里只装一次，在 ExitingPlayMode 时移除。

#### ROB-3 [低][未验证] Lua 调度器在重载时可能卡住

- 它没有重载前的 drain，`Stop()` 也不释放还在等待的任务（`<pkg>/Runtime/LuaDeviceDebugServer/MainThreadDispatcher.cs:43-71`）。
- 重载前后可能卡住最多 5 s，但这一点没有被证实。
- 修复方向：`Stop()` 时把还在等待的任务标为 Cancelled 并释放；`ReadRequest` 加上整体时限和请求头数量上限。

### 4.3 跨平台（macOS）

#### L1 [中][部分确认] Linker agent sync 只支持 Windows

**代码现状：**
- agent sync 写死用 `WindowsJunctionManager`，它通过 DllImport 调 `kernel32.dll`（`<pkg>/Editor/U3DAILinker/Operations/WindowsJunctionManager.cs:28-47`）。
- 生产代码里创建它的地方有 4 处：`Settings/U3DAILinkerSettingsProvider.cs:322,504,797` 和 `Operations/U3DAILinkerBootstrap.cs:83`。
- 另外还有一个所有平台都能看到的菜单项：`Operations/WindowsJunctionSmoke.cs:9,19`。
- 代码里没有任何 OS 判断，也没有 symlink 实现；asmdef 也不限平台。
- `Create` 会先 `Directory.CreateDirectory(linkPath)` 再调 P/Invoke，失败时可能留下一个空目录。

**后果：**
- 调用方都有 try/catch，并会回滚（`Operations/AgentSyncService.cs:140-215`），所以表现为日志报错或面板提示，不会让 Editor 崩溃。
- 原 finding 说 `PreflightTargets` 没有保护，这一点不成立。
- README 只写了 "Windows for the first linker junction workflow"（`README.md:208`），没有明确说 macOS 上 agent sync 用不了。
- 实测没有覆盖。具体异常类型（推测是 `DllNotFoundException` 或 `EntryPointNotFoundException`）没有通过运行确认。

**修复方向：**
- 加一个按平台选择实现的工厂。macOS 和 Linux 用 symlink：2022.3 的 Mono 没有 `Directory.CreateSymbolicLink`，需要起一个 `ln -s` 进程，或者改成“复制 + ownership 文件”的方式。
- 非 Windows 平台上要给出明确的错误，并清理已经建好的目录。
- 在修好之前，先在 README 里写清楚。

#### L2 [低][已确认] agent sync 的步骤顺序不当

- `AgentSyncOrchestrator.Sync` 的顺序是：先写 CLAUDE.md 和 AGENTS.md（`<pkg>/Editor/U3DAILinker/Settings/AgentSyncOrchestrator.cs:49`），再逐个工具同步 skills（`:53-70`），最后写 .gitignore。
- 如果某个工具同步失败，markdown 里已经留下了一个托管块，指向并没有装上的 skill。
- 重跑是幂等的。在 macOS 上这个问题会和 L1 叠加。
- 修复方向：先同步 skills，再写文本文件。

#### L7 [低][已确认] Python 客户端在 macOS 上找不到全局端口注册表

- Python 侧的 `port_resolver._local_appdata()`（`<pkg>/Agent~/skills/test-runner-mcp/port_resolver.py:221-225`，四份副本 md5 相同）在没有 `LOCALAPPDATA` 环境变量时，回退到 `~/AppData/Local`。
- C# 侧用的是 `Environment.GetFolderPath(LocalApplicationData)`（`<pkg>/Editor/Core/Ports/ServiceInstanceRegistry.cs:25-31`）。
- 本机 macOS 上，Editor 实际写到了 `~/.local/share/Yoji/U3D-Dev-Tools-AI/instances.json`，而 `~/AppData` 根本不存在（本文复核）。
- 后果：在 macOS 上，全局注册表这条回退路径永远读不到。不过项目级的 `ports.json` 和 legacy 端口优先级更高，实测带 `--project` 时所有服务都能找到。
- 修复方向：
  - 按 `sys.platform` 复刻 Mono 的路径规则，或者让 Editor 把注册表路径直接写进 `ports.json`。
  - 在 `tools/test-agent-port-resolvers.py` 里加一个 darwin 用例。

#### L9 [低][部分确认] 部分脚本只能在 Windows 上方便使用

- `tools/run-editmode.ps1:76-100` 只搜索 Windows 默认的 Hub 目录，只找 `Unity.exe` 和 `Unity.cmd`，并按 `;` 切分 `U3D_EDITOR_ROOTS`。在 macOS 上必须显式传 `-Unity` 或设 `UNITY_EXE`，README 里写了这一点。
- `install.ps1`、`act.ps1`、`shot.ps1` 和 `tools/check-u3d-linker-stable.ps1` 只有 PowerShell 版本。
- skills 也可以通过 Linker 安装，所以 `install.ps1` 不是唯一途径。总体属于便利性缺口。

### 4.4 数据安全：改写用户文件

| ID | 级别/状态 | 问题和位置 | 后果 | 修复方向 |
|---|---|---|---|---|
| L3 | 低/已确认 | `AtomicTextFile.Write`（`<pkg>/Editor/U3DAILinker/Agents/AtomicTextFile.cs:12-21`）先 `File.Delete` 再 `File.Move`，并不是原子操作。 | 如果在这两步之间崩溃，CLAUDE.md、AGENTS.md 或 .gitignore 会暂时消失，内容只留在 `.u3dtmp` 文件里。 | 照 `Operations/ManifestTransaction.cs:158` 的做法用 `File.Replace`，改一行即可。 |
| L4 | 低/已确认 | `Agents/ManagedBlockWriter.cs:23-25,55-63` 读文件时把 CRLF 统一成 LF，然后整个文件写回去。 | CRLF 的仓库第一次同步就会出现整文件的 diff。块内的用户修改被覆盖是有意的设计（块里写着 "Do not edit"），不算缺陷。 | 写回时保留原文件的换行风格。 |
| L5 | 低/已确认 | `ManifestTransaction.Apply` 在读（`:32`）和替换（`:158`）之间没有再检查一次。 | 如果 UPM 或用户恰好在这几毫秒内写了 manifest，那次修改会丢失。 | 替换前比较哈希，不一致就放弃并提示重试。 |
| L6 | 低/已确认 | `Operations/GitLsRemoteResolver.cs:38-60` 先后顺序读 stdout 和 stderr，`WaitForExit()`（`:57`）不设超时，URL 也没加引号就拼进命令（不过它是常量，不存在注入）。设置面板会同步调用它（`Settings/U3DAILinkerSettingsProvider.cs:682,999`）。 | 一旦 git 卡住（比如网络黑洞），Editor 界面会冻住。只有用户点 Refresh、Install 或切换渠道时才会触发，Editor 启动时不会。 | 改成异步，或者用有超时的 `WaitForExit(ms)`，超时就 kill；再设 `GIT_TERMINAL_PROMPT=0`。 |

## 5. 改进空间

**可维护性**
- **MAINT-1 [低][已确认]** 三个服务的代码大量重复：
  - `EditorDebugMCP` 和 `TestRunnerMCP` 的监听、生命周期、心跳、响应写回代码几乎逐行相同（`EditorDebugMCP.cs:31-134`，`TestRunnerMCP.cs:30-141`）。
  - 两个 `RecompileHandler` 也近似是副本；TestRunner 那份的注释直接说是有意复制的（`<pkg>/Editor/TestRunner/RecompileHandler.cs:11-12`）。
  - 错误返回格式有三套：EditorDebug 一律回 200 加 ok/error；TestRunner 用真实状态码加 success/error；Lua 用状态码加 error.code。
  - SEC-1 里的守卫只在一个服务里有，根源就在这里。
  - 修复方向：在 `Editor/Core` 里抽出一个 loopback HTTP host，负责生命周期、守卫、心跳和统一的错误格式，各服务只提供路由。Lua 运行时用的是 TcpListener，套不进同一个抽象，但守卫逻辑可以共用。
- **L14 [低][部分确认]** `U3DAILinkerSettingsProvider.cs` 有 1072 行，状态全放在静态 IMGUI 字段里。`AgentSyncOrchestrator.cs:107-142` 又重新实现了一遍 block writer 里已有的 marker 预检。这一条偏主观。
- **L15 [低][部分确认]** `AgentSyncService.cs:234-241` 用 `string.Replace` 映射路径。要让它出错，完整的绝对 src 路径必须在路径里再次出现，实际不会发生。改用 `Substring(src.Length)` 只是让代码更干净。

**协议**
- **PROTO-1 [低][已确认]** 各服务报告的版本号和包版本脱节：
  - editor-debug 的 ping 写死 `0.1.5`（`EditorDebugMCP.cs:20`）；
  - Lua 写死 `0.1.1`（`<pkg>/Runtime/LuaDeviceDebug/LuaDeviceDebugPackage.cs:7`）；
  - TestRunner 的 ping 根本没有版本字段；
  - 而包本身是 0.2.23。
  - 客户端也从不比对版本。实测的 ping 输出证实了这些。
  - 修复方向：ping 返回包版本和端点列表，版本对不上时客户端给出警告。

**测试**
- **TEST-1 [低][部分确认]** 以下几块没有测试覆盖：
  - `EditorDebugMCP.Handle` 拒绝 Origin 的分支；
  - `TestRunnerMCP` 的路由和方法检查；
  - 调度器超时后任务仍被执行的情况；
  - 重载后留下的 running 孤儿 job；
  - 两个 `RecompileHandler`；
  - Play Mode 下的 live `/ui/act`。

  前四项在 EditMode 里就能测。另外，2.4 里两个失败的测试要先修好，否则套件永远不会全绿，真正的回归会被淹没在里面。

**CI**
- **L10（包含 L8）[低][已确认]** 仓库里没有 `.github` 目录，也没有任何其他 CI。
  - `tools/test-*.py` 全靠手动跑。其中 `tools/test-agent-port-resolvers.py:145-147` 已经断言四份 port_resolver 逐字节相同。
  - 根目录和包内 `Registry/*.json` 归一化后是否相等，已经由 EditMode 测试断言（`<pkg>/Tests/Editor/U3DAILinker/RegistryFixtureTests.cs:82-92`），实测设了环境变量时通过。
  - 所以真正的缺口只有一个：没有东西自动去跑这些检查。
  - 修复方向：加一个最小的 GitHub Actions（ubuntu），跑 python 测试、py_compile 和 JSON 校验；Unity 相关的运行留在本地。

**文档**
- **L11 [低][部分确认]** “已验证版本”在三个地方互相矛盾：
  - README 写的支持范围是 "Unity 2022.3 LTS through Unity 6000.3.x"，已验证版本是 2022.3.62f2c1 和 6000.3.12f1（`README.md:203-204`）；
  - `<pkg>/Agent~/skills/unity-editor-debug-mcp/references/manual-verification.md:7,31` 写的是 6000.3.16f1；
  - 这次实测用的是 6000.3.13f1 和 2022.3.62f3c1。

  仓库里所有 fixture 都是 2022.3.62f2c1，没有 Unity 6 的 fixture，也没有运行记录。6000.3 支持本身已经被实测证实，问题在于版本号三处说法不一致，而且别人没法复现。修复方向：
  - 统一成一份已验证版本列表；
  - 在 Requirements 里写明“6000.4+ 未验证；6000.5 已知不兼容”。
- **L12 [低][已确认]**
  - `docs/lua-runtime-whitebox.md:4` 和 `docs/superpowers/specs/2026-06-25-targeted-agent-skills-design.md:4` 里写着本机仓库的绝对路径。
  - `docs/lua-runtime-whitebox.md:10` 提到了一个下游私有项目的名字。
  - 这两点都违反仓库 CLAUDE.md 的规则。
  - 另外，`docs/superpowers/` 写在 `.gitignore:24` 里，但其中的文件仍被 git 跟踪。
  - `manual-verification.md:7` 是一个明显的占位路径，可以接受，但建议改成 `<project-root>`。
- **L13 [低][已确认]** packages-lock.json 的处理不统一：
  - 只有 `TestProjects/editor-debug/Packages/packages-lock.json` 被跟踪；
  - `TestProjects/test-runner` 的 lock 既没跟踪也没忽略，一直出现在 `git status` 里；
  - 用 Unity 6 运行时，lock 文件会被改写。

  修复方向：所有 fixture 统一忽略 lock 文件。

**发布流程**
- **L16 [低][部分确认]** 发布要手动改好几处：`package.json`、根目录和包内的 `Registry/*.json`、README 里的 tag、changelog。
  - `dev.json` 里的 commit，按这个流程天然会落后一个提交。
  - 唯一的交叉检查脚本 `tools/check-u3d-linker-stable.ps1` 只有 pwsh 版本。
  - 从提交历史看，维护者每次都做得一致，“容易漂移”只是推测。
  - 修复方向：把检查移植成 Python，并扩展到 README 和 changelog。

## 6. 建议的处理顺序

下面全部都还没有实施。

**P0**
1. **修好两个失败的测试。** 文件：`<pkg>/Tests/Editor/LuaDeviceDebug/PackageShapeTests.cs`、`<pkg>/Tests/PlayMode/UiObserverHttpTests.cs`。成本最低，能让基线变绿。
2. **完成 Unity 6.4/6.5 的 ID 迁移（3.1 和 3.2）。**
   - 在 `<pkg>/Editor/EditorDebug/` 下新增 helper；
   - 修改 `Ui/UiObserver.cs`、`Ui/UiActor.cs`、`Ui/UiWorldActor.cs`、`Ui/UiRaycast.cs`、`ConsoleHandler.cs`；
   - 更新 `<pkg>/Agent~/skills/unity-ui-observe/references/protocol.md`；
   - 同步修改 3.1 列出的测试文件。
   - 改完至少要在 6000.4 或 6000.5 上编译一次。本机目前没有这两个 Editor。

**P1**
3. **加本地 HTTP 守卫（SEC-1、SEC-2，顺带完成 MAINT-1 的守卫抽取）。**
   - 修改 `<pkg>/Editor/TestRunner/TestRunnerMCP.cs`、`<pkg>/Editor/EditorDebug/EditorDebugMCP.cs`、`<pkg>/Runtime/LuaDeviceDebugServer/LuaDeviceDebugServer.cs`；
   - 把 `<pkg>/Agent~/skills/test-runner-mcp/client.py` 里的 `/recompile` 改为 POST；
   - 补上 Handle 层面的测试。
4. **让测试运行器能从重载或崩溃中恢复（ROB-2/U6-05）。** 修改 `<pkg>/Editor/TestRunner/JobStore.cs`、`TestRunService.cs`、`TestRunnerMCP.cs`，并加一个孤儿 job 的测试。
5. **处理 macOS 上的 Linker（L1、L2）。**
   - 涉及：`<pkg>/Editor/U3DAILinker/Operations/WindowsJunctionManager.cs`（或者新增 symlink 实现加工厂）、`Settings/U3DAILinkerSettingsProvider.cs`、`Operations/U3DAILinkerBootstrap.cs`、`Operations/WindowsJunctionSmoke.cs`、`Settings/AgentSyncOrchestrator.cs`、`README.md`。
   - 最少也要先在 README 里写明 agent sync 目前只支持 Windows。
6. **修正 port_resolver 在 macOS 上的路径（L7）。** 改 `<pkg>/Agent~/skills/test-runner-mcp/port_resolver.py`，再用 `tools/sync-agent-port-resolver.py` 同步到另外三份；`tools/test-agent-port-resolvers.py` 加一个 darwin 用例。

**P2**
7. **一些小的健壮性修复：**
   - `AtomicTextFile.cs`（L3）
   - `ManagedBlockWriter.cs`（L4）
   - `ManifestTransaction.cs`（L5）
   - `GitLsRemoteResolver.cs`（L6）
   - `<pkg>/Editor/Core/MainThreadDispatcher.cs`（ROB-1）
   - `EditorDebugMCP.cs`：让 act 也走 game frame（DES-1），并加 PlayMode 下的 act 测试。
8. **文档和流程：**
   - 修正 `README.md`、`manual-verification.md` 和 `docs/` 里的两处路径（L11、L12）；
   - 调整 `.gitignore`（L12、L13）；
   - ping 返回版本号（PROTO-1）；
   - 搭一个最小 CI（L10）；
   - 用 Python 重写 run-editmode 和 stable check（L9、L16）。
9. **补上覆盖缺口：**
   - Unity 6 新建工程的 fixture（只用 Input System）；
   - 在 6000.0 和 6000.4+ 上编译；
   - 在 Windows 上完整跑一遍；
   - IL2CPP dev player。

## 7. 覆盖范围与局限

**Unity 6 静态审查（U6-xx）**
- 读过：全部 asmdef 和 package.json；Core 的调度器和生命周期；EditorDebug 的主要文件，以及 Ui 目录里相关的部分；两个 TestRunner adapter；TestRunService；Lua bootstrap；PlayMode 下的 GameView 测试。
- 对 Editor、Runtime、Tests 全量 grep 了 Unity 6 的风险 API。
- 没有逐行读：EditorServiceEndpoint、Ports、ServicePortSettingsStore、JobStore 内部实现、Linker 设置 UI、ReflectionInvoker、DescribeHandler、EvalParser。
- 检查后没有发现问题的：
  - 没有使用旧 Input；
  - TMP 按类型名反射获取；
  - `ScriptRunBehaviourUpdate` 和 `QueuePlayerLoopUpdate` 在 6000.3 上存在；
  - ugui 2.0 的 ExecuteEvents 和 GraphicRegistry 可用；
  - 没有用 Camera.Render 或 GrabPass；
  - `AlwaysLinkAssembly` 用法正确。

**服务设计审查（SEC/ROB/DES/MAINT/PROTO/TEST）**
- 读过：Core、EditorDebugMCP、两个 RecompileHandler、UiFrameDispatcher、UiActor、UiContext、TestRunnerMCP、TestRunService、JobStore、Lua 服务和它的调度器。
- 只是浏览过：UiObserver、UiWorldActor（957 行）、ResultSerializer、ServiceInstanceRegistry，以及另外 3 个 Python 客户端。
- 没有验证：退出 Play Mode 后 PlayerLoop 是否保留；Editor 失焦时是否被节流。Mono HttpListener 对 Host 头的行为在第二轮实测过（2.6）。

**Linker、Agent~ 和工具审查（L1-L16）**
- 读过：Linker 的 Operations、block writer、Settings provider 的部分代码、port_resolver 和 client、install.ps1、tools 脚本的开头部分、Registry、README、docs 的文件头、fixture 的 manifest。
- 没有通读：GitignoreBlockWriter、FragmentMerger、RegistryValidator、InstallQueueBuilder、UpmQueueRunner、UnityUpmClient，以及大部分 Python 测试的函数体。
- 审查时 ls-remote 失败，远端 tag 另行核对过：`u3d-dev-tools-ai-v0.2.23` 已推送，指向 `1307392`。Python 3.8 兼容性没有逐一检查。

**本文复核的内容**

每条 finding 都经过了独立核验。写报告时，又额外复核了以下内容：
- 3.1 的全部调用点，以及 6000.4 和 6000.5 的官方页面；
- 3.2 涉及的 2022.3、6000.3、6000.4 三个版本的 `LogEntries.bindings.cs`；
- SEC-1 和 SEC-2 的请求处理函数；
- ROB-2 涉及的 JobStore 和 TestRunService；
- L1 的所有调用点；
- L7 在本机的注册表位置；
- 两个失败测试的根因；
- `RegistryFixtureTests` 的一致性断言；
- `U3D_HAS_PHYSICS` 在 6000.3 上能正确解析（见 2.3）。这一点原来是静态审查没能确认的问题，现在已经解决。
- 第二轮：三个服务对 Host、Origin、方法和 Content-Type 的实际反应（2.6），以及 Fetch 规范、Chrome Local Network Access 和 Android loopback 的文档依据。

**实测的局限**
- 只在 macOS arm64 上，6000.3.13f1 和 2022.3.62f3c1 各跑了一次。
- 第二轮的 Host 和 Origin 探测只在 6000.3.13f1 上做，用 curl 伪造请求头，没有用浏览器，也没有在 Development Player 上做。
- fixture 都是从 2022 升级上来的工程。
- 其他未覆盖的项见 1.1。

## 附录：没有采纳或被否定的论断

没有哪条 finding 被整体判为 refuted。下面这些子论断，或者被实测和复核否定，或者被合并到别的条目：

- **U6-06**：“6000.3 上 `RetrieveTestList` 至少会产生 CS0618”——6000.3 编译时没有这条警告。
- **U6-06**：“`ToXml().WriteTo` 可能需要额外的 nunit 引用”——6000.3 编译通过了。
- **U6-04**：“`includeStack` 在 6000.x 上会丢失堆栈”——`message` 里已经带了堆栈；而且 2022.3 也没有 `callstackText` 字段，所以这不是 Unity 6 回归。
- **U6-02**：并入 U6-01。6000.5 上这些代码无论如何都得改，它不是一个独立的缺陷。
- **U6-03**：原来定为 medium。在 6000.3 上没有任何影响，降为低级别，随 U6-01 一起处理。
- **L1**：“`PreflightTargets` 没有 try/catch 保护”——它的调用方都有 try/catch。
- **L8**：“应该新增四份 port_resolver 相同的断言”——`tools/test-agent-port-resolvers.py:145-147` 已经有了，所以并入 L10。
- **L10**：“根目录和包内 registry 的一致性只有 pwsh 脚本在检查”——`RegistryFixtureTests.cs:82-92` 已经断言了两者归一化后相等。
- **L11**：“Unity 6 支持没有被证明”“ugui 1.0.0 依赖在 Unity 6 上的行为未知”——6000.3 实测通过，UPM 解析为 ugui 2.0.0。
- **L15**：原 finding 给出的触发例子（子目录名重复父目录的前缀）并不会触发问题。
- **L4**：“块内的用户修改被静默覆盖”算作缺陷——那个块里明确写着 "Do not edit"，这是有意的设计。
- **核验注释里几条当时没有出处的说法，第一轮没有当作论据采用。** 第二轮的处理：
  - 浏览器的本地网络限制会缓解 SEC-1：Chrome 142 起确实有权限提示，但用户可以点允许，不能当作服务端保证（见 2.6、SEC-1）；
  - Package Manager 自己写 manifest 也不加锁：仍未采用；
  - HttpListener 对非法 Host 回 400：第二轮已在 6000.3.13f1 上实测（见 2.6、SEC-3）。
