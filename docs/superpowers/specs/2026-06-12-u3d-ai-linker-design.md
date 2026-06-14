# U3D AI Linker 设计

## 目标

为 Unity 工程提供一个独立、公开、可通过 Git URL 安装的轻量 Linker。

首版服务于个人 Windows 开发环境。它负责批量安装本仓库中的 Unity AI 工具，并将项目级 Skills 和规则同步给 Claude Code 与 Codex。工具更新后，目标工程可以从稳定版本或开发分支主动刷新。

## 核心原则

- 当前仓库采用 Monorepo。
- 每个工具独立封装、独立启停、独立声明版本。
- Linker 只负责编排，不复制工具业务实现。
- 用户文件只修改托管区块，不整体覆盖。
- 所有同步操作必须可重复执行。
- 首版仅支持 Windows 和 Unity 2022.3 及以上版本。

## 仓库结构

```text
U3D-Dev-Tools-AI/
  Packages/
    com.yoji.u3d-ai-linker/
      Editor/
      Registry/
      BundledSkills~/
      package.json
    com.yoji.editor-core/
      Editor/
      package.json
    com.yoji.test-runner/
      Editor/
      Agent~/
        skills/
        fragments/
      package.json
    com.yoji.editor-debug/
      Editor/
      Agent~/
        skills/
        fragments/
      package.json
    com.yoji.lua-device-debug/
      Runtime/
      Editor/
      Agent~/
        skills/
      package.json
  Registry/
    stable.json
    dev.json
```

`Packages/` 存放可由 Unity Package Manager 安装的包。

每个工具包的 `Agent~/` 存放 Skill、脚本、参考文档、示例和规则片段。预期目录：

```text
Agent~/
  skills/
    <skill-name>/
      SKILL.md
      scripts/
      references/
      assets/
  fragments/
    CLAUDE.md
    AGENTS.md
```

任意 `~` 后缀目录是否能在 Unity 2022.3 Git Package 的 `resolvedPath` 中稳定保留，必须在实现前通过最小探针同时验证 `Agent~/` 与 `BundledSkills~/`。探针失败时停止后续打包，不以经验假设继续实现；替代方案改为包内 `Editor/Resources/AgentData.zip.bytes` 和 `Editor/Resources/BundledSkills.zip.bytes`，由 Linker 解压到 staging，避免 Skill 文件被 Unity 当作普通 Package Asset 导入。

根目录 `Registry/` 是公开发布源，描述工具列表、默认启用状态、UPM 子路径和目标 revision。Linker 包内保留一份 Registry 快照，作为离线安装和远程获取失败时的只读回退。

Registry 顶层必须包含 `schemaVersion`。Linker 遇到不支持的版本时停止解析，避免新版目录误驱动旧版 Linker。

每个包条目还必须声明迁移状态、包类别、安装顺序和 Agent 资产位置：

```json
{
  "id": "editor-debug",
  "status": "ready",
  "kind": "tool",
  "order": 20,
  "packageName": "com.yoji.editor-debug",
  "packagePath": "Packages/com.yoji.editor-debug",
  "revision": "editor-debug-v1.2.0",
  "defaultEnabled": true,
  "userToggle": true,
  "agentAssets": "Agent~",
  "minUnity": "2022.3"
}
```

`status` 只能取：

- `ready`：公开 Package 与 Skill 都已具备，可安装。
- `skill-only`：过渡态 Skill 随 Linker 的 `BundledSkills~/` 交付，不发起独立 UPM 安装。它随 Linker 版本更新，不具备独立版本。
- `planned`：尚未完成迁移，只展示状态。

`kind` 只能取：

- `tool`：用户可见工具，允许启停与 Agent 同步。
- `infra`：用户不可见基础包，例如未来 `com.yoji.editor-core`。它没有 Skill，不出现在启停列表里，但可作为其它工具的前置安装项。
- `linker`：Linker 自身。更新队列中必须最后处理。

`userToggle=false` 的条目不显示启停开关。`agentAssets=null` 或缺省表示该包没有 Agent 侧资产。`minUnity` 是必填兼容性字段，声明该包要求的最低 Unity 版本（`editor-debug`/`test-runner` 为 `2022.3`，`lua-device-debug` 为 `6000.3`）。Linker 在 `Install All`/单项安装前必须比对目标工程 Editor 版本与 `minUnity`，低于要求则跳过该项并显式告警，避免把高基线包（如 `6000.3` 的 `lua-device-debug`）误装进 2022.3 工程导致编译失败；缺 `minUnity` 字段按非法处理。Registry 还应显式声明包间关系：

