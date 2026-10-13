# 语义 UI 观测层 (Semantic UI Observation Layer)

Status: Phase 1 (observe) 随 v0.2.12 发布（2026-10-12，v0.2.13 修内城世界空间 TextMeshPro 异常）；Phase 2 (act) 随 v0.2.14 发布（2026-10-13）。

## 目的

让 AI agent 用**结构化事实**判断 uGUI 的当前状态——哪个面板在最上、某控件现在能不能点、引导有没有抢占——
而不是退回截图 + eval 探针 + computer-use。这是 U3D Dev Tools AI 独占的互补车道：Pipeline CLI 管编译/测试/eval，
MCP 管交互式操作与截图，都不碰"游戏 UI 语义真相"。

### 要堵的准确度漏洞

实战记录里反复出现：agent 截图抓到的是**错状态**（引导抢占了基地、GameView 尺寸不对），只有人读 PNG 才发现。
根因是 uGUI 的"谁在最上、点击打到谁"由 `Canvas.overrideSorting + sortingOrder` 与 `ICanvasRaycastFilter`
（引导遮罩的洞）共同决定，截图和层级 dump 都看不出来。

## 范围

- **Phase 1 = observe only**（本文主体）：两个只读端点，一次调用拿全量语义状态。
- **Phase 2 = act**（后续）：按语义 id 经 EventSystem 注入 uGUI 指针事件，注入前重算命中、被遮挡则拒绝。
- 目标栈限定 **uGUI**（Canvas / Graphic / Selectable）。UIToolkit 已有部分遍历，IMGUI 不做。
- 世界空间点击（CityCamera 走 legacy `UnityEngine.Input`）**不在** act 覆盖范围，单列。

## 落点：寄生 EditorDebug 服务

新端点加进现有 `Editor/EditorDebug/EditorDebugMCP` 服务，不新建服务。复用：HttpListener (loopback)、
主线程派发 (`MainThreadDispatcher`)、端口发现 (`.u3d-ai-linker/ports.json` + `instances.json`)、CLI 客户端
(`Agent~/skills/.../client.py` + `port_resolver.py`)。EditorDebug 跑在 Editor 程序集里，可直接引用 `UnityEngine.UI`，
读运行中场景的 EventSystem 与全部 Canvas，不靠反射。Pipeline 与 MCP 一行不碰。

### 端点

| 端点 | 作用 |
|---|---|
| `GET /ui/snapshot[?include=all&max=N]` | 当前整棵语义 UI 树 |
| `GET /ui/hit?x=&y=` | 某屏幕点上有效命中的最顶控件（"这里点下去打到谁"） |
| `POST /ui/act`（Phase 2） | 按语义 id 注入 click/drag，注入前重算命中 |

### 两条硬约束（都是踩过的坑）

1. **自带紧凑序列化**，不走通用 `ResultSerializer`——它把 `Vector3` 展开到 ~1.7KB、`Quaternion` ~10KB
   (`ResultSerializer.cs` SerializeComplex 递归到 maxDepth=12)。UI 节点只出 int/string/bool 扁平字段。
2. **节点超限给截断标记 + 分页**（`max` 参数 + `truncated` 标志 + `total`），不学 EditorDebug 现在的
   4MB 整包丢弃信封 (`EditorDebugMCP.cs:332-338`，请求上限才 1MB `RequestBodyReader.cs:8`)。

## snapshot 数据形状

顶层三块 `meta` / `layers` / `nodes`，外加 `guide`。

### meta

```
{ "frame": int, "scene": string, "playing": bool,
  "screen": {"w": int, "h": int}, "eventSystemPresent": bool,
  "currentSelected": id|null }
```

### layers —— 页 / 弹窗栈

所有根 Canvas 按**有效顺序**排（sortingLayer 值 → sortingOrder → 层级序），逐层：

```
{ "id": string, "name": string, "sortingOrder": int, "overrideSorting": bool,
  "renderMode": "ScreenSpaceOverlay"|"ScreenSpaceCamera"|"WorldSpace",
  "blocksBelow": bool, "topmost": bool }
```

`blocksBelow` = 这层有没有铺满屏幕的 raycast target 或遮罩挡住下面。引导遮罩
(`SafehouseGuideClickMask`: `overrideSorting=true`, `sortingOrder=MaskSortingOrder`) 就作为一层出现，`blocksBelow=true`。

### nodes

默认只出**可交互控件 + 它们的文字标签**（Selectable 或带 raycastTarget Graphic），不是场景里每个 Image。
`?include=all` 放开到全部节点。

