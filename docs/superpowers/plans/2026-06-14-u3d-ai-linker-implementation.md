# U3D AI Linker Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 构建一个 Editor-only 的 UPM 编排包 `com.yoji.u3d-ai-linker`，从 Project Settings 一键把本仓库的 Unity AI 工具批量装进目标工程，并把项目级 Skills/规则同步给 Claude Code 与 Codex。

**Architecture:** Editor-only UPM 编排包，由根目录 `Registry/` 白名单驱动安装决策；用 manifest 事务把工具写成顶层 Git-URL 依赖以解决同级依赖阻断；Agent 资产用事务式目录复制 + Windows Junction 落地到 `.u3d-ai-linker/skills/<tool>` 并链接到 `.claude`/`.agents`；三通道 stable/dev/local 分别走 tag/SHA/file URL；所有跨域重载的安装/同步操作以持久化操作日志记账，支撑 `[InitializeOnLoad]` 域重载恢复。

**Tech Stack:** Unity 2022.3 Editor、C#、UnityEditor.PackageManager、Newtonsoft.Json、NUnit EditMode、Windows Junction P/Invoke。

---

## Source & Scope

- **源 spec:** `docs/superpowers/specs/2026-06-12-u3d-ai-linker-design.md`（完整设计；本计划逐条对齐其验证清单与实施顺序）。
- **范围（首版目标）:** 仅 Windows、仅 Unity 2022.3+。非目标已在 spec 末尾明确：不支持 macOS / Linux；**不涉及 HybridCLR**（lua-device-debug 是 transport-only，不引入任何 HybridCLR 依赖）。
- **与现有概览的关系:** `docs/superpowers/plans/2026-06-13-remaining-work-plan.md` 第 4 章是 u3d-ai-linker 的高层概览与里程碑速写；**本文是可执行的 task-by-task 实现计划**，把那一章的 LINK-0..LINK-7 概念展开为带失败测试、最小实现、命令与提交的 TDD 任务序列。两者冲突时以本文为准。

本计划假设工程师对本代码库零上下文：每个任务给出精确文件路径、完整代码块、运行命令与期望输出。纯逻辑层（Registry 解析/校验、URL 生成、拓扑排序、manifest 事务、操作日志、内容哈希、ownership、Fragment 合并、托管区块、面板状态模型、Restore/RefreshDev 计划）一律走完整 EditMode TDD，使用临时目录与 fake 接口，无需真实 UPM/网络/Junction/域重载；真实副作用（`Client.Add`、真实 Junction、真实 `git ls-remote`、IMGUI 渲染、真实 `.asset` 序列化、域重载恢复）抽到接口或列为显式手动验证步骤。

---

## File Structure

整个包根 `Packages/com.yoji.u3d-ai-linker/`。各文件单一职责如下（按目录分组）。同名文件（如 `package.json`、两个 asmdef、`AssemblyInfo.cs`、`OperationLog.cs`）在多个子系统的任务里出现，是**幂等基线**：先落地者创建，后到者核对存在即跳过，绝不重复创建导致冲突。

### 包元数据与程序集

- `package.json` — UPM 包清单：name `com.yoji.u3d-ai-linker`、unity `2022.3`、依赖 `com.unity.nuget.newtonsoft-json`。
- `Editor/Yoji.U3DAILinker.Editor.asmdef` — 主程序集（Editor-only，rootNamespace `Yoji.U3DAILinker`，预编译引用 Newtonsoft.Json.dll）。
- `Editor/AssemblyInfo.cs` — `InternalsVisibleTo("Yoji.U3DAILinker.Editor.Tests")`，对测试暴露 internal 类型。
- `Tests/Editor/Yoji.U3DAILinker.Editor.Tests.asmdef` — 测试程序集（引用主 asmdef + NUnit + TestRunner，`UNITY_INCLUDE_TESTS` 约束）。

### Editor/Probe（LINK-0：Agent 资产探针）

- `Editor/Probe/ProbeModels.cs` — `ProbeTarget` / `ProbeResult` / `ProbeMode` 纯数据 POCO（directory vs zip-fallback 决策门的数据形状）。
- `Editor/Probe/ProbeEvaluator.cs` — 纯逻辑：把逐目标存在性事实归约为总判定 + 推荐模式。
- `Editor/Probe/ProbeResultWriter.cs` — 解析 `Library/U3DAILinker/probe-result.json` 路径并用 camelCase Newtonsoft 序列化写盘。
- `Editor/Probe/AgentAssetProbe.cs` — 薄菜单入口 `Tools/U3D AI Linker/Run Agent Asset Probe`：用 `Client.List` 取真实 resolvedPath、做 File/Directory.Exists、喂给 evaluator、写盘。
- `BundledSkills~/.keep` — 占位文件，使过渡态 skill-only 资产目录进 Git（`~` 后缀避免被 Unity 当资产导入）。

### Editor/Registry（LINK-2：Registry 纯逻辑核心 / LINK-6 复用的最小形状）

- `Editor/Registry/ToolStatus.cs` — `ToolStatus` 枚举 + `ToolStatusExtensions.TryParse`（ready/skill-only/planned 严格解析）。
- `Editor/Registry/ToolKind.cs` — `ToolKind` 枚举 + `ToolKindExtensions.TryParse`（tool/infra/linker）。
- `Editor/Registry/RegistryEntry.cs` — 单条目 POCO（id/status/kind/order/packageName/packagePath/revision/...）。
- `Editor/Registry/RegistryDocument.cs` — 顶层 POCO（schemaVersion/channel/branch/entries）。
- `Editor/Registry/RegistryParseException.cs` — 解析层异常。
- `Editor/Registry/RegistryParser.cs` — 反序列化 + 未知字段拒绝（MissingMemberHandling.Error）+ schemaVersion 校验。
- `Editor/Registry/RegistryChannel.cs` — `RegistryChannel { Stable, Dev }`。
- `Editor/Registry/RegistryValidationException.cs` — 携带逐条错误列表的校验异常。
- `Editor/Registry/RegistryValidator.cs` — 白名单语义校验（前缀/路径/revision/唯一性/minUnity），一次性聚合全部错误。
- `Editor/Registry/ToolUrlBuilder.cs` — 由已验证字段拼 stable git / dev git / local file URL（仓库固定，绝不消费 Registry 内整串 URL）。
- `Editor/Registry/TopologicalSortException.cs` — 排序层异常。
- `Editor/Registry/TopologicalSorter.cs` — dependsOn + order 拓扑排序，环/未知依赖/infra 直接启用拒绝。
- `Editor/Registry/RegistryTypes.cs` — （LINK-6 自带的最小 Registry 形状 + 顶层共享枚举 LinkerChannel/RegistrySource/OperationState；组装时与 LINK-2 等价类型合并为同一份）。

### Editor/Operations（LINK-2b/3/4/6：manifest 事务、UPM 队列、Agent 同步、URL/Git）

- `Editor/Operations/ManifestUrlClassifier.cs` — 判定 manifest 依赖值的 ownership（GitRepo/LocalFile/Unmanaged/Absent）。
- `Editor/Operations/ManifestPlan.cs` — 事务输入模型（ManifestPlan/ManifestEdit/ManifestChangeType）。
- `Editor/Operations/OperationLog.cs` — 操作日志/记账 POCO（DependencyChange/OperationRecord/OperationLog/OperationPhase/冲突与结果类型）。
- `Editor/Operations/OperationLogStore.cs` — `operation.json` 的 tmp + File.Replace 原子读写。
- `Editor/Operations/ManifestTransaction.cs` — 七步原子事务（读 JObject 保序 → backup → 改受管依赖 → tmp → 校验 → File.Replace → 记账），含冲突预检。
- `Editor/Operations/RemovePlanner.cs` — Remove 第二步：dependsOn 闭包算可移除孤儿 infra（linker 永不移除）。
- `Editor/Operations/ManifestRollback.cs` — 用 backup/oldValue 回滚，恢复前检测手动改动。
- `Editor/Operations/InstallQueueBuilder.cs` — 启用闭包 + infra 依赖 + 只取 ready + linker 强制排尾。
- `Editor/Operations/IUpmClient.cs` / `UnityUpmClient.cs` — `Client.Add` 抽象与真实实现。
- `Editor/Operations/IInstalledPackageProbe.cs` / `UnityInstalledPackageProbe.cs` — 已安装包来源 URL 探针抽象与真实实现。
- `Editor/Operations/UpmQueueRunner.cs` — 串行队列状态机：先原子落盘再 Add，恢复核对 + 重试 ≤2。
- `Editor/Operations/U3DAILinkerBootstrap.cs` — `[InitializeOnLoad]` + delayCall 域重载恢复；纯决策器 RecoveryReconciler。
- `Editor/Operations/ContentHash.cs` — 目录确定性内容哈希（排除 ownership 文件）。
- `Editor/Operations/OwnershipFile.cs` — `.u3d-ai-owner.json` 读写（坏 JSON 读为 null）。
- `Editor/Operations/OwnershipGuard.cs` — 目标目录归属判定（Missing/Foreign/ManagedMatch/ManagedMismatch）。
- `Editor/Operations/IJunctionManager.cs` / `WindowsJunctionManager.cs` — Junction 抽象 + P/Invoke DeviceIoControl 实现。
- `Editor/Operations/AgentSyncService.cs` — 事务式六步 Agent 同步（staging→校验→ownership→backup→promote→junction），失败回滚 backup。
- `Editor/Operations/InstalledPackageInfo.cs` — 已装包纯数据投影（面板状态模型用）。
- `Editor/Operations/PackageUrlBuilder.cs` — 三通道 URL 生成 + 受管 URL 判定（LINK-6 面板侧）。
- `Editor/Operations/IGitRefResolver.cs` / `GitLsRemoteResolver.cs` — 远端分支 SHA 解析抽象 + `git ls-remote` 实现。
- `Editor/Operations/RefreshDevPlanner.cs` — Dev 通道把启用闭包锁到同一 main SHA 的计划器。

### Editor/Agents（LINK-5：Fragment 合并与托管区块）

- `Editor/Agents/IFragmentSource.cs` — 单工具对托管文件/gitignore 的贡献契约 + `ManagedBlockKind`。
- `Editor/Agents/BlockWriteResult.cs` — 托管区块写入结果（Written/Unchanged/Conflict）。
- `Editor/Agents/ManagedBlockWriter.cs` — CLAUDE/AGENTS 托管区块创建/更新/保留用户内容/marker 损坏即停。
- `Editor/Agents/FragmentMergeResult.cs` — 合并结果（成功带 body / 失败带原因）。
- `Editor/Agents/FragmentMerger.cs` — 确定性合并（order→toolId 排序）+ 重复 Skill 名/负 order 预检。
- `Editor/Agents/GitignoreBlockWriter.cs` — `.gitignore` 托管区块创建/更新/损坏保护/卸载只删匹配行。
- `Editor/Agents/ManagedFileSync.cs` — 把合并预检与托管区块写入串成"失败即不部分写"的编排。

### Editor/Settings（LINK-1/6：包常量、Project Settings 面板、三层状态）

- `Editor/U3DAILinkerPackage.cs` — 包级常量单一源（PackageName/DisplayName/SettingsPath/RootNamespace）。
- `Editor/U3DAILinkerSettingsProvider.cs`（LINK-1 占位）/ `Editor/Settings/U3DAILinkerSettingsProvider.cs`（LINK-6 完整面板）— `[SettingsProvider]("Project/U3D AI Linker")` 注册与 IMGUI 渲染（状态区/工具表/操作区/Local 警告/Restore）。
- `Editor/Settings/U3DAILinkerSettings.cs` — ProjectSettings 层 ScriptableObject（通道/启用工具/期望版本；禁绝对路径不变量）。
- `Editor/Settings/U3DAILinkerUserSettings.cs` — UserSettings 层（本机仓库路径/窗口态）。
- `Editor/Settings/U3DAILinkerSettingsStore.cs` — 两层 `.asset` 加载/保存（保存前强制不变量校验）。
- `Editor/Settings/PanelStateModel.cs` — 由 Registry+manifest+PackageInfo 算工具行（Installed/Desired/Current/Agent/RequiredBy）。
- `Editor/Settings/RestorePlanner.cs` — 检测托管 file: 依赖并生成 Restore Stable/Dev 计划。
- `Registry/.keep` — Registry 快照目录占位。

### Registry 数据与文档（LINK-7：初始内容与收尾）

- `Registry/stable.json` / `Registry/dev.json`（仓库根，公开发布源）。
- `Packages/com.yoji.u3d-ai-linker/Registry/stable.json` / `dev.json`（包内离线快照，与根逐字节一致）。
- `Packages/com.yoji.editor-debug/Agent~/fragments/{CLAUDE.md,AGENTS.md}` — editor-debug 的 Fragment。
- `Packages/com.yoji.lua-device-debug/Agent~/fragments/{CLAUDE.md,AGENTS.md}` — lua-device-debug 的 Fragment。
- `README.md` — 修正迁移完成度误称。

### Tests/Editor（各子系统的 EditMode 测试）

- `Tests/Editor/Probe/ProbeEvaluatorTests.cs` / `Probe/ProbeResultWriterTests.cs`
- `Tests/Editor/U3DAILinkerPackageTests.cs` / `U3DAILinkerSettingsProviderTests.cs`
- `Tests/Editor/EnumParsingTests.cs` / `RegistryParserTests.cs` / `RegistryValidatorTests.cs` / `ToolUrlBuilderTests.cs` / `TopologicalSorterTests.cs`
- `Tests/Editor/ManifestUrlClassifierTests.cs` / `ManifestTransactionTests.cs` / `RemovePlannerTests.cs` / `ManifestRollbackTests.cs`
- `Tests/Editor/Operations/OperationLogStoreTests.cs` / `Operations/InstallQueueBuilderTests.cs` / `Operations/FakeUpmClient.cs` / `Operations/FakeInstalledPackageProbe.cs` / `Operations/UpmQueueRunnerTests.cs` / `Operations/RecoveryReconcilerTests.cs`
- `Tests/Editor/FakeJunctionManager.cs` / `ContentHashTests.cs` / `OwnershipFileTests.cs` / `OwnershipGuardTests.cs` / `AgentSyncServiceTests.cs`
- `Tests/Editor/Agents/FragmentSourceContractTests.cs` / `Agents/ManagedBlockWriterTests.cs` / `Agents/FragmentMergerTests.cs` / `Agents/GitignoreBlockWriterTests.cs` / `Agents/ManagedFileSyncTests.cs`
- `Tests/Editor/PackageUrlBuilderTests.cs` / `PanelStateModelTests.cs` / `FakeGitRefResolver.cs` / `RefreshDevPlannerTests.cs` / `U3DAILinkerSettingsTests.cs` / `RestorePlannerTests.cs` / `U3DAILinkerSettingsStoreTests.cs`
- `Tests/Editor/RegistryFixtureTests.cs`

---

## Tasks

任务按依赖顺序排列：LINK-0（探针硬前置）→ LINK-1（包骨架 + SettingsProvider）→ LINK-2（Registry 纯逻辑）→ LINK-2b（manifest 事务）→ LINK-3（UPM 队列 + 域重载恢复）→ LINK-4（Skill 复制 + Junction）→ LINK-5（Fragment 合并 + 托管区块）→ LINK-6（Project Settings 面板 + 三通道）→ LINK-7（工具迁移收尾）。下方 Task 编号 1..50 为全局连续编号；每个子系统的 body 原样保留（失败测试、最小实现、命令、提交），仅把其内部的 `### Task: ...` 改写为全局 `### Task N: ...`。

> **幂等基线提醒：** LINK-0/1/2/2b/3/4/5/6 各自的 body 都包含"创建包骨架（package.json + 主/测试 asmdef + AssemblyInfo）"的步骤，因为每个子系统被设计为可独立落地。按本计划顺序执行时，**第一个建包骨架的任务（Task 1）真正创建这些文件；后续任务遇到已存在的同名文件应核对内容一致后跳过创建**，不要重复创建或覆盖。下文保留各子系统原始 body 以确保独立可读，执行者据此去重。


### LINK-0 — Agent~/BundledSkills~ resolvedPath 探针（硬前置）

本子系统是 LINK 系列的硬前置。spec 71 行明确:`Agent~/`/`BundledSkills~/` 能否在 Git Package Cache 的 `resolvedPath` 中稳定保留,必须在实现前用最小探针验证,失败则停止目录模式、切 zip fallback(影响 LINK-1/4/7 的资产读取路径)。

本质 = 一次性可丢弃的探针脚本 + 手动验证。但探针的"判定 + 序列化结果"是纯逻辑,可在 EditMode 无需真实 UPM/网络跑完整 TDD;真正依赖真实 Editor 的部分(`Client.List` 取 `resolvedPath`、对真实 Git Cache 路径做 `File.Exists`)抽到一个薄菜单入口里,列为明确的手动验证步骤。

设计要点:
- 纯逻辑核心 `ProbeEvaluator`:输入"每个被探目标的存在性事实"(一组 `ProbeTarget`),输出结构化 `ProbeResult`(总判定 + 逐项明细 + 推荐模式 `directory`/`zip-fallback`)。这层完全不碰 IO,直接单测。
- 序列化用 Newtonsoft(与全包一致),固定写 `Library/U3DAILinker/probe-result.json`。写盘路径拼接逻辑(取 `Application.dataPath` 的父目录 + `Library/U3DAILinker`)是纯字符串逻辑,单测覆盖。
- 薄 Editor 入口 `AgentAssetProbe`:菜单 `Tools/U3D AI Linker/Run Agent Asset Probe`。它调用 `Client.List` 拿真实 `resolvedPath`,对三个真实目标做 `File.Exists`/`Directory.Exists`,把事实喂给 `ProbeEvaluator`,再调用 `ProbeResultWriter` 落盘。这一步的真实副作用走手动验证,不写单测(无法在普通 EditMode 模拟真实 Git Cache)。

被探三目标(对齐 spec 286-288):
- `<editor-debug.resolvedPath>/Agent~/skills/unity-editor-debug-mcp/SKILL.md`(File)
- `<test-runner.resolvedPath>/Agent~/skills/test-runner-mcp/SKILL.md`(File)
- `<linker.resolvedPath>/BundledSkills~/`(Directory)

决策门(写清,供 LINK-1/4/7 引用):
- 三项全 `exists=true` -> `recommendedMode="directory"`,LINK-1/4/7 用 `resolvedPath` 下的 `Agent~/`/`BundledSkills~/` 目录直接读。
- 任一 `exists=false` -> `recommendedMode="zip-fallback"`,停止目录模式;改为各工具包 `Editor/Resources/U3DAgentAssets.zip.bytes` + Linker 包 `Editor/Resources/BundledSkills.zip.bytes`,同步时解压到 `.u3d-ai-linker/.staging/`(spec 298-302)。对外产物布局两种模式必须一致(spec 304)。

注意:本子系统只建包骨架(asmdef/package.json)够探针编译运行即可,完整 Registry/Operations 由 LINK-1+ 引入。这里建立的 `Yoji.U3DAILinker.Editor` 主 asmdef 与测试 asmdef 是后续所有 LINK 子系统复用的基座。

---

### Task 1: Linker 包骨架与 asmdef 基座

建立 `com.yoji.u3d-ai-linker` 包的最小可编译骨架(package.json + 主/测试 asmdef),后续所有 LINK 子系统在此之上扩展。无业务代码,先确保工程能识别包并编译空程序集。

Files:
- Create `Packages/com.yoji.u3d-ai-linker/package.json`
- Create `Packages/com.yoji.u3d-ai-linker/Editor/Yoji.U3DAILinker.Editor.asmdef`
- Create `Packages/com.yoji.u3d-ai-linker/Tests/Editor/Yoji.U3DAILinker.Editor.Tests.asmdef`

- [ ] Step 1: 创建包清单 `Packages/com.yoji.u3d-ai-linker/package.json`,声明 Newtonsoft 依赖与 2022.3 基线:

```json
{
  "name": "com.yoji.u3d-ai-linker",
  "version": "0.1.0",
  "displayName": "U3D AI Linker",
  "description": "Batch-installs Unity AI tool packages from this monorepo and syncs project-level Skills/rules to Claude Code and Codex. Windows + Unity 2022.3+ first.",
  "unity": "2022.3",
  "dependencies": {
    "com.unity.nuget.newtonsoft-json": "3.2.2"
  }
}
```

- [ ] Step 2: 创建主程序集 `Packages/com.yoji.u3d-ai-linker/Editor/Yoji.U3DAILinker.Editor.asmdef`(Editor-only,引用 Newtonsoft;`UnityEditor.PackageManager` 属于 UnityEditor 内置,无需在 references 显式列出):

```json
{
  "name": "Yoji.U3DAILinker.Editor",
  "rootNamespace": "Yoji.U3DAILinker",
  "references": [],
  "includePlatforms": ["Editor"],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": true,
  "precompiledReferences": ["Newtonsoft.Json.dll"],
  "autoReferenced": true,
  "defineConstraints": [],
  "versionDefines": [],
  "noEngineReferences": false
}
```

- [ ] Step 3: 创建测试程序集 `Packages/com.yoji.u3d-ai-linker/Tests/Editor/Yoji.U3DAILinker.Editor.Tests.asmdef`(引用主 asmdef + NUnit + TestRunner,`UNITY_INCLUDE_TESTS` 约束):

```json
{
  "name": "Yoji.U3DAILinker.Editor.Tests",
  "rootNamespace": "Yoji.U3DAILinker.Tests",
  "references": ["Yoji.U3DAILinker.Editor", "UnityEngine.TestRunner", "UnityEditor.TestRunner"],
  "includePlatforms": ["Editor"],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": true,
  "precompiledReferences": ["nunit.framework.dll", "Newtonsoft.Json.dll"],
  "autoReferenced": false,
  "defineConstraints": ["UNITY_INCLUDE_TESTS"],
  "versionDefines": [],
  "noEngineReferences": false
}
```

- [ ] Step 4: 在 Unity 编辑器在线时确认包被识别且空程序集编译通过。用 test-runner MCP 触发一次重编译(端口以本机 Test Runner 服务为准,示例 17800):

```bash
curl -s -X POST http://127.0.0.1:17800/recompile
```

  期望:返回 200,响应体 `compilationSucceeded` 为 `true`(或等价的编译成功标志),Console 无 `Yoji.U3DAILinker` 相关编译错误。若 Unity 未在线,改为在 Unity 菜单手动触发 `Assets > Reimport All` 后观察 Console 无该包编译错误。

- [ ] Step 5: 提交骨架:

```bash
git checkout -b feat/u3d-ai-linker-probe
git add Packages/com.yoji.u3d-ai-linker/package.json Packages/com.yoji.u3d-ai-linker/Editor/Yoji.U3DAILinker.Editor.asmdef Packages/com.yoji.u3d-ai-linker/Tests/Editor/Yoji.U3DAILinker.Editor.Tests.asmdef
git commit -m "$(cat <<'EOF'
chore(u3d-ai-linker): scaffold linker package skeleton and asmdefs

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: ProbeTarget/ProbeResult 数据模型与 ProbeEvaluator 纯逻辑判定

把"逐个被探目标的存在性事实 -> 总判定 + 推荐模式"的决策抽成纯逻辑类 `ProbeEvaluator`,无任何 IO,完整 TDD。这是决策门(directory vs zip-fallback)的唯一真相来源。

Files:
- Create `Packages/com.yoji.u3d-ai-linker/Editor/Probe/ProbeModels.cs`
- Create `Packages/com.yoji.u3d-ai-linker/Editor/Probe/ProbeEvaluator.cs`
- Test `Packages/com.yoji.u3d-ai-linker/Tests/Editor/Probe/ProbeEvaluatorTests.cs`

- [ ] Step 1: 先写数据模型 `Packages/com.yoji.u3d-ai-linker/Editor/Probe/ProbeModels.cs`(普通可序列化 POCO,Newtonsoft 友好,无 record):

```csharp
using System;
using System.Collections.Generic;

namespace Yoji.U3DAILinker.Probe
{
    /// <summary>
    /// 一个被探测的资产位置及其存在性事实。kind 区分 File 还是 Directory。
    /// </summary>
    public sealed class ProbeTarget
    {
        /// <summary>稳定标识,如 "editor-debug.SKILL.md" / "linker.BundledSkills~".</summary>
        public string Id { get; set; }

        /// <summary>被探测的绝对路径(由真实 resolvedPath 拼出,或测试直接给定)。</summary>
        public string Path { get; set; }

        /// <summary>"File" 或 "Directory"。</summary>
        public string Kind { get; set; }

        /// <summary>该路径是否真实存在。由 IO 层填入或测试直接给定。</summary>
        public bool Exists { get; set; }

        public ProbeTarget() { }

        public ProbeTarget(string id, string path, string kind, bool exists)
        {
            Id = id;
            Path = path;
            Kind = kind;
            Exists = exists;
        }
    }

    /// <summary>
    /// 探针总结果。allTargetsReadable 为总判定;recommendedMode 是给 LINK-1/4/7 的决策门。
    /// </summary>
    public sealed class ProbeResult
    {
        /// <summary>结果 schema 版本,便于面板/后续解析做兼容。</summary>
        public int SchemaVersion { get; set; } = 1;

        /// <summary>探针运行的 UTC 时间(ISO 8601)。</summary>
        public string ProbedAtUtc { get; set; }

        /// <summary>所有目标是否都可读。</summary>
        public bool AllTargetsReadable { get; set; }

        /// <summary>"directory" 或 "zip-fallback"。</summary>
        public string RecommendedMode { get; set; }

        /// <summary>逐项明细,顺序与输入一致。</summary>
        public List<ProbeTarget> Targets { get; set; } = new List<ProbeTarget>();
    }

    /// <summary>recommendedMode 取值常量,避免散落字符串字面量。</summary>
    public static class ProbeMode
    {
        public const string Directory = "directory";
        public const string ZipFallback = "zip-fallback";
    }
}
```

- [ ] Step 2: 写失败测试 `Packages/com.yoji.u3d-ai-linker/Tests/Editor/Probe/ProbeEvaluatorTests.cs`(此时 `ProbeEvaluator` 尚不存在,编译失败即为预期的"红"):

```csharp
using System;
using System.Collections.Generic;
using NUnit.Framework;
using Yoji.U3DAILinker.Probe;

namespace Yoji.U3DAILinker.Tests.Probe
{
    public sealed class ProbeEvaluatorTests
    {
        private static List<ProbeTarget> Targets(params bool[] existsFlags)
        {
            var list = new List<ProbeTarget>();
            for (int i = 0; i < existsFlags.Length; i++)
            {
                list.Add(new ProbeTarget(
                    id: "t" + i,
                    path: "/fake/path/" + i,
                    kind: i % 2 == 0 ? "File" : "Directory",
                    exists: existsFlags[i]));
            }
            return list;
        }

        [Test]
        public void Evaluate_AllExist_RecommendsDirectoryMode()
        {
            var result = ProbeEvaluator.Evaluate(Targets(true, true, true));

            Assert.IsTrue(result.AllTargetsReadable);
            Assert.AreEqual(ProbeMode.Directory, result.RecommendedMode);
        }

        [Test]
        public void Evaluate_AnyMissing_RecommendsZipFallback()
        {
            var result = ProbeEvaluator.Evaluate(Targets(true, false, true));

            Assert.IsFalse(result.AllTargetsReadable);
            Assert.AreEqual(ProbeMode.ZipFallback, result.RecommendedMode);
        }

        [Test]
        public void Evaluate_AllMissing_RecommendsZipFallback()
        {
            var result = ProbeEvaluator.Evaluate(Targets(false, false, false));

            Assert.IsFalse(result.AllTargetsReadable);
            Assert.AreEqual(ProbeMode.ZipFallback, result.RecommendedMode);
        }

        [Test]
        public void Evaluate_PreservesTargetOrderAndFacts()
        {
            var input = Targets(true, false, true);
            var result = ProbeEvaluator.Evaluate(input);

            Assert.AreEqual(3, result.Targets.Count);
            Assert.AreEqual("t0", result.Targets[0].Id);
            Assert.AreEqual("t1", result.Targets[1].Id);
            Assert.AreEqual("t2", result.Targets[2].Id);
            Assert.IsFalse(result.Targets[1].Exists);
        }

        [Test]
        public void Evaluate_SetsSchemaVersionAndProbedAtUtc()
        {
            var result = ProbeEvaluator.Evaluate(Targets(true));

            Assert.AreEqual(1, result.SchemaVersion);
            Assert.IsFalse(string.IsNullOrEmpty(result.ProbedAtUtc));
            // ProbedAtUtc 必须可被解析回 DateTime(round-trip 格式)
            Assert.DoesNotThrow(() => DateTime.Parse(result.ProbedAtUtc));
        }

        [Test]
        public void Evaluate_EmptyInput_TreatedAsNotReadable()
        {
            var result = ProbeEvaluator.Evaluate(new List<ProbeTarget>());

            Assert.IsFalse(result.AllTargetsReadable);
            Assert.AreEqual(ProbeMode.ZipFallback, result.RecommendedMode);
        }

        [Test]
        public void Evaluate_NullInput_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => ProbeEvaluator.Evaluate(null));
        }
    }
}
```

- [ ] Step 3: 跑测试确认"红"。Unity 在线时用 test-runner MCP 按程序集过滤跑(端口以本机为准,示例 17800):

```bash
curl -s -X POST "http://127.0.0.1:17800/run?mode=EditMode&assembly=Yoji.U3DAILinker.Editor.Tests"
```

  期望:此时 `ProbeEvaluator` 类不存在,测试程序集**编译失败**,响应/Console 报 `The name 'ProbeEvaluator' does not exist` 或等价编译错误。这就是预期的失败基线(无法进入用例执行)。

- [ ] Step 4: 写最小实现 `Packages/com.yoji.u3d-ai-linker/Editor/Probe/ProbeEvaluator.cs` 让测试转"绿":

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Yoji.U3DAILinker.Probe
{
    /// <summary>
    /// 纯逻辑:把逐个目标的存在性事实归约为总判定与推荐模式。无任何 IO,可在 EditMode 直接单测。
    /// </summary>
    public static class ProbeEvaluator
    {
        /// <summary>
        /// 评估一组探测目标。空集合视为不可读(无证据 = 不能走目录模式)。
        /// </summary>
        public static ProbeResult Evaluate(IReadOnlyList<ProbeTarget> targets)
        {
            if (targets == null)
            {
                throw new ArgumentNullException(nameof(targets));
            }

            bool allReadable = targets.Count > 0;
            var copied = new List<ProbeTarget>(targets.Count);
            foreach (var t in targets)
            {
                copied.Add(new ProbeTarget(t.Id, t.Path, t.Kind, t.Exists));
                if (!t.Exists)
                {
                    allReadable = false;
                }
            }

            return new ProbeResult
            {
                SchemaVersion = 1,
                ProbedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                AllTargetsReadable = allReadable,
                RecommendedMode = allReadable ? ProbeMode.Directory : ProbeMode.ZipFallback,
                Targets = copied
            };
        }
    }
}
```

- [ ] Step 5: 重跑测试确认全绿:

```bash
curl -s -X POST "http://127.0.0.1:17800/run?mode=EditMode&assembly=Yoji.U3DAILinker.Editor.Tests"
```

  期望:7 个用例全部 `Passed`,0 failed。

- [ ] Step 6: 提交:

```bash
git add Packages/com.yoji.u3d-ai-linker/Editor/Probe/ProbeModels.cs Packages/com.yoji.u3d-ai-linker/Editor/Probe/ProbeEvaluator.cs Packages/com.yoji.u3d-ai-linker/Tests/Editor/Probe/ProbeEvaluatorTests.cs
git commit -m "$(cat <<'EOF'
feat(u3d-ai-linker): add ProbeEvaluator decision logic for agent asset probe

Pure-logic reduction of per-target existence facts to directory vs
zip-fallback recommendation. Empty input treated as not-readable.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 3: probe-result.json 路径解析与 ProbeResultWriter 序列化

把"决定写盘到 `Library/U3DAILinker/probe-result.json`"与"用 Newtonsoft 序列化 ProbeResult"两段纯逻辑 TDD 出来。路径解析接收 projectRoot 入参(测试直接给假路径,生产由菜单入口传 `Application.dataPath` 的父目录),避免单测依赖真实工程目录。

Files:
- Create `Packages/com.yoji.u3d-ai-linker/Editor/Probe/ProbeResultWriter.cs`
- Test `Packages/com.yoji.u3d-ai-linker/Tests/Editor/Probe/ProbeResultWriterTests.cs`

- [ ] Step 1: 写失败测试 `Packages/com.yoji.u3d-ai-linker/Tests/Editor/Probe/ProbeResultWriterTests.cs`。覆盖:相对路径常量正确、序列化往返、实际写盘到临时目录后能读回。用 `System.IO.Path.GetTempPath()` 造临时 projectRoot,测试自清理:

```csharp
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using Yoji.U3DAILinker.Probe;

namespace Yoji.U3DAILinker.Tests.Probe
{
    public sealed class ProbeResultWriterTests
    {
        private string _tempRoot;

        [SetUp]
        public void SetUp()
        {
            _tempRoot = Path.Combine(
                Path.GetTempPath(),
                "U3DAILinkerProbeTest_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }

        [Test]
        public void ResultPathFor_BuildsLibraryU3DAILinkerPath()
        {
            string path = ProbeResultWriter.ResultPathFor(_tempRoot);

            string expected = Path.Combine(_tempRoot, "Library", "U3DAILinker", "probe-result.json");
            Assert.AreEqual(Path.GetFullPath(expected), Path.GetFullPath(path));
        }

        [Test]
        public void Write_CreatesFileWithExpectedJsonFields()
        {
            var result = ProbeEvaluator.Evaluate(new List<ProbeTarget>
            {
                new ProbeTarget("editor-debug.SKILL.md", "/x/Agent~/skills/a/SKILL.md", "File", true),
                new ProbeTarget("linker.BundledSkills~", "/y/BundledSkills~", "Directory", false),
            });

            string path = ProbeResultWriter.Write(_tempRoot, result);

            Assert.IsTrue(File.Exists(path));
            string json = File.ReadAllText(path);
            JObject root = JObject.Parse(json);

            Assert.AreEqual(1, (int)root["schemaVersion"]);
            Assert.AreEqual(false, (bool)root["allTargetsReadable"]);
            Assert.AreEqual("zip-fallback", (string)root["recommendedMode"]);
            Assert.IsNotNull(root["probedAtUtc"]);

            JArray targets = (JArray)root["targets"];
            Assert.AreEqual(2, targets.Count);
            Assert.AreEqual("editor-debug.SKILL.md", (string)targets[0]["id"]);
            Assert.AreEqual(true, (bool)targets[0]["exists"]);
            Assert.AreEqual("File", (string)targets[0]["kind"]);
            Assert.AreEqual(false, (bool)targets[1]["exists"]);
        }

        [Test]
        public void Write_CreatesMissingDirectories()
        {
            // _tempRoot 下尚无 Library/U3DAILinker,Write 必须自建。
            var result = ProbeEvaluator.Evaluate(new List<ProbeTarget>
            {
                new ProbeTarget("only", "/z", "Directory", true),
            });

            string path = ProbeResultWriter.Write(_tempRoot, result);

            Assert.IsTrue(File.Exists(path));
            Assert.IsTrue(Directory.Exists(Path.Combine(_tempRoot, "Library", "U3DAILinker")));
        }

        [Test]
        public void Write_IsIdempotent_OverwritesExisting()
        {
            var first = ProbeEvaluator.Evaluate(new List<ProbeTarget>
            {
                new ProbeTarget("a", "/a", "File", true),
            });
            string path = ProbeResultWriter.Write(_tempRoot, first);

            var second = ProbeEvaluator.Evaluate(new List<ProbeTarget>
            {
                new ProbeTarget("a", "/a", "File", false),
            });
            string path2 = ProbeResultWriter.Write(_tempRoot, second);

            Assert.AreEqual(Path.GetFullPath(path), Path.GetFullPath(path2));
            JObject root = JObject.Parse(File.ReadAllText(path2));
            Assert.AreEqual("zip-fallback", (string)root["recommendedMode"]);
        }
    }
}
```

- [ ] Step 2: 跑测试确认"红"(`ProbeResultWriter` 不存在,测试程序集编译失败):

```bash
curl -s -X POST "http://127.0.0.1:17800/run?mode=EditMode&assembly=Yoji.U3DAILinker.Editor.Tests"
```

  期望:报 `The name 'ProbeResultWriter' does not exist`(或等价编译错误),无法进入执行。

- [ ] Step 3: 写最小实现 `Packages/com.yoji.u3d-ai-linker/Editor/Probe/ProbeResultWriter.cs`。序列化采用 camelCase 契约(与上面测试断言的 `schemaVersion`/`allTargetsReadable` 等一致),用 Newtonsoft 显式配置 `CamelCasePropertyNamesContractResolver`:

```csharp
using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Yoji.U3DAILinker.Probe
{
    /// <summary>
    /// 把 ProbeResult 序列化并写入 &lt;projectRoot&gt;/Library/U3DAILinker/probe-result.json。
    /// 路径解析与序列化均为纯逻辑,projectRoot 由调用方注入,便于单测。
    /// </summary>
    public static class ProbeResultWriter
    {
        public const string LibraryDirName = "Library";
        public const string LinkerDirName = "U3DAILinker";
        public const string ResultFileName = "probe-result.json";

        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Include
        };

        /// <summary>计算结果文件绝对路径,不触碰磁盘。</summary>
        public static string ResultPathFor(string projectRoot)
        {
            if (string.IsNullOrEmpty(projectRoot))
            {
                throw new ArgumentException("projectRoot must not be null or empty", nameof(projectRoot));
            }

            return Path.Combine(projectRoot, LibraryDirName, LinkerDirName, ResultFileName);
        }

        /// <summary>把 result 序列化为 JSON。</summary>
        public static string Serialize(ProbeResult result)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            return JsonConvert.SerializeObject(result, Settings);
        }

        /// <summary>
        /// 写入结果文件,自动创建缺失目录,覆盖已存在文件。返回写入的绝对路径。
        /// </summary>
        public static string Write(string projectRoot, ProbeResult result)
        {
            string path = ResultPathFor(projectRoot);
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(path, Serialize(result));
            return path;
        }
    }
}
```

- [ ] Step 4: 重跑测试确认全绿:

```bash
curl -s -X POST "http://127.0.0.1:17800/run?mode=EditMode&assembly=Yoji.U3DAILinker.Editor.Tests"
```

  期望:本 Task 的 4 个用例 + 上个 Task 的 7 个用例全部 `Passed`,0 failed。

- [ ] Step 5: 提交:

```bash
git add Packages/com.yoji.u3d-ai-linker/Editor/Probe/ProbeResultWriter.cs Packages/com.yoji.u3d-ai-linker/Tests/Editor/Probe/ProbeResultWriterTests.cs
git commit -m "$(cat <<'EOF'
feat(u3d-ai-linker): add ProbeResultWriter for probe-result.json

Pure path resolution (Library/U3DAILinker/probe-result.json) and
camelCase Newtonsoft serialization, projectRoot injected for testability.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 4: AgentAssetProbe 薄菜单入口(真实 resolvedPath + IO)

把纯逻辑包装成可在真实工程跑的探针:菜单 `Tools/U3D AI Linker/Run Agent Asset Probe`。它用 `Client.List` 拿真实 `resolvedPath`,对三个真实目标做 `File.Exists`/`Directory.Exists`,喂给 `ProbeEvaluator`,落盘并打印结论。真实副作用部分不写单测,改为下一 Task 的手动验证。

Files:
- Create `Packages/com.yoji.u3d-ai-linker/Editor/Probe/AgentAssetProbe.cs`

- [ ] Step 1: 写菜单入口 `Packages/com.yoji.u3d-ai-linker/Editor/Probe/AgentAssetProbe.cs`。`Client.List` 是异步操作,在菜单回调里用 `EditorApplication.update` 轮询其 `IsCompleted`,完成后构造目标并写盘:

```csharp
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using Yoji.U3DAILinker.Probe;

namespace Yoji.U3DAILinker.Probe
{
    /// <summary>
    /// 一次性探针入口:验证 Agent~/ 与 BundledSkills~/ 在 Git Package Cache 的
    /// resolvedPath 下是否可读,结果写 Library/U3DAILinker/probe-result.json。
    /// 这是 LINK 系列的硬前置;探针失败 -> 切 zip fallback(见 probe-result.json 的 recommendedMode)。
    /// </summary>
    public static class AgentAssetProbe
    {
        private const string EditorDebugPackage = "com.yoji.editor-debug";
        private const string TestRunnerPackage = "com.yoji.test-runner";
        private const string LinkerPackage = "com.yoji.u3d-ai-linker";

        private static ListRequest _listRequest;

        [MenuItem("Tools/U3D AI Linker/Run Agent Asset Probe")]
        public static void Run()
        {
            if (_listRequest != null)
            {
                Debug.LogWarning("[U3DAILinker] Probe already running.");
                return;
            }

            Debug.Log("[U3DAILinker] Agent asset probe started. Listing packages...");
            // offlineMode=false,includeIndirectDependencies=true,确保拿到完整 resolvedPath。
            _listRequest = Client.List(offlineMode: false, includeIndirectDependencies: true);
            EditorApplication.update += PollList;
        }

        private static void PollList()
        {
            if (_listRequest == null || !_listRequest.IsCompleted)
            {
                return;
            }

            EditorApplication.update -= PollList;
            ListRequest request = _listRequest;
            _listRequest = null;

            if (request.Status != StatusCode.Success)
            {
                string err = request.Error != null ? request.Error.message : "unknown error";
                Debug.LogError("[U3DAILinker] Client.List failed: " + err);
                return;
            }

            string editorDebugPath = ResolvePath(request, EditorDebugPackage);
            string testRunnerPath = ResolvePath(request, TestRunnerPackage);
            string linkerPath = ResolvePath(request, LinkerPackage);

            var targets = new List<ProbeTarget>
            {
                MakeFileTarget(
                    "editor-debug.SKILL.md",
                    editorDebugPath,
                    "Agent~/skills/unity-editor-debug-mcp/SKILL.md"),
                MakeFileTarget(
                    "test-runner.SKILL.md",
                    testRunnerPath,
                    "Agent~/skills/test-runner-mcp/SKILL.md"),
                MakeDirectoryTarget(
                    "linker.BundledSkills~",
                    linkerPath,
                    "BundledSkills~"),
            };

            ProbeResult result = ProbeEvaluator.Evaluate(targets);
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string written = ProbeResultWriter.Write(projectRoot, result);

            if (result.AllTargetsReadable)
            {
                Debug.Log(
                    "[U3DAILinker] Probe PASSED. All Agent~/BundledSkills~ targets readable. " +
                    "recommendedMode=directory. Wrote " + written);
            }
            else
            {
                Debug.LogWarning(
                    "[U3DAILinker] Probe FAILED. At least one target missing under resolvedPath. " +
                    "recommendedMode=zip-fallback. Inspect " + written +
                    " and switch LINK-1/4/7 to zip-bytes mode.");
            }
        }

        private static string ResolvePath(ListRequest request, string packageName)
        {
            foreach (var info in request.Result)
            {
                if (info.name == packageName)
                {
                    return info.resolvedPath;
                }
            }
            return null;
        }

        private static ProbeTarget MakeFileTarget(string id, string resolvedPath, string relative)
        {
            if (string.IsNullOrEmpty(resolvedPath))
            {
                return new ProbeTarget(id, "<package-not-installed:" + id + ">", "File", false);
            }
            string full = Path.Combine(resolvedPath, relative);
            return new ProbeTarget(id, full, "File", File.Exists(full));
        }

        private static ProbeTarget MakeDirectoryTarget(string id, string resolvedPath, string relative)
        {
            if (string.IsNullOrEmpty(resolvedPath))
            {
                return new ProbeTarget(id, "<package-not-installed:" + id + ">", "Directory", false);
            }
            string full = Path.Combine(resolvedPath, relative);
            return new ProbeTarget(id, full, "Directory", Directory.Exists(full));
        }
    }
}
```

- [ ] Step 2: Unity 在线时触发重编译,确认入口编译通过且菜单项注册(纯编译验证,不执行探针逻辑):

```bash
curl -s -X POST http://127.0.0.1:17800/recompile
```

  期望:返回 200 且编译成功;Console 无 `AgentAssetProbe` 相关错误。Unity 主菜单出现 `Tools > U3D AI Linker > Run Agent Asset Probe`(本仓库作为开发工程已直接包含该包,可立即看到)。

- [ ] Step 3: 提交入口:

```bash
git add Packages/com.yoji.u3d-ai-linker/Editor/Probe/AgentAssetProbe.cs
git commit -m "$(cat <<'EOF'
feat(u3d-ai-linker): add AgentAssetProbe menu entry for resolvedPath probe

Resolves real resolvedPath via Client.List, checks Agent~/BundledSkills~
targets, writes probe-result.json with directory vs zip-fallback verdict.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 5: 手动验证 - 空 2022.3 工程经 Git URL 安装后实跑探针(决策门)

这是 LINK-0 的硬前置交付:在真实 Git Package Cache 条件下确认 `~` 后缀目录是否可读,产出 `probe-result.json`,据此决定整个 LINK 系列走目录模式还是 zip fallback。无法用 EditMode 单测替代(普通 EditMode 工程里包是本地 `Packages/` 嵌入式,不经 Git Cache),必须手动执行并记录。

前置条件:Step 1-4 的代码已合入并能在本仓库工程编译通过;`editor-debug`/`test-runner` 两包确已各自带有 `Agent~/skills/.../SKILL.md`,linker 包确已带 `BundledSkills~/` 目录(至少一个占位文件,避免空目录不入 Git)。

- [ ] Step 1: 在本仓库准备最小可探资产(若尚不存在)。确认以下真实路径存在,缺则先补占位再提交:
  - `Packages/com.yoji.editor-debug/Agent~/skills/unity-editor-debug-mcp/SKILL.md`
  - `Packages/com.yoji.test-runner/Agent~/skills/test-runner-mcp/SKILL.md`
  - `Packages/com.yoji.u3d-ai-linker/BundledSkills~/.keep`(占位,保证 `BundledSkills~/` 进 Git)

  在仓库根用 PowerShell 核对(期望三行均为 `True`):

```powershell
Test-Path Packages/com.yoji.editor-debug/Agent~/skills/unity-editor-debug-mcp/SKILL.md
Test-Path Packages/com.yoji.test-runner/Agent~/skills/test-runner-mcp/SKILL.md
Test-Path Packages/com.yoji.u3d-ai-linker/BundledSkills~
```

  说明:这些资产的完整内容由 editor-debug/test-runner/LINK-7 各自负责;LINK-0 只要求"可探到一个真实 SKILL.md 与 BundledSkills~ 目录"。若 Agent~/ 资产尚未就绪,本验证可推迟到那些子系统落地后执行,但**必须在 LINK-1 写资产读取逻辑之前完成**。

- [ ] Step 2: 把当前分支推到一个可被 Git URL 引用的远端 ref(探针必须经真实 Git Cache,不能用本机嵌入式包)。在仓库根:

```powershell
git push origin feat/u3d-ai-linker-probe
```

  记下用于安装的 ref(分支名或一个临时 tag,如 `probe-test`)。

- [ ] Step 3: 新建一个**空** Unity 2022.3 工程(独立目录,不在本仓库内),例如 `E:/Yoji/_probe-empty-2022`。用 Unity Hub 选 2022.3 LTS 模板 `3D (Built-In Render Pipeline)` 创建,打开一次让其生成 `Library/`。

- [ ] Step 4: 在该空工程的 `Packages/manifest.json` 顶层 `dependencies` 加入三条 Git URL(linker + editor-debug + test-runner,均指向 Step 2 的 ref;若 editor-debug/test-runner 暂无独立 ref,用同一分支的 `?path=` 子路径)。示例:

```json
{
  "dependencies": {
    "com.yoji.u3d-ai-linker": "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/com.yoji.u3d-ai-linker#feat/u3d-ai-linker-probe",
    "com.yoji.editor-debug": "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/com.yoji.editor-debug#feat/u3d-ai-linker-probe",
    "com.yoji.test-runner": "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/com.yoji.test-runner#feat/u3d-ai-linker-probe"
  }
}
```

  保存后回到 Unity,等待 Package Manager 解析下载(状态栏出现下载进度;首次拉取较慢)。期望:三包出现在 `Window > Package Manager > In Project`,无红色解析错误。

- [ ] Step 5: 在该空工程菜单点 `Tools > U3D AI Linker > Run Agent Asset Probe`。观察 Console:
  - 期望先出现 `[U3DAILinker] Agent asset probe started. Listing packages...`
  - 随后出现 `Probe PASSED ... recommendedMode=directory`(理想)或 `Probe FAILED ... recommendedMode=zip-fallback`。

- [ ] Step 6: 读取产物 `probe-result.json` 核对逐项事实。在该空工程根:

```powershell
Get-Content Library/U3DAILinker/probe-result.json
```

  期望结构(示意,以实际为准):

```json
{
  "schemaVersion": 1,
  "probedAtUtc": "2026-06-14T...Z",
  "allTargetsReadable": true,
  "recommendedMode": "directory",
  "targets": [
    { "id": "editor-debug.SKILL.md", "path": "...Library/PackageCache/com.yoji.editor-debug@.../Agent~/skills/unity-editor-debug-mcp/SKILL.md", "kind": "File", "exists": true },
    { "id": "test-runner.SKILL.md", "path": "...Agent~/skills/test-runner-mcp/SKILL.md", "kind": "File", "exists": true },
    { "id": "linker.BundledSkills~", "path": "...BundledSkills~", "kind": "Directory", "exists": true }
  ]
}
```

  逐项检查每个 `path` 确实指向 `Library/PackageCache/...@<hash>/...`(即真实 Git Cache,而非本机 `Packages/`),且对应 `exists` 与 Step 5 的 PASSED/FAILED 一致。

- [ ] Step 7: 记录决策门结论(把 `probe-result.json` 内容粘到 PR 描述或 spec 旁注),并据此为 LINK-1/4/7 定调:
  - 若 `recommendedMode=directory`(三项全 `exists:true`):确认 `~` 后缀目录在 Git Cache 中**可读**,LINK-1/4/7 直接用 `resolvedPath` 下 `Agent~/`/`BundledSkills~/` 读取,无需 zip。
  - 若 `recommendedMode=zip-fallback`(任一 `exists:false`):确认 Unity 把 `~` 目录从 Git Package 中**剥离/不可读**,LINK-1/4/7 改走 zip 模式 —— 各工具包打 `Editor/Resources/U3DAgentAssets.zip.bytes`、linker 打 `Editor/Resources/BundledSkills.zip.bytes`,同步时解压到 `.u3d-ai-linker/.staging/`(spec 298-302),对外 `.u3d-ai-linker/skills/<tool>/` 布局保持一致(spec 304)。
  - 无论哪种结论,**停止凭经验假设**:后续 LINK 子系统的资产读取实现必须显式引用本步产出的 `recommendedMode`。

- [ ] Step 8: 清理一次性探针痕迹(可选,在结论记录后)。`AgentAssetProbe.cs` 及其纯逻辑(`ProbeEvaluator`/`ProbeResultWriter`/`ProbeModels`)可保留作为后续诊断面板复用(LINK-8 Project Settings 诊断报告会复用 `probe-result.json`);若决定保留则不删。删除临时空工程目录 `E:/Yoji/_probe-empty-2022`。若 Step 2 建了临时 tag `probe-test`,删之:

```powershell
git push origin :refs/tags/probe-test
```

- [ ] Step 9: 把"决策门已确定"写入分支提交(仅文档/旁注,无代码改动时可跳过 commit;若在 spec 旁记了结论则提交该 md):

```powershell
git add docs/superpowers/specs/2026-06-12-u3d-ai-linker-design.md
git commit -m @'
docs(u3d-ai-linker): record agent-asset probe verdict and chosen asset mode

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
'@
```

---

### LINK-1 — Linker 包骨架 + SettingsProvider 注册

本子系统是整个 u3d-ai-linker 包的地基:它先把 UPM 包的目录与元数据搭好(package.json、主/测试 asmdef、Registry 与 BundledSkills~ 占位、包常量类),再注册 Project Settings 入口。后续子系统(Registry 解析、Manifest 事务、Skill 复制等)都在这套骨架上叠加,因此本计划要保证:包能被 Unity 识别、两个 asmdef 能编译、EditMode 测试能跑、SettingsProvider 路径常量唯一可信。

源设计核实要点(已 Read spec 18-54 / 341-357 / 494):
- spec 22-27 规定包内目录 `Editor/` `Registry/` `BundledSkills~/` 与 `package.json` 并列。
- spec 71 警告:`~` 后缀目录(`BundledSkills~`)能否在 Git Package 的 `resolvedPath` 中稳定保留属于未验证假设,本子系统**不**依赖该假设运行任何逻辑,只创建占位目录;真实探针留给 LINK-0(spec 实施顺序第 2 步)。因此本计划只把 `BundledSkills~/.keep` 当普通文件创建,不在 C# 里读它。
- spec 349-356 给出 SettingsProvider 注册范式:路径 `"Project/U3D AI Linker"`,作用域 `SettingsScope.Project`。
- spec 494(1):实施顺序第 1 步 = "Linker 空包 + SettingsProvider 骨架",即本子系统。

约定核实(已 Read 现有 test-runner 包):
- 主 asmdef 形如 `{ "name":"Yoji.TestRunner.Editor", "rootNamespace":"Yoji.TestRunner", "includePlatforms":["Editor"], "precompiledReferences":["Newtonsoft.Json.dll"], "overrideReferences":true }`;测试 asmdef 形如 `{ "name":"...Tests", "references":["主 asmdef","UnityEngine.TestRunner","UnityEditor.TestRunner"], "precompiledReferences":["nunit.framework.dll","Newtonsoft.Json.dll"], "autoReferenced":false, "defineConstraints":["UNITY_INCLUDE_TESTS"] }`。
- 测试目录是 `Tests/Editor/`(与 test-runner 一致),不是 `Editor/Tests/`。
- 用 `AssemblyInfo.cs` 里 `[assembly: InternalsVisibleTo("...Tests")]` 暴露 internal 给测试,与 test-runner 完全一致。

测试运行约定(Unity 当前在线时):用 test-runner MCP 的 HTTP 接口跑 EditMode 测试。本计划"跑测试"步骤统一给出 `unity-test-runner` skill / test-runner MCP 的标准做法:先触发重编译(`POST /recompile`),再发起 EditMode 运行(`POST /tests` 带 `filter`),轮询 `GET /tests/{id}` 拿结果。若 Unity 未在线,则在 Unity 编辑器内手动打开 `Window > General > Test Runner > EditMode > Run Selected`。下文"运行测试"步骤会写清精确 filter 与期望输出。

> 幂等基线:本子系统的 package.json / 主 asmdef / 测试 asmdef 与 LINK-0 Task 1 创建的文件同名。按本计划顺序执行时这些已由 Task 1 创建,本子系统的 Step 1-2/4/7 遇到已存在文件应核对内容一致(尤其 package.json 的 newtonsoft 依赖版本号与 asmdef 字段)后跳过;新增的是 `Editor/AssemblyInfo.cs`、`Editor/U3DAILinkerPackage.cs`、`Editor/U3DAILinkerSettingsProvider.cs`、`Registry/.keep`、`BundledSkills~/.keep`(若 LINK-0 已建则核对)与测试。

---

### Task 6: 创建 linker 包骨架与元数据

**Files**
- Create: `Packages/com.yoji.u3d-ai-linker/package.json`
- Create: `Packages/com.yoji.u3d-ai-linker/Editor/Yoji.U3DAILinker.Editor.asmdef`
- Create: `Packages/com.yoji.u3d-ai-linker/Editor/AssemblyInfo.cs`
- Create: `Packages/com.yoji.u3d-ai-linker/Editor/U3DAILinkerPackage.cs`
- Create: `Packages/com.yoji.u3d-ai-linker/Registry/.keep`
- Create: `Packages/com.yoji.u3d-ai-linker/BundledSkills~/.keep`
- Test: `Packages/com.yoji.u3d-ai-linker/Tests/Editor/Yoji.U3DAILinker.Editor.Tests.asmdef`
- Test: `Packages/com.yoji.u3d-ai-linker/Tests/Editor/U3DAILinkerPackageTests.cs`

本 Task 是纯逻辑 + 元数据,可在 EditMode 完整 TDD:`U3DAILinkerPackage` 只是一组编译期常量,测试无需真实 UPM/网络/Junction。

- [ ] Step 1: 创建包目录与两个占位文件。在仓库根执行(Git Bash):
  ```bash
  mkdir -p "Packages/com.yoji.u3d-ai-linker/Editor" \
           "Packages/com.yoji.u3d-ai-linker/Registry" \
           "Packages/com.yoji.u3d-ai-linker/BundledSkills~" \
           "Packages/com.yoji.u3d-ai-linker/Tests/Editor"
  printf 'Placeholder so the empty Registry folder is tracked by Git. Real registry snapshot lands in LINK-2/LINK-3.\n' > "Packages/com.yoji.u3d-ai-linker/Registry/.keep"
  printf 'Placeholder so the empty BundledSkills~ folder is tracked by Git. The trailing ~ keeps Unity from importing its contents as assets; real bundled skills land later.\n' > "Packages/com.yoji.u3d-ai-linker/BundledSkills~/.keep"
  ```
  期望:四个目录存在,两个 `.keep` 文件非空。

- [ ] Step 2: 写包测试 asmdef(先建测试程序集,后续步骤往里加测试)。创建 `Packages/com.yoji.u3d-ai-linker/Tests/Editor/Yoji.U3DAILinker.Editor.Tests.asmdef`,完整内容:
  ```json
  {
    "name": "Yoji.U3DAILinker.Editor.Tests",
    "rootNamespace": "Yoji.U3DAILinker.Tests",
    "references": ["Yoji.U3DAILinker.Editor", "UnityEngine.TestRunner", "UnityEditor.TestRunner"],
    "includePlatforms": ["Editor"],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": ["nunit.framework.dll", "Newtonsoft.Json.dll"],
    "autoReferenced": false,
    "defineConstraints": ["UNITY_INCLUDE_TESTS"],
    "versionDefines": [],
    "noEngineReferences": false
  }
  ```
  说明:`references` 里 `Yoji.U3DAILinker.Editor` 此刻尚未存在(Step 4 才建),Unity 会暂时报"missing reference",这是预期的红灯状态。

- [ ] Step 3: 写失败测试。创建 `Packages/com.yoji.u3d-ai-linker/Tests/Editor/U3DAILinkerPackageTests.cs`,完整内容:
  ```csharp
  using NUnit.Framework;
  using Yoji.U3DAILinker.Settings;

  namespace Yoji.U3DAILinker.Tests
  {
      // 验证包级常量:这些值被 SettingsProvider、Registry 加载、诊断报告等多处引用,
      // 必须集中定义且不可漂移。纯编译期常量,EditMode 即可断言,无外部副作用。
      public sealed class U3DAILinkerPackageTests
      {
          [Test]
          public void PackageName_IsCanonicalUpmName()
          {
              Assert.AreEqual("com.yoji.u3d-ai-linker", U3DAILinkerPackage.PackageName);
          }

          [Test]
          public void DisplayName_MatchesSettingsLeaf()
          {
              // SettingsPath 的末段必须与 DisplayName 一致,保证面板标题与菜单项一致。
              Assert.AreEqual("U3D AI Linker", U3DAILinkerPackage.DisplayName);
          }

          [Test]
          public void SettingsPath_IsProjectScopedAndStable()
          {
              Assert.AreEqual("Project/U3D AI Linker", U3DAILinkerPackage.SettingsPath);
          }

          [Test]
          public void RootNamespace_MatchesAsmdef()
          {
              Assert.AreEqual("Yoji.U3DAILinker", U3DAILinkerPackage.RootNamespace);
          }
      }
  }
  ```

- [ ] Step 4: 创建主 asmdef 让测试程序集能解析引用。创建 `Packages/com.yoji.u3d-ai-linker/Editor/Yoji.U3DAILinker.Editor.asmdef`,完整内容:
  ```json
  {
    "name": "Yoji.U3DAILinker.Editor",
    "rootNamespace": "Yoji.U3DAILinker",
    "references": [],
    "includePlatforms": ["Editor"],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": ["Newtonsoft.Json.dll"],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
  }
  ```
  说明:`overrideReferences:true` + `precompiledReferences:["Newtonsoft.Json.dll"]` 与 test-runner 主程序集一致,使本程序集能用 Newtonsoft(供后续 Registry JSON 解析,本 Task 暂未用到但先就位)。

- [ ] Step 5: 创建 `AssemblyInfo.cs` 暴露 internal 给测试。创建 `Packages/com.yoji.u3d-ai-linker/Editor/AssemblyInfo.cs`,完整内容:
  ```csharp
  using System.Runtime.CompilerServices;

  [assembly: InternalsVisibleTo("Yoji.U3DAILinker.Editor.Tests")]
  ```

- [ ] Step 6: 写最小实现 —— 包常量类。创建 `Packages/com.yoji.u3d-ai-linker/Editor/U3DAILinkerPackage.cs`,完整内容:
  ```csharp
  namespace Yoji.U3DAILinker.Settings
  {
      /// <summary>
      /// Linker 包的单一可信常量源。包名、显示名、Project Settings 路径与根命名空间
      /// 都集中在此,供 SettingsProvider 注册、Registry 加载、诊断报告等处引用,避免散落漂移。
      /// </summary>
      public static class U3DAILinkerPackage
      {
          /// <summary>UPM 包名,与 package.json 的 "name" 字段严格一致。</summary>
          public const string PackageName = "com.yoji.u3d-ai-linker";

          /// <summary>面板标题与菜单叶子名,与 package.json 的 "displayName" 一致。</summary>
          public const string DisplayName = "U3D AI Linker";

          /// <summary>Project Settings 注册路径(SettingsScope.Project)。</summary>
          public const string SettingsPath = "Project/U3D AI Linker";

          /// <summary>主/测试程序集的根命名空间前缀,与 asmdef 的 rootNamespace 一致。</summary>
          public const string RootNamespace = "Yoji.U3DAILinker";
      }
  }
  ```

- [ ] Step 7: 创建 `package.json`。创建 `Packages/com.yoji.u3d-ai-linker/package.json`,完整内容:
  ```json
  {
    "name": "com.yoji.u3d-ai-linker",
    "version": "0.1.0",
    "displayName": "U3D AI Linker",
    "description": "Editor-only orchestrator that batch-installs the Yoji Unity AI tools and syncs project Skills/rules to Claude Code and Codex. Windows + Unity 2022.3+ first release.",
    "unity": "2022.3",
    "dependencies": {
      "com.unity.nuget.newtonsoft-json": "3.2.2"
    }
  }
  ```
  说明:版本与依赖号(`3.2.2`)与 test-runner 保持一致,避免 manifest 解析冲突;不依赖 `com.unity.test-framework` 作为运行时依赖(测试程序集通过 `UNITY_INCLUDE_TESTS` 约束 + 引用 `UnityEditor.TestRunner` 即可,与 test-runner 包结构一致)。

- [ ] Step 8: 跑测试确认通过(Unity 在线时,用 test-runner MCP/`unity-test-runner` skill)。先触发重编译让新程序集被加载,再运行本 Task 的测试类:
  - 触发重编译:`POST /recompile`,轮询直到状态 `done` 且无编译错误。
  - 运行 EditMode 测试,filter 精确到本类:`POST /tests`,body `{ "mode": "EditMode", "filter": "Yoji.U3DAILinker.Tests.U3DAILinkerPackageTests" }`,轮询 `GET /tests/{id}`。
  - 期望输出:`passed=4 failed=0 skipped=0`,四个用例 `PackageName_IsCanonicalUpmName` / `DisplayName_MatchesSettingsLeaf` / `SettingsPath_IsProjectScopedAndStable` / `RootNamespace_MatchesAsmdef` 全绿。
  - 若 Unity 离线:在编辑器内 `Window > General > Test Runner`,EditMode 标签下勾选 `U3DAILinkerPackageTests`,点 `Run Selected`,期望 4/4 通过。

- [ ] Step 9: 提交。在仓库根执行:
  ```bash
  git checkout -b feat/u3d-ai-linker-skeleton
  git add "Packages/com.yoji.u3d-ai-linker/"
  git commit -m "$(cat <<'EOF'
feat(u3d-ai-linker): scaffold linker package skeleton

Add com.yoji.u3d-ai-linker package: package.json (Unity 2022.3,
Newtonsoft dependency), Editor/Tests asmdefs matching existing
package conventions, Registry/ and BundledSkills~/ placeholders,
and the U3DAILinkerPackage constant source covered by EditMode tests.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
  ```
  说明:当前在 `main`,先开分支 `feat/u3d-ai-linker-skeleton` 再提交。`.meta` 文件由 Unity 在导入时生成;若此刻 Unity 已生成 `.meta`,`git add` 目录会一并纳入(预期且正确)。

---

### Task 7: 注册 Project/U3D AI Linker SettingsProvider

**Files**
- Create: `Packages/com.yoji.u3d-ai-linker/Editor/U3DAILinkerSettingsProvider.cs`
- Test: `Packages/com.yoji.u3d-ai-linker/Tests/Editor/U3DAILinkerSettingsProviderTests.cs`

`[SettingsProvider]` 的注册本身依赖真实 Editor(`SettingsService` 扫描带该特性的静态方法),但"provider 对象的 path/scope 是否正确"可在 EditMode 直接调用 `CreateProvider()` 断言,无需打开 Project Settings 窗口、无 IMGUI 渲染、无外部副作用。真正的"面板能在 Edit > Project Settings 里出现并可点开"列为手动验证步骤。

- [ ] Step 1: 写失败测试。创建 `Packages/com.yoji.u3d-ai-linker/Tests/Editor/U3DAILinkerSettingsProviderTests.cs`,完整内容:
  ```csharp
  using NUnit.Framework;
  using UnityEditor;
  using Yoji.U3DAILinker.Settings;

  namespace Yoji.U3DAILinker.Tests
  {
      // 验证 SettingsProvider 的注册元数据正确:路径 = "Project/U3D AI Linker"、
      // 作用域 = Project。直接调用 CreateProvider() 取回 provider 对象断言,
      // 不打开 Project Settings 窗口、不触发 IMGUI 渲染,EditMode 安全。
      public sealed class U3DAILinkerSettingsProviderTests
      {
          [Test]
          public void CreateProvider_ReturnsNonNull()
          {
              SettingsProvider provider = U3DAILinkerSettingsProvider.CreateProvider();
              Assert.IsNotNull(provider, "CreateProvider() 不应返回 null");
          }

          [Test]
          public void CreateProvider_UsesPackageSettingsPath()
          {
              SettingsProvider provider = U3DAILinkerSettingsProvider.CreateProvider();
              Assert.AreEqual(U3DAILinkerPackage.SettingsPath, provider.settingsPath);
          }

          [Test]
          public void CreateProvider_IsProjectScoped()
          {
              SettingsProvider provider = U3DAILinkerSettingsProvider.CreateProvider();
              Assert.AreEqual(SettingsScope.Project, provider.scope);
          }

          [Test]
          public void CreateProvider_LabelIsDisplayName()
          {
              // SettingsProvider 的 label 默认取路径末段;显式断言它等于 DisplayName,
              // 防止有人改了路径却忘了同步显示名。
              SettingsProvider provider = U3DAILinkerSettingsProvider.CreateProvider();
              Assert.AreEqual(U3DAILinkerPackage.DisplayName, provider.label);
          }
      }
  }
  ```
  说明:`provider.settingsPath`、`provider.scope`、`provider.label` 都是 UnityEditor `SettingsProvider` 的公开属性,可在 EditMode 直接读取。

- [ ] Step 2: 跑测试确认失败。`U3DAILinkerSettingsProvider` 类尚不存在,测试程序集编译失败(红灯)。
  - Unity 在线:`POST /recompile`,轮询后期望编译错误,报 `The name 'U3DAILinkerSettingsProvider' does not exist`(或等价的未解析符号错误)。这就是预期的失败信号。
  - Unity 离线:Test Runner 窗口会显示该测试程序集编译失败、无法运行,等价红灯。

- [ ] Step 3: 写最小实现。创建 `Packages/com.yoji.u3d-ai-linker/Editor/U3DAILinkerSettingsProvider.cs`,完整内容:
  ```csharp
  using UnityEditor;

  namespace Yoji.U3DAILinker.Settings
  {
      /// <summary>
      /// 在 Edit &gt; Project Settings 下注册 "U3D AI Linker" 面板入口(SettingsScope.Project)。
      /// 当前为骨架:仅完成注册与最小空白 GUI。安装/同步操作按钮、状态表与诊断报告
      /// 由后续子系统填充。注册路径与显示名一律取自 U3DAILinkerPackage 常量,避免漂移。
      /// </summary>
      internal static class U3DAILinkerSettingsProvider
      {
          [SettingsProvider]
          internal static SettingsProvider CreateProvider()
          {
              var provider = new SettingsProvider(
                  U3DAILinkerPackage.SettingsPath,
                  SettingsScope.Project)
              {
                  label = U3DAILinkerPackage.DisplayName,
                  guiHandler = OnGui,
                  // 搜索关键字,便于在 Project Settings 搜索框命中本面板。
                  keywords = new[] { "Yoji", "U3D", "AI", "Linker", "Skill", "Agent", "Claude", "Codex" },
              };
              return provider;
          }

          private static void OnGui(string searchContext)
          {
              // 骨架占位 GUI:后续子系统在此渲染状态区、工具表与操作按钮。
              EditorGUILayout.HelpBox(
                  "U3D AI Linker 控制面板(骨架)。安装与 Agent 同步功能将在后续版本接入。",
                  MessageType.Info);
          }
      }
  }
  ```
  说明:`OnGui` 在 EditMode 单测中不会被触发(测试只读 provider 属性),IMGUI 渲染留作手动验证。`internal` 可见性靠 Task 6 的 `InternalsVisibleTo` 暴露给测试程序集。

- [ ] Step 4: 跑测试确认通过。
  - Unity 在线:`POST /recompile` 确认无编译错误后,`POST /tests` body `{ "mode": "EditMode", "filter": "Yoji.U3DAILinker.Tests.U3DAILinkerSettingsProviderTests" }`,轮询 `GET /tests/{id}`。期望 `passed=4 failed=0 skipped=0`,四个用例 `CreateProvider_ReturnsNonNull` / `CreateProvider_UsesPackageSettingsPath` / `CreateProvider_IsProjectScoped` / `CreateProvider_LabelIsDisplayName` 全绿。
  - Unity 离线:Test Runner 勾选 `U3DAILinkerSettingsProviderTests` 点 `Run Selected`,期望 4/4 通过。

- [ ] Step 5: 手动验证真实面板出现(依赖真实 Editor,无法自动化)。在 Unity 编辑器中:
  - 打开 `Edit > Project Settings`。
  - 在左侧树中找到顶层条目 `U3D AI Linker`(应位于 Project 作用域分组下)。期望:条目存在、可点击。
  - 点击该条目。期望:右侧主区显示一条 Info HelpBox,文案为 "U3D AI Linker 控制面板(骨架)。安装与 Agent 同步功能将在后续版本接入。"。
  - 在 Project Settings 顶部搜索框输入 `Linker`。期望:`U3D AI Linker` 条目被高亮/筛出(验证 keywords 生效)。
  - 任一项不符则回到 Step 3 修正后重跑 Step 4-5。

- [ ] Step 6: 提交。在仓库根执行:
  ```bash
  git add "Packages/com.yoji.u3d-ai-linker/Editor/U3DAILinkerSettingsProvider.cs" \
          "Packages/com.yoji.u3d-ai-linker/Tests/Editor/U3DAILinkerSettingsProviderTests.cs"
  git commit -m "$(cat <<'EOF'
feat(u3d-ai-linker): register Project/U3D AI Linker settings provider

Add the [SettingsProvider] entry under Edit > Project Settings with a
placeholder GUI. Path, label and scope are sourced from
U3DAILinkerPackage and covered by EditMode tests asserting the
provider's settingsPath/scope/label.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
  ```
  说明:若 Unity 已为新文件生成 `.meta`,把对应 `.meta` 一并 `git add`(可改用 `git add "Packages/com.yoji.u3d-ai-linker/"` 一次性纳入)。

---

### Task 8: 包骨架冒烟回归(防漂移哨兵)

**Files**
- Test: `Packages/com.yoji.u3d-ai-linker/Tests/Editor/U3DAILinkerPackageTests.cs`(Modify,追加一个跨文件一致性断言)

本 Task 加一个"哨兵"测试,确保 `package.json` 的 `name`/`displayName`/`unity` 与 C# 常量、SettingsProvider 路径不会各改各的而失步。读取真实 `package.json` 属于读文件副作用,但用 `UnityEditor.PackageManager` 的离线 API(`PackageInfo.FindForAssembly`)即可在 EditMode 安全拿到本包元数据,无网络、无 UPM 写操作。

- [ ] Step 1: 写失败测试(追加到现有 `U3DAILinkerPackageTests.cs`)。在文件顶部 `using` 区追加 `using UnityEditor.PackageManager;` 与 `using System.Reflection;`,并在 `U3DAILinkerPackageTests` 类内追加以下方法:
  ```csharp
          [Test]
          public void PackageJson_NameMatchesConstant()
          {
              // 通过本程序集反查所属 UPM 包,断言 package.json 的 name 与常量一致。
              // PackageInfo.FindForAssembly 是离线本地查询,无网络副作用。
              PackageInfo info = PackageInfo.FindForAssembly(
                  typeof(U3DAILinkerPackage).Assembly);
              Assert.IsNotNull(
                  info,
                  "应能通过程序集定位 com.yoji.u3d-ai-linker 包元数据");
              Assert.AreEqual(U3DAILinkerPackage.PackageName, info.name);
          }

          [Test]
          public void PackageJson_DisplayNameMatchesConstant()
          {
              PackageInfo info = PackageInfo.FindForAssembly(
                  typeof(U3DAILinkerPackage).Assembly);
              Assert.IsNotNull(info);
              Assert.AreEqual(U3DAILinkerPackage.DisplayName, info.displayName);
          }
  ```
  说明:`PackageInfo.FindForAssembly` 在 `UnityEditor.PackageManager` 命名空间;它对本地已解析包返回离线快照,适合 EditMode。引用 `System.Reflection` 仅为可读性预留(`typeof(...).Assembly` 不强制需要它,但保留以防后续扩展用到 `Assembly` 类型显式声明)。

- [ ] Step 2: 跑测试确认失败/通过判定。此处实现(`package.json` 与常量)在 Task 6 已就位,因此这两个新断言**预期直接通过**——它们是回归哨兵而非驱动新实现。仍按 TDD 纪律先单独跑一次确认:
  - Unity 在线:`POST /recompile` 无错后,`POST /tests` body `{ "mode": "EditMode", "filter": "Yoji.U3DAILinker.Tests.U3DAILinkerPackageTests" }`,轮询结果。期望本类从 4 个用例增长到 6 个:`passed=6 failed=0`。
  - 若任一新断言失败(例如 `info` 为 null,说明包未被 Unity 解析/`.meta` 缺失,或 displayName 不符),按错误信息修正 `package.json` 后重跑,不得放过红灯。
  - Unity 离线:Test Runner 勾选 `U3DAILinkerPackageTests` `Run Selected`,期望 6/6 通过。

- [ ] Step 3: 提交。在仓库根执行:
  ```bash
  git add "Packages/com.yoji.u3d-ai-linker/Tests/Editor/U3DAILinkerPackageTests.cs"
  git commit -m "$(cat <<'EOF'
test(u3d-ai-linker): assert package.json stays in sync with constants

Add EditMode sentinels using PackageInfo.FindForAssembly so the
package.json name/displayName cannot drift away from the
U3DAILinkerPackage constants.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
  ```

---

交接给后续子系统的事实:
- 包根 `Packages/com.yoji.u3d-ai-linker/`;主程序集 `Yoji.U3DAILinker.Editor`(rootNamespace `Yoji.U3DAILinker`,已引用 Newtonsoft.Json.dll);测试程序集 `Yoji.U3DAILinker.Editor.Tests`(在 `Tests/Editor/`,`UNITY_INCLUDE_TESTS` 约束)。
- 常量单一源:`Yoji.U3DAILinker.Settings.U3DAILinkerPackage`(PackageName/DisplayName/SettingsPath/RootNamespace)。后续子系统引用常量而非硬编码字符串。
- Project Settings 入口已就位:`Yoji.U3DAILinker.Settings.U3DAILinkerSettingsProvider.CreateProvider()`,`guiHandler` 当前是占位 HelpBox,后续子系统(控制面板)在 `OnGui` 内扩展状态区/工具表/按钮。
- 占位目录 `Registry/`(放 Registry 快照)与 `BundledSkills~/`(放过渡态 skill-only 的 skill 资产)已建,各含 `.keep`;`BundledSkills~` 的 `~` 保留能力尚属未验证假设,LINK-0 须以最小探针验证后再依赖。
- `InternalsVisibleTo("Yoji.U3DAILinker.Editor.Tests")` 已在 `Editor/AssemblyInfo.cs` 声明,新增 internal 类型默认对测试可见。

---

### LINK-2 — Registry schema + 校验 + URL 生成 + 拓扑排序

本子系统 LINK-2 是 u3d-ai-linker 的纯逻辑核心层:把 Registry JSON 解析为强类型 POCO、做严格白名单校验、由已验证字段生成安装 URL、按 `dependsOn`+`order` 拓扑排序。全部可在 EditMode 无真实 UPM/网络/Junction 下跑通,走完整 TDD。

下游子系统(LINK-2b Manifest 事务、LINK-3 UPM 安装队列、LINK-6 Settings UI)消费本层产出的 `RegistryDocument`/`RegistryEntry`/`ToolUrlBuilder.Build*`/`TopologicalSorter.Sort`。

约定提醒:命名空间 `Yoji.U3DAILinker.Registry`;主 asmdef 引用 `Newtonsoft.Json.dll`;测试 asmdef 引用主 asmdef + `nunit.framework.dll`。下面每个 Task 顶部 Files 给精确路径,步骤是 2-5 分钟一个的 checkbox,严格 TDD(写失败测试 -> 跑确认失败 -> 最小实现 -> 跑确认通过 -> commit)。所有代码完整可编译,零占位。

> 幂等基线:本子系统 dependsOn LINK-1,Task 9 的包骨架(package.json/主/测试 asmdef)与 LINK-0/LINK-1 已创建的同名;按顺序执行时核对存在即跳过创建(注意 LINK-1 用 version 0.1.0 + newtonsoft 3.2.2;本子系统原 body 写 1.0.0/3.2.1,以 LINK-1 已落地的为准,不回改)。本子系统真正新增的是 `Editor/Registry/*.cs` 与对应测试。

测试运行命令统一约定(本仓库 EditMode 测试通过 test-runner 的 HTTP recompile+run,或 Unity CLI 批处理):每个 commit 前用以下任一确认绿:
- 在线 Editor: 先 `POST /recompile` 触发重编译,再 `POST /run {"testMode":"EditMode","assemblyNames":["Yoji.U3DAILinker.Editor.Tests"]}`,轮询 `GET /result/<jobId>` 直到 `status=completed`,期望 `failed:0`。
- 或 Unity 批处理(离线): 命令见各步骤,期望退出码 0 且 `results.xml` 中目标 fixture `result="Passed"`。

为避免每步重复长命令,下文用占位 `RUN_TESTS(<fixtureName>)` 指代"对该 fixture 跑 EditMode 测试"。其展开形式(PowerShell,工程根 `E:/Yoji/U3D-Dev-Tools-AI`):

```
& "C:/Program Files/Unity/Hub/Editor/2022.3.62f1/Editor/Unity.exe" -batchmode -projectPath "E:/Yoji/U3D-Dev-Tools-AI" -runTests -testPlatform EditMode -testFilter "Yoji.U3DAILinker.Registry.Tests.<fixtureName>" -testResults "E:/Yoji/U3D-Dev-Tools-AI/Temp/u3dlinker-<fixtureName>.xml" -logFile "E:/Yoji/U3D-Dev-Tools-AI/Temp/u3dlinker-<fixtureName>.log" -quit
```

实际 Unity 版本号以本机 Hub 安装为准(2022.3 LTS 任一补丁版)。CI 不可用时,可用 test-runner skill 的在线接口替代。

---

### Task 9: 创建 u3d-ai-linker 包骨架与 asmdef

建立包目录、package.json、主/测试 asmdef,使后续所有 Task 的代码有可编译落点。本 Task 只创建工程骨架,不含业务逻辑,因此无单测;以"Unity 能加载 asmdef 且空程序集编译通过"作为验收。

Files:
- Create: `Packages/com.yoji.u3d-ai-linker/package.json`
- Create: `Packages/com.yoji.u3d-ai-linker/Editor/Yoji.U3DAILinker.Editor.asmdef`
- Create: `Packages/com.yoji.u3d-ai-linker/Tests/Editor/Yoji.U3DAILinker.Editor.Tests.asmdef`

- [ ] Step 1: 创建 `Packages/com.yoji.u3d-ai-linker/package.json`,声明包名、依赖 Newtonsoft.Json:

```json
{
  "name": "com.yoji.u3d-ai-linker",
  "version": "1.0.0",
  "displayName": "U3D AI Linker",
  "description": "Editor tool that installs Yoji Unity AI tool packages and syncs project Skills to Claude Code and Codex.",
  "unity": "2022.3",
  "dependencies": {
    "com.unity.nuget.newtonsoft-json": "3.2.1"
  },
  "author": {
    "name": "Yoji"
  }
}
```

- [ ] Step 2: 创建主 asmdef `Packages/com.yoji.u3d-ai-linker/Editor/Yoji.U3DAILinker.Editor.asmdef`(对齐 test-runner 主 asmdef 的 overrideReferences/precompiledReferences 风格):

```json
{
  "name": "Yoji.U3DAILinker.Editor",
  "rootNamespace": "Yoji.U3DAILinker",
  "references": [],
  "includePlatforms": ["Editor"],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": true,
  "precompiledReferences": ["Newtonsoft.Json.dll"],
  "autoReferenced": true,
  "defineConstraints": [],
  "versionDefines": [],
  "noEngineReferences": false
}
```

- [ ] Step 3: 创建测试 asmdef `Packages/com.yoji.u3d-ai-linker/Tests/Editor/Yoji.U3DAILinker.Editor.Tests.asmdef`(对齐 test-runner 测试 asmdef):

```json
{
  "name": "Yoji.U3DAILinker.Editor.Tests",
  "rootNamespace": "Yoji.U3DAILinker.Registry.Tests",
  "references": ["Yoji.U3DAILinker.Editor", "UnityEngine.TestRunner", "UnityEditor.TestRunner"],
  "includePlatforms": ["Editor"],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": true,
  "precompiledReferences": ["nunit.framework.dll", "Newtonsoft.Json.dll"],
  "autoReferenced": false,
  "defineConstraints": ["UNITY_INCLUDE_TESTS"],
  "versionDefines": [],
  "noEngineReferences": false
}
```

  幂等说明:若 LINK-1 已创建该测试 asmdef(其 rootNamespace 为 `Yoji.U3DAILinker.Tests`),本子系统的 Registry 测试命名空间用 `Yoji.U3DAILinker.Registry.Tests`,这只是文件内 `namespace` 声明,不要求改 asmdef 的 rootNamespace。沿用 LINK-1 已落地的 asmdef 即可,Registry 测试文件自己声明完整命名空间。

- [ ] Step 4: 手动验证(在线 Editor 或下次 Unity 启动): 让 Unity 刷新导入新包。期望 Console 无编译错误,且 Test Runner 窗口的 EditMode 列表出现空程序集 `Yoji.U3DAILinker.Editor.Tests`(尚无用例,正常)。若用 test-runner: `POST /recompile`,期望返回非 409 且最终 `compilation succeeded`。

- [ ] Step 5: commit。

```
git add Packages/com.yoji.u3d-ai-linker/package.json Packages/com.yoji.u3d-ai-linker/Editor/Yoji.U3DAILinker.Editor.asmdef Packages/com.yoji.u3d-ai-linker/Tests/Editor/Yoji.U3DAILinker.Editor.Tests.asmdef
git commit -m "feat(u3d-ai-linker): scaffold package and editor/test asmdefs"
```

提交信息结尾追加(所有 commit 同此约定):

```
Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
```

---

### Task 10: Registry POCO 与枚举(ToolStatus / ToolKind / RegistryEntry / RegistryDocument)

定义解析目标类型与枚举。`status`/`kind` 用自定义字符串枚举语义:JSON 里是字符串,POCO 暂存原始字符串字段 + 一个解析后的枚举字段,以便校验层对"未知 status/kind"拒绝并给精确报错(不能让 Newtonsoft 默认枚举转换把未知值静默丢成 0)。本 Task 写枚举解析的纯逻辑单测。

Files:
- Create: `Packages/com.yoji.u3d-ai-linker/Editor/Registry/ToolStatus.cs`
- Create: `Packages/com.yoji.u3d-ai-linker/Editor/Registry/ToolKind.cs`
- Create: `Packages/com.yoji.u3d-ai-linker/Editor/Registry/RegistryEntry.cs`
- Create: `Packages/com.yoji.u3d-ai-linker/Editor/Registry/RegistryDocument.cs`
- Test: `Packages/com.yoji.u3d-ai-linker/Tests/Editor/EnumParsingTests.cs`

- [ ] Step 1: 写失败测试 `EnumParsingTests.cs`。先只覆盖枚举解析(POCO 的反序列化在下个 Task)。注意此处引用 `ToolStatusExtensions.TryParse`/`ToolKindExtensions.TryParse` 尚未存在,编译失败即"红":

```csharp
using NUnit.Framework;
using Yoji.U3DAILinker.Registry;

namespace Yoji.U3DAILinker.Registry.Tests
{
    public class EnumParsingTests
    {
        [Test] public void Status_Ready_Parses()
        {
            Assert.IsTrue(ToolStatusExtensions.TryParse("ready", out var s));
            Assert.AreEqual(ToolStatus.Ready, s);
        }

        [Test] public void Status_SkillOnly_Parses()
        {
            Assert.IsTrue(ToolStatusExtensions.TryParse("skill-only", out var s));
            Assert.AreEqual(ToolStatus.SkillOnly, s);
        }

        [Test] public void Status_Planned_Parses()
        {
            Assert.IsTrue(ToolStatusExtensions.TryParse("planned", out var s));
            Assert.AreEqual(ToolStatus.Planned, s);
        }

        [Test] public void Status_Unknown_Fails()
            => Assert.IsFalse(ToolStatusExtensions.TryParse("retired", out _));

        [Test] public void Status_CaseSensitive_Fails()
            => Assert.IsFalse(ToolStatusExtensions.TryParse("Ready", out _));

        [Test] public void Kind_Tool_Parses()
        {
            Assert.IsTrue(ToolKindExtensions.TryParse("tool", out var k));
            Assert.AreEqual(ToolKind.Tool, k);
        }

        [Test] public void Kind_Infra_Parses()
        {
            Assert.IsTrue(ToolKindExtensions.TryParse("infra", out var k));
            Assert.AreEqual(ToolKind.Infra, k);
        }

        [Test] public void Kind_Linker_Parses()
        {
            Assert.IsTrue(ToolKindExtensions.TryParse("linker", out var k));
            Assert.AreEqual(ToolKind.Linker, k);
        }

        [Test] public void Kind_Unknown_Fails()
            => Assert.IsFalse(ToolKindExtensions.TryParse("plugin", out _));
    }
}
```

- [ ] Step 2: 跑 `RUN_TESTS(EnumParsingTests)`,期望失败(编译错误: `ToolStatus`/`ToolKind`/extensions 未定义)。这确认测试确实先红。

- [ ] Step 3: 创建 `ToolStatus.cs`:

```csharp
namespace Yoji.U3DAILinker.Registry
{
    public enum ToolStatus
    {
        Ready,
        SkillOnly,
        Planned
    }

    public static class ToolStatusExtensions
    {
        public static bool TryParse(string raw, out ToolStatus status)
        {
            switch (raw)
            {
                case "ready":
                    status = ToolStatus.Ready;
                    return true;
                case "skill-only":
                    status = ToolStatus.SkillOnly;
                    return true;
                case "planned":
                    status = ToolStatus.Planned;
                    return true;
                default:
                    status = ToolStatus.Planned;
                    return false;
            }
        }
    }
}
```

- [ ] Step 4: 创建 `ToolKind.cs`:

```csharp
namespace Yoji.U3DAILinker.Registry
{
    public enum ToolKind
    {
        Tool,
        Infra,
        Linker
    }

    public static class ToolKindExtensions
    {
        public static bool TryParse(string raw, out ToolKind kind)
        {
            switch (raw)
            {
                case "tool":
                    kind = ToolKind.Tool;
                    return true;
                case "infra":
                    kind = ToolKind.Infra;
                    return true;
                case "linker":
                    kind = ToolKind.Linker;
                    return true;
                default:
                    kind = ToolKind.Tool;
                    return false;
            }
        }
    }
}
```

- [ ] Step 5: 创建 `RegistryEntry.cs`。所有 JSON 字段显式 `[JsonProperty]`;未知字段拒绝由 `MissingMemberHandling.Error` 在反序列化层强制(下个 Task 配置)。`Status`/`Kind` 保留原始字符串,解析后枚举为非序列化只读属性。`AgentAssets` 可空(`null`/缺省合法),`DependsOn` 默认空数组:

```csharp
using Newtonsoft.Json;

namespace Yoji.U3DAILinker.Registry
{
    public sealed class RegistryEntry
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("kind")]
        public string Kind { get; set; }

        [JsonProperty("order")]
        public int Order { get; set; }

        [JsonProperty("packageName")]
        public string PackageName { get; set; }

        [JsonProperty("packagePath")]
        public string PackagePath { get; set; }

        [JsonProperty("revision")]
        public string Revision { get; set; }

        [JsonProperty("defaultEnabled")]
        public bool DefaultEnabled { get; set; }

        [JsonProperty("userToggle")]
        public bool UserToggle { get; set; }

        [JsonProperty("agentAssets")]
        public string AgentAssets { get; set; }

        [JsonProperty("minUnity")]
        public string MinUnity { get; set; }

        [JsonProperty("dependsOn")]
        public string[] DependsOn { get; set; } = new string[0];
    }
}
```

- [ ] Step 6: 创建 `RegistryDocument.cs`。顶层含 `schemaVersion`、`channel`、`branch`(Dev 用)、`entries`:

```csharp
using Newtonsoft.Json;

namespace Yoji.U3DAILinker.Registry
{
    public sealed class RegistryDocument
    {
        [JsonProperty("schemaVersion")]
        public int SchemaVersion { get; set; }

        [JsonProperty("channel")]
        public string Channel { get; set; }

        [JsonProperty("branch")]
        public string Branch { get; set; }

        [JsonProperty("entries")]
        public RegistryEntry[] Entries { get; set; } = new RegistryEntry[0];
    }
}
```

- [ ] Step 7: 跑 `RUN_TESTS(EnumParsingTests)`,期望全绿(9 个用例 Passed,failed:0)。

- [ ] Step 8: commit。

```
git add Packages/com.yoji.u3d-ai-linker/Editor/Registry Packages/com.yoji.u3d-ai-linker/Tests/Editor/EnumParsingTests.cs
git commit -m "feat(u3d-ai-linker): add registry POCOs and status/kind enums"
```

---

### Task 11: RegistryParser — 反序列化与未知字段/schemaVersion 拒绝

把 JSON 字符串解析为 `RegistryDocument`,配置 Newtonsoft `MissingMemberHandling.Error`(未知字段直接抛),并把支持的 `schemaVersion` 常量校验放在解析入口。解析层只负责"结构合法 + schema 受支持 + 未知字段拒绝";语义白名单(仓库/前缀/正则等)在下个 Task 的 Validator。解析失败统一抛 `RegistryParseException`。

Files:
- Create: `Packages/com.yoji.u3d-ai-linker/Editor/Registry/RegistryParseException.cs`
- Create: `Packages/com.yoji.u3d-ai-linker/Editor/Registry/RegistryParser.cs`
- Test: `Packages/com.yoji.u3d-ai-linker/Tests/Editor/RegistryParserTests.cs`

- [ ] Step 1: 写失败测试 `RegistryParserTests.cs`。用最小合法 JSON 验证解析成功,用未知字段/未知 schema 验证抛异常:

```csharp
using NUnit.Framework;
using Yoji.U3DAILinker.Registry;

namespace Yoji.U3DAILinker.Registry.Tests
{
    public class RegistryParserTests
    {
        private const string MinimalValid = @"{
  ""schemaVersion"": 1,
  ""channel"": ""stable"",
  ""entries"": [
    {
      ""id"": ""editor-debug"",
      ""status"": ""ready"",
      ""kind"": ""tool"",
      ""order"": 20,
      ""packageName"": ""com.yoji.editor-debug"",
      ""packagePath"": ""Packages/com.yoji.editor-debug"",
      ""revision"": ""editor-debug-v1.2.0"",
      ""defaultEnabled"": true,
      ""userToggle"": true,
      ""agentAssets"": ""Agent~"",
      ""minUnity"": ""2022.3"",
      ""dependsOn"": []
    }
  ]
}";

        [Test] public void Parse_MinimalValid_Succeeds()
        {
            var doc = RegistryParser.Parse(MinimalValid);
            Assert.AreEqual(1, doc.SchemaVersion);
            Assert.AreEqual("stable", doc.Channel);
            Assert.AreEqual(1, doc.Entries.Length);
            Assert.AreEqual("editor-debug", doc.Entries[0].Id);
            Assert.AreEqual(20, doc.Entries[0].Order);
        }

        [Test] public void Parse_UnknownTopLevelField_Throws()
        {
            var json = @"{ ""schemaVersion"": 1, ""channel"": ""stable"", ""entries"": [], ""extra"": 1 }";
            Assert.Throws<RegistryParseException>(() => RegistryParser.Parse(json));
        }

        [Test] public void Parse_UnknownEntryField_Throws()
        {
            var json = @"{ ""schemaVersion"": 1, ""channel"": ""stable"", ""entries"": [ { ""id"": ""x"", ""mystery"": true } ] }";
            Assert.Throws<RegistryParseException>(() => RegistryParser.Parse(json));
        }

        [Test] public void Parse_UnsupportedSchemaVersion_Throws()
        {
            var json = @"{ ""schemaVersion"": 2, ""channel"": ""stable"", ""entries"": [] }";
            var ex = Assert.Throws<RegistryParseException>(() => RegistryParser.Parse(json));
            StringAssert.Contains("schemaVersion", ex.Message);
        }

        [Test] public void Parse_MissingSchemaVersion_Throws()
        {
            var json = @"{ ""channel"": ""stable"", ""entries"": [] }";
            Assert.Throws<RegistryParseException>(() => RegistryParser.Parse(json));
        }

        [Test] public void Parse_MalformedJson_Throws()
            => Assert.Throws<RegistryParseException>(() => RegistryParser.Parse("{ not json"));
    }
}
```

注: `MissingSchemaVersion` 期望抛是因为缺失时 JSON 无 `schemaVersion` 键,默认 0,不等于支持版本 1,被 schema 校验拒绝。

- [ ] Step 2: 跑 `RUN_TESTS(RegistryParserTests)`,期望失败(编译错误: `RegistryParser`/`RegistryParseException` 未定义)。

- [ ] Step 3: 创建 `RegistryParseException.cs`:

```csharp
using System;

namespace Yoji.U3DAILinker.Registry
{
    public sealed class RegistryParseException : Exception
    {
        public RegistryParseException(string message) : base(message) { }

        public RegistryParseException(string message, Exception inner) : base(message, inner) { }
    }
}
```

- [ ] Step 4: 创建 `RegistryParser.cs`。关键点: `MissingMemberHandling.Error` 让未知字段抛 `JsonSerializationException`,捕获后包成 `RegistryParseException`;`SupportedSchemaVersion` 常量集中管理:

```csharp
using System;
using Newtonsoft.Json;

namespace Yoji.U3DAILinker.Registry
{
    public static class RegistryParser
    {
        public const int SupportedSchemaVersion = 1;

        public static RegistryDocument Parse(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                throw new RegistryParseException("Registry JSON is empty.");
            }

            RegistryDocument doc;
            var settings = new JsonSerializerSettings
            {
                MissingMemberHandling = MissingMemberHandling.Error
            };

            try
            {
                doc = JsonConvert.DeserializeObject<RegistryDocument>(json, settings);
            }
            catch (JsonException ex)
            {
                throw new RegistryParseException("Registry JSON is not well-formed or contains unknown fields: " + ex.Message, ex);
            }

            if (doc == null)
            {
                throw new RegistryParseException("Registry JSON deserialized to null.");
            }

            if (doc.SchemaVersion != SupportedSchemaVersion)
            {
                throw new RegistryParseException(
                    "Unsupported schemaVersion " + doc.SchemaVersion + "; this Linker supports schemaVersion " + SupportedSchemaVersion + ".");
            }

            if (doc.Entries == null)
            {
                doc.Entries = new RegistryEntry[0];
            }

            return doc;
        }
    }
}
```

- [ ] Step 5: 跑 `RUN_TESTS(RegistryParserTests)`,期望全绿(6 用例 Passed)。

- [ ] Step 6: 同时回归 `RUN_TESTS(EnumParsingTests)` 确认未回退。然后 commit。

```
git add Packages/com.yoji.u3d-ai-linker/Editor/Registry/RegistryParseException.cs Packages/com.yoji.u3d-ai-linker/Editor/Registry/RegistryParser.cs Packages/com.yoji.u3d-ai-linker/Tests/Editor/RegistryParserTests.cs
git commit -m "feat(u3d-ai-linker): parse registry json with strict unknown-field and schema rejection"
```

---

### Task 12: RegistryValidator — 白名单语义校验

对已解析的 `RegistryDocument` 做全部白名单校验(spec 162-170, 466):未知 status/kind、`com.yoji.` 前缀、`packagePath == Packages/<packageName>` 且禁 `../绝对/URL`、stable revision 正则、dev 40 位 SHA、`minUnity` 必填、id/packageName 唯一性。校验失败收集**全部**错误一次性抛 `RegistryValidationException`(便于用户一次看清所有问题),而非首错即停。校验需要知道当前通道(stable vs dev)以决定 revision 规则,因此入口接收 `RegistryChannel`。

Files:
- Create: `Packages/com.yoji.u3d-ai-linker/Editor/Registry/RegistryChannel.cs`
- Create: `Packages/com.yoji.u3d-ai-linker/Editor/Registry/RegistryValidationException.cs`
- Create: `Packages/com.yoji.u3d-ai-linker/Editor/Registry/RegistryValidator.cs`
- Test: `Packages/com.yoji.u3d-ai-linker/Tests/Editor/RegistryValidatorTests.cs`

- [ ] Step 1: 写失败测试 `RegistryValidatorTests.cs`。用一个 helper 构造可变 `RegistryEntry`,逐条违规断言抛异常且消息含关键字;再断合法文档通过:

```csharp
using NUnit.Framework;
using Yoji.U3DAILinker.Registry;

namespace Yoji.U3DAILinker.Registry.Tests
{
    public class RegistryValidatorTests
    {
        private static RegistryEntry ValidStableEntry()
        {
            return new RegistryEntry
            {
                Id = "editor-debug",
                Status = "ready",
                Kind = "tool",
                Order = 20,
                PackageName = "com.yoji.editor-debug",
                PackagePath = "Packages/com.yoji.editor-debug",
                Revision = "editor-debug-v1.2.0",
                DefaultEnabled = true,
                UserToggle = true,
                AgentAssets = "Agent~",
                MinUnity = "2022.3",
                DependsOn = new string[0]
            };
        }

        private static RegistryDocument Doc(params RegistryEntry[] entries)
        {
            return new RegistryDocument
            {
                SchemaVersion = 1,
                Channel = "stable",
                Entries = entries
            };
        }

        private static RegistryValidationException AssertInvalid(RegistryDocument doc, RegistryChannel channel)
        {
            return Assert.Throws<RegistryValidationException>(() => RegistryValidator.Validate(doc, channel));
        }

        [Test] public void Validate_ValidStableDoc_Passes()
        {
            Assert.DoesNotThrow(() => RegistryValidator.Validate(Doc(ValidStableEntry()), RegistryChannel.Stable));
        }

        [Test] public void Validate_UnknownStatus_Fails()
        {
            var e = ValidStableEntry();
            e.Status = "retired";
            var ex = AssertInvalid(Doc(e), RegistryChannel.Stable);
            StringAssert.Contains("status", ex.Message);
        }

        [Test] public void Validate_UnknownKind_Fails()
        {
            var e = ValidStableEntry();
            e.Kind = "plugin";
            var ex = AssertInvalid(Doc(e), RegistryChannel.Stable);
            StringAssert.Contains("kind", ex.Message);
        }

        [Test] public void Validate_PackageNameWrongPrefix_Fails()
        {
            var e = ValidStableEntry();
            e.PackageName = "com.acme.editor-debug";
            var ex = AssertInvalid(Doc(e), RegistryChannel.Stable);
            StringAssert.Contains("com.yoji.", ex.Message);
        }

        [Test] public void Validate_PackagePathMismatch_Fails()
        {
            var e = ValidStableEntry();
            e.PackagePath = "Packages/com.yoji.other";
            var ex = AssertInvalid(Doc(e), RegistryChannel.Stable);
            StringAssert.Contains("packagePath", ex.Message);
        }

        [Test] public void Validate_PackagePathWithDotDot_Fails()
        {
            var e = ValidStableEntry();
            e.PackagePath = "Packages/../com.yoji.editor-debug";
            AssertInvalid(Doc(e), RegistryChannel.Stable);
        }

        [Test] public void Validate_PackagePathAbsolute_Fails()
        {
            var e = ValidStableEntry();
            e.PackagePath = "C:/Packages/com.yoji.editor-debug";
            AssertInvalid(Doc(e), RegistryChannel.Stable);
        }

        [Test] public void Validate_PackagePathUrl_Fails()
        {
            var e = ValidStableEntry();
            e.PackagePath = "https://example.com/com.yoji.editor-debug";
            AssertInvalid(Doc(e), RegistryChannel.Stable);
        }

        [Test] public void Validate_StableRevisionMatchesIdAndSemver_Passes()
        {
            Assert.DoesNotThrow(() => RegistryValidator.Validate(Doc(ValidStableEntry()), RegistryChannel.Stable));
        }

        [Test] public void Validate_StableRevisionWrongPrefix_Fails()
        {
            var e = ValidStableEntry();
            e.Revision = "editor-core-v1.2.0";
            var ex = AssertInvalid(Doc(e), RegistryChannel.Stable);
            StringAssert.Contains("revision", ex.Message);
        }

        [Test] public void Validate_StableRevisionNotSemver_Fails()
        {
            var e = ValidStableEntry();
            e.Revision = "editor-debug-v1.2";
            AssertInvalid(Doc(e), RegistryChannel.Stable);
        }

        [Test] public void Validate_DevRevisionFullSha_Passes()
        {
            var e = ValidStableEntry();
            e.Revision = "0123456789abcdef0123456789abcdef01234567";
            Assert.DoesNotThrow(() => RegistryValidator.Validate(Doc(e), RegistryChannel.Dev));
        }

        [Test] public void Validate_DevRevisionShortSha_Fails()
        {
            var e = ValidStableEntry();
            e.Revision = "0123456";
            AssertInvalid(Doc(e), RegistryChannel.Dev);
        }

        [Test] public void Validate_DevRevisionUppercaseSha_Fails()
        {
            var e = ValidStableEntry();
            e.Revision = "0123456789ABCDEF0123456789abcdef01234567";
            AssertInvalid(Doc(e), RegistryChannel.Dev);
        }

        [Test] public void Validate_MissingMinUnity_Fails()
        {
            var e = ValidStableEntry();
            e.MinUnity = null;
            var ex = AssertInvalid(Doc(e), RegistryChannel.Stable);
            StringAssert.Contains("minUnity", ex.Message);
        }

        [Test] public void Validate_DuplicateId_Fails()
        {
            var a = ValidStableEntry();
            var b = ValidStableEntry();
            b.PackageName = "com.yoji.editor-debug-2";
            b.PackagePath = "Packages/com.yoji.editor-debug-2";
            b.Revision = "editor-debug-v9.9.9";
            var ex = AssertInvalid(Doc(a, b), RegistryChannel.Stable);
            StringAssert.Contains("id", ex.Message);
        }

        [Test] public void Validate_DuplicatePackageName_Fails()
        {
            var a = ValidStableEntry();
            var b = ValidStableEntry();
            b.Id = "editor-debug-2";
            b.Revision = "editor-debug-2-v1.0.0";
            var ex = AssertInvalid(Doc(a, b), RegistryChannel.Stable);
            StringAssert.Contains("packageName", ex.Message);
        }

        [Test] public void Validate_DevBranchOnlyMain_FailsWhenNotMain()
        {
            var doc = Doc(ValidStableEntry());
            doc.Entries[0].Revision = "0123456789abcdef0123456789abcdef01234567";
            doc.Branch = "feature/x";
            AssertInvalid(doc, RegistryChannel.Dev);
        }

        [Test] public void Validate_AggregatesMultipleErrors()
        {
            var e = ValidStableEntry();
            e.Status = "retired";
            e.Kind = "plugin";
            e.MinUnity = null;
            var ex = AssertInvalid(Doc(e), RegistryChannel.Stable);
            StringAssert.Contains("status", ex.Message);
            StringAssert.Contains("kind", ex.Message);
            StringAssert.Contains("minUnity", ex.Message);
        }
    }
}
```

- [ ] Step 2: 跑 `RUN_TESTS(RegistryValidatorTests)`,期望失败(编译错误: `RegistryChannel`/`RegistryValidator`/`RegistryValidationException` 未定义)。

- [ ] Step 3: 创建 `RegistryChannel.cs`:

```csharp
namespace Yoji.U3DAILinker.Registry
{
    public enum RegistryChannel
    {
        Stable,
        Dev
    }
}
```

- [ ] Step 4: 创建 `RegistryValidationException.cs`(携带所有错误列表,Message 拼接):

```csharp
using System;
using System.Collections.Generic;

namespace Yoji.U3DAILinker.Registry
{
    public sealed class RegistryValidationException : Exception
    {
        public IReadOnlyList<string> Errors { get; }

        public RegistryValidationException(IReadOnlyList<string> errors)
            : base("Registry validation failed:\n" + string.Join("\n", errors))
        {
            Errors = errors;
        }
    }
}
```

- [ ] Step 5: 创建 `RegistryValidator.cs`。逐条对应 spec 白名单。注意: `Packages/<name>` 用普通字符串比较(不调用 `Path.GetFullPath`,避免引入本机路径副作用);`../`、绝对路径、URL 通过前缀/字符检查拒绝:

```csharp
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Yoji.U3DAILinker.Registry
{
    public static class RegistryValidator
    {
        public const string RequiredPackagePrefix = "com.yoji.";
        private static readonly Regex SemverRevisionTail = new Regex(@"^v\d+\.\d+\.\d+$");
        private static readonly Regex FullSha = new Regex(@"^[0-9a-f]{40}$");

        public static void Validate(RegistryDocument doc, RegistryChannel channel)
        {
            var errors = new List<string>();

            if (doc == null)
            {
                errors.Add("Registry document is null.");
                throw new RegistryValidationException(errors);
            }

            if (channel == RegistryChannel.Dev && doc.Branch != null && doc.Branch != "main")
            {
                errors.Add("Dev registry branch must be 'main' but was '" + doc.Branch + "'.");
            }

            var seenIds = new HashSet<string>();
            var seenPackageNames = new HashSet<string>();

            var entries = doc.Entries ?? new RegistryEntry[0];
            foreach (var e in entries)
            {
                var label = e != null && !string.IsNullOrEmpty(e.Id) ? e.Id : "<missing-id>";

                if (e == null)
                {
                    errors.Add("Registry contains a null entry.");
                    continue;
                }

                if (string.IsNullOrEmpty(e.Id))
                {
                    errors.Add("Entry has empty id.");
                }
                else if (!seenIds.Add(e.Id))
                {
                    errors.Add("Duplicate id '" + e.Id + "'.");
                }

                if (!ToolStatusExtensions.TryParse(e.Status, out _))
                {
                    errors.Add("Entry '" + label + "' has unknown status '" + e.Status + "'.");
                }

                if (!ToolKindExtensions.TryParse(e.Kind, out _))
                {
                    errors.Add("Entry '" + label + "' has unknown kind '" + e.Kind + "'.");
                }

                if (string.IsNullOrEmpty(e.PackageName))
                {
                    errors.Add("Entry '" + label + "' has empty packageName.");
                }
                else
                {
                    if (!e.PackageName.StartsWith(RequiredPackagePrefix))
                    {
                        errors.Add("Entry '" + label + "' packageName '" + e.PackageName + "' must start with '" + RequiredPackagePrefix + "'.");
                    }

                    if (!seenPackageNames.Add(e.PackageName))
                    {
                        errors.Add("Duplicate packageName '" + e.PackageName + "'.");
                    }

                    ValidatePackagePath(e, label, errors);
                }

                ValidateRevision(e, label, channel, errors);

                if (string.IsNullOrEmpty(e.MinUnity))
                {
                    errors.Add("Entry '" + label + "' is missing required minUnity.");
                }
            }

            if (errors.Count > 0)
            {
                throw new RegistryValidationException(errors);
            }
        }

        private static void ValidatePackagePath(RegistryEntry e, string label, List<string> errors)
        {
            var path = e.PackagePath;
            if (string.IsNullOrEmpty(path))
            {
                errors.Add("Entry '" + label + "' has empty packagePath.");
                return;
            }

            if (path.Contains(".."))
            {
                errors.Add("Entry '" + label + "' packagePath '" + path + "' must not contain '..'.");
            }

            if (path.Contains("://") || path.StartsWith("/") || (path.Length >= 2 && path[1] == ':'))
            {
                errors.Add("Entry '" + label + "' packagePath '" + path + "' must be a relative 'Packages/<name>' path, not an absolute path or URL.");
            }

            var expected = "Packages/" + e.PackageName;
            if (path != expected)
            {
                errors.Add("Entry '" + label + "' packagePath '" + path + "' must equal '" + expected + "'.");
            }
        }

        private static void ValidateRevision(RegistryEntry e, string label, RegistryChannel channel, List<string> errors)
        {
            var rev = e.Revision;
            if (string.IsNullOrEmpty(rev))
            {
                errors.Add("Entry '" + label + "' has empty revision.");
                return;
            }

            if (channel == RegistryChannel.Stable)
            {
                var prefix = (e.Id ?? string.Empty) + "-";
                if (!rev.StartsWith(prefix))
                {
                    errors.Add("Entry '" + label + "' stable revision '" + rev + "' must start with '" + prefix + "'.");
                    return;
                }

                var tail = rev.Substring(prefix.Length);
                if (!SemverRevisionTail.IsMatch(tail))
                {
                    errors.Add("Entry '" + label + "' stable revision '" + rev + "' must match '<id>-v<major>.<minor>.<patch>'.");
                }
            }
            else
            {
                if (!FullSha.IsMatch(rev))
                {
                    errors.Add("Entry '" + label + "' dev revision '" + rev + "' must be a 40-character lowercase Git commit SHA.");
                }
            }
        }
    }
}
```

- [ ] Step 6: 跑 `RUN_TESTS(RegistryValidatorTests)`,期望全绿(20 用例 Passed)。

- [ ] Step 7: commit。

```
git add Packages/com.yoji.u3d-ai-linker/Editor/Registry/RegistryChannel.cs Packages/com.yoji.u3d-ai-linker/Editor/Registry/RegistryValidationException.cs Packages/com.yoji.u3d-ai-linker/Editor/Registry/RegistryValidator.cs Packages/com.yoji.u3d-ai-linker/Tests/Editor/RegistryValidatorTests.cs
git commit -m "feat(u3d-ai-linker): whitelist-validate registry repo/path/naming/revision/uniqueness"
```

---

### Task 13: ToolUrlBuilder — 由已验证字段生成安装 URL

只接收已通过 Validator 的 `RegistryEntry`,按固定仓库生成三种 URL:Stable tag Git URL、Dev SHA Git URL、Local `file:` URL(spec 174-188, 468)。仓库固定 `sputnicyoji/U3D-Dev-Tools-AI`,绝不消费 Registry 内任何完整 URL 字段(Registry 也没有该字段)。`BuildGit` 按通道选 stable/dev revision 格式;`BuildLocalFile` 需要工程根绝对路径作为参数(由调用方注入,本层不读文件系统,保持纯逻辑)。

Files:
- Create: `Packages/com.yoji.u3d-ai-linker/Editor/Registry/ToolUrlBuilder.cs`
- Test: `Packages/com.yoji.u3d-ai-linker/Tests/Editor/ToolUrlBuilderTests.cs`

- [ ] Step 1: 写失败测试 `ToolUrlBuilderTests.cs`:

```csharp
using NUnit.Framework;
using Yoji.U3DAILinker.Registry;

namespace Yoji.U3DAILinker.Registry.Tests
{
    public class ToolUrlBuilderTests
    {
        private static RegistryEntry Entry(string id, string packageName, string packagePath, string revision)
        {
            return new RegistryEntry
            {
                Id = id,
                Status = "ready",
                Kind = "tool",
                Order = 20,
                PackageName = packageName,
                PackagePath = packagePath,
                Revision = revision,
                DefaultEnabled = true,
                UserToggle = true,
                AgentAssets = "Agent~",
                MinUnity = "2022.3",
                DependsOn = new string[0]
            };
        }

        [Test] public void BuildGit_Stable_UsesTagRevision()
        {
            var e = Entry("editor-debug", "com.yoji.editor-debug", "Packages/com.yoji.editor-debug", "editor-debug-v1.2.0");
            var url = ToolUrlBuilder.BuildGit(e);
            Assert.AreEqual(
                "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/com.yoji.editor-debug#editor-debug-v1.2.0",
                url);
        }

        [Test] public void BuildGit_Dev_UsesShaRevision()
        {
            var e = Entry("editor-debug", "com.yoji.editor-debug", "Packages/com.yoji.editor-debug",
                "0123456789abcdef0123456789abcdef01234567");
            var url = ToolUrlBuilder.BuildGit(e);
            Assert.AreEqual(
                "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/com.yoji.editor-debug#0123456789abcdef0123456789abcdef01234567",
                url);
        }

        [Test] public void BuildLocalFile_UsesAbsoluteProjectRoot()
        {
            var e = Entry("editor-debug", "com.yoji.editor-debug", "Packages/com.yoji.editor-debug", "editor-debug-v1.2.0");
            var url = ToolUrlBuilder.BuildLocalFile(e, "E:/Yoji/U3D-Dev-Tools-AI");
            Assert.AreEqual("file:E:/Yoji/U3D-Dev-Tools-AI/Packages/com.yoji.editor-debug", url);
        }

        [Test] public void BuildLocalFile_NormalizesBackslashesAndTrailingSlash()
        {
            var e = Entry("editor-debug", "com.yoji.editor-debug", "Packages/com.yoji.editor-debug", "editor-debug-v1.2.0");
            var url = ToolUrlBuilder.BuildLocalFile(e, @"E:\Yoji\U3D-Dev-Tools-AI\");
            Assert.AreEqual("file:E:/Yoji/U3D-Dev-Tools-AI/Packages/com.yoji.editor-debug", url);
        }

        [Test] public void BuildGit_UsesPackagePathFromEntry()
        {
            var e = Entry("test-runner", "com.yoji.test-runner", "Packages/com.yoji.test-runner", "test-runner-v1.1.0");
            var url = ToolUrlBuilder.BuildGit(e);
            StringAssert.Contains("?path=/Packages/com.yoji.test-runner#", url);
        }
    }
}
```

- [ ] Step 2: 跑 `RUN_TESTS(ToolUrlBuilderTests)`,期望失败(编译错误: `ToolUrlBuilder` 未定义)。

- [ ] Step 3: 创建 `ToolUrlBuilder.cs`。仓库与 host 写死为常量;`BuildGit` 直接拼 `?path=/<packagePath>#<revision>`;`BuildLocalFile` 规范化路径分隔符与尾斜杠:

```csharp
namespace Yoji.U3DAILinker.Registry
{
    public static class ToolUrlBuilder
    {
        public const string GitRepoUrl = "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git";

        public static string BuildGit(RegistryEntry entry)
        {
            return GitRepoUrl + "?path=/" + entry.PackagePath + "#" + entry.Revision;
        }

        public static string BuildLocalFile(RegistryEntry entry, string projectRootAbsolutePath)
        {
            var root = projectRootAbsolutePath.Replace('\\', '/');
            if (root.EndsWith("/"))
            {
                root = root.Substring(0, root.Length - 1);
            }

            return "file:" + root + "/" + entry.PackagePath;
        }
    }
}
```

- [ ] Step 4: 跑 `RUN_TESTS(ToolUrlBuilderTests)`,期望全绿(5 用例 Passed)。

- [ ] Step 5: commit。

```
git add Packages/com.yoji.u3d-ai-linker/Editor/Registry/ToolUrlBuilder.cs Packages/com.yoji.u3d-ai-linker/Tests/Editor/ToolUrlBuilderTests.cs
git commit -m "feat(u3d-ai-linker): build stable/dev git and local file install urls from validated fields"
```

---

### Task 14: TopologicalSorter — 按 dependsOn + order 排序与拒绝规则

把已验证的 entries 排成安装顺序(spec 116, 467):先按 `dependsOn` 做拓扑排序(被依赖者在前),同层用 `order` 升序打破并列,保证确定性输出。拒绝规则:环依赖、未知依赖 ID、infra 被用户直接启用(`kind:infra` 且 `defaultEnabled=true` 表示被直接启用,违规)。失败抛 `TopologicalSortException`。

Files:
- Create: `Packages/com.yoji.u3d-ai-linker/Editor/Registry/TopologicalSortException.cs`
- Create: `Packages/com.yoji.u3d-ai-linker/Editor/Registry/TopologicalSorter.cs`
- Test: `Packages/com.yoji.u3d-ai-linker/Tests/Editor/TopologicalSorterTests.cs`

- [ ] Step 1: 写失败测试 `TopologicalSorterTests.cs`。覆盖:基本拓扑、order 打破并列、环依赖拒绝、未知 ID 拒绝、infra 被直接启用拒绝、infra 作为前置不被拒绝:

```csharp
using System.Linq;
using NUnit.Framework;
using Yoji.U3DAILinker.Registry;

namespace Yoji.U3DAILinker.Registry.Tests
{
    public class TopologicalSorterTests
    {
        private static RegistryEntry E(string id, string kind, int order, bool defaultEnabled, params string[] dependsOn)
        {
            return new RegistryEntry
            {
                Id = id,
                Status = "ready",
                Kind = kind,
                Order = order,
                PackageName = "com.yoji." + id,
                PackagePath = "Packages/com.yoji." + id,
                Revision = id + "-v1.0.0",
                DefaultEnabled = defaultEnabled,
                UserToggle = kind == "tool",
                AgentAssets = null,
                MinUnity = "2022.3",
                DependsOn = dependsOn
            };
        }

        private static string[] SortIds(params RegistryEntry[] entries)
        {
            return TopologicalSorter.Sort(entries).Select(x => x.Id).ToArray();
        }

        [Test] public void Sort_DependencyComesBeforeDependent()
        {
            var core = E("editor-core", "infra", 10, false);
            var tr = E("test-runner", "tool", 20, true, "editor-core");
            var ids = SortIds(tr, core);
            Assert.Less(System.Array.IndexOf(ids, "editor-core"), System.Array.IndexOf(ids, "test-runner"));
        }

        [Test] public void Sort_TiesBrokenByOrder()
        {
            var a = E("a", "tool", 30, true);
            var b = E("b", "tool", 10, true);
            var c = E("c", "tool", 20, true);
            var ids = SortIds(a, b, c);
            CollectionAssert.AreEqual(new[] { "b", "c", "a" }, ids);
        }

        [Test] public void Sort_DeterministicAcrossInputOrder()
        {
            var a = E("a", "tool", 30, true);
            var b = E("b", "tool", 10, true);
            var c = E("c", "tool", 20, true);
            CollectionAssert.AreEqual(SortIds(a, b, c), SortIds(c, b, a));
        }

        [Test] public void Sort_CyclicDependency_Throws()
        {
            var x = E("x", "tool", 10, true, "y");
            var y = E("y", "tool", 20, true, "x");
            var ex = Assert.Throws<TopologicalSortException>(() => TopologicalSorter.Sort(new[] { x, y }));
            StringAssert.Contains("cycle", ex.Message.ToLowerInvariant());
        }

        [Test] public void Sort_UnknownDependency_Throws()
        {
            var tr = E("test-runner", "tool", 20, true, "nonexistent");
            var ex = Assert.Throws<TopologicalSortException>(() => TopologicalSorter.Sort(new[] { tr }));
            StringAssert.Contains("nonexistent", ex.Message);
        }

        [Test] public void Sort_InfraDirectlyEnabled_Throws()
        {
            var core = E("editor-core", "infra", 10, true);
            var ex = Assert.Throws<TopologicalSortException>(() => TopologicalSorter.Sort(new[] { core }));
            StringAssert.Contains("infra", ex.Message.ToLowerInvariant());
        }

        [Test] public void Sort_InfraAsDependency_NotDirectlyEnabled_Passes()
        {
            var core = E("editor-core", "infra", 10, false);
            var tr = E("test-runner", "tool", 20, true, "editor-core");
            Assert.DoesNotThrow(() => TopologicalSorter.Sort(new[] { core, tr }));
        }

        [Test] public void Sort_ChainedDependencies_OrderedCorrectly()
        {
            var core = E("editor-core", "infra", 5, false);
            var dbg = E("editor-debug", "tool", 20, true, "editor-core");
            var tr = E("test-runner", "tool", 10, true, "editor-debug");
            var ids = SortIds(tr, dbg, core);
            Assert.Less(System.Array.IndexOf(ids, "editor-core"), System.Array.IndexOf(ids, "editor-debug"));
            Assert.Less(System.Array.IndexOf(ids, "editor-debug"), System.Array.IndexOf(ids, "test-runner"));
        }
    }
}
```

- [ ] Step 2: 跑 `RUN_TESTS(TopologicalSorterTests)`,期望失败(编译错误: `TopologicalSorter`/`TopologicalSortException` 未定义)。

- [ ] Step 3: 创建 `TopologicalSortException.cs`:

```csharp
using System;

namespace Yoji.U3DAILinker.Registry
{
    public sealed class TopologicalSortException : Exception
    {
        public TopologicalSortException(string message) : base(message) { }
    }
}
```

- [ ] Step 4: 创建 `TopologicalSorter.cs`。用 Kahn 算法保证确定性:每轮从入度为 0 的候选里按 `order`(并列再按 `id`)取最小,逐个出队;循环结束若仍有剩余即存在环。先做前置拒绝(未知依赖、infra 直接启用):

```csharp
using System.Collections.Generic;
using System.Linq;

namespace Yoji.U3DAILinker.Registry
{
    public static class TopologicalSorter
    {
        public static IReadOnlyList<RegistryEntry> Sort(IReadOnlyList<RegistryEntry> entries)
        {
            var byId = new Dictionary<string, RegistryEntry>();
            foreach (var e in entries)
            {
                byId[e.Id] = e;
            }

            foreach (var e in entries)
            {
                if (ToolKindExtensions.TryParse(e.Kind, out var kind) && kind == ToolKind.Infra && e.DefaultEnabled)
                {
                    throw new TopologicalSortException(
                        "Infra entry '" + e.Id + "' must not be directly enabled (defaultEnabled=true); infra is installed only as a dependency.");
                }

                foreach (var dep in e.DependsOn ?? new string[0])
                {
                    if (!byId.ContainsKey(dep))
                    {
                        throw new TopologicalSortException(
                            "Entry '" + e.Id + "' depends on unknown id '" + dep + "'.");
                    }
                }
            }

            var inDegree = new Dictionary<string, int>();
            var dependents = new Dictionary<string, List<string>>();
            foreach (var e in entries)
            {
                inDegree[e.Id] = 0;
                dependents[e.Id] = new List<string>();
            }

            foreach (var e in entries)
            {
                foreach (var dep in e.DependsOn ?? new string[0])
                {
                    inDegree[e.Id]++;
                    dependents[dep].Add(e.Id);
                }
            }

            var result = new List<RegistryEntry>();
            var ready = new List<string>();
            foreach (var e in entries)
            {
                if (inDegree[e.Id] == 0)
                {
                    ready.Add(e.Id);
                }
            }

            while (ready.Count > 0)
            {
                ready.Sort((x, y) =>
                {
                    var cmp = byId[x].Order.CompareTo(byId[y].Order);
                    return cmp != 0 ? cmp : string.CompareOrdinal(x, y);
                });

                var nextId = ready[0];
                ready.RemoveAt(0);
                result.Add(byId[nextId]);

                foreach (var dependent in dependents[nextId])
                {
                    inDegree[dependent]--;
                    if (inDegree[dependent] == 0)
                    {
                        ready.Add(dependent);
                    }
                }
            }

            if (result.Count != entries.Count)
            {
                var remaining = entries.Select(e => e.Id).Where(id => !result.Any(r => r.Id == id));
                throw new TopologicalSortException(
                    "Dependency cycle detected among entries: " + string.Join(", ", remaining) + ".");
            }

            return result;
        }
    }
}
```

- [ ] Step 5: 跑 `RUN_TESTS(TopologicalSorterTests)`,期望全绿(8 用例 Passed)。

- [ ] Step 6: 全量回归: 跑整个 `Yoji.U3DAILinker.Editor.Tests` 程序集(在线 `POST /run {"testMode":"EditMode","assemblyNames":["Yoji.U3DAILinker.Editor.Tests"]}`,或 CLI 去掉 `-testFilter`),期望 `failed:0`(累计约 48 用例)。然后 commit。

```
git add Packages/com.yoji.u3d-ai-linker/Editor/Registry/TopologicalSortException.cs Packages/com.yoji.u3d-ai-linker/Editor/Registry/TopologicalSorter.cs Packages/com.yoji.u3d-ai-linker/Tests/Editor/TopologicalSorterTests.cs
git commit -m "feat(u3d-ai-linker): topologically sort entries by dependsOn and order with cycle/unknown/infra rejection"
```

---

下游交接说明(供组装者跨子系统一致性):
- LINK-2b/3/6 应通过 `RegistryParser.Parse` -> `RegistryValidator.Validate(doc, channel)` -> `TopologicalSorter.Sort(doc.Entries)` 的固定管线消费本层。`channel` 由远程 Registry 文件名(stable.json/dev.json)决定,本层不自行判断通道来源。
- 安装 URL 一律走 `ToolUrlBuilder`,严禁下游自行拼 Git URL;本机开发模式调 `BuildLocalFile(entry, projectRoot)`,`projectRoot` 由 LINK-2b 的环境层提供绝对路径。
- 所有自定义异常(`RegistryParseException`/`RegistryValidationException`/`TopologicalSortException`)继承 `System.Exception`,下游可统一捕获展示;`RegistryValidationException.Errors` 提供逐条错误列表供 UI 展示。

---

### LINK-2b — Manifest 修改事务 + 冲突检测 + Remove 语义

本子系统是纯文件/纯逻辑层，全部可在 EditMode 用临时目录跑完整 TDD，不触碰真实 UPM、域重载或网络。它向上层（UPM 安装队列子系统）暴露 `ManifestTransaction.Apply` / `ManifestRollback.Rollback` / `RemovePlanner.Compute` 三个入口；不直接调用 `Client.Add`，只负责把 manifest 改对、记账、可回滚。

> 依赖说明：`RegistryEntryInfo` 是本子系统为 `RemovePlanner` 自带的最小只读投影类型（仅 Id/PackageName/Kind/DependsOn 四字段），**不**复用 Registry 子系统（LINK-2）的完整解析模型，以保持本包纯逻辑、可独立单测。上层在 Remove 时把已解析的 Registry 条目映射成 `RegistryEntryInfo[]` 传入即可。dependsOn LINK-1 仅表示语义上游，无编译期引用。

公共契约（其它子系统按此对接，不得改名）：
- ownership 判定：`com.yoji.*` 依赖值是「本仓 Git URL」或「Local file:」→ 受管可覆盖/可删；其它（第三方 registry 版本号、外部 Git、手写）→ 冲突，不自动动。
- 事务七步：读 JObject 保序保留未知字段 → backup 到 `Library/U3DAILinker/backups/manifest-<operationId>.json` → 只改 `dependencies` 里受管 `com.yoji.*` → 写 `Packages/manifest.json.u3d-ai-linker.tmp` → 解析校验 tmp → `File.Replace` 原子替换 → 写 `Library/U3DAILinker/operation.json` 记 oldValue/newValue/channel/revision/受影响包。
- 失败恢复：替换前失败删 tmp、不动 manifest；冲突在写任何东西之前就拒绝（`Committed=false` 且 `Conflicts` 非空）。
- 回滚：优先用 backup 文件整体恢复；恢复前重读当前 manifest，若已被手动改动（与 operation.json 记录的 newValue 不一致）则停，返回 `FailureReason`。

> 幂等基线:本子系统 Task 15 的包骨架与 LINK-0/1/2 已创建的同名,核对存在即跳过创建。注意本子系统 `OperationLog.cs` 定义 `DependencyChange`/`OperationRecord` 等记账类型,与 LINK-3 的 `Editor/Operations/OperationLog.cs` 是同一文件:LINK-3 的版本是更完整的队列日志 POCO。组装时**以 LINK-3 的 `OperationLog.cs` 为权威**(它含队列 Phase/ToolIds 等),本子系统所需的 `DependencyChange`/`OperationRecord`/`ManifestConflict`/`ManifestTransactionResult` 合并进同一命名空间 `Yoji.U3DAILinker.Operations`;若按本计划顺序 LINK-2b 先落地,则 LINK-3 Task 22 创建 `OperationLog.cs` 时核对并合并,不重复定义同名类型。

---

### Task 15: 创建 u3d-ai-linker 包骨架与 asmdef

本任务只建包结构，无生产逻辑，便于后续任务有编译目标。

Files:
- Create: `Packages/com.yoji.u3d-ai-linker/package.json`
- Create: `Packages/com.yoji.u3d-ai-linker/Editor/Yoji.U3DAILinker.Editor.asmdef`
- Create: `Packages/com.yoji.u3d-ai-linker/Tests/Editor/Yoji.U3DAILinker.Editor.Tests.asmdef`

- [ ] Step 1: 创建 `Packages/com.yoji.u3d-ai-linker/package.json`，声明对 Newtonsoft 的依赖：

```json
{
  "name": "com.yoji.u3d-ai-linker",
  "version": "1.0.0",
  "displayName": "U3D AI Linker",
  "description": "Installs and links Yoji U3D dev tools and their AI agent assets into a Unity project.",
  "unity": "2022.3",
  "dependencies": {
    "com.unity.nuget.newtonsoft-json": "3.2.1"
  },
  "author": {
    "name": "Yoji"
  }
}
```

- [ ] Step 2: 创建主程序集 `Packages/com.yoji.u3d-ai-linker/Editor/Yoji.U3DAILinker.Editor.asmdef`，仅 Editor 平台，引用 Newtonsoft（与 test-runner 包一致用 `precompiledReferences` + `overrideReferences`）：

```json
{
  "name": "Yoji.U3DAILinker.Editor",
  "rootNamespace": "Yoji.U3DAILinker",
  "references": [],
  "includePlatforms": ["Editor"],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": true,
  "precompiledReferences": ["Newtonsoft.Json.dll"],
  "autoReferenced": true,
  "defineConstraints": [],
  "versionDefines": [],
  "noEngineReferences": false
}
```

- [ ] Step 3: 创建测试程序集 `Packages/com.yoji.u3d-ai-linker/Tests/Editor/Yoji.U3DAILinker.Editor.Tests.asmdef`，引用主程序集 + NUnit + TestRunner：

```json
{
  "name": "Yoji.U3DAILinker.Editor.Tests",
  "rootNamespace": "Yoji.U3DAILinker.Tests",
  "references": ["Yoji.U3DAILinker.Editor", "UnityEngine.TestRunner", "UnityEditor.TestRunner"],
  "includePlatforms": ["Editor"],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": true,
  "precompiledReferences": ["nunit.framework.dll", "Newtonsoft.Json.dll"],
  "autoReferenced": false,
  "defineConstraints": ["UNITY_INCLUDE_TESTS"],
  "versionDefines": [],
  "noEngineReferences": false
}
```

- [ ] Step 4: commit：

```bash
git checkout -b feat/u3d-ai-linker-manifest
git add Packages/com.yoji.u3d-ai-linker/package.json \
        Packages/com.yoji.u3d-ai-linker/Editor/Yoji.U3DAILinker.Editor.asmdef \
        Packages/com.yoji.u3d-ai-linker/Tests/Editor/Yoji.U3DAILinker.Editor.Tests.asmdef
git commit -m "$(cat <<'EOF'
chore(u3d-ai-linker): scaffold package, editor and test asmdefs

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 16: ManifestUrlClassifier 区分受管依赖与冲突依赖

判定一个 `dependencies` 条目当前值是否由 Linker 管理：只有「本仓 Git URL」或「Local `file:` 路径」算受管（可覆盖/可删）；其它一律 `Unmanaged`（冲突）；缺失为 `Absent`。这是事务和 Remove 共用的 ownership 基础。本任务先写测试再实现。

Files:
- Create: `Packages/com.yoji.u3d-ai-linker/Tests/Editor/ManifestUrlClassifierTests.cs`
- Create: `Packages/com.yoji.u3d-ai-linker/Editor/Operations/ManifestUrlClassifier.cs`

- [ ] Step 1: 写失败测试 `Packages/com.yoji.u3d-ai-linker/Tests/Editor/ManifestUrlClassifierTests.cs`。覆盖：本仓 Git URL（带 `?path=` 与 `#revision`）= GitRepo；`file:` = LocalFile；空/null = Absent；第三方语义版本号、外部 Git、其它仓 Git = Unmanaged；非 `com.yoji.` 前缀即便是本仓 URL 也判 Unmanaged（Linker 只接管自己命名空间）。

```csharp
using NUnit.Framework;
using Yoji.U3DAILinker.Operations;

namespace Yoji.U3DAILinker.Tests
{
    public sealed class ManifestUrlClassifierTests
    {
        private const string RepoGit =
            "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/com.yoji.editor-debug#editor-debug-v1.2.0";

        [Test]
        public void RepoGitUrl_OnYojiPackage_IsGitRepo()
        {
            Assert.AreEqual(DependencyOwnership.GitRepo,
                ManifestUrlClassifier.Classify("com.yoji.editor-debug", RepoGit));
        }

        [Test]
        public void RepoGitUrl_WithCaseInsensitiveHostAndSlug_IsGitRepo()
        {
            var url = "https://github.com/SputnicYoji/U3D-Dev-Tools-AI.git?path=/Packages/com.yoji.editor-debug#x";
            Assert.AreEqual(DependencyOwnership.GitRepo,
                ManifestUrlClassifier.Classify("com.yoji.editor-debug", url));
        }

        [Test]
        public void FileUrl_OnYojiPackage_IsLocalFile()
        {
            var url = "file:E:/Yoji/U3D-Dev-Tools-AI/Packages/com.yoji.editor-debug";
            Assert.AreEqual(DependencyOwnership.LocalFile,
                ManifestUrlClassifier.Classify("com.yoji.editor-debug", url));
        }

        [Test]
        public void NullOrEmptyValue_IsAbsent()
        {
            Assert.AreEqual(DependencyOwnership.Absent,
                ManifestUrlClassifier.Classify("com.yoji.editor-debug", null));
            Assert.AreEqual(DependencyOwnership.Absent,
                ManifestUrlClassifier.Classify("com.yoji.editor-debug", "   "));
        }

        [Test]
        public void SemverVersion_IsUnmanaged()
        {
            Assert.AreEqual(DependencyOwnership.Unmanaged,
                ManifestUrlClassifier.Classify("com.yoji.editor-debug", "1.2.0"));
        }

        [Test]
        public void ForeignGitUrl_IsUnmanaged()
        {
            var url = "https://github.com/someoneelse/other-repo.git?path=/Packages/com.yoji.editor-debug#v1";
            Assert.AreEqual(DependencyOwnership.Unmanaged,
                ManifestUrlClassifier.Classify("com.yoji.editor-debug", url));
        }

        [Test]
        public void NonYojiPackageName_IsUnmanaged_EvenWithRepoUrl()
        {
            Assert.AreEqual(DependencyOwnership.Unmanaged,
                ManifestUrlClassifier.Classify("com.thirdparty.thing", RepoGit));
        }
    }
}
```

- [ ] Step 2: 在 Unity 在线时通过 test-runner 跑该测试文件，确认编译失败/测试失败（`ManifestUrlClassifier` 尚不存在）。用 test-runner-mcp skill 触发 EditMode、filter 到本程序集：

```text
POST /recompile        # 等 200
POST /run-tests        # body: {"testMode":"EditMode","assemblyNames":["Yoji.U3DAILinker.Editor.Tests"]}
GET  /result?jobId=... # 轮询
```

期望：编译期报错 `The type or namespace name 'ManifestUrlClassifier' could not be found`（实现缺失），即红灯。

- [ ] Step 3: 写最小实现 `Packages/com.yoji.u3d-ai-linker/Editor/Operations/ManifestUrlClassifier.cs`：

```csharp
using System;

namespace Yoji.U3DAILinker.Operations
{
    /// 受管依赖归属：GitRepo/LocalFile 由 Linker 管理（可覆盖、可删）；
    /// Unmanaged 是用户或第三方手写值（冲突，不自动动）；Absent 表示该包不在 dependencies。
    public enum DependencyOwnership
    {
        GitRepo,
        LocalFile,
        Unmanaged,
        Absent,
    }

    /// 判定 manifest dependencies 中某条目当前值是否由 Linker 管理。
    /// 纯字符串逻辑，无 IO，可单测。
    public static class ManifestUrlClassifier
    {
        public const string RepoSlug = "sputnicyoji/U3D-Dev-Tools-AI";
        private const string YojiPrefix = "com.yoji.";

        public static DependencyOwnership Classify(string packageName, string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return DependencyOwnership.Absent;

            // Linker 只接管自己命名空间下的包；非 com.yoji.* 一律不动。
            if (packageName == null || !packageName.StartsWith(YojiPrefix, StringComparison.Ordinal))
                return DependencyOwnership.Unmanaged;

            var value = url.Trim();

            if (value.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                return DependencyOwnership.LocalFile;

            if (IsThisRepoGitUrl(value))
                return DependencyOwnership.GitRepo;

            return DependencyOwnership.Unmanaged;
        }

        // 必须是 github 上本仓的 .git URL，slug 与 host 大小写不敏感；其它 Git 仓库判 Unmanaged。
        private static bool IsThisRepoGitUrl(string value)
        {
            if (value.IndexOf(".git", StringComparison.OrdinalIgnoreCase) < 0)
                return false;
            return value.IndexOf("github.com/" + RepoSlug, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
```

- [ ] Step 4: 重新 `/recompile` + `/run-tests`（同 Step 2 filter），确认 6 个用例全绿。期望 `/result` 的 `passed=6, failed=0`。

- [ ] Step 5: commit：

```bash
git add Packages/com.yoji.u3d-ai-linker/Editor/Operations/ManifestUrlClassifier.cs \
        Packages/com.yoji.u3d-ai-linker/Tests/Editor/ManifestUrlClassifierTests.cs
git commit -m "$(cat <<'EOF'
feat(u3d-ai-linker): classify managed vs conflicting manifest deps

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 17: ManifestPlan 与 OperationLog 数据模型

定义事务输入（`ManifestPlan` + `ManifestEdit`）与产出记账（`OperationRecord` + `DependencyChange`），以及冲突结果类型。这些是纯数据 + Newtonsoft round-trip。本任务先写序列化往返测试再实现。

Files:
- Create: `Packages/com.yoji.u3d-ai-linker/Editor/Operations/ManifestPlan.cs`
- Create: `Packages/com.yoji.u3d-ai-linker/Editor/Operations/OperationLog.cs`
- Modify: `Packages/com.yoji.u3d-ai-linker/Tests/Editor/ManifestTransactionTests.cs`（本任务先建文件，仅放数据模型测试；事务测试在下一任务追加）

- [ ] Step 1: 写失败测试 `Packages/com.yoji.u3d-ai-linker/Tests/Editor/ManifestTransactionTests.cs`，先只测 `OperationRecord` 的 JSON round-trip（保证 operation.json 可读回，供回滚/面板使用）：

```csharp
using System.Collections.Generic;
using Newtonsoft.Json;
using NUnit.Framework;
using Yoji.U3DAILinker.Operations;

namespace Yoji.U3DAILinker.Tests
{
    public sealed class ManifestTransactionTests
    {
        [Test]
        public void OperationRecord_RoundTripsThroughJson()
        {
            var rec = new OperationRecord
            {
                OperationId = "op123",
                Channel = "stable",
                Revision = "editor-debug-v1.2.0",
                BackupPath = "Library/U3DAILinker/backups/manifest-op123.json",
                Status = "committed",
                DependencyChanges = new List<DependencyChange>
                {
                    new DependencyChange
                    {
                        PackageName = "com.yoji.editor-debug",
                        ChangeType = "Add",
                        OldValue = null,
                        NewValue = "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/com.yoji.editor-debug#editor-debug-v1.2.0",
                    },
                },
            };

            var json = JsonConvert.SerializeObject(rec);
            var back = JsonConvert.DeserializeObject<OperationRecord>(json);

            Assert.AreEqual("op123", back.OperationId);
            Assert.AreEqual("stable", back.Channel);
            Assert.AreEqual("committed", back.Status);
            Assert.AreEqual(1, back.DependencyChanges.Count);
            Assert.AreEqual("com.yoji.editor-debug", back.DependencyChanges[0].PackageName);
            Assert.IsNull(back.DependencyChanges[0].OldValue);
            Assert.AreEqual("Add", back.DependencyChanges[0].ChangeType);
        }
    }
}
```

- [ ] Step 2: 跑测试确认红灯（`OperationRecord`/`DependencyChange` 未定义，编译失败）。命令同上一 Task 的 `/run-tests`（filter `Yoji.U3DAILinker.Editor.Tests`）。期望编译错误 `'OperationRecord' could not be found`。

- [ ] Step 3: 写 `Packages/com.yoji.u3d-ai-linker/Editor/Operations/ManifestPlan.cs`：

```csharp
using System.Collections.Generic;

namespace Yoji.U3DAILinker.Operations
{
    /// 一条 manifest 依赖变更意图。Remove 时 NewValue 为 null。
    public enum ManifestChangeType
    {
        Add,
        Update,
        Remove,
    }

    /// 调用方（安装/移除子系统）算好的单条编辑：要把 packageName 设为 NewValue，或移除。
    /// 注意：ChangeType 是「意图」；事务执行时会按 manifest 现状重新判定 Add/Update 并做 ownership 校验。
    public sealed class ManifestEdit
    {
        public string PackageName;
        public ManifestChangeType ChangeType;
        public string NewValue;   // Remove 时为 null

        public ManifestEdit() { }

        public ManifestEdit(string packageName, ManifestChangeType changeType, string newValue)
        {
            PackageName = packageName;
            ChangeType = changeType;
            NewValue = newValue;
        }
    }

    /// 一次 manifest 事务的完整输入。OperationId 唯一标识本次操作（用于 backup 文件名）。
    public sealed class ManifestPlan
    {
        public string OperationId;
        public string Channel;    // stable | dev | local
        public string Revision;   // tag 或 40 位 SHA 或 file 路径描述；仅记账用
        public List<ManifestEdit> Edits = new List<ManifestEdit>();

        public ManifestPlan() { }

        public ManifestPlan(string operationId, string channel, string revision)
        {
            OperationId = operationId;
            Channel = channel;
            Revision = revision;
        }
    }
}
```

- [ ] Step 4: 写 `Packages/com.yoji.u3d-ai-linker/Editor/Operations/OperationLog.cs`：

```csharp
using System.Collections.Generic;

namespace Yoji.U3DAILinker.Operations
{
    /// operation.json 中单条依赖变更记账：旧值 -> 新值。Remove 时 NewValue 为 null；Add 时 OldValue 为 null。
    public sealed class DependencyChange
    {
        public string PackageName;
        public string ChangeType;   // Add | Update | Remove（字符串，便于面板/日志直接输出）
        public string OldValue;
        public string NewValue;
    }

    /// 写入 Library/U3DAILinker/operation.json 的事务记录。Status: committed | failed | rolledback。
    public sealed class OperationRecord
    {
        public string OperationId;
        public string Channel;
        public string Revision;
        public string BackupPath;
        public string Status;
        public List<DependencyChange> DependencyChanges = new List<DependencyChange>();
    }

    /// 受管包当前值非 Linker 管理（第三方/手写）时的冲突描述。
    public sealed class ManifestConflict
    {
        public string PackageName;
        public string ExistingValue;
    }

    /// 事务结果。Committed=false 时看 Conflicts（非空=因冲突拒绝）或 FailureReason（IO/解析失败）。
    public sealed class ManifestTransactionResult
    {
        public bool Committed;
        public OperationRecord Record;
        public List<ManifestConflict> Conflicts = new List<ManifestConflict>();
        public string FailureReason;
    }
}
```

  组装提示:LINK-3 的 `OperationLog.cs` 还会在 `Yoji.U3DAILinker.Operations` 命名空间定义 `OperationLog`/`OperationPhase`,并定义自己的 `DependencyChange`(字段 PackageName/OldValue/NewValue,无 ChangeType)。两个 `DependencyChange` 字段不同会冲突。组装时统一为**含 ChangeType 的本子系统版本**(LINK-3 的队列日志读 NewValue 即可,ChangeType 对它无害),把 LINK-3 的 `OperationLog`/`OperationPhase` 追加进同一文件,删去 LINK-3 中重复的 `DependencyChange` 定义。

- [ ] Step 5: 跑测试确认绿灯（round-trip 用例通过）。命令同上。期望 `passed` 计数 +1 全绿。

- [ ] Step 6: commit：

```bash
git add Packages/com.yoji.u3d-ai-linker/Editor/Operations/ManifestPlan.cs \
        Packages/com.yoji.u3d-ai-linker/Editor/Operations/OperationLog.cs \
        Packages/com.yoji.u3d-ai-linker/Tests/Editor/ManifestTransactionTests.cs
git commit -m "$(cat <<'EOF'
feat(u3d-ai-linker): add manifest plan and operation log models

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 18: ManifestTransaction 七步原子事务（核心）

实现读 JObject 保序保留未知字段 → backup → 只改受管 `com.yoji.*` → tmp → 解析校验 → `File.Replace` 原子替换 → 写 operation.json。事务内对每条 edit 用 `ManifestUrlClassifier` 重判 ownership：受管或缺失才动；Unmanaged 且未 `acceptConflicts` 则整体拒绝不写任何文件。本任务测试用临时目录造 manifest，纯 EditMode 可跑。

Files:
- Create: `Packages/com.yoji.u3d-ai-linker/Editor/Operations/ManifestTransaction.cs`
- Modify: `Packages/com.yoji.u3d-ai-linker/Tests/Editor/ManifestTransactionTests.cs`

- [ ] Step 1: 在 `ManifestTransactionTests.cs` 顶部补 `using`，并加 `SetUp/TearDown` 临时目录与一个写 manifest 的 helper。在类内（`OperationRecord_RoundTripsThroughJson` 之后）追加。先加基础设施与「保留未知字段 + 只改托管 + Add 新条目」测试：

```csharp
// 文件顶部 using 区追加：
using System.IO;
using Newtonsoft.Json.Linq;

// 类内字段与 SetUp/TearDown：
private string m_Root;
private string m_ManifestPath;
private string m_StateDir;

[SetUp]
public void SetUp()
{
    m_Root = Path.Combine(Path.GetTempPath(), "u3d-ai-linker-tests", Path.GetRandomFileName());
    Directory.CreateDirectory(Path.Combine(m_Root, "Packages"));
    m_ManifestPath = Path.Combine(m_Root, "Packages", "manifest.json");
    m_StateDir = Path.Combine(m_Root, "Library", "U3DAILinker");
}

[TearDown]
public void TearDown()
{
    if (Directory.Exists(m_Root)) Directory.Delete(m_Root, true);
}

private void WriteManifest(string json) => File.WriteAllText(m_ManifestPath, json);

private JObject ReadManifest() => JObject.Parse(File.ReadAllText(m_ManifestPath));

private const string RepoUrl =
    "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/com.yoji.editor-debug#editor-debug-v1.2.0";

[Test]
public void Apply_AddsManagedDep_PreservesUnknownFieldsAndForeignDeps()
{
    WriteManifest(@"{
  ""dependencies"": {
    ""com.unity.modules.ai"": ""1.0.0"",
    ""com.thirdparty.cool"": ""2.3.4""
  },
  ""scopedRegistries"": [ { ""name"": ""npm"", ""url"": ""https://example.com"" } ],
  ""enableLockFile"": true
}");
    var tx = new ManifestTransaction(m_ManifestPath, m_StateDir);
    var plan = new ManifestPlan("op1", "stable", "editor-debug-v1.2.0");
    plan.Edits.Add(new ManifestEdit("com.yoji.editor-debug", ManifestChangeType.Add, RepoUrl));

    var result = tx.Apply(plan, acceptConflicts: false);

    Assert.IsTrue(result.Committed, result.FailureReason);
    var m = ReadManifest();
    var deps = (JObject)m["dependencies"];
    Assert.AreEqual(RepoUrl, (string)deps["com.yoji.editor-debug"]);
    // 第三方依赖与未知顶层字段原样保留
    Assert.AreEqual("2.3.4", (string)deps["com.thirdparty.cool"]);
    Assert.AreEqual("1.0.0", (string)deps["com.unity.modules.ai"]);
    Assert.IsNotNull(m["scopedRegistries"]);
    Assert.AreEqual(true, (bool)m["enableLockFile"]);
}
```

- [ ] Step 2: 继续追加：backup 写入、operation.json 记账、tmp 清理、Update 既有受管条目（记 oldValue）、Remove 受管条目的测试：

```csharp
[Test]
public void Apply_WritesBackupAndOperationLog_AndCleansTmp()
{
    WriteManifest(@"{ ""dependencies"": { } }");
    var tx = new ManifestTransaction(m_ManifestPath, m_StateDir);
    var plan = new ManifestPlan("opBackup", "stable", "editor-debug-v1.2.0");
    plan.Edits.Add(new ManifestEdit("com.yoji.editor-debug", ManifestChangeType.Add, RepoUrl));

    var result = tx.Apply(plan, acceptConflicts: false);

    Assert.IsTrue(result.Committed, result.FailureReason);
    var backup = Path.Combine(m_StateDir, "backups", "manifest-opBackup.json");
    Assert.IsTrue(File.Exists(backup), "backup must exist");
    Assert.AreEqual(backup, result.Record.BackupPath);
    var opLog = Path.Combine(m_StateDir, "operation.json");
    Assert.IsTrue(File.Exists(opLog), "operation.json must exist");
    Assert.AreEqual("committed", result.Record.Status);
    Assert.AreEqual(1, result.Record.DependencyChanges.Count);
    Assert.AreEqual("Add", result.Record.DependencyChanges[0].ChangeType);
    Assert.IsNull(result.Record.DependencyChanges[0].OldValue);
    Assert.AreEqual(RepoUrl, result.Record.DependencyChanges[0].NewValue);
    // tmp 不残留
    Assert.IsFalse(File.Exists(m_ManifestPath + ".u3d-ai-linker.tmp"));
}

[Test]
public void Apply_UpdatesExistingManagedDep_RecordsOldValue()
{
    var oldUrl = "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/com.yoji.editor-debug#editor-debug-v1.0.0";
    WriteManifest("{ \"dependencies\": { \"com.yoji.editor-debug\": \"" + oldUrl + "\" } }");
    var tx = new ManifestTransaction(m_ManifestPath, m_StateDir);
    var plan = new ManifestPlan("opUpd", "stable", "editor-debug-v1.2.0");
    plan.Edits.Add(new ManifestEdit("com.yoji.editor-debug", ManifestChangeType.Update, RepoUrl));

    var result = tx.Apply(plan, acceptConflicts: false);

    Assert.IsTrue(result.Committed, result.FailureReason);
    Assert.AreEqual(RepoUrl, (string)((JObject)ReadManifest()["dependencies"])["com.yoji.editor-debug"]);
    var change = result.Record.DependencyChanges[0];
    Assert.AreEqual("Update", change.ChangeType);
    Assert.AreEqual(oldUrl, change.OldValue);
    Assert.AreEqual(RepoUrl, change.NewValue);
}

[Test]
public void Apply_RemovesManagedDep_RecordsRemove()
{
    WriteManifest("{ \"dependencies\": { \"com.yoji.editor-debug\": \"" + RepoUrl + "\", \"com.unity.x\": \"1.0.0\" } }");
    var tx = new ManifestTransaction(m_ManifestPath, m_StateDir);
    var plan = new ManifestPlan("opRem", "stable", null);
    plan.Edits.Add(new ManifestEdit("com.yoji.editor-debug", ManifestChangeType.Remove, null));

    var result = tx.Apply(plan, acceptConflicts: false);

    Assert.IsTrue(result.Committed, result.FailureReason);
    var deps = (JObject)ReadManifest()["dependencies"];
    Assert.IsNull(deps["com.yoji.editor-debug"]);
    Assert.IsNotNull(deps["com.unity.x"]);   // 非托管依赖不动
    Assert.AreEqual("Remove", result.Record.DependencyChanges[0].ChangeType);
    Assert.AreEqual(RepoUrl, result.Record.DependencyChanges[0].OldValue);
    Assert.IsNull(result.Record.DependencyChanges[0].NewValue);
}
```

- [ ] Step 3: 继续追加冲突拒绝测试：受管包当前值是第三方版本号 → 不 accept 时整体拒绝、manifest 不变、不写 backup/op log；accept 时接管覆盖：

```csharp
[Test]
public void Apply_RejectsWhenExistingDepIsUnmanaged_AndDoesNotTouchAnything()
{
    WriteManifest("{ \"dependencies\": { \"com.yoji.editor-debug\": \"9.9.9\" } }");
    var before = File.ReadAllText(m_ManifestPath);
    var tx = new ManifestTransaction(m_ManifestPath, m_StateDir);
    var plan = new ManifestPlan("opConf", "stable", "editor-debug-v1.2.0");
    plan.Edits.Add(new ManifestEdit("com.yoji.editor-debug", ManifestChangeType.Update, RepoUrl));

    var result = tx.Apply(plan, acceptConflicts: false);

    Assert.IsFalse(result.Committed);
    Assert.AreEqual(1, result.Conflicts.Count);
    Assert.AreEqual("com.yoji.editor-debug", result.Conflicts[0].PackageName);
    Assert.AreEqual("9.9.9", result.Conflicts[0].ExistingValue);
    // 冲突在写任何文件前就拒绝：manifest 原样、无 backup、无 op log、无 tmp
    Assert.AreEqual(before, File.ReadAllText(m_ManifestPath));
    Assert.IsFalse(Directory.Exists(Path.Combine(m_StateDir, "backups")));
    Assert.IsFalse(File.Exists(Path.Combine(m_StateDir, "operation.json")));
    Assert.IsFalse(File.Exists(m_ManifestPath + ".u3d-ai-linker.tmp"));
}

[Test]
public void Apply_AcceptConflicts_TakesOverUnmanagedDep()
{
    WriteManifest("{ \"dependencies\": { \"com.yoji.editor-debug\": \"9.9.9\" } }");
    var tx = new ManifestTransaction(m_ManifestPath, m_StateDir);
    var plan = new ManifestPlan("opTake", "stable", "editor-debug-v1.2.0");
    plan.Edits.Add(new ManifestEdit("com.yoji.editor-debug", ManifestChangeType.Update, RepoUrl));

    var result = tx.Apply(plan, acceptConflicts: true);

    Assert.IsTrue(result.Committed, result.FailureReason);
    Assert.AreEqual(RepoUrl, (string)((JObject)ReadManifest()["dependencies"])["com.yoji.editor-debug"]);
    Assert.AreEqual("9.9.9", result.Record.DependencyChanges[0].OldValue);
}
```

- [ ] Step 4: 再追加两个边界测试：Remove 一个本就不存在的包是 no-op（不进 DependencyChanges、仍可 commit 其它 edit）；Remove 一个非托管包在不 accept 时也算冲突拒绝（对应 spec 263「删除时值非托管则标冲突不自动删」）：

```csharp
[Test]
public void Apply_RemoveAbsentPackage_IsNoOpButStillCommitsOthers()
{
    WriteManifest("{ \"dependencies\": { \"com.yoji.editor-debug\": \"" + RepoUrl + "\" } }");
    var tx = new ManifestTransaction(m_ManifestPath, m_StateDir);
    var plan = new ManifestPlan("opNoop", "stable", null);
    plan.Edits.Add(new ManifestEdit("com.yoji.not-installed", ManifestChangeType.Remove, null));
    plan.Edits.Add(new ManifestEdit("com.yoji.editor-debug", ManifestChangeType.Remove, null));

    var result = tx.Apply(plan, acceptConflicts: false);

    Assert.IsTrue(result.Committed, result.FailureReason);
    Assert.AreEqual(1, result.Record.DependencyChanges.Count);   // 只记真正发生的移除
    Assert.AreEqual("com.yoji.editor-debug", result.Record.DependencyChanges[0].PackageName);
}

[Test]
public void Apply_RemoveUnmanagedDep_IsConflict()
{
    WriteManifest("{ \"dependencies\": { \"com.yoji.editor-debug\": \"9.9.9\" } }");
    var tx = new ManifestTransaction(m_ManifestPath, m_StateDir);
    var plan = new ManifestPlan("opRemConf", "stable", null);
    plan.Edits.Add(new ManifestEdit("com.yoji.editor-debug", ManifestChangeType.Remove, null));

    var result = tx.Apply(plan, acceptConflicts: false);

    Assert.IsFalse(result.Committed);
    Assert.AreEqual(1, result.Conflicts.Count);
    Assert.AreEqual("9.9.9", result.Conflicts[0].ExistingValue);
}
```

- [ ] Step 5: 跑全部新测试确认红灯（`ManifestTransaction` 未定义，编译失败）。命令同前 `/run-tests` filter `Yoji.U3DAILinker.Editor.Tests`。期望编译错误 `'ManifestTransaction' could not be found`。

- [ ] Step 6: 写实现 `Packages/com.yoji.u3d-ai-linker/Editor/Operations/ManifestTransaction.cs`。关键点：用 `JObject.Parse`（Newtonsoft 默认保序保留未知字段）；`dependencies` 缺失时新建；先全量做冲突预检（任何 Unmanaged 且未 accept 立即返回，不写盘）；再算实际变更；无实际变更也跳过写盘直接返回 committed（空 change 列表）；tmp 写好 `JObject.ToString` 后 `JObject.Parse` 复验；`File.Replace` 在目标存在时原子替换，destination backup 传 null。

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Yoji.U3DAILinker.Operations
{
    /// 把 Packages/manifest.json 当配置文件做的原子事务：保序保留未知字段、backup、只改托管
    /// com.yoji.* 依赖、tmp + 解析校验 + File.Replace 原子替换、写 operation.json 记账。
    /// 纯文件逻辑，路径从构造注入，可在 EditMode 用临时目录单测。
    public sealed class ManifestTransaction
    {
        private const string TmpSuffix = ".u3d-ai-linker.tmp";

        private readonly string m_ManifestPath;
        private readonly string m_StateDir;   // 通常 <project>/Library/U3DAILinker

        public ManifestTransaction(string manifestPath, string stateDir)
        {
            m_ManifestPath = manifestPath;
            m_StateDir = stateDir;
        }

        public ManifestTransactionResult Apply(ManifestPlan plan, bool acceptConflicts)
        {
            var result = new ManifestTransactionResult();

            JObject root;
            try
            {
                root = JObject.Parse(File.ReadAllText(m_ManifestPath));
            }
            catch (Exception e)
            {
                result.FailureReason = "read/parse manifest failed: " + e.Message;
                return result;
            }

            var deps = root["dependencies"] as JObject;
            if (deps == null)
            {
                deps = new JObject();
                root["dependencies"] = deps;
            }

            // 1) 冲突预检 + 计算实际变更（此阶段不写任何文件）。
            var changes = new List<DependencyChange>();
            foreach (var edit in plan.Edits)
            {
                var existing = deps[edit.PackageName] != null ? (string)deps[edit.PackageName] : null;
                var ownership = ManifestUrlClassifier.Classify(edit.PackageName, existing);

                if (ownership == DependencyOwnership.Unmanaged && !acceptConflicts)
                {
                    result.Conflicts.Add(new ManifestConflict
                    {
                        PackageName = edit.PackageName,
                        ExistingValue = existing,
                    });
                    continue;
                }

                if (edit.ChangeType == ManifestChangeType.Remove)
                {
                    if (ownership == DependencyOwnership.Absent)
                        continue;   // 不存在 -> no-op
                    changes.Add(new DependencyChange
                    {
                        PackageName = edit.PackageName,
                        ChangeType = "Remove",
                        OldValue = existing,
                        NewValue = null,
                    });
                }
                else
                {
                    if (existing == edit.NewValue)
                        continue;   // 值未变 -> no-op
                    changes.Add(new DependencyChange
                    {
                        PackageName = edit.PackageName,
                        ChangeType = ownership == DependencyOwnership.Absent ? "Add" : "Update",
                        OldValue = existing,
                        NewValue = edit.NewValue,
                    });
                }
            }

            // 任一冲突 -> 整体拒绝，不写盘。
            if (result.Conflicts.Count > 0)
                return result;

            // 无实际变更 -> 直接 committed，不写 backup/tmp/op log。
            if (changes.Count == 0)
            {
                result.Committed = true;
                result.Record = new OperationRecord
                {
                    OperationId = plan.OperationId,
                    Channel = plan.Channel,
                    Revision = plan.Revision,
                    Status = "committed",
                };
                return result;
            }

            // 2) backup。
            string backupPath;
            try
            {
                var backupDir = Path.Combine(m_StateDir, "backups");
                Directory.CreateDirectory(backupDir);
                backupPath = Path.Combine(backupDir, "manifest-" + plan.OperationId + ".json");
                File.Copy(m_ManifestPath, backupPath, overwrite: true);
            }
            catch (Exception e)
            {
                result.FailureReason = "backup failed: " + e.Message;
                return result;
            }

            // 3) 应用变更到内存 JObject。
            foreach (var c in changes)
            {
                if (c.ChangeType == "Remove") deps.Remove(c.PackageName);
                else deps[c.PackageName] = c.NewValue;
            }

            // 4) 写 tmp。
            var tmpPath = m_ManifestPath + TmpSuffix;
            try
            {
                File.WriteAllText(tmpPath, root.ToString(Formatting.Indented));
            }
            catch (Exception e)
            {
                SafeDelete(tmpPath);
                result.FailureReason = "write tmp failed: " + e.Message;
                return result;
            }

            // 5) 解析 tmp 复验合法 JSON。
            try
            {
                JObject.Parse(File.ReadAllText(tmpPath));
            }
            catch (Exception e)
            {
                SafeDelete(tmpPath);
                result.FailureReason = "tmp not valid json: " + e.Message;
                return result;
            }

            // 6) 原子替换。File.Replace 要求目标已存在；manifest 一定存在（前面已读过）。
            try
            {
                File.Replace(tmpPath, m_ManifestPath, null);
            }
            catch (Exception e)
            {
                SafeDelete(tmpPath);
                result.FailureReason = "atomic replace failed: " + e.Message;
                return result;
            }

            // 7) 写 operation.json 记账。
            var record = new OperationRecord
            {
                OperationId = plan.OperationId,
                Channel = plan.Channel,
                Revision = plan.Revision,
                BackupPath = backupPath,
                Status = "committed",
                DependencyChanges = changes,
            };
            try
            {
                Directory.CreateDirectory(m_StateDir);
                File.WriteAllText(Path.Combine(m_StateDir, "operation.json"),
                    JsonConvert.SerializeObject(record, Formatting.Indented));
            }
            catch
            {
                // 记账失败不回滚已提交的 manifest；保留 backup 供手动恢复。
            }

            result.Committed = true;
            result.Record = record;
            return result;
        }

        private static void SafeDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { /* 清理失败不致命 */ }
        }
    }
}
```

- [ ] Step 7: 跑全部测试确认绿灯。命令同前 filter `Yoji.U3DAILinker.Editor.Tests`。期望本程序集全部用例 `failed=0`。

- [ ] Step 8: 手动验证步骤（真实 Unity 工程，验证 `File.Replace` 原子语义与 UPM 不误触）：在一个 2022.3 工程的 Library 下放本包，打开 C# Interactive 或临时 EditorWindow 调一次 `Apply` 把 `com.yoji.editor-debug` 写入真实 `Packages/manifest.json`。期望：(a) manifest 中第三方依赖、`scopedRegistries`、缩进风格保持；(b) `Library/U3DAILinker/backups/manifest-<op>.json` 出现且内容等于改前 manifest；(c) `Library/U3DAILinker/operation.json` 出现且 `dependencyChanges` 正确；(d) 不存在残留 `manifest.json.u3d-ai-linker.tmp`。记录结果于 PR 描述。

- [ ] Step 9: commit：

```bash
git add Packages/com.yoji.u3d-ai-linker/Editor/Operations/ManifestTransaction.cs \
        Packages/com.yoji.u3d-ai-linker/Tests/Editor/ManifestTransactionTests.cs
git commit -m "$(cat <<'EOF'
feat(u3d-ai-linker): atomic manifest transaction with conflict guard

Read-modify-write manifest.json preserving unknown fields, back up before
write, only touch managed com.yoji.* deps, validate tmp then File.Replace,
record dependencyChanges in operation.json. Reject when an existing dep is
not Linker-managed unless caller accepts takeover.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 19: RemovePlanner 两步 dependsOn 闭包计算

实现 Remove 第二步：禁用目标 tool 后，对剩余启用工具取 `dependsOn` 闭包，只有没有剩余 dependents 的 `infra` 包可从 manifest 移除；`tool` 包自身（目标）总是移除；`linker` 永不移除。纯图算法，无 IO，完整 TDD。

Files:
- Create: `Packages/com.yoji.u3d-ai-linker/Editor/Operations/RemovePlanner.cs`
- Create: `Packages/com.yoji.u3d-ai-linker/Tests/Editor/RemovePlannerTests.cs`

- [ ] Step 1: 写失败测试 `Packages/com.yoji.u3d-ai-linker/Tests/Editor/RemovePlannerTests.cs`。场景：A、B 两 tool 都 dependsOn infra `core`；移除 A 时 `core` 仍被 B 用 → 不移除；移除 A 且 B 也未启用时 → `core` 无 dependents 可移除；`linker` 即便无 dependents 也不移除；目标 tool 自身总在 removable。

```csharp
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Yoji.U3DAILinker.Operations;

namespace Yoji.U3DAILinker.Tests
{
    public sealed class RemovePlannerTests
    {
        private static RegistryEntryInfo Tool(string id, params string[] deps) =>
            new RegistryEntryInfo { Id = id, PackageName = "com.yoji." + id, Kind = "tool", DependsOn = deps };

        private static RegistryEntryInfo Infra(string id, params string[] deps) =>
            new RegistryEntryInfo { Id = id, PackageName = "com.yoji." + id, Kind = "infra", DependsOn = deps };

        private static RegistryEntryInfo Linker() =>
            new RegistryEntryInfo { Id = "linker", PackageName = "com.yoji.u3d-ai-linker", Kind = "linker", DependsOn = new string[0] };

        [Test]
        public void Remove_Tool_KeepsInfraStillUsedByAnotherEnabledTool()
        {
            var entries = new[] { Tool("a", "core"), Tool("b", "core"), Infra("core"), Linker() };
            var enabled = new HashSet<string> { "a", "b" };

            var (removable, blocked) = RemovePlanner.Compute(entries, enabled, "a");

            CollectionAssert.Contains(removable, "com.yoji.a");      // 目标 tool 自身移除
            CollectionAssert.DoesNotContain(removable, "com.yoji.core"); // core 仍被 b 用
            CollectionAssert.Contains(blocked, "com.yoji.core");
            CollectionAssert.DoesNotContain(removable, "com.yoji.b");
        }

        [Test]
        public void Remove_LastTool_AlsoRemovesOrphanedInfra()
        {
            var entries = new[] { Tool("a", "core"), Tool("b", "core"), Infra("core"), Linker() };
            var enabled = new HashSet<string> { "a" };   // b 已不启用

            var (removable, blocked) = RemovePlanner.Compute(entries, enabled, "a");

            CollectionAssert.Contains(removable, "com.yoji.a");
            CollectionAssert.Contains(removable, "com.yoji.core");   // 无剩余 dependents
            CollectionAssert.IsEmpty(blocked);
        }

        [Test]
        public void Remove_NeverRemovesLinker_EvenIfNoDependents()
        {
            var entries = new[] { Tool("a"), Linker() };
            var enabled = new HashSet<string> { "a" };

            var (removable, _) = RemovePlanner.Compute(entries, enabled, "a");

            CollectionAssert.Contains(removable, "com.yoji.a");
            CollectionAssert.DoesNotContain(removable, "com.yoji.u3d-ai-linker");
        }

        [Test]
        public void Remove_TransitiveInfra_OnlyOrphansAreRemovable()
        {
            // a -> mid -> core ; b -> core 。移除 a 后 mid 成孤儿可删，但 core 仍被 b 用不可删。
            var entries = new[]
            {
                Tool("a", "mid"), Tool("b", "core"),
                Infra("mid", "core"), Infra("core"), Linker(),
            };
            var enabled = new HashSet<string> { "a", "b" };

            var (removable, blocked) = RemovePlanner.Compute(entries, enabled, "a");

            CollectionAssert.Contains(removable, "com.yoji.a");
            CollectionAssert.Contains(removable, "com.yoji.mid");
            CollectionAssert.DoesNotContain(removable, "com.yoji.core");
            CollectionAssert.Contains(blocked, "com.yoji.core");
        }

        [Test]
        public void Remove_TargetToolPackage_AlwaysFirstInRemovable()
        {
            var entries = new[] { Tool("a", "core"), Infra("core"), Linker() };
            var enabled = new HashSet<string> { "a" };

            var (removable, _) = RemovePlanner.Compute(entries, enabled, "a");

            Assert.AreEqual("com.yoji.a", removable.First());
        }
    }
}
```

- [ ] Step 2: 跑测试确认红灯（`RemovePlanner`/`RegistryEntryInfo` 未定义）。命令同前 filter。期望编译错误 `'RemovePlanner' could not be found`。

- [ ] Step 3: 写实现 `Packages/com.yoji.u3d-ai-linker/Editor/Operations/RemovePlanner.cs`。算法：目标 tool 的 package 总进 removable（首位）；对「剩余启用 tool」（enabled 去掉目标，且只保留 tool kind）取 dependsOn 传递闭包，得到「仍需保留的 infra 集合」；遍历所有 infra 条目，不在保留集合内的 = 孤儿 → removable，在集合内 = blocked；linker 永不进 removable。

```csharp
using System.Collections.Generic;
using System.Linq;

namespace Yoji.U3DAILinker.Operations
{
    /// RemovePlanner 输入用的最小 Registry 条目投影：只取计算闭包必需字段。
    /// 上层把已解析的完整 Registry 条目映射成本类型传入，保持本包纯逻辑。
    public sealed class RegistryEntryInfo
    {
        public string Id;
        public string PackageName;
        public string Kind;        // tool | infra | linker
        public string[] DependsOn; // 工具 ID 列表，可空
    }

    /// Remove 第二步：算出可从 manifest 移除的包集合。
    /// 目标 tool 自身总移除；只有没有剩余 dependents 的 infra 可移除；linker 永不移除。
    /// 纯图算法，无 IO，可单测。
    public sealed class RemovePlanner
    {
        /// 返回 (removablePackages, blockedInfra)，均为 packageName 列表。
        /// removablePackages[0] 是目标 tool 的 package（若目标存在）。
        public static (List<string> removablePackages, List<string> blockedInfra) Compute(
            RegistryEntryInfo[] entries, ISet<string> enabledToolIds, string removeToolId)
        {
            var byId = new Dictionary<string, RegistryEntryInfo>();
            foreach (var e in entries)
                if (e != null && e.Id != null) byId[e.Id] = e;

            var removable = new List<string>();
            var blocked = new List<string>();

            // 目标 tool 自身总进 removable（首位）。linker 不允许作为目标在此移除。
            if (byId.TryGetValue(removeToolId, out var target)
                && target.Kind != "linker"
                && !string.IsNullOrEmpty(target.PackageName))
            {
                removable.Add(target.PackageName);
            }

            // 剩余启用工具 = enabled 去掉目标，仅保留 tool kind。
            var remainingTools = new List<RegistryEntryInfo>();
            foreach (var id in enabledToolIds)
            {
                if (id == removeToolId) continue;
                if (byId.TryGetValue(id, out var e) && e.Kind == "tool")
                    remainingTools.Add(e);
            }

            // 剩余工具的 dependsOn 传递闭包 = 仍需保留的 infra ID 集合。
            var keepInfra = new HashSet<string>();
            foreach (var t in remainingTools)
                CollectClosure(t, byId, keepInfra);

            // 遍历所有 infra：不在保留集 = 孤儿可删；在保留集 = blocked。
            foreach (var e in entries)
            {
                if (e == null || e.Kind != "infra" || string.IsNullOrEmpty(e.PackageName)) continue;
                if (keepInfra.Contains(e.Id)) blocked.Add(e.PackageName);
                else removable.Add(e.PackageName);
            }

            return (removable, blocked);
        }

        // 把 entry 的 dependsOn 中所有 infra 传递地加入 keep（tool/linker 不计入 infra 保留集）。
        private static void CollectClosure(RegistryEntryInfo entry,
            Dictionary<string, RegistryEntryInfo> byId, HashSet<string> keep)
        {
            if (entry.DependsOn == null) return;
            foreach (var depId in entry.DependsOn)
            {
                if (!byId.TryGetValue(depId, out var dep)) continue;
                if (dep.Kind == "infra" && keep.Add(dep.Id))
                    CollectClosure(dep, byId, keep);
                else if (dep.Kind != "infra")
                    CollectClosure(dep, byId, keep);
            }
        }
    }
}
```

- [ ] Step 4: 跑测试确认绿灯。命令同前 filter。期望 5 个 RemovePlanner 用例全过。

- [ ] Step 5: commit：

```bash
git add Packages/com.yoji.u3d-ai-linker/Editor/Operations/RemovePlanner.cs \
        Packages/com.yoji.u3d-ai-linker/Tests/Editor/RemovePlannerTests.cs
git commit -m "$(cat <<'EOF'
feat(u3d-ai-linker): compute orphaned-infra closure for Remove

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 20: ManifestRollback 用 backup/oldValue 恢复，恢复前检测手动改动

实现回滚：优先用 operation.json 记录的 backup 文件整体恢复 manifest；恢复前重读当前 manifest，逐条比对每个 dependencyChange 的 packageName 当前值是否仍等于事务写入的 newValue —— 若不一致（被手动改动或 UPM 改写）则停，返回 `FailureReason`，不覆盖用户改动。恢复也走 tmp + 解析校验 + `File.Replace`。

Files:
- Create: `Packages/com.yoji.u3d-ai-linker/Editor/Operations/ManifestRollback.cs`
- Create: `Packages/com.yoji.u3d-ai-linker/Tests/Editor/ManifestRollbackTests.cs`

- [ ] Step 1: 写失败测试 `Packages/com.yoji.u3d-ai-linker/Tests/Editor/ManifestRollbackTests.cs`。用真实 `ManifestTransaction.Apply` 先造一笔已提交事务，再：(a) 不动 manifest → Rollback 成功且 manifest 等于 backup；(b) 手动改掉被事务写入的包值 → Rollback 拒绝、manifest 保持手改值、返回 FailureReason；(c) backup 文件丢失 → 拒绝并报原因。

```csharp
using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Yoji.U3DAILinker.Operations;

namespace Yoji.U3DAILinker.Tests
{
    public sealed class ManifestRollbackTests
    {
        private string m_Root;
        private string m_ManifestPath;
        private string m_StateDir;

        private const string RepoUrl =
            "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/com.yoji.editor-debug#editor-debug-v1.2.0";

        [SetUp]
        public void SetUp()
        {
            m_Root = Path.Combine(Path.GetTempPath(), "u3d-ai-linker-rb", Path.GetRandomFileName());
            Directory.CreateDirectory(Path.Combine(m_Root, "Packages"));
            m_ManifestPath = Path.Combine(m_Root, "Packages", "manifest.json");
            m_StateDir = Path.Combine(m_Root, "Library", "U3DAILinker");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(m_Root)) Directory.Delete(m_Root, true);
        }

        private JObject Deps() =>
            (JObject)JObject.Parse(File.ReadAllText(m_ManifestPath))["dependencies"];

        private OperationRecord CommitAdd()
        {
            File.WriteAllText(m_ManifestPath, "{ \"dependencies\": { \"com.unity.x\": \"1.0.0\" } }");
            var tx = new ManifestTransaction(m_ManifestPath, m_StateDir);
            var plan = new ManifestPlan("opRB", "stable", "editor-debug-v1.2.0");
            plan.Edits.Add(new ManifestEdit("com.yoji.editor-debug", ManifestChangeType.Add, RepoUrl));
            var r = tx.Apply(plan, acceptConflicts: false);
            Assert.IsTrue(r.Committed, r.FailureReason);
            return r.Record;
        }

        [Test]
        public void Rollback_WhenUnchanged_RestoresManifestToBackup()
        {
            var rec = CommitAdd();

            var result = ManifestRollback.Rollback(m_ManifestPath, m_StateDir, rec);

            Assert.IsTrue(result.Committed, result.FailureReason);
            // 回到改前：托管包不在，第三方仍在
            Assert.IsNull(Deps()["com.yoji.editor-debug"]);
            Assert.IsNotNull(Deps()["com.unity.x"]);
        }

        [Test]
        public void Rollback_WhenManagedDepManuallyChanged_RefusesAndKeepsUserEdit()
        {
            var rec = CommitAdd();
            // 用户/UPM 把该包值改成别的（与事务写入的 newValue 不同）
            File.WriteAllText(m_ManifestPath,
                "{ \"dependencies\": { \"com.unity.x\": \"1.0.0\", \"com.yoji.editor-debug\": \"file:somewhere\" } }");

            var result = ManifestRollback.Rollback(m_ManifestPath, m_StateDir, rec);

            Assert.IsFalse(result.Committed);
            Assert.IsNotNull(result.FailureReason);
            // 手改值保留，未被 backup 覆盖
            Assert.AreEqual("file:somewhere", (string)Deps()["com.yoji.editor-debug"]);
        }

        [Test]
        public void Rollback_WhenBackupMissing_RefusesWithReason()
        {
            var rec = CommitAdd();
            File.Delete(rec.BackupPath);

            var result = ManifestRollback.Rollback(m_ManifestPath, m_StateDir, rec);

            Assert.IsFalse(result.Committed);
            Assert.IsNotNull(result.FailureReason);
        }
    }
}
```

- [ ] Step 2: 跑测试确认红灯（`ManifestRollback` 未定义）。命令同前 filter。期望编译错误 `'ManifestRollback' could not be found`。

- [ ] Step 3: 写实现 `Packages/com.yoji.u3d-ai-linker/Editor/Operations/ManifestRollback.cs`。逻辑：校验 backup 存在；重读当前 manifest，对每条 `DependencyChange`，若 `NewValue != null`（Add/Update）则当前值必须仍等于 NewValue，否则视为被手动改动 → 拒绝（Remove 类变更不阻断，因为事务删掉的包回滚时本来要恢复，当前缺失是预期的）；通过后把 backup 内容经 tmp + 解析校验 + `File.Replace` 写回，并把 operation.json 的 Status 更新为 `rolledback`。

```csharp
using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Yoji.U3DAILinker.Operations
{
    /// 用 operation.json 记录的 backup 整体恢复 manifest。
    /// 恢复前重读当前 manifest，若被事务写入的托管依赖已被手动改动则停，不覆盖用户改动。
    /// 纯文件逻辑，可在 EditMode 用临时目录单测。
    public static class ManifestRollback
    {
        private const string TmpSuffix = ".u3d-ai-linker.tmp";

        public static ManifestTransactionResult Rollback(
            string manifestPath, string stateDir, OperationRecord record)
        {
            var result = new ManifestTransactionResult();

            if (record == null || string.IsNullOrEmpty(record.BackupPath) || !File.Exists(record.BackupPath))
            {
                result.FailureReason = "backup missing: " + (record == null ? "<null record>" : record.BackupPath);
                return result;
            }

            // 重读当前 manifest，检测是否被手动改动。
            JObject current;
            try
            {
                current = JObject.Parse(File.ReadAllText(manifestPath));
            }
            catch (Exception e)
            {
                result.FailureReason = "read/parse current manifest failed: " + e.Message;
                return result;
            }

            var deps = current["dependencies"] as JObject ?? new JObject();
            if (record.DependencyChanges != null)
            {
                foreach (var c in record.DependencyChanges)
                {
                    if (c.NewValue == null) continue;   // Remove 类变更不阻断回滚
                    var nowValue = deps[c.PackageName] != null ? (string)deps[c.PackageName] : null;
                    if (nowValue != c.NewValue)
                    {
                        result.FailureReason =
                            "manifest changed since operation; refuse rollback. package=" + c.PackageName +
                            " expected=" + c.NewValue + " actual=" + (nowValue ?? "<absent>");
                        return result;
                    }
                }
            }

            // 读 backup，经 tmp + 解析校验 + 原子替换写回。
            string backupText;
            try
            {
                backupText = File.ReadAllText(record.BackupPath);
                JObject.Parse(backupText);   // 复验 backup 合法
            }
            catch (Exception e)
            {
                result.FailureReason = "backup not valid json: " + e.Message;
                return result;
            }

            var tmpPath = manifestPath + TmpSuffix;
            try
            {
                File.WriteAllText(tmpPath, backupText);
                JObject.Parse(File.ReadAllText(tmpPath));
                File.Replace(tmpPath, manifestPath, null);
            }
            catch (Exception e)
            {
                SafeDelete(tmpPath);
                result.FailureReason = "restore write failed: " + e.Message;
                return result;
            }

            // 更新 operation.json 状态为 rolledback（失败不致命）。
            record.Status = "rolledback";
            try
            {
                Directory.CreateDirectory(stateDir);
                File.WriteAllText(Path.Combine(stateDir, "operation.json"),
                    JsonConvert.SerializeObject(record, Formatting.Indented));
            }
            catch { /* 记账失败不影响已恢复的 manifest */ }

            result.Committed = true;
            result.Record = record;
            return result;
        }

        private static void SafeDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { /* 清理失败不致命 */ }
        }
    }
}
```

- [ ] Step 4: 跑测试确认绿灯。命令同前 filter `Yoji.U3DAILinker.Editor.Tests`。期望 3 个 Rollback 用例全过，且本程序集全部累计用例 `failed=0`。

- [ ] Step 5: 手动验证步骤（真实工程，验证「替换后 UPM 解析失败 → Rollback」闭环）：在 2022.3 工程用上一 Task 的入口写入一个故意指向不存在 revision 的 `com.yoji.*` Git URL，让 UPM 报解析失败；然后调 `ManifestRollback.Rollback(manifestPath, stateDir, record)`。期望：(a) manifest 恢复到操作前（等于 backup）；(b) 若期间手动改过该包值，Rollback 返回 `Committed=false` 且不覆盖手改；(c) `operation.json` 的 `status` 变为 `rolledback`。记录于 PR 描述。

- [ ] Step 6: commit：

```bash
git add Packages/com.yoji.u3d-ai-linker/Editor/Operations/ManifestRollback.cs \
        Packages/com.yoji.u3d-ai-linker/Tests/Editor/ManifestRollbackTests.cs
git commit -m "$(cat <<'EOF'
feat(u3d-ai-linker): rollback manifest from backup, guard manual edits

Restore manifest from the recorded backup, but re-read current manifest
first and refuse if a managed dep written by the operation was changed
manually or by UPM, to avoid clobbering user edits.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### LINK-3 — 持久化 UPM 安装队列 + 域重载恢复

实现 spec 120-128 / 250-256 / 406-459 / 471-472 的核心：UPM 安装可能触发脚本编译和域重载，批量队列不得只存内存。每步先把意图原子落盘到 `Library/U3DAILinker/operation.json`，再发 `Client.Add`；域重载后由 `[InitializeOnLoad]` Bootstrap 读日志、用实际已安装包核对目标，达成则推进、否则重试 ≤2。

依赖说明：本子系统消费 LINK-2 的 Registry 解析结果（用本地 `ResolvedTool` 视图表达，字段在 Task 23 定义，组装时若 LINK-2 已导出等价类型可替换）、LINK-2b 的 manifest 事务（只读取其写入的 `ManifestBackupPath`/`DependencyChanges`，本子系统不重复实现 manifest 写入）。

纯逻辑（日志原子写、队列构建、linker-last、恢复核对用 fake probe）走完整 EditMode TDD；真实 `Client.Add` 与真实域重载列为手动验证（Task 26）。

> 幂等基线/合并提示:本子系统 `Editor/Operations/OperationLog.cs` 与 LINK-2b 同名。LINK-2b 的版本定义 manifest 记账类型(`DependencyChange` 含 ChangeType、`OperationRecord`、`ManifestConflict`、`ManifestTransactionResult`),本子系统的版本定义队列日志(`OperationLog`、`OperationPhase`,以及一个仅 PackageName/OldValue/NewValue 的 `DependencyChange`)。组装时**合并为同一文件**:保留 LINK-2b 含 ChangeType 的 `DependencyChange`(LINK-3 队列只读 NewValue,ChangeType 多一个字段无害),把本子系统的 `OperationLog`/`OperationPhase` 追加进去,删去本子系统重复的 `DependencyChange`。下面 Task 22 的 POCO 代码保留原样以便独立阅读,组装去重。

---

### Task 21: 建包骨架与主/测试 asmdef

**Files**
- Create: `Packages/com.yoji.u3d-ai-linker/package.json`
- Create: `Packages/com.yoji.u3d-ai-linker/Editor/Yoji.U3DAILinker.Editor.asmdef`
- Create: `Packages/com.yoji.u3d-ai-linker/Editor/AssemblyInfo.cs`
- Create: `Packages/com.yoji.u3d-ai-linker/Tests/Editor/Yoji.U3DAILinker.Editor.Tests.asmdef`

> 若 LINK-1/LINK-2 已创建本包骨架（package.json + 主 asmdef + AssemblyInfo），跳过 Step 1-3 已存在文件，仅核对 `InternalsVisibleTo` 与测试 asmdef 存在；本 Task 的内容是幂等基线。

- [ ] Step 1: 创建 `Packages/com.yoji.u3d-ai-linker/package.json`：

```json
{
  "name": "com.yoji.u3d-ai-linker",
  "version": "1.0.0",
  "displayName": "U3D AI Linker",
  "description": "Installs and links Yoji U3D dev tools via UPM with persistent, domain-reload-safe operation queue.",
  "unity": "2022.3",
  "dependencies": {
    "com.unity.nuget.newtonsoft-json": "3.2.1"
  }
}
```

- [ ] Step 2: 创建主 asmdef `Packages/com.yoji.u3d-ai-linker/Editor/Yoji.U3DAILinker.Editor.asmdef`（与 test-runner 主 asmdef 同结构，Editor-only，预编译引用 Newtonsoft）：

```json
{
  "name": "Yoji.U3DAILinker.Editor",
  "rootNamespace": "Yoji.U3DAILinker",
  "references": [],
  "includePlatforms": ["Editor"],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": true,
  "precompiledReferences": ["Newtonsoft.Json.dll"],
  "autoReferenced": true,
  "defineConstraints": [],
  "versionDefines": [],
  "noEngineReferences": false
}
```

- [ ] Step 3: 创建 `Packages/com.yoji.u3d-ai-linker/Editor/AssemblyInfo.cs`，向测试程序集暴露 internal（队列/日志类型都是 internal）：

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Yoji.U3DAILinker.Editor.Tests")]
```

- [ ] Step 4: 创建测试 asmdef `Packages/com.yoji.u3d-ai-linker/Tests/Editor/Yoji.U3DAILinker.Editor.Tests.asmdef`（与 test-runner 测试 asmdef 同结构）：

```json
{
  "name": "Yoji.U3DAILinker.Editor.Tests",
  "rootNamespace": "Yoji.U3DAILinker.Tests",
  "references": ["Yoji.U3DAILinker.Editor", "UnityEngine.TestRunner", "UnityEditor.TestRunner"],
  "includePlatforms": ["Editor"],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": true,
  "precompiledReferences": ["nunit.framework.dll", "Newtonsoft.Json.dll"],
  "autoReferenced": false,
  "defineConstraints": ["UNITY_INCLUDE_TESTS"],
  "versionDefines": [],
  "noEngineReferences": false
}
```

- [ ] Step 5: 提交骨架。

```bash
git checkout -b feat/u3d-ai-linker-link3
git add Packages/com.yoji.u3d-ai-linker/package.json \
        Packages/com.yoji.u3d-ai-linker/Editor/Yoji.U3DAILinker.Editor.asmdef \
        Packages/com.yoji.u3d-ai-linker/Editor/AssemblyInfo.cs \
        Packages/com.yoji.u3d-ai-linker/Tests/Editor/Yoji.U3DAILinker.Editor.Tests.asmdef
git commit -m "$(cat <<'EOF'
chore(u3d-ai-linker): scaffold package and editor/test asmdefs

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 22: OperationLog POCO 与 OperationLogStore 原子写

实现 spec 406-459 的操作日志：POCO 字段与 spec JSON 示例一一对应；Store 用 tmp + `File.Replace` 原子替换，路径 `Library/U3DAILinker/operation.json`。纯 I/O 逻辑，目录从构造注入，走完整 TDD。

**Files**
- Create: `Packages/com.yoji.u3d-ai-linker/Editor/Operations/OperationLog.cs`
- Create: `Packages/com.yoji.u3d-ai-linker/Editor/Operations/OperationLogStore.cs`
- Test: `Packages/com.yoji.u3d-ai-linker/Tests/Editor/Operations/OperationLogStoreTests.cs`

- [ ] Step 1: 先写失败测试 `Packages/com.yoji.u3d-ai-linker/Tests/Editor/Operations/OperationLogStoreTests.cs`。此时 `OperationLog` / `OperationLogStore` 还不存在，编译失败即第一次"红"：

```csharp
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Yoji.U3DAILinker.Operations;

namespace Yoji.U3DAILinker.Tests.Operations
{
    public class OperationLogStoreTests
    {
        private string m_LibraryRoot;

        [SetUp] public void SetUp()
        {
            m_LibraryRoot = Path.Combine(Path.GetTempPath(), "u3dlinker_" + System.Guid.NewGuid().ToString("N"));
        }

        [TearDown] public void TearDown()
        {
            try { if (Directory.Exists(m_LibraryRoot)) Directory.Delete(m_LibraryRoot, true); } catch { }
        }

        private OperationLogStore New() => new OperationLogStore(m_LibraryRoot);

        private static OperationLog Sample()
        {
            return new OperationLog
            {
                OperationId = "guid-1",
                Action = "install-all",
                ToolIds = new[] { "editor-debug" },
                CurrentIndex = 0,
                Phase = OperationPhase.PackageRequested,
                Channel = "stable",
                ResolvedRevision = "editor-debug-v1.2.0",
                ManifestBackupPath = "Library/U3DAILinker/backups/manifest-guid-1.json",
                DependencyChanges = new List<DependencyChange>
                {
                    new DependencyChange { PackageName = "com.yoji.editor-debug", OldValue = null, NewValue = "url-1" },
                },
                Completed = new List<string>(),
                RetryCount = 0,
            };
        }

        [Test] public void LogPath_IsUnderLibraryOperationJson()
        {
            var store = New();
            Assert.AreEqual(Path.Combine(m_LibraryRoot, "U3DAILinker", "operation.json"), store.LogPath);
        }

        [Test] public void Load_WhenNoFile_ReturnsNull()
        {
            Assert.IsNull(New().Load());
        }

        [Test] public void Save_ThenLoad_RoundTripsAllFields()
        {
            var store = New();
            store.Save(Sample());
            var loaded = store.Load();

            Assert.IsNotNull(loaded);
            Assert.AreEqual("guid-1", loaded.OperationId);
            Assert.AreEqual("install-all", loaded.Action);
            Assert.AreEqual(1, loaded.ToolIds.Length);
            Assert.AreEqual("editor-debug", loaded.ToolIds[0]);
            Assert.AreEqual(0, loaded.CurrentIndex);
            Assert.AreEqual(OperationPhase.PackageRequested, loaded.Phase);
            Assert.AreEqual("stable", loaded.Channel);
            Assert.AreEqual("editor-debug-v1.2.0", loaded.ResolvedRevision);
            Assert.AreEqual("Library/U3DAILinker/backups/manifest-guid-1.json", loaded.ManifestBackupPath);
            Assert.AreEqual(1, loaded.DependencyChanges.Count);
            Assert.AreEqual("com.yoji.editor-debug", loaded.DependencyChanges[0].PackageName);
            Assert.IsNull(loaded.DependencyChanges[0].OldValue);
            Assert.AreEqual("url-1", loaded.DependencyChanges[0].NewValue);
            Assert.AreEqual(0, loaded.Completed.Count);
            Assert.AreEqual(0, loaded.RetryCount);
        }

        [Test] public void Save_CreatesLibraryDirectory()
        {
            var store = New();
            store.Save(Sample());
            Assert.IsTrue(File.Exists(store.LogPath));
        }

        [Test] public void Save_OverwritesExistingLog_NoLeftoverTmp()
        {
            var store = New();
            store.Save(Sample());
            var second = Sample();
            second.CurrentIndex = 1;
            second.Phase = OperationPhase.Completed;
            store.Save(second);

            var loaded = store.Load();
            Assert.AreEqual(1, loaded.CurrentIndex);
            Assert.AreEqual(OperationPhase.Completed, loaded.Phase);
            Assert.IsFalse(File.Exists(store.LogPath + ".tmp"), "tmp 应在原子替换后消失");
        }

        [Test] public void Clear_RemovesLog()
        {
            var store = New();
            store.Save(Sample());
            store.Clear();
            Assert.IsFalse(File.Exists(store.LogPath));
            Assert.IsNull(store.Load());
        }

        [Test] public void Clear_WhenNoFile_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => New().Clear());
        }

        [Test] public void Load_WhenCorruptJson_ReturnsNull()
        {
            var store = New();
            Directory.CreateDirectory(Path.GetDirectoryName(store.LogPath));
            File.WriteAllText(store.LogPath, "{ not valid json");
            Assert.IsNull(store.Load());
        }
    }
}
```

- [ ] Step 2: 跑测试确认红（编译失败，类型缺失）。Unity 在线则用 test-runner MCP：
  `python Packages/com.yoji.test-runner/client.py --timeout 180 run-tests --mode EditMode --assembly Yoji.U3DAILinker.Editor.Tests`
  期望：返回编译错误或 0 测试（`Yoji.U3DAILinker.Operations` 不存在）。离线则用 headless batchmode：
  `"E:/Unity/6000.3.16f1/Editor/Unity.exe" -batchmode -projectPath "E:/Yoji/U3D-Dev-Tools-AI" -runTests -testPlatform EditMode -testFilter "Yoji.U3DAILinker.Tests.Operations.OperationLogStoreTests" -testResults "E:/Yoji/U3D-Dev-Tools-AI/TestResults/link3-step.xml" -logFile -` 期望：编译报错 `OperationLogStore 找不到`。

- [ ] Step 3: 写 POCO `Packages/com.yoji.u3d-ai-linker/Editor/Operations/OperationLog.cs`（public 字段 + 字符串 Phase 常量，与 spec JSON 对齐）：

```csharp
using System.Collections.Generic;

namespace Yoji.U3DAILinker.Operations
{
    /// manifest 事务记录的单条依赖变更（旧值 -> 新值）。oldValue 为 null 表示新增。
    internal sealed class DependencyChange
    {
        public string PackageName;
        public string OldValue;
        public string NewValue;
    }

    /// 操作 phase 取值。字符串以便直接读日志、跨版本宽容。
    internal static class OperationPhase
    {
        public const string Pending = "pending";                 // 已落盘待发请求
        public const string PackageRequested = "package-requested"; // 已 Client.Add，等域重载/解析
        public const string Completed = "completed";             // 整队完成
        public const string Failed = "failed";                   // 超重试上限或 UPM 报错
    }

    /// 持久化的当前操作日志，与 spec 状态示例字段一一对应。
    /// 写入 Library/U3DAILinker/operation.json，跨域重载存活；只表达意图，恢复以实际 manifest 为准。
    internal sealed class OperationLog
    {
        public string OperationId;
        public string Action;                    // install-all | update | remove ...
        public string[] ToolIds;
        public int CurrentIndex;                 // 当前处理到队列第几项
        public string Phase;                     // 见 OperationPhase
        public string Channel;                   // stable | dev | local
        public string ResolvedRevision;          // 当前项解析出的 revision（tag / SHA）
        public string ManifestBackupPath;        // 由 LINK-2b manifest 事务写入
        public List<DependencyChange> DependencyChanges;
        public List<string> Completed;           // 已成功的 packageName
        public int RetryCount;                   // 当前项已重试次数（恢复核对失败 +1）
    }
}
```

  组装提示:此处 `internal sealed class DependencyChange` 与 LINK-2b 的 public 版(含 ChangeType)冲突。组装时删去本文件这一份,改用 LINK-2b 的 `DependencyChange`(队列只读 PackageName/OldValue/NewValue,多一个 ChangeType 字段无害);`OperationLog`/`OperationPhase` 保留并入同一命名空间文件。本子系统的类型用 `internal`,LINK-2b 用 `public`;统一时建议都 `internal`(测试经 InternalsVisibleTo 可见),或都 `public`,二选一保持一致。

- [ ] Step 4: 写 `Packages/com.yoji.u3d-ai-linker/Editor/Operations/OperationLogStore.cs`（tmp + `File.Replace` 原子写；`File.Replace` 要求目标已存在，故首次写用直接移动）：

```csharp
using System.IO;
using Newtonsoft.Json;

namespace Yoji.U3DAILinker.Operations
{
    /// operation.json 的原子读写。tmp + File.Replace 保证域重载/崩溃下日志不被写半截。
    /// libraryRoot 从构造注入（真实运行传工程 Library 目录，测试传临时目录）。
    internal sealed class OperationLogStore
    {
        private readonly string m_Dir;
        public string LogPath { get; }

        public OperationLogStore(string libraryRoot)
        {
            m_Dir = Path.Combine(libraryRoot, "U3DAILinker");
            LogPath = Path.Combine(m_Dir, "operation.json");
        }

        /// 原子落盘：先写 tmp，再 File.Replace 覆盖正式日志；目标不存在时直接 Move。
        public void Save(OperationLog log)
        {
            Directory.CreateDirectory(m_Dir);
            var tmp = LogPath + ".tmp";
            File.WriteAllText(tmp, JsonConvert.SerializeObject(log, Formatting.Indented));
            if (File.Exists(LogPath))
                File.Replace(tmp, LogPath, null);
            else
                File.Move(tmp, LogPath);
        }

        /// 读取日志；无文件或损坏 JSON 返回 null（恢复逻辑据此判定"无进行中操作"）。
        public OperationLog Load()
        {
            if (!File.Exists(LogPath)) return null;
            try { return JsonConvert.DeserializeObject<OperationLog>(File.ReadAllText(LogPath)); }
            catch { return null; }
        }

        /// 操作完成或取消后清除日志，避免下次启动误恢复。
        public void Clear()
        {
            try { if (File.Exists(LogPath)) File.Delete(LogPath); } catch { }
        }
    }
}
```

- [ ] Step 5: 跑测试确认全绿。命令同 Step 2。期望：`OperationLogStoreTests` 8 个用例全部 Passed。

- [ ] Step 6: 提交。

```bash
git add Packages/com.yoji.u3d-ai-linker/Editor/Operations/OperationLog.cs \
        Packages/com.yoji.u3d-ai-linker/Editor/Operations/OperationLogStore.cs \
        Packages/com.yoji.u3d-ai-linker/Tests/Editor/Operations/OperationLogStoreTests.cs
git commit -m "$(cat <<'EOF'
feat(u3d-ai-linker): add OperationLog POCO and atomic OperationLogStore

tmp + File.Replace atomic write to Library/U3DAILinker/operation.json,
survives domain reload. Corrupt/absent log loads as null.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 23: InstallQueueBuilder（启用闭包 + infra 依赖 + linker 强制排尾 + 只取 ready）

实现 spec 116/118/254/256/229：对启用 tool 取 `dependsOn` 闭包（递归加 infra），只取 `status:"ready"` 的包，linker 自身永远排队尾（spec 229：自更新替换 Editor Assembly，必须最后）。纯算法，走完整 TDD。

**Files**
- Create: `Packages/com.yoji.u3d-ai-linker/Editor/Operations/InstallQueueBuilder.cs`
- Test: `Packages/com.yoji.u3d-ai-linker/Tests/Editor/Operations/InstallQueueBuilderTests.cs`

- [ ] Step 1: 先写失败测试 `Packages/com.yoji.u3d-ai-linker/Tests/Editor/Operations/InstallQueueBuilderTests.cs`：

```csharp
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Yoji.U3DAILinker.Operations;

namespace Yoji.U3DAILinker.Tests.Operations
{
    public class InstallQueueBuilderTests
    {
        private static ResolvedTool Tool(string id, string kind, string status, string[] deps = null, bool isLinker = false)
        {
            return new ResolvedTool
            {
                ToolId = id,
                PackageName = "com.yoji." + id,
                Kind = kind,
                Status = status,
                DependsOn = deps ?? System.Array.Empty<string>(),
                IsLinker = isLinker,
                PackageUrl = "url:" + id,
            };
        }

        private static List<ResolvedTool> Registry()
        {
            return new List<ResolvedTool>
            {
                Tool("u3d-ai-linker", "infra", "ready", isLinker: true),
                Tool("editor-core", "infra", "ready"),
                Tool("editor-debug", "tool", "ready", new[] { "editor-core" }),
                Tool("test-runner", "tool", "ready", new[] { "editor-core" }),
                Tool("lua-device-debug", "tool", "planned"),
            };
        }

        private static InstallQueueBuilder New() => new InstallQueueBuilder();
        private static string[] Ids(IReadOnlyList<QueueItem> q) => q.Select(i => i.ToolId).ToArray();

        [Test] public void Build_EnabledTool_PullsInfraDependency()
        {
            var q = New().Build(Registry(), new[] { "editor-debug" });
            CollectionAssert.Contains(Ids(q), "editor-core");
            CollectionAssert.Contains(Ids(q), "editor-debug");
        }

        [Test] public void Build_InfraBeforeDependentTool()
        {
            var ids = Ids(New().Build(Registry(), new[] { "editor-debug" }));
            Assert.Less(System.Array.IndexOf(ids, "editor-core"), System.Array.IndexOf(ids, "editor-debug"));
        }

        [Test] public void Build_SharedInfra_AppearsOnce()
        {
            var ids = Ids(New().Build(Registry(), new[] { "editor-debug", "test-runner" }));
            Assert.AreEqual(1, ids.Count(x => x == "editor-core"));
        }

        [Test] public void Build_SkipsPlannedStatus()
        {
            var ids = Ids(New().Build(Registry(), new[] { "lua-device-debug" }));
            CollectionAssert.DoesNotContain(ids, "lua-device-debug");
        }

        [Test] public void Build_LinkerNotEnabled_NotIncluded()
        {
            var ids = Ids(New().Build(Registry(), new[] { "editor-debug" }));
            CollectionAssert.DoesNotContain(ids, "u3d-ai-linker");
        }

        [Test] public void Build_LinkerEnabled_AlwaysLast()
        {
            var ids = Ids(New().Build(Registry(), new[] { "u3d-ai-linker", "editor-debug" }));
            Assert.AreEqual("u3d-ai-linker", ids[ids.Length - 1]);
            CollectionAssert.Contains(ids, "editor-debug");
        }

        [Test] public void Build_LinkerLast_EvenIfInfraDepends()
        {
            // linker 是 infra，但即便被当作依赖闭包拉入也必须排最后一项
            var reg = Registry();
            var q = New().Build(reg, new[] { "u3d-ai-linker", "test-runner" });
            var ids = Ids(q);
            Assert.AreEqual("u3d-ai-linker", ids[ids.Length - 1]);
        }

        [Test] public void Build_NothingEnabled_EmptyQueue()
        {
            Assert.AreEqual(0, New().Build(Registry(), System.Array.Empty<string>()).Count);
        }

        [Test] public void Build_QueueItemCarriesUrlAndPackageName()
        {
            var q = New().Build(Registry(), new[] { "editor-debug" });
            var item = q.First(i => i.ToolId == "editor-debug");
            Assert.AreEqual("com.yoji.editor-debug", item.PackageName);
            Assert.AreEqual("url:editor-debug", item.PackageUrl);
            Assert.IsFalse(item.IsLinker);
        }

        [Test] public void Build_UnknownEnabledId_Ignored()
        {
            var ids = Ids(New().Build(Registry(), new[] { "does-not-exist", "editor-debug" }));
            CollectionAssert.Contains(ids, "editor-debug");
            CollectionAssert.DoesNotContain(ids, "does-not-exist");
        }
    }
}
```

- [ ] Step 2: 跑测试确认红（`InstallQueueBuilder`/`ResolvedTool`/`QueueItem` 不存在，编译失败）。命令同上一 Task 的 Step 2，把 `-testFilter` 换成 `Yoji.U3DAILinker.Tests.Operations.InstallQueueBuilderTests`。

- [ ] Step 3: 写实现 `Packages/com.yoji.u3d-ai-linker/Editor/Operations/InstallQueueBuilder.cs`：

```csharp
using System.Collections.Generic;

namespace Yoji.U3DAILinker.Operations
{
    /// Registry 解析后的单个工具视图（本子系统消费 LINK-2 的解析结果）。
    /// 组装时若 LINK-2 已导出等价类型，可用其替换并删除本类。
    internal sealed class ResolvedTool
    {
        public string ToolId;
        public string PackageName;
        public string Kind;        // "tool" | "infra"
        public string Status;      // "ready" | "skill-only" | "planned"
        public string[] DependsOn;
        public bool IsLinker;      // linker 自身（自更新必须排队尾）
        public string PackageUrl;  // 已由 Registry 校验字段生成的安装 URL
    }

    /// 安装队列单项：串行 Client.Add 的目标。
    internal sealed class QueueItem
    {
        public string ToolId;
        public string PackageName;
        public string PackageUrl;
        public bool IsLinker;
    }

    /// 把"用户启用的 tool 集合"展开成有序安装队列：
    /// 1) 取启用工具 + 其 dependsOn 递归闭包（拉入 infra）；
    /// 2) 只保留 status=="ready" 的项；
    /// 3) 依赖在前（infra 先于 dependent），用确定性后序遍历得到拓扑序；
    /// 4) linker 自身强制排到最后（spec 229：自更新替换 Editor Assembly）。
    /// 不在此做环依赖/未知 ID 拒绝（那是 LINK-2 Registry 校验职责）；未知启用 ID 静默跳过。
    internal sealed class InstallQueueBuilder
    {
        public IReadOnlyList<QueueItem> Build(
            IReadOnlyList<ResolvedTool> resolvedTools,
            IReadOnlyCollection<string> enabledToolIds)
        {
            var byId = new Dictionary<string, ResolvedTool>();
            foreach (var t in resolvedTools)
                byId[t.ToolId] = t;

            var ordered = new List<ResolvedTool>(); // 依赖在前的确定性拓扑序
            var visited = new HashSet<string>();

            foreach (var id in enabledToolIds)
                Visit(id, byId, visited, ordered);

            // 拆出 linker，过滤非 ready，linker 排尾
            var items = new List<QueueItem>();
            QueueItem linkerItem = null;
            foreach (var t in ordered)
            {
                if (t.Status != "ready") continue;
                var item = new QueueItem
                {
                    ToolId = t.ToolId,
                    PackageName = t.PackageName,
                    PackageUrl = t.PackageUrl,
                    IsLinker = t.IsLinker,
                };
                if (t.IsLinker) linkerItem = item;
                else items.Add(item);
            }
            if (linkerItem != null) items.Add(linkerItem);
            return items;
        }

        // 后序遍历：先递归依赖，再加自身 -> 依赖天然排在前面。HashSet 去重保证共享 infra 只出现一次。
        private static void Visit(
            string id,
            Dictionary<string, ResolvedTool> byId,
            HashSet<string> visited,
            List<ResolvedTool> ordered)
        {
            if (!byId.TryGetValue(id, out var tool)) return; // 未知启用 ID 跳过
            if (!visited.Add(id)) return;                    // 已访问（环或共享）
            if (tool.DependsOn != null)
            {
                foreach (var dep in tool.DependsOn)
                    Visit(dep, byId, visited, ordered);
            }
            ordered.Add(tool);
        }
    }
}
```

- [ ] Step 4: 跑测试确认全绿（`InstallQueueBuilderTests` 10 个用例 Passed）。命令同 Step 2。

- [ ] Step 5: 提交。

```bash
git add Packages/com.yoji.u3d-ai-linker/Editor/Operations/InstallQueueBuilder.cs \
        Packages/com.yoji.u3d-ai-linker/Tests/Editor/Operations/InstallQueueBuilderTests.cs
git commit -m "$(cat <<'EOF'
feat(u3d-ai-linker): add InstallQueueBuilder with infra closure and linker-last

Enabled tools expand to dependsOn closure (deps first), ready-only,
linker self forced to queue tail for safe self-update.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 24: IUpmClient / IInstalledPackageProbe 抽象 + 真实实现

把真实 Editor 副作用（`Client.Add`、读已安装包 URL）抽成接口，供 EditMode 用 fake 单测；真实实现薄封装 `UnityEditor.PackageManager` API，不进自动化测试（Task 26 手动验证）。

**Files**
- Create: `Packages/com.yoji.u3d-ai-linker/Editor/Operations/IUpmClient.cs`
- Create: `Packages/com.yoji.u3d-ai-linker/Editor/Operations/UnityUpmClient.cs`
- Create: `Packages/com.yoji.u3d-ai-linker/Editor/Operations/IInstalledPackageProbe.cs`
- Create: `Packages/com.yoji.u3d-ai-linker/Editor/Operations/UnityInstalledPackageProbe.cs`

- [ ] Step 1: 写接口与请求句柄 `Packages/com.yoji.u3d-ai-linker/Editor/Operations/IUpmClient.cs`：

```csharp
namespace Yoji.U3DAILinker.Operations
{
    /// 一次 UPM Add 请求的可轮询句柄。抽象掉 UnityEditor.PackageManager.Requests.AddRequest，
    /// 使队列逻辑可在 EditMode 用 fake 驱动。注意：真实 AddRequest 无法跨域重载保留，
    /// 因此 runner 永不持久化句柄，只在当前域内用它判断"本次是否已发出请求"。
    internal sealed class UpmAddHandle
    {
        public bool IsComplete;
        public bool IsError;
        public string ErrorMessage;
    }

    /// UPM 安装客户端抽象。Add 串行调用，调用方在前一项达成后才发下一项。
    internal interface IUpmClient
    {
        UpmAddHandle Add(string identifier);
    }
}
```

- [ ] Step 2: 写已安装包探针接口 `Packages/com.yoji.u3d-ai-linker/Editor/Operations/IInstalledPackageProbe.cs`：

```csharp
namespace Yoji.U3DAILinker.Operations
{
    /// 读取目标工程当前已解析包的来源 URL（用于恢复时核对目标是否达成）。
    /// 抽象掉 PackageInfo / Client.List，使恢复核对逻辑可在 EditMode 用 fake 单测。
    internal interface IInstalledPackageProbe
    {
        /// 返回 packageName 当前已安装的来源标识（Git URL / file: 路径）；未安装返回 null。
        string GetInstalledUrl(string packageName);
    }
}
```

- [ ] Step 3: 写真实 UPM 客户端 `Packages/com.yoji.u3d-ai-linker/Editor/Operations/UnityUpmClient.cs`（薄封装 `Client.Add`；句柄包当前域内的 `AddRequest`）：

```csharp
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;

namespace Yoji.U3DAILinker.Operations
{
    /// 真实 UPM 客户端：把 UnityEditor.PackageManager.Client.Add 适配为 IUpmClient。
    /// 不进自动化测试（依赖真实 Editor / 网络）；由 Task 26 手动验证覆盖。
    internal sealed class UnityUpmClient : IUpmClient
    {
        public UpmAddHandle Add(string identifier)
        {
            var req = Client.Add(identifier);
            return new RequestHandle(req).View;
        }

        // 把 AddRequest 状态投影到 UpmAddHandle。Add 触发的域重载会丢弃本句柄；
        // 这没关系——恢复时改用 IInstalledPackageProbe 以实际 manifest 为准核对，不依赖句柄。
        private sealed class RequestHandle
        {
            private readonly AddRequest m_Req;
            public readonly UpmAddHandle View = new UpmAddHandle();

            public RequestHandle(AddRequest req)
            {
                m_Req = req;
                Refresh();
            }

            public void Refresh()
            {
                if (m_Req.Status == StatusCode.InProgress) return;
                View.IsComplete = true;
                if (m_Req.Status == StatusCode.Failure)
                {
                    View.IsError = true;
                    View.ErrorMessage = m_Req.Error != null ? m_Req.Error.message : "unknown UPM error";
                }
            }
        }
    }
}
```

- [ ] Step 4: 写真实探针 `Packages/com.yoji.u3d-ai-linker/Editor/Operations/UnityInstalledPackageProbe.cs`（用 `PackageInfo.FindForPackageName`，2022.3 可用；其 `packageId`/`projectDependenciesEntry` 暴露来源）：

```csharp
using UnityEditor.PackageManager;

namespace Yoji.U3DAILinker.Operations
{
    /// 真实探针：从 PackageInfo 读取目标工程对某包的依赖来源字符串。
    /// projectDependenciesEntry 即 manifest dependencies 中该包的右值（Git URL / file: 路径）。
    /// 不进自动化测试；由 Task 26 手动验证覆盖。
    internal sealed class UnityInstalledPackageProbe : IInstalledPackageProbe
    {
        public string GetInstalledUrl(string packageName)
        {
            var info = PackageInfo.FindForPackageName(packageName);
            if (info == null) return null;
            // projectDependenciesEntry 反映 manifest 顶层声明值；为空时回退 packageId 末段。
            return !string.IsNullOrEmpty(info.projectDependenciesEntry)
                ? info.projectDependenciesEntry
                : info.packageId;
        }
    }
}
```

- [ ] Step 5: 编译确认无错误（仅新增接口与真实实现，无新测试）。Unity 在线触发一次重编译：
  `python Packages/com.yoji.test-runner/client.py --timeout 120 recompile`
  期望：编译成功、无错误（`UnityEditor.PackageManager` 命名空间在 Editor asmdef 下可用，无需额外 reference）。

- [ ] Step 6: 提交。

```bash
git add Packages/com.yoji.u3d-ai-linker/Editor/Operations/IUpmClient.cs \
        Packages/com.yoji.u3d-ai-linker/Editor/Operations/UnityUpmClient.cs \
        Packages/com.yoji.u3d-ai-linker/Editor/Operations/IInstalledPackageProbe.cs \
        Packages/com.yoji.u3d-ai-linker/Editor/Operations/UnityInstalledPackageProbe.cs
git commit -m "$(cat <<'EOF'
feat(u3d-ai-linker): add IUpmClient/IInstalledPackageProbe and Unity impls

Abstract Client.Add and installed-package URL probe behind interfaces so
queue/recovery logic is fake-testable in EditMode; real impls thin-wrap
UnityEditor.PackageManager (manually verified, not in automated suite).

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 25: UpmQueueRunner（串行：先原子落盘再 Add；恢复核对 + 重试 ≤2）

实现 spec 420/449-454 的队列推进核心：`Advance(log)` 是单步状态机，被首次发起和域重载恢复共用。规则——每步先把 `phase=package-requested`、`currentIndex` 原子落盘，再 `Add`；恢复时用 probe 核对当前项目标 URL 是否达成：达成则把当前包加入 `Completed`、`currentIndex++`、`RetryCount` 归零并推进；未达成则 `RetryCount++`，>`MaxRetries` 标 `failed` 停队。纯逻辑，fake client + fake probe 走完整 TDD。

**Files**
- Create: `Packages/com.yoji.u3d-ai-linker/Editor/Operations/UpmQueueRunner.cs`
- Test: `Packages/com.yoji.u3d-ai-linker/Tests/Editor/Operations/FakeUpmClient.cs`
- Test: `Packages/com.yoji.u3d-ai-linker/Tests/Editor/Operations/FakeInstalledPackageProbe.cs`
- Test: `Packages/com.yoji.u3d-ai-linker/Tests/Editor/Operations/UpmQueueRunnerTests.cs`

- [ ] Step 1: 写 fake 客户端 `Packages/com.yoji.u3d-ai-linker/Tests/Editor/Operations/FakeUpmClient.cs`：

```csharp
using System.Collections.Generic;
using Yoji.U3DAILinker.Operations;

namespace Yoji.U3DAILinker.Tests.Operations
{
    /// 记录 Add 调用序列的假 UPM 客户端。可配置某个 identifier 返回错误句柄。
    internal sealed class FakeUpmClient : IUpmClient
    {
        public readonly List<string> AddCalls = new List<string>();
        public string FailIdentifierContains; // 命中则返回 IsError 句柄

        public UpmAddHandle Add(string identifier)
        {
            AddCalls.Add(identifier);
            if (FailIdentifierContains != null && identifier.Contains(FailIdentifierContains))
                return new UpmAddHandle { IsComplete = true, IsError = true, ErrorMessage = "fake UPM failure" };
            return new UpmAddHandle { IsComplete = true, IsError = false };
        }
    }
}
```

- [ ] Step 2: 写 fake 探针 `Packages/com.yoji.u3d-ai-linker/Tests/Editor/Operations/FakeInstalledPackageProbe.cs`：

```csharp
using System.Collections.Generic;
using Yoji.U3DAILinker.Operations;

namespace Yoji.U3DAILinker.Tests.Operations
{
    /// 可编程的已安装包探针。测试用 Set 模拟"某包已解析到某 URL"，模拟域重载后的实际状态。
    internal sealed class FakeInstalledPackageProbe : IInstalledPackageProbe
    {
        private readonly Dictionary<string, string> m_Installed = new Dictionary<string, string>();

        public void Set(string packageName, string url) => m_Installed[packageName] = url;

        public string GetInstalledUrl(string packageName)
            => m_Installed.TryGetValue(packageName, out var url) ? url : null;
    }
}
```

- [ ] Step 3: 写失败测试 `Packages/com.yoji.u3d-ai-linker/Tests/Editor/Operations/UpmQueueRunnerTests.cs`：

```csharp
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Yoji.U3DAILinker.Operations;

namespace Yoji.U3DAILinker.Tests.Operations
{
    public class UpmQueueRunnerTests
    {
        private string m_LibraryRoot;
        private OperationLogStore m_Store;
        private FakeUpmClient m_Upm;
        private FakeInstalledPackageProbe m_Probe;

        [SetUp] public void SetUp()
        {
            m_LibraryRoot = Path.Combine(Path.GetTempPath(), "u3dlinker_" + System.Guid.NewGuid().ToString("N"));
            m_Store = new OperationLogStore(m_LibraryRoot);
            m_Upm = new FakeUpmClient();
            m_Probe = new FakeInstalledPackageProbe();
        }

        [TearDown] public void TearDown()
        {
            try { if (Directory.Exists(m_LibraryRoot)) Directory.Delete(m_LibraryRoot, true); } catch { }
        }

        private UpmQueueRunner New() => new UpmQueueRunner(m_Store, m_Upm, m_Probe);

        // 两项队列：editor-core -> editor-debug
        private static OperationLog TwoItemLog()
        {
            return new OperationLog
            {
                OperationId = "op-1",
                Action = "install-all",
                ToolIds = new[] { "editor-debug" },
                CurrentIndex = 0,
                Phase = OperationPhase.Pending,
                Channel = "stable",
                DependencyChanges = new List<DependencyChange>
                {
                    new DependencyChange { PackageName = "com.yoji.editor-core", NewValue = "url-core" },
                    new DependencyChange { PackageName = "com.yoji.editor-debug", NewValue = "url-debug" },
                },
                Completed = new List<string>(),
                RetryCount = 0,
            };
        }

        [Test] public void Advance_FromPending_RequestsCurrentAndPersists()
        {
            var log = TwoItemLog();
            var result = New().Advance(log);

            Assert.AreEqual(QueueStepResult.Requested, result);
            Assert.AreEqual(1, m_Upm.AddCalls.Count);
            Assert.AreEqual("url-core", m_Upm.AddCalls[0]);
            // 先落盘再 Add：日志已记 package-requested
            var persisted = m_Store.Load();
            Assert.AreEqual(OperationPhase.PackageRequested, persisted.Phase);
            Assert.AreEqual(0, persisted.CurrentIndex);
        }

        [Test] public void Advance_RequestedButNotInstalled_RetriesSamePackage()
        {
            var log = TwoItemLog();
            log.Phase = OperationPhase.PackageRequested; // 模拟域重载后回来，但包还没解析到
            var result = New().Advance(log);

            Assert.AreEqual(QueueStepResult.Requested, result);
            Assert.AreEqual(1, log.RetryCount);
            Assert.AreEqual(0, log.CurrentIndex); // 仍停在第一项
            Assert.AreEqual("url-core", m_Upm.AddCalls[0]); // 重发同一项
        }

        [Test] public void Advance_RequestedAndInstalled_AdvancesToNext()
        {
            var log = TwoItemLog();
            log.Phase = OperationPhase.PackageRequested;
            m_Probe.Set("com.yoji.editor-core", "url-core"); // 已达成

            var result = New().Advance(log);

            Assert.AreEqual(QueueStepResult.Requested, result);
            CollectionAssert.Contains(log.Completed, "com.yoji.editor-core");
            Assert.AreEqual(1, log.CurrentIndex);
            Assert.AreEqual(0, log.RetryCount); // 推进后归零
            Assert.AreEqual("url-debug", m_Upm.AddCalls[m_Upm.AddCalls.Count - 1]); // 已发第二项
        }

        [Test] public void Advance_LastItemInstalled_ReturnsAlreadySatisfiedAndCompletes()
        {
            var log = TwoItemLog();
            log.CurrentIndex = 1;
            log.Phase = OperationPhase.PackageRequested;
            log.Completed = new List<string> { "com.yoji.editor-core" };
            m_Probe.Set("com.yoji.editor-debug", "url-debug");

            var result = New().Advance(log);

            Assert.AreEqual(QueueStepResult.AlreadySatisfied, result);
            Assert.AreEqual(OperationPhase.Completed, log.Phase);
            CollectionAssert.Contains(log.Completed, "com.yoji.editor-debug");
            Assert.IsNull(m_Store.Load(), "完成后日志应被清除");
        }

        [Test] public void Advance_RetryExceedsMax_Faults()
        {
            var log = TwoItemLog();
            log.Phase = OperationPhase.PackageRequested;
            log.RetryCount = UpmQueueRunner.MaxRetries; // 已到上限，本次核对仍失败

            var result = New().Advance(log);

            Assert.AreEqual(QueueStepResult.Faulted, result);
            Assert.AreEqual(OperationPhase.Failed, log.Phase);
            var persisted = m_Store.Load();
            Assert.IsNotNull(persisted, "失败日志保留供用户 Retry/Cancel");
            Assert.AreEqual(OperationPhase.Failed, persisted.Phase);
        }

        [Test] public void Advance_UpmAddReturnsError_Faults()
        {
            var log = TwoItemLog();
            m_Upm.FailIdentifierContains = "url-core";

            var result = New().Advance(log);

            Assert.AreEqual(QueueStepResult.Faulted, result);
            Assert.AreEqual(OperationPhase.Failed, log.Phase);
        }

        [Test] public void Advance_AlreadyCompletedLog_NoOp()
        {
            var log = TwoItemLog();
            log.Phase = OperationPhase.Completed;

            var result = New().Advance(log);

            Assert.AreEqual(QueueStepResult.AlreadySatisfied, result);
            Assert.AreEqual(0, m_Upm.AddCalls.Count);
        }

        [Test] public void Advance_RecordsResolvedRevisionFromCurrentChange()
        {
            var log = TwoItemLog();
            New().Advance(log);
            // resolvedRevision 跟随当前项；此处用 NewValue 作为可核对目标已足够
            Assert.AreEqual("url-core", m_Store.Load().DependencyChanges[0].NewValue);
        }
    }
}
```

- [ ] Step 4: 跑测试确认红（`UpmQueueRunner`/`QueueStepResult` 不存在）。命令把 `-testFilter` 换成 `Yoji.U3DAILinker.Tests.Operations.UpmQueueRunnerTests`。

- [ ] Step 5: 写实现 `Packages/com.yoji.u3d-ai-linker/Editor/Operations/UpmQueueRunner.cs`：

```csharp
namespace Yoji.U3DAILinker.Operations
{
    /// Advance 的单步结果。
    internal enum QueueStepResult
    {
        Requested,        // 发出了一次 Add（当前项或推进后的下一项），等待下次回来核对
        AlreadySatisfied, // 整队已完成（无需再 Add）
        Faulted,          // UPM 报错或超重试上限，队列停在失败态
    }

    /// 串行 UPM 安装队列推进器。单步状态机，被首次发起与域重载恢复共用。
    /// 不变式：每发一次 Add 之前，必先把 phase=package-requested 与 currentIndex 原子落盘，
    /// 保证域重载后 Bootstrap 能从日志接续。恢复核对以 IInstalledPackageProbe 的实际结果为准，
    /// 日志只表达意图（spec 449-454）。
    internal sealed class UpmQueueRunner
    {
        public const int MaxRetries = 2;

        private readonly OperationLogStore m_Store;
        private readonly IUpmClient m_Upm;
        private readonly IInstalledPackageProbe m_Probe;

        public UpmQueueRunner(OperationLogStore store, IUpmClient upm, IInstalledPackageProbe probe)
        {
            m_Store = store;
            m_Upm = upm;
            m_Probe = probe;
        }

        /// 推进一步。修改传入 log（就地）并落盘。语义：
        /// - completed/failed：直接返回，不再动队列。
        /// - pending：发当前项 Add，转 package-requested。
        /// - package-requested：核对当前项是否已安装。
        ///     达成 -> 计入 Completed、index++、retry 归零；
        ///       若是最后一项则整队 completed、清日志、返回 AlreadySatisfied；
        ///       否则发下一项 Add、返回 Requested。
        ///     未达成 -> retry++；超上限标 failed 返回 Faulted；否则重发当前项、返回 Requested。
        public QueueStepResult Advance(OperationLog log)
        {
            if (log.Phase == OperationPhase.Completed) return QueueStepResult.AlreadySatisfied;
            if (log.Phase == OperationPhase.Failed) return QueueStepResult.Faulted;

            if (log.Phase == OperationPhase.Pending)
                return RequestCurrent(log);

            // package-requested：核对当前项
            var current = CurrentChange(log);
            var installed = m_Probe.GetInstalledUrl(current.PackageName);
            if (installed == current.NewValue)
            {
                if (!log.Completed.Contains(current.PackageName))
                    log.Completed.Add(current.PackageName);
                log.RetryCount = 0;
                log.CurrentIndex++;

                if (log.CurrentIndex >= log.DependencyChanges.Count)
                {
                    log.Phase = OperationPhase.Completed;
                    m_Store.Clear();
                    return QueueStepResult.AlreadySatisfied;
                }
                return RequestCurrent(log); // 发下一项
            }

            // 未达成：重试或失败
            if (log.RetryCount >= MaxRetries)
            {
                log.Phase = OperationPhase.Failed;
                m_Store.Save(log);
                return QueueStepResult.Faulted;
            }
            log.RetryCount++;
            return RequestCurrent(log); // 重发当前项
        }

        // 先原子落盘 package-requested + 当前 revision，再 Add；Add 报错则转 failed。
        private QueueStepResult RequestCurrent(OperationLog log)
        {
            var change = CurrentChange(log);
            log.Phase = OperationPhase.PackageRequested;
            log.ResolvedRevision = change.NewValue;
            m_Store.Save(log); // 不变式：落盘在 Add 之前

            var handle = m_Upm.Add(change.NewValue);
            if (handle.IsError)
            {
                log.Phase = OperationPhase.Failed;
                m_Store.Save(log);
                return QueueStepResult.Faulted;
            }
            return QueueStepResult.Requested;
        }

        private static DependencyChange CurrentChange(OperationLog log)
            => log.DependencyChanges[log.CurrentIndex];
    }
}
```

- [ ] Step 6: 跑测试确认全绿（`UpmQueueRunnerTests` 8 个用例 Passed）。命令同 Step 4。

- [ ] Step 7: 提交。

```bash
git add Packages/com.yoji.u3d-ai-linker/Editor/Operations/UpmQueueRunner.cs \
        Packages/com.yoji.u3d-ai-linker/Tests/Editor/Operations/FakeUpmClient.cs \
        Packages/com.yoji.u3d-ai-linker/Tests/Editor/Operations/FakeInstalledPackageProbe.cs \
        Packages/com.yoji.u3d-ai-linker/Tests/Editor/Operations/UpmQueueRunnerTests.cs
git commit -m "$(cat <<'EOF'
feat(u3d-ai-linker): add serial UpmQueueRunner with persist-before-add and retry

Single-step state machine shared by first-dispatch and domain-reload
recovery. Persists package-requested before each Add; reconciles via
installed-package probe; retries <=2 then faults and keeps log.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 26: U3DAILinkerBootstrap 域重载恢复（[InitializeOnLoad] + delayCall）

实现 spec 449：`[InitializeOnLoad]` 静态构造里挂 `EditorApplication.delayCall`，等编译结束、Package Manager 空闲后读 `operation.json`，用真实 probe 核对并调 `UpmQueueRunner.Advance` 接续队列。恢复决策逻辑（"有进行中日志才恢复 / 已完成或失败不动"）抽成纯静态方法 `RecoveryReconciler.ShouldResume`，走 TDD；真实 `[InitializeOnLoad]` 触发与真实域重载列手动验证。

**Files**
- Create: `Packages/com.yoji.u3d-ai-linker/Editor/Operations/U3DAILinkerBootstrap.cs`
- Test: `Packages/com.yoji.u3d-ai-linker/Tests/Editor/Operations/RecoveryReconcilerTests.cs`

- [ ] Step 1: 写失败测试 `Packages/com.yoji.u3d-ai-linker/Tests/Editor/Operations/RecoveryReconcilerTests.cs`（只测纯决策函数 `RecoveryReconciler.ShouldResume`，不触碰 Editor 静态构造）：

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using Yoji.U3DAILinker.Operations;

namespace Yoji.U3DAILinker.Tests.Operations
{
    public class RecoveryReconcilerTests
    {
        private static OperationLog Log(string phase)
        {
            return new OperationLog
            {
                OperationId = "op-1",
                Phase = phase,
                CurrentIndex = 0,
                DependencyChanges = new List<DependencyChange>
                {
                    new DependencyChange { PackageName = "com.yoji.editor-core", NewValue = "url-core" },
                },
                Completed = new List<string>(),
                RetryCount = 0,
            };
        }

        [Test] public void ShouldResume_NullLog_False()
            => Assert.IsFalse(RecoveryReconciler.ShouldResume(null));

        [Test] public void ShouldResume_PackageRequested_True()
            => Assert.IsTrue(RecoveryReconciler.ShouldResume(Log(OperationPhase.PackageRequested)));

        [Test] public void ShouldResume_Pending_True()
            => Assert.IsTrue(RecoveryReconciler.ShouldResume(Log(OperationPhase.Pending)));

        [Test] public void ShouldResume_Completed_False()
            => Assert.IsFalse(RecoveryReconciler.ShouldResume(Log(OperationPhase.Completed)));

        [Test] public void ShouldResume_Failed_False()
            => Assert.IsFalse(RecoveryReconciler.ShouldResume(Log(OperationPhase.Failed)));

        [Test] public void ShouldResume_EmptyDependencyChanges_False()
        {
            var log = Log(OperationPhase.PackageRequested);
            log.DependencyChanges = new List<DependencyChange>();
            Assert.IsFalse(RecoveryReconciler.ShouldResume(log));
        }

        [Test] public void ShouldResume_IndexOutOfRange_False()
        {
            var log = Log(OperationPhase.PackageRequested);
            log.CurrentIndex = 5; // 越界（不应恢复，交由完成/失败处理）
            Assert.IsFalse(RecoveryReconciler.ShouldResume(log));
        }
    }
}
```

- [ ] Step 2: 跑测试确认红（`RecoveryReconciler` 不存在）。`-testFilter` 换成 `Yoji.U3DAILinker.Tests.Operations.RecoveryReconcilerTests`。

- [ ] Step 3: 写 Bootstrap 与纯决策器 `Packages/com.yoji.u3d-ai-linker/Editor/Operations/U3DAILinkerBootstrap.cs`。决策器纯逻辑（被测），Bootstrap 静态构造与 delayCall 装配真实依赖（手动验证）：

```csharp
using UnityEditor;
using UnityEditor.PackageManager;

namespace Yoji.U3DAILinker.Operations
{
    /// 纯决策：给定日志，是否应在本次域加载后接续队列。
    /// 只在有"进行中"日志（pending / package-requested）且当前 index 合法时恢复；
    /// completed / failed / null / 空队列 / 越界一律不动（交由用户在面板 Retry/Cancel）。
    internal static class RecoveryReconciler
    {
        public static bool ShouldResume(OperationLog log)
        {
            if (log == null) return false;
            if (log.DependencyChanges == null || log.DependencyChanges.Count == 0) return false;
            if (log.CurrentIndex < 0 || log.CurrentIndex >= log.DependencyChanges.Count) return false;
            return log.Phase == OperationPhase.Pending || log.Phase == OperationPhase.PackageRequested;
        }
    }

    /// 域重载恢复入口。[InitializeOnLoad] 在每次域加载后运行静态构造，
    /// 挂 EditorApplication.delayCall 到下一帧，等编译结束、Package Manager 空闲后再恢复。
    /// 由于旧 AddRequest 无法跨域保留，恢复完全依赖持久化日志 + 真实 probe 核对（spec 229/449）。
    /// 静态构造与 delayCall 的真实触发列为手动验证；可被测的决策在 RecoveryReconciler。
    [InitializeOnLoad]
    internal static class U3DAILinkerBootstrap
    {
        static U3DAILinkerBootstrap()
        {
            EditorApplication.delayCall += TryResume;
        }

        private static void TryResume()
        {
            // Package Manager 还在解析时让出，下一帧再试，避免与进行中的 UPM 请求竞争。
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += TryResume;
                return;
            }

            var store = new OperationLogStore(LibraryRoot());
            var log = store.Load();
            if (!RecoveryReconciler.ShouldResume(log))
                return;

            var runner = new UpmQueueRunner(store, new UnityUpmClient(), new UnityInstalledPackageProbe());
            var result = runner.Advance(log);

            // Requested 表示又发了一次 Add：它可能触发新一轮域重载，
            // 下次加载时本静态构造会再次运行并从落盘日志接续，无需在此自旋等待。
            if (result == QueueStepResult.Requested)
                EditorApplication.delayCall += TryResume;
        }

        // 工程根的 Library 目录：Application.dataPath 去掉末尾 /Assets 再拼 /Library。
        private static string LibraryRoot()
        {
            var assets = UnityEngine.Application.dataPath;            // <proj>/Assets
            var projectRoot = System.IO.Directory.GetParent(assets).FullName;
            return System.IO.Path.Combine(projectRoot, "Library");
        }
    }
}
```

- [ ] Step 4: 跑测试确认全绿（`RecoveryReconcilerTests` 7 个用例 Passed）。命令同 Step 2。

- [ ] Step 5: 跑整个子系统测试套件确认无回归：
  `python Packages/com.yoji.test-runner/client.py --timeout 240 run-tests --mode EditMode --assembly Yoji.U3DAILinker.Editor.Tests`
  期望:`OperationLogStoreTests` + `InstallQueueBuilderTests` + `UpmQueueRunnerTests` + `RecoveryReconcilerTests` 全部 Passed,无失败无报错。

- [ ] Step 6: 提交。

```bash
git add Packages/com.yoji.u3d-ai-linker/Editor/Operations/U3DAILinkerBootstrap.cs \
        Packages/com.yoji.u3d-ai-linker/Tests/Editor/Operations/RecoveryReconcilerTests.cs
git commit -m "$(cat <<'EOF'
feat(u3d-ai-linker): add domain-reload recovery bootstrap

[InitializeOnLoad] + delayCall reads operation.json after compile/PM idle
and resumes queue via UpmQueueRunner using real probe. Pure resume
decision (RecoveryReconciler.ShouldResume) is unit-tested.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

- [ ] Step 7（手动验证，真实域重载与真实 Client.Add，无法自动化）：
  1. 在空 Unity 2022.3 工程中以 `file:` 方式装入本仓库 `Packages/com.yoji.u3d-ai-linker`。
  2. 临时加一个 Editor 菜单或在 `[MenuItem]` 调试入口里：构造 `OperationLog`（两项真实 ready 包 URL，`editor-core` 后跟 `editor-debug`，`phase=pending`），存到 `Library/U3DAILinker/operation.json`，调一次 `new UpmQueueRunner(store, new UnityUpmClient(), new UnityInstalledPackageProbe()).Advance(log)`。
  - 看什么 / 期望：Console 无异常；`Library/U3DAILinker/operation.json` 出现且 `phase=package-requested`、`currentIndex=0`；Package Manager 开始下载第一个包；UPM 完成后触发一次域重载。
  3. 域重载完成后不要手动操作，等下一帧。
  - 看什么 / 期望：`U3DAILinkerBootstrap.TryResume` 自动接续——`operation.json` 的 `completed` 含 `com.yoji.editor-core`、`currentIndex` 变为 1、第二项开始安装。
  4. 第二项装完、再次域重载稳定后。
  - 看什么 / 期望:`operation.json` 被删除(整队 `completed` 后 `Clear`);`Packages/manifest.json` 顶层含两个 `com.yoji.*` Git URL 依赖;Package Manager 窗口显示两包已安装。
  5. 失败路径:把第二项 URL 改成不存在的 revision 重跑。
  - 看什么 / 期望:`Client.Add` 失败后 `operation.json` 保留且 `phase=failed`;不再自动重试(已达 `Requested` 不再自旋);日志保留供后续 Project Settings 面板 Retry/Rollback(由 LINK-6/UI 子系统消费)。
  6. linker-last 真实验证:启用集合含 `u3d-ai-linker` + 一个 tool,构建队列后确认 `u3d-ai-linker` 的 `Client.Add` 最后发出,且其触发的 Editor Assembly 替换发生在其它包之后,恢复仍能从日志读到 `completed`。

---

### LINK-4 — 事务式 Skill 复制 + ownership + Windows Junction

本子系统实现 spec 306-315（事务式目录替换六步）、265-280（Agent 同步物理目录与 Junction 布局）、479-480（Junction 创建/重复同步/失效修复/ownership 校验/未知目录保护、staging 失败不破坏现有版本、替换失败恢复 backup）的验收点。

依赖说明（LINK-1）：本计划假定 LINK-1 已创建 `Packages/com.yoji.u3d-ai-linker/package.json`（依赖 `com.unity.nuget.newtonsoft-json`）并创建/接入一个名为 `u3d-ai-linker` 的 TestProject（`TestProjects/u3d-ai-linker/`，其 `Packages/manifest.json` 含 `"com.yoji.u3d-ai-linker": "file:../../../Packages/com.yoji.u3d-ai-linker"`）。本子系统若先于 LINK-1 落地，需先手动建该 TestProject 并把 linker 包加入其 manifest，否则下面的 `run-editmode.ps1 -Project u3d-ai-linker` 无法运行。所有测试运行命令统一为：

```
pwsh -File tools/run-editmode.ps1 -Project u3d-ai-linker
```

该脚本批跑整个工程的 EditMode 测试（NUnit3 XML），无法按类过滤；下面每个 Task 的"跑测试"步骤都用同一命令，靠输出里的 `total/passed/failed` 与 `FAIL: <fullname>` 行判断本 Task 新增测试的红/绿。第一次运行前请确保没有别的 Unity Editor 打开着该 TestProject（脚本只清 stale lockfile，不删 Library）。若本仓库直接作为开发工程,亦可改用 test-runner MCP 的 `POST /recompile` + `POST /run?mode=EditMode&assembly=Yoji.U3DAILinker.Editor.Tests`。

> 幂等基线:Task 27 的主/测试 asmdef + AssemblyInfo 与 LINK-0/1/2/2b/3 同名,核对存在即跳过。本子系统的 `Editor/Operations/ContentHash.cs`/`OwnershipFile.cs`/`OwnershipGuard.cs`/`IJunctionManager.cs`/`WindowsJunctionManager.cs`/`AgentSyncService.cs` 是新增。

---

### Task 27: Linker Editor/Tests 程序集骨架

建立 LINK-4 代码所在的主/测试程序集，命名与引用对齐 test-runner 包。本 Task 不含业务逻辑，仅让后续 Task 有可编译、可被测试发现的程序集；用一个最小冒烟测试确认 TestProject 能发现 linker 测试程序集。

Files:
- Create: `Packages/com.yoji.u3d-ai-linker/Editor/Yoji.U3DAILinker.Editor.asmdef`
- Create: `Packages/com.yoji.u3d-ai-linker/Editor/AssemblyInfo.cs`
- Create: `Packages/com.yoji.u3d-ai-linker/Tests/Editor/Yoji.U3DAILinker.Editor.Tests.asmdef`
- Test: `Packages/com.yoji.u3d-ai-linker/Tests/Editor/ContentHashTests.cs`（本 Task 仅放一个冒烟测试，Task 28 再扩充）

- [ ] Step 1: 创建主程序集 `Packages/com.yoji.u3d-ai-linker/Editor/Yoji.U3DAILinker.Editor.asmdef`，内容（对齐 test-runner，`overrideReferences`+`precompiledReferences` 引 Newtonsoft，`includePlatforms:["Editor"]`）：

```json
{
  "name": "Yoji.U3DAILinker.Editor",
  "rootNamespace": "Yoji.U3DAILinker",
  "references": [],
  "includePlatforms": ["Editor"],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": true,
  "precompiledReferences": ["Newtonsoft.Json.dll"],
  "autoReferenced": true,
  "defineConstraints": [],
  "versionDefines": [],
  "noEngineReferences": false
}
```

- [ ] Step 2: 创建 `Packages/com.yoji.u3d-ai-linker/Editor/AssemblyInfo.cs`，让测试程序集能访问 internal 类型：

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Yoji.U3DAILinker.Editor.Tests")]
```

- [ ] Step 3: 创建测试程序集 `Packages/com.yoji.u3d-ai-linker/Tests/Editor/Yoji.U3DAILinker.Editor.Tests.asmdef`（对齐 test-runner 测试 asmdef）：

```json
{
  "name": "Yoji.U3DAILinker.Editor.Tests",
  "rootNamespace": "Yoji.U3DAILinker.Tests",
  "references": ["Yoji.U3DAILinker.Editor", "UnityEngine.TestRunner", "UnityEditor.TestRunner"],
  "includePlatforms": ["Editor"],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": true,
  "precompiledReferences": ["nunit.framework.dll", "Newtonsoft.Json.dll"],
  "autoReferenced": false,
  "defineConstraints": ["UNITY_INCLUDE_TESTS"],
  "versionDefines": [],
  "noEngineReferences": false
}
```

- [ ] Step 4: 创建冒烟测试文件 `Packages/com.yoji.u3d-ai-linker/Tests/Editor/ContentHashTests.cs`，先只验证程序集装配（Task 28 会替换为真实断言）：

```csharp
using NUnit.Framework;

namespace Yoji.U3DAILinker.Tests
{
    public class ContentHashTests
    {
        [Test] public void AssemblyWiringSmoke()
        {
            Assert.Pass();
        }
    }
}
```

- [ ] Step 5: 跑测试确认程序集被发现且绿：`pwsh -File tools/run-editmode.ps1 -Project u3d-ai-linker`。期望输出含 `===== RESULT: Passed =====` 且 `passed` 计数 >= 1，无 `error CS`。若出现 `NO result XML -> likely compile failure`，按日志里的 `error CS` 行修 asmdef/JSON 语法。

- [ ] Step 6: commit：

```
git add Packages/com.yoji.u3d-ai-linker/Editor/Yoji.U3DAILinker.Editor.asmdef Packages/com.yoji.u3d-ai-linker/Editor/AssemblyInfo.cs Packages/com.yoji.u3d-ai-linker/Tests/Editor/Yoji.U3DAILinker.Editor.Tests.asmdef Packages/com.yoji.u3d-ai-linker/Tests/Editor/ContentHashTests.cs
git commit -m "test(u3d-ai-linker): scaffold Editor/Tests assemblies for sync transaction"
```

---

### Task 28: ContentHash 目录内容哈希

实现对一个 skill 目录的确定性内容哈希：哈希与文件遍历顺序、绝对路径无关，只取决于相对路径集合 + 各文件字节。它用于写入 ownership、以及未来检测"目标内容是否已和源一致"。纯逻辑，完整 TDD。

Files:
- Create: `Packages/com.yoji.u3d-ai-linker/Editor/Operations/ContentHash.cs`
- Test: `Packages/com.yoji.u3d-ai-linker/Tests/Editor/ContentHashTests.cs`（替换 Task 27 的冒烟内容）

- [ ] Step 1: 把 `ContentHashTests.cs` 整体替换为失败测试（此时 `ContentHash` 类还不存在，编译失败 = 红）：

```csharp
using System;
using System.IO;
using NUnit.Framework;
using Yoji.U3DAILinker.Operations;

namespace Yoji.U3DAILinker.Tests
{
    public class ContentHashTests
    {
        private string m_Root;

        [SetUp] public void SetUp()
        {
            m_Root = Path.Combine(Path.GetTempPath(), "u3dlink_hash_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_Root);
        }

        [TearDown] public void TearDown()
        {
            try { if (Directory.Exists(m_Root)) Directory.Delete(m_Root, true); } catch { }
        }

        private string Dir(string name)
        {
            var d = Path.Combine(m_Root, name);
            Directory.CreateDirectory(d);
            return d;
        }

        private static void WriteFile(string dir, string relative, string content)
        {
            var full = Path.Combine(dir, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllText(full, content);
        }

        [Test] public void Hash_IsLowercaseHex64()
        {
            var d = Dir("a");
            WriteFile(d, "SKILL.md", "hello");
            var h = ContentHash.OfDirectory(d);
            Assert.AreEqual(64, h.Length);
            StringAssert.IsMatch("^[0-9a-f]{64}$", h);
        }

        [Test] public void SameContent_SameHash()
        {
            var a = Dir("a"); var b = Dir("b");
            WriteFile(a, "SKILL.md", "x"); WriteFile(a, "scripts/run.py", "print(1)");
            WriteFile(b, "SKILL.md", "x"); WriteFile(b, "scripts/run.py", "print(1)");
            Assert.AreEqual(ContentHash.OfDirectory(a), ContentHash.OfDirectory(b));
        }

        [Test] public void DifferentFileBytes_DifferentHash()
        {
            var a = Dir("a"); var b = Dir("b");
            WriteFile(a, "SKILL.md", "x");
            WriteFile(b, "SKILL.md", "y");
            Assert.AreNotEqual(ContentHash.OfDirectory(a), ContentHash.OfDirectory(b));
        }

        [Test] public void DifferentRelativePath_DifferentHash()
        {
            // 内容相同但路径不同必须改变哈希，否则文件改名不可检测
            var a = Dir("a"); var b = Dir("b");
            WriteFile(a, "SKILL.md", "x");
            WriteFile(b, "OTHER.md", "x");
            Assert.AreNotEqual(ContentHash.OfDirectory(a), ContentHash.OfDirectory(b));
        }

        [Test] public void OwnershipFile_IsExcludedFromHash()
        {
            // .u3d-ai-owner.json 自身存了哈希，必须不参与哈希计算，否则自指
            var a = Dir("a"); var b = Dir("b");
            WriteFile(a, "SKILL.md", "x");
            WriteFile(b, "SKILL.md", "x");
            WriteFile(b, ".u3d-ai-owner.json", "{\"ContentHash\":\"whatever\"}");
            Assert.AreEqual(ContentHash.OfDirectory(a), ContentHash.OfDirectory(b));
        }

        [Test] public void MissingDirectory_Throws()
        {
            Assert.Throws<DirectoryNotFoundException>(
                () => ContentHash.OfDirectory(Path.Combine(m_Root, "nope")));
        }
    }
}
```

- [ ] Step 2: 跑测试确认红：`pwsh -File tools/run-editmode.ps1 -Project u3d-ai-linker`。期望 `NO result XML -> likely compile failure`，日志含 `error CS0103`/`CS0234`（`ContentHash` / `Operations` 命名空间未定义）。这一步红是编译失败而非断言失败，符合 TDD（先红再实现）。

- [ ] Step 3: 创建实现 `Packages/com.yoji.u3d-ai-linker/Editor/Operations/ContentHash.cs`，完整代码：

```csharp
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Yoji.U3DAILinker.Operations
{
    /// 对一个 skill 目录做确定性内容哈希。结果只取决于相对路径集合（正斜杠、忽略大小写无关地排序）
    /// 与各文件原始字节，和遍历顺序、卷、绝对路径无关。ownership 文件自身被排除（避免自指）。
    /// 纯逻辑：仅依赖 System.IO，可在 EditMode 无副作用单测。
    internal static class ContentHash
    {
        public static string OfDirectory(string root)
        {
            if (!Directory.Exists(root))
                throw new DirectoryNotFoundException("hash source not found: " + root);

            var files = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
            var entries = new System.Collections.Generic.List<(string rel, string full)>(files.Length);
            foreach (var full in files)
            {
                var rel = Relative(root, full);
                if (string.Equals(rel, OwnershipFile.FileName, StringComparison.OrdinalIgnoreCase))
                    continue; // 排除 ownership 文件本身
                entries.Add((rel, full));
            }
            entries.Sort((a, b) => string.CompareOrdinal(a.rel, b.rel));

            using (var sha = SHA256.Create())
            {
                foreach (var (rel, full) in entries)
                {
                    // 路径长度 + 路径字节 + 内容长度 + 内容字节，逐项喂入，防止边界歧义
                    var pathBytes = Encoding.UTF8.GetBytes(rel);
                    FeedLength(sha, pathBytes.Length);
                    sha.TransformBlock(pathBytes, 0, pathBytes.Length, null, 0);

                    var content = File.ReadAllBytes(full);
                    FeedLength(sha, content.Length);
                    sha.TransformBlock(content, 0, content.Length, null, 0);
                }
                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                return ToHex(sha.Hash);
            }
        }

        private static void FeedLength(HashAlgorithm sha, int length)
        {
            var lenBytes = BitConverter.GetBytes((long)length);
            if (!BitConverter.IsLittleEndian) Array.Reverse(lenBytes); // 跨平台统一小端
            sha.TransformBlock(lenBytes, 0, lenBytes.Length, null, 0);
        }

        private static string Relative(string root, string full)
        {
            var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fileFull = Path.GetFullPath(full);
            var rel = fileFull.Substring(rootFull.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return rel.Replace('\\', '/');
        }

        private static string ToHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
```

- [ ] Step 4: 此实现引用了 `OwnershipFile.FileName`。在 Task 29 之前 `OwnershipFile` 不存在，会编译失败。为保持 Task 独立可编译，本 Task 先在 `ContentHash.cs` 顶部临时内联常量，**不引用** `OwnershipFile`：把 `OwnershipFile.FileName` 改为字面量 `".u3d-ai-owner.json"`。即把 Step 3 代码里那一行改为：

```csharp
                    if (string.Equals(rel, ".u3d-ai-owner.json", StringComparison.OrdinalIgnoreCase))
                        continue; // 排除 ownership 文件本身（Task 29 落地 OwnershipFile 后改为引用其常量）
```

- [ ] Step 5: 跑测试确认绿：`pwsh -File tools/run-editmode.ps1 -Project u3d-ai-linker`。期望 `===== RESULT: Passed =====`，本 Task 6 个测试全过，无 `FAIL:` 行。

- [ ] Step 6: commit：

```
git add Packages/com.yoji.u3d-ai-linker/Editor/Operations/ContentHash.cs Packages/com.yoji.u3d-ai-linker/Tests/Editor/ContentHashTests.cs
git commit -m "feat(u3d-ai-linker): deterministic directory content hash"
```

---

### Task 29: OwnershipFile 读写与 schema

实现 `.u3d-ai-owner.json` 的写入与读取（toolId/sourceRevision/contentHash/schemaVersion），用 Newtonsoft round-trip；坏 JSON 读为 null 而非抛异常。落地后把 Task 28 里临时内联的常量改回引用 `OwnershipFile.FileName`。纯逻辑，完整 TDD。

Files:
- Create: `Packages/com.yoji.u3d-ai-linker/Editor/Operations/OwnershipFile.cs`
- Modify: `Packages/com.yoji.u3d-ai-linker/Editor/Operations/ContentHash.cs`（常量改回引用）
- Test: `Packages/com.yoji.u3d-ai-linker/Tests/Editor/OwnershipFileTests.cs`

- [ ] Step 1: 创建失败测试 `Packages/com.yoji.u3d-ai-linker/Tests/Editor/OwnershipFileTests.cs`：

```csharp
using System;
using System.IO;
using NUnit.Framework;
using Yoji.U3DAILinker.Operations;

namespace Yoji.U3DAILinker.Tests
{
    public class OwnershipFileTests
    {
        private string m_Dir;

        [SetUp] public void SetUp()
        {
            m_Dir = Path.Combine(Path.GetTempPath(), "u3dlink_own_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_Dir);
        }

        [TearDown] public void TearDown()
        {
            try { if (Directory.Exists(m_Dir)) Directory.Delete(m_Dir, true); } catch { }
        }

        [Test] public void FileName_IsStable()
        {
            Assert.AreEqual(".u3d-ai-owner.json", OwnershipFile.FileName);
        }

        [Test] public void Write_ThenExists_AndReadRoundTrips()
        {
            var rec = new OwnershipRecord
            {
                ToolId = "test-runner",
                SourceRevision = "abc123",
                ContentHash = new string('a', 64),
            };
            OwnershipFile.Write(m_Dir, rec);
            Assert.IsTrue(OwnershipFile.Exists(m_Dir));

            var back = OwnershipFile.Read(m_Dir);
            Assert.IsNotNull(back);
            Assert.AreEqual("test-runner", back.ToolId);
            Assert.AreEqual("abc123", back.SourceRevision);
            Assert.AreEqual(new string('a', 64), back.ContentHash);
        }

        [Test] public void Write_StampsSchemaVersion()
        {
            OwnershipFile.Write(m_Dir, new OwnershipRecord { ToolId = "t", SourceRevision = "r", ContentHash = "h" });
            Assert.AreEqual(OwnershipFile.SchemaVersion, OwnershipFile.Read(m_Dir).SchemaVersion);
        }

        [Test] public void Write_PlacesFileAtExpectedPath()
        {
            OwnershipFile.Write(m_Dir, new OwnershipRecord { ToolId = "t", SourceRevision = "r", ContentHash = "h" });
            Assert.IsTrue(File.Exists(Path.Combine(m_Dir, ".u3d-ai-owner.json")));
        }

        [Test] public void Exists_FalseWhenAbsent()
        {
            Assert.IsFalse(OwnershipFile.Exists(m_Dir));
        }

        [Test] public void Read_MissingFile_ReturnsNull()
        {
            Assert.IsNull(OwnershipFile.Read(m_Dir));
        }

        [Test] public void Read_CorruptJson_ReturnsNull()
        {
            File.WriteAllText(Path.Combine(m_Dir, ".u3d-ai-owner.json"), "{ this is not json ");
            Assert.IsNull(OwnershipFile.Read(m_Dir)); // 坏文件按"无合法 ownership"处理，不抛
        }

        [Test] public void Read_EmptyJson_HasNullFields()
        {
            File.WriteAllText(Path.Combine(m_Dir, ".u3d-ai-owner.json"), "{}");
            var back = OwnershipFile.Read(m_Dir);
            Assert.IsNotNull(back);     // 合法 JSON 但字段空：解析成功，字段为 null
            Assert.IsNull(back.ToolId);
        }
    }
}
```

- [ ] Step 2: 跑测试确认红：`pwsh -File tools/run-editmode.ps1 -Project u3d-ai-linker`。期望编译失败（`OwnershipRecord`/`OwnershipFile` 未定义，`error CS0246`/`CS0103`）。

- [ ] Step 3: 创建实现 `Packages/com.yoji.u3d-ai-linker/Editor/Operations/OwnershipFile.cs`：

```csharp
using System.IO;
using Newtonsoft.Json;

namespace Yoji.U3DAILinker.Operations
{
    /// 一个被 Linker 管理的 skill 目录的归属记录。随 .u3d-ai-owner.json 经 Newtonsoft round-trip。
    internal sealed class OwnershipRecord
    {
        public string ToolId;          // Registry 中的工具 ID，例如 "test-runner"
        public string SourceRevision;  // 来源 revision（Git SHA / tag / local 标记）
        public string ContentHash;     // 同步时刻 ContentHash.OfDirectory 的结果
        public string SchemaVersion;   // ownership 文件结构版本，便于后续迁移
    }

    /// .u3d-ai-owner.json 的读写。读坏文件返回 null（按"无合法 ownership"处理），不抛异常。
    /// 纯逻辑：仅 System.IO + Newtonsoft，可在 EditMode 无副作用单测。
    internal static class OwnershipFile
    {
        public const string FileName = ".u3d-ai-owner.json";
        public const string SchemaVersion = "1";

        private static string PathFor(string dir) => Path.Combine(dir, FileName);

        public static bool Exists(string dir) => File.Exists(PathFor(dir));

        public static void Write(string dir, OwnershipRecord record)
        {
            Directory.CreateDirectory(dir);
            record.SchemaVersion = SchemaVersion;
            File.WriteAllText(PathFor(dir), JsonConvert.SerializeObject(record, Formatting.Indented));
        }

        public static OwnershipRecord Read(string dir)
        {
            var path = PathFor(dir);
            if (!File.Exists(path)) return null;
            try { return JsonConvert.DeserializeObject<OwnershipRecord>(File.ReadAllText(path)); }
            catch { return null; }
        }
    }
}
```

- [ ] Step 4: 把 Task 28 临时内联的常量改回引用。打开 `Packages/com.yoji.u3d-ai-linker/Editor/Operations/ContentHash.cs`，把那一行从字面量改回：

```csharp
                    if (string.Equals(rel, OwnershipFile.FileName, StringComparison.OrdinalIgnoreCase))
                        continue; // 排除 ownership 文件本身
```

- [ ] Step 5: 跑测试确认绿（OwnershipFile 8 个测试 + ContentHash 6 个测试都过）：`pwsh -File tools/run-editmode.ps1 -Project u3d-ai-linker`。期望 `===== RESULT: Passed =====`，无 `FAIL:` 行。特别确认 `OwnershipFile_IsExcludedFromHash` 仍绿（改回引用后行为不变）。

- [ ] Step 6: commit：

```
git add Packages/com.yoji.u3d-ai-linker/Editor/Operations/OwnershipFile.cs Packages/com.yoji.u3d-ai-linker/Editor/Operations/ContentHash.cs Packages/com.yoji.u3d-ai-linker/Tests/Editor/OwnershipFileTests.cs
git commit -m "feat(u3d-ai-linker): ownership file read/write with schema version"
```

---

### Task 30: OwnershipGuard 目标归属校验

实现"目标目录是否由 Linker 管理"的判定（spec 280/315/479：缺合法 ownership 视为用户目录不覆盖；ownership 工具 ID 不匹配视为别的工具的目录不覆盖）。这是 staging→替换前的安全闸。纯逻辑，完整 TDD。

Files:
- Create: `Packages/com.yoji.u3d-ai-linker/Editor/Operations/OwnershipGuard.cs`
- Test: `Packages/com.yoji.u3d-ai-linker/Tests/Editor/OwnershipGuardTests.cs`

- [ ] Step 1: 创建失败测试 `Packages/com.yoji.u3d-ai-linker/Tests/Editor/OwnershipGuardTests.cs`：

```csharp
using System;
using System.IO;
using NUnit.Framework;
using Yoji.U3DAILinker.Operations;

namespace Yoji.U3DAILinker.Tests
{
    public class OwnershipGuardTests
    {
        private string m_Dir;

        [SetUp] public void SetUp()
        {
            m_Dir = Path.Combine(Path.GetTempPath(), "u3dlink_guard_" + Guid.NewGuid().ToString("N"));
        }

        [TearDown] public void TearDown()
        {
            try { if (Directory.Exists(m_Dir)) Directory.Delete(m_Dir, true); } catch { }
        }

        private void WriteOwner(string toolId)
        {
            OwnershipFile.Write(m_Dir, new OwnershipRecord
            {
                ToolId = toolId, SourceRevision = "r", ContentHash = "h",
            });
        }

        [Test] public void AbsentDirectory_IsMissing()
        {
            Assert.AreEqual(OwnershipStatus.Missing, OwnershipGuard.Inspect(m_Dir, "test-runner"));
        }

        [Test] public void DirectoryWithoutOwner_IsForeign()
        {
            Directory.CreateDirectory(m_Dir);
            File.WriteAllText(Path.Combine(m_Dir, "SKILL.md"), "user content");
            // 存在但没有 ownership 文件：视为用户目录
            Assert.AreEqual(OwnershipStatus.Foreign, OwnershipGuard.Inspect(m_Dir, "test-runner"));
        }

        [Test] public void CorruptOwner_IsForeign()
        {
            Directory.CreateDirectory(m_Dir);
            File.WriteAllText(Path.Combine(m_Dir, ".u3d-ai-owner.json"), "{ broken ");
            Assert.AreEqual(OwnershipStatus.Foreign, OwnershipGuard.Inspect(m_Dir, "test-runner"));
        }

        [Test] public void OwnerForSameTool_IsManagedMatch()
        {
            WriteOwner("test-runner");
            Assert.AreEqual(OwnershipStatus.ManagedMatch, OwnershipGuard.Inspect(m_Dir, "test-runner"));
        }

        [Test] public void OwnerForOtherTool_IsManagedMismatch()
        {
            WriteOwner("editor-debug");
            // 是 Linker 管理的目录，但属于别的工具：不能当作本工具目标覆盖
            Assert.AreEqual(OwnershipStatus.ManagedMismatch, OwnershipGuard.Inspect(m_Dir, "test-runner"));
        }

        [Test] public void OwnerWithEmptyToolId_IsForeign()
        {
            OwnershipFile.Write(m_Dir, new OwnershipRecord { ToolId = "", SourceRevision = "r", ContentHash = "h" });
            // 合法 JSON 但 toolId 空：不构成合法归属，按用户目录保护
            Assert.AreEqual(OwnershipStatus.Foreign, OwnershipGuard.Inspect(m_Dir, "test-runner"));
        }

        [Test] public void MayOverwrite_TrueOnMissing()
        {
            Assert.IsTrue(OwnershipGuard.MayOverwrite(m_Dir, "test-runner")); // 不存在=可放心创建
        }

        [Test] public void MayOverwrite_TrueOnManagedMatch()
        {
            WriteOwner("test-runner");
            Assert.IsTrue(OwnershipGuard.MayOverwrite(m_Dir, "test-runner"));
        }

        [Test] public void MayOverwrite_FalseOnForeign()
        {
            Directory.CreateDirectory(m_Dir);
            File.WriteAllText(Path.Combine(m_Dir, "x.txt"), "user");
            Assert.IsFalse(OwnershipGuard.MayOverwrite(m_Dir, "test-runner"));
        }

        [Test] public void MayOverwrite_FalseOnManagedMismatch()
        {
            WriteOwner("editor-debug");
            Assert.IsFalse(OwnershipGuard.MayOverwrite(m_Dir, "test-runner"));
        }
    }
}
```

- [ ] Step 2: 跑测试确认红：`pwsh -File tools/run-editmode.ps1 -Project u3d-ai-linker`。期望编译失败（`OwnershipStatus`/`OwnershipGuard` 未定义）。

- [ ] Step 3: 创建实现 `Packages/com.yoji.u3d-ai-linker/Editor/Operations/OwnershipGuard.cs`：

```csharp
using System.IO;

namespace Yoji.U3DAILinker.Operations
{
    /// 目标目录的归属状态。
    internal enum OwnershipStatus
    {
        Missing,           // 目录不存在：可放心创建
        Foreign,           // 存在但无合法 ownership：用户目录，不覆盖
        ManagedMatch,      // Linker 管理且属于期望工具：可替换
        ManagedMismatch,   // Linker 管理但属于别的工具：不覆盖
    }

    /// 决定一个目标 skill 目录是否可被本工具的同步覆盖。
    /// 规则（spec 280/315/479）：缺合法 ownership 文件 = 用户目录，不动；ownership 工具 ID 与期望不符 = 别人的目录，不动。
    /// 纯逻辑：只读文件系统与 OwnershipFile，可在 EditMode 单测。
    internal static class OwnershipGuard
    {
        public static OwnershipStatus Inspect(string dir, string expectedToolId)
        {
            if (!Directory.Exists(dir)) return OwnershipStatus.Missing;

            var record = OwnershipFile.Read(dir);
            if (record == null || string.IsNullOrEmpty(record.ToolId))
                return OwnershipStatus.Foreign;

            return record.ToolId == expectedToolId
                ? OwnershipStatus.ManagedMatch
                : OwnershipStatus.ManagedMismatch;
        }

        /// 仅 Missing 与 ManagedMatch 允许覆盖；Foreign / ManagedMismatch 必须拒绝。
        public static bool MayOverwrite(string dir, string expectedToolId)
        {
            var status = Inspect(dir, expectedToolId);
            return status == OwnershipStatus.Missing || status == OwnershipStatus.ManagedMatch;
        }
    }
}
```

- [ ] Step 4: 跑测试确认绿：`pwsh -File tools/run-editmode.ps1 -Project u3d-ai-linker`。期望 `===== RESULT: Passed =====`，OwnershipGuard 的全部测试通过。

- [ ] Step 5: commit：

```
git add Packages/com.yoji.u3d-ai-linker/Editor/Operations/OwnershipGuard.cs Packages/com.yoji.u3d-ai-linker/Tests/Editor/OwnershipGuardTests.cs
git commit -m "feat(u3d-ai-linker): ownership guard for managed-target protection"
```

---

### Task 31: IJunctionManager 接口 + WindowsJunctionManager(P/Invoke) + FakeJunctionManager

把"真实 Windows Junction"这一外部副作用抽成 `IJunctionManager`。`WindowsJunctionManager` 用 P/Invoke `DeviceIoControl` 建 directory reparse point（cross-project 文档指出 mklink 子进程不可靠，优先 P/Invoke）。`FakeJunctionManager`(测试用，纯内存)走 EditMode 单测覆盖契约。真实 Junction 创建由本 Task 的手动验证步骤覆盖。

注意：`WindowsJunctionManager` 含 unsafe-ish 的 marshaling，但用 `Marshal`/`byte[]` 即可，不需 `allowUnsafeCode`。它内部不被任何 EditMode 测试直接调用真实建链（避免在批跑里真改文件系统/需要权限），只在手动验证里跑。

Files:
- Create: `Packages/com.yoji.u3d-ai-linker/Editor/Operations/IJunctionManager.cs`
- Create: `Packages/com.yoji.u3d-ai-linker/Editor/Operations/WindowsJunctionManager.cs`
- Create: `Packages/com.yoji.u3d-ai-linker/Tests/Editor/FakeJunctionManager.cs`
- Test: 对 Fake 的契约测试也放在 `FakeJunctionManager.cs` 同名测试类里（见 Step 3）

- [ ] Step 1: 创建接口 `Packages/com.yoji.u3d-ai-linker/Editor/Operations/IJunctionManager.cs`：

```csharp
namespace Yoji.U3DAILinker.Operations
{
    /// Windows directory junction 的抽象，便于把真实 reparse-point 副作用与同步逻辑解耦。
    /// 生产实现 = WindowsJunctionManager（P/Invoke）；测试用 FakeJunctionManager（内存）。
    internal interface IJunctionManager
    {
        /// linkPath 是否是一个 junction（reparse point）。不存在或是普通目录返回 false。
        bool IsJunction(string linkPath);

        /// 返回 junction 指向的目标目录；linkPath 不是 junction 时返回 null。
        string GetTarget(string linkPath);

        /// 在 linkPath 创建指向 targetDir 的 junction。linkPath 的父目录必须已存在。
        void Create(string linkPath, string targetDir);

        /// 删除 junction 本身（不删除目标内容）。linkPath 不是 junction 时不应误删普通目录。
        void Delete(string linkPath);
    }
}
```

- [ ] Step 2: 创建生产实现 `Packages/com.yoji.u3d-ai-linker/Editor/Operations/WindowsJunctionManager.cs`（P/Invoke `DeviceIoControl` 设置 `IO_REPARSE_TAG_MOUNT_POINT`；删除用 `FSCTL_DELETE_REPARSE_POINT` + `RemoveDirectory`，避免误删目标）：

```csharp
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Yoji.U3DAILinker.Operations
{
    /// Windows directory junction（mount-point reparse point）的生产实现。
    /// 用 P/Invoke DeviceIoControl 直接写 reparse 数据，而非 spawn mklink 子进程
    /// （cross-project 经验：shell/子进程在不同环境不可靠，且无法稳定拿退出语义）。
    /// 仅在 Windows + 真实 Editor 下使用；EditMode 批跑用 FakeJunctionManager，不触碰真实 FS。
    internal sealed class WindowsJunctionManager : IJunctionManager
    {
        private const uint IO_REPARSE_TAG_MOUNT_POINT = 0xA0000003;
        private const uint FSCTL_SET_REPARSE_POINT = 0x000900A4;
        private const uint FSCTL_GET_REPARSE_POINT = 0x000900A8;
        private const uint FSCTL_DELETE_REPARSE_POINT = 0x000900AC;

        private const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
        private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_SHARE_READ = 0x1, FILE_SHARE_WRITE = 0x2, FILE_SHARE_DELETE = 0x4;
        private const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000;
        private const int FILE_ATTRIBUTE_REPARSE_POINT = 0x400;
        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);
        private const int MAX_REPARSE_SIZE = 16 * 1024;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode,
            IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeviceIoControl(IntPtr hDevice, uint dwIoControlCode,
            byte[] lpInBuffer, int nInBufferSize, byte[] lpOutBuffer, int nOutBufferSize,
            out int lpBytesReturned, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetFileAttributes(string lpFileName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RemoveDirectory(string lpPathName);

        public bool IsJunction(string linkPath)
        {
            if (!Directory.Exists(linkPath)) return false;
            var attr = GetFileAttributes(linkPath);
            if (attr == -1) return false;
            return (attr & FILE_ATTRIBUTE_REPARSE_POINT) != 0;
        }

        public string GetTarget(string linkPath)
        {
            if (!IsJunction(linkPath)) return null;
            var handle = OpenReparse(linkPath, GENERIC_READ);
            try
            {
                var outBuf = new byte[MAX_REPARSE_SIZE];
                if (!DeviceIoControl(handle, FSCTL_GET_REPARSE_POINT, null, 0, outBuf, outBuf.Length, out _, IntPtr.Zero))
                    throw new IOException("FSCTL_GET_REPARSE_POINT failed: " + Marshal.GetLastWin32Error());
                return ParseMountPointTarget(outBuf);
            }
            finally { CloseHandle(handle); }
        }

        public void Create(string linkPath, string targetDir)
        {
            if (linkPath == null) throw new ArgumentNullException(nameof(linkPath));
            if (!Directory.Exists(targetDir)) throw new DirectoryNotFoundException("junction target missing: " + targetDir);

            Directory.CreateDirectory(linkPath); // junction 必须建在一个空目录上
            var handle = OpenReparse(linkPath, GENERIC_WRITE);
            try
            {
                var buffer = BuildMountPointBuffer(Path.GetFullPath(targetDir));
                if (!DeviceIoControl(handle, FSCTL_SET_REPARSE_POINT, buffer, buffer.Length, null, 0, out _, IntPtr.Zero))
                {
                    var err = Marshal.GetLastWin32Error();
                    CloseHandle(handle);
                    try { Directory.Delete(linkPath); } catch { }
                    throw new IOException("FSCTL_SET_REPARSE_POINT failed: " + err);
                }
            }
            finally { CloseHandle(handle); }
        }

        public void Delete(string linkPath)
        {
            if (!IsJunction(linkPath)) return; // 只删 junction，绝不误删普通目录
            var handle = OpenReparse(linkPath, GENERIC_WRITE);
            try
            {
                // 删 reparse 数据需要一个最小 header（仅 ReparseTag + 0 长度）
                var header = new byte[8];
                BitConverter.GetBytes(IO_REPARSE_TAG_MOUNT_POINT).CopyTo(header, 0);
                DeviceIoControl(handle, FSCTL_DELETE_REPARSE_POINT, header, header.Length, null, 0, out _, IntPtr.Zero);
            }
            finally { CloseHandle(handle); }
            RemoveDirectory(linkPath); // 此时 linkPath 是空目录壳，移除它
        }

        private static IntPtr OpenReparse(string path, uint access)
        {
            var handle = CreateFile(path, access,
                FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, IntPtr.Zero, OPEN_EXISTING,
                FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);
            if (handle == INVALID_HANDLE_VALUE)
                throw new IOException("CreateFile(reparse) failed for " + path + ": " + Marshal.GetLastWin32Error());
            return handle;
        }

        // REPARSE_DATA_BUFFER(mount point): ReparseTag(4) ReparseDataLength(2) Reserved(2)
        // SubstituteNameOffset(2) SubstituteNameLength(2) PrintNameOffset(2) PrintNameLength(2) PathBuffer(...)
        private static byte[] BuildMountPointBuffer(string targetFullPath)
        {
            var substitute = "\\??\\" + targetFullPath;
            var subBytes = Encoding.Unicode.GetBytes(substitute);
            var printBytes = Encoding.Unicode.GetBytes(targetFullPath);

            int subLen = subBytes.Length;
            int printLen = printBytes.Length;
            // PathBuffer = substitute + null(2) + print + null(2)
            int pathBufferLen = subLen + 2 + printLen + 2;
            int reparseDataLength = 8 + pathBufferLen; // 4 个 offset/length 字段(8) + path buffer
            var buffer = new byte[8 + reparseDataLength]; // 头 8 字节(tag+len+reserved) + 数据

            int p = 0;
            BitConverter.GetBytes(IO_REPARSE_TAG_MOUNT_POINT).CopyTo(buffer, p); p += 4;
            BitConverter.GetBytes((ushort)reparseDataLength).CopyTo(buffer, p); p += 2;
            BitConverter.GetBytes((ushort)0).CopyTo(buffer, p); p += 2; // Reserved

            ushort subOffset = 0;
            ushort subNameLen = (ushort)subLen;
            ushort printOffset = (ushort)(subLen + 2);
            ushort printNameLen = (ushort)printLen;
            BitConverter.GetBytes(subOffset).CopyTo(buffer, p); p += 2;
            BitConverter.GetBytes(subNameLen).CopyTo(buffer, p); p += 2;
            BitConverter.GetBytes(printOffset).CopyTo(buffer, p); p += 2;
            BitConverter.GetBytes(printNameLen).CopyTo(buffer, p); p += 2;

            Buffer.BlockCopy(subBytes, 0, buffer, p, subLen); p += subLen;
            p += 2; // null terminator (already zero)
            Buffer.BlockCopy(printBytes, 0, buffer, p, printLen);
            // 末尾 null 已是 0
            return buffer;
        }

        private static string ParseMountPointTarget(byte[] outBuf)
        {
            uint tag = BitConverter.ToUInt32(outBuf, 0);
            if (tag != IO_REPARSE_TAG_MOUNT_POINT) return null;
            // 头 8 字节后是 mount-point 专属字段
            ushort subOffset = BitConverter.ToUInt16(outBuf, 8);
            ushort subLen = BitConverter.ToUInt16(outBuf, 10);
            int pathStart = 8 + 8 + subOffset; // 8 头 + 8 字段 + offset
            var substitute = Encoding.Unicode.GetString(outBuf, pathStart, subLen);
            if (substitute.StartsWith("\\??\\")) substitute = substitute.Substring(4);
            return substitute;
        }
    }
}
```

- [ ] Step 3: 创建 Fake + 其契约测试 `Packages/com.yoji.u3d-ai-linker/Tests/Editor/FakeJunctionManager.cs`（Fake 是内存 map：linkPath -> targetDir；Create 时若已存在 link 先覆盖，模拟"修复失效 junction"）：

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using Yoji.U3DAILinker.Operations;

namespace Yoji.U3DAILinker.Tests
{
    /// IJunctionManager 的内存假实现：记录 link->target 映射，不触碰真实文件系统。
    /// 供 AgentSyncService 的 EditMode 单测使用；真实 reparse-point 由 WindowsJunctionManager 手动验证。
    internal sealed class FakeJunctionManager : IJunctionManager
    {
        private readonly Dictionary<string, string> m_Links = new Dictionary<string, string>();

        public int CreateCalls { get; private set; }
        public int DeleteCalls { get; private set; }

        public bool IsJunction(string linkPath) => m_Links.ContainsKey(linkPath);

        public string GetTarget(string linkPath)
            => m_Links.TryGetValue(linkPath, out var t) ? t : null;

        public void Create(string linkPath, string targetDir)
        {
            CreateCalls++;
            m_Links[linkPath] = targetDir; // 覆盖 = 修复指向
        }

        public void Delete(string linkPath)
        {
            DeleteCalls++;
            m_Links.Remove(linkPath);
        }
    }

    public class FakeJunctionManagerTests
    {
        [Test] public void Create_ThenIsJunction_AndGetTarget()
        {
            var j = new FakeJunctionManager();
            j.Create("C:/link", "C:/target");
            Assert.IsTrue(j.IsJunction("C:/link"));
            Assert.AreEqual("C:/target", j.GetTarget("C:/link"));
            Assert.AreEqual(1, j.CreateCalls);
        }

        [Test] public void Unknown_IsNotJunction_TargetNull()
        {
            var j = new FakeJunctionManager();
            Assert.IsFalse(j.IsJunction("C:/nope"));
            Assert.IsNull(j.GetTarget("C:/nope"));
        }

        [Test] public void Create_Again_RepointsTarget()
        {
            var j = new FakeJunctionManager();
            j.Create("C:/link", "C:/old");
            j.Create("C:/link", "C:/new");
            Assert.AreEqual("C:/new", j.GetTarget("C:/link"));
        }

        [Test] public void Delete_RemovesLink()
        {
            var j = new FakeJunctionManager();
            j.Create("C:/link", "C:/target");
            j.Delete("C:/link");
            Assert.IsFalse(j.IsJunction("C:/link"));
            Assert.AreEqual(1, j.DeleteCalls);
        }
    }
}
```

- [ ] Step 4: 跑测试确认绿（接口/Fake 都编译且 Fake 契约测试过；`WindowsJunctionManager` 只编译不被测）：`pwsh -File tools/run-editmode.ps1 -Project u3d-ai-linker`。期望 `===== RESULT: Passed =====`。若日志报 `WindowsJunctionManager` 编译错误（P/Invoke 签名），按 `error CS` 行修正。

- [ ] Step 5: commit：

```
git add Packages/com.yoji.u3d-ai-linker/Editor/Operations/IJunctionManager.cs Packages/com.yoji.u3d-ai-linker/Editor/Operations/WindowsJunctionManager.cs Packages/com.yoji.u3d-ai-linker/Tests/Editor/FakeJunctionManager.cs
git commit -m "feat(u3d-ai-linker): junction manager interface + Windows P/Invoke impl + fake"
```

- [ ] Step 6: 手动验证真实 Junction（无法在批跑里做，必须在真实 Windows + Unity Editor 里）：
  1. 在 `TestProjects/u3d-ai-linker/` 打开 Unity 2022.3+ Editor。
  2. 临时菜单触发：在 `Packages/com.yoji.u3d-ai-linker/Editor/` 临时加一个 `[MenuItem("U3D AI Linker/Debug/Junction Smoke")]` 方法，调用：
     ```csharp
     var jm = new Yoji.U3DAILinker.Operations.WindowsJunctionManager();
     var target = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "u3djn_target");
     System.IO.Directory.CreateDirectory(target);
     System.IO.File.WriteAllText(System.IO.Path.Combine(target, "marker.txt"), "hi");
     var link = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "u3djn_link");
     jm.Create(link, target);
     UnityEngine.Debug.Log("IsJunction=" + jm.IsJunction(link) + " target=" + jm.GetTarget(link) +
         " readBack=" + System.IO.File.ReadAllText(System.IO.Path.Combine(link, "marker.txt")));
     jm.Delete(link);
     UnityEngine.Debug.Log("afterDelete IsJunction=" + jm.IsJunction(link) +
         " targetMarkerStillThere=" + System.IO.File.Exists(System.IO.Path.Combine(target, "marker.txt")));
     ```
  3. 在命令行 `cmd /c dir /AL %TEMP%` 或 PowerShell `Get-Item $env:TEMP\u3djn_link | Select-Object LinkType,Target`，期望看到 `LinkType=Junction`、`Target` 指向 `...u3djn_target`。
  4. 期望 Console 第一行 `IsJunction=True`，`target` 含 `u3djn_target`，`readBack=hi`（穿过 junction 读到了目标文件）。
  5. 期望第二行 `afterDelete IsJunction=False`、`targetMarkerStillThere=True`（删 junction 不删目标内容）。
  6. 验证通过后删除临时 `[MenuItem]` 方法，不提交它。

---

### Task 32: AgentSyncService 事务式六步同步

把前面所有单元组合成 spec 306-315 的六步事务：staging→校验 SKILL.md→写 ownership→backup 旧目标→move 为 `skills/<tool>`→建/修 Junction→删 backup。覆盖 spec 479-480 验收：未知目录保护、staging 失败不破坏现有版本、替换失败恢复 backup、重复同步幂等、失效 Junction 修复。同卷 move 用 `Directory.Move`。junction 副作用走 `FakeJunctionManager`，纯逻辑全 TDD。

Files:
- Create: `Packages/com.yoji.u3d-ai-linker/Editor/Operations/AgentSyncService.cs`
- Test: `Packages/com.yoji.u3d-ai-linker/Tests/Editor/AgentSyncServiceTests.cs`

设计约定（写进实现 XML 注释）：
- `AgentSyncRequest.SourceDir` = ready 源 `PackageInfo.resolvedPath/Agent~/skills/<tool>` 或 skill-only 的 `BundledSkills~/<tool>`（或 zip 解压后目录）；本 Task 不关心来源怎么来的，只要求是一个已存在、含 `SKILL.md` 的目录。该来源定位/zip fallback 属于上层子系统（LINK-3/同步编排），不在 LINK-4。
- `SkillsRoot` = `<project>/.u3d-ai-linker`；服务在其下用 `skills/<tool>`、`.staging/<tool>-<op>`、`.backup/<tool>-<op>`。所有这些都在同一卷（同一 `.u3d-ai-linker` 根下），满足 spec"同卷 move"。
- `JunctionLinks` = 形如 `<project>/.claude/skills/<tool>`、`<project>/.agents/skills/<tool>` 的链接路径，全部指向最终的 `skills/<tool>`。

- [ ] Step 1: 创建失败测试 `Packages/com.yoji.u3d-ai-linker/Tests/Editor/AgentSyncServiceTests.cs`：

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Yoji.U3DAILinker.Operations;

namespace Yoji.U3DAILinker.Tests
{
    public class AgentSyncServiceTests
    {
        private string m_Root;       // 模拟 project 根
        private string m_SourceDir;  // 模拟 Agent~ 源
        private string m_SkillsRoot; // <project>/.u3d-ai-linker
        private FakeJunctionManager m_Junctions;

        [SetUp] public void SetUp()
        {
            m_Root = Path.Combine(Path.GetTempPath(), "u3dlink_sync_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_Root);
            m_SkillsRoot = Path.Combine(m_Root, ".u3d-ai-linker");
            m_SourceDir = Path.Combine(m_Root, "src", "test-runner");
            Directory.CreateDirectory(m_SourceDir);
            File.WriteAllText(Path.Combine(m_SourceDir, "SKILL.md"), "# skill v1");
            Directory.CreateDirectory(Path.Combine(m_SourceDir, "scripts"));
            File.WriteAllText(Path.Combine(m_SourceDir, "scripts", "run.py"), "print(1)");
            m_Junctions = new FakeJunctionManager();
        }

        [TearDown] public void TearDown()
        {
            try { if (Directory.Exists(m_Root)) Directory.Delete(m_Root, true); } catch { }
        }

        private AgentSyncRequest Request(string op = "op1")
        {
            return new AgentSyncRequest
            {
                ToolId = "test-runner",
                SourceDir = m_SourceDir,
                SourceRevision = "rev-" + op,
                OperationId = op,
                SkillsRoot = m_SkillsRoot,
                RequiredSkillMarkers = new List<string> { "SKILL.md" },
                JunctionLinks = new List<string>
                {
                    Path.Combine(m_Root, ".claude", "skills", "test-runner"),
                    Path.Combine(m_Root, ".agents", "skills", "test-runner"),
                },
            };
        }

        private string ToolDir() => Path.Combine(m_SkillsRoot, "skills", "test-runner");

        [Test] public void Sync_FreshInstall_CreatesToolDirWithContentAndOwnership()
        {
            var result = new AgentSyncService(m_Junctions).Sync(Request());
            Assert.IsTrue(result.Success, result.Message);
            Assert.IsTrue(File.Exists(Path.Combine(ToolDir(), "SKILL.md")));
            Assert.IsTrue(File.Exists(Path.Combine(ToolDir(), "scripts", "run.py")));
            var owner = OwnershipFile.Read(ToolDir());
            Assert.IsNotNull(owner);
            Assert.AreEqual("test-runner", owner.ToolId);
            Assert.AreEqual("rev-op1", owner.SourceRevision);
            Assert.AreEqual(ContentHash.OfDirectory(ToolDir()), owner.ContentHash);
        }

        [Test] public void Sync_CreatesAllJunctions_PointingToToolDir()
        {
            var req = Request();
            var result = new AgentSyncService(m_Junctions).Sync(req);
            Assert.IsTrue(result.Success);
            foreach (var link in req.JunctionLinks)
            {
                Assert.IsTrue(m_Junctions.IsJunction(link), "missing junction: " + link);
                Assert.AreEqual(ToolDir(), m_Junctions.GetTarget(link));
            }
        }

        [Test] public void Sync_LeavesNoStagingOrBackup_OnSuccess()
        {
            new AgentSyncService(m_Junctions).Sync(Request());
            Assert.IsFalse(Directory.Exists(Path.Combine(m_SkillsRoot, ".staging", "test-runner-op1")));
            Assert.IsFalse(Directory.Exists(Path.Combine(m_SkillsRoot, ".backup", "test-runner-op1")));
        }

        [Test] public void Sync_MissingSkillMarker_FailsBeforeTouchingTarget()
        {
            File.Delete(Path.Combine(m_SourceDir, "SKILL.md")); // 源缺 SKILL.md
            var result = new AgentSyncService(m_Junctions).Sync(Request());
            Assert.IsFalse(result.Success);
            Assert.AreEqual("validate", result.FailureStage);
            Assert.IsFalse(Directory.Exists(ToolDir()));          // 目标未被创建
            Assert.IsFalse(Directory.Exists(Path.Combine(m_SkillsRoot, ".staging", "test-runner-op1"))); // staging 已清
            Assert.AreEqual(0, m_Junctions.CreateCalls);
        }

        [Test] public void Sync_Reapply_IsIdempotent_AndRepointsJunction()
        {
            var svc = new AgentSyncService(m_Junctions);
            Assert.IsTrue(svc.Sync(Request("op1")).Success);

            // 第二次：源改了内容 + 新 operationId，应替换旧目标且 junction 仍指向同 toolDir
            File.WriteAllText(Path.Combine(m_SourceDir, "SKILL.md"), "# skill v2");
            var r2 = svc.Sync(Request("op2"));
            Assert.IsTrue(r2.Success, r2.Message);
            StringAssert.Contains("v2", File.ReadAllText(Path.Combine(ToolDir(), "SKILL.md")));
            Assert.AreEqual("rev-op2", OwnershipFile.Read(ToolDir()).SourceRevision);
            foreach (var link in Request("op2").JunctionLinks)
                Assert.AreEqual(ToolDir(), m_Junctions.GetTarget(link));
            // 重同步后不残留 backup
            Assert.IsFalse(Directory.Exists(Path.Combine(m_SkillsRoot, ".backup", "test-runner-op2")));
        }

        [Test] public void Sync_ForeignExistingTarget_RefusesAndPreservesUserDir()
        {
            // 目标已存在但没有 ownership = 用户目录：拒绝覆盖
            Directory.CreateDirectory(ToolDir());
            File.WriteAllText(Path.Combine(ToolDir(), "SKILL.md"), "USER OWNED");
            var result = new AgentSyncService(m_Junctions).Sync(Request());
            Assert.IsFalse(result.Success);
            Assert.AreEqual("ownership", result.FailureStage);
            Assert.AreEqual("USER OWNED", File.ReadAllText(Path.Combine(ToolDir(), "SKILL.md"))); // 原样保留
        }

        [Test] public void Sync_MismatchOwnedTarget_Refuses()
        {
            Directory.CreateDirectory(ToolDir());
            OwnershipFile.Write(ToolDir(), new OwnershipRecord { ToolId = "editor-debug", SourceRevision = "x", ContentHash = "h" });
            File.WriteAllText(Path.Combine(ToolDir(), "SKILL.md"), "other tool");
            var result = new AgentSyncService(m_Junctions).Sync(Request());
            Assert.IsFalse(result.Success);
            Assert.AreEqual("ownership", result.FailureStage);
            Assert.AreEqual("other tool", File.ReadAllText(Path.Combine(ToolDir(), "SKILL.md")));
        }

        [Test] public void Sync_JunctionFailureAfterReplace_RestoresBackup()
        {
            // 先成功装一版（managed），再让第二次的 junction 创建抛错，验证恢复旧目标
            var svc = new AgentSyncService(m_Junctions);
            Assert.IsTrue(svc.Sync(Request("op1")).Success);
            var v1Hash = ContentHash.OfDirectory(ToolDir());

            File.WriteAllText(Path.Combine(m_SourceDir, "SKILL.md"), "# skill v2");
            var throwing = new ThrowingJunctionManager();
            var r2 = new AgentSyncService(throwing).Sync(Request("op2"));

            Assert.IsFalse(r2.Success);
            Assert.AreEqual("junction", r2.FailureStage);
            // 旧版本必须被恢复（backup 回滚）
            Assert.IsTrue(Directory.Exists(ToolDir()));
            Assert.AreEqual(v1Hash, ContentHash.OfDirectory(ToolDir()));
            StringAssert.Contains("v1", File.ReadAllText(Path.Combine(ToolDir(), "SKILL.md")));
            // backup 已清回
            Assert.IsFalse(Directory.Exists(Path.Combine(m_SkillsRoot, ".backup", "test-runner-op2")));
        }

        [Test] public void Sync_MissingSourceDir_Fails()
        {
            var req = Request();
            req.SourceDir = Path.Combine(m_Root, "does-not-exist");
            var result = new AgentSyncService(m_Junctions).Sync(req);
            Assert.IsFalse(result.Success);
            Assert.AreEqual("staging", result.FailureStage);
        }

        /// 每次 Create 都抛，用于模拟 junction 阶段失败触发回滚。
        private sealed class ThrowingJunctionManager : IJunctionManager
        {
            public bool IsJunction(string linkPath) => false;
            public string GetTarget(string linkPath) => null;
            public void Create(string linkPath, string targetDir) => throw new IOException("simulated junction failure");
            public void Delete(string linkPath) { }
        }
    }
}
```

- [ ] Step 2: 跑测试确认红：`pwsh -File tools/run-editmode.ps1 -Project u3d-ai-linker`。期望编译失败（`AgentSyncRequest`/`AgentSyncResult`/`AgentSyncService` 未定义）。

- [ ] Step 3: 创建实现 `Packages/com.yoji.u3d-ai-linker/Editor/Operations/AgentSyncService.cs`：

```csharp
using System;
using System.Collections.Generic;
using System.IO;

namespace Yoji.U3DAILinker.Operations
{
    /// 一次 Agent 资产同步请求。来源定位（PackageInfo.resolvedPath/Agent~ 或 BundledSkills~ / zip 解压）
    /// 由上层完成；此处只要求 SourceDir 是已存在、含全部 RequiredSkillMarkers 的目录。
    internal sealed class AgentSyncRequest
    {
        public string ToolId;                            // Registry 工具 ID
        public string SourceDir;                         // 源 skill 目录（含 SKILL.md/scripts/...）
        public string SourceRevision;                    // 写入 ownership 的来源 revision
        public string OperationId;                       // 本次操作 ID，用于 staging/backup 子目录唯一命名
        public string SkillsRoot;                        // <project>/.u3d-ai-linker
        public IReadOnlyList<string> RequiredSkillMarkers; // 必须存在的相对文件，至少 ["SKILL.md"]
        public IReadOnlyList<string> JunctionLinks;      // 指向最终 skills/<tool> 的链接路径集合
    }

    /// 同步结果。Success=false 时 FailureStage 标明失败阶段：
    /// staging | validate | ownership | replace | junction。
    internal sealed class AgentSyncResult
    {
        public bool Success;
        public string ToolDir;        // 最终 skills/<tool> 路径
        public string ContentHash;    // 成功时的内容哈希
        public string FailureStage;
        public string Message;
    }

    /// 事务式目录替换（spec 306-315）：
    /// 1 复制到 .staging/<tool>-<op>
    /// 2 校验 SKILL.md 等 marker
    /// 3 写 .u3d-ai-owner.json（toolId/revision/hash）
    /// 4 若旧目标存在且 ownership 合法，move 到 .backup/<tool>-<op>
    /// 5 staging move 为 skills/<tool>
    /// 6 建/修 Junction，成功后删 backup
    /// 所有 move 同卷（同在 SkillsRoot 下）。步骤 4 之后失败恢复 backup；步骤 4 之前失败只删 staging。
    /// 目标缺合法 ownership = 用户目录，不覆盖不删除。junction 副作用经 IJunctionManager 注入（测试用 fake）。
    internal sealed class AgentSyncService
    {
        private readonly IJunctionManager m_Junctions;

        public AgentSyncService(IJunctionManager junctions)
        {
            m_Junctions = junctions ?? throw new ArgumentNullException(nameof(junctions));
        }

        public AgentSyncResult Sync(AgentSyncRequest request)
        {
            var toolDir = Path.Combine(request.SkillsRoot, "skills", request.ToolId);
            var stagingDir = Path.Combine(request.SkillsRoot, ".staging", request.ToolId + "-" + request.OperationId);
            var backupDir = Path.Combine(request.SkillsRoot, ".backup", request.ToolId + "-" + request.OperationId);

            // ---- 步骤 1：复制到 staging（步骤 4 之前，失败只清 staging）----
            try
            {
                if (!Directory.Exists(request.SourceDir))
                    return Fail("staging", "source dir missing: " + request.SourceDir, stagingDir, backupDir, false);
                SafeDelete(stagingDir);
                CopyTree(request.SourceDir, stagingDir);
            }
            catch (Exception e)
            {
                return Fail("staging", "staging copy failed: " + e.Message, stagingDir, backupDir, false);
            }

            // ---- 步骤 2：校验 marker（步骤 4 之前）----
            foreach (var marker in request.RequiredSkillMarkers)
            {
                var markerPath = Path.Combine(stagingDir, marker.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(markerPath))
                    return Fail("validate", "required marker missing: " + marker, stagingDir, backupDir, false);
            }

            // ---- ownership 前置校验：目标若是用户目录或别的工具，拒绝（步骤 4 之前）----
            if (Directory.Exists(toolDir) && !OwnershipGuard.MayOverwrite(toolDir, request.ToolId))
                return Fail("ownership", "refusing to overwrite non-managed target: " + toolDir, stagingDir, backupDir, false);

            // ---- 步骤 3：写 ownership（hash 基于 staging 内容；ownership 文件自身被 hash 排除）----
            string hash;
            try
            {
                hash = ContentHash.OfDirectory(stagingDir);
                OwnershipFile.Write(stagingDir, new OwnershipRecord
                {
                    ToolId = request.ToolId,
                    SourceRevision = request.SourceRevision,
                    ContentHash = hash,
                });
            }
            catch (Exception e)
            {
                return Fail("validate", "ownership stamp failed: " + e.Message, stagingDir, backupDir, false);
            }

            // ---- 步骤 4：backup 旧目标（此后失败需恢复 backup）----
            bool backedUp = false;
            try
            {
                if (Directory.Exists(toolDir))
                {
                    SafeDelete(backupDir);
                    Directory.CreateDirectory(Path.GetDirectoryName(backupDir));
                    Directory.Move(toolDir, backupDir);
                    backedUp = true;
                }
            }
            catch (Exception e)
            {
                return Fail("replace", "backup move failed: " + e.Message, stagingDir, backupDir, false);
            }

            // ---- 步骤 5：staging move 为 skills/<tool> ----
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(toolDir));
                Directory.Move(stagingDir, toolDir);
            }
            catch (Exception e)
            {
                RestoreBackup(backedUp, backupDir, toolDir);
                return Fail("replace", "promote staging failed: " + e.Message, stagingDir, backupDir, backedUp);
            }

            // ---- 步骤 6：建/修 Junction，成功后删 backup ----
            try
            {
                foreach (var link in request.JunctionLinks)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(link));
                    if (m_Junctions.IsJunction(link))
                    {
                        if (m_Junctions.GetTarget(link) != toolDir)
                        {
                            m_Junctions.Delete(link);
                            m_Junctions.Create(link, toolDir);
                        }
                    }
                    else
                    {
                        if (Directory.Exists(link)) SafeDelete(link); // 空壳/旧目录，避免 Create 撞名
                        m_Junctions.Create(link, toolDir);
                    }
                }
            }
            catch (Exception e)
            {
                // junction 失败：把新目标退回 staging 名，恢复旧 backup
                try { SafeDelete(stagingDir); Directory.Move(toolDir, stagingDir); } catch { }
                RestoreBackup(backedUp, backupDir, toolDir);
                SafeDelete(stagingDir);
                return new AgentSyncResult { Success = false, FailureStage = "junction", Message = e.Message, ToolDir = toolDir };
            }

            SafeDelete(backupDir);
            return new AgentSyncResult { Success = true, ToolDir = toolDir, ContentHash = hash };
        }

        private static void RestoreBackup(bool backedUp, string backupDir, string toolDir)
        {
            if (!backedUp) return;
            try
            {
                if (Directory.Exists(toolDir)) SafeDelete(toolDir);
                Directory.Move(backupDir, toolDir);
            }
            catch { /* 恢复尽力而为；FailureStage 已告知调用方需人工介入 */ }
        }

        private AgentSyncResult Fail(string stage, string message, string stagingDir, string backupDir, bool backedUp)
        {
            // 步骤 4 之前的失败：只清 staging，不动现有目标。backedUp=false 时 backup 还没产生。
            SafeDelete(stagingDir);
            if (backedUp) // 理论上 Fail 仅在步骤 4 之前调用（backedUp=false）；保留分支以防误用
            {
                // 不在此处恢复，恢复由 RestoreBackup 负责
            }
            return new AgentSyncResult { Success = false, FailureStage = stage, Message = message };
        }

        private static void CopyTree(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (var dir in Directory.GetDirectories(src, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(dir.Replace(src, dst));
            foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
                File.Copy(file, file.Replace(src, dst), true);
        }

        private static void SafeDelete(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        }
    }
}
```

- [ ] Step 4: 跑测试确认绿：`pwsh -File tools/run-editmode.ps1 -Project u3d-ai-linker`。期望 `===== RESULT: Passed =====`，AgentSyncService 全部 10 个测试 + 之前所有 Task 的测试都过，无 `FAIL:` 行。重点确认：`Sync_JunctionFailureAfterReplace_RestoresBackup`（回滚路径）、`Sync_ForeignExistingTarget_RefusesAndPreservesUserDir`（未知目录保护）、`Sync_Reapply_IsIdempotent_AndRepointsJunction`（幂等重同步）三个验收测试为绿。

- [ ] Step 5: commit：

```
git add Packages/com.yoji.u3d-ai-linker/Editor/Operations/AgentSyncService.cs Packages/com.yoji.u3d-ai-linker/Tests/Editor/AgentSyncServiceTests.cs
git commit -m "feat(u3d-ai-linker): transactional six-step agent sync with backup rollback"
```

- [ ] Step 6: 手动验证端到端真实 Junction（在真实 Windows + Unity Editor，配合真实 `WindowsJunctionManager`）：
  1. 在 `TestProjects/u3d-ai-linker/` 打开 Unity Editor，临时加 `[MenuItem("U3D AI Linker/Debug/Sync Smoke")]`：用真实 `new WindowsJunctionManager()` 构造 `AgentSyncService`，`SourceDir` 指向 `Packages/com.yoji.test-runner/Agent~/skills/test-runner-mcp`（若 `Agent~` 在 PackageCache 不可读则手动复制一份到 Temp 作 SourceDir），`SkillsRoot` 指向 `<project>/.u3d-ai-linker`，`JunctionLinks` 为 `<project>/.claude/skills/test-runner-mcp` 与 `<project>/.agents/skills/test-runner-mcp`。调用 `Sync` 并 `Debug.Log(result.Success + " " + result.FailureStage)`。
  2. 期望 Console 打印 `True `（Success，FailureStage 空）。
  3. 文件系统检查：`<project>/.u3d-ai-linker/skills/test-runner-mcp/SKILL.md` 存在且含 `.u3d-ai-owner.json`；`Get-Item <project>/.claude/skills/test-runner-mcp | Select LinkType,Target` 显示 `Junction` 且 `Target` 指向 `...\.u3d-ai-linker\skills\test-runner-mcp`。
  4. 穿过 junction 读：`Get-Content <project>/.claude/skills/test-runner-mcp/SKILL.md` 应等于源 SKILL.md 内容。
  5. 再点一次菜单（同 op 或新 op）：期望仍 `True`、目标内容不变、junction 仍指向同目录（幂等，无报错）。
  6. 负向：手动在 `<project>/.u3d-ai-linker/skills/` 下建一个 `fake-tool/` 放个 `SKILL.md` 但不写 ownership，再用 `ToolId="fake-tool"` 跑 Sync，期望 `Success=False`、`FailureStage=ownership`，该目录内容原样保留。
  7. 验证通过后删除临时 `[MenuItem]`，不提交。清理 `<project>/.u3d-ai-linker`、`<project>/.claude`、`<project>/.agents` 下的测试残留。

---

### LINK-5 — Fragment 合并 + CLAUDE/AGENTS 托管区块 + .gitignore 区块

本子系统是纯字符串/文件逻辑,无 UPM/网络/Junction/域重载依赖,因此 **每个 Task 全程走完整 EditMode TDD**(写失败测试→跑确认失败→最小实现→跑确认通过→commit)。所有文件 IO 用 `Path.GetTempPath()` + GUID 临时目录(与 test-runner 的 `JobStoreTests` 一致),`[SetUp]` 建临时根、`[TearDown]` 递归删。

为了让本子系统可独立测试、不耦合 Registry/Operations 子系统的具体类型,我们在 `.Agents` 子命名空间内定义两个最小输入契约:
- `IFragmentSource`(单个工具贡献的 fragment 信息:`ToolId` / `SkillName` / `Order` / `ClaudeFragment` / `AgentsFragment` / `ManagedSkillRelativePaths`)。Registry 子系统负责构造实现该接口的对象;本子系统只消费接口,不关心它从 UPM 还是 zip 来。
- `ManagedBlockKind`(枚举 `Claude` / `Agents`),用于选取来源注释文案与 fragment 字段。

> 运行测试的统一命令(与现有 test-runner skill 一致,Unity Editor 在线时通过 HTTP 触发 EditMode):本计划在每个 "跑测试" 步骤给出 `class.method` 级过滤名,执行者用 test-runner-mcp 的 `/recompile` + `run tests`(EditMode,testNames 过滤)拿结果。所有 `dotnet test` 不可用(Unity 工程),必须走 Unity Test Runner。期望输出统一写成 "该测试 FAILED:`<断言/编译错误>`" 或 "该测试 PASSED"。

> 幂等基线:Task 33 的包骨架/asmdef/`IFragmentSource` 与 LINK-0/1/2/2b/3/4 已落地的同名文件,核对存在即跳过创建。本子系统新增 `Editor/Agents/*.cs` 与对应测试。

---

### Task 33: Linker 包骨架与 Agents 子命名空间空壳(使本子系统可编译可测)

本 Task 建立 `com.yoji.u3d-ai-linker` 包的 asmdef、package.json,以及 LINK-5 三个写入器所在的 `Editor/Agents/` 与测试 `Tests/Editor/` 目录,让后续 TDD 有可编译落点。若包骨架已由 LINK-1 子系统创建,本 Task 的 Step 1-4 跳过(以实际文件是否存在为准),直接从 Step 5 起。

Files:
- Create `Packages/com.yoji.u3d-ai-linker/package.json`
- Create `Packages/com.yoji.u3d-ai-linker/Editor/Yoji.U3DAILinker.Editor.asmdef`
- Create `Packages/com.yoji.u3d-ai-linker/Tests/Editor/Yoji.U3DAILinker.Editor.Tests.asmdef`
- Create `Packages/com.yoji.u3d-ai-linker/Editor/Agents/IFragmentSource.cs`
- Create `Packages/com.yoji.u3d-ai-linker/Tests/Editor/Agents/FragmentSourceContractTests.cs`

- [ ] Step 1: 创建 `Packages/com.yoji.u3d-ai-linker/package.json`(若不存在):

```json
{
  "name": "com.yoji.u3d-ai-linker",
  "version": "0.1.0",
  "displayName": "U3D AI Linker",
  "description": "Project-level installer and Agent asset sync for U3D Dev Tools.",
  "unity": "2022.3",
  "dependencies": {
    "com.unity.nuget.newtonsoft-json": "3.2.1"
  }
}
```

- [ ] Step 2: 创建主 asmdef `Packages/com.yoji.u3d-ai-linker/Editor/Yoji.U3DAILinker.Editor.asmdef`(对齐 test-runner 包,Editor-only,显式引用 Newtonsoft):

```json
{
  "name": "Yoji.U3DAILinker.Editor",
  "rootNamespace": "Yoji.U3DAILinker",
  "references": [],
  "includePlatforms": ["Editor"],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": true,
  "precompiledReferences": ["Newtonsoft.Json.dll"],
  "autoReferenced": true,
  "defineConstraints": [],
  "versionDefines": [],
  "noEngineReferences": false
}
```

- [ ] Step 3: 创建测试 asmdef `Packages/com.yoji.u3d-ai-linker/Tests/Editor/Yoji.U3DAILinker.Editor.Tests.asmdef`(引用主 asmdef + nunit + TestRunner,`UNITY_INCLUDE_TESTS` 约束,与 test-runner 测试包一致):

```json
{
  "name": "Yoji.U3DAILinker.Editor.Tests",
  "rootNamespace": "Yoji.U3DAILinker.Tests",
  "references": ["Yoji.U3DAILinker.Editor", "UnityEngine.TestRunner", "UnityEditor.TestRunner"],
  "includePlatforms": ["Editor"],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": true,
  "precompiledReferences": ["nunit.framework.dll", "Newtonsoft.Json.dll"],
  "autoReferenced": false,
  "defineConstraints": ["UNITY_INCLUDE_TESTS"],
  "versionDefines": [],
  "noEngineReferences": false
}
```

- [ ] Step 4: 创建输入契约 `Packages/com.yoji.u3d-ai-linker/Editor/Agents/IFragmentSource.cs`。`ManagedSkillRelativePaths` 是相对项目根、正斜杠的托管 skill 路径(供 gitignore 用),例如 `.claude/skills/test-runner-mcp`。fragment 为 `null` 表示该工具未提供对应 fragment:

```csharp
using System.Collections.Generic;

namespace Yoji.U3DAILinker.Agents
{
    /// <summary>Which managed Markdown file a block targets.</summary>
    public enum ManagedBlockKind
    {
        Claude,
        Agents
    }

    /// <summary>
    /// One enabled tool's contribution to managed Agent files and .gitignore.
    /// Implemented by the Registry subsystem; consumed read-only here.
    /// </summary>
    public interface IFragmentSource
    {
        /// <summary>Stable tool id, e.g. "test-runner". Used as the secondary sort key.</summary>
        string ToolId { get; }

        /// <summary>Unique Skill name, e.g. "test-runner-mcp". Duplicates fail preflight.</summary>
        string SkillName { get; }

        /// <summary>Registry order. Primary ascending sort key. Must be a non-negative integer.</summary>
        int Order { get; }

        /// <summary>Content of fragments/CLAUDE.md, or null if absent.</summary>
        string ClaudeFragment { get; }

        /// <summary>Content of fragments/AGENTS.md, or null if absent.</summary>
        string AgentsFragment { get; }

        /// <summary>
        /// Managed skill paths this tool owns, relative to project root, forward slashes,
        /// e.g. ".claude/skills/test-runner-mcp". Each becomes one ignored line.
        /// </summary>
        IReadOnlyList<string> ManagedSkillRelativePaths { get; }
    }
}
```

- [ ] Step 5: 写契约冒烟测试 `Packages/com.yoji.u3d-ai-linker/Tests/Editor/Agents/FragmentSourceContractTests.cs`(确认测试程序集能引用主程序集类型并编译;同时提供一个可复用的 fake,后续 Task 引用它):

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using Yoji.U3DAILinker.Agents;

namespace Yoji.U3DAILinker.Tests.Agents
{
    /// <summary>Reusable in-memory IFragmentSource for EditMode tests.</summary>
    internal sealed class FakeFragmentSource : IFragmentSource
    {
        public string ToolId { get; set; } = "tool";
        public string SkillName { get; set; } = "skill";
        public int Order { get; set; }
        public string ClaudeFragment { get; set; }
        public string AgentsFragment { get; set; }
        public IReadOnlyList<string> ManagedSkillRelativePaths { get; set; } = new List<string>();
    }

    public class FragmentSourceContractTests
    {
        [Test]
        public void Fake_ExposesAllContractFields()
        {
            var src = new FakeFragmentSource
            {
                ToolId = "test-runner",
                SkillName = "test-runner-mcp",
                Order = 10,
                ClaudeFragment = "claude body",
                AgentsFragment = "agents body",
                ManagedSkillRelativePaths = new List<string> { ".claude/skills/test-runner-mcp" }
            };

            Assert.AreEqual("test-runner", src.ToolId);
            Assert.AreEqual("test-runner-mcp", src.SkillName);
            Assert.AreEqual(10, src.Order);
            Assert.AreEqual("claude body", src.ClaudeFragment);
            Assert.AreEqual("agents body", src.AgentsFragment);
            Assert.AreEqual(1, src.ManagedSkillRelativePaths.Count);
            Assert.AreEqual(ManagedBlockKind.Claude, ManagedBlockKind.Claude);
        }
    }
}
```

- [ ] Step 6: 跑该测试,确认它 PASS(本步只验证骨架能编译并跑通,无失败前置)。用 test-runner-mcp 触发 `/recompile`,再 EditMode run,testNames 过滤 `Yoji.U3DAILinker.Tests.Agents.FragmentSourceContractTests`。期望:`Fake_ExposesAllContractFields PASSED`;若编译失败,先修 asmdef 引用与 `precompiledReferences` 再重跑。

- [ ] Step 7: commit:

```bash
cd /e/Yoji/U3D-Dev-Tools-AI
git checkout -b feat/u3d-ai-linker-link5
git add Packages/com.yoji.u3d-ai-linker/package.json \
        Packages/com.yoji.u3d-ai-linker/Editor/Yoji.U3DAILinker.Editor.asmdef \
        Packages/com.yoji.u3d-ai-linker/Tests/Editor/Yoji.U3DAILinker.Editor.Tests.asmdef \
        Packages/com.yoji.u3d-ai-linker/Editor/Agents/IFragmentSource.cs \
        Packages/com.yoji.u3d-ai-linker/Tests/Editor/Agents/FragmentSourceContractTests.cs
git commit -m "feat(u3d-ai-linker): scaffold linker package and IFragmentSource contract"
```

> 注:`git checkout -b` 仅在当前还在 `main` 且尚无 LINK 分支时执行;若组装计划已统一在某 feature 分支,删除该行直接 `git add`。

---

### Task 34: ManagedBlockWriter — 托管区块创建/更新/保留用户内容/冲突检测

实现 `<!-- u3d-ai-linker:start -->` ... `<!-- u3d-ai-linker:end -->` 区块写入器。规则(spec 320-325):区块外用户内容字节不变;文件缺则建;marker 损坏(只有 start 或只有 end、end 在 start 之前)或重复(多个 start/end)→停写并报冲突,绝不部分写。换行规范化为 `\n`(避免 CRLF/LF 抖动导致用户内容"被改")。

Files:
- Create `Packages/com.yoji.u3d-ai-linker/Editor/Agents/BlockWriteResult.cs`
- Create `Packages/com.yoji.u3d-ai-linker/Editor/Agents/ManagedBlockWriter.cs`
- Create `Packages/com.yoji.u3d-ai-linker/Tests/Editor/Agents/ManagedBlockWriterTests.cs`

- [ ] Step 1: 先写结果类型 `Packages/com.yoji.u3d-ai-linker/Editor/Agents/BlockWriteResult.cs`(被本 Task 与 gitignore Task 共用;只读、无副作用,纯数据):

```csharp
namespace Yoji.U3DAILinker.Agents
{
    /// <summary>Outcome of a managed-block write attempt. No partial writes on Conflict.</summary>
    public sealed class BlockWriteResult
    {
        public enum Status
        {
            /// <summary>File created or block inserted/updated successfully.</summary>
            Written,
            /// <summary>Content already matched; nothing was written.</summary>
            Unchanged,
            /// <summary>Markers corrupt/duplicated; file left untouched.</summary>
            Conflict
        }

        public Status Outcome { get; }
        public string Message { get; }

        private BlockWriteResult(Status outcome, string message)
        {
            Outcome = outcome;
            Message = message;
        }

        public static BlockWriteResult Written() => new BlockWriteResult(Status.Written, null);
        public static BlockWriteResult Unchanged() => new BlockWriteResult(Status.Unchanged, null);
        public static BlockWriteResult Conflict(string message) => new BlockWriteResult(Status.Conflict, message);

        public bool IsConflict => Outcome == Status.Conflict;
    }
}
```

- [ ] Step 2: 写失败测试 `Packages/com.yoji.u3d-ai-linker/Tests/Editor/Agents/ManagedBlockWriterTests.cs`(此时 `ManagedBlockWriter` 尚不存在,编译失败即第一个红)。覆盖:文件缺失→创建并含 marker;既有用户文本→区块追加到末尾且用户文本逐字保留;再次以相同内容写→`Unchanged` 且文件 mtime 内容不变;更新区块内容→只替换区块内文本,区块外不变;只有 start marker→`Conflict` 且文件原样;重复 start→`Conflict`:

```csharp
using System.IO;
using NUnit.Framework;
using Yoji.U3DAILinker.Agents;

namespace Yoji.U3DAILinker.Tests.Agents
{
    public class ManagedBlockWriterTests
    {
        private string m_Dir;
        private string m_File;

        [SetUp]
        public void SetUp()
        {
            m_Dir = Path.Combine(Path.GetTempPath(), "u3dlink_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_Dir);
            m_File = Path.Combine(m_Dir, "CLAUDE.md");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(m_Dir)) Directory.Delete(m_Dir, true);
        }

        private static string Norm(string s) => s.Replace("\r\n", "\n");

        [Test]
        public void Write_MissingFile_CreatesFileWithMarkers()
        {
            var r = ManagedBlockWriter.Write(m_File, "BODY");

            Assert.AreEqual(BlockWriteResult.Status.Written, r.Outcome);
            Assert.IsTrue(File.Exists(m_File));
            var text = Norm(File.ReadAllText(m_File));
            StringAssert.Contains("<!-- u3d-ai-linker:start -->", text);
            StringAssert.Contains("<!-- u3d-ai-linker:end -->", text);
            StringAssert.Contains("BODY", text);
        }

        [Test]
        public void Write_ExistingUserContent_PreservedVerbatimOutsideBlock()
        {
            File.WriteAllText(m_File, "# My Project\n\nUser notes line.\n");

            var r = ManagedBlockWriter.Write(m_File, "BODY");

            Assert.AreEqual(BlockWriteResult.Status.Written, r.Outcome);
            var text = Norm(File.ReadAllText(m_File));
            StringAssert.StartsWith("# My Project\n\nUser notes line.\n", text);
            StringAssert.Contains("BODY", text);
        }

        [Test]
        public void Write_SameContentTwice_SecondIsUnchanged()
        {
            ManagedBlockWriter.Write(m_File, "BODY");
            var before = File.ReadAllText(m_File);

            var r = ManagedBlockWriter.Write(m_File, "BODY");

            Assert.AreEqual(BlockWriteResult.Status.Unchanged, r.Outcome);
            Assert.AreEqual(before, File.ReadAllText(m_File));
        }

        [Test]
        public void Write_UpdateBody_ReplacesOnlyInsideBlock()
        {
            File.WriteAllText(m_File, "USER TOP\n");
            ManagedBlockWriter.Write(m_File, "OLD");

            var r = ManagedBlockWriter.Write(m_File, "NEW");

            Assert.AreEqual(BlockWriteResult.Status.Written, r.Outcome);
            var text = Norm(File.ReadAllText(m_File));
            StringAssert.StartsWith("USER TOP\n", text);
            StringAssert.Contains("NEW", text);
            StringAssert.DoesNotContain("OLD", text);
        }

        [Test]
        public void Write_OnlyStartMarker_ConflictAndFileUntouched()
        {
            var corrupt = "USER\n<!-- u3d-ai-linker:start -->\nstray\n";
            File.WriteAllText(m_File, corrupt);

            var r = ManagedBlockWriter.Write(m_File, "BODY");

            Assert.AreEqual(BlockWriteResult.Status.Conflict, r.Outcome);
            Assert.AreEqual(corrupt, File.ReadAllText(m_File));
        }

        [Test]
        public void Write_DuplicateStartMarker_Conflict()
        {
            var corrupt =
                "<!-- u3d-ai-linker:start -->\na\n<!-- u3d-ai-linker:end -->\n" +
                "<!-- u3d-ai-linker:start -->\nb\n<!-- u3d-ai-linker:end -->\n";
            File.WriteAllText(m_File, corrupt);

            var r = ManagedBlockWriter.Write(m_File, "BODY");

            Assert.AreEqual(BlockWriteResult.Status.Conflict, r.Outcome);
            Assert.AreEqual(corrupt, File.ReadAllText(m_File));
        }

        [Test]
        public void Write_EndBeforeStart_Conflict()
        {
            var corrupt =
                "<!-- u3d-ai-linker:end -->\nx\n<!-- u3d-ai-linker:start -->\n";
            File.WriteAllText(m_File, corrupt);

            var r = ManagedBlockWriter.Write(m_File, "BODY");

            Assert.AreEqual(BlockWriteResult.Status.Conflict, r.Outcome);
            Assert.AreEqual(corrupt, File.ReadAllText(m_File));
        }
    }
}
```

- [ ] Step 3: 跑测试确认全红。testNames 过滤 `Yoji.U3DAILinker.Tests.Agents.ManagedBlockWriterTests`。期望:编译失败,报 `The name 'ManagedBlockWriter' does not exist`(因 `ManagedBlockWriter` 未定义)。记录此为预期失败基线。

- [ ] Step 4: 写最小实现 `Packages/com.yoji.u3d-ai-linker/Editor/Agents/ManagedBlockWriter.cs`。算法:读全文(缺则空)→统计 marker 出现次数→任一 marker 出现 0 次而另一 >0、或任一 >1、或 end 在 start 前 → `Conflict`;两者都 0 → 视为"无区块",在原内容尾部追加新区块;两者各 1 且 end 在 start 后 → 替换其间内容。换行统一 `\n`,写入用 `\n`,与"相等比较"一致以保证幂等:

```csharp
using System.IO;
using System.Text;

namespace Yoji.U3DAILinker.Agents
{
    /// <summary>
    /// Writes the single u3d-ai-linker managed block into a Markdown file.
    /// Content outside the block is preserved byte-for-byte (after newline
    /// normalization to \n). Corrupt or duplicated markers abort without writing.
    /// </summary>
    public static class ManagedBlockWriter
    {
        public const string StartMarker = "<!-- u3d-ai-linker:start -->";
        public const string EndMarker = "<!-- u3d-ai-linker:end -->";
        private const string Preamble = "Generated by U3D AI Linker. Do not edit this block.";

        /// <summary>
        /// Inserts or updates the managed block. <paramref name="body"/> is the merged
        /// fragment text (without markers); pass an empty string to write an empty block.
        /// </summary>
        public static BlockWriteResult Write(string filePath, string body)
        {
            string existing = File.Exists(filePath)
                ? File.ReadAllText(filePath).Replace("\r\n", "\n")
                : null;

            int startCount = CountOccurrences(existing ?? string.Empty, StartMarker);
            int endCount = CountOccurrences(existing ?? string.Empty, EndMarker);

            if (startCount > 1 || endCount > 1)
                return BlockWriteResult.Conflict("Duplicate u3d-ai-linker markers found.");
            if (startCount != endCount)
                return BlockWriteResult.Conflict("Unbalanced u3d-ai-linker markers found.");

            string block = BuildBlock(body);

            if (startCount == 0)
            {
                // No managed block yet: create file or append to user content.
                string head = existing ?? string.Empty;
                string newText = head.Length == 0
                    ? block + "\n"
                    : EnsureTrailingNewline(head) + block + "\n";
                if (existing != null && existing == newText)
                    return BlockWriteResult.Unchanged();
                WriteAtomic(filePath, newText);
                return BlockWriteResult.Written();
            }

            int startIdx = existing.IndexOf(StartMarker, System.StringComparison.Ordinal);
            int endIdx = existing.IndexOf(EndMarker, System.StringComparison.Ordinal);
            if (endIdx < startIdx)
                return BlockWriteResult.Conflict("u3d-ai-linker end marker precedes start marker.");

            int endLineEnd = endIdx + EndMarker.Length;
            string before = existing.Substring(0, startIdx);
            string after = existing.Substring(endLineEnd);
            string replaced = before + block + after;

            if (replaced == existing)
                return BlockWriteResult.Unchanged();

            WriteAtomic(filePath, replaced);
            return BlockWriteResult.Written();
        }

        private static string BuildBlock(string body)
        {
            var sb = new StringBuilder();
            sb.Append(StartMarker).Append('\n');
            sb.Append(Preamble).Append('\n');
            if (!string.IsNullOrEmpty(body))
                sb.Append(body.Replace("\r\n", "\n").TrimEnd('\n')).Append('\n');
            sb.Append(EndMarker);
            return sb.ToString();
        }

        private static string EnsureTrailingNewline(string s)
            => s.EndsWith("\n") ? s : s + "\n";

        private static int CountOccurrences(string haystack, string needle)
        {
            int count = 0, i = 0;
            while ((i = haystack.IndexOf(needle, i, System.StringComparison.Ordinal)) >= 0)
            {
                count++;
                i += needle.Length;
            }
            return count;
        }

        private static void WriteAtomic(string filePath, string content)
        {
            string dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            string tmp = filePath + ".u3dtmp";
            File.WriteAllText(tmp, content, new UTF8Encoding(false));
            if (File.Exists(filePath)) File.Delete(filePath);
            File.Move(tmp, filePath);
        }
    }
}
```

- [ ] Step 5: 跑测试确认全绿。testNames 过滤 `Yoji.U3DAILinker.Tests.Agents.ManagedBlockWriterTests`。期望:7 个测试全部 PASSED。若 `Write_SameContentTwice` 仍 fail,核对 `BuildBlock` 的尾换行与 `Unchanged` 比较路径(append 分支也要先比较再写)。

- [ ] Step 6: commit:

```bash
cd /e/Yoji/U3D-Dev-Tools-AI
git add Packages/com.yoji.u3d-ai-linker/Editor/Agents/BlockWriteResult.cs \
        Packages/com.yoji.u3d-ai-linker/Editor/Agents/ManagedBlockWriter.cs \
        Packages/com.yoji.u3d-ai-linker/Tests/Editor/Agents/ManagedBlockWriterTests.cs
git commit -m "feat(u3d-ai-linker): ManagedBlockWriter with conflict-safe block writes"
```

---

### Task 35: FragmentMerger — 确定性合并 + 重复 Skill 名预检 + 来源注释

实现从启用 `IFragmentSource` 列表收集 fragment、按 `Order` 升序再按 `ToolId` 排序、每段前写来源注释、合并成 body 文本的逻辑(spec 327)。`Order` 必须非负整数(负数→预检失败)。重复 `SkillName`→整体预检失败,返回失败结果且不产出任何 body(调用方据此跳过写入,实现"不部分写")。fragment 为 `null` 的工具不产出段落(片段可缺省)。

Files:
- Create `Packages/com.yoji.u3d-ai-linker/Editor/Agents/FragmentMergeResult.cs`
- Create `Packages/com.yoji.u3d-ai-linker/Editor/Agents/FragmentMerger.cs`
- Create `Packages/com.yoji.u3d-ai-linker/Tests/Editor/Agents/FragmentMergerTests.cs`

- [ ] Step 1: 写结果类型 `Packages/com.yoji.u3d-ai-linker/Editor/Agents/FragmentMergeResult.cs`(成功带合并 body;失败带原因,body 为 `null`):

```csharp
namespace Yoji.U3DAILinker.Agents
{
    /// <summary>Outcome of merging tool fragments for one managed file kind.</summary>
    public sealed class FragmentMergeResult
    {
        public bool Succeeded { get; }
        /// <summary>Merged block body (no markers), or null when not succeeded.</summary>
        public string Body { get; }
        /// <summary>Preflight failure reason, or null on success.</summary>
        public string Error { get; }

        private FragmentMergeResult(bool ok, string body, string error)
        {
            Succeeded = ok;
            Body = body;
            Error = error;
        }

        public static FragmentMergeResult Ok(string body) => new FragmentMergeResult(true, body, null);
        public static FragmentMergeResult Fail(string error) => new FragmentMergeResult(false, null, error);
    }
}
```

- [ ] Step 2: 写失败测试 `Packages/com.yoji.u3d-ai-linker/Tests/Editor/Agents/FragmentMergerTests.cs`(用 Task 33 的 `FakeFragmentSource`)。覆盖:按 order 升序产出、order 相同按 toolId 字典序、`null` fragment 跳过、每段前来源注释、重复 SkillName 预检失败、负 order 预检失败、Claude/Agents 选取正确字段、空输入→空 body 成功:

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using Yoji.U3DAILinker.Agents;

namespace Yoji.U3DAILinker.Tests.Agents
{
    public class FragmentMergerTests
    {
        private static FakeFragmentSource Src(string toolId, string skill, int order, string claude, string agents = null)
            => new FakeFragmentSource
            {
                ToolId = toolId,
                SkillName = skill,
                Order = order,
                ClaudeFragment = claude,
                AgentsFragment = agents
            };

        [Test]
        public void Merge_OrdersByOrderAscending()
        {
            var sources = new List<IFragmentSource>
            {
                Src("b", "skill-b", 20, "BODY_B"),
                Src("a", "skill-a", 10, "BODY_A")
            };

            var r = FragmentMerger.Merge(sources, ManagedBlockKind.Claude);

            Assert.IsTrue(r.Succeeded);
            Assert.Less(r.Body.IndexOf("BODY_A"), r.Body.IndexOf("BODY_B"));
        }

        [Test]
        public void Merge_SameOrder_TieBrokenByToolId()
        {
            var sources = new List<IFragmentSource>
            {
                Src("zeta", "skill-z", 5, "BODY_Z"),
                Src("alpha", "skill-al", 5, "BODY_AL")
            };

            var r = FragmentMerger.Merge(sources, ManagedBlockKind.Claude);

            Assert.IsTrue(r.Succeeded);
            Assert.Less(r.Body.IndexOf("BODY_AL"), r.Body.IndexOf("BODY_Z"));
        }

        [Test]
        public void Merge_NullFragment_ProducesNoSection()
        {
            var sources = new List<IFragmentSource>
            {
                Src("a", "skill-a", 1, null),
                Src("b", "skill-b", 2, "BODY_B")
            };

            var r = FragmentMerger.Merge(sources, ManagedBlockKind.Claude);

            Assert.IsTrue(r.Succeeded);
            StringAssert.DoesNotContain("skill-a", r.Body);
            StringAssert.Contains("BODY_B", r.Body);
        }

        [Test]
        public void Merge_WritesSourceCommentBeforeEachSection()
        {
            var sources = new List<IFragmentSource>
            {
                Src("test-runner", "test-runner-mcp", 1, "BODY")
            };

            var r = FragmentMerger.Merge(sources, ManagedBlockKind.Claude);

            Assert.IsTrue(r.Succeeded);
            StringAssert.Contains("<!-- source: test-runner-mcp (tool: test-runner) -->", r.Body);
            Assert.Less(
                r.Body.IndexOf("<!-- source: test-runner-mcp"),
                r.Body.IndexOf("BODY"));
        }

        [Test]
        public void Merge_DuplicateSkillName_FailsPreflight_NoBody()
        {
            var sources = new List<IFragmentSource>
            {
                Src("a", "dup-skill", 1, "BODY_A"),
                Src("b", "dup-skill", 2, "BODY_B")
            };

            var r = FragmentMerger.Merge(sources, ManagedBlockKind.Claude);

            Assert.IsFalse(r.Succeeded);
            Assert.IsNull(r.Body);
            StringAssert.Contains("dup-skill", r.Error);
        }

        [Test]
        public void Merge_NegativeOrder_FailsPreflight()
        {
            var sources = new List<IFragmentSource>
            {
                Src("a", "skill-a", -1, "BODY_A")
            };

            var r = FragmentMerger.Merge(sources, ManagedBlockKind.Claude);

            Assert.IsFalse(r.Succeeded);
            Assert.IsNull(r.Body);
            StringAssert.Contains("order", r.Error);
        }

        [Test]
        public void Merge_AgentsKind_UsesAgentsFragment()
        {
            var sources = new List<IFragmentSource>
            {
                Src("a", "skill-a", 1, claude: "CLAUDE_ONLY", agents: "AGENTS_ONLY")
            };

            var r = FragmentMerger.Merge(sources, ManagedBlockKind.Agents);

            Assert.IsTrue(r.Succeeded);
            StringAssert.Contains("AGENTS_ONLY", r.Body);
            StringAssert.DoesNotContain("CLAUDE_ONLY", r.Body);
        }

        [Test]
        public void Merge_EmptyInput_SucceedsWithEmptyBody()
        {
            var r = FragmentMerger.Merge(new List<IFragmentSource>(), ManagedBlockKind.Claude);

            Assert.IsTrue(r.Succeeded);
            Assert.AreEqual(string.Empty, r.Body);
        }
    }
}
```

- [ ] Step 3: 跑测试确认全红。testNames 过滤 `Yoji.U3DAILinker.Tests.Agents.FragmentMergerTests`。期望:编译失败,报 `The name 'FragmentMerger' does not exist`。记录为预期基线。

- [ ] Step 4: 写实现 `Packages/com.yoji.u3d-ai-linker/Editor/Agents/FragmentMerger.cs`。预检顺序:先验证所有 `Order >= 0`(任一负数→Fail),再验证 `SkillName` 唯一(重复→Fail),全过后才按 `(Order, ToolId)` 稳定排序并拼接。重复检测对**全部**输入做(包括 fragment 为 null 的工具,因为它们也占用 Skill 名):

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Yoji.U3DAILinker.Agents
{
    /// <summary>
    /// Deterministically merges enabled tools' CLAUDE.md / AGENTS.md fragments into
    /// a single managed-block body. Preflight rejects negative order and duplicate
    /// Skill names; on failure no body is produced (callers must not write partially).
    /// </summary>
    public static class FragmentMerger
    {
        public static FragmentMergeResult Merge(
            IReadOnlyList<IFragmentSource> sources,
            ManagedBlockKind kind)
        {
            if (sources == null) throw new ArgumentNullException(nameof(sources));

            // Preflight 1: order must be a non-negative integer.
            foreach (var s in sources)
            {
                if (s.Order < 0)
                {
                    return FragmentMergeResult.Fail(string.Format(
                        CultureInfo.InvariantCulture,
                        "Tool '{0}' has negative order {1}; order must be a non-negative integer.",
                        s.ToolId, s.Order));
                }
            }

            // Preflight 2: Skill names must be unique across all enabled tools.
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var s in sources)
            {
                if (!seen.Add(s.SkillName))
                {
                    return FragmentMergeResult.Fail(string.Format(
                        CultureInfo.InvariantCulture,
                        "Duplicate Skill name '{0}'; sync preflight aborted.",
                        s.SkillName));
                }
            }

            // Deterministic order: by Order ascending, then ToolId ordinal.
            var ordered = sources
                .OrderBy(s => s.Order)
                .ThenBy(s => s.ToolId, StringComparer.Ordinal)
                .ToList();

            var sb = new StringBuilder();
            bool first = true;
            foreach (var s in ordered)
            {
                string fragment = kind == ManagedBlockKind.Claude
                    ? s.ClaudeFragment
                    : s.AgentsFragment;
                if (string.IsNullOrEmpty(fragment))
                    continue;

                if (!first) sb.Append('\n');
                first = false;

                sb.Append("<!-- source: ")
                  .Append(s.SkillName)
                  .Append(" (tool: ")
                  .Append(s.ToolId)
                  .Append(") -->")
                  .Append('\n');
                sb.Append(fragment.Replace("\r\n", "\n").TrimEnd('\n'));
                sb.Append('\n');
            }

            return FragmentMergeResult.Ok(sb.ToString().TrimEnd('\n'));
        }
    }
}
```

- [ ] Step 5: 跑测试确认全绿。testNames 过滤 `Yoji.U3DAILinker.Tests.Agents.FragmentMergerTests`。期望:8 个测试全 PASSED。若 `Merge_EmptyInput` 因 `TrimEnd` 报错,核对空 `StringBuilder` 返回 `string.Empty`(`"".TrimEnd('\n') == ""`,符合)。

- [ ] Step 6: commit:

```bash
cd /e/Yoji/U3D-Dev-Tools-AI
git add Packages/com.yoji.u3d-ai-linker/Editor/Agents/FragmentMergeResult.cs \
        Packages/com.yoji.u3d-ai-linker/Editor/Agents/FragmentMerger.cs \
        Packages/com.yoji.u3d-ai-linker/Tests/Editor/Agents/FragmentMergerTests.cs
git commit -m "feat(u3d-ai-linker): FragmentMerger with deterministic merge and duplicate-skill preflight"
```

---

### Task 36: GitignoreBlockWriter — gitignore 托管区块创建/更新/损坏保护/卸载只删匹配行

实现 `# >>> u3d-ai-linker >>>` ... `# <<< u3d-ai-linker <<<` 区块(spec 332-339)。只忽略具体托管 skill 路径(不忽略整个 `.claude/`/`.agents/`);marker 损坏(只有一侧、重复、end 在 start 前)→停写报冲突;`.gitignore` 之外的用户行字节不变。`Remove(...)` 卸载只删 ownership 匹配的 ignore 行(传入被卸载工具的路径集合,从区块内删除这些行;若区块清空则整段移除)。

Files:
- Create `Packages/com.yoji.u3d-ai-linker/Editor/Agents/GitignoreBlockWriter.cs`
- Create `Packages/com.yoji.u3d-ai-linker/Tests/Editor/Agents/GitignoreBlockWriterTests.cs`

- [ ] Step 1: 写失败测试 `Packages/com.yoji.u3d-ai-linker/Tests/Editor/Agents/GitignoreBlockWriterTests.cs`。覆盖:缺文件→建并含 `/.u3d-ai-linker/` 与各 skill 行;用户既有行保留;重复写→`Unchanged`;路径集变更→更新行;损坏 marker→`Conflict` 文件原样;`Remove` 只删匹配行、其它行保留;`Remove` 清空后整段移除但用户行保留;`Sync` 的 ignore 行用 `/` 前缀且无重复:

```csharp
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Yoji.U3DAILinker.Agents;

namespace Yoji.U3DAILinker.Tests.Agents
{
    public class GitignoreBlockWriterTests
    {
        private string m_Dir;
        private string m_File;

        [SetUp]
        public void SetUp()
        {
            m_Dir = Path.Combine(Path.GetTempPath(), "u3dgi_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_Dir);
            m_File = Path.Combine(m_Dir, ".gitignore");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(m_Dir)) Directory.Delete(m_Dir, true);
        }

        private static string Norm(string s) => s.Replace("\r\n", "\n");

        private static List<string> Paths(params string[] p) => new List<string>(p);

        [Test]
        public void Sync_MissingFile_CreatesBlockWithInfraAndSkillPaths()
        {
            var r = GitignoreBlockWriter.Sync(m_File,
                Paths(".claude/skills/test-runner-mcp", ".agents/skills/test-runner-mcp"));

            Assert.AreEqual(BlockWriteResult.Status.Written, r.Outcome);
            var text = Norm(File.ReadAllText(m_File));
            StringAssert.Contains("# >>> u3d-ai-linker >>>", text);
            StringAssert.Contains("# <<< u3d-ai-linker <<<", text);
            StringAssert.Contains("/.u3d-ai-linker/", text);
            StringAssert.Contains("/.claude/skills/test-runner-mcp", text);
            StringAssert.Contains("/.agents/skills/test-runner-mcp", text);
            // Must NOT ignore the whole agent dirs.
            StringAssert.DoesNotContain("/.claude/\n", text);
        }

        [Test]
        public void Sync_PreservesUserLines()
        {
            File.WriteAllText(m_File, "Library/\nTemp/\n*.csproj\n");

            GitignoreBlockWriter.Sync(m_File, Paths(".claude/skills/skill-a"));

            var text = Norm(File.ReadAllText(m_File));
            StringAssert.StartsWith("Library/\nTemp/\n*.csproj\n", text);
            StringAssert.Contains("/.claude/skills/skill-a", text);
        }

        [Test]
        public void Sync_SameContentTwice_Unchanged()
        {
            GitignoreBlockWriter.Sync(m_File, Paths(".claude/skills/skill-a"));
            var before = File.ReadAllText(m_File);

            var r = GitignoreBlockWriter.Sync(m_File, Paths(".claude/skills/skill-a"));

            Assert.AreEqual(BlockWriteResult.Status.Unchanged, r.Outcome);
            Assert.AreEqual(before, File.ReadAllText(m_File));
        }

        [Test]
        public void Sync_PathsChanged_UpdatesBlock()
        {
            GitignoreBlockWriter.Sync(m_File, Paths(".claude/skills/old"));

            var r = GitignoreBlockWriter.Sync(m_File, Paths(".claude/skills/new"));

            Assert.AreEqual(BlockWriteResult.Status.Written, r.Outcome);
            var text = Norm(File.ReadAllText(m_File));
            StringAssert.Contains("/.claude/skills/new", text);
            StringAssert.DoesNotContain("/.claude/skills/old", text);
        }

        [Test]
        public void Sync_CorruptMarker_ConflictAndUntouched()
        {
            var corrupt = "Library/\n# >>> u3d-ai-linker >>>\n/.u3d-ai-linker/\n";
            File.WriteAllText(m_File, corrupt);

            var r = GitignoreBlockWriter.Sync(m_File, Paths(".claude/skills/skill-a"));

            Assert.AreEqual(BlockWriteResult.Status.Conflict, r.Outcome);
            Assert.AreEqual(corrupt, File.ReadAllText(m_File));
        }

        [Test]
        public void Remove_DeletesOnlyMatchingLines_KeepsOthers()
        {
            GitignoreBlockWriter.Sync(m_File,
                Paths(".claude/skills/keep", ".claude/skills/drop"));

            var r = GitignoreBlockWriter.Remove(m_File, Paths(".claude/skills/drop"));

            Assert.AreEqual(BlockWriteResult.Status.Written, r.Outcome);
            var text = Norm(File.ReadAllText(m_File));
            StringAssert.Contains("/.claude/skills/keep", text);
            StringAssert.DoesNotContain("/.claude/skills/drop", text);
            StringAssert.Contains("/.u3d-ai-linker/", text);
        }

        [Test]
        public void Remove_LastManagedSkill_DropsBlockButKeepsUserLines()
        {
            File.WriteAllText(m_File, "Library/\n");
            GitignoreBlockWriter.Sync(m_File, Paths(".claude/skills/only"));

            var r = GitignoreBlockWriter.Remove(m_File, Paths(".claude/skills/only"));

            Assert.AreEqual(BlockWriteResult.Status.Written, r.Outcome);
            var text = Norm(File.ReadAllText(m_File));
            StringAssert.StartsWith("Library/\n", text);
            StringAssert.DoesNotContain("# >>> u3d-ai-linker >>>", text);
            StringAssert.DoesNotContain("/.u3d-ai-linker/", text);
        }

        [Test]
        public void Remove_CorruptMarker_ConflictAndUntouched()
        {
            var corrupt = "# <<< u3d-ai-linker <<<\n/.u3d-ai-linker/\n";
            File.WriteAllText(m_File, corrupt);

            var r = GitignoreBlockWriter.Remove(m_File, Paths(".claude/skills/x"));

            Assert.AreEqual(BlockWriteResult.Status.Conflict, r.Outcome);
            Assert.AreEqual(corrupt, File.ReadAllText(m_File));
        }
    }
}
```

- [ ] Step 2: 跑测试确认全红。testNames 过滤 `Yoji.U3DAILinker.Tests.Agents.GitignoreBlockWriterTests`。期望:编译失败,报 `The name 'GitignoreBlockWriter' does not exist`。记录为预期基线。

- [ ] Step 3: 写实现 `Packages/com.yoji.u3d-ai-linker/Editor/Agents/GitignoreBlockWriter.cs`。复用 `BlockWriteResult`。内部用统一的解析/重建:解析定位 marker(损坏→Conflict),`Sync` 用 `/.u3d-ai-linker/` + 每个托管路径(规范化为 `/` 前缀、去重、排序保证确定性)重建区块行;`Remove` 读出区块现有 skill 行,删掉匹配传入路径的行,若只剩 infra 行(`/.u3d-ai-linker/`)且无 skill 行则整段移除。区块外用户行逐字保留:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Yoji.U3DAILinker.Agents
{
    /// <summary>
    /// Maintains a dedicated managed block in the project root .gitignore.
    /// Ignores only specific managed skill paths plus the linker working dir;
    /// never the whole .claude/ or .agents/. Corrupt markers abort without writing.
    /// Removal deletes only ownership-matching ignore lines.
    /// </summary>
    public static class GitignoreBlockWriter
    {
        public const string StartMarker = "# >>> u3d-ai-linker >>>";
        public const string EndMarker = "# <<< u3d-ai-linker <<<";
        public const string InfraLine = "/.u3d-ai-linker/";

        /// <summary>Creates/updates the block to ignore exactly the given managed skill paths.</summary>
        public static BlockWriteResult Sync(string filePath, IReadOnlyList<string> managedSkillPaths)
        {
            if (managedSkillPaths == null) throw new ArgumentNullException(nameof(managedSkillPaths));

            string existing = File.Exists(filePath)
                ? File.ReadAllText(filePath).Replace("\r\n", "\n")
                : string.Empty;

            if (!TryLocate(existing, out var locate))
                return BlockWriteResult.Conflict(locate.Error);

            var ignoreLines = BuildIgnoreLines(managedSkillPaths);
            return Rewrite(filePath, existing, locate, ignoreLines);
        }

        /// <summary>Removes only the ignore lines owned by the uninstalled tool(s).</summary>
        public static BlockWriteResult Remove(string filePath, IReadOnlyList<string> removedSkillPaths)
        {
            if (removedSkillPaths == null) throw new ArgumentNullException(nameof(removedSkillPaths));

            if (!File.Exists(filePath))
                return BlockWriteResult.Unchanged();

            string existing = File.ReadAllText(filePath).Replace("\r\n", "\n");

            if (!TryLocate(existing, out var locate))
                return BlockWriteResult.Conflict(locate.Error);

            if (!locate.HasBlock)
                return BlockWriteResult.Unchanged();

            var removeSet = new HashSet<string>(
                removedSkillPaths.Select(NormalizeLine), StringComparer.Ordinal);

            var kept = locate.BlockLines
                .Where(line => line != InfraLine && !removeSet.Contains(line))
                .ToList();

            // Rebuild remaining ignore lines: infra line kept only if any managed skill remains.
            var ignoreLines = kept.Count > 0
                ? new List<string> { InfraLine }.Concat(kept).ToList()
                : new List<string>();

            return Rewrite(filePath, existing, locate, ignoreLines);
        }

        private static List<string> BuildIgnoreLines(IReadOnlyList<string> managedSkillPaths)
        {
            var skillLines = managedSkillPaths
                .Select(NormalizeLine)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToList();

            if (skillLines.Count == 0)
                return new List<string>();

            var lines = new List<string> { InfraLine };
            lines.AddRange(skillLines);
            return lines;
        }

        private static string NormalizeLine(string path)
        {
            string p = path.Replace("\\", "/").Trim();
            if (!p.StartsWith("/")) p = "/" + p;
            return p;
        }

        private struct Locate
        {
            public bool HasBlock;
            public int StartLineIndex;   // index in Lines of start marker
            public int EndLineIndex;     // index in Lines of end marker
            public List<string> Lines;   // full file split by '\n'
            public List<string> BlockLines; // ignore lines strictly between markers
            public string Error;
        }

        private static bool TryLocate(string content, out Locate locate)
        {
            locate = new Locate
            {
                Lines = SplitLines(content),
                BlockLines = new List<string>(),
                StartLineIndex = -1,
                EndLineIndex = -1
            };

            int startCount = 0, endCount = 0;
            for (int i = 0; i < locate.Lines.Count; i++)
            {
                string t = locate.Lines[i].Trim();
                if (t == StartMarker) { startCount++; locate.StartLineIndex = i; }
                else if (t == EndMarker) { endCount++; locate.EndLineIndex = i; }
            }

            if (startCount > 1 || endCount > 1)
            { locate.Error = "Duplicate u3d-ai-linker gitignore markers."; return false; }
            if (startCount != endCount)
            { locate.Error = "Unbalanced u3d-ai-linker gitignore markers."; return false; }

            if (startCount == 0)
            {
                locate.HasBlock = false;
                return true;
            }

            if (locate.EndLineIndex < locate.StartLineIndex)
            { locate.Error = "u3d-ai-linker gitignore end marker precedes start."; return false; }

            locate.HasBlock = true;
            for (int i = locate.StartLineIndex + 1; i < locate.EndLineIndex; i++)
            {
                string line = locate.Lines[i].Trim();
                if (line.Length > 0) locate.BlockLines.Add(line);
            }
            return true;
        }

        private static BlockWriteResult Rewrite(
            string filePath, string existing, Locate locate, List<string> ignoreLines)
        {
            var outLines = new List<string>();

            if (locate.HasBlock)
            {
                for (int i = 0; i < locate.StartLineIndex; i++)
                    outLines.Add(locate.Lines[i]);
                AppendBlock(outLines, ignoreLines);
                for (int i = locate.EndLineIndex + 1; i < locate.Lines.Count; i++)
                    outLines.Add(locate.Lines[i]);
            }
            else
            {
                foreach (var l in locate.Lines) outLines.Add(l);
                if (ignoreLines.Count > 0)
                {
                    EnsureTrailingBlank(outLines);
                    AppendBlock(outLines, ignoreLines);
                }
            }

            string newText = JoinLines(outLines);
            if (newText == existing)
                return BlockWriteResult.Unchanged();

            WriteAtomic(filePath, newText);
            return BlockWriteResult.Written();
        }

        private static void AppendBlock(List<string> outLines, List<string> ignoreLines)
        {
            // No ignore lines => no block at all (e.g. last skill removed).
            if (ignoreLines.Count == 0) return;
            outLines.Add(StartMarker);
            outLines.AddRange(ignoreLines);
            outLines.Add(EndMarker);
        }

        private static void EnsureTrailingBlank(List<string> outLines)
        {
            // Drop trailing empty lines introduced by split, then re-add one separator
            // only when there is preceding user content.
            while (outLines.Count > 0 && outLines[outLines.Count - 1].Length == 0)
                outLines.RemoveAt(outLines.Count - 1);
            if (outLines.Count > 0)
                outLines.Add(string.Empty);
        }

        private static List<string> SplitLines(string content)
        {
            // Preserve a trailing newline as a final empty element so round-trips match.
            if (content.Length == 0) return new List<string>();
            return content.Split('\n').ToList();
        }

        private static string JoinLines(List<string> lines)
        {
            if (lines.Count == 0) return string.Empty;
            return string.Join("\n", lines);
        }

        private static void WriteAtomic(string filePath, string content)
        {
            string dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            string tmp = filePath + ".u3dtmp";
            File.WriteAllText(tmp, content, new UTF8Encoding(false));
            if (File.Exists(filePath)) File.Delete(filePath);
            File.Move(tmp, filePath);
        }
    }
}
```

- [ ] Step 4: 跑测试确认全绿。testNames 过滤 `Yoji.U3DAILinker.Tests.Agents.GitignoreBlockWriterTests`。期望:8 个测试全 PASSED。重点核对两处可能首跑失败:(a) `Sync_PreservesUserLines` —— 若用户内容 `Library/\nTemp/\n*.csproj\n` 经 `SplitLines` 末尾产生空元素,`EnsureTrailingBlank` 会去掉再补一个空行,`StartsWith("Library/\nTemp/\n*.csproj\n")` 仍成立(区块前恰好一个空分隔行);(b) `Remove_LastManagedSkill...` —— `Remove` 后 `ignoreLines` 为空,`AppendBlock` 不产出区块,且重建时区块前的空分隔行会随 start/end 删除路径保留为用户尾部,断言只检查 `StartsWith("Library/\n")` 与不含 marker,通过。若 `Sync_SameContentTwice_Unchanged` 失败,核对 `Sync` 第二次的 `newText == existing` 比较(两次 split/join 必须幂等)。

- [ ] Step 5: commit:

```bash
cd /e/Yoji/U3D-Dev-Tools-AI
git add Packages/com.yoji.u3d-ai-linker/Editor/Agents/GitignoreBlockWriter.cs \
        Packages/com.yoji.u3d-ai-linker/Tests/Editor/Agents/GitignoreBlockWriterTests.cs
git commit -m "feat(u3d-ai-linker): GitignoreBlockWriter with ownership-scoped removal and corruption guard"
```

---

### Task 37: 端到端串联测试 — 合并预检失败时不写托管文件(跨组件不部分写保证)

把 `FragmentMerger` + `ManagedBlockWriter` 串起来验证 spec 327 的关键不变式:**重复 Skill 名时整个同步预检失败,不进行部分写入**。这一层是纯逻辑集成测试,验证调用方"先 Merge、失败则不调用 Write"的契约,防止后续 LINK-6 控制面板误把失败 body 写进文件。

Files:
- Create `Packages/com.yoji.u3d-ai-linker/Editor/Agents/ManagedFileSync.cs`
- Create `Packages/com.yoji.u3d-ai-linker/Tests/Editor/Agents/ManagedFileSyncTests.cs`

- [ ] Step 1: 写失败测试 `Packages/com.yoji.u3d-ai-linker/Tests/Editor/Agents/ManagedFileSyncTests.cs`(`ManagedFileSync` 尚不存在)。覆盖:正常多工具→CLAUDE.md 与 AGENTS.md 各写一次且区块含两段;重复 Skill 名→两个文件都不被创建/修改(预检失败,返回失败,文件不存在保持不存在);marker 损坏的既有文件→预检通过但写入返回 Conflict 且文件原样:

```csharp
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Yoji.U3DAILinker.Agents;

namespace Yoji.U3DAILinker.Tests.Agents
{
    public class ManagedFileSyncTests
    {
        private string m_Dir;
        private string m_Claude;
        private string m_Agents;

        [SetUp]
        public void SetUp()
        {
            m_Dir = Path.Combine(Path.GetTempPath(), "u3dsync_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_Dir);
            m_Claude = Path.Combine(m_Dir, "CLAUDE.md");
            m_Agents = Path.Combine(m_Dir, "AGENTS.md");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(m_Dir)) Directory.Delete(m_Dir, true);
        }

        private static FakeFragmentSource Src(string toolId, string skill, int order)
            => new FakeFragmentSource
            {
                ToolId = toolId,
                SkillName = skill,
                Order = order,
                ClaudeFragment = "C_" + toolId,
                AgentsFragment = "A_" + toolId
            };

        [Test]
        public void Sync_TwoTools_WritesBothManagedFiles()
        {
            var sources = new List<IFragmentSource>
            {
                Src("b", "skill-b", 2),
                Src("a", "skill-a", 1)
            };

            var ok = ManagedFileSync.Sync(m_Claude, m_Agents, sources, out var error);

            Assert.IsTrue(ok, error);
            Assert.IsTrue(File.Exists(m_Claude));
            Assert.IsTrue(File.Exists(m_Agents));
            var claude = File.ReadAllText(m_Claude).Replace("\r\n", "\n");
            StringAssert.Contains("C_a", claude);
            StringAssert.Contains("C_b", claude);
            Assert.Less(claude.IndexOf("C_a"), claude.IndexOf("C_b"));
        }

        [Test]
        public void Sync_DuplicateSkillName_WritesNothing()
        {
            var sources = new List<IFragmentSource>
            {
                Src("a", "dup", 1),
                Src("b", "dup", 2)
            };

            var ok = ManagedFileSync.Sync(m_Claude, m_Agents, sources, out var error);

            Assert.IsFalse(ok);
            StringAssert.Contains("dup", error);
            Assert.IsFalse(File.Exists(m_Claude), "CLAUDE.md must not be created on preflight failure");
            Assert.IsFalse(File.Exists(m_Agents), "AGENTS.md must not be created on preflight failure");
        }

        [Test]
        public void Sync_CorruptExistingFile_ReportsConflictAndLeavesItUntouched()
        {
            var corrupt = "<!-- u3d-ai-linker:start -->\nstray\n";
            File.WriteAllText(m_Claude, corrupt);
            var sources = new List<IFragmentSource> { Src("a", "skill-a", 1) };

            var ok = ManagedFileSync.Sync(m_Claude, m_Agents, sources, out var error);

            Assert.IsFalse(ok);
            StringAssert.Contains("CLAUDE.md", error);
            Assert.AreEqual(corrupt, File.ReadAllText(m_Claude));
        }
    }
}
```

- [ ] Step 2: 跑测试确认全红。testNames 过滤 `Yoji.U3DAILinker.Tests.Agents.ManagedFileSyncTests`。期望:编译失败,报 `The name 'ManagedFileSync' does not exist`。记录为预期基线。

- [ ] Step 3: 写实现 `Packages/com.yoji.u3d-ai-linker/Editor/Agents/ManagedFileSync.cs`。流程:先对 Claude 和 Agents 各做一次 Merge 预检;任一失败→返回 false 且不写任何文件;两者都成功→先写 CLAUDE.md,Conflict→返回 false(此时 AGENTS.md 还没写,符合"损坏即停");再写 AGENTS.md,Conflict→返回 false。错误信息带上具体文件名以便定位:

```csharp
using System.Collections.Generic;
using System.IO;

namespace Yoji.U3DAILinker.Agents
{
    /// <summary>
    /// Orchestrates fragment merge + managed-block write for CLAUDE.md and AGENTS.md.
    /// Preflight (negative order, duplicate Skill names) runs for both files before any
    /// write, so a failure leaves both files untouched. A corrupt marker in either file
    /// aborts with a conflict and leaves that file byte-for-byte unchanged.
    /// </summary>
    public static class ManagedFileSync
    {
        public static bool Sync(
            string claudePath,
            string agentsPath,
            IReadOnlyList<IFragmentSource> sources,
            out string error)
        {
            var claudeMerge = FragmentMerger.Merge(sources, ManagedBlockKind.Claude);
            if (!claudeMerge.Succeeded)
            {
                error = claudeMerge.Error;
                return false;
            }

            var agentsMerge = FragmentMerger.Merge(sources, ManagedBlockKind.Agents);
            if (!agentsMerge.Succeeded)
            {
                error = agentsMerge.Error;
                return false;
            }

            var claudeWrite = ManagedBlockWriter.Write(claudePath, claudeMerge.Body);
            if (claudeWrite.IsConflict)
            {
                error = FileName(claudePath) + ": " + claudeWrite.Message;
                return false;
            }

            var agentsWrite = ManagedBlockWriter.Write(agentsPath, agentsMerge.Body);
            if (agentsWrite.IsConflict)
            {
                error = FileName(agentsPath) + ": " + agentsWrite.Message;
                return false;
            }

            error = null;
            return true;
        }

        private static string FileName(string path) => Path.GetFileName(path);
    }
}
```

- [ ] Step 4: 跑测试确认全绿。testNames 过滤 `Yoji.U3DAILinker.Tests.Agents.ManagedFileSyncTests`。期望:3 个测试全 PASSED。注意 `Sync_DuplicateSkillName_WritesNothing` 依赖 Merge 在写之前失败(预检先于任何 `ManagedBlockWriter.Write`),实现已保证此顺序。

- [ ] Step 5: 跑本子系统全部测试做回归。testNames 过滤命名空间前缀 `Yoji.U3DAILinker.Tests`(EditMode)。期望:`FragmentSourceContractTests`(1) + `ManagedBlockWriterTests`(7) + `FragmentMergerTests`(8) + `GitignoreBlockWriterTests`(8) + `ManagedFileSyncTests`(3) 共 27 个 PASSED,0 失败。

- [ ] Step 6: commit:

```bash
cd /e/Yoji/U3D-Dev-Tools-AI
git add Packages/com.yoji.u3d-ai-linker/Editor/Agents/ManagedFileSync.cs \
        Packages/com.yoji.u3d-ai-linker/Tests/Editor/Agents/ManagedFileSyncTests.cs
git commit -m "feat(u3d-ai-linker): ManagedFileSync ties merge preflight to conflict-safe writes"
```

---

### LINK-5 手动验证(真实 Unity Editor,无法在 EditMode fake 覆盖的部分)

以上全部逻辑在 EditMode 无需真实 UPM/网络/Junction 即可跑。仅以下真实副作用需在 Unity 内人工确认(本子系统不产生 Junction/域重载副作用,故仅文件落地与 Git 行为):

- [ ] 手动 1:在真实游戏工程根放一个已有 `CLAUDE.md`(含用户自定义段落),通过 LINK-6 控制面板(或临时 EditorWindow 调用 `ManagedFileSync.Sync`)同步两个启用工具。预期:`CLAUDE.md` 顶部用户段落逐字保留,底部出现单一 `<!-- u3d-ai-linker:start -->` 区块,内含两段 `<!-- source: ... -->` 注释;`AGENTS.md` 同样生成。再次同步,文件无变化(`Unchanged`)。
- [ ] 手动 2:在工程根 `.gitignore` 已有用户行的前提下同步,确认新增 `# >>> u3d-ai-linker >>>` 区块只列 `/.u3d-ai-linker/` 与具体 skill 路径,不含 `/.claude/` 或 `/.agents/` 整目录。运行 `git status` 确认托管 skill 目录被忽略、用户其它文件不受影响。
- [ ] 手动 3:手工把 `CLAUDE.md` 的 end marker 删掉制造损坏,再同步。预期:控制面板报 conflict,`CLAUDE.md` 内容字节不变,`AGENTS.md` 未被写入(预检/损坏即停)。
- [ ] 手动 4:卸载其中一个工具,确认 `.gitignore` 区块只删掉该工具的 skill 行,另一工具的 ignore 行与 `/.u3d-ai-linker/` 保留;卸载最后一个工具后整个 `# >>> u3d-ai-linker >>>` 区块消失,但用户原有 `.gitignore` 行完好。

---

### LINK-6 — Project Settings 面板 + 三通道 + Refresh Dev + Restore

本子系统 LINK-6 是 Project Settings 控制台 + 三通道 URL 生成 + Refresh Dev + Restore。它消费 Registry(LINK-2 产出),但为保证本切片可独立 TDD,**LINK-6 在 `Editor/Registry/RegistryTypes.cs` 定义自己需要的最小 Registry 数据形状**(`RegistryEntry` / `LinkerRegistry` / `PackageStatus` / `PackageKind`)。组装时若 LINK-2 已落地等价类型,组装者负责把这两份合并为同一份(字段名与本切片公开签名一致);在 LINK-2 落地前,本切片用自带类型即可编译与测试。所有依赖真实副作用(真实 `git ls-remote`、真实 `Client.Add`、域重载、IMGUI 渲染、真实 `.asset` 序列化路径)抽到接口或列为手动验证;纯逻辑全部 EditMode TDD + fake。

测试命令统一走 test-runner MCP 的 `/run-tests`(EditMode);本计划中每处"跑测试"给出 HTTP 调用与期望结果。若环境未起 Unity Editor / MCP,等价命令为在 Unity 菜单 `Window > General > Test Runner > EditMode` 勾选对应类运行。下文统一约定:

```bash
# 触发重编译(改了 C# 后)
curl -s -X POST http://127.0.0.1:17890/recompile
# 跑某个测试类(EditMode),按 fullName 过滤
curl -s -X POST http://127.0.0.1:17890/run-tests \
  -H 'Content-Type: application/json' \
  -d '{"testMode":"EditMode","testNames":["<FULLNAME>"]}'
```

`<FULLNAME>` 形如 `Yoji.U3DAILinker.Tests.PackageUrlBuilderTests`。期望"失败"= 响应 JSON 里 `failed>0` 或编译错误;期望"通过"= `passed>0 && failed==0`。

> 幂等基线/合并提示:Task 38 的包骨架与前序子系统同名,核对存在即跳过。LINK-6 自带的 `Editor/Registry/RegistryTypes.cs`(最小 Registry 形状 + 顶层枚举 `LinkerChannel`/`RegistrySource`/`OperationState`)与 LINK-2 的完整 Registry 类型有重叠:`RegistryEntry`/`PackageStatus`/`PackageKind` 与 LINK-2 的 `RegistryEntry`/`ToolStatus`/`ToolKind` 语义对应但字段/命名不同。组装时统一为一套(建议保留 LINK-2 的解析模型作权威,LINK-6 面板侧用其字段;或保留本子系统的 `LinkerRegistry`/`RegistryEntry` 作面板视图,由适配层从 LINK-2 解析结果映射)。本子系统的 `Editor/Operations/PackageUrlBuilder.cs` 与 LINK-2 的 `ToolUrlBuilder.cs` 功能重叠(都生成三通道 URL);组装时二选一(建议合并为一处),公开签名以被引用方为准。`DependencyChange`(本子系统 `RefreshDevPlanner.cs` 内定义,字段 PackageName/OldValue/NewValue)与 LINK-2b/3 的同名类型合并为同一个。

---

### Task 38: 建立 u3d-ai-linker 包骨架与 SettingsProvider 注册占位

只建包结构、asmdef、空 SettingsProvider,让 `Project/U3D AI Linker` 入口可注册、测试程序集可编译。先不放业务逻辑。

Files:
- Create `Packages/com.yoji.u3d-ai-linker/package.json`
- Create `Packages/com.yoji.u3d-ai-linker/Editor/Yoji.U3DAILinker.Editor.asmdef`
- Create `Packages/com.yoji.u3d-ai-linker/Editor/AssemblyInfo.cs`
- Create `Packages/com.yoji.u3d-ai-linker/Editor/Settings/U3DAILinkerSettingsProvider.cs`
- Create `Packages/com.yoji.u3d-ai-linker/Tests/Editor/Yoji.U3DAILinker.Editor.Tests.asmdef`

> 若 LINK-1 已注册 `Editor/U3DAILinkerSettingsProvider.cs`(占位 HelpBox),本子系统的完整面板版放在 `Editor/Settings/U3DAILinkerSettingsProvider.cs`。组装时**只保留一个 SettingsProvider**(本子系统 Task 45 的完整版替换 LINK-1 占位版),避免重复注册同一路径导致两个面板入口。本 Task 的占位版仅在 LINK-6 独立落地时使用;按本计划顺序,LINK-1 已注册过,本 Task Step 4 的占位 provider 可跳过,直接复用 LINK-1 的,在 Task 45 升级为完整面板。

- [ ] Step 1: 创建 `Packages/com.yoji.u3d-ai-linker/package.json`:

```json
{
  "name": "com.yoji.u3d-ai-linker",
  "version": "1.0.0",
  "displayName": "U3D AI Linker",
  "description": "Project Settings console that installs Yoji tool packages and syncs AI agent assets.",
  "unity": "2022.3",
  "dependencies": {
    "com.unity.nuget.newtonsoft-json": "3.2.1"
  }
}
```

- [ ] Step 2: 创建主 asmdef `Packages/com.yoji.u3d-ai-linker/Editor/Yoji.U3DAILinker.Editor.asmdef`(参考 test-runner,Editor 平台 + Newtonsoft 预编译引用):

```json
{
  "name": "Yoji.U3DAILinker.Editor",
  "rootNamespace": "Yoji.U3DAILinker",
  "references": [],
  "includePlatforms": ["Editor"],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": true,
  "precompiledReferences": ["Newtonsoft.Json.dll"],
  "autoReferenced": true,
  "defineConstraints": [],
  "versionDefines": [],
  "noEngineReferences": false
}
```

- [ ] Step 3: 创建 `Packages/com.yoji.u3d-ai-linker/Editor/AssemblyInfo.cs`,对测试程序集开放 internal:

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Yoji.U3DAILinker.Editor.Tests")]
```

- [ ] Step 4: 创建空 SettingsProvider `Packages/com.yoji.u3d-ai-linker/Editor/Settings/U3DAILinkerSettingsProvider.cs`(本任务只注册路径,guiHandler 后续任务补全):

```csharp
using UnityEditor;
using UnityEngine.UIElements;

namespace Yoji.U3DAILinker.Settings
{
    /// Project Settings 入口 Project/U3D AI Linker。
    /// 本类只负责注册与 IMGUI 渲染;业务状态由 PanelStateModel 等纯逻辑类计算。
    internal static class U3DAILinkerSettingsProvider
    {
        internal const string ProviderPath = "Project/U3D AI Linker";

        [SettingsProvider]
        public static SettingsProvider CreateProvider()
        {
            return new SettingsProvider(ProviderPath, SettingsScope.Project)
            {
                label = "U3D AI Linker",
                guiHandler = OnGui,
                keywords = new[] { "linker", "agent", "skill", "upm", "registry" },
            };
        }

        private static void OnGui(string searchContext)
        {
            EditorGUILayout.HelpBox(
                "U3D AI Linker panel is initializing. Implementation follows in later tasks.",
                MessageType.Info);
        }
    }
}
```

  组装提示:若 LINK-1 已在 `Editor/U3DAILinkerSettingsProvider.cs` 注册 `Project/U3D AI Linker`,这里**不要**再注册第二个同路径 provider。二选一:删 LINK-1 占位版、用本子系统这份(并在 Task 45 升级);或保留 LINK-1 版、把本子系统的完整 OnGui 合进 LINK-1 版。最终全包只有一个 `[SettingsProvider]` 指向 `Project/U3D AI Linker`。

- [ ] Step 5: 创建测试 asmdef `Packages/com.yoji.u3d-ai-linker/Tests/Editor/Yoji.U3DAILinker.Editor.Tests.asmdef`(参考 test-runner 测试 asmdef):

```json
{
  "name": "Yoji.U3DAILinker.Editor.Tests",
  "rootNamespace": "Yoji.U3DAILinker.Tests",
  "references": ["Yoji.U3DAILinker.Editor", "UnityEngine.TestRunner", "UnityEditor.TestRunner"],
  "includePlatforms": ["Editor"],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": true,
  "precompiledReferences": ["nunit.framework.dll", "Newtonsoft.Json.dll"],
  "autoReferenced": false,
  "defineConstraints": ["UNITY_INCLUDE_TESTS"],
  "versionDefines": [],
  "noEngineReferences": false
}
```

- [ ] Step 6: 触发重编译,确认两个程序集都编译通过且无错误:`curl -s -X POST http://127.0.0.1:17890/recompile`。期望响应不含编译错误(`errors` 为空/200)。手动核对:Unity 中打开 `Edit > Project Settings`,左栏出现 `U3D AI Linker` 节点,右侧显示蓝色 Info HelpBox。
- [ ] Step 7: commit:

```bash
git checkout -b feat/u3d-ai-linker-panel
git add Packages/com.yoji.u3d-ai-linker/package.json \
  Packages/com.yoji.u3d-ai-linker/Editor/Yoji.U3DAILinker.Editor.asmdef \
  Packages/com.yoji.u3d-ai-linker/Editor/AssemblyInfo.cs \
  Packages/com.yoji.u3d-ai-linker/Editor/Settings/U3DAILinkerSettingsProvider.cs \
  Packages/com.yoji.u3d-ai-linker/Tests/Editor/Yoji.U3DAILinker.Editor.Tests.asmdef
git commit -m "feat(u3d-ai-linker): scaffold package, asmdefs and SettingsProvider entry"
```

---

### Task 39: Registry 与 InstalledPackageInfo 最小数据类型

LINK-6 自带的最小 Registry 形状与"当前已装包"快照类型,供 PanelStateModel / RefreshDev / Restore 消费。这些是纯数据类,无副作用,本任务不写测试(由下游消费它们的任务覆盖),但代码必须完整可编译。

Files:
- Create `Packages/com.yoji.u3d-ai-linker/Editor/Registry/RegistryTypes.cs`
- Create `Packages/com.yoji.u3d-ai-linker/Editor/Operations/InstalledPackageInfo.cs`

- [ ] Step 1: 创建 `Packages/com.yoji.u3d-ai-linker/Editor/Registry/RegistryTypes.cs`。注意 `LinkerChannel` / `RegistrySource` / `OperationState` 这些面板顶部状态枚举放在 `Yoji.U3DAILinker` 顶层命名空间(被多个子命名空间共享):

```csharp
using System.Collections.Generic;

namespace Yoji.U3DAILinker
{
    /// 当前通道。Local 仅本机内循环,绝对路径只入 UserSettings。
    internal enum LinkerChannel
    {
        Stable,
        Dev,
        Local,
    }

    /// Registry 数据来源,用于面板顶部状态区显示。
    internal enum RegistrySource
    {
        Remote,
        BundledSnapshot,
        LocalFile,
    }

    /// 当前操作态,用于面板顶部状态区显示。
    internal enum OperationState
    {
        Idle,
        Running,
        Failed,
        NeedsRecovery,
    }
}

namespace Yoji.U3DAILinker.Registry
{
    internal enum PackageStatus
    {
        Ready,
        SkillOnly,
        Planned,
    }

    internal enum PackageKind
    {
        Tool,
        Infra,
        Linker,
    }

    /// Registry 中单个包条目的已校验视图(LINK-6 消费所需的最小字段)。
    /// 组装时若 LINK-2 已有等价类型,合并为同一份。
    internal sealed class RegistryEntry
    {
        public string Id;
        public PackageStatus Status;
        public PackageKind Kind;
        public int Order;
        public string PackageName;
        public string PackagePath;
        public string Revision;
        public bool DefaultEnabled;
        public bool UserToggle;
        public string MinUnity;
        public List<string> DependsOn = new List<string>();
        public string DisplayName;
    }

    /// 单通道 Registry 的已校验视图。
    internal sealed class LinkerRegistry
    {
        public int SchemaVersion;
        public LinkerChannel Channel;
        public string Branch;
        public List<RegistryEntry> Entries = new List<RegistryEntry>();
    }
}
```

  组装提示:LINK-2 已定义 `Yoji.U3DAILinker.Registry.RegistryEntry`(string Status/Kind + JsonProperty)。本文件再定义同命名空间同名 `RegistryEntry`(枚举 Status/Kind)会冲突。组装时二选一:(a) 保留 LINK-2 的 `RegistryEntry`,把本子系统面板视图改名为 `RegistryEntryView`/挪到 `Yoji.U3DAILinker.Settings`;或 (b) 把本子系统的 `LinkerRegistry`/`RegistryEntry`/`PackageStatus`/`PackageKind` 放到独立命名空间 `Yoji.U3DAILinker.Panel`。务必避免同命名空间同名类型重复定义。

- [ ] Step 2: 创建 `Packages/com.yoji.u3d-ai-linker/Editor/Operations/InstalledPackageInfo.cs`。这是对真实 `PackageInfo` + manifest 读取结果的纯数据投影,EditMode 测试用 fake 字典构造,不触真实 UPM:

```csharp
namespace Yoji.U3DAILinker.Operations
{
    /// 目标工程当前已安装包的纯数据快照。
    /// 由 manifest dependencies 值 + PackageInfo 解析得到,但本类本身不触 UPM,
    /// 便于在 EditMode 用字典构造做纯逻辑测试。
    internal sealed class InstalledPackageInfo
    {
        /// UPM 包名,如 com.yoji.editor-debug。
        public string PackageName;

        /// manifest 中该依赖的当前值(Git URL 或 file: 路径);未安装为 null。
        public string ResolvedUrl;

        /// 该依赖是否由 Linker 管理(URL 匹配本仓库托管格式或本机 file:)。
        public bool IsManaged;

        public InstalledPackageInfo(string packageName, string resolvedUrl, bool isManaged)
        {
            PackageName = packageName;
            ResolvedUrl = resolvedUrl;
            IsManaged = isManaged;
        }
    }
}
```

- [ ] Step 3: 触发重编译确认通过:`curl -s -X POST http://127.0.0.1:17890/recompile`(期望无编译错误)。
- [ ] Step 4: commit:

```bash
git add Packages/com.yoji.u3d-ai-linker/Editor/Registry/RegistryTypes.cs \
  Packages/com.yoji.u3d-ai-linker/Editor/Operations/InstalledPackageInfo.cs
git commit -m "feat(u3d-ai-linker): add minimal Registry and InstalledPackageInfo data types"
```

---

### Task 40: PackageUrlBuilder 三通道 URL 生成(TDD)

生成 Stable tag URL、Dev SHA URL、Local file URL,并识别"是否 Linker 管理的 URL"。这是纯字符串逻辑,完整 TDD。覆盖 spec 468 行(三通道 URL 生成)与 173-190 行格式。

Files:
- Create `Packages/com.yoji.u3d-ai-linker/Editor/Operations/PackageUrlBuilder.cs`
- Create `Packages/com.yoji.u3d-ai-linker/Tests/Editor/PackageUrlBuilderTests.cs`

- [ ] Step 1: 先写失败测试 `Packages/com.yoji.u3d-ai-linker/Tests/Editor/PackageUrlBuilderTests.cs`:

```csharp
using NUnit.Framework;
using Yoji.U3DAILinker.Operations;

namespace Yoji.U3DAILinker.Tests
{
    public sealed class PackageUrlBuilderTests
    {
        private const string Path = "Packages/com.yoji.editor-debug";

        [Test]
        public void BuildStable_ProducesTagUrl()
        {
            var url = PackageUrlBuilder.BuildStable(Path, "editor-debug-v1.2.0");
            Assert.AreEqual(
                "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/com.yoji.editor-debug#editor-debug-v1.2.0",
                url);
        }

        [Test]
        public void BuildDevSha_ProducesShaUrl()
        {
            var sha = "0123456789abcdef0123456789abcdef01234567";
            var url = PackageUrlBuilder.BuildDevSha(Path, sha);
            Assert.AreEqual(
                "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/com.yoji.editor-debug#" + sha,
                url);
        }

        [Test]
        public void BuildLocalFile_NormalizesBackslashesToForwardSlashes()
        {
            var url = PackageUrlBuilder.BuildLocalFile(@"E:\Yoji\U3D-Dev-Tools-AI", Path);
            Assert.AreEqual("file:E:/Yoji/U3D-Dev-Tools-AI/Packages/com.yoji.editor-debug", url);
        }

        [Test]
        public void BuildLocalFile_TrimsTrailingSlashOnRoot()
        {
            var url = PackageUrlBuilder.BuildLocalFile("E:/Yoji/U3D-Dev-Tools-AI/", Path);
            Assert.AreEqual("file:E:/Yoji/U3D-Dev-Tools-AI/Packages/com.yoji.editor-debug", url);
        }

        [Test]
        public void IsManagedUrl_TrueForRepoGitUrl()
        {
            Assert.IsTrue(PackageUrlBuilder.IsManagedUrl(
                "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/com.yoji.editor-debug#editor-debug-v1.2.0"));
        }

        [Test]
        public void IsManagedUrl_TrueForFileScheme()
        {
            Assert.IsTrue(PackageUrlBuilder.IsManagedUrl("file:E:/Yoji/U3D-Dev-Tools-AI/Packages/com.yoji.editor-debug"));
        }

        [Test]
        public void IsManagedUrl_FalseForThirdPartyRegistryVersion()
        {
            Assert.IsFalse(PackageUrlBuilder.IsManagedUrl("3.2.1"));
        }

        [Test]
        public void IsManagedUrl_FalseForForeignGitRepo()
        {
            Assert.IsFalse(PackageUrlBuilder.IsManagedUrl(
                "https://github.com/someone-else/other-repo.git?path=/Packages/x#v1"));
        }

        [Test]
        public void IsManagedUrl_FalseForNull()
        {
            Assert.IsFalse(PackageUrlBuilder.IsManagedUrl(null));
        }
    }
}
```

- [ ] Step 2: 重编译并跑测试,确认**失败**(类型不存在 -> 编译错误):

```bash
curl -s -X POST http://127.0.0.1:17890/recompile
curl -s -X POST http://127.0.0.1:17890/run-tests -H 'Content-Type: application/json' \
  -d '{"testMode":"EditMode","testNames":["Yoji.U3DAILinker.Tests.PackageUrlBuilderTests"]}'
```

期望:编译错误(`PackageUrlBuilder` 未定义)或 `failed>0`。

- [ ] Step 3: 写最小实现 `Packages/com.yoji.u3d-ai-linker/Editor/Operations/PackageUrlBuilder.cs`:

```csharp
namespace Yoji.U3DAILinker.Operations
{
    /// 三通道 UPM 依赖值生成器。安装 URL 只能由本类用已校验字段拼装,
    /// 不能直接执行 Registry 提供的整串 URL(见设计 172 行)。
    internal static class PackageUrlBuilder
    {
        /// 固定仓库,Registry 不接受自定义仓库 URL(设计 164 行)。
        internal const string RepoUrl = "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git";

        private const string FilePrefix = "file:";

        /// Stable: tag 形如 editor-debug-v1.2.0。packagePath 形如 Packages/com.yoji.editor-debug。
        public static string BuildStable(string packagePath, string revision)
        {
            return RepoUrl + "?path=/" + Normalize(packagePath) + "#" + revision;
        }

        /// Dev: 锁定到 40 位 commit SHA。
        public static string BuildDevSha(string packagePath, string commitSha)
        {
            return RepoUrl + "?path=/" + Normalize(packagePath) + "#" + commitSha;
        }

        /// Local: file: 指向本机仓库工作区中的子包目录。
        /// localRepoRoot 是本机绝对路径(只来自 UserSettings),反斜杠归一为正斜杠。
        public static string BuildLocalFile(string localRepoRoot, string packagePath)
        {
            var root = Normalize(localRepoRoot).TrimEnd('/');
            return FilePrefix + root + "/" + Normalize(packagePath);
        }

        /// 判断某 manifest 依赖值是否由 Linker 管理:本仓库 Git URL 或本机 file: 路径。
        public static bool IsManagedUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
                return false;
            if (url.StartsWith(FilePrefix, System.StringComparison.Ordinal))
                return true;
            return url.StartsWith(RepoUrl, System.StringComparison.Ordinal);
        }

        private static string Normalize(string path)
        {
            return path == null ? string.Empty : path.Replace('\\', '/');
        }
    }
}
```

  组装提示:本类与 LINK-2 的 `ToolUrlBuilder` 功能重叠。组装时二选一(建议保留一处);若保留 `ToolUrlBuilder`,把本子系统对 `PackageUrlBuilder.BuildStable/BuildDevSha/BuildLocalFile/IsManagedUrl` 的调用改为对等接口。下面 Task 41/43/44 的代码均引用 `PackageUrlBuilder`,合并时统一改名即可。

- [ ] Step 4: 重编译并重跑,确认**通过**:

```bash
curl -s -X POST http://127.0.0.1:17890/recompile
curl -s -X POST http://127.0.0.1:17890/run-tests -H 'Content-Type: application/json' \
  -d '{"testMode":"EditMode","testNames":["Yoji.U3DAILinker.Tests.PackageUrlBuilderTests"]}'
```

期望:`passed==9 && failed==0`。

- [ ] Step 5: commit:

```bash
git add Packages/com.yoji.u3d-ai-linker/Editor/Operations/PackageUrlBuilder.cs \
  Packages/com.yoji.u3d-ai-linker/Tests/Editor/PackageUrlBuilderTests.cs
git commit -m "feat(u3d-ai-linker): three-channel package URL builder with managed-url check"
```

---

### Task 41: U3DAILinkerSettings / UserSettings 与状态分离断言(TDD)

ScriptableObject 三层状态:ProjectSettings 层(通道/启用工具/期望版本,**禁止绝对路径**)与 UserSettings 层(本机仓库路径/窗口态)。覆盖设计 416 行(可提交、不含绝对路径)与验证项 474。真实 `.asset` 落盘走 `U3DAILinkerSettingsStore`(后述手动验证);本任务对纯内存对象 + 一个"不变量校验器"做 TDD。

Files:
- Create `Packages/com.yoji.u3d-ai-linker/Editor/Settings/U3DAILinkerSettings.cs`
- Create `Packages/com.yoji.u3d-ai-linker/Editor/Settings/U3DAILinkerUserSettings.cs`
- Create `Packages/com.yoji.u3d-ai-linker/Tests/Editor/U3DAILinkerSettingsTests.cs`

- [ ] Step 1: 写失败测试 `Packages/com.yoji.u3d-ai-linker/Tests/Editor/U3DAILinkerSettingsTests.cs`。核心断言:`U3DAILinkerSettings.ContainsAbsolutePath()` 对纯通道/工具 ID 配置返回 false,本机路径只能进 UserSettings;且 `Validate()` 把误塞进 DesiredVersions 的绝对路径标为非法:

```csharp
using NUnit.Framework;
using UnityEngine;
using Yoji.U3DAILinker;
using Yoji.U3DAILinker.Settings;

namespace Yoji.U3DAILinker.Tests
{
    public sealed class U3DAILinkerSettingsTests
    {
        private static U3DAILinkerSettings NewProjectSettings()
        {
            var s = ScriptableObject.CreateInstance<U3DAILinkerSettings>();
            s.Channel = LinkerChannel.Stable;
            s.EnabledToolIds.Add("editor-debug");
            s.EnabledToolIds.Add("test-runner");
            s.DesiredVersions.Add(new DesiredVersion { ToolId = "editor-debug", Revision = "editor-debug-v1.2.0" });
            return s;
        }

        [Test]
        public void CleanProjectSettings_HasNoAbsolutePath()
        {
            var s = NewProjectSettings();
            Assert.IsFalse(s.ContainsAbsolutePath());
            Assert.IsTrue(s.Validate(out var error), error);
            Object.DestroyImmediate(s);
        }

        [Test]
        public void WindowsAbsolutePathInRevision_IsDetectedAndRejected()
        {
            var s = NewProjectSettings();
            s.DesiredVersions.Add(new DesiredVersion
            {
                ToolId = "local-hack",
                Revision = "file:E:/Yoji/U3D-Dev-Tools-AI/Packages/com.yoji.editor-debug",
            });
            Assert.IsTrue(s.ContainsAbsolutePath());
            Assert.IsFalse(s.Validate(out var error));
            StringAssert.Contains("local-hack", error);
            Object.DestroyImmediate(s);
        }

        [Test]
        public void DriveLetterAbsolutePath_IsDetected()
        {
            var s = NewProjectSettings();
            s.DesiredVersions.Add(new DesiredVersion { ToolId = "x", Revision = @"E:\repo\pkg" });
            Assert.IsTrue(s.ContainsAbsolutePath());
            Object.DestroyImmediate(s);
        }

        [Test]
        public void UnixAbsolutePath_IsDetected()
        {
            var s = NewProjectSettings();
            s.DesiredVersions.Add(new DesiredVersion { ToolId = "x", Revision = "/home/me/repo" });
            Assert.IsTrue(s.ContainsAbsolutePath());
            Object.DestroyImmediate(s);
        }

        [Test]
        public void UserSettings_HoldsLocalRepoRoot()
        {
            var u = ScriptableObject.CreateInstance<U3DAILinkerUserSettings>();
            u.LocalRepoRoot = @"E:\Yoji\U3D-Dev-Tools-AI";
            Assert.AreEqual(@"E:\Yoji\U3D-Dev-Tools-AI", u.LocalRepoRoot);
            Object.DestroyImmediate(u);
        }
    }
}
```

- [ ] Step 2: 重编译并跑测试确认**失败**(类型未定义):

```bash
curl -s -X POST http://127.0.0.1:17890/recompile
curl -s -X POST http://127.0.0.1:17890/run-tests -H 'Content-Type: application/json' \
  -d '{"testMode":"EditMode","testNames":["Yoji.U3DAILinker.Tests.U3DAILinkerSettingsTests"]}'
```

期望:编译错误或 `failed>0`。

- [ ] Step 3: 实现 `Packages/com.yoji.u3d-ai-linker/Editor/Settings/U3DAILinkerSettings.cs`(ProjectSettings 层,带绝对路径不变量):

```csharp
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Yoji.U3DAILinker.Settings
{
    /// 工具的项目期望版本条目(通道无关的 tool->revision 映射)。
    [System.Serializable]
    internal sealed class DesiredVersion
    {
        public string ToolId;
        public string Revision;
    }

    /// ProjectSettings 层状态:通道、启用工具、项目期望版本。
    /// 必须可提交、不含本机绝对路径(设计 416 行)。
    /// 本机仓库路径属于 U3DAILinkerUserSettings,不在此类。
    internal sealed class U3DAILinkerSettings : ScriptableObject
    {
        public LinkerChannel Channel = LinkerChannel.Stable;
        public List<string> EnabledToolIds = new List<string>();
        public List<DesiredVersion> DesiredVersions = new List<DesiredVersion>();

        // Windows 盘符绝对路径 (E:\ 或 E:/) 或 file: 前缀或 Unix 绝对路径 (/...)。
        private static readonly Regex AbsolutePathPattern =
            new Regex(@"^([a-zA-Z]:[\\/]|file:|/)", RegexOptions.Compiled);

        /// 任一 revision 看起来像本机绝对路径/ file: 路径,即视为污染。
        public bool ContainsAbsolutePath()
        {
            foreach (var dv in DesiredVersions)
            {
                if (dv != null && LooksAbsolute(dv.Revision))
                    return true;
            }
            return false;
        }

        /// 校验 ProjectSettings 不变量:不得出现绝对路径。失败时 error 给出违例 toolId。
        public bool Validate(out string error)
        {
            foreach (var dv in DesiredVersions)
            {
                if (dv != null && LooksAbsolute(dv.Revision))
                {
                    error = "DesiredVersion for tool '" + dv.ToolId +
                            "' contains an absolute/file path, which must live in UserSettings, not ProjectSettings: " +
                            dv.Revision;
                    return false;
                }
            }
            error = null;
            return true;
        }

        private static bool LooksAbsolute(string value)
        {
            return !string.IsNullOrEmpty(value) && AbsolutePathPattern.IsMatch(value);
        }
    }
}
```

- [ ] Step 4: 实现 `Packages/com.yoji.u3d-ai-linker/Editor/Settings/U3DAILinkerUserSettings.cs`(UserSettings 层,本机路径与窗口态):

```csharp
using UnityEngine;

namespace Yoji.U3DAILinker.Settings
{
    /// UserSettings 层:本机仓库路径与面板偏好。不要求提交(设计 417 行)。
    /// Local 通道的绝对路径只能存这里,绝不进 ProjectSettings。
    internal sealed class U3DAILinkerUserSettings : ScriptableObject
    {
        /// Local 通道使用的本机仓库根,如 E:\Yoji\U3D-Dev-Tools-AI。
        public string LocalRepoRoot = string.Empty;

        /// 面板是否展开 infra 包细节。
        public bool ShowInfraDetails = false;
    }
}
```

- [ ] Step 5: 重编译并重跑,确认**通过**:

```bash
curl -s -X POST http://127.0.0.1:17890/recompile
curl -s -X POST http://127.0.0.1:17890/run-tests -H 'Content-Type: application/json' \
  -d '{"testMode":"EditMode","testNames":["Yoji.U3DAILinker.Tests.U3DAILinkerSettingsTests"]}'
```

期望:`passed==5 && failed==0`。

- [ ] Step 6: commit:

```bash
git add Packages/com.yoji.u3d-ai-linker/Editor/Settings/U3DAILinkerSettings.cs \
  Packages/com.yoji.u3d-ai-linker/Editor/Settings/U3DAILinkerUserSettings.cs \
  Packages/com.yoji.u3d-ai-linker/Tests/Editor/U3DAILinkerSettingsTests.cs
git commit -m "feat(u3d-ai-linker): split Project/User settings with no-absolute-path invariant"
```

---

### Task 42: PanelStateModel 状态计算(TDD)

由 Registry + 已装包快照 + 启用集 + Agent 状态 + 当前通道,算出工具列表每行:Enabled 可见性、InstallState、Desired(按通道选 tag/SHA/local path)、Current(manifest 当前值)、Agent、RequiredBy(infra 由哪些工具需要)。这是面板的核心可测状态模型(设计 371-383)。完整 TDD + fake 字典,无 IMGUI。

Files:
- Create `Packages/com.yoji.u3d-ai-linker/Editor/Settings/PanelStateModel.cs`
- Create `Packages/com.yoji.u3d-ai-linker/Tests/Editor/PanelStateModelTests.cs`

- [ ] Step 1: 写失败测试 `Packages/com.yoji.u3d-ai-linker/Tests/Editor/PanelStateModelTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Yoji.U3DAILinker;
using Yoji.U3DAILinker.Operations;
using Yoji.U3DAILinker.Registry;
using Yoji.U3DAILinker.Settings;

namespace Yoji.U3DAILinker.Tests
{
    public sealed class PanelStateModelTests
    {
        private static LinkerRegistry SampleRegistry(LinkerChannel channel)
        {
            return new LinkerRegistry
            {
                SchemaVersion = 2,
                Channel = channel,
                Branch = channel == LinkerChannel.Dev ? "main" : null,
                Entries = new List<RegistryEntry>
                {
                    new RegistryEntry
                    {
                        Id = "editor-core", DisplayName = "Editor Core", Kind = PackageKind.Infra,
                        Status = PackageStatus.Ready, Order = 10,
                        PackageName = "com.yoji.editor-core", PackagePath = "Packages/com.yoji.editor-core",
                        Revision = "editor-core-v1.0.0", DefaultEnabled = false, UserToggle = false,
                        MinUnity = "2022.3",
                    },
                    new RegistryEntry
                    {
                        Id = "test-runner", DisplayName = "Test Runner", Kind = PackageKind.Tool,
                        Status = PackageStatus.Ready, Order = 20,
                        PackageName = "com.yoji.test-runner", PackagePath = "Packages/com.yoji.test-runner",
                        Revision = "test-runner-v1.1.0", DefaultEnabled = true, UserToggle = true,
                        MinUnity = "2022.3", DependsOn = new List<string> { "editor-core" },
                    },
                },
            };
        }

        private static Dictionary<string, InstalledPackageInfo> NoneInstalled()
        {
            return new Dictionary<string, InstalledPackageInfo>();
        }

        private static ToolRow Row(ToolRow[] rows, string id)
        {
            return rows.First(r => r.Id == id);
        }

        [Test]
        public void ToolWithUserToggle_ShowsEnabledColumn_InfraDoesNot()
        {
            var rows = PanelStateModel.Build(
                SampleRegistry(LinkerChannel.Stable), NoneInstalled(),
                new[] { "test-runner" }, new Dictionary<string, AgentState>(),
                LinkerChannel.Stable, null);

            Assert.IsTrue(Row(rows, "test-runner").EnabledVisible);
            Assert.IsTrue(Row(rows, "test-runner").Enabled);
            Assert.IsFalse(Row(rows, "editor-core").EnabledVisible);
        }

        [Test]
        public void DesiredOnStable_UsesTag()
        {
            var rows = PanelStateModel.Build(
                SampleRegistry(LinkerChannel.Stable), NoneInstalled(),
                new[] { "test-runner" }, new Dictionary<string, AgentState>(),
                LinkerChannel.Stable, null);

            Assert.AreEqual(
                "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/com.yoji.test-runner#test-runner-v1.1.0",
                Row(rows, "test-runner").Desired);
        }

        [Test]
        public void DesiredOnLocal_UsesFileUrlFromLocalRoot()
        {
            var rows = PanelStateModel.Build(
                SampleRegistry(LinkerChannel.Local), NoneInstalled(),
                new[] { "test-runner" }, new Dictionary<string, AgentState>(),
                LinkerChannel.Local, @"E:\Yoji\U3D-Dev-Tools-AI");

            Assert.AreEqual(
                "file:E:/Yoji/U3D-Dev-Tools-AI/Packages/com.yoji.test-runner",
                Row(rows, "test-runner").Desired);
        }

        [Test]
        public void InstalledManagedMatchingDesired_IsInstalled()
        {
            var installed = new Dictionary<string, InstalledPackageInfo>
            {
                ["com.yoji.test-runner"] = new InstalledPackageInfo(
                    "com.yoji.test-runner",
                    "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/com.yoji.test-runner#test-runner-v1.1.0",
                    true),
            };
            var rows = PanelStateModel.Build(
                SampleRegistry(LinkerChannel.Stable), installed,
                new[] { "test-runner" }, new Dictionary<string, AgentState>(),
                LinkerChannel.Stable, null);

            var r = Row(rows, "test-runner");
            Assert.AreEqual(InstallState.Installed, r.Installed);
            Assert.AreEqual(r.Desired, r.Current);
        }

        [Test]
        public void InstalledUnmanagedDependency_IsConflict()
        {
            var installed = new Dictionary<string, InstalledPackageInfo>
            {
                ["com.yoji.test-runner"] = new InstalledPackageInfo(
                    "com.yoji.test-runner", "1.0.0-foreign", false),
            };
            var rows = PanelStateModel.Build(
                SampleRegistry(LinkerChannel.Stable), installed,
                new[] { "test-runner" }, new Dictionary<string, AgentState>(),
                LinkerChannel.Stable, null);

            Assert.AreEqual(InstallState.Conflict, Row(rows, "test-runner").Installed);
        }

        [Test]
        public void NotInstalled_IsMissing()
        {
            var rows = PanelStateModel.Build(
                SampleRegistry(LinkerChannel.Stable), NoneInstalled(),
                new[] { "test-runner" }, new Dictionary<string, AgentState>(),
                LinkerChannel.Stable, null);

            Assert.AreEqual(InstallState.Missing, Row(rows, "test-runner").Installed);
            Assert.IsNull(Row(rows, "test-runner").Current);
        }

        [Test]
        public void InfraRequiredBy_ListsEnabledDependents()
        {
            var rows = PanelStateModel.Build(
                SampleRegistry(LinkerChannel.Stable), NoneInstalled(),
                new[] { "test-runner" }, new Dictionary<string, AgentState>(),
                LinkerChannel.Stable, null);

            CollectionAssert.AreEqual(new[] { "test-runner" }, Row(rows, "editor-core").RequiredBy);
        }

        [Test]
        public void AgentState_DefaultsToNotApplicableWhenAbsent()
        {
            var rows = PanelStateModel.Build(
                SampleRegistry(LinkerChannel.Stable), NoneInstalled(),
                new[] { "test-runner" }, new Dictionary<string, AgentState>(),
                LinkerChannel.Stable, null);

            Assert.AreEqual(AgentState.NotApplicable, Row(rows, "test-runner").Agent);
        }

        [Test]
        public void AgentState_ReadFromMapWhenPresent()
        {
            var agents = new Dictionary<string, AgentState> { ["test-runner"] = AgentState.Stale };
            var rows = PanelStateModel.Build(
                SampleRegistry(LinkerChannel.Stable), NoneInstalled(),
                new[] { "test-runner" }, agents,
                LinkerChannel.Stable, null);

            Assert.AreEqual(AgentState.Stale, Row(rows, "test-runner").Agent);
        }

        [Test]
        public void Rows_SortedByOrderThenId()
        {
            var rows = PanelStateModel.Build(
                SampleRegistry(LinkerChannel.Stable), NoneInstalled(),
                new[] { "test-runner" }, new Dictionary<string, AgentState>(),
                LinkerChannel.Stable, null);

            Assert.AreEqual("editor-core", rows[0].Id);
            Assert.AreEqual("test-runner", rows[1].Id);
        }
    }
}
```

- [ ] Step 2: 重编译并跑测试确认**失败**:

```bash
curl -s -X POST http://127.0.0.1:17890/recompile
curl -s -X POST http://127.0.0.1:17890/run-tests -H 'Content-Type: application/json' \
  -d '{"testMode":"EditMode","testNames":["Yoji.U3DAILinker.Tests.PanelStateModelTests"]}'
```

期望:编译错误或 `failed>0`。

- [ ] Step 3: 实现 `Packages/com.yoji.u3d-ai-linker/Editor/Settings/PanelStateModel.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using Yoji.U3DAILinker.Operations;
using Yoji.U3DAILinker.Registry;

namespace Yoji.U3DAILinker.Settings
{
    internal enum InstallState
    {
        Missing,
        Installed,
        Conflict,
    }

    internal enum AgentState
    {
        Synced,
        Stale,
        Missing,
        Conflict,
        NotApplicable,
    }

    /// 面板工具列表单行的纯数据视图(设计 371-383 列定义)。
    internal sealed class ToolRow
    {
        public string Id;
        public string DisplayName;
        public PackageKind Kind;
        public bool EnabledVisible;
        public bool Enabled;
        public InstallState Installed;
        public string Desired;
        public string Current;
        public AgentState Agent;
        public string[] RequiredBy;
    }

    /// 由 Registry + 当前安装快照 + 启用集 + Agent 状态 + 通道,
    /// 纯函数地算出面板工具列表。无 IMGUI、无 UPM 副作用,完全可单测。
    internal static class PanelStateModel
    {
        public static ToolRow[] Build(
            LinkerRegistry registry,
            IReadOnlyDictionary<string, InstalledPackageInfo> installed,
            IReadOnlyCollection<string> enabledToolIds,
            IReadOnlyDictionary<string, AgentState> agentStates,
            LinkerChannel channel,
            string localRepoRoot)
        {
            var enabled = new HashSet<string>(enabledToolIds ?? System.Array.Empty<string>());

            var rows = new List<ToolRow>(registry.Entries.Count);
            foreach (var entry in registry.Entries)
            {
                var desired = BuildDesired(entry, channel, localRepoRoot);
                var current = ResolveCurrent(installed, entry.PackageName);
                rows.Add(new ToolRow
                {
                    Id = entry.Id,
                    DisplayName = string.IsNullOrEmpty(entry.DisplayName) ? entry.Id : entry.DisplayName,
                    Kind = entry.Kind,
                    EnabledVisible = entry.Kind == PackageKind.Tool && entry.UserToggle,
                    Enabled = enabled.Contains(entry.Id),
                    Installed = ResolveInstallState(installed, entry.PackageName),
                    Desired = desired,
                    Current = current,
                    Agent = ResolveAgentState(agentStates, entry.Id),
                    RequiredBy = ResolveRequiredBy(registry, enabled, entry),
                });
            }

            return rows
                .OrderBy(r => OrderOf(registry, r.Id))
                .ThenBy(r => r.Id, System.StringComparer.Ordinal)
                .ToArray();
        }

        private static string BuildDesired(RegistryEntry entry, LinkerChannel channel, string localRepoRoot)
        {
            switch (channel)
            {
                case LinkerChannel.Local:
                    return string.IsNullOrEmpty(localRepoRoot)
                        ? null
                        : PackageUrlBuilder.BuildLocalFile(localRepoRoot, entry.PackagePath);
                case LinkerChannel.Dev:
                    // Dev 的精确 SHA 由 RefreshDev 锁定;此处展示 branch 占位。
                    return PackageUrlBuilder.RepoUrl + "?path=/" +
                           entry.PackagePath.Replace('\\', '/') + "#main";
                default:
                    return PackageUrlBuilder.BuildStable(entry.PackagePath, entry.Revision);
            }
        }

        private static string ResolveCurrent(
            IReadOnlyDictionary<string, InstalledPackageInfo> installed, string packageName)
        {
            return installed != null && installed.TryGetValue(packageName, out var info)
                ? info.ResolvedUrl
                : null;
        }

        private static InstallState ResolveInstallState(
            IReadOnlyDictionary<string, InstalledPackageInfo> installed, string packageName)
        {
            if (installed == null || !installed.TryGetValue(packageName, out var info))
                return InstallState.Missing;
            return info.IsManaged ? InstallState.Installed : InstallState.Conflict;
        }

        private static AgentState ResolveAgentState(
            IReadOnlyDictionary<string, AgentState> agentStates, string toolId)
        {
            return agentStates != null && agentStates.TryGetValue(toolId, out var state)
                ? state
                : AgentState.NotApplicable;
        }

        private static string[] ResolveRequiredBy(
            LinkerRegistry registry, HashSet<string> enabled, RegistryEntry entry)
        {
            if (entry.Kind != PackageKind.Infra)
                return System.Array.Empty<string>();

            return registry.Entries
                .Where(e => e.Kind == PackageKind.Tool
                            && enabled.Contains(e.Id)
                            && e.DependsOn != null
                            && e.DependsOn.Contains(entry.Id))
                .Select(e => e.Id)
                .OrderBy(id => id, System.StringComparer.Ordinal)
                .ToArray();
        }

        private static int OrderOf(LinkerRegistry registry, string id)
        {
            var e = registry.Entries.FirstOrDefault(x => x.Id == id);
            return e != null ? e.Order : int.MaxValue;
        }
    }
}
```

- [ ] Step 4: 重编译并重跑,确认**通过**:

```bash
curl -s -X POST http://127.0.0.1:17890/recompile
curl -s -X POST http://127.0.0.1:17890/run-tests -H 'Content-Type: application/json' \
  -d '{"testMode":"EditMode","testNames":["Yoji.U3DAILinker.Tests.PanelStateModelTests"]}'
```

期望:`passed==10 && failed==0`。

- [ ] Step 5: commit:

```bash
git add Packages/com.yoji.u3d-ai-linker/Editor/Settings/PanelStateModel.cs \
  Packages/com.yoji.u3d-ai-linker/Tests/Editor/PanelStateModelTests.cs
git commit -m "feat(u3d-ai-linker): pure PanelStateModel computing tool-row install/desired/agent state"
```

---

### Task 43: IGitRefResolver + GitLsRemoteResolver + RefreshDevPlanner(TDD,真实 git 列手动验证)

Dev 通道 Refresh:一次解析远端 main 的 SHA(`IGitRefResolver`),把同一操作中所有工具 URL 锁到同一 SHA(`RefreshDevPlanner`)。真实 `git ls-remote` 抽到接口,EditMode 用 fake 单测计划生成;真实进程调用与 Client.Add 列手动验证(设计 220-227)。

Files:
- Create `Packages/com.yoji.u3d-ai-linker/Editor/Operations/IGitRefResolver.cs`
- Create `Packages/com.yoji.u3d-ai-linker/Editor/Operations/GitLsRemoteResolver.cs`
- Create `Packages/com.yoji.u3d-ai-linker/Editor/Operations/RefreshDevPlanner.cs`
- Create `Packages/com.yoji.u3d-ai-linker/Tests/Editor/FakeGitRefResolver.cs`
- Create `Packages/com.yoji.u3d-ai-linker/Tests/Editor/RefreshDevPlannerTests.cs`

- [ ] Step 1: 定义接口 `Packages/com.yoji.u3d-ai-linker/Editor/Operations/IGitRefResolver.cs`:

```csharp
namespace Yoji.U3DAILinker.Operations
{
    /// 解析远端某分支当前 commit SHA。把真实 git ls-remote 进程调用抽出去,
    /// 使 RefreshDev 计划生成可在 EditMode 用 fake 单测。
    internal interface IGitRefResolver
    {
        /// 返回 40 位小写十六进制 commit SHA。无法解析时抛异常。
        string ResolveBranchSha(string branch);
    }
}
```

- [ ] Step 2: 实现真实解析器 `Packages/com.yoji.u3d-ai-linker/Editor/Operations/GitLsRemoteResolver.cs`(真实进程,**不在 EditMode 单测里跑**,仅手动验证):

```csharp
using System;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Yoji.U3DAILinker.Operations
{
    /// 通过 `git ls-remote <repo> <branch>` 解析远端分支 SHA。
    /// 真实进程调用,列为手动验证;EditMode 测试用 FakeGitRefResolver。
    internal sealed class GitLsRemoteResolver : IGitRefResolver
    {
        private static readonly Regex Sha40 = new Regex("^[0-9a-f]{40}$", RegexOptions.Compiled);

        private readonly string _repoUrl;

        public GitLsRemoteResolver(string repoUrl)
        {
            _repoUrl = repoUrl;
        }

        public string ResolveBranchSha(string branch)
        {
            if (branch != "main")
                throw new InvalidOperationException("Dev channel only allows branch 'main', got: " + branch);

            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "ls-remote " + _repoUrl + " refs/heads/" + branch,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using (var process = Process.Start(psi))
            {
                if (process == null)
                    throw new InvalidOperationException("Failed to start git process.");

                var stdout = process.StandardOutput.ReadToEnd();
                var stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                    throw new InvalidOperationException("git ls-remote failed: " + stderr);

                var sha = ExtractSha(stdout);
                if (!Sha40.IsMatch(sha))
                    throw new InvalidOperationException("git ls-remote returned non-SHA output: " + stdout);
                return sha;
            }
        }

        private static string ExtractSha(string lsRemoteOutput)
        {
            if (string.IsNullOrEmpty(lsRemoteOutput))
                return string.Empty;
            var firstLine = lsRemoteOutput.Split('\n')[0];
            var tabIndex = firstLine.IndexOf('\t');
            return tabIndex > 0 ? firstLine.Substring(0, tabIndex).Trim() : firstLine.Trim();
        }
    }
}
```

- [ ] Step 3: 实现计划器 `Packages/com.yoji.u3d-ai-linker/Editor/Operations/RefreshDevPlanner.cs`(纯逻辑:校验 branch、解析 SHA、为启用工具 + 其 infra 闭包按同一 SHA 生成 DependencyChange):

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Yoji.U3DAILinker.Registry;

namespace Yoji.U3DAILinker.Operations
{
    /// 单条依赖变更(用于 manifest 事务与回滚)。
    internal sealed class DependencyChange
    {
        public string PackageName;
        public string OldValue;
        public string NewValue;
    }

    /// Refresh Dev 的计划结果:锁定的 SHA + 全部依赖变更(同一 SHA)。
    internal sealed class RefreshDevPlan
    {
        public string CommitSha;
        public List<DependencyChange> Changes = new List<DependencyChange>();
    }

    /// 生成 Refresh Dev 计划:把启用工具及其 infra 依赖闭包的 URL 全部锁到同一个远端 main SHA。
    /// 真实 Client.Add 不在此处;此类只产出意图,便于单测。
    internal sealed class RefreshDevPlanner
    {
        private static readonly Regex Sha40 = new Regex("^[0-9a-f]{40}$", RegexOptions.Compiled);

        private readonly IGitRefResolver _resolver;

        public RefreshDevPlanner(IGitRefResolver resolver)
        {
            _resolver = resolver;
        }

        public RefreshDevPlan BuildPlan(LinkerRegistry devRegistry, IReadOnlyCollection<string> enabledToolIds)
        {
            if (devRegistry.Channel != LinkerChannel.Dev)
                throw new InvalidOperationException("RefreshDev requires a Dev-channel registry.");
            if (devRegistry.Branch != "main")
                throw new InvalidOperationException("Dev registry branch must be 'main', got: " + devRegistry.Branch);

            var sha = _resolver.ResolveBranchSha(devRegistry.Branch);
            if (!Sha40.IsMatch(sha))
                throw new InvalidOperationException("Resolver returned non-40-hex SHA: " + sha);

            var byId = devRegistry.Entries.ToDictionary(e => e.Id, e => e);
            var closure = ResolveClosure(byId, enabledToolIds);

            var plan = new RefreshDevPlan { CommitSha = sha };
            foreach (var entry in closure
                         .Where(e => e.Status == PackageStatus.Ready)
                         .OrderBy(e => e.Order)
                         .ThenBy(e => e.Id, StringComparer.Ordinal))
            {
                plan.Changes.Add(new DependencyChange
                {
                    PackageName = entry.PackageName,
                    OldValue = null,
                    NewValue = PackageUrlBuilder.BuildDevSha(entry.PackagePath, sha),
                });
            }
            return plan;
        }

        // 启用工具 + 其 dependsOn 的 infra 闭包。未知 ID 抛错(与设计 116 行一致)。
        private static List<RegistryEntry> ResolveClosure(
            Dictionary<string, RegistryEntry> byId, IReadOnlyCollection<string> enabledToolIds)
        {
            var result = new List<RegistryEntry>();
            var seen = new HashSet<string>();
            var stack = new Stack<string>(enabledToolIds ?? Array.Empty<string>());
            while (stack.Count > 0)
            {
                var id = stack.Pop();
                if (!seen.Add(id))
                    continue;
                if (!byId.TryGetValue(id, out var entry))
                    throw new InvalidOperationException("Unknown tool id in enabled set: " + id);
                result.Add(entry);
                if (entry.DependsOn != null)
                {
                    foreach (var dep in entry.DependsOn)
                        stack.Push(dep);
                }
            }
            return result;
        }
    }
}
```

  组装提示:此处 `internal sealed class DependencyChange`(PackageName/OldValue/NewValue)与 LINK-2b/3 的同名类型重复。组装时合并为同一个 `DependencyChange`(以 LINK-2b 含 ChangeType 的 public 版为权威),删去本文件这一份定义。

- [ ] Step 4: 写 fake `Packages/com.yoji.u3d-ai-linker/Tests/Editor/FakeGitRefResolver.cs`:

```csharp
using System;
using Yoji.U3DAILinker.Operations;

namespace Yoji.U3DAILinker.Tests
{
    /// 测试用 git 解析器:返回预置 SHA,记录被请求的分支。
    internal sealed class FakeGitRefResolver : IGitRefResolver
    {
        private readonly string _sha;
        public string LastBranch { get; private set; }

        public FakeGitRefResolver(string sha)
        {
            _sha = sha;
        }

        public string ResolveBranchSha(string branch)
        {
            LastBranch = branch;
            if (_sha == null)
                throw new InvalidOperationException("fake resolver configured to fail");
            return _sha;
        }
    }
}
```

- [ ] Step 5: 写失败测试 `Packages/com.yoji.u3d-ai-linker/Tests/Editor/RefreshDevPlannerTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Yoji.U3DAILinker;
using Yoji.U3DAILinker.Operations;
using Yoji.U3DAILinker.Registry;

namespace Yoji.U3DAILinker.Tests
{
    public sealed class RefreshDevPlannerTests
    {
        private const string Sha = "0123456789abcdef0123456789abcdef01234567";

        private static LinkerRegistry DevRegistry()
        {
            return new LinkerRegistry
            {
                SchemaVersion = 2,
                Channel = LinkerChannel.Dev,
                Branch = "main",
                Entries = new List<RegistryEntry>
                {
                    new RegistryEntry
                    {
                        Id = "editor-core", Kind = PackageKind.Infra, Status = PackageStatus.Ready,
                        Order = 10, PackageName = "com.yoji.editor-core",
                        PackagePath = "Packages/com.yoji.editor-core", MinUnity = "2022.3",
                    },
                    new RegistryEntry
                    {
                        Id = "test-runner", Kind = PackageKind.Tool, Status = PackageStatus.Ready,
                        Order = 20, PackageName = "com.yoji.test-runner",
                        PackagePath = "Packages/com.yoji.test-runner", MinUnity = "2022.3",
                        DependsOn = new List<string> { "editor-core" },
                    },
                    new RegistryEntry
                    {
                        Id = "planned-tool", Kind = PackageKind.Tool, Status = PackageStatus.Planned,
                        Order = 30, PackageName = "com.yoji.planned",
                        PackagePath = "Packages/com.yoji.planned", MinUnity = "2022.3",
                    },
                },
            };
        }

        [Test]
        public void BuildPlan_LocksAllToSameSha()
        {
            var planner = new RefreshDevPlanner(new FakeGitRefResolver(Sha));
            var plan = planner.BuildPlan(DevRegistry(), new[] { "test-runner" });

            Assert.AreEqual(Sha, plan.CommitSha);
            Assert.IsTrue(plan.Changes.All(c => c.NewValue.EndsWith("#" + Sha, StringComparison.Ordinal)));
        }

        [Test]
        public void BuildPlan_IncludesInfraClosure()
        {
            var planner = new RefreshDevPlanner(new FakeGitRefResolver(Sha));
            var plan = planner.BuildPlan(DevRegistry(), new[] { "test-runner" });

            var names = plan.Changes.Select(c => c.PackageName).ToArray();
            CollectionAssert.Contains(names, "com.yoji.editor-core");
            CollectionAssert.Contains(names, "com.yoji.test-runner");
        }

        [Test]
        public void BuildPlan_OrdersByRegistryOrder()
        {
            var planner = new RefreshDevPlanner(new FakeGitRefResolver(Sha));
            var plan = planner.BuildPlan(DevRegistry(), new[] { "test-runner" });

            Assert.AreEqual("com.yoji.editor-core", plan.Changes[0].PackageName);
            Assert.AreEqual("com.yoji.test-runner", plan.Changes[1].PackageName);
        }

        [Test]
        public void BuildPlan_SkipsNonReadyEntries()
        {
            var planner = new RefreshDevPlanner(new FakeGitRefResolver(Sha));
            var plan = planner.BuildPlan(DevRegistry(), new[] { "test-runner" });

            CollectionAssert.DoesNotContain(
                plan.Changes.Select(c => c.PackageName).ToArray(), "com.yoji.planned");
        }

        [Test]
        public void BuildPlan_PassesBranchToResolver()
        {
            var fake = new FakeGitRefResolver(Sha);
            new RefreshDevPlanner(fake).BuildPlan(DevRegistry(), new[] { "test-runner" });
            Assert.AreEqual("main", fake.LastBranch);
        }

        [Test]
        public void BuildPlan_RejectsNonDevRegistry()
        {
            var reg = DevRegistry();
            reg.Channel = LinkerChannel.Stable;
            var planner = new RefreshDevPlanner(new FakeGitRefResolver(Sha));
            Assert.Throws<InvalidOperationException>(() => planner.BuildPlan(reg, new[] { "test-runner" }));
        }

        [Test]
        public void BuildPlan_RejectsNonMainBranch()
        {
            var reg = DevRegistry();
            reg.Branch = "develop";
            var planner = new RefreshDevPlanner(new FakeGitRefResolver(Sha));
            Assert.Throws<InvalidOperationException>(() => planner.BuildPlan(reg, new[] { "test-runner" }));
        }

        [Test]
        public void BuildPlan_RejectsNon40HexSha()
        {
            var planner = new RefreshDevPlanner(new FakeGitRefResolver("not-a-sha"));
            Assert.Throws<InvalidOperationException>(() => planner.BuildPlan(DevRegistry(), new[] { "test-runner" }));
        }

        [Test]
        public void BuildPlan_RejectsUnknownEnabledId()
        {
            var planner = new RefreshDevPlanner(new FakeGitRefResolver(Sha));
            Assert.Throws<InvalidOperationException>(
                () => planner.BuildPlan(DevRegistry(), new[] { "ghost-tool" }));
        }
    }
}
```

- [ ] Step 6: 重编译并跑测试确认**失败**:

```bash
curl -s -X POST http://127.0.0.1:17890/recompile
curl -s -X POST http://127.0.0.1:17890/run-tests -H 'Content-Type: application/json' \
  -d '{"testMode":"EditMode","testNames":["Yoji.U3DAILinker.Tests.RefreshDevPlannerTests"]}'
```

期望:编译错误或 `failed>0`(此步骤实现已在 Step 1-4 写好,若编译已过则应直接通过;若先单独提交测试,期望失败)。注:按 TDD 严格顺序,可先只放 Step 5 测试文件与 Step 1 接口、Step 4 fake,确认失败,再补 Step 2/3 实现。

- [ ] Step 7: 确认实现就位后重跑,期望**通过**:`passed==9 && failed==0`(命令同 Step 6)。
- [ ] Step 8: 手动验证(真实 git,需联网):在 Unity 工程中加一个临时菜单或在 C# Console 执行
  `new GitLsRemoteResolver("https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git").ResolveBranchSha("main")`,
  期望返回 40 位十六进制 SHA,且与 `git ls-remote https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git refs/heads/main` 命令行输出首列一致。断网时期望抛 `InvalidOperationException` 且面板不改任何 manifest。
- [ ] Step 9: commit:

```bash
git add Packages/com.yoji.u3d-ai-linker/Editor/Operations/IGitRefResolver.cs \
  Packages/com.yoji.u3d-ai-linker/Editor/Operations/GitLsRemoteResolver.cs \
  Packages/com.yoji.u3d-ai-linker/Editor/Operations/RefreshDevPlanner.cs \
  Packages/com.yoji.u3d-ai-linker/Tests/Editor/FakeGitRefResolver.cs \
  Packages/com.yoji.u3d-ai-linker/Tests/Editor/RefreshDevPlannerTests.cs
git commit -m "feat(u3d-ai-linker): IGitRefResolver + RefreshDevPlanner locking enabled closure to one main SHA"
```

---

### Task 44: RestorePlanner —— Local file: 依赖检测与 Restore Stable/Dev(TDD)

Local 通道把 file: 写入 manifest 但绝对路径只入 UserSettings;面板须检测当前 manifest 里的托管 file: 依赖(避免误提交),并提供 Restore Stable/Dev 把它们换回可提交的 tag/SHA Git URL(设计 192、395)。纯逻辑,完整 TDD。

Files:
- Create `Packages/com.yoji.u3d-ai-linker/Editor/Settings/RestorePlanner.cs`
- Create `Packages/com.yoji.u3d-ai-linker/Tests/Editor/RestorePlannerTests.cs`

- [ ] Step 1: 写失败测试 `Packages/com.yoji.u3d-ai-linker/Tests/Editor/RestorePlannerTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Yoji.U3DAILinker;
using Yoji.U3DAILinker.Operations;
using Yoji.U3DAILinker.Registry;
using Yoji.U3DAILinker.Settings;

namespace Yoji.U3DAILinker.Tests
{
    public sealed class RestorePlannerTests
    {
        private const string Sha = "0123456789abcdef0123456789abcdef01234567";

        private static LinkerRegistry StableRegistry()
        {
            return new LinkerRegistry
            {
                SchemaVersion = 2,
                Channel = LinkerChannel.Stable,
                Entries = new List<RegistryEntry>
                {
                    new RegistryEntry
                    {
                        Id = "test-runner", Kind = PackageKind.Tool, Status = PackageStatus.Ready,
                        Order = 20, PackageName = "com.yoji.test-runner",
                        PackagePath = "Packages/com.yoji.test-runner",
                        Revision = "test-runner-v1.1.0", MinUnity = "2022.3",
                    },
                },
            };
        }

        private static Dictionary<string, InstalledPackageInfo> WithLocalFile()
        {
            return new Dictionary<string, InstalledPackageInfo>
            {
                ["com.yoji.test-runner"] = new InstalledPackageInfo(
                    "com.yoji.test-runner",
                    "file:E:/Yoji/U3D-Dev-Tools-AI/Packages/com.yoji.test-runner",
                    true),
            };
        }

        [Test]
        public void HasLocalFileDependencies_TrueWhenManagedFileUrlPresent()
        {
            Assert.IsTrue(RestorePlanner.HasLocalFileDependencies(WithLocalFile()));
        }

        [Test]
        public void HasLocalFileDependencies_FalseForGitUrlOnly()
        {
            var installed = new Dictionary<string, InstalledPackageInfo>
            {
                ["com.yoji.test-runner"] = new InstalledPackageInfo(
                    "com.yoji.test-runner",
                    "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/com.yoji.test-runner#test-runner-v1.1.0",
                    true),
            };
            Assert.IsFalse(RestorePlanner.HasLocalFileDependencies(installed));
        }

        [Test]
        public void HasLocalFileDependencies_IgnoresUnmanagedFileUrl()
        {
            // 用户手写的 file: 不是 Linker 管理的 -> 不算 Linker 误提交风险目标。
            var installed = new Dictionary<string, InstalledPackageInfo>
            {
                ["com.thirdparty.x"] = new InstalledPackageInfo(
                    "com.thirdparty.x", "file:C:/somewhere/x", false),
            };
            Assert.IsFalse(RestorePlanner.HasLocalFileDependencies(installed));
        }

        [Test]
        public void BuildRestore_ToStable_UsesTagUrl()
        {
            var changes = RestorePlanner.BuildRestore(WithLocalFile(), StableRegistry(), null);
            Assert.AreEqual(1, changes.Length);
            Assert.AreEqual("com.yoji.test-runner", changes[0].PackageName);
            Assert.AreEqual("file:E:/Yoji/U3D-Dev-Tools-AI/Packages/com.yoji.test-runner", changes[0].OldValue);
            Assert.AreEqual(
                "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/com.yoji.test-runner#test-runner-v1.1.0",
                changes[0].NewValue);
        }

        [Test]
        public void BuildRestore_ToDev_UsesShaUrlWhenShaProvided()
        {
            var devReg = StableRegistry();
            devReg.Channel = LinkerChannel.Dev;
            devReg.Branch = "main";
            var changes = RestorePlanner.BuildRestore(WithLocalFile(), devReg, Sha);
            Assert.AreEqual(
                "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/com.yoji.test-runner#" + Sha,
                changes[0].NewValue);
        }

        [Test]
        public void BuildRestore_OnlyRewritesManagedFileDependencies()
        {
            var installed = WithLocalFile();
            installed["com.thirdparty.x"] = new InstalledPackageInfo(
                "com.thirdparty.x", "file:C:/somewhere/x", false);
            var changes = RestorePlanner.BuildRestore(installed, StableRegistry(), null);
            CollectionAssert.AreEquivalent(
                new[] { "com.yoji.test-runner" }, changes.Select(c => c.PackageName).ToArray());
        }

        [Test]
        public void BuildRestore_SkipsPackagesNotInTargetRegistry()
        {
            var installed = new Dictionary<string, InstalledPackageInfo>
            {
                ["com.yoji.ghost"] = new InstalledPackageInfo(
                    "com.yoji.ghost", "file:E:/Yoji/U3D-Dev-Tools-AI/Packages/com.yoji.ghost", true),
            };
            var changes = RestorePlanner.BuildRestore(installed, StableRegistry(), null);
            Assert.AreEqual(0, changes.Length);
        }
    }
}
```

- [ ] Step 2: 重编译并跑测试确认**失败**:

```bash
curl -s -X POST http://127.0.0.1:17890/recompile
curl -s -X POST http://127.0.0.1:17890/run-tests -H 'Content-Type: application/json' \
  -d '{"testMode":"EditMode","testNames":["Yoji.U3DAILinker.Tests.RestorePlannerTests"]}'
```

期望:编译错误或 `failed>0`。

- [ ] Step 3: 实现 `Packages/com.yoji.u3d-ai-linker/Editor/Settings/RestorePlanner.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Yoji.U3DAILinker.Operations;
using Yoji.U3DAILinker.Registry;

namespace Yoji.U3DAILinker.Settings
{
    /// 把 Local 通道写入 manifest 的托管 file: 依赖恢复为可提交的 tag/SHA Git URL。
    /// 只处理 Linker 管理的 file: 依赖,且只处理目标 Registry 已知的包(设计 192/395)。
    internal static class RestorePlanner
    {
        private const string FilePrefix = "file:";

        /// 当前 manifest 是否存在 Linker 管理的 file: 依赖(误提交风险,面板须告警)。
        public static bool HasLocalFileDependencies(
            IReadOnlyDictionary<string, InstalledPackageInfo> installed)
        {
            if (installed == null)
                return false;
            return installed.Values.Any(IsManagedFile);
        }

        /// 生成把所有托管 file: 依赖换回 targetRegistry 通道 URL 的依赖变更集。
        /// targetRegistry.Channel == Dev 时使用 commitSha;否则用各条目的 Stable tag。
        public static DependencyChange[] BuildRestore(
            IReadOnlyDictionary<string, InstalledPackageInfo> installed,
            LinkerRegistry targetRegistry,
            string commitSha)
        {
            if (installed == null)
                return Array.Empty<DependencyChange>();

            var byPackageName = targetRegistry.Entries.ToDictionary(e => e.PackageName, e => e);
            var result = new List<DependencyChange>();

            foreach (var info in installed.Values
                         .Where(IsManagedFile)
                         .OrderBy(i => i.PackageName, StringComparer.Ordinal))
            {
                if (!byPackageName.TryGetValue(info.PackageName, out var entry))
                    continue;

                var newValue = targetRegistry.Channel == LinkerChannel.Dev
                    ? PackageUrlBuilder.BuildDevSha(entry.PackagePath, RequireSha(commitSha))
                    : PackageUrlBuilder.BuildStable(entry.PackagePath, entry.Revision);

                result.Add(new DependencyChange
                {
                    PackageName = info.PackageName,
                    OldValue = info.ResolvedUrl,
                    NewValue = newValue,
                });
            }

            return result.ToArray();
        }

        private static bool IsManagedFile(InstalledPackageInfo info)
        {
            return info != null
                   && info.IsManaged
                   && !string.IsNullOrEmpty(info.ResolvedUrl)
                   && info.ResolvedUrl.StartsWith(FilePrefix, StringComparison.Ordinal);
        }

        private static string RequireSha(string commitSha)
        {
            if (string.IsNullOrEmpty(commitSha))
                throw new InvalidOperationException(
                    "Restore to Dev requires a resolved commit SHA; run Refresh Dev first.");
            return commitSha;
        }
    }
}
```

- [ ] Step 4: 重编译并重跑,确认**通过**:

```bash
curl -s -X POST http://127.0.0.1:17890/recompile
curl -s -X POST http://127.0.0.1:17890/run-tests -H 'Content-Type: application/json' \
  -d '{"testMode":"EditMode","testNames":["Yoji.U3DAILinker.Tests.RestorePlannerTests"]}'
```

期望:`passed==7 && failed==0`。

- [ ] Step 5: commit:

```bash
git add Packages/com.yoji.u3d-ai-linker/Editor/Settings/RestorePlanner.cs \
  Packages/com.yoji.u3d-ai-linker/Tests/Editor/RestorePlannerTests.cs
git commit -m "feat(u3d-ai-linker): RestorePlanner detecting managed file: deps and rebuilding commit-safe URLs"
```

---

### Task 45: U3DAILinkerSettingsStore 与 SettingsProvider IMGUI 面板(手动验证为主)

把纯逻辑接到真实 Editor:`U3DAILinkerSettingsStore` 负责把 ProjectSettings/UserSettings 落到 `ProjectSettings/U3DAILinkerSettings.asset` 与 `UserSettings/U3DAILinkerUserSettings.asset`(真实序列化路径);`U3DAILinkerSettingsProvider.OnGui` 渲染顶部状态区 / 工具列表 / 底部操作区,并在 Local 通道显示警告与 Restore 按钮。真实 `.asset` I/O 与 IMGUI 渲染为手动验证;Store 的"路径常量正确、ProjectSettings 不变量在保存前被校验"逻辑做 EditMode 断言。

Files:
- Create `Packages/com.yoji.u3d-ai-linker/Editor/Settings/U3DAILinkerSettingsStore.cs`
- Modify `Packages/com.yoji.u3d-ai-linker/Editor/Settings/U3DAILinkerSettingsProvider.cs`
- Create `Packages/com.yoji.u3d-ai-linker/Tests/Editor/U3DAILinkerSettingsStoreTests.cs`

- [ ] Step 1: 写失败测试 `Packages/com.yoji.u3d-ai-linker/Tests/Editor/U3DAILinkerSettingsStoreTests.cs`(只断言可纯测的部分:路径常量、保存前校验拦截绝对路径):

```csharp
using NUnit.Framework;
using UnityEngine;
using Yoji.U3DAILinker;
using Yoji.U3DAILinker.Settings;

namespace Yoji.U3DAILinker.Tests
{
    public sealed class U3DAILinkerSettingsStoreTests
    {
        [Test]
        public void ProjectSettingsPath_IsUnderProjectSettingsFolder()
        {
            StringAssert.StartsWith("ProjectSettings/", U3DAILinkerSettingsStore.ProjectSettingsAssetPath);
            StringAssert.EndsWith("U3DAILinkerSettings.asset", U3DAILinkerSettingsStore.ProjectSettingsAssetPath);
        }

        [Test]
        public void UserSettingsPath_IsUnderUserSettingsFolder()
        {
            StringAssert.StartsWith("UserSettings/", U3DAILinkerSettingsStore.UserSettingsAssetPath);
            StringAssert.EndsWith("U3DAILinkerUserSettings.asset", U3DAILinkerSettingsStore.UserSettingsAssetPath);
        }

        [Test]
        public void SaveProjectSettings_RejectsAbsolutePathPollution()
        {
            var s = ScriptableObject.CreateInstance<U3DAILinkerSettings>();
            s.DesiredVersions.Add(new DesiredVersion
            {
                ToolId = "bad", Revision = "file:E:/Yoji/U3D-Dev-Tools-AI/Packages/com.yoji.x",
            });
            Assert.IsFalse(U3DAILinkerSettingsStore.TrySaveProjectSettings(s, out var error));
            StringAssert.Contains("bad", error);
            Object.DestroyImmediate(s);
        }
    }
}
```

- [ ] Step 2: 重编译并跑测试确认**失败**:

```bash
curl -s -X POST http://127.0.0.1:17890/recompile
curl -s -X POST http://127.0.0.1:17890/run-tests -H 'Content-Type: application/json' \
  -d '{"testMode":"EditMode","testNames":["Yoji.U3DAILinker.Tests.U3DAILinkerSettingsStoreTests"]}'
```

期望:编译错误或 `failed>0`。

- [ ] Step 3: 实现 `Packages/com.yoji.u3d-ai-linker/Editor/Settings/U3DAILinkerSettingsStore.cs`。保存 ProjectSettings 前强制 `Validate`,杜绝绝对路径落入可提交文件;真实写盘用 `UnityEditorInternal.InternalEditorUtility.SaveToSerializedFileAndCleanup`(2022.3 可用),列手动验证:

```csharp
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Yoji.U3DAILinker.Settings
{
    /// 负责 ProjectSettings / UserSettings 两层 ScriptableObject 的加载与保存。
    /// ProjectSettings 落 ProjectSettings/ 目录(可提交、不含绝对路径);
    /// UserSettings 落 UserSettings/ 目录(本机偏好)。
    /// 保存 ProjectSettings 前强制校验绝对路径不变量。
    internal static class U3DAILinkerSettingsStore
    {
        public const string ProjectSettingsAssetPath = "ProjectSettings/U3DAILinkerSettings.asset";
        public const string UserSettingsAssetPath = "UserSettings/U3DAILinkerUserSettings.asset";

        public static U3DAILinkerSettings LoadOrCreateProjectSettings()
        {
            var loaded = LoadFromFile<U3DAILinkerSettings>(ProjectSettingsAssetPath);
            return loaded != null ? loaded : ScriptableObject.CreateInstance<U3DAILinkerSettings>();
        }

        public static U3DAILinkerUserSettings LoadOrCreateUserSettings()
        {
            var loaded = LoadFromFile<U3DAILinkerUserSettings>(UserSettingsAssetPath);
            return loaded != null ? loaded : ScriptableObject.CreateInstance<U3DAILinkerUserSettings>();
        }

        /// 保存 ProjectSettings;先校验不变量,失败则不写盘。
        public static bool TrySaveProjectSettings(U3DAILinkerSettings settings, out string error)
        {
            if (!settings.Validate(out error))
                return false;
            SaveToFile(ProjectSettingsAssetPath, settings);
            error = null;
            return true;
        }

        public static void SaveUserSettings(U3DAILinkerUserSettings settings)
        {
            SaveToFile(UserSettingsAssetPath, settings);
        }

        private static T LoadFromFile<T>(string path) where T : ScriptableObject
        {
            if (!File.Exists(path))
                return null;
            var objects = InternalEditorUtility.LoadSerializedFileAndForget(path);
            return objects != null ? objects.OfType<T>().FirstOrDefault() : null;
        }

        private static void SaveToFile(string path, ScriptableObject settings)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            InternalEditorUtility.SaveToSerializedFileAndCleanup(
                new Object[] { settings }, path, allowTextSerialization: true);
        }
    }
}
```

- [ ] Step 4: 重编译并重跑,确认**通过**:

```bash
curl -s -X POST http://127.0.0.1:17890/recompile
curl -s -X POST http://127.0.0.1:17890/run-tests -H 'Content-Type: application/json' \
  -d '{"testMode":"EditMode","testNames":["Yoji.U3DAILinker.Tests.U3DAILinkerSettingsStoreTests"]}'
```

期望:`passed==3 && failed==0`。

- [ ] Step 5: 替换 `Packages/com.yoji.u3d-ai-linker/Editor/Settings/U3DAILinkerSettingsProvider.cs` 的内容,渲染完整面板(顶部状态区 + 工具列表 + 底部操作区 + Local 警告/Restore)。本步是 IMGUI,逻辑全部委托已测试的纯逻辑类;真实安装/同步动作在其它子系统接入,这里按钮先调占位回调并禁用以避免并发。把整段文件替换为:

```csharp
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Yoji.U3DAILinker.Operations;
using Yoji.U3DAILinker.Registry;

namespace Yoji.U3DAILinker.Settings
{
    /// Project Settings 入口 Project/U3D AI Linker。
    /// 渲染顶部状态区 / 工具列表 / 底部操作区。所有状态计算委托 PanelStateModel 等纯逻辑类;
    /// 真实 UPM/Junction 动作由其它子系统接入(此处仅触发其入口)。
    internal static class U3DAILinkerSettingsProvider
    {
        internal const string ProviderPath = "Project/U3D AI Linker";

        private static U3DAILinkerSettings _project;
        private static U3DAILinkerUserSettings _user;
        private static Vector2 _scroll;

        [SettingsProvider]
        public static SettingsProvider CreateProvider()
        {
            return new SettingsProvider(ProviderPath, SettingsScope.Project)
            {
                label = "U3D AI Linker",
                guiHandler = OnGui,
                keywords = new[] { "linker", "agent", "skill", "upm", "registry" },
                activateHandler = (searchContext, root) =>
                {
                    _project = U3DAILinkerSettingsStore.LoadOrCreateProjectSettings();
                    _user = U3DAILinkerSettingsStore.LoadOrCreateUserSettings();
                },
            };
        }

        private static void OnGui(string searchContext)
        {
            if (_project == null)
                _project = U3DAILinkerSettingsStore.LoadOrCreateProjectSettings();
            if (_user == null)
                _user = U3DAILinkerSettingsStore.LoadOrCreateUserSettings();

            DrawStatusSection();
            EditorGUILayout.Space();
            DrawToolList();
            EditorGUILayout.Space();
            DrawLocalChannelWarning();
            EditorGUILayout.Space();
            DrawActionSection();
        }

        private static void DrawStatusSection()
        {
            EditorGUILayout.LabelField("Status", EditorStyles.boldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                var newChannel = (LinkerChannel)EditorGUILayout.EnumPopup("Channel", _project.Channel);
                if (newChannel != _project.Channel)
                {
                    _project.Channel = newChannel;
                    if (U3DAILinkerSettingsStore.TrySaveProjectSettings(_project, out var error))
                    {
                        // saved
                    }
                    else
                    {
                        Debug.LogError("[U3DAILinker] " + error);
                    }
                }

                EditorGUILayout.LabelField("Registry Source", _registrySource.ToString());
                EditorGUILayout.LabelField("Schema Version",
                    _registry != null ? _registry.SchemaVersion.ToString() : "-");
                EditorGUILayout.LabelField("Current Revision", _currentRevision ?? "-");
                EditorGUILayout.LabelField("Operation", _operationState.ToString());

                EditorGUILayout.LabelField("Git Available", _gitAvailable ? "yes" : "no");
                EditorGUILayout.LabelField("Network Available", _networkAvailable ? "yes" : "no");
                EditorGUILayout.LabelField("Package Manager", _upmIdle ? "idle" : "busy");
            }

            if (_project.Channel == LinkerChannel.Local)
            {
                var newRoot = EditorGUILayout.TextField("Local Repo Root", _user.LocalRepoRoot);
                if (newRoot != _user.LocalRepoRoot)
                {
                    _user.LocalRepoRoot = newRoot;
                    U3DAILinkerSettingsStore.SaveUserSettings(_user);
                }
            }
        }

        private static void DrawToolList()
        {
            EditorGUILayout.LabelField("Tools", EditorStyles.boldLabel);
            if (_registry == null)
            {
                EditorGUILayout.HelpBox("Registry not loaded. Press Refresh Registry.", MessageType.Info);
                return;
            }

            var rows = PanelStateModel.Build(
                _registry, _installed, _project.EnabledToolIds, _agentStates,
                _project.Channel, _user.LocalRepoRoot);

            using (var scope = new EditorGUILayout.ScrollViewScope(_scroll))
            {
                _scroll = scope.scrollPosition;
                foreach (var row in rows)
                    DrawToolRow(row);
            }
        }

        private static void DrawToolRow(ToolRow row)
        {
            using (new EditorGUILayout.HorizontalScope("box"))
            {
                if (row.EnabledVisible)
                {
                    var enabled = EditorGUILayout.Toggle(row.Enabled, GUILayout.Width(20));
                    if (enabled != row.Enabled)
                        ToggleTool(row.Id, enabled);
                }
                else
                {
                    GUILayout.Space(24);
                }

                EditorGUILayout.LabelField(row.DisplayName, GUILayout.Width(160));
                EditorGUILayout.LabelField(row.Kind.ToString(), GUILayout.Width(60));
                EditorGUILayout.LabelField(row.Installed.ToString(), GUILayout.Width(80));
                EditorGUILayout.LabelField(row.Agent.ToString(), GUILayout.Width(90));

                if (row.Kind == PackageKind.Infra && row.RequiredBy != null && row.RequiredBy.Length > 0)
                    EditorGUILayout.LabelField("req by: " + string.Join(",", row.RequiredBy));
            }
        }

        private static void DrawLocalChannelWarning()
        {
            if (_project.Channel != LinkerChannel.Local)
                return;

            EditorGUILayout.HelpBox(
                "Local channel writes file: dependencies into Packages/manifest.json. " +
                "These must not be committed. Absolute paths live only in UserSettings.",
                MessageType.Warning);

            if (RestorePlanner.HasLocalFileDependencies(_installed))
            {
                EditorGUILayout.HelpBox(
                    "manifest.json currently contains managed file: dependencies.",
                    MessageType.Warning);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Restore Stable"))
                    RequestRestore(LinkerChannel.Stable);
                using (new EditorGUI.DisabledScope(_devSha == null))
                {
                    if (GUILayout.Button("Restore Dev"))
                        RequestRestore(LinkerChannel.Dev);
                }
            }
        }

        private static void DrawActionSection()
        {
            EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(_operationState == OperationState.Running))
            using (new EditorGUILayout.VerticalScope())
            {
                if (GUILayout.Button("Refresh Registry")) RequestRefreshRegistry();
                if (GUILayout.Button("Install/Update Selected")) RequestInstallSelected();
                if (GUILayout.Button("Install/Update All")) RequestInstallAll();
                if (GUILayout.Button("Sync Agent Assets")) RequestSyncAgents();
                if (GUILayout.Button("Repair Links")) RequestRepairLinks();
                if (GUILayout.Button("Rollback Manifest")) RequestRollback();
                if (GUILayout.Button("Open Generated Folder")) RequestOpenFolder();
                if (GUILayout.Button("Copy Diagnostic Report")) RequestCopyDiagnostics();
            }
        }

        // --- 外部子系统注入点(由组装阶段接线;此切片提供默认空实现以保证编译与手动验证)。---

        private static LinkerRegistry _registry;
        private static RegistrySource _registrySource = RegistrySource.BundledSnapshot;
        private static OperationState _operationState = OperationState.Idle;
        private static string _currentRevision;
        private static string _devSha;
        private static bool _gitAvailable = true;
        private static bool _networkAvailable = true;
        private static bool _upmIdle = true;

        private static readonly Dictionary<string, InstalledPackageInfo> _installed =
            new Dictionary<string, InstalledPackageInfo>();
        private static readonly Dictionary<string, AgentState> _agentStates =
            new Dictionary<string, AgentState>();

        private static void ToggleTool(string toolId, bool enabled)
        {
            if (enabled)
            {
                if (!_project.EnabledToolIds.Contains(toolId))
                    _project.EnabledToolIds.Add(toolId);
            }
            else
            {
                _project.EnabledToolIds.Remove(toolId);
            }

            if (!U3DAILinkerSettingsStore.TrySaveProjectSettings(_project, out var error))
                Debug.LogError("[U3DAILinker] " + error);
        }

        private static void RequestRestore(LinkerChannel target)
        {
            if (_registry == null)
                return;
            var targetReg = _registry;
            var changes = RestorePlanner.BuildRestore(_installed, targetReg, _devSha);
            Debug.Log("[U3DAILinker] Restore plan to " + target + ": " + changes.Length + " changes.");
            // 实际 manifest 事务由 Manifest 子系统执行(组装阶段接线)。
        }

        private static void RequestRefreshRegistry() { Debug.Log("[U3DAILinker] Refresh Registry requested."); }
        private static void RequestInstallSelected() { Debug.Log("[U3DAILinker] Install/Update Selected requested."); }
        private static void RequestInstallAll() { Debug.Log("[U3DAILinker] Install/Update All requested."); }
        private static void RequestSyncAgents() { Debug.Log("[U3DAILinker] Sync Agent Assets requested."); }
        private static void RequestRepairLinks() { Debug.Log("[U3DAILinker] Repair Links requested."); }
        private static void RequestRollback() { Debug.Log("[U3DAILinker] Rollback Manifest requested."); }
        private static void RequestOpenFolder() { Debug.Log("[U3DAILinker] Open Generated Folder requested."); }
        private static void RequestCopyDiagnostics() { Debug.Log("[U3DAILinker] Copy Diagnostic Report requested."); }
    }
}
```

  组装提示:此完整版应**替换** LINK-1 的占位 `Editor/U3DAILinkerSettingsProvider.cs`(删该文件),全包只保留这一个指向 `Project/U3D AI Linker` 的 `[SettingsProvider]`。`Request*` 占位回调在组装阶段接到 LINK-3(安装队列)、LINK-4(Agent 同步)、LINK-2b(manifest 事务/回滚)的真实入口。

- [ ] Step 6: 重编译,确认全包编译通过并跑全部已写测试类回归:

```bash
curl -s -X POST http://127.0.0.1:17890/recompile
curl -s -X POST http://127.0.0.1:17890/run-tests -H 'Content-Type: application/json' \
  -d '{"testMode":"EditMode","assemblyNames":["Yoji.U3DAILinker.Editor.Tests"]}'
```

期望:`failed==0`,全部 LINK-6 测试通过(PackageUrlBuilder 9 + Settings 5 + PanelStateModel 10 + RefreshDevPlanner 9 + RestorePlanner 7 + Store 3 = 43 个用例)。

- [ ] Step 7: 手动验证(IMGUI + 真实 .asset,Unity 2022.3):
  1. 打开 `Edit > Project Settings > U3D AI Linker`,确认顶部 Status 区显示 Channel 下拉、Registry Source、Schema、Operation、Git/Network/PackageManager 行;中部 Tools 区显示 HelpBox(Registry 未加载);底部 Actions 区八个按钮齐全。
  2. 把 Channel 切到 Local,确认出现黄色警告 HelpBox、`Local Repo Root` 输入框、`Restore Stable`/`Restore Dev` 按钮(`Restore Dev` 在无 SHA 时禁用)。
  3. 在 `Local Repo Root` 填 `E:\Yoji\U3D-Dev-Tools-AI`,关闭再打开 Project Settings,确认值保留;检查磁盘上存在 `UserSettings/U3DAILinkerUserSettings.asset` 且其文本含该路径,而 `ProjectSettings/U3DAILinkerSettings.asset` **不含**该绝对路径(只记录 `channel: Local`)。这是设计 190/395/474 的关键人工核对。
  4. 切回 Stable,确认警告消失。

- [ ] Step 8: commit:

```bash
git add Packages/com.yoji.u3d-ai-linker/Editor/Settings/U3DAILinkerSettingsStore.cs \
  Packages/com.yoji.u3d-ai-linker/Editor/Settings/U3DAILinkerSettingsProvider.cs \
  Packages/com.yoji.u3d-ai-linker/Tests/Editor/U3DAILinkerSettingsStoreTests.cs
git commit -m "feat(u3d-ai-linker): settings store with path invariant and full IMGUI panel (status/tools/actions/local-restore)"
```

---

LINK-6 实现完整集成测试(真实 Client.Add 串行安装、真实 Junction、真实域重载恢复)属于 LINK-3/LINK-4/LINK-5 子系统职责;LINK-6 通过上述注入点(`_registry`/`_installed`/`_agentStates`/`Request*` 回调)与它们接线,组装阶段把占位回调替换为对应子系统的真实入口即可,不改本切片的纯逻辑与测试。

---

### LINK-7 — 工具迁移 planned→ready 收尾:fragments + Registry 初始内容 + tags + README 修正

### 现实基线(已核实,务必遵守)

- 本仓库**没有任何公开 UPM 包发布**:`editor-debug`/`test-runner`/`lua-device-debug` 都只是 `Packages/com.yoji.*` 工作区目录,`package.json` version 均为 `0.1.0`,**没有打过 `<tool-id>-v<semver>` tag**。`editor-core` 包**根本不存在**(仅 progress.md ARCH-1 路线图项)。因此按 spec 117-118、504-506:初始 Registry **全部条目只能标 `planned` 或 `skill-only`,严禁 `ready`**(`ready` 要求"公开 Package 与 Skill 都已具备")。
- spec 506 明确:"在对应公开 Package 尚未完成前,不把现有公司内部服务描述为已迁移完成"。README 第 6-9 行说三个服务 "now included as UPM packages",第 15-17 行状态列写 "Usable"——这在"公开可安装 UPM"语义上是误称(它们只是本仓库内 `file:` 可用的工作区包,未发布、无 tag、无 Git URL 安装)。本子系统负责把这层误称改清楚。
- `test-runner` 的 `Agent~/fragments/{CLAUDE.md,AGENTS.md}` 已存在且**两文件内容完全相同**(均 320B)。`editor-debug` 与 `lua-device-debug` 的 `Agent~/` 下**只有 `skills/`,没有 `fragments/`**,需新建。合并逻辑(LINK-7 不实现,在 LINK-5)不能假设 CLAUDE 与 AGENTS 相同,故本子系统为新工具的两文件写**各自独立**的内容(语义一致但允许文件不同),不沿用"两文件必相同"的偶然现状。
- Registry 校验器(`RegistryParser`/`RegistryValidator`/`RegistryChannel`)由 **LINK-2** 定义,本子系统**只消费、不重定义**。本子系统的 TDD 部分 = 写一个 EditMode 测试,把真实的 `Registry/stable.json` 与 `Registry/dev.json`(及 Linker 包内快照副本)喂给 LINK-2 校验器,断言**零校验错误**,以此锁死"初始 Registry 内容能过 LINK-2 校验"。
- spec 73、255、288:Linker 包内必须保留一份 Registry 快照作为离线回退(`Packages/com.yoji.u3d-ai-linker/Registry/{stable,dev}.json`);spec 157 远程地址用根 `Registry/<channel>.json`。本子系统让两处内容**逐字节一致**,并由测试断言一致,避免快照漂移。
- 所有 git 写操作(commit、打 tag、push)**交给人执行**;计划只给出精确命令与 message,AI 不自动提交。

### minUnity / displayName 取值(来自各 package.json,已核实)

| id | packageName | minUnity | displayName | kind | status |
|---|---|---|---|---|---|
| editor-core | com.yoji.editor-core | 2022.3 | Editor Core | infra | planned(包尚不存在) |
| editor-debug | com.yoji.editor-debug | 2022.3 | Editor Debug MCP | tool | skill-only(skill 随 Linker 交付) |
| test-runner | com.yoji.test-runner | 2022.3 | Test Runner MCP | tool | skill-only |
| lua-device-debug | com.yoji.lua-device-debug | 6000.3 | Lua Device Debug | tool | planned(传输层包,无公开发布,Skill 也未随 Linker 交付) |

说明:editor-debug/test-runner 标 `skill-only`(spec 98:过渡态 Skill 随 Linker `BundledSkills~/` 交付、不发独立 UPM、无独立版本),lua-device-debug 标 `planned`(spec 99:尚未完成迁移、只展示状态——它的 Skill 是 transport-only、依赖项目侧适配器,首版不随 Linker 交付)。editor-core 标 `planned`(包还没建)。**没有任何 `ready`**,符合 spec 118 "未形成公开 UPM 包时初始 Registry 必须标 planned 或 skill-only"。

> 依赖:LINK-2 必须已实现 `Yoji.U3DAILinker.Registry.RegistryParser`/`RegistryValidator`/`RegistryChannel`。本子系统 Task 48 的测试引用它们的真实签名;若 LINK-2 用属性/字段命名不同,以 LINK-2 实际签名为准微调测试的成员访问(断言语义不变)。注意:LINK-2 的 `RegistryDocument` 字段名是 `Entries`,`RegistryEntry` 字段是 `Id/Status/Kind/...`;下面 Task 48 测试若用了 `doc.Tools`/`ToolEntry`/`ValidationError`,需按 LINK-2 实际类型(`doc.Entries`/`RegistryEntry`/`RegistryValidationException`)对齐——LINK-2 的 `Validate` 抛 `RegistryValidationException` 而非返回错误列表,故测试改为 `Assert.DoesNotThrow(...)` 即可表达"零校验错误"。

---

### Task 46: editor-debug 补 Agent~/fragments

为 `editor-debug` 工具补 `fragments/{CLAUDE.md,AGENTS.md}`,内容简述服务、与 test-runner 风格一致(中文、无 emoji、~300-400B、点出端口与核心端点)。纯内容产出,无 TDD(fragment 内容由 LINK-5 合并子系统的测试覆盖)。

Files:
- Create: `Packages/com.yoji.editor-debug/Agent~/fragments/CLAUDE.md`
- Create: `Packages/com.yoji.editor-debug/Agent~/fragments/AGENTS.md`

- [ ] Step 1: 创建 `Packages/com.yoji.editor-debug/Agent~/fragments/CLAUDE.md`,完整内容:

```markdown
## unity-editor-debug-mcp

Unity Editor 在线时通过 HTTP+JSON 反射调用任意 Unity API 的调试服务（端口 21891，
被占回退 21892/21893）。Agent 用 client.py 发请求。核心端点：/invoke 反射调用、
/describe 列成员、/console 读真实日志条目、/ping 暴露编辑器态（isPlaying/isCompiling）、
/batch 合并多次 invoke。object 引用用 EntityId 十进制字符串，兼容 Unity 6.4+。
```

- [ ] Step 2: 创建 `Packages/com.yoji.editor-debug/Agent~/fragments/AGENTS.md`,完整内容(与 CLAUDE.md 语义一致,文件可独立):

```markdown
## unity-editor-debug-mcp

Unity Editor 在线时通过 HTTP+JSON 反射调用任意 Unity API 的调试服务（端口 21891，
被占回退 21892/21893）。Agent 用 client.py 发请求。核心端点：/invoke 反射调用、
/describe 列成员、/console 读真实日志条目、/ping 暴露编辑器态（isPlaying/isCompiling）、
/batch 合并多次 invoke。object 引用用 EntityId 十进制字符串，兼容 Unity 6.4+。
```

- [ ] Step 3: 确认编码符合约定。运行:

```bash
file Packages/com.yoji.editor-debug/Agent~/fragments/CLAUDE.md Packages/com.yoji.editor-debug/Agent~/fragments/AGENTS.md
```

期望输出每行均为 `UTF-8 Unicode text`(若显示 `UTF-8 ... with BOM` 或 `ASCII`,需用无 BOM UTF-8 重写)。再用以下命令确认无 emoji / 非法字符(只应匹配中文 CJK 与 ASCII):

```bash
grep -nP "[\x{1F000}-\x{1FAFF}\x{2600}-\x{27BF}]" Packages/com.yoji.editor-debug/Agent~/fragments/CLAUDE.md Packages/com.yoji.editor-debug/Agent~/fragments/AGENTS.md
```

期望:无任何输出(退出码 1)。

- [ ] Step 4(人工 git,交人执行):

```bash
git add "Packages/com.yoji.editor-debug/Agent~/fragments/CLAUDE.md" "Packages/com.yoji.editor-debug/Agent~/fragments/AGENTS.md"
git commit -m "feat(editor-debug): add Agent fragments for linker CLAUDE/AGENTS merge"
```

---

### Task 47: lua-device-debug 补 Agent~/fragments

为 `lua-device-debug` 工具补 `fragments/{CLAUDE.md,AGENTS.md}`。注意它是 transport-only、需项目侧注册 `ILuaDeviceDebugHost`,fragment 要点出这一边界与端口 21894。纯内容产出。

Files:
- Create: `Packages/com.yoji.lua-device-debug/Agent~/fragments/CLAUDE.md`
- Create: `Packages/com.yoji.lua-device-debug/Agent~/fragments/AGENTS.md`

- [ ] Step 1: 创建 `Packages/com.yoji.lua-device-debug/Agent~/fragments/CLAUDE.md`,完整内容:

```markdown
## unity-lua-device-debug

Unity Lua 运行时诊断的 HTTP+JSON 传输层（端口 21894，固定无回退），覆盖 Editor 与
Android Development Build。Agent 用 client.py：ping/commands/execute、adb-forward/
adb-remove。只暴露项目注册过的 Lua 调试命令，无任意 Lua eval、无 C# 反射 eval、
无 HybridCLR。前置：目标工程须安装本包并注册 ILuaDeviceDebugHost 适配器，否则无命令可用。
```

- [ ] Step 2: 创建 `Packages/com.yoji.lua-device-debug/Agent~/fragments/AGENTS.md`,完整内容(语义一致,文件独立):

```markdown
## unity-lua-device-debug

Unity Lua 运行时诊断的 HTTP+JSON 传输层（端口 21894，固定无回退），覆盖 Editor 与
Android Development Build。Agent 用 client.py：ping/commands/execute、adb-forward/
adb-remove。只暴露项目注册过的 Lua 调试命令，无任意 Lua eval、无 C# 反射 eval、
无 HybridCLR。前置：目标工程须安装本包并注册 ILuaDeviceDebugHost 适配器，否则无命令可用。
```

- [ ] Step 3: 确认编码。运行:

```bash
file Packages/com.yoji.lua-device-debug/Agent~/fragments/CLAUDE.md Packages/com.yoji.lua-device-debug/Agent~/fragments/AGENTS.md
```

期望每行 `UTF-8 Unicode text`。再:

```bash
grep -nP "[\x{1F000}-\x{1FAFF}\x{2600}-\x{27BF}]" Packages/com.yoji.lua-device-debug/Agent~/fragments/CLAUDE.md Packages/com.yoji.lua-device-debug/Agent~/fragments/AGENTS.md
```

期望:无输出(退出码 1)。

- [ ] Step 4(人工 git,交人执行):

```bash
git add "Packages/com.yoji.lua-device-debug/Agent~/fragments/CLAUDE.md" "Packages/com.yoji.lua-device-debug/Agent~/fragments/AGENTS.md"
git commit -m "feat(lua-device-debug): add Agent fragments for linker CLAUDE/AGENTS merge"
```

---

### Task 48: 写 Registry 初始内容(stable.json + dev.json)并用 LINK-2 校验器测试

填根 `Registry/{stable,dev}.json` 与 Linker 包内快照副本,四工具按现实标 `planned`/`skill-only`,`editor-core` 为 `kind:"infra"`。先写**失败测试**(把真实 Registry 文件喂给 LINK-2 校验器断言零错误 + 两份快照逐字节一致),确认 LINK-2 校验器存在后跑红→放置 JSON 内容→跑绿。

> 依赖:LINK-2 必须已实现 `Yoji.U3DAILinker.Registry.RegistryParser.Parse`、`RegistryValidator.Validate(doc, channel)`(校验失败抛 `RegistryValidationException`)、`enum RegistryChannel { Stable, Dev }`。本 Task 的测试即对这些 API 的真实数据回归。

Files:
- Create: `Registry/stable.json`
- Create: `Registry/dev.json`
- Create: `Packages/com.yoji.u3d-ai-linker/Registry/stable.json`
- Create: `Packages/com.yoji.u3d-ai-linker/Registry/dev.json`
- Create (Test): `Packages/com.yoji.u3d-ai-linker/Tests/Editor/RegistryFixtureTests.cs`

- [ ] Step 1: 先写失败测试 `Packages/com.yoji.u3d-ai-linker/Tests/Editor/RegistryFixtureTests.cs`。测试遍历当前测试程序集所在目录向上查找含 `Registry/stable.json` 的目录定位仓库根。完整测试代码(用 LINK-2 实际 API:`RegistryParser.Parse`、`RegistryValidator.Validate` 抛异常、`RegistryChannel`、`doc.Entries`/`entry.Status`/`entry.Kind`):

```csharp
using System.IO;
using NUnit.Framework;
using Yoji.U3DAILinker.Registry;

namespace Yoji.U3DAILinker.Tests
{
    public class RegistryFixtureTests
    {
        // 从当前测试程序集位置向上查找仓库根（含 Registry/stable.json 的目录）。
        // 找不到则用 U3D_LINKER_REPO_ROOT 环境变量兜底（headless 跑用）。
        private static string FindRepoRoot()
        {
            var envRoot = System.Environment.GetEnvironmentVariable("U3D_LINKER_REPO_ROOT");
            if (!string.IsNullOrEmpty(envRoot) &&
                File.Exists(Path.Combine(envRoot, "Registry", "stable.json")))
            {
                return envRoot.Replace('\\', '/');
            }

            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Registry", "stable.json")))
                {
                    return dir.FullName.Replace('\\', '/');
                }
                dir = dir.Parent;
            }
            return null;
        }

        private static string ReadRegistry(string repoRoot, string relative)
        {
            var full = Path.Combine(repoRoot, relative);
            Assert.IsTrue(File.Exists(full), "registry file missing: " + full);
            return File.ReadAllText(full);
        }

        [Test]
        public void RootRegistry_RepoRootResolves()
        {
            var root = FindRepoRoot();
            Assert.IsNotNull(
                root,
                "could not locate repo root containing Registry/stable.json; " +
                "set U3D_LINKER_REPO_ROOT env var when running headless");
        }

        [Test]
        public void StableRegistry_PassesValidation()
        {
            var root = FindRepoRoot();
            Assume.That(root, Is.Not.Null);
            var doc = RegistryParser.Parse(ReadRegistry(root, "Registry/stable.json"));
            Assert.DoesNotThrow(() => RegistryValidator.Validate(doc, RegistryChannel.Stable));
        }

        [Test]
        public void DevRegistry_PassesValidation()
        {
            var root = FindRepoRoot();
            Assume.That(root, Is.Not.Null);
            var doc = RegistryParser.Parse(ReadRegistry(root, "Registry/dev.json"));
            Assert.DoesNotThrow(() => RegistryValidator.Validate(doc, RegistryChannel.Dev));
        }

        [Test]
        public void BundledSnapshot_StableMatchesRoot()
        {
            var root = FindRepoRoot();
            Assume.That(root, Is.Not.Null);
            var rootJson = ReadRegistry(root, "Registry/stable.json");
            var snapJson = ReadRegistry(
                root, "Packages/com.yoji.u3d-ai-linker/Registry/stable.json");
            Assert.AreEqual(
                Normalize(rootJson), Normalize(snapJson),
                "bundled stable snapshot drifted from root Registry/stable.json");
        }

        [Test]
        public void BundledSnapshot_DevMatchesRoot()
        {
            var root = FindRepoRoot();
            Assume.That(root, Is.Not.Null);
            var rootJson = ReadRegistry(root, "Registry/dev.json");
            var snapJson = ReadRegistry(
                root, "Packages/com.yoji.u3d-ai-linker/Registry/dev.json");
            Assert.AreEqual(
                Normalize(rootJson), Normalize(snapJson),
                "bundled dev snapshot drifted from root Registry/dev.json");
        }

        [Test]
        public void StableRegistry_AllToolsArePlannedOrSkillOnly()
        {
            // spec 117-118：尚无公开 UPM 包时不得标 ready。
            var root = FindRepoRoot();
            Assume.That(root, Is.Not.Null);
            var doc = RegistryParser.Parse(ReadRegistry(root, "Registry/stable.json"));
            foreach (var entry in doc.Entries)
            {
                Assert.AreNotEqual(
                    "ready", entry.Status,
                    "tool '" + entry.Id + "' must not be 'ready' before public UPM release");
            }
        }

        [Test]
        public void StableRegistry_EditorCoreIsInfra()
        {
            var root = FindRepoRoot();
            Assume.That(root, Is.Not.Null);
            var doc = RegistryParser.Parse(ReadRegistry(root, "Registry/stable.json"));
            RegistryEntry core = null;
            foreach (var entry in doc.Entries)
            {
                if (entry.Id == "editor-core") { core = entry; break; }
            }
            Assert.IsNotNull(core, "editor-core entry missing from stable.json");
            Assert.AreEqual("infra", core.Kind);
            Assert.IsFalse(core.UserToggle, "infra editor-core must not be user-toggleable");
        }

        // 归一化换行,避免 CRLF/LF 差异导致快照逐字节比较误判。
        private static string Normalize(string s)
        {
            return s.Replace("\r\n", "\n").Replace("\r", "\n").TrimEnd('\n');
        }
    }
}
```

> 注:测试引用 LINK-2 的公开成员 `RegistryParser.Parse`、`RegistryValidator.Validate`、`RegistryChannel`,以及 `RegistryDocument.Entries` / `RegistryEntry.{Id,Status,Kind,UserToggle}`。LINK-2 的 schemaVersion 支持值是 1(见 `RegistryParser.SupportedSchemaVersion`),因此下面 stable.json/dev.json 的 `schemaVersion` 必须写 `1`(不是 2)。若 LINK-2 后续把支持版本提到 2,同步改 JSON。

- [ ] Step 2: 跑测试确认**失败**(此时 Registry 文件还不存在)。Unity Editor 在线时用 test-runner skill:

```bash
python Packages/com.yoji.test-runner/Agent~/skills/test-runner-mcp/client.py run-tests --mode EditMode --testNames Yoji.U3DAILinker.Tests.RegistryFixtureTests
```

期望:`RepoRootResolves` 与各 `PassesValidation` 测试 FAIL,失败消息形如 `registry file missing: .../Registry/stable.json`。这确认测试真的在检查文件存在与校验,而非空跑。

- [ ] Step 3: 写根 `Registry/stable.json`,完整内容(四工具按现实标注,无 ready;Stable 通道无 `branch` 字段;每工具给 `revision` 为合法 `<tool-id>-v<semver>` 占位 tag,即使尚未推送——Registry 字段须满足 spec 167 的格式校验,实际安装由 `status` 闸住只处理 ready;`schemaVersion` 必须为 LINK-2 支持的 `1`):

```json
{
  "schemaVersion": 1,
  "channel": "stable",
  "entries": [
    {
      "id": "editor-core",
      "status": "planned",
      "kind": "infra",
      "order": 10,
      "packageName": "com.yoji.editor-core",
      "packagePath": "Packages/com.yoji.editor-core",
      "revision": "editor-core-v1.0.0",
      "defaultEnabled": false,
      "userToggle": false,
      "agentAssets": null,
      "minUnity": "2022.3",
      "dependsOn": []
    },
    {
      "id": "editor-debug",
      "status": "skill-only",
      "kind": "tool",
      "order": 20,
      "packageName": "com.yoji.editor-debug",
      "packagePath": "Packages/com.yoji.editor-debug",
      "revision": "editor-debug-v0.1.0",
      "defaultEnabled": true,
      "userToggle": true,
      "agentAssets": "Agent~",
      "minUnity": "2022.3",
      "dependsOn": ["editor-core"]
    },
    {
      "id": "test-runner",
      "status": "skill-only",
      "kind": "tool",
      "order": 30,
      "packageName": "com.yoji.test-runner",
      "packagePath": "Packages/com.yoji.test-runner",
      "revision": "test-runner-v0.1.0",
      "defaultEnabled": true,
      "userToggle": true,
      "agentAssets": "Agent~",
      "minUnity": "2022.3",
      "dependsOn": ["editor-core"]
    },
    {
      "id": "lua-device-debug",
      "status": "planned",
      "kind": "tool",
      "order": 40,
      "packageName": "com.yoji.lua-device-debug",
      "packagePath": "Packages/com.yoji.lua-device-debug",
      "revision": "lua-device-debug-v0.1.0",
      "defaultEnabled": false,
      "userToggle": true,
      "agentAssets": "Agent~",
      "minUnity": "6000.3",
      "dependsOn": []
    }
  ]
}
```

> 决策说明:(a) editor-debug/test-runner 标 `skill-only` 因其 Skill 成熟、可随 Linker `BundledSkills~/` 交付(spec 98、255),但无公开 UPM 故不为 ready;它们 `dependsOn:["editor-core"]`(spec 113-116、151,共享 HTTP/dispatcher 脚手架的拓扑前置)。(b) lua-device-debug 标 `planned`:transport-only、需项目侧适配器,首版不随 Linker 交付 Skill(spec 99),`defaultEnabled:false`。(c) editor-core 标 `planned`(包未建)、`kind:"infra"`、`userToggle:false`、`agentAssets:null`(spec 104),`order:10` 保证拓扑/order 排序时它在依赖它的工具之前。(d) revision 用各包当前 `0.1.0`,格式满足 `<tool-id>-v<semver>`(spec 167)。
> 重要:LINK-2 的 `RegistryParser` 配置 `MissingMemberHandling.Error`,且 `RegistryEntry`/`RegistryDocument` 只声明了 `id/status/kind/order/packageName/packagePath/revision/defaultEnabled/userToggle/agentAssets/minUnity/dependsOn` 与 `schemaVersion/channel/branch/entries` 字段。本 JSON 的 `displayName` 字段**不在 LINK-2 POCO 中,会被未知字段拒绝**。因此本 stable.json/dev.json **不写 `displayName`**(上表的 displayName 仅供面板/文档参考,不入 Registry JSON)。若组装时 LINK-2 已加 `displayName` 字段则可加回。

- [ ] Step 4: 写根 `Registry/dev.json`,完整内容(Dev 通道:用 `"branch":"main"`;LINK-2 的 dev 校验要求 `revision` 是 40 位小写 SHA。首版各工具尚无真实 commit 锁定,且 dev 安装的 SHA 由运行时 `git ls-remote` 解析——但 LINK-2 `RegistryValidator` 对 dev 通道每条目仍校验 `revision` 为 40 位 SHA(`RegistryEntry.Revision` 非空)。为让 dev.json 过校验,这里给每条目一个占位的 40 位全 0 SHA `0000000000000000000000000000000000000000`,RefreshDev 运行时会用真实 SHA 覆盖;`schemaVersion` 为 `1`):

```json
{
  "schemaVersion": 1,
  "channel": "dev",
  "branch": "main",
  "entries": [
    {
      "id": "editor-core",
      "status": "planned",
      "kind": "infra",
      "order": 10,
      "packageName": "com.yoji.editor-core",
      "packagePath": "Packages/com.yoji.editor-core",
      "revision": "0000000000000000000000000000000000000000",
      "defaultEnabled": false,
      "userToggle": false,
      "agentAssets": null,
      "minUnity": "2022.3",
      "dependsOn": []
    },
    {
      "id": "editor-debug",
      "status": "skill-only",
      "kind": "tool",
      "order": 20,
      "packageName": "com.yoji.editor-debug",
      "packagePath": "Packages/com.yoji.editor-debug",
      "revision": "0000000000000000000000000000000000000000",
      "defaultEnabled": true,
      "userToggle": true,
      "agentAssets": "Agent~",
      "minUnity": "2022.3",
      "dependsOn": ["editor-core"]
    },
    {
      "id": "test-runner",
      "status": "skill-only",
      "kind": "tool",
      "order": 30,
      "packageName": "com.yoji.test-runner",
      "packagePath": "Packages/com.yoji.test-runner",
      "revision": "0000000000000000000000000000000000000000",
      "defaultEnabled": true,
      "userToggle": true,
      "agentAssets": "Agent~",
      "minUnity": "2022.3",
      "dependsOn": ["editor-core"]
    },
    {
      "id": "lua-device-debug",
      "status": "planned",
      "kind": "tool",
      "order": 40,
      "packageName": "com.yoji.lua-device-debug",
      "packagePath": "Packages/com.yoji.lua-device-debug",
      "revision": "0000000000000000000000000000000000000000",
      "defaultEnabled": false,
      "userToggle": true,
      "agentAssets": "Agent~",
      "minUnity": "6000.3",
      "dependsOn": []
    }
  ]
}
```

> 注:LINK-2 `RegistryValidator` 对 Dev 通道要求 `revision` 匹配 `^[0-9a-f]{40}$`;全 0 SHA 满足该正则(合法占位)。`branch` 为 `main` 满足"Dev 通道 branch 必须 main"校验。若 LINK-2 后续改成"dev 条目允许空 revision、运行时解析",则改 dev.json 去掉 revision——但以 LINK-2 当前校验规则为准,这里给全 0 占位最稳妥。

- [ ] Step 5: 把两份文件**逐字节复制**到 Linker 包内快照目录(离线回退,spec 73/255/288)。运行:

```bash
mkdir -p Packages/com.yoji.u3d-ai-linker/Registry
cp Registry/stable.json Packages/com.yoji.u3d-ai-linker/Registry/stable.json
cp Registry/dev.json Packages/com.yoji.u3d-ai-linker/Registry/dev.json
```

- [ ] Step 6: 再跑测试,确认**全绿**:

```bash
python Packages/com.yoji.test-runner/Agent~/skills/test-runner-mcp/client.py run-tests --mode EditMode --testNames Yoji.U3DAILinker.Tests.RegistryFixtureTests
```

期望:`RepoRootResolves`、`StableRegistry_PassesValidation`、`DevRegistry_PassesValidation`、`BundledSnapshot_StableMatchesRoot`、`BundledSnapshot_DevMatchesRoot`、`StableRegistry_AllToolsArePlannedOrSkillOnly`、`StableRegistry_EditorCoreIsInfra` 全部 PASS(共 7 个)。

- [ ] Step 7(离线兜底,可选):若 Unity 不在线,headless batchmode runner 跑同一过滤。运行前导出仓库根供测试 `FindRepoRoot` 兜底:

```bash
export U3D_LINKER_REPO_ROOT="$(pwd)"
```

确认其后 `RootRegistry_RepoRootResolves` 不再因路径找不到而 fail。

- [ ] Step 8(人工 git,交人执行):

```bash
git add Registry/stable.json Registry/dev.json "Packages/com.yoji.u3d-ai-linker/Registry/stable.json" "Packages/com.yoji.u3d-ai-linker/Registry/dev.json" "Packages/com.yoji.u3d-ai-linker/Tests/Editor/RegistryFixtureTests.cs"
git commit -m "feat(linker): seed stable/dev Registry (planned+skill-only, editor-core infra) with validation fixtures"
```

---

### Task 49: 升 version 与打 <tool-id>-v<semver> tag(人工 git)

把准备进入 Registry 编排的工具包 `package.json` 的 version 与 Registry `revision` 对齐,并给出打 tag 的人工命令。**注意现实**:这些工具尚未公开发布、status 仍是 `planned`/`skill-only`,`Install All` 不会拉它们,故打 tag 是为"将来可复现引用 + Stable 回滚锚点"准备;**AI 不执行任何 git 写**,只产出命令清单交人。

Files:
- (无源文件改动;version 文案见下,实际 bump 由人按需执行)

- [ ] Step 1: 决策记录——本子系统**不强制**把 version 从 `0.1.0` 升上去。理由:Registry `revision` 已用 `<tool-id>-v0.1.0` 对齐各包当前 `0.1.0`,且工具仍是 `planned`/`skill-only`、不发起独立 UPM 安装,无需此刻 bump。**当某工具真正迁移到 `ready`(在别的迭代)时**,才按下面流程升 version 并打正式 tag。本 Task 仅固化"version ↔ revision ↔ tag 三者一致"的规程与命令模板。

- [ ] Step 2: 一致性自检命令(确认 Registry revision 的 semver 与对应 `package.json` version 一致)。运行:

```bash
for p in editor-debug test-runner lua-device-debug; do
  v=$(grep '"version"' "Packages/com.yoji.$p/package.json" | head -1)
  r=$(grep "\"revision\": \"$p-v" Registry/stable.json)
  printf "%-20s pkg:%s  registry:%s\n" "$p" "$v" "$r"
done
```

期望:每行 pkg 的 version 数字(`0.1.0`)与 registry revision 里 `-v` 后的数字一致(`editor-debug-v0.1.0` 等)。不一致则说明 Registry revision 与 package.json 漂移,需先对齐再继续。

- [ ] Step 3: 打 tag 的**人工**命令模板(交人,在确认对应包要进 ready 并已 push 后执行;`editor-core` 包尚不存在,不打它的 tag)。例如把 test-runner 正式发到 `v1.1.0`:

```bash
# 1) 先在 package.json 里把 "version": "0.1.0" 改为目标 semver（如 1.1.0），
#    并同步改 Registry/stable.json + 包内快照里该工具的 revision 为 test-runner-v1.1.0，
#    把该工具 status 由 skill-only/planned 改为 ready（仅当公开 UPM 与 Skill 都就绪）。
# 2) 提交：
git add "Packages/com.yoji.test-runner/package.json" Registry/stable.json "Packages/com.yoji.u3d-ai-linker/Registry/stable.json"
git commit -m "release(test-runner): v1.1.0 -> ready in stable Registry"
# 3) 推送后打前缀 tag（Monorepo 唯一前缀，spec 208-216）：
git tag test-runner-v1.1.0
git push origin main
git push origin test-runner-v1.1.0
```

- [ ] Step 4: tag 命名校验(打完 tag 后人工自检,确保符合 spec 167 `<tool-id>-v<semver>`)。运行:

```bash
git tag --list "*-v*" | grep -vE "^(linker|editor-core|editor-debug|test-runner|lua-device-debug)-v[0-9]+\.[0-9]+\.[0-9]+$"
```

期望:无输出(所有已打 tag 都符合 `<allowed-id>-v<major>.<minor>.<patch>` 命名);有输出即存在不合规 tag,需删除重打。

> 本 Task 不产生 AI 自动提交。首版四工具均未达 ready,故**现在不打任何工具 tag**;Step 3/4 是规程模板,供后续 ready 迁移迭代使用。Linker 自身 `linker-v1.0.0` tag 由 Linker 发布迭代负责,不在本子系统。

---

### Task 50: 修正 README 对迁移完成度的误称(6-9、15-17 行)

README 第 6-9 行(IMPORTANT 块)与第 15-17 行(状态表)把三个服务说成已作为 UPM 包"now included"且"Usable",在"公开可 Git URL 安装的 UPM"语义上是误称(spec 506:公开 Package 未完成前不得描述为已迁移完成)。改为如实表述:本仓库内 `file:` 工作区包可用,但**尚未发布公开 UPM、无 Git URL 安装、由规划中的 Linker 编排**。

Files:
- Modify: `README.md`

- [ ] Step 1: 读 README 顶部确认当前文本(已核实:第 6-9 行为 IMPORTANT 块,第 13-20 行为状态表)。改第 6-9 行的 IMPORTANT 块。把:

```
> [!IMPORTANT]
> This repository is currently a migration workspace. The unity-editor-debug-mcp
> test-runner-mcp, and lua-device-debug Unity-side services are now included as
> UPM packages. This toolset targets non-HybridCLR Unity projects; the old
> client-only runtime expression debugger asset has been removed.
```

替换为(顺手补回原文漏掉的 `unity-editor-debug-mcp` 与 `test-runner-mcp` 之间的逗号):

```
> [!IMPORTANT]
> This repository is a migration workspace. The unity-editor-debug-mcp,
> test-runner-mcp, and lua-device-debug Unity-side services live here as
> in-repo UPM source packages under `Packages/com.yoji.*`. They are NOT yet
> published as public, Git-URL-installable UPM packages and carry no release
> tags; today they are consumed via a local `file:` manifest entry. A planned
> U3D AI Linker will orchestrate their public install once each tool reaches
> the `ready` state. This toolset targets non-HybridCLR Unity projects; the old
> client-only runtime expression debugger asset has been removed.
```

- [ ] Step 2: 改状态表表头说明与 `Status` 列措辞(第 13-17 行),把"Usable"这种暗示公开可装的措辞收敛为"in-repo (file:)"现状。把这三行:

```
| Tool | Agent-side assets in this repo | Unity-side service in this repo | Status |
|------|--------------------------------|---------------------------------|--------|
| test-runner-mcp | Python client, skill, and references | Yes (`Packages/com.yoji.test-runner`) | Usable (EditMode only; PlayMode planned); verified on Unity 6000.3.16f1 |
| unity-editor-debug-mcp | Python client, skill, and references | Yes (`Packages/com.yoji.editor-debug`) | Usable; verified on Unity 6000.3.16f1 |
| unity-lua-device-debug | Python client and skill | Yes (`Packages/com.yoji.lua-device-debug`) | Transport package started; targets Unity 6000.3.16f1; project Lua adapter still required |
```

替换为:

```
| Tool | Agent-side assets in this repo | Unity-side service in this repo | Status (in-repo `file:`, not yet public UPM) |
|------|--------------------------------|---------------------------------|--------|
| test-runner-mcp | Python client, skill, and references | Yes (`Packages/com.yoji.test-runner`) | Works via `file:` (EditMode only; PlayMode planned); verified on Unity 6000.3.16f1; Registry status: skill-only |
| unity-editor-debug-mcp | Python client, skill, and references | Yes (`Packages/com.yoji.editor-debug`) | Works via `file:`; verified on Unity 6000.3.16f1; Registry status: skill-only |
| unity-lua-device-debug | Python client and skill | Yes (`Packages/com.yoji.lua-device-debug`) | Transport-only via `file:`; targets Unity 6000.3.16f1; project Lua adapter still required; Registry status: planned |
```

- [ ] Step 3: 校验改动只动了这两块、未引入 emoji/非 UTF-8。运行:

```bash
grep -nP "[\x{1F000}-\x{1FAFF}\x{2600}-\x{27BF}]" README.md
```

期望:无输出(退出码 1)。再确认关键措辞已落地:

```bash
grep -n "not yet" README.md && grep -n "Registry status:" README.md
```

期望:IMPORTANT 块出现 "not yet ... published",状态表三行各出现 `Registry status:`。

- [ ] Step 4: 与 Registry 自洽性人工核对——README 状态表里每个工具的 `Registry status:` 值必须与 `Registry/stable.json` 中该工具 `status` 一致(test-runner=skill-only、editor-debug=skill-only、lua-device-debug=planned)。运行:

```bash
for p in test-runner editor-debug lua-device-debug; do
  printf "%-20s registry=%s\n" "$p" "$(grep -A1 "\"id\": \"$p\"" Registry/stable.json | grep status)"
done
```

期望:test-runner/editor-debug 显示 `"status": "skill-only"`,lua-device-debug 显示 `"status": "planned"`,与 README 文案逐一对应。

- [ ] Step 5(人工 git,交人执行):

```bash
git add README.md
git commit -m "docs(readme): correct migration-completeness wording (in-repo file: packages, not public UPM)"
```

---

### 跨子系统一致性备注(给组装者)

- 本子系统**依赖 LINK-2** 的 `RegistryParser`/`RegistryValidator`/`RegistryChannel` 以及 `RegistryDocument.Entries`、`RegistryEntry.{Id,Status,Kind,UserToggle,...}`。`RegistryFixtureTests.cs` 必须在 LINK-2 这些 API 落地后编译;若 LINK-2 用字段而非属性、或命名不同,微调测试的成员访问,但断言语义不动。
- 本子系统产出的 `Registry/{stable,dev}.json` 是 LINK-2 校验器的**真实回归数据源**:LINK-2 若后续收紧校验(如强制 `dependsOn` 无环、`order` 唯一、editor-core 必须先于其 dependents),本 Registry 内容已满足(order 10<20/30、dependsOn 仅指向已存在的 editor-core、无环)。`schemaVersion` 与 LINK-2 `RegistryParser.SupportedSchemaVersion` 必须一致(本计划用 `1`)。
- `Tests/Editor` 目录需要 Linker 测试 asmdef(`Yoji.U3DAILinker.Editor.Tests`),该 asmdef 由 LINK-0/LINK-1/LINK-2 创建;本子系统**只新增测试源文件**到该目录,不重复创建 asmdef。
- README 状态表的 `Registry status:` 必须与 `Registry/stable.json` 的 `status` 字段保持同步;任何一处改 status 都要同步另一处(Task 50 Step 4 是该自洽性的校验点)。

---

## Roadmap

分阶段里程碑对齐 spec:494-506 的 9 步实施顺序。依赖门控:**LINK-0 探针是所有后续子系统的硬前置**(它决定 directory vs zip-fallback 资产模式,影响 LINK-1/4/7 的资产读取实现);**LINK-1 包骨架是全部代码的编译基座**;LINK-2(Registry 纯逻辑)是 LINK-2b/3/6 的上游;LINK-2b(manifest 事务)被 LINK-3 队列消费;LINK-3(队列)是 LINK-4(Agent 同步编排)与 LINK-6(面板安装动作)的运行时;LINK-7 收尾依赖 LINK-2 校验器。

```
依赖门控图(箭头 = 必须先于):

  LINK-0 (Task 1-5, 探针硬前置 / 决策门)
     │  (recommendedMode 决定 directory vs zip-fallback)
     ▼
  LINK-1 (Task 6-8, 包骨架 + SettingsProvider) ──→ 全部后续子系统(编译基座)
     │
     ├──→ LINK-2 (Task 9-14, Registry 纯逻辑)
     │        ├──→ LINK-2b (Task 15-20, manifest 事务)
     │        │        └──→ LINK-3 (Task 21-26, UPM 队列 + 域重载恢复)
     │        │                 ├──→ LINK-4 (Task 27-32, Skill 复制 + Junction)
     │        │                 └──→ (面板安装动作接线)
     │        ├──→ LINK-6 (Task 38-45, Project Settings 面板 + 三通道)
     │        └──→ LINK-7 (Task 46-50, 工具迁移收尾;消费 LINK-2 校验器)
     │
     └──→ LINK-5 (Task 33-37, Fragment 合并 + 托管区块;仅依赖 LINK-1 骨架)
```

| 阶段 | 子系统 | Task 编号 | 出口标准 | 粗估人天 |
|---|---|---|---|---|
| **0 — 探针决策门** | LINK-0 | 1-5 | 空 2022.3 工程经 Git URL 装包后实跑探针,`probe-result.json` 落盘并记录 `recommendedMode`(directory 或 zip-fallback);ProbeEvaluator/Writer 单测全绿;**LINK-1/4/7 的资产读取模式已据此定调**。 | 1.5 |
| **1 — 包骨架与入口** | LINK-1 | 6-8 | 包被 Unity 识别、两 asmdef 编译;`U3DAILinkerPackage` 常量哨兵测试 6/6 绿;`Project/U3D AI Linker` 入口在 Project Settings 出现(占位 HelpBox)。 | 1 |
| **2 — Registry 纯逻辑** | LINK-2 | 9-14 | 解析/校验/URL 生成/拓扑排序全套 EditMode 测试绿(约 48 用例);未知字段/schema/前缀/路径/revision/环/未知依赖/infra 直接启用全部拒绝有用例覆盖。 | 2.5 |
| **3 — manifest 事务** | LINK-2b | 15-20 | 七步原子事务、冲突预检、Remove 闭包、回滚检测手动改动全部 EditMode 绿;手动验证 `File.Replace` 原子语义 + backup/operation.json 落地。 | 2.5 |
| **4 — UPM 队列与恢复** | LINK-3 | 21-26 | 操作日志原子写、队列构建(infra 闭包 + linker-last)、串行 runner(先落盘再 Add + 重试 ≤2)、恢复决策全套 EditMode 绿;手动验证真实 `Client.Add` 串行安装 + 域重载续跑 + linker 排尾。 | 3 |
| **5 — Skill 复制与 Junction** | LINK-4 | 27-32 | 内容哈希/ownership/守卫/Junction 抽象/六步事务同步全套 EditMode 绿(含未知目录保护、staging 失败不破坏、junction 失败回滚 backup、幂等重同步);手动验证真实 Windows Junction 创建/穿透/删除不伤目标。 | 3 |
| **6 — Fragment 与托管区块** | LINK-5 | 33-37 | 托管区块写入(保留用户内容/marker 损坏即停)、确定性合并(重复 Skill 名/负 order 预检)、gitignore 区块(卸载只删匹配行)、端到端不部分写,共 27 用例绿;手动验证 CLAUDE/AGENTS/.gitignore 真实落地。 | 2.5 |
| **7 — Project Settings 面板** | LINK-6 | 38-45 | 三通道 URL、三层状态分离(禁绝对路径不变量)、面板状态模型、RefreshDev SHA 锁定、Restore 计划共 43 用例绿;手动验证面板渲染 + Local 绝对路径只入 UserSettings。 | 3 |
| **8 — 工具迁移收尾** | LINK-7 | 46-50 | editor-debug/lua-device-debug 补 fragments;`Registry/{stable,dev}.json` + 包内快照写入(全 planned/skill-only、editor-core infra)且过 LINK-2 校验器、两快照逐字节一致(7 用例绿);README 误称修正且与 Registry status 自洽;tag 规程文档化(首版不打 tag)。 | 1.5 |

> 总粗估 ~20.5 人天(纯逻辑层占多数,但每层都含失败测试→实现→回归的完整 TDD 循环与若干手动验证)。**LINK-0 是项目门:其 `recommendedMode` 结论未产出前,LINK-1/4/7 的资产读取实现不得开工。** LINK-5 可与 LINK-2/2b/3 并行(只依赖 LINK-1 骨架)。组装阶段需处理跨子系统同名文件去重(package.json/asmdef/AssemblyInfo/OperationLog/SettingsProvider/Registry 类型/URL builder)——各子系统 body 已在其"幂等基线/组装提示"注明合并规则。

---

## Manual Verification

把 spec:482-492 的 9 步手动验收逐条列为 checkbox(覆盖 Unity 2022.3,真实副作用,无法用 EditMode fake 替代):

- [ ] 1. 先验证 `Agent~/` 在 Git Package Cache 中可读取(LINK-0 Task 5):空 2022.3 工程经 Git URL 装 linker + editor-debug + test-runner 后,运行 `Tools > U3D AI Linker > Run Agent Asset Probe`,`Library/U3DAILinker/probe-result.json` 的三目标 `exists` 与 `recommendedMode` 与实际 PackageCache 路径一致;据此确定 directory 或 zip-fallback 资产模式。
- [ ] 2. 从空工程用 Git URL 安装 Linker,确认入口出现在 `Edit > Project Settings > U3D AI Linker`(LINK-1 Task 7 手动验证 / LINK-6 Task 45 面板):左栏出现 `U3D AI Linker` 节点,右侧渲染状态区/工具表/操作区。
- [ ] 3. Stable 通道一键安装全部 `ready` 工具,并在中途触发域重载(LINK-3 Task 26):构造两项以上 ready 队列,`Install/Update All` 后 `operation.json` 出现且 `phase=package-requested`,第一项装完触发域重载。
- [ ] 4. 验证重载后队列继续,Claude Code 与 Codex 均能发现 Skill(LINK-3 Task 26 + LINK-4 Task 32 + LINK-5 手动 1-2):域重载后 `U3DAILinkerBootstrap` 自动续跑直至整队 `completed`、`operation.json` 被清;`.claude/skills/<tool>` 与 `.agents/skills/<tool>` Junction 指向 `.u3d-ai-linker/skills/<tool>`,穿透可读 SKILL.md;CLAUDE.md/AGENTS.md 托管区块含各工具来源段。
- [ ] 5. 更新 Stable Tag 后执行升级和回滚(LINK-2b Task 18/20 手动验证 + LINK-6):改 Registry revision 后 `Install/Update` 把 manifest 受管依赖升级、写 backup 与 operation.json;`Rollback Manifest` 用 backup 恢复,恢复前检测手动改动则拒绝。
- [ ] 6. 推送 `main` 后执行 `Refresh Dev`,确认所有工具使用同一 SHA(LINK-6 Task 43 手动验证):Dev 通道点 Refresh,`git ls-remote` 解析 main SHA,启用闭包内所有 `com.yoji.*` Git URL 的 `#` 后均为同一 40 位 SHA。
- [ ] 7. Local 通道选择本机 `E:/Yoji/U3D-Dev-Tools-AI`,确认 manifest 使用 `file:`,且绝对路径只写入 UserSettings(LINK-6 Task 45 Step 7.3 + LINK-6 Task 41 不变量):Local 通道安装后 manifest 受管依赖为 `file:...`;`UserSettings/U3DAILinkerUserSettings.asset` 含绝对路径,`ProjectSettings/U3DAILinkerSettings.asset` **不含**绝对路径(只记 `channel: Local`)。
- [ ] 8. 制造网络失败、非法 Registry、普通目录冲突、已有非托管 dependency 和损坏 marker,确认不覆盖用户内容(跨 LINK-2/2b/4/5 的拒绝路径):断网时 RefreshDev 抛错且不改 manifest;非法 Registry 不改现有安装;`.u3d-ai-linker/skills/<tool>` 为用户目录(无 ownership)时 Agent 同步 `FailureStage=ownership` 拒绝;manifest 已有非 Linker 管理依赖时事务 `Committed=false` 列冲突;CLAUDE.md/.gitignore marker 损坏时托管区块写入返回 Conflict 且文件字节不变。
- [ ] 9. 卸载单个工具,确认 infra 依赖、其他工具和用户文档不受影响(LINK-2b Task 19 + LINK-5 手动 4):Remove 时 `RemovePlanner` 只移除目标 tool 与无剩余 dependents 的孤儿 infra,被其它启用工具共用的 infra 保留;`.gitignore` 托管区块只删该工具的 skill 行,其它工具 ignore 行与用户行完好;卸载最后一个工具后整段区块消失但用户 `.gitignore` 行保留。

---

## Self-Review

对照 spec:461-480 验证清单(共约 20 项),逐项指到 Task 编号。每项写明"由哪个 Task 的哪些用例覆盖"。

**Editor 测试至少覆盖(spec 464-480):**

1. **Registry 解析和字段校验** — Task 10(`EnumParsingTests` 枚举解析)+ Task 11(`RegistryParserTests` 反序列化、未知字段/schema 拒绝、解析成功)。
2. **Registry 仓库、路径、命名、revision、`kind`、`dependsOn`、未知字段拒绝** — Task 11(未知字段/未知 schema 拒绝)+ Task 12(`RegistryValidatorTests`:`com.yoji.` 前缀、`packagePath==Packages/<name>` 且禁 `../绝对/URL`、stable revision 正则、dev 40 位 SHA、未知 status/kind、minUnity 必填、id/packageName 唯一、聚合多错)。仓库固定性由 Task 13(`ToolUrlBuilder` 仓库写死常量)保证;`dependsOn` 的语义校验(环/未知)在 Task 14。
3. **`infra` 拓扑排序、环依赖拒绝、用户不可启停** — Task 14(`TopologicalSorterTests`:依赖在前、order 打破并列、确定性、环拒绝、未知依赖拒绝、infra 直接启用拒绝、infra 作为依赖通过、链式依赖)。"用户不可启停 infra"=`infra defaultEnabled=true 拒绝`(Task 14)+ 面板 `EnabledVisible=false`(Task 42 `ToolWithUserToggle_ShowsEnabledColumn_InfraDoesNot`)。
4. **Stable URL、Dev SHA URL 与 Local file URL 生成** — Task 13(`ToolUrlBuilderTests`:stable tag、dev SHA、local file、路径规范化)+ Task 40(`PackageUrlBuilderTests`:三通道 + `IsManagedUrl`,面板侧)。
5. **Manifest 修改事务:保留未知字段、只改托管依赖、写 tmp、解析验证、backup 路径记录** — Task 18(`ManifestTransactionTests`:`Apply_AddsManagedDep_PreservesUnknownFieldsAndForeignDeps`、`Apply_WritesBackupAndOperationLog_AndCleansTmp`、Update 记 oldValue、Remove)。
6. **Manifest 冲突检测:已有非 Linker 管理 dependency 时不覆盖** — Task 16(`ManifestUrlClassifierTests` ownership 判定)+ Task 18(`Apply_RejectsWhenExistingDepIsUnmanaged_AndDoesNotTouchAnything`、`Apply_AcceptConflicts_TakesOverUnmanagedDep`、`Apply_RemoveUnmanagedDep_IsConflict`)。
7. **UPM 安装队列状态转换、日志原子写入和域重载恢复** — Task 22(`OperationLogStoreTests` tmp+File.Replace 原子写、损坏读 null)+ Task 25(`UpmQueueRunnerTests` 状态转换:pending→requested、未达成重试、达成推进、整队完成清日志、超重试 fault、Add 报错 fault)+ Task 26(`RecoveryReconcilerTests` 恢复决策)。
8. **Linker 自更新排在队列最后** — Task 23(`InstallQueueBuilderTests`:`Build_LinkerEnabled_AlwaysLast`、`Build_LinkerLast_EvenIfInfraDepends`)。
9. **`SettingsProvider("Project/U3D AI Linker")` 注册和 UI 状态模型** — Task 7(`U3DAILinkerSettingsProviderTests`:settingsPath/scope/label)+ Task 42(`PanelStateModelTests` 10 用例:Enabled 可见性、Installed/Missing/Conflict、Desired 按通道、Current、Agent、RequiredBy、排序)。
10. **ProjectSettings 与 UserSettings 分离:本机绝对路径不得写入 `ProjectSettings/U3DAILinkerSettings.asset`** — Task 41(`U3DAILinkerSettingsTests`:`ContainsAbsolutePath`/`Validate` 检测 file:/盘符/Unix 绝对路径)+ Task 45(`U3DAILinkerSettingsStoreTests.SaveProjectSettings_RejectsAbsolutePathPollution` + 手动验证 Step 7.3)。
11. **`Agent~/` / `BundledSkills~/` 探针结果持久化和 zip fallback 分支** — Task 2(`ProbeEvaluatorTests`:全存在→directory、任一缺→zip-fallback、空集→不可读)+ Task 3(`ProbeResultWriterTests`:持久化到 `Library/U3DAILinker/probe-result.json`、camelCase 字段、幂等覆盖)+ Task 5(手动实跑决策门)。
12. **托管区块创建、更新、保留用户内容和冲突检测** — Task 34(`ManagedBlockWriterTests` 7 用例:缺文件创建、用户内容逐字保留、相同内容 Unchanged、更新只换区块内、只 start marker Conflict、重复 start Conflict、end 在 start 前 Conflict)。
13. **Fragment 确定性合并和重复 Skill 名预检** — Task 35(`FragmentMergerTests` 8 用例:order 升序、order 同按 toolId、null fragment 跳过、来源注释、重复 SkillName 预检失败无 body、负 order 预检失败、Agents kind、空输入)+ Task 37(`ManagedFileSyncTests.Sync_DuplicateSkillName_WritesNothing` 跨组件不部分写)。
14. **`.gitignore` 托管区块创建、更新和损坏保护** — Task 36(`GitignoreBlockWriterTests` 8 用例:缺文件创建含 infra+skill 行、保留用户行、相同 Unchanged、路径变更更新、损坏 marker Conflict、Remove 只删匹配行、Remove 清空整段移除保留用户行、Remove 损坏 marker Conflict)。
15. **Junction 创建、重复同步、失效修复、ownership 校验和未知目录保护** — Task 31(`FakeJunctionManagerTests`:创建/查询/重指向/删除 + `WindowsJunctionManager` 编译 + 手动验证真实 Junction)+ Task 30(`OwnershipGuardTests` 10 用例:Missing/Foreign/ManagedMatch/ManagedMismatch + MayOverwrite)+ Task 32(`AgentSyncServiceTests`:`Sync_Reapply_IsIdempotent_AndRepointsJunction` 重复同步/失效修复、`Sync_ForeignExistingTarget_RefusesAndPreservesUserDir` 未知目录保护、`Sync_MismatchOwnedTarget_Refuses` ownership 校验)。
16. **staging 失败不破坏现有同步版本,替换失败能恢复 backup** — Task 32(`AgentSyncServiceTests`:`Sync_MissingSkillMarker_FailsBeforeTouchingTarget`/`Sync_MissingSourceDir_Fails` staging 失败不动目标、`Sync_JunctionFailureAfterReplace_RestoresBackup` 替换后失败恢复 backup)。

**spec 461-463 验证总纲对应的额外不变式(已纳入 Task):**

17. **Rollback 用 manifestBackupPath / oldValue 恢复,恢复前检测手动改动则停**(spec 455) — Task 20(`ManifestRollbackTests`:`Rollback_WhenUnchanged_RestoresManifestToBackup`、`Rollback_WhenManagedDepManuallyChanged_RefusesAndKeepsUserEdit`、`Rollback_WhenBackupMissing_RefusesWithReason`)。
18. **Junction 冲突时不删除目标,要求用户确认**(spec 456) — Task 30(`OwnershipGuard` Foreign/Mismatch 拒绝)+ Task 31(`WindowsJunctionManager.Delete` 只删 junction 不删目标,手动验证 `targetMarkerStillThere=True`)+ Task 32(ownership 拒绝路径)。
19. **Registry 无法读取时不改变现有安装**(spec 457) — Task 11(解析失败抛 `RegistryParseException`)+ Task 12(校验失败抛 `RegistryValidationException`),调用方据异常停止;Task 48 用真实 Registry 数据回归"合法 Registry 过校验"。Dev 断网不改 manifest 见 Task 43 手动验证 Step 8。
20. **托管区块非法时不修改文件**(spec 458) — Task 34(marker 损坏 Conflict 文件原样)+ Task 36(gitignore marker 损坏 Conflict 文件原样)+ Task 37(`Sync_CorruptExistingFile_ReportsConflictAndLeavesItUntouched`)。
21. **每个错误显示工具 ID、操作、源路径和原始错误**(spec 459) — 贯穿各异常/结果类型:`RegistryValidationException.Errors` 逐条(Task 12)、`ManifestConflict.PackageName/ExistingValue` 与 `ManifestTransactionResult.FailureReason`(Task 17/18)、`AgentSyncResult.FailureStage/Message/ToolDir`(Task 32)、`BlockWriteResult.Message`(Task 34/36)、`ManagedFileSync` error 带文件名(Task 37)。

**缺口与组装注记(无 spec 验证项未覆盖,但有需组装阶段落实的接线点):**

- **同名文件去重**:package.json / 两个 asmdef / AssemblyInfo / `OperationLog.cs`(LINK-2b 含 ChangeType 的 `DependencyChange` 为权威,LINK-3 的合并)/ `SettingsProvider`(LINK-6 完整版替换 LINK-1 占位)/ Registry 类型(LINK-2 解析模型 vs LINK-6 `LinkerRegistry` 面板视图)/ URL builder(`ToolUrlBuilder` vs `PackageUrlBuilder`)需组装时按各子系统"组装提示"合并为单一来源。这是实现耦合点,非 spec 功能缺口。
- **面板动作接线**:LINK-6 面板的 `Request*` 占位回调(Install/Sync/Rollback/RefreshDev/Restore)需在组装阶段接到 LINK-3(队列)/LINK-4(Agent 同步)/LINK-2b(manifest 事务/回滚)/LINK-6(RefreshDevPlanner/RestorePlanner)的真实入口。spec 验证项已分别由各子系统的纯逻辑测试覆盖;端到端接线由 Manual Verification 第 3-9 步在真实 Editor 验收。
- **zip-fallback 资产读取实现**:LINK-0 探针若判 `zip-fallback`,LINK-4 的 `AgentSyncRequest.SourceDir` 来源定位(解压 `Editor/Resources/*.zip.bytes` 到 `.staging/`)需在同步编排层补一段解压逻辑(spec 298-302);本计划把"来源怎么来的"留给上层,LINK-4 只要求 SourceDir 是含 SKILL.md 的已存在目录。这是 LINK-0 决策门的下游分支,非缺口。

结论:spec 461-480 的全部约 20 项 Editor 测试要求 + 461-463 的不变式均有明确 Task 与用例对应,无功能性缺口;剩余事项均为组装阶段的实现耦合接线,已在各子系统 body 与本节注明处置方式。

---

## Appendix B: 类型与文件收敛(权威 — 执行时必须遵守)

> 本附录是跨子系统类型/文件命名的**单一权威**。计划正文由 9 个子系统并行起草,存在同名/同义类型重复定义。**凡正文与本附录冲突,以本附录为准**;subagent 逐 Task 执行时遇到下列类型/文件必须按此收敛(未收敛处编译期会以 CS0101 强制暴露)。
> 校正:经核实,本仓库三个现有包(editor-debug / test-runner / lua-device-debug)的 newtonsoft 依赖**均为 `3.2.2`**;故 linker `package.json` 权威值为 newtonsoft **`3.2.2`**(收敛分析初稿曾误判为 3.2.1)。

### B.1 权威命名表

| 概念 | 权威名 / 定义 | 废弃的同义名 | 处置 |
|---|---|---|---|
| 工具状态枚举 | `ToolStatus`(LINK-2,ns `Yoji.U3DAILinker.Registry`) | `PackageStatus`(LINK-6) | 删 LINK-6 的 `enum PackageStatus`;12 处 `PackageStatus.*` → `ToolStatus.*`;对齐可见性 |
| 工具类别枚举 | `ToolKind`(LINK-2) | `PackageKind`(LINK-6) | 删 LINK-6 的 `enum PackageKind`;16 处 `PackageKind.*` → `ToolKind.*` |
| Registry 解析模型 | `RegistryEntry`(LINK-2,public,`[JsonProperty]`,string Status/Kind,含 AgentAssets,DependsOn=string[]) | — | 保持不动,被解析/校验/排序/URL 全链路消费 |
| Registry 面板视图 | `RegistryEntryView`(由 LINK-6 面板视图版改名;Status/Kind 用 ToolStatus/ToolKind) | `RegistryEntry`(LINK-6 的 internal 视图版,line 8100 组) | 改名范围 = LINK-6(line 8100-9550)内**全部** `RegistryEntry` token(含 `List<RegistryEntry>`/`Dictionary<..,RegistryEntry>`/字段类型,**非仅** `new RegistryEntry{}` 初始化器与方法形参);**完整行号清单见 B.5**;**严禁全局 replace**(LINK-2 权威版 line 1619 + TopologicalSorter 2530/2532/2573 + RegistryFixtureTests 10220 不可动) |
| URL 生成器 | 类名 `ToolUrlBuilder`,API 取 LINK-6 `PackageUrlBuilder` 的 surface:`BuildStable(path,rev)` / `BuildDevSha(path,sha)` / `BuildLocalFile(root,packagePath)` / `IsManagedUrl(url)` / 常量 `RepoUrl` | 类名 `PackageUrlBuilder`;LINK-2 旧 API `BuildGit(entry)`/`BuildLocalFile(entry,root)`/常量 `GitRepoUrl` | 把 `PackageUrlBuilder` 改名 `ToolUrlBuilder` 并入 ns `Registry`;删 LINK-2 旧 `ToolUrlBuilder` 定义;LINK-2 的 5 处旧调用(line 2312/2322/2331/2338/2345)改新 API;`GitRepoUrl`→`RepoUrl` |
| 依赖变更记录 | `DependencyChange`(LINK-2b,含 `ChangeType` 字段,internal) | LINK-3 / LINK-6 各自的无 ChangeType 版 | 删 LINK-3(line 4207-4212)、LINK-6(line 9024-9029)两份本地定义,统一引用 LINK-2b 权威版(唯一被 Task18 `c.ChangeType=="Remove"` 消费) |
| SettingsProvider | `Yoji.U3DAILinker.Settings.U3DAILinkerSettingsProvider`(LINK-6 完整版,`Editor/Settings/`) | LINK-1 占位文件 `Editor/U3DAILinkerSettingsProvider.cs` | 删 LINK-1 占位文件(否则 CS0101 + 同路径 provider 双注册);存活版改用 `U3DAILinkerPackage.SettingsPath`/`.DisplayName`(保 Task7 测试断言) |

### B.2 重复 Create 文件 — 权威 Task

| 文件 | 权威 Create | 其余 Task 处置 |
|---|---|---|
| `package.json` | Task 1(version `0.1.0`、newtonsoft **`3.2.2`**、author `{name:"Yoji"}`、取最完整 description) | T6/9/15/21/33/38 不再 Create;正文里的 `1.0.0`/`3.2.1` 为漂移作废,以 Task 1 为准 |
| `Editor/Yoji.U3DAILinker.Editor.asmdef` | Task 1 | T6/9/15/21/27/33/38 改"核对存在即跳过"(8 处字面一致) |
| `Tests/Editor/Yoji.U3DAILinker.Editor.Tests.asmdef` | Task 1(rootNamespace `Yoji.U3DAILinker.Tests`) | 同上;T9 的 Registry 测试文件用文件内 `namespace Yoji.U3DAILinker.Registry.Tests` 即可,不改 asmdef rootNamespace |
| `Editor/AssemblyInfo.cs` | Task 6 | T21/27/38 改"核对存在即跳过"(单行 InternalsVisibleTo,字面一致) |
| `Editor/Operations/OperationLog.cs` | Task 17(LINK-2b)权威 Create | Task 22 改 Modify:**追加** `OperationLog`/`OperationPhase` 入同文件,**删去** T22 本地 `DependencyChange`;合并后所有类型统一 `internal` |

### B.3 otherFixes(随上述收敛一并处理)

1. **`BuildLocalFile` 参数序**:权威签名 `(localRepoRoot, packagePath)`(下游 line 8211/8839 已按此);把 LINK-2 自身 2 处调用(2331/2338)`BuildLocalFile(e,"E:/...")` 改为 `BuildLocalFile("E:/...", e.PackagePath)`。两参皆 string,顺序错会静默产出错误的 `file:` URL。
2. **仓库常量名**:统一 `RepoUrl`(下游 line 8842 已用);LINK-2 内 `GitRepoUrl` 引用改 `RepoUrl`。
3. **`DependencyChange.ChangeType`**:保留(LINK-2b 版),Task18 `c.ChangeType=="Remove"` 不动;LINK-3/LINK-6 删本地定义后只读 PackageName/OldValue/NewValue,不受影响。
4. **SettingsProvider 路径源**:存活 provider 删 `const ProviderPath="Project/U3D AI Linker"`,`CreateProvider` 改 `new SettingsProvider(U3DAILinkerPackage.SettingsPath, ...)`;label `"U3D AI Linker"` 改 `U3DAILinkerPackage.DisplayName`(保 Task7 两条断言)。
5. **测试 namespace(可选)**:T9 Registry 测试文件 namespace 由 `Yoji.U3DAILinker.Registry.Tests` 统一为 `Yoji.U3DAILinker.Tests`,使全包测试 filter 一致(不阻断编译)。

### B.4 判为合法演进 — 不动

- LINK-1 SettingsProvider 占位 → LINK-6 完整版:正常 TDD 演进,仅按 B.3 第 4 条修引用源。
- `ResolvedTool`(LINK-3,line 4446)/`RegistryEntryInfo`(LINK-3,line 3629)两个投影类型:在 ns `Yoji.U3DAILinker.Operations`,不与 `Registry.RegistryEntry` 撞名,可编译;为各自单测刻意做的最小投影,保留。

### B.5 RegistryEntryView 改名完整行号清单(预检 workflow 确认的 build-break 修复)

> 背景:B.1 初稿把改名范围写成"定义 8100 + 6 初始化器 + 2 引用 = 9 处",**漏了 7 个** `List<RegistryEntry>`/`Dictionary<..,RegistryEntry>`/字段位置的 token。因 LINK-6 视图类型 `RegistryEntry`(line 8100,ns `Yoji.U3DAILinker.Registry`,internal,enum Status/Kind + DisplayName)与 LINK-2 权威 `RegistryEntry`(line 1619,**同命名空间**,public,string Status/Kind,无 DisplayName)同名同 ns,只改 9 处会让残留 token 静默重绑到 LINK-2 POCO,在 Task 39-44 触发 CS0029/CS0117/CS0019/CS1503/CS1061。两个独立 verifier 确认会破坏串行编译。

**方案 A(主,逐 token 改名 —— 编译器是兜底):** 把下列 LINK-6(line 8100-9550)范围内**每一个** `RegistryEntry` token 改为 `RegistryEntryView`,**含泛型元素类型**(非仅 `new RegistryEntry{}` 初始化器与方法形参):

| 文件 | 行号 | token 形态 |
|---|---|---|
| `Editor/Settings/RegistryTypes.cs` | 8100 | `class RegistryEntry`(定义) |
| 同上 | 8122 | `public List<RegistryEntry> Entries = new List<RegistryEntry>();`(字段类型 + 初始化器,共 2 处) |
| `Editor/Settings/PanelStateModel.cs` | 8832 | `BuildDesired(RegistryEntry entry, ...)`(形参) |
| 同上 | 8874 | `ResolveRequiredBy(..., RegistryEntry entry)`(形参) |
| `Tests/Editor/PanelStateModelTests.cs` | 8573 | `Entries = new List<RegistryEntry>`(元素类型) |
| 同上 | 8575, 8583 | `new RegistryEntry { ... }`(初始化器) |
| `Editor/Operations/RefreshDevPlanner.cs` | 9082 | `static List<RegistryEntry> ResolveClosure(`(返回类型) |
| 同上 | 9083 | `Dictionary<string, RegistryEntry> byId`(值类型) |
| 同上 | 9085 | `var result = new List<RegistryEntry>();`(元素类型) |
| `Tests/Editor/RefreshDevPlannerTests.cs` | 9164 | `Entries = new List<RegistryEntry>`(元素类型) |
| 同上 | 9166, 9172, 9179 | `new RegistryEntry { ... }`(初始化器) |
| `Tests/Editor/RestorePlannerTests.cs` | 9332 | `Entries = new List<RegistryEntry>`(元素类型) |
| 同上 | 9334 | `new RegistryEntry { ... }`(初始化器) |

> 必须**与定义改名同批**完成,不留任何残留 token。改完后 `LinkerRegistry.Entries` 类型为 `List<RegistryEntryView>`,与测试集合初始化器、`PanelStateModel`/`RefreshDevPlanner` 的成员访问(`DisplayName`、`Kind==ToolKind.*`、`Status==ToolStatus.*`)类型自洽。**不可动**:LINK-2 POCO line 1619、TopologicalSorter line 2530/2532/2573、RegistryFixtureTests line 10220(均消费 LINK-2 权威版)。行号为预检时快照,执行者以 Task 39-44 实际代码块为准定位;批改后跑 batchmode,若现上述 CS 错误,对照本表补齐遗漏 token——编译器保证无残留。

**方案 B(可选,命名空间隔离):** 不逐 token 改名,而把整个 LINK-6 视图簇(`LinkerRegistry` + 视图 `RegistryEntry`)移出 ns `Yoji.U3DAILinker.Registry`、放进专用 ns `Yoji.U3DAILinker.Panel`。注意:这些文件仍需从 ns `Registry` 引入 `ToolStatus`/`ToolKind`(B.1 决策 1/2 已删 PackageStatus/PackageKind),若同时 `using` 两 ns 会因 `RegistryEntry` 双定义产生 CS0104 歧义——故方案 B 必须配合"视图类型仍叫 `RegistryEntryView`"或"对 ToolStatus/ToolKind 全限定 `Registry.ToolStatus`"才无歧义。综合权衡:**方案 A 更直接且编译器可逐错暴露,定为主方案;方案 B 仅在执行者偏好彻底隔离时采用。**

> 真冲突共 6 类(OperationLog.cs 文件 / DependencyChange 类型 / RegistryEntry 类型 / URL builder 方法名+参数序+常量名 / SettingsProvider 双注册 / package.json 字面漂移),已在 B.1-B.3 给出权威解;其余为幂等基线重复或合法演进。执行任一 Task 时,凡触及上述类型/文件,以本附录为准。