```json
{
  "id": "test-runner",
  "dependsOn": ["editor-core"]
}
```

`dependsOn` 只表达 Linker 的安装拓扑，不依赖 UPM 包内 Git URL dependency。Linker 必须按拓扑排序后再按 `order` 安装；发现环依赖、未知 ID 或 infra 被用户直接启用时，拒绝整个操作。

当前仓库中的工具尚未形成公开 UPM 包时，初始 Registry 必须标为 `planned` 或 `skill-only`。`Install All` 只处理 `ready` 项。

## 安装模型

用户首次通过 Git URL 安装 Linker：

```text
https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/com.yoji.u3d-ai-linker#linker-v1.0.0
```

Linker 从公开仓库获取对应通道的 Registry，再使用 Unity `PackageManager.Client.Add` 串行安装工具包。UPM 请求不能并行执行。每次请求完成后才能启动下一项。

### Git URL 同级依赖阻断

Unity Package Manager 不支持在一个 Git UPM 包的 `package.json` 中声明另一个 Git URL 依赖；Git dependency 只能作为目标工程 `Packages/manifest.json` 的顶层依赖声明。官方 Git dependency 文档也要求多子包 Monorepo 用多个 `?path=/Packages/<package>` 条目分别加入项目 manifest。

因此本设计禁止用工具包 `package.json` 解决 sibling package 依赖。所有来自本仓库的 Git UPM 包都由 Linker 写入目标工程 manifest 顶层：

```json
{
  "dependencies": {
    "com.yoji.u3d-ai-linker": "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/com.yoji.u3d-ai-linker#linker-v1.0.0",
    "com.yoji.editor-core": "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/com.yoji.editor-core#editor-core-v1.0.0",
    "com.yoji.editor-debug": "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/com.yoji.editor-debug#editor-debug-v1.2.0",
    "com.yoji.test-runner": "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/com.yoji.test-runner#test-runner-v1.1.0"
  }
}
```

> 上例是工具迁移完成、进入 `ready` 并打出 Stable tag 后的**稳态 manifest，非当前现状**：它不表示当前仓库已经发布这些包。初始 Registry 中未发布项必须标为 `planned` 或 `skill-only`，此时 `Install/Update` 只处理 `ready` 项、manifest 不写入未发布工具；`editor-core` 同理，在其作为 `kind:"infra"` 包发布前不出现于 manifest。示例未列出 `lua-device-debug`，实际迁移完成后应将其纳入。

这带来两个结果：

- `com.yoji.editor-core` 必须在 Registry 中作为 `kind:"infra"` 条目出现，由 Linker 安装和升级，而不是被 `editor-debug` 或 `test-runner` 自动拉取。
- Stable 与 Dev 通道必须对同一次操作中的所有本仓库包使用一致 revision 策略：Stable 使用各自 tag；Dev 使用同一个远端 `main` commit SHA。不得混用 `#main` 与 tag。

远程 Registry 地址固定为：

```text
https://raw.githubusercontent.com/sputnicyoji/U3D-Dev-Tools-AI/main/Registry/<channel>.json
```

网络失败时允许使用 Linker 包内快照，但界面必须明确标记为离线数据，不得声称已经检查到最新版本。

Registry 不是任意安装脚本。解析后必须执行白名单校验：

- 仓库固定为 `sputnicyoji/U3D-Dev-Tools-AI`，Registry 不接受自定义仓库 URL。
- `packageName` 必须以 `com.yoji.` 开头。
- `packagePath` 必须是 `Packages/<packageName>`，禁止 `..`、绝对路径和 URL。
- Stable revision 必须匹配 `<tool-id>-v<semver>`。
- Dev Registry 的 `branch` 只能是 `main`；运行时解析出的 revision 必须是 40 位 Git commit SHA。
- 工具 ID、Package 名和 Skill 名必须唯一。
- 未知字段、未知状态和不支持的 `schemaVersion` 均拒绝整个 Registry。

安装 URL 只能由 Linker 使用已验证字段生成，不能直接执行 Registry 中提供的完整 URL。

工具包 URL 使用以下格式：

```text
https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/<package>#<revision>
```

本机开发模式使用 `file:` 路径，不经 Git URL：

```json
{
  "dependencies": {
    "com.yoji.editor-debug": "file:E:/Yoji/U3D-Dev-Tools-AI/Packages/com.yoji.editor-debug"
  }
}
```