```
{ "id": string,          // 稳定语义 id，见下
  "iid": int,            // instanceID，仅同帧快捷通道用
  "path": string,        // canvas 相对层级路径
  "type": string,        // Button/Toggle/InputField/ScrollRect/TMP/Image/...
  "text": string,        // 解析后的标签（Text/TMP，或 Button 的子标签）
  "rect": {"x":int,"y":int,"w":int,"h":int},  // 屏幕坐标，左上原点
  "visible": bool,       // activeInHierarchy && CanvasGroup链alpha>0 && Graphic.enabled
  "interactable": bool,  // Selectable.IsInteractable() && CanvasGroup链interactable
  "raycastTarget": bool,
  "hitTop": bool,        // 杀手字段：对该控件中心 EventSystem.RaycastAll，它是不是最顶命中
  "coveredBy": id|null } // hitTop=false 时，压在上面的是谁
```

**`hitTop` / `coveredBy` 是准确度核心**。`EventSystem.RaycastAll` 天然尊重 `ICanvasRaycastFilter`
（遮罩的洞）与 sorting，所以"现在点它是否真打得到"一次算准。`coveredBy` 直接回答"为什么点不到"
（如 `GuideClickMask`）。

**实现注记（v0.2.12）**：没有直接调 `EventSystem.RaycastAll` / `GraphicRaycaster.Raycast`。两者都依赖渲染副产物——
`RaycasterManager` 只收 OnEnable 过的 raycaster（`BaseRaycaster` 非 ExecuteAlways），`GraphicRaycaster` 跳过
`depth==-1`（canvas 尚未渲染）的图元——编辑模式与 batchmode 下结果为空。`UiRaycast` 照搬 GraphicRaycaster 的逐图元过滤
（`Graphic.Raycast` 仍走 `ICanvasRaycastFilter` 与 CanvasGroup），depth 换成根 canvas 下的先序遍历序，比较器镜像
`EventSystem.RaycastComparer`。不覆盖 PhysicsRaycaster 与 `blockingObjects` 物理遮挡。
v0.2.13：`Graphic` 不 RequireComponent `CanvasRenderer`，世界空间 `TMPro.TextMeshPro`（走 MeshRenderer）也是 Graphic；
没有 CanvasRenderer 的图元按不绘制处理（`UiContext.Culled`），否则 Cargo 内城的建筑名牌会让 snapshot 抛 `MissingComponentException`。

### guide —— 引导抢占一次判清

```
{ "active": bool, "maskId": id, "holeRect": {"x","y","w","h"}, "targetId": id }
```

引导遮罩是 `ICanvasRaycastFilter`，带 `screenHole`，除了洞哪儿都吃点击。那个"截图抓错状态"的 bug，
靠这块一次判掉：`active=true` 且 `targetId != 期望面板` ⇒ 当前被引导抢占。

#### 怎么识别（2026-10-12 补，对照 Cargo 实现）

host 的洞和目标拿不到：Cargo `SafehouseGuideClickMask` 的 `screenHole` / `hasHole` / `finger` 全是 private，
公开面只有 `IsRaycastLocationValid`。"host 零改动 + 不靠反射"下，guide 块只能**用 filter 本身探出来**：

1. **候选遮罩**：激活且 `raycastTarget` 的 Graphic，实现 `ICanvasRaycastFilter`，**类型不在 `UnityEngine.UI`
   命名空间**（`Image` 本身就实现该接口，全屏背景图不能被当成遮罩），屏幕矩形覆盖全屏。多个取 `layers` 最顶者。
   无候选 ⇒ `active=false`。
2. **探洞**：按步长 `S`（默认 24px）网格调 `IsRaycastLocationValid(p, eventCamera)`；返回 `false` 的点即洞
   （点击穿透）。`holeRect` = 这些点的包围盒，精度 ±`S`。全格都返回 `true` ⇒ 全屏吃点击，`holeRect=null`。
   竖屏 1080x2400 约 4.5k 次调用，只在请求时跑，不进帧循环。Cargo 的 filter 每次调用重算洞，幂等，探测无副作用。
3. **targetId**：`holeRect` 内中心命中、`hitTop=true` 的节点，取面积最小者。
   **洞下没有 uGUI 控件 ⇒ `targetId=null` 而 `holeRect` 有值**：引导指向世界空间目标（Cargo 内城建筑
   `GuidePos` 锚点，`finger.WorldTarget` 分支），这是正常态，不是识别失败。

升级路径：探洞精度不够时，加 Runtime asmdef 定义 `IUiGuideMaskSource { bool TryGetHole(out Rect hole, out Transform target); }`
由 host 实现。代价是新 asmdef（包里现有 Runtime 程序集只有 `LuaDeviceDebug.Runtime`）+ host 改一处，破"零改动"，Phase 1 不取。

## 语义 id

主 id = **canvas 相对的层级路径**，如 `CityHud/BottomBar/BtnHeroes`。同名兄弟才追加下标 `[i]`。
不用 instanceID——实测几秒就失效（"no alive object"）。稳定边界分三类，写进文档不含糊：