`file:` 模式只服务个人内循环，不应提交到共享游戏工程。Linker 可以在 Project Settings 中提供 Local 通道，但必须把本机绝对路径写入 `UserSettings/U3DAILinkerUserSettings.asset`，项目级 `ProjectSettings/U3DAILinkerSettings.asset` 只记录“当前选择 Local 通道”和启用工具，不记录绝对路径。

Local 通道仍会临时改写目标工程的 `Packages/manifest.json`，否则 Unity Package Manager 无法加载本机包。它的语义是“本机开发状态”，不是“共享可提交状态”。Project Settings 面板必须在 Local 通道下明确显示警告，并提供 `Restore Stable/Dev` 操作，把所有 `file:` 托管依赖恢复为可提交的 tag/SHA Git URL。诊断报告也必须列出当前 manifest 中的托管 `file:` 依赖，避免开发者误提交。

同一 Git 仓库的多个 UPM 子包会被 Unity 重复获取。这是 Monorepo 的已知成本。首版接受该成本，不额外实现本地镜像或自建 Registry。

## 双通道更新

通道定义：

- `stable`：可提交、可复现。Registry 指向 tag，适合团队/CI。
- `dev`：可提交、可复现。Registry 指向远端 `main` 的一次解析结果，Linker 固定为 40 位 commit SHA。
- `local`：本机内循环。使用 `file:` 指向本仓库工作区，会临时写入本机 manifest；仅用于开发者本机快速验证，不应提交到共享仓库。

### Stable

`stable.json` 为每个工具指定发布 Tag。同步时只在目标 Tag 变化后更新依赖。稳定通道必须可回滚到旧 Tag。

Monorepo Tag 使用唯一前缀：

```text
linker-v1.0.0
editor-debug-v1.2.0
test-runner-v1.1.0
lua-device-debug-v1.0.0
editor-core-v1.0.0
```

### Dev

`dev.json` 描述 Dev 可用工具，并以 `"branch": "main"` 声明唯一允许的开发分支，但不直接保存 revision。由于 Unity 可能缓存 Git revision，Linker 提供显式 `Refresh Dev`：

1. 通过一次 `git ls-remote` 查询远程 `main` 的最新 commit。
2. 将同一次操作中的全部工具 URL 固定到该 commit SHA。
3. 调用 `Client.Add` 更新依赖。
4. 安装完成后重建 Agent 链接。

使用 commit SHA 而不是长期保留 `#main`，保证更新可检测、可复现。

Linker 自身更新必须排在队列最后。更新 Linker 会替换当前 Editor Assembly，恢复逻辑只能依赖持久化操作日志，不能依赖旧窗口实例或静态字段。

### Manifest 修改事务

Linker 写 `Packages/manifest.json` 时必须把 manifest 当作项目配置文件处理，而不是普通文本拼接：

1. 读入并解析 JSON，保留未知字段。
2. 写入前复制 `Packages/manifest.json` 到 `Library/U3DAILinker/backups/manifest-<operationId>.json`。
3. 只修改 `dependencies` 中由 Registry 管理的 `com.yoji.*` 条目；第三方依赖和用户手写条目不动。
4. 写入临时文件 `Packages/manifest.json.u3d-ai-linker.tmp`。
5. 解析临时文件确认合法 JSON。
6. 原子替换正式 manifest。
7. 写入 `Library/U3DAILinker/operation.json`，记录旧值、新值、channel、revision 和受影响包。

失败恢复原则：

- 替换前失败：删除 tmp，不改 manifest。
- 替换后但 UPM 解析失败：保留失败状态和 backup 路径，Project Settings 面板提供 `Rollback Manifest`。
- 目标 dependency 已存在且不是 Linker 管理的 URL 或 file 路径：标为冲突，要求用户确认接管，不自动覆盖。
- Linker 自身条目不得在同一操作开始阶段移除；自更新永远作为最后一项。

### 安装、同步与移除语义

`Install/Update` 与 `Sync Agent Assets` 是两个不同动作：

- `Install/Update` 只处理 `status:"ready"` 的包条目，并按 `dependsOn` 递归加入所需 `infra` 包。它修改 manifest 并触发 UPM。
- `Sync Agent Assets` 处理 `status:"ready"` 和 `status:"skill-only"` 的 Agent 资产。`ready` 从已安装包的 `agentAssets` 读取；`skill-only` 从 Linker 包内 `BundledSkills~/<tool>/` 或 zip fallback 读取。`planned` 不参与同步。
- `Install/Update All` 对用户启用的 `tool` 取闭包：启用工具 + 其 `infra` 依赖。`infra` 不直接显示启停开关。

`Remove` 必须分两步处理：

1. 先禁用目标 `tool` 并移除其 Agent 资产、Junction、托管 fragment 和 `.gitignore` 条目；只删除 ownership 匹配的内容。
2. 再计算剩余启用工具的 `dependsOn` 闭包。只有没有剩余 dependents 的 `infra` 才能从 manifest 中移除。Linker 自身不能由普通 `Remove` 删除。

manifest 删除同样走“Manifest 修改事务”流程，写 backup、tmp、operation log，并提供 rollback。若目标 packageName 在 manifest 中的当前值不是 Linker 管理的 Git URL 或 Local `file:`，则标为冲突，不自动删除。

## Agent 同步模型

目标工程中的物理目录：

```text
<project>/.u3d-ai-linker/skills/<tool>/
```

Linker 为以下位置创建 Windows Junction：

```text
<project>/.claude/skills/<tool>/
<project>/.agents/skills/<tool>/
```

`ready` 工具的 `Agent~/` 内容通过 `PackageInfo.resolvedPath` 定位。`skill-only` 工具从 Linker 包内 `BundledSkills~/<tool>/` 定位。两者复制到物理目录后，再由 Junction 提供 Claude Code 和 Codex 视图。同步前先验证目标是否由 Linker 管理；不得覆盖普通目录或未知链接。

### Agent 资产探针与 fallback

实现前必须先做最小探针，验证从 Git URL 安装后的 `PackageInfo.resolvedPath` 下仍可读取：

- `Packages/com.yoji.editor-debug/Agent~/skills/unity-editor-debug-mcp/SKILL.md`
- `Packages/com.yoji.test-runner/Agent~/skills/test-runner-mcp/SKILL.md`
- `Packages/com.yoji.u3d-ai-linker/BundledSkills~/`

探针步骤：

1. 建一个空 Unity 2022.3 工程。
2. 用 Git URL 安装 linker 包。
3. 通过 Editor 代码调用 `PackageInfo.FindForAssetPath` 或 `Client.List` 取得 `resolvedPath`。
4. 用 `File.Exists` 和 `Directory.Exists` 检查 `Agent~/` 与 `BundledSkills~/`。
5. 把结果写入 `Library/U3DAILinker/probe-result.json`，并显示在 Project Settings 面板。

若 `~` 后缀目录在 Git Package Cache 中不可读，Linker 不继续使用目录模式。替代方案：

- 每个工具包把 Agent 资产打成 `Editor/Resources/U3DAgentAssets.zip.bytes`。
- Linker 包把 bundled skill 打成 `Editor/Resources/BundledSkills.zip.bytes`。
- 同步时先解压到 `.u3d-ai-linker/.staging/`，再走同一套 ownership、hash 和 Junction 流程。

目录模式和 zip 模式对外产物必须一致：`.u3d-ai-linker/skills/<tool>/` 下的 `SKILL.md`、`scripts/`、`references/`、`fragments/` 布局不变。

复制采用事务式目录替换：

1. 复制到 `.u3d-ai-linker/.staging/<tool>-<operationId>/`。
2. 校验所有声明的 `SKILL.md` 存在。
3. 写入包含工具 ID、来源 revision 和内容哈希的 `.u3d-ai-owner.json`。
4. 若旧目标存在且 ownership 合法，先移动到 `.u3d-ai-linker/.backup/<tool>-<operationId>/`。
5. 将 staging 移动为 `.u3d-ai-linker/skills/<tool>/`。
6. 创建或修复 Junction，成功后删除 backup。

所有移动必须在同一卷内完成。步骤 4 之后失败时恢复 backup；步骤 4 之前失败时只删除 staging。目标目录缺少合法 ownership 文件时视为用户目录，不覆盖、不删除。

Codex 使用 `AGENTS.md`。Claude Code 使用 `CLAUDE.md`。Linker 只维护以下区块：

```markdown
<!-- u3d-ai-linker:start -->
Generated by U3D AI Linker. Do not edit this block.
<!-- u3d-ai-linker:end -->
```

区块之外的用户内容保持不变。文件不存在时创建。标记损坏或重复时停止写入并报告冲突。

托管区块内容来自各启用工具的 `fragments/CLAUDE.md` 与 `fragments/AGENTS.md`。片段可缺省。合并顺序按 Registry 的 `order` 升序，再按工具 ID 排序；每段前写入来源注释。`order` 必须是非负整数。发现重复 Skill 名时整个同步预检失败，不进行部分写入。