- **authored / 生成面板**（CityHudStep / SafehouseHudAuthoring 起了确定名字）：路径跨会话稳定。
- **运行时列表格子**（ScrollRect 里 N 个同构 cell）：按"父节点内位置下标 + 该格关键文字"标识；
  只在该列表内容不变时稳定。
- **本地化文字**：`text` 随调试语言变，所以 id 走路径不走文字，`text` 只作信息。

`iid`（instanceID）作**同帧快捷通道**：拿到 snapshot 立刻 act 用 iid 省一次解析；跨帧/跨会话一律用稳定 `id`。

## 测试

**一个可跑的 EditMode 检查**。搭一座合成 Canvas：底板 + 一个在某按钮上开洞的引导遮罩 + 一个被盖住的按钮。断言：

- `layers` 排序正确（遮罩在底板之上）；
- 洞里的按钮 `hitTop=true`；
- 被盖的按钮 `hitTop=false` 且 `coveredBy=` 遮罩 id；
- `guide` 块填着 holeRect/targetId；
- 洞挪到无控件处时 `targetId=null`、`holeRect` 仍有值；全屏 `Image` 背景不被认成遮罩。

排序或 raycast 逻辑一坏这条就红。再借现成 `Agent~/skills/.../references/run-e2e.py` 骨架对活 Editor 冒烟
（snapshot 能回、节点数合理）。不引新框架。

## 安全

observe 只读，继承 loopback。但 EditorDebug 现在**完全没有** Origin/Content-Type/token 校验
（实测 POST 带任意 Origin 成功）。既然动这个服务，顺手补一道 Origin/Content-Type 拒绝。
Phase 2 的 act 是 EditorDebug 上第一个真正的写操作，mutation guard 正好在那时落地。

## 发版与接入

- U3D 发 **v0.2.12**（additive 端点），走和 0.2.11 一样的发布仪式：改 `package.json` 版本 + 四个
  `Registry/*.json` revision + 打 tag `u3d-dev-tools-ai-v0.2.12` + 推 main 与 tag。
- host 侧零改动——EditorDebug 读任意 uGUI，不挂游戏专属 host。
- Cargo 侧两件（见 Cargo 票 `.scratch/ui-observation-layer/`）：UPM ref 升一行 + 装 `unity-ui-observe` skill +
  路由规则写进 `cargo-client/CLAUDE.md`（工作树只读它）与根仓 `CLAUDE.md` "Unity 工具路由" 两处。**没有这一步，工具重蹈 U3D 现状"造了没人用"。**

## Phase 2：act（v0.2.14 实现）

实现与速写的差别，以代码 `Editor/EditorDebug/Ui/UiActor.cs` 和 skill 的 `references/protocol.md` 为准：

- **只收 POST + `application/json`**，GET 查询参数只并入 snapshot / hit。否则跨站 `<img src=".../ui/act?id=...">` 这类不带 Origin 的 GET 就能点。
- **事件序列逐段照搬 StandaloneInputModule 的鼠标路径**，而不是只发 pointerClick：move（悬停沿父链，移植 `HandlePointerExitAndEnter`）、
  press（`DeselectIfSelectionChanged`、down、initializePotentialDrag）、drag（`pixelDragThreshold`、按下对象不是拖拽对象时先抬起并取消点击）、
  release（up、click、drop、endDrag），最后补一次指针离开（等同手指抬起，不留悬停高亮）。拖拽每步重建命中器，beginDrag 里新出现的放置区能接到 drop。
- **拒绝按真人可达性判**：可交互性查在真正接事件的 Selectable（命中冒泡到的处理者）上，不查目标自身；不查 CanvasGroup alpha（射线不看 alpha）。
- **跨场景同名根导致 id 重名**时回 `ambiguous`，用同一次 snapshot 的 `iid` 消歧。
- 同一帧发完：长按、双击（`clickCount` 恒 1）、依赖帧间时间的处理不模拟。
- Cargo 实机（6000.3.12f1）走通：壳选择页进基地、点掉对白推进 Cn1、摇杆拖拽（阈值内 `dragged=false`）、内城引导下 HUD/非目标行被拒 `covered`、
  扳手 -> `Row_1017` -> 世界空间建筑菜单 `Key_1` 一路推进到 Ct2:6。

原速写：


`POST /ui/act {id, action: "click"|"drag", to?}`：路径解析回 GameObject → `ExecuteEvents.Execute` 注入
pointerClick。注入前重算一次 `hitTop`，**被遮住就拒绝并回 `coveredBy`**——agent 不会"点"到真人也点不到的东西。
observe 阶段把 `hitTop` 建对，act 阶段零额外判定直接复用。世界空间（CityCamera legacy Input）另案。