Linker 同时维护项目根 `.gitignore` 中的独立区块：

```text
# >>> u3d-ai-linker >>>
/.u3d-ai-linker/
/.claude/skills/<managed-skill>/
/.agents/skills/<managed-skill>/
# <<< u3d-ai-linker <<<
```

只忽略 Linker 生成的具体 Skill 路径，不忽略整个 `.claude/` 或 `.agents/`。标记损坏时停止修改。卸载工具时只删除 ownership 匹配的物理目录、Junction 和对应 ignore 行。

## Project Settings 控制面板

主入口不是独立 EditorWindow，而是 Unity Project Settings：

```text
Edit > Project Settings > U3D AI Linker
```

实现使用 `SettingsProvider`，路径为：

```csharp
[SettingsProvider]
public static SettingsProvider CreateProvider()
{
    return new SettingsProvider("Project/U3D AI Linker", SettingsScope.Project);
}
```

控制面板是项目级工具安装与 Agent 同步的控制台。它必须克制、可审计、适合反复操作，不做营销式首页。

### 面板信息架构

顶部状态区：

- 当前通道：Stable / Dev / Local。
- Registry 来源：Remote / Bundled Snapshot / Local File。
- Registry schemaVersion、更新时间、当前 revision。
- 当前操作：Idle / Running / Failed / Needs Recovery。
- Git/UPM 前置检查：Git 可用、网络可用、Package Manager 空闲。

工具列表：

| 列 | 含义 |
|---|---|
| Enabled | 只对 `kind:"tool"` 且 `userToggle:true` 显示 |
| Tool | 工具 ID 与 displayName |
| Kind | tool / infra / linker |
| Installed | missing / installed / conflict |
| Desired | Registry 中的 tag、SHA 或 local path |
| Current | manifest / lock file / PackageInfo 解析出的当前值 |
| Agent | synced / stale / missing / conflict / not-applicable |
| Action | Install、Update、Sync、Repair、Remove |

底部操作区：

- `Refresh Registry`
- `Install/Update Selected`
- `Install/Update All`
- `Sync Agent Assets`
- `Repair Links`
- `Rollback Manifest`
- `Open Generated Folder`
- `Copy Diagnostic Report`

Local 通道额外显示本机仓库路径输入框与探测按钮。路径只写入 `UserSettings`，不得写入可提交的 `ProjectSettings`。

### 交互原则

- 默认不自动后台更新。所有网络、manifest、文件链接变更都由用户显式触发。
- 操作前显示将要修改的包名、旧值、新值和文件路径。
- 操作中禁用会造成并发 UPM 请求的按钮。
- 失败时保留日志，按钮变为 `Retry` / `Rollback Manifest` / `Cancel Operation`。
- 面板不得隐藏 infra 包状态；infra 不可启停，但必须显示“由哪些工具需要它”。
- 面板不直接编辑用户 `CLAUDE.md` / `AGENTS.md` 正文，只显示托管区块健康状态。

## 状态与错误处理

项目状态写入：

```text
ProjectSettings/U3DAILinkerSettings.asset
```

状态分三层：

- `ProjectSettings/U3DAILinkerSettings.asset`：通道、启用工具和项目期望版本。可提交，不含绝对路径。
- `UserSettings/U3DAILinkerUserSettings.asset`：窗口状态和本机偏好。不要求提交。
- `Library/U3DAILinker/operation.json`：当前操作日志。临时文件，不提交。

UPM 安装可能触发脚本编译和域重载，批量队列不得只保存在内存中。每次请求前原子写入 `operation.json.tmp`，再替换正式日志。日志至少包含：

```json
{
  "operationId": "guid",
  "action": "install-all",
  "toolIds": ["editor-debug"],
  "currentIndex": 0,
  "phase": "package-requested",
  "channel": "stable",
  "resolvedRevision": "editor-debug-v1.2.0",
  "manifestBackupPath": "Library/U3DAILinker/backups/manifest-guid.json",
  "dependencyChanges": [
    {
      "packageName": "com.yoji.editor-debug",
      "oldValue": null,
      "newValue": "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/com.yoji.editor-debug#editor-debug-v1.2.0"
    },
    {
      "packageName": "com.yoji.editor-core",
      "oldValue": null,
      "newValue": "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/com.yoji.editor-core#editor-core-v1.0.0"
    }
  ],
  "completed": [],
  "retryCount": 0
}
```

Linker 使用 `[InitializeOnLoad]` 和 `EditorApplication.delayCall` 在 Editor 不再编译、Package Manager 空闲后恢复。由于旧 `AddRequest` 无法跨域保留，恢复时先用 `PackageInfo` 与项目 manifest 核对目标 URL/revision：已达到目标则推进队列，否则最多重试两次。超过上限后停止并保留日志供用户重试或取消。

失败处理：

- UPM 操作失败时停止队列，保留已成功项。
- 恢复队列时以实际 manifest 与已解析 Package 为准，操作日志只表示意图。
- Rollback 使用 `manifestBackupPath` 或 `dependencyChanges[].oldValue` 恢复 manifest；恢复前再次读取当前 manifest，若托管依赖已被用户手动改动则停止并要求人工确认。
- Junction 冲突时不删除目标，要求用户确认处理。
- Registry 无法读取时不改变现有安装。
- 托管区块非法时不修改文件。
- 每个错误显示工具 ID、操作、源路径和原始错误。

## 验证

Editor 测试至少覆盖：

- Registry 解析和字段校验。
- Registry 仓库、路径、命名、revision、`kind`、`dependsOn`、未知字段拒绝。
- `infra` 拓扑排序、环依赖拒绝、用户不可启停。
- Stable URL、Dev SHA URL 与 Local file URL 生成。
- Manifest 修改事务：保留未知字段、只改托管依赖、写 tmp、解析验证、backup 路径记录。
- Manifest 冲突检测：已有非 Linker 管理 dependency 时不覆盖。
- UPM 安装队列状态转换、日志原子写入和域重载恢复。
- Linker 自更新排在队列最后。
- `SettingsProvider("Project/U3D AI Linker")` 注册和 UI 状态模型。
- ProjectSettings 与 UserSettings 分离：本机绝对路径不得写入 `ProjectSettings/U3DAILinkerSettings.asset`。
- `Agent~/` / `BundledSkills~/` 探针结果持久化和 zip fallback 分支。
- 托管区块创建、更新、保留用户内容和冲突检测。
- Fragment 确定性合并和重复 Skill 名预检。
- `.gitignore` 托管区块创建、更新和损坏保护。
- Junction 创建、重复同步、失效修复、ownership 校验和未知目录保护。
- staging 失败不破坏现有同步版本，替换失败能恢复 backup。

手动验证覆盖 Unity 2022.3：

1. 先验证 `Agent~/` 在 Git Package Cache 中可读取。
2. 从空工程用 Git URL 安装 Linker，确认入口出现在 `Edit > Project Settings > U3D AI Linker`。
3. Stable 通道一键安装全部 `ready` 工具，并在中途触发域重载。
4. 验证重载后队列继续，Claude Code 与 Codex 均能发现 Skill。
5. 更新 Stable Tag 后执行升级和回滚。
6. 推送 `main` 后执行 `Refresh Dev`，确认所有工具使用同一 SHA。
7. Local 通道选择本机 `E:/Yoji/U3D-Dev-Tools-AI`，确认 manifest 使用 `file:`，且绝对路径只写入 UserSettings。
8. 制造网络失败、非法 Registry、普通目录冲突、已有非托管 dependency 和损坏 marker，确认不覆盖用户内容。
9. 卸载单个工具，确认 infra 依赖、其他工具和用户文档不受影响。

## 实施顺序

1. Linker 空包 + `SettingsProvider("Project/U3D AI Linker")` 骨架。
2. `Agent~/` / `BundledSkills~/` Package 探针与 zip fallback 决策。
3. Registry Schema v2：`status`、`kind`、`dependsOn`、严格校验和拓扑排序。
4. Manifest 修改事务与 Git URL 顶层依赖安装策略。
5. 持久化 UPM 操作队列和域重载恢复。
6. Skill 事务复制、ownership 与 Junction。
7. CLAUDE/AGENTS Fragment 合并和 `.gitignore` 托管区块。
8. Project Settings 控制面板补齐操作按钮、状态表和诊断报告。
9. 逐个将现有工具从 `planned` 迁移到 `ready`；把 `editor-core` 建为 `kind:"infra"`。

在对应公开 Package 尚未完成前，不把现有公司内部服务描述为已迁移完成。

## 首版非目标

- macOS 与 Linux。
- Cursor 与 Gemini CLI。
- 私有 Git 仓库认证。
- 后台自动更新。
- 自建 UPM Registry。
- Tap4fun 内部 Package 的直接复用。
- 自动迁移现有 `.agent-linker` 工程。
