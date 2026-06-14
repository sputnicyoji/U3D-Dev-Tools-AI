# U3D-Dev-Tools-AI 剩余工作计划

> 日期: 2026-06-13
> HEAD: 1ebe62c
> 范围: 非 HybridCLR(本计划不覆盖 HybridCLR 相关工作)
> 数据来源: remaining-improvements 审计(8 agent 逐项核实代码与设计文档)

## 0. 执行摘要

本仓库当前落地三个 Unity AI 工具包(`com.yoji.editor-debug`、`com.yoji.test-runner`、`com.yoji.lua-device-debug`),它们以 loopback HTTP + 反射/Test Runner API 的形态让 AI agent 在编辑器或设备在线时驱动 Unity;test-runner 与 editor-debug 的多项增强已在 main 1ebe62c 合入并通过离线 review,lua-device-debug 已 transport-only 就绪(8/8 fake-host EditMode 测试通过)。但把这些工具变成"可信、可批量分发、可跨工程协作"的完整产品,还隔着三大杠杆主题尚未跨越:

- **linker 全未建且必须承担 Git-URL 顶层依赖编排**: `u3d-ai-linker`(把工具批量装进游戏工程、并把 Skill/规则片段同步给 Claude Code 与 Codex 的编排包)在仓库里完全不存在——无包目录、无 Registry、无任何 `Client.Add`/Junction/`resolvedPath` 代码,git tag 为空。Git-URL UPM 不支持包内声明同级 Git 依赖,因此已定案由 linker 把 `com.yoji.*` 工具包和未来 `com.yoji.editor-core` 作为目标工程 `Packages/manifest.json` 顶层依赖写入,并用 Registry `kind:"infra"` + `dependsOn` 表达安装拓扑。这是规模最大、关键路径最长的一块。
- **验证欠账**: test-runner/editor-debug 的增强代码已落地,但"它真的能跑/能编译"从未被在线 Unity 证明过——e2e 从未在在线 Editor 重跑、验证基线计数仍停留在增强落地前、Unity 6.4 的 entity-id 分支从未在 6.4 真机编译、headless batchmode runner 已存在却硬编码 Unity 路径且未接 CI。lua-device-debug 的 Android 真机全流程同样从未跑通。这些是"已写未验"的债,价值高、成本以等待 Unity 资源为主。
- **editor-core 抽取门控**: editor-debug 与 test-runner 在 dispatcher / HTTP host 外壳 / recompile 原语上有约 150-200 行并行维护的重复代码,抽取本身技术上可行(dispatcher 移动零风险),但包化分发必须等 linker 的 Registry schema v2、manifest 顶层依赖事务和 `kind:"infra"` 拓扑先落地。共享 core 让两工具 semver/回滚不再完全独立,这与 linker 每工具独立版本原则有张力;host 抽取又必须先于 TR-1c reattach 浇筑("先浇混凝土再钻孔"),其紧迫性取决于 TR-1a spike 的结论。

本计划按依赖顺序把全部剩余工作拆为 7 个工作流(WS1-WS7)与 6 个分阶段里程碑(Phase A-F),并在附录给出全量项索引。

## 1. 现状快照

三工具的落地与未落地一览:

- **test-runner(`com.yoji.test-runner`)**: 已落地——EditMode 测试面(TR-2 每测失败详情 `failures[]`、TR-3 的 0 命中守卫与 `/list-tests`、TR-4 端口改为 `{21890, 21896, 21897}` 以避开 lua 钉死的 21894),均在 commit `b72d241` 合入。未落地——PlayMode 全家(TR-1a/b/c,全包无任何 `EnterPlayMode`/`DisableDomainReload`/`SessionState`/`isPlaying` 引用,`BuildFilter` 仍写死 `TestMode.EditMode`,`/run-tests` 仍硬拒 PlayMode);若干健壮性补漏(`/list-tests` 缺 busy 守卫、`client.py` 缺 `--groups` 且 mode 面不一致、completed message 与 SKILL.md 不符、`k_StaleJobMs` 不可配)。
- **editor-debug(`com.yoji.editor-debug`)**: 已落地——`/batch`、`/console`、`/ping` 编辑器态、`ResultSerializer.ToPayload` 共享整形(ED-2-SERIALIZE-DEDUP 实质已完成)。未落地——写类调用零防护与 kill-switch 仅覆盖 `/eval`(ED-4 只读/denylist)、describe 与 invoke 成员枚举不一致(ED-DESCRIBE base-walk)、describe 不支持成员过滤/批量(ED-6)。Unity 6.4 的 entity-id 分支代码已写但从未在 6.4 编译。
- **lua-device-debug(`com.yoji.lua-device-debug`)**: 已落地——通用传输层(HTTP/JSON 服务、Host 契约、主线程派发、JSON 限额、双授权 mutation gate、Editor/Player 启动器、Agent CLI),8/8 fake-host EditMode 通过。未落地——无任何真实 `ILuaDeviceDebugHost` 参考实现、无 `Samples~`、SyncContext 设备侧抓取未验证、Android 真机全流程从未跑通、Release 排除仅靠 `#if` 而无构建期断言。
- **u3d-ai-linker**: 完全不存在(无包、无 Registry、无代码、无 tag)。
- **协作工作流**: 跨工程协作方案仅停留在文字层面,无 handoff 模板/例程,planning 产物被 `.gitignore` 整体忽略,通道切换依赖尚未构建的 linker。

注:两个 doc P0 一致性欠账已修复——`progress.md` 头部 SHA 已更新为 `main = 1ebe62c`(`progress.md:5`);README 端口表已修正为 `21890 (fallback 21896/21897)`(`README.md:132`),与 `TestRunnerMCP.cs:22` 的 `k_Ports = { 21890, 21896, 21897 }` 一致。

## 2. 依赖与推荐顺序

关键门控链(箭头表示"前者解除后者才能落地"):

```
LINKER-MANIFEST-TOPLEVEL-DEPS(已定案: manifest 顶层依赖 + kind:infra)
        |
        +--> ARCH-1a(dispatcher 源码级收尾:删副本/改依赖)
        +--> ARCH-1b(host + recompile 抽取与 editor-core 包化分发)

ARCH-1b(LoopbackHttpHost on-start hook)
        |
        +--> TR-1b / TR-1c(SessionState reattach 必须挂稳定 host)

LINK-0(Agent~/BundledSkills~ Git-Package-Cache 存活探针)
        |
        +--> LINK-1 ... LINK-8(全链;探针失败则全改 *.zip.bytes 形态)

LDD-ADAPTER-EXAMPLE(参考适配器样例)
        |
        +--> LDD-ANDROID-LIVE(消费工程需样例才能实现 xLua adapter)

并行先行(无上述门控、最便宜高杠杆):
  - 验证欠账(在线 e2e 重跑、6.4 编译、/console probe、CI 去硬编码)
  - 一致性补漏(client.py --groups、completed message、错误码对齐)
  - ARCH-1-envelope-guard(信封守卫,作为 ARCH-1b 的设计约束随其落地)
```

补充耦合关系:

- `ARCH-1-upm-sibling-dep-blocker` 已由 linker 设计定案: Linker 写目标工程 manifest 顶层依赖,`editor-core` 在 Registry 中建为 `kind:"infra"` 并通过 `dependsOn` 排序。剩余工作不是拍板,而是先实现 Linker skeleton/schema/manifest 事务,再让 editor-core 包化分发接入。
- `TR-1a`(DisableDomainReload)的 spike 结论决定 `ARCH-1b` 的 forcing function 是否成立:若 TR-1a 够用使 TR-1 不碰 host,则 ARCH-1b 紧迫性下降、TR-1b/c 整体丢弃。
- `WS7`(协作落地)的 7.3 通道切换"自动化执行"被 WS6 linker 阻塞;7.1/7.2(handoff 模板 + planning 可追踪)无阻塞,可立即做。
- 验证欠账(WS1)的 CI + 刷新基线是 `ARCH-1`(editor-core 抽取)无回归门禁的前置基础设施,应在 ARCH-1 动工前先就位。

## 3. 分阶段里程碑

每个 Phase 标注纳入的条目 id 与出口标准。Phase 之间大体串行,但 Phase A 的子项之间、以及 Phase A 与 Phase B 的 LINK-0 探针可并行启动。

### Phase A — 验证 + 一致性 + envelope-guard(最便宜高杠杆)

纳入条目: TR-e2e-rerun、HYG-VERIFY-DEBT、ED-6.4-COMPILE、ED-1-VERSION-ROBUSTNESS、HYG-BATCHMODE-NOT-WIRED、TR-failmsg-mismatch、TR-listtests-null-api、TR-client-groups、TR-5(仅 k_StaleJobMs 可配)、LDD-ERRCODE-MAP、LDD-PER-COMMAND-TIMEOUT、LDD-CMDDESC-DEAD、LDD-EXECUTE-DOUBLE-DISPATCH、LDD-SKILL-ASSETS、ARCH-1-envelope-guard(设计定稿)。

出口标准: 两套 `run-e2e.py` 在线全 PASS 并刷新 `progress.md`/`README` 基线计数;Unity 6.4 entity-id 分支真机编译零 error 且 round-trip 冒烟通过;headless runner 去硬编码可移植 + CI 骨架就位;lua 传输层的错误码对齐设计码表、429 队列满有测试覆盖、双趟主线程跳合并为一趟;信封守卫设计定稿。

### Phase B — linker 前置骨架与安装契约(LINK-0..3 最小闭环)

纳入条目: LINK-0-probe、LINK-1-skeleton、LINK-2-registry-schema、LINK-2b-manifest-transaction。

出口标准: `Agent~/`/`BundledSkills~/` 在 Git 安装 `resolvedPath` 下是否可读已有明确探针结果(带 Unity 版本);linker 空包和 `SettingsProvider("Project/U3D AI Linker")` 骨架存在;Registry schema v2 支持 `status`/`kind`/`dependsOn`/`agentAssets` 并严格校验;`editor-core` 作为 `kind:"infra"` 的建模落地;manifest 修改事务能生成 Git URL 顶层依赖、备份、tmp 写入、冲突检测与 rollback 记录。

### Phase C — editor-core 源码收敛与包化(ARCH-1)

纳入条目: ARCH-1a、ARCH-1b-host、ARCH-1-registry-model(接入 Phase B 的 schema)。

出口标准: 仓库内 `MainThreadDispatcher.cs` 只剩 core 一份(lua Runtime 那份保留);两工具 Start/Loop/Stop/WriteResponse/recompile 脚手架收敛到 core 单一来源,各自 new 独立 RecompilePrimitive(并发不串 flag);editor-debug 仍恒 200 flat 信封、test-runner 仍真状态码(202/409/404/400)逐字节不变;`LoopbackHttpHost` 暴露 on-start hook,test-runner 的 `EnsureRegistered` 经其调用;`editor-core` 通过 linker 顶层依赖和 infra 拓扑可安装。

### Phase D — PlayMode(TR-1)+ editor-debug 加固 + lua 设备侧

纳入条目: TR-1a(spike + 实现)、TR-1b/TR-1c(条件触发)、ED-4、ED-6、ED-DESCRIBE-OVERLOAD-COLLISION、LDD-ADAPTER-EXAMPLE、LDD-SYNCCTX-INIT、LDD-RELEASE-BUILD-VERIFY、LDD-ANDROID-LIVE。

出口标准: TR-1a spike 给出 DisableDomainReload 是否可接受的明确结论,PlayMode pass/fail 用例在线 e2e 通过且 `EditorSettings` scoped 还原无副作用;若进入重路径则 TR-1b/c 的跨域 RunFinished-survival 经 live e2e 证明、init-timeout 收尾有界。editor-debug 的 describe 与 invoke 成员枚举口径一致(base-walk)、describe 支持 filter/kinds/多类型、ED-4 只读/denylist 默认关零回归。lua 提供编译可过且与 xLua 解耦的参考适配器样例(Package Manager 可导入),Android Dev Build 真机 8 命令全 200、SyncContext 设备侧非空、Release 下 21894 无监听。

### Phase E — u3d-ai-linker 完整链路(LINK-4..8)

纳入条目: LINK-3-upm-queue、LINK-4-skill-copy、LINK-5-fragment-merge、LINK-6-project-settings-provider、LINK-7-tool-migration、LINK-8-editor-tests。

出口标准: 持久化 UPM 队列能跨域重载恢复、linker 自更新排队尾、失败停队保留已成功项;事务式 Skill 复制 + Windows Junction + ownership 保护用户目录;CLAUDE/AGENTS 托管区块与 `.gitignore` 区块确定性合并;Project Settings 控制面板可一键 Install/Update All、Sync Agent Assets、Refresh Registry、Rollback Manifest、Copy Diagnostic Report;工具从 planned 迁到 ready(fragments + tags + status);Editor 测试套件覆盖 linker 设计文档“验证”章节列出的 Registry、manifest 事务、ProjectSettings/UserSettings 分离、Agent 探针、Junction 与托管区块断言。

### Phase F — 协作落地(WS7)

纳入条目: HYG-COLLAB-NOT-OPERATIONAL(7.1 handoff 模板 + 例程、7.2 planning 可追踪、7.3 通道切换 SOP)。

出口标准: `docs/superpowers/templates/handoff.template.md` 存在且含全部区块(改动文件/三引用 git 自检/建议提交信息/建议 tag/人需执行命令/未确认项/通道切换建议);`.gitignore` 白名单使 `handoff.md` 可追踪而 mission 草稿仍忽略;COLLAB 文档标注通道切换的自动化执行属 WS6 范围、linker 落地前用手动 SOP,且 `#<tag>` URL 格式与 linker 设计逐字一致。7.3 的自动化执行随 WS6 完成后回补。

## 4. u3d-ai-linker(从零构建,最大块)

> 工作流 id: WS6 | 依赖: WS-tool-completion(工具 ready 化/发布,供 LINK-7 迁移与打 tag)、WS-dev-tools-enhancements(editor-core 包化分发,接入 LINK-2 Registry schema v2 的 `kind:"infra"`/`dependsOn`) | 工作量: L(约 18-26 人天;关键路径 LINK-3 UPM 队列与 LINK-4 事务复制+Junction)
### 概述与现状核实

本章覆盖 u3d-ai-linker 从零到可用的全部工作。撰写前已核实仓库真实状态(HEAD 附近),关键事实:

- 无 linker 包:`Packages/` 下仅 `com.yoji.editor-debug/`、`com.yoji.lua-device-debug/`、`com.yoji.test-runner/`,无 `com.yoji.u3d-ai-linker/`(设计文档 LINK:22-43 期望的结构未落地)。
- 无 Registry:仓库根无 `Registry/` 目录(LINK:40-43 未落地),也无包内 `BundledSkills~/`。
- 无任何 linker 运行时代码:全仓库 grep `Client.Add` / `AddRequest` / `Junction` / `CreateSymbolicLink` / `resolvedPath` 均无业务命中;仅有的 `InitializeOnLoadMethod`(editor-debug `EditorStateCache.cs:25`)、`PackageManager`(各工具无关引用)与 linker 无关。
- 版本与 tag:`git tag -l` 为空;`editor-debug` / `lua-device-debug` / `test-runner` 的 `package.json` 全是 `"version": "0.1.0"`,均无 `<tool-id>-v<semver>` tag。
- Fragment 现状:仅 `test-runner` 有 `Agent~/fragments/`(`CLAUDE.md` 与 `AGENTS.md`,且当前两文件内容完全相同);`editor-debug`、`lua-device-debug` 只有 `Agent~/skills/`,无 `fragments/`。
- 工具数量:设计文档 LINK:22-43 只画了三个工具包,但仓库实际有四个(多出 `lua-device-debug`);Registry 与迁移必须把它纳入。
- README 误称:根 `README.md:6-9` 称三工具 "now included as UPM packages",`README.md:15-17` 的 Status 列与设计文档的 `planned`/`skill-only`/`ready` 迁移态(LINK:82-88)脱节 —— 实际仍是 `file:` 本地包、未发布 UPM、无 tag。
- asmdef 命名约定:现有包用 `Yoji.<Tool>.Editor`(如 `Yoji.TestRunner.Editor.asmdef`、`Yoji.EditorDebug.Editor.asmdef`),linker 应沿用 `Yoji.U3DAILinker.Editor`。
- 前瞻约束:`docs/superpowers/specs/2026-06-13-dev-tools-enhancements-design.md:168-171` 提出未来 `editor-core` 纯 infra 包(无 Skill、`defaultEnabled=false`、有包间 UPM 依赖与安装顺序),而当前 Registry schema 不表达「包间 UPM 依赖与安装顺序」。LINK-2 schema 设计需为此预留扩展位(不必首版实现)。

依赖文档:`docs/superpowers/specs/2026-06-12-u3d-ai-linker-design.md`(完整设计,下文 file:line 简写为 LINK:行号)、`docs/superpowers/specs/2026-06-13-cross-project-collaboration-solution.md`(内/外循环、file: 内循环与 tag 外循环边界)。

> 本章是该 spec 的下游执行蓝图;**spec 是 source of truth**(本章基于 spec @ main 1ebe62c)。两者职责分工:spec 定 WHAT/WHY(架构、Registry schema 字段语义、状态/事务/探针硬约束),本章定 HOW(LINK-0..8 排序、类名、改动文件、验收)。spec 的入口形态、schema 字段、版本基线一旦变更,须回写本章。

---

#### LINK-0 Agent~/ 与 BundledSkills~/ Git Package Cache 存活探针(硬前置)

问题:整个 linker 的 Agent 同步模型(LINK:156-181)假设 `ready` 工具的 `Agent~/` 能通过 `PackageInfo.resolvedPath` 在 Git Package Cache 中定位、且 `skill-only` 工具的 `BundledSkills~/<tool>/` 同样可读。但 Unity 对 `~` 后缀目录的处理是「不导入为 Asset」,其在 Git URL 安装后的 PackageCache 中是否仍以原文件树保留、`resolvedPath` 下能否枚举到这些文件,设计文档 LINK:62、LINK:284、LINK:295 明确要求「实现前用最小探针同时验证 `Agent~/` 与 `BundledSkills~/`,失败则停止打包」。当前仓库无任何探针、无 `resolvedPath` 调用,这一硬前置完全未验证。若直接假设可读而开工 LINK-3..7,一旦 PackageCache 吞掉 `~` 目录,后续复制逻辑全部作废。

方案:做一个独立、可丢弃的最小验证,不依赖 linker 主包:
- 在一个临时工具包(可直接复用现有 `com.yoji.test-runner`,临时加一个 `BundledSkills~/probe/marker.txt` 与已有的 `Agent~/`)上,打一个临时 tag 并 push,然后在一个空白 Unity 2022.3 工程里用 Git URL(`?path=/Packages/com.yoji.test-runner#<probe-tag>`)安装。
- 写一个一次性 `EditorWindow` 或 `[MenuItem]` 脚本 `Yoji.LinkerProbe`,通过 `UnityEditor.PackageManager.PackageInfo.FindForAssembly` 或 `PackageInfo.GetAllRegisteredPackages()` 取目标包的 `resolvedPath`,再用 `System.IO.Directory.EnumerateFileSystemEntries` 递归列出 `resolvedPath/Agent~/` 与 `resolvedPath/BundledSkills~/`,断言能读到 `SKILL.md` / `marker.txt`。
- 关键取舍与坑:(1) 必须在真正的 Git URL 安装(走 PackageCache)下验证,不能用 `file:` 本地包验证 —— `file:` 包不经 PackageCache,结论不可迁移(对照 cross-project 文档 4.1 内循环边界)。(2) Unity 版本差异:首版目标 Unity 2022.3+,但本仓库工具实际 baseline 是 6000.3.16f1(README:15-17),探针应至少在 2022.3 与 6000.3 各跑一次,因为 `~` 目录处理在版本间可能变化。(3) 探针只读不写,跑完即弃,不污染主线。
- 失败分支(LINK:62 指定):若任一版本下 `Agent~/` 或 `BundledSkills~/` 在 `resolvedPath` 下不可枚举,则全链改用包内 `Editor/Resources/AgentData.zip.bytes` 与 `Editor/Resources/BundledSkills.zip.bytes`,由 linker 在运行时解压到 staging。该分支会显著改变 LINK-1(包结构需加打 zip 的构建步骤)、LINK-4(复制源从 `resolvedPath/Agent~` 改为解压产物)、LINK-7(每个工具的发布流程要在打 tag 前生成 zip.bytes)。因此 LINK-0 结论必须先于 LINK-1 定稿。

改动文件:
- 新建(临时,不入主线):空白验证工程 + `Editor/LinkerProbe.cs`;`com.yoji.test-runner/BundledSkills~/probe/marker.txt`(验证后删除)。
- 产出:一份探针结论记录(写入本计划文档或 spec 的「探针结果」小节,标注 Unity 版本与 `resolvedPath` 是否可读),不产生 linker 代码。

步骤:
1. 在现有工具包临时加 `BundledSkills~/probe/marker.txt` 与 probe 脚本,打临时 tag 并 push(git 写由人执行,见 cross-project 文档第 5 节)。
2. 空白 Unity 2022.3 工程经 Git URL 安装该包。
3. 运行 `LinkerProbe`,记录 `Agent~/` 与 `BundledSkills~/` 在 `resolvedPath` 下是否可完整枚举。
4. 在 Unity 6000.3 工程重复 2-3。
5. 写下结论并据此锁定 LINK-1 走「`Agent~/` 直读」还是「`*.zip.bytes` 解压」分支;删除临时探针物料与 tag。

验收:有一份明确结论(带 Unity 版本号)说明 `Agent~/`、`BundledSkills~/` 是否在 Git 安装的 `resolvedPath` 下可读;LINK-1 的目录/构建形态据此确定;若走 zip 分支,已记录对 LINK-4/LINK-7 的具体影响。无结论前不得开工 LINK-1。

依赖/阻塞:无前置(它是全章硬前置)。阻塞 LINK-1..8 全部。需要一次人工 git tag/push(外循环,cross-project 文档 4.2)。

---

#### LINK-1 linker 包骨架(com.yoji.u3d-ai-linker)

问题:`Packages/` 下无 `com.yoji.u3d-ai-linker/`,设计期望的 `Editor/`、`Registry/`、`BundledSkills~/`、`package.json`(LINK:22-27)均不存在。没有骨架,后续所有代码无处安放。

方案:按设计 LINK:22-27 与现有包惯例搭骨架。
- 目录:`Packages/com.yoji.u3d-ai-linker/{Editor,Tests/Editor,Registry,BundledSkills~}` + `package.json`。
- `package.json`:`name: com.yoji.u3d-ai-linker`,`version: 0.1.0`,`unity: 2022.3`(首版目标 LINK:16),依赖 `com.unity.nuget.newtonsoft-json`(Registry JSON 解析,与其它包一致)。注意 linker 不依赖 `com.unity.test-framework` 作为运行依赖,但测试程序集需要 —— 用 `versionDefines`/测试 asmdef 单独引用,避免把 test-framework 拖进运行时。
- asmdef:`Editor/Yoji.U3DAILinker.Editor.asmdef`(`includePlatforms: ["Editor"]`,引用 newtonsoft),沿用现有 `Yoji.<Tool>.Editor` 命名(对照 `Yoji.TestRunner.Editor.asmdef`)。`Tests/Editor/Yoji.U3DAILinker.Editor.Tests.asmdef`(引用主 asmdef + nunit + test-framework,`Editor`-only)。
- 关键坑:(1) `BundledSkills~/` 与 `Registry/` 在 linker 自身包内 —— linker 自己也是 Git 包,LINK-0 的探针结论同样适用于「linker 包内 `BundledSkills~/` 能否在被安装后读到」;若 LINK-0 判定不可读,`BundledSkills~/` 也要改 zip.bytes。(2) `Registry/`(包内快照)是离线回退(LINK:64),与仓库根 `Registry/`(LINK-2 的发布源)是两份,别混。

改动文件:
- 新建:`Packages/com.yoji.u3d-ai-linker/package.json`、`Editor/Yoji.U3DAILinker.Editor.asmdef`、`Tests/Editor/Yoji.U3DAILinker.Editor.Tests.asmdef`、占位 `Editor/U3DAILinkerSettingsProvider.cs`(空壳,LINK-6 填充)、`BundledSkills~/.keep`、`Registry/.keep`。

步骤:
1. 建目录与 `package.json`。
2. 建两个 asmdef 并确认编译通过(空工程或现有 TestProjects 引入)。
3. 提交骨架(人执行 git)。

验收:Unity 打开能识别该包、两个 asmdef 编译无错;`com.yoji.u3d-ai-linker` 出现在 Package Manager;骨架与 LINK-0 结论一致(走直读则保留 `BundledSkills~/`,走 zip 则改 Resources 形态)。

依赖/阻塞:依赖 LINK-0 定稿。阻塞 LINK-2..8。

---

#### LINK-2 Registry schema + stable/dev.json + 严格白名单校验

问题:无仓库根 `Registry/`(LINK:40-43),无 `stable.json`/`dev.json`,无任何 schema 与校验代码。设计对 Registry 有强安全约束(LINK:64-88、108-118):必须含 `schemaVersion`、每工具声明 `status`/`order`/`packageName`/`packagePath`/`revision`/`defaultEnabled`,且解析后必须过白名单(仓库固定、`com.yoji.` 前缀、`packagePath==Packages/<packageName>` 禁 `..`/绝对路径/URL、stable revision 匹配 `<tool-id>-v<semver>`、dev branch 只能 `main` 且运行时 revision 必须 40 位 SHA、ID/包名/Skill 名唯一、未知字段/状态/`schemaVersion` 拒绝整个 Registry)。这是 linker 安全边界的核心,缺它则 Registry 等同任意安装脚本。

方案:
- 数据形态:仓库根 `Registry/stable.json`、`Registry/dev.json`,顶层 `schemaVersion`(整数,首版为 1)+ `tools[]`。`stable.json` 每项带 `revision: <tool-id>-v<semver>`;`dev.json` 每项不带 revision,顶层或每项声明 `branch: "main"`(LINK:144),运行时由 LINK-3 的 `git ls-remote` 解析为 40 位 SHA。
- 代码(`Editor/Registry/` 下):`RegistryDocument`(POCO,newtonsoft 反序列化,`MissingMemberHandling.Error` 以实现「未知字段拒绝」)、`RegistryEntry`、`ToolStatus` 枚举(`ready`/`skill-only`/`planned`,未知值拒绝)、`RegistryValidator`(执行 LINK:108-118 全部白名单规则,返回结构化错误而非抛裸异常)、`RegistryLoader`(远程拉取 `https://raw.githubusercontent.com/sputnicyoji/U3D-Dev-Tools-AI/main/Registry/<channel>.json`,LINK:103;失败回退包内快照并标记 `IsOffline=true`,LINK:106)。
- 关键取舍与坑:(1) 安装 URL 必须由 linker 用已验证字段「生成」,绝不执行 Registry 提供的整串 URL(LINK:118)——把 URL 生成放在 `ToolUrlBuilder`,只接受已过校验的 `packageName`/`packagePath`/`revision`。(2) `schemaVersion` 不支持时整文档拒绝(LINK:66、116),避免新目录驱动旧 linker。(3) semver 与 SHA 用严格正则(`^[a-z0-9-]+-v\d+\.\d+\.\d+$`、`^[0-9a-f]{40}$`),拒绝带 `..`/路径分隔符的注入。(4) 唯一性校验覆盖 tool id、packageName、Skill 名三套集合。(5) 前瞻:enhancements design:168-171 的 `editor-core`(无 Skill、`defaultEnabled=false`、有包间 UPM 依赖与安装顺序)当前 schema 不表达;首版 schema 应允许 `status` 之外的字段被「显式忽略」难以兼顾「未知字段拒绝」,因此预留一个明确的可选字段(如 `dependsOnPackages: []`,首版校验为空数组)而非靠未知字段穿透。注(对 spec 的显式有意偏离):spec(LINK:100-118、431-432)把 `dependsOn` 拓扑排序 + 环检测列为首版强约束;本计划因首版 `ready` 集为空/单包、暂无多包依赖可排,先把它降级为「字段预留、校验为空」,待 `editor-core`(`kind:"infra"`)接入时再补齐拓扑排序与环检测。这是经权衡的范围收窄,非遗漏。
- 初始内容:四个工具首版按 LINK:88 标 `planned` 或 `skill-only`(无公开 UPM 包时不得标 `ready`);`Install All` 只处理 `ready`(LINK:88),首版可能为空集,符合预期。

改动文件:
- 新建:`Registry/stable.json`、`Registry/dev.json`;`Packages/com.yoji.u3d-ai-linker/Registry/stable.json`、`dev.json`(包内快照);`Editor/Registry/RegistryDocument.cs`、`RegistryEntry.cs`、`ToolStatus.cs`、`RegistryValidator.cs`、`RegistryLoader.cs`、`ToolUrlBuilder.cs`。

步骤:
1. 定 schema(POCO + 枚举 + schemaVersion=1),写 `stable.json`/`dev.json` 初始内容(四工具,planned/skill-only)。
2. 实现 `RegistryValidator` 全部白名单规则。
3. 实现 `RegistryLoader`(远程 + 离线回退 + `IsOffline` 标记)与 `ToolUrlBuilder`。
4. 在包内 `Registry/` 放一份快照。

验收:LINK-8 的解析与校验测试覆盖 LINK:271-273(合法解析、仓库/路径/命名/revision/未知字段/未知状态/不支持 schemaVersion 的拒绝、stable URL 与 dev SHA URL 生成)全部通过;喂一份带 `..` 的 `packagePath` 或自定义仓库 URL 时整文档被拒;离线回退时 `IsOffline=true`。

依赖/阻塞:依赖 LINK-1。被 LINK-3(队列消费 Registry 与生成的 URL)、LINK-5(按 `order` 合并 fragment)、LINK-6(展示状态)依赖。

---

#### LINK-2b 目标工程 manifest 修改事务

问题:UPM 不解析 Git 包内声明的同级 `com.yoji.*` 依赖(本仓库已记录的阻断,见 `_planning/.../git-url-upm-no-sibling-dependency-resolution`),因此所有工具与未来的 `editor-core` 必须由 linker 写入目标工程 `Packages/manifest.json` 顶层依赖。设计为此专门规定了一套 manifest 修改事务(LINK:226-243),并把它列为 UPM 阻断解法的承重墙(LINK:129-149)与验证矩阵项(LINK:434-435)。当前 LINK-3 只读 manifest 做恢复核对,没有事务式「写」manifest 的实现,留下覆盖洼地——本条把它独立补回(对应背景段 Phase B 的 `LINK-2b-manifest-transaction`)。

方案(严格按 LINK:226-243 七步):
- `ManifestTransaction`:(1) 读并解析目标 `Packages/manifest.json`,用 newtonsoft `JObject` 保序、保留全部未知字段与原格式;(2) 先备份原文件到 `Library/U3DAILinker/manifest.backup`;(3) 只增改 linker 托管的 `com.yoji.*` 依赖项(用 LINK-2 `ToolUrlBuilder` 生成的已验证 Git URL),不碰其它依赖;(4) 写 `manifest.json.tmp`;(5) 解析校验 tmp 合法(JSON 可解析、托管项齐全);(6) `File.Replace` 原子替换;(7) 把本次改动(`desiredUrl`/旧值)记入 `operation.json`,供失败回滚与 LINK-3 队列核对。
- 冲突检测(LINK:242):若目标已存在同名 `com.yoji.*` 依赖但非 linker 托管/指向不同源,停止并要求用户确认,不静默覆盖。
- 失败恢复:tmp 写入/校验失败只删 tmp;原子替换后若后续步骤失败,用 `manifest.backup` 回滚。
- 关键取舍与坑:(1) 必须保留 manifest 中 linker 不托管的依赖与未知顶层字段,只动 `com.yoji.*` 托管项(LINK:230)。(2) 与 LINK-3 的关系:LINK-3 在 `Client.Add` 前后据本事务写/核对 manifest;本条是「写」,LINK-3 的恢复核对是「读」,二者共用 `operation.json`。(3) `editor-core` 作为 `kind:"infra"` 也经本事务写入顶层(这正是 UPM 同级依赖阻断的落地解,LINK:129-149)。

改动文件:
- 新建:`Editor/Operations/ManifestTransaction.cs`、`ManifestBackupStore.cs`。

步骤:
1. 实现保序、保留未知字段的 manifest 读写(`JObject`)。
2. 实现备份→只改托管项→tmp→校验→原子替换→记日志七步。
3. 实现「已存在非托管同名依赖」的冲突检测分支。
4. 用 LINK-8 `ManifestTransactionTests` 覆盖:七步事务、冲突检测、tmp/backup 回滚(逐条对齐 LINK:434、LINK:242)。

验收:LINK-8 的 manifest 事务断言(LINK:434)与冲突检测(LINK:242)逐条通过;注入「已存在非托管 `com.yoji.x`」时被拒不覆盖;中途失败能用 `manifest.backup` 回滚且 manifest 未损坏;非托管依赖与未知字段原样保留。

依赖/阻塞:依赖 LINK-2(URL 生成)。被 LINK-3(队列在 `Client.Add` 前后据此写/核对 manifest)依赖。

---

#### LINK-3 持久化 UPM 安装队列 + 域重载恢复

问题:无任何 `Client.Add`/`AddRequest` 调用,无操作日志,无 `[InitializeOnLoad]` 恢复逻辑。设计要求 UPM 请求串行(LINK:98)、每次请求前原子写 `Library/U3DAILinker/operation.json`(LINK:90-153、227-266、241),并能在 UPM 触发的脚本编译/域重载后,用 `[InitializeOnLoad]`+`EditorApplication.delayCall` 在 Editor 空闲后,以「实际 manifest + 已解析 PackageInfo」为准恢复队列(LINK:256、261),旧 `AddRequest` 不跨域保留。这是 linker 最难、最易出错的一块。

方案:
- 状态三层(LINK:236-239):`ProjectSettings/U3DAILinkerSettings.asset`(通道/启用工具/期望版本,可提交、无绝对路径)、`UserSettings/U3DAILinkerUserSettings.asset`(窗口/本机偏好)、`Library/U3DAILinker/operation.json`(当前操作日志,临时)。
- `OperationLog`(POCO,字段见 LINK:243-254:`operationId`/`action`/`toolIds`/`currentIndex`/`phase`/`desiredUrl`/`completed`/`retryCount`)。`OperationLogStore`:写入走「先写 `operation.json.tmp` 再 `File.Replace`/原子 rename」(LINK:241),保证域重载中途崩溃不留半截文件。
- `InstallQueue`/`UpmQueueRunner`:串行 `Client.Add`(LINK:98),每发一个请求前先把 `phase=package-requested`、`desiredUrl=<已验证生成的 URL>` 原子落盘,再调用 `Client.Add`;轮询 `AddRequest.IsCompleted`。完成后 `phase` 推进、`completed` 追加、`currentIndex++`,再落盘下一项。
- 恢复:`[InitializeOnLoad]` 静态构造里读 `operation.json`,若存在未完成队列,挂 `EditorApplication.delayCall` 等到 `!EditorApplication.isCompiling && Client.???空闲`;恢复时不复用旧 `AddRequest`,而是用 `PackageInfo`/读项目 `Packages/manifest.json` 核对「目标 URL/revision 是否已达成」(LINK:256、261):已达成则推进队列,否则对该项重试,`retryCount` 上限 2(LINK:256),超限停止并保留日志供用户重试/取消。
- 关键取舍与坑:(1) 恢复绝不能依赖旧窗口实例或静态字段(LINK:153)——尤其 linker 自更新会替换 Editor Assembly,所有恢复状态只能来自磁盘日志。(2) linker 自身更新必须排队列最后(LINK:153、275):队列构建时把 `com.yoji.u3d-ai-linker` 强制排到尾部。(3) `Client.Add` 失败时停止队列、保留已成功项(LINK:260),不回滚已装包。(4) 域重载判定要兼容「编译完成但 PackageManager 仍 busy」——`delayCall` 里再查一次 `isCompiling`,必要时重挂。(5) 写日志用 `File.Replace`(同卷原子),`Library/` 目录可能不存在需先建。

改动文件:
- 新建:`Editor/Operations/OperationLog.cs`、`OperationLogStore.cs`、`UpmQueueRunner.cs`、`InstallQueueBuilder.cs`(含 linker-last 排序)、`U3DAILinkerSettings.cs`(ScriptableObject,ProjectSettings)、`U3DAILinkerUserSettings.cs`(UserSettings)、`Editor/U3DAILinkerBootstrap.cs`(`[InitializeOnLoad]` 恢复入口)。

步骤:
1. 定义 `OperationLog` 与 `OperationLogStore`(原子 tmp+replace),先用测试覆盖原子写。
2. 实现 `InstallQueueBuilder`(只取 `ready`,linker 强制排尾)。
3. 实现 `UpmQueueRunner` 串行 `Client.Add` + 每步落盘。
4. 实现 `U3DAILinkerBootstrap` 的 `[InitializeOnLoad]`+`delayCall` 恢复,核对 manifest/PackageInfo,重试上限 2。
5. 用 LINK-8 测试覆盖状态机转换、原子写、恢复、linker-last、失败停队。

验收:LINK-8 覆盖 LINK:274-275(队列状态转换、日志原子写、域重载恢复、linker 自更新排最后)通过;手动验证(LINK:286)中途触发域重载后队列能继续;`Client.Add` 失败时队列停止且已成功项保留;杀进程后重开 Editor 能从 `operation.json` 续跑。

依赖/阻塞:依赖 LINK-2(URL 生成与 Registry)。被 LINK-4(安装完成后重建 Agent 链接,LINK:149)、LINK-6(窗口触发 Install All/Refresh Dev)依赖。是本章风险最高项。

---

#### LINK-4 事务式 Skill 复制 + ownership + Windows Junction

问题:无 `Junction`/`CreateSymbolicLink` 代码,无 `.u3d-ai-linker/` 物理目录管理。设计要求把工具 `Agent~/`(`ready`,经 `resolvedPath`)或 `BundledSkills~/<tool>/`(`skill-only`)复制到 `<project>/.u3d-ai-linker/skills/<tool>/`,再为 `.claude/skills/<tool>/` 与 `.agents/skills/<tool>/` 建 Windows Junction(LINK:156-181);复制必须事务式(staging→校验 SKILL.md→写 ownership→backup 旧目录→move→建/修 Junction→删 backup,LINK:174-181),同卷 move,失败可回滚,缺合法 ownership 的目标视为用户目录不覆盖(LINK:170、181)。

方案:
- `AgentSyncService` 主流程严格按 LINK:174-180 六步:(1) 复制到 `.u3d-ai-linker/.staging/<tool>-<operationId>/`;(2) 校验 Registry 声明的所有 `SKILL.md` 存在;(3) 写 `.u3d-ai-owner.json`(工具 ID、来源 revision、内容哈希,LINK:176);(4) 旧目标存在且 ownership 合法则先 move 到 `.u3d-ai-linker/.backup/<tool>-<operationId>/`;(5) staging move 为正式 `skills/<tool>/`;(6) 建/修 Junction,成功后删 backup。
- `OwnershipFile`(读写 `.u3d-ai-owner.json`)+ `OwnershipGuard`:同步前先判定目标是否 linker 管理(LINK:170);缺合法 ownership 即用户目录,不覆盖不删除(LINK:181)。
- `JunctionManager`(Windows):用 directory junction(非符号链接,避免管理员权限要求)。实现可走 `CreateSymbolicLink` w/ `SYMBOLIC_LINK_FLAG_DIRECTORY` 但 junction 更稳;首版可 P/Invoke `DeviceIoControl` 建 reparse point,或退而用 `cmd /c mklink /J`(取舍:mklink 起子进程、依赖 shell,P/Invoke 无外部依赖但代码量大)。鉴于 cross-project 文档指出 shell 通道在本环境不可靠,优先 P/Invoke junction,避免依赖 `mklink`。
- 来源定位:`ready` 用 LINK-3 已解析的 `PackageInfo.resolvedPath + /Agent~/skills`;`skill-only` 用 linker 包内 `BundledSkills~/<tool>/`(若 LINK-0 走 zip 分支,则改为解压产物目录)。
- 关键取舍与坑:(1) 所有 move 同卷(LINK:181)——staging/backup/skills 都在 `.u3d-ai-linker/` 下,天然同卷;但若工程与 PackageCache 跨盘,复制(copy)而非 move 跨盘,只有最终 staging→skills 的 move 需同卷,设计已保证。(2) 步骤 4 之后失败恢复 backup,4 之前失败只删 staging(LINK:181)——用 try/catch 分段标记当前 phase。(3) Junction 冲突不删目标,要用户确认(LINK:262)。(4) 安装完成后由 LINK-3 队列回调触发本服务(LINK:149「安装完成后重建 Agent 链接」)。(5) 内容哈希用于检测漂移与卸载比对。

改动文件:
- 新建:`Editor/Agent/AgentSyncService.cs`、`OwnershipFile.cs`、`OwnershipGuard.cs`、`JunctionManager.cs`(P/Invoke)、`TransactionalCopy.cs`、`ContentHasher.cs`。

步骤:
1. 实现 `JunctionManager`(建/查/删 junction,判断目标是否 junction)。
2. 实现 `OwnershipFile`/`OwnershipGuard`。
3. 实现 `TransactionalCopy` 六步事务,接 staging/backup/skills。
4. 接 `ready`(resolvedPath)与 `skill-only`(BundledSkills~)两类来源。
5. LINK-8 覆盖事务回滚、ownership 校验、未知目录保护、重复同步、Junction 失效修复。

验收:LINK-8 覆盖 LINK:279-280(Junction 创建/重复同步/失效修复/ownership 校验/未知目录保护、staging 失败不破坏现有版本、替换失败能恢复 backup)通过;手动验证(LINK:287)重载后 Claude Code 与 Codex 都能发现 Skill;对一个无 ownership 的普通目录执行同步时不被覆盖(LINK:290)。

依赖/阻塞:依赖 LINK-3(安装完成触发)、LINK-2(Registry 声明的 SKILL.md 清单与来源)。与 LINK-5 共同构成完整 `Sync Agents`。

---

#### LINK-5 CLAUDE/AGENTS 托管区块合并 + .gitignore 托管区块

问题:fragments 仅 `test-runner` 有(`Agent~/fragments/CLAUDE.md`、`AGENTS.md`,且当前两文件内容相同),`editor-debug`、`lua-device-debug` 无 fragments;无任何托管区块合并代码,无 `.gitignore` 区块维护。设计要求把各启用工具的 `fragments/CLAUDE.md`/`AGENTS.md` 按 `order` 升序再按工具 ID 合并进项目 `CLAUDE.md`/`AGENTS.md` 的 `u3d-ai-linker:start/end` 托管区块(LINK:183-205、193),区块外用户内容不动,marker 损坏/重复则停写报冲突(LINK:191);并维护根 `.gitignore` 的 `# >>> u3d-ai-linker >>>` 独立区块,只忽略具体 managed skill 路径(LINK:195-205)。

方案:
- `ManagedBlockWriter`:在目标 markdown 中用正则定位 `<!-- u3d-ai-linker:start -->`..`<!-- u3d-ai-linker:end -->`;不存在则创建(LINK:191);出现 0 或 2+ 对、或 start/end 不配对则判定损坏/重复,停写并报冲突(LINK:191、264)。区块内容整体替换,区块外字节不动。
- `FragmentMerger`:从每个启用工具收集 `fragments/CLAUDE.md`/`AGENTS.md`(可缺省,LINK:193),按 Registry `order` 升序、同 order 按工具 ID 排序(LINK:193),每段前写来源注释。`order` 必须非负整数(LINK:193,校验已在 LINK-2)。发现重复 Skill 名则整个同步预检失败、不部分写入(LINK:193)。
- `GitignoreBlockWriter`:维护 `# >>> u3d-ai-linker >>>`..`# <<< u3d-ai-linker <<<` 区块(LINK:197-203),只写 `/.u3d-ai-linker/` 与各 managed skill 的 `/.claude/skills/<skill>/`、`/.agents/skills/<skill>/` 具体行,不忽略整个 `.claude/`/`.agents/`(LINK:205);marker 损坏停改(LINK:205)。卸载工具时只删 ownership 匹配的物理目录、Junction 与对应 ignore 行(LINK:205)。
- 关键取舍与坑:(1) 合并必须确定性(LINK:277)——排序键固定、来源注释固定,保证可重复执行结果一致。(2) 当前 `test-runner` 的 `CLAUDE.md` 与 `AGENTS.md` 内容相同是巧合,合并逻辑不能假设两者相同,要分别读两套文件。(3) 重复 Skill 名预检与 LINK-2 的唯一性校验是两层:Registry 层校验 Skill 名声明唯一,合并层再校验实际 fragment 不冲突。(4) 区块写入与 LINK-4 的物理复制是同一次 `Sync Agents` 的两半,任一半预检失败都不得部分写入(LINK:193「不进行部分写入」)。

改动文件:
- 新建:`Editor/Agent/ManagedBlockWriter.cs`、`FragmentMerger.cs`、`GitignoreBlockWriter.cs`。
- 修改:为 `editor-debug`、`lua-device-debug` 补 `Agent~/fragments/CLAUDE.md`+`AGENTS.md`(该工作也属 LINK-7 迁移,见下)。

步骤:
1. 实现 `ManagedBlockWriter`(创建/更新/保留用户内容/损坏检测)。
2. 实现 `FragmentMerger`(确定性排序 + 重复 Skill 名预检)。
3. 实现 `GitignoreBlockWriter`(具体路径行 + 损坏保护 + 卸载删行)。
4. 把三者接入 `Sync Agents`,与 LINK-4 共用一次预检。

验收:LINK-8 覆盖 LINK:276-278(区块创建/更新/保留用户内容/冲突检测、fragment 确定性合并与重复 Skill 名预检、`.gitignore` 区块创建/更新/损坏保护)通过;手动制造损坏 marker 时不修改文件(LINK:290);区块外用户内容逐字节保留。

依赖/阻塞:依赖 LINK-2(`order`/Skill 名)。与 LINK-4 共同组成 `Sync Agents`。需 LINK-7 补齐另两个工具的 fragments 才能端到端验证多工具合并(可先用 test-runner 单工具验证)。

---

#### LINK-6 Project Settings 面板(Edit > Project Settings > U3D AI Linker)

问题:无任何控制面板。设计明确主入口**不是独立 `EditorWindow`**,而是 Unity Project Settings(Edit > Project Settings > U3D AI Linker),用 `SettingsProvider` 注册(LINK:321-339);需提供:Stable/Dev 通道选择、工具状态与版本差异、单项安装/卸载/更新/启停、`Install All`、`Sync Agents`、`Refresh Dev`、Junction 与托管区块健康检查;默认不自动后台更新,所有网络/文件变更由用户显式触发(LINK:225)。

方案:
- `U3DAILinkerSettingsProvider`:`[SettingsProvider] static SettingsProvider CreateProvider()` 返回 `new SettingsProvider("Project/U3D AI Linker", SettingsScope.Project)`(LINK:321-339),用 IMGUI `guiHandler` 绘制面板(2022.3 稳)。不建独立 `EditorWindow`、不加 `Tools` 菜单(spec:321-323 明确否决)。
- 数据源:`RegistryLoader`(LINK-2)拉当前通道 Registry(离线则显著标记 offline,LINK:106);对每个工具用 `PackageInfo`/manifest 算「已装版本 vs 期望版本」差异(LINK:218)。
- 动作:按钮分别触发 `InstallQueueBuilder`+`UpmQueueRunner`(Install All / 单项)、`AgentSyncService`+`FragmentMerger`(Sync Agents)、`RefreshDev`(LINK:144-149:`git ls-remote` 取 main 最新 commit→固定所有工具 URL 到该 SHA→`Client.Add`→重建 Agent 链接)。
- 健康检查:Junction 是否存在/失效、托管区块是否损坏(复用 LINK-4/5 的 guard),只读展示,问题项给修复按钮(LINK:223)。
- 关键取舍与坑:(1) 域重载后窗口实例丢失——窗口只是 UI,真正的队列恢复在 LINK-3 的 `[InitializeOnLoad]`,窗口重开时从磁盘日志读当前进度展示,不持有队列状态(LINK:153)。(2) 所有网络/文件操作显式触发,无后台轮询(LINK:225,亦是首版非目标 LINK:310)。(3) `Refresh Dev` 的 `git ls-remote` 是少数必须走外部进程的操作;失败时不改现有安装(LINK:263),并提示用户。

改动文件:
- 修改:`Editor/U3DAILinkerSettingsProvider.cs`(从 LINK-1 占位填充)。
- 新建:`Editor/RefreshDevService.cs`(`git ls-remote` + 锁 SHA + 入队)、`Editor/HealthCheckService.cs`。

步骤:
1. 搭窗口骨架与菜单,展示通道与工具列表/版本差异。
2. 接 Install All / 单项 / 卸载 / 启停。
3. 接 Sync Agents。
4. 实现并接 Refresh Dev(ls-remote→锁 SHA→队列→重建链接)。
5. 接健康检查与修复入口。

验收:`Edit > Project Settings > U3D AI Linker` 可打开;手动验证(LINK:286-289)能从空工程一键装全部 `ready`、Sync Agents、Stable 升级/回滚、Refresh Dev 后所有工具同一 SHA;离线时明确标记;无后台自动更新。

依赖/阻塞:依赖 LINK-2/3/4/5(窗口是它们的统一入口)。被 LINK-7 手动验证依赖。

---

#### LINK-7 工具迁移:planned -> skill-only / ready(fragments + tags + status)

问题:四个工具全停在 `0.1.0` 且无任何 `<tool-id>-v<semver>` tag(`git tag -l` 空);仅 `test-runner` 有 fragments,`editor-debug`/`lua-device-debug` 无;Registry status 尚不存在。根 README:6-9 误称三工具 "now included as UPM packages"、README:15-17 的 Status 与设计的 `planned`/`skill-only`/`ready` 迁移态(LINK:82-88)脱节。设计要求逐个把工具从 `planned` 迁到 `skill-only` 或 `ready`,且公开 Package 未完成前不得描述为已迁移(LINK:301-303)。

方案:
- 为 `editor-debug`、`lua-device-debug` 补 `Agent~/fragments/CLAUDE.md`+`AGENTS.md`(内容简述各自服务,与 test-runner 风格一致),使三/四工具都能进 fragment 合并。
- 迁移决策(LINK:84-88):工具达到「公开 Package + Skill 都具备」才标 `ready` 并打 `<tool-id>-v<semver>` tag(如 `editor-debug-v1.0.0`);仅 Skill 随 linker 交付的过渡态标 `skill-only`(随 `BundledSkills~/`,无独立版本,LINK:85);未完成的留 `planned`(LINK:86)。首版现实:四工具尚无公开 UPM 发布,初始 Registry 按 LINK:88 标 `planned`/`skill-only`,逐个验证后再升 `ready`。
- 同步修正 README:6-9 与 15-17 的「误称」——改成与 Registry status 一致的事实表述(`file:` 本地包 / 迁移态),不再宣称已发布 UPM。
- 关键取舍与坑:(1) tag 打在 monorepo 上必须带工具前缀(LINK:134-140),避免不同工具版本冲突;打 tag 是 git 写操作,交人执行(cross-project 文档第 5 节)。(2) `skill-only` 工具的 Skill 要拷进 linker 的 `BundledSkills~/<tool>/`(或 zip 分支),与 `Agent~/skills/` 保持同源。(3) 迁移顺序:先把最成熟的(test-runner / editor-debug,README:15-16 标 Usable)推到 `ready`,lua-device-debug(README:17「transport started、adapter 仍需」)更可能停在 `skill-only`/`planned`。

改动文件:
- 新建:`Packages/com.yoji.editor-debug/Agent~/fragments/{CLAUDE.md,AGENTS.md}`、`Packages/com.yoji.lua-device-debug/Agent~/fragments/{CLAUDE.md,AGENTS.md}`;linker `BundledSkills~/<skill-only 工具>/`(若有)。
- 修改:`Registry/stable.json`+`dev.json`(填实际 status/revision)、各工具 `package.json`(随 tag 升 version)、根 `README.md`(修误称与 Status 表)。

步骤:
1. 补两个工具的 fragments。
2. 评估每个工具达到 `ready` 还是 `skill-only`/`planned`,更新 Registry status。
3. 对达 `ready` 的工具,升 `package.json` version 并(人)打 `<tool-id>-v<semver>` tag、push。
4. `skill-only` 工具的 Skill 同步进 `BundledSkills~/`。
5. 修正 README:6-9、15-17 使其与 Registry 一致。

验收:Registry 中每工具有合法 status;`ready` 工具有对应 tag 且能经 linker `Install All` 从 Git URL 装上;README 不再宣称未发布的 UPM;fragment 合并能纳入全部启用工具。

依赖/阻塞:依赖 LINK-2(status 语义)、LINK-4/5(skill 复制与 fragment 合并验证)、LINK-6(经窗口端到端验证)。打 tag 需人工 git(外循环)。

---

#### LINK-8 linker Editor 测试套件

问题:无 linker 包即无测试。设计 LINK:268-281 列出必须覆盖的 11 类断言(Registry 解析与校验、各类拒绝、Stable/Dev URL 生成、UPM 队列状态机/日志原子写/域重载恢复、linker 自更新排尾、托管区块创建/更新/保留/冲突、fragment 确定性合并与重复 Skill 名预检、`.gitignore` 区块、Junction 全套、staging/backup 回滚)。

方案:
- 在 `Tests/Editor/`(`Yoji.U3DAILinker.Editor.Tests.asmdef`,EditMode-only,与现有包测试形态一致)建分模块测试类,逐条对应 LINK:271-280。
- 纯逻辑(Registry 校验、URL 生成、fragment 合并、托管区块、`.gitignore`、操作日志原子写、队列状态机)用临时目录 + 内存/磁盘 fixture,不依赖真实 UPM/网络/真实 Junction —— 把 `Client.Add`、`git ls-remote`、junction 建立抽象为接口(`IUpmClient`、`IGitRefResolver`、`IJunctionManager`),测试注入 fake,既可单测又便于 LINK-3/4 的可测性。
- Junction 与域重载恢复部分:状态机与日志读写可纯单测;真实 junction 建立与真实域重载属手动验证(LINK:282-291),测试里用 fake junction 验证 ownership/回滚分支。
- 关键坑:(1) 测试不能落到真实 `Client.Add`(会改工程 manifest)——必须接口隔离。(2) 文件事务测试要在临时目录造「合法 ownership / 无 ownership / 损坏 marker」三类场景,验证不覆盖用户目录。(3) 确定性合并测试要 shuffle 输入顺序后断言输出稳定。

改动文件:
- 新建:`Tests/Editor/RegistryValidatorTests.cs`、`ToolUrlBuilderTests.cs`、`OperationLogStoreTests.cs`、`InstallQueueBuilderTests.cs`(linker-last)、`ManifestTransactionTests.cs`(七步事务 + 冲突检测 + backup 回滚,对齐 LINK:434)、`ManagedBlockWriterTests.cs`、`FragmentMergerTests.cs`、`GitignoreBlockWriterTests.cs`、`TransactionalCopyTests.cs`、`OwnershipGuardTests.cs`。

步骤:
1. 抽象 `IUpmClient`/`IGitRefResolver`/`IJunctionManager` 接口(回填 LINK-3/4/6)。
2. 逐模块写测试,对齐 LINK:271-280 每条。
3. 经 test-runner-mcp(端口 21890,本仓库自带)在 Unity 在线时跑 EditMode 套件,作为内循环验证(cross-project 文档 4.1)。

验收:LINK:271-280 的 11 类断言每条至少一个测试且全绿;`Client.Add`/网络/真实 junction 均被接口 fake,测试不改工程 manifest、不触网;域重载与真实 junction 留手动验证清单(LINK:282-291)并记录已执行。

依赖/阻塞:横向依赖 LINK-2..6(测谁需谁先存在);接口抽象应在 LINK-3/4 实现时同步引入(TDD 更佳)。

---

### 本章顺序与工作量

推荐顺序严格等于设计实施顺序(LINK:293-301),因为存在硬依赖链:

1. LINK-0 探针(硬前置,阻塞全章)— S(约 0.5-1 人天,主要是搭空工程 + 两版本各跑一次 + 记录结论;需一次人工 git tag/push)
2. LINK-1 包骨架 — S(约 0.5 人天)
3. LINK-2 Registry schema + 白名单校验 — M(约 2-3 人天,校验规则多且需严谨)
4. LINK-3 UPM 队列 + 域重载恢复 — L(约 4-6 人天,本章最高风险,跨域恢复与原子日志易错)
5. LINK-4 事务复制 + Junction — L(约 3-4 人天,P/Invoke junction + 六步事务 + 回滚)
6. LINK-5 Fragment/.gitignore 托管区块 — M(约 2 人天)
7. LINK-6 Project Settings 面板 — M(约 2-3 人天,集成层)
8. LINK-7 工具迁移 — M(约 2 人天,含 fragments 补齐、tag、README 修正;含人工 git)
9. LINK-8 测试套件 — M(约 2-3 人天,但应与 LINK-2..6 并行用 TDD 滚动落地,接口抽象前置)

内部可并行/合并:LINK-4 与 LINK-5 同属一次 `Sync Agents`,可由同一开发者连做;LINK-8 不应留到最后,接口抽象(IUpmClient/IGitRefResolver/IJunctionManager)在 LINK-3/4 实现时就引入并随手补测试。

跨章依赖:LINK-7 的工具迁移与 README 修正与本仓库其它「工具完善/发布」工作流相关(三/四个工具的 ready 化、tag、版本);所有 git tag/push 属外循环、交人执行(对照 cross-project 文档第 4-5 节);LINK-2 的 Registry schema 需为未来 editor-core infra 包(enhancements-design:168-171,无 Skill、defaultEnabled=false、包间 UPM 依赖与安装顺序)预留扩展位,但首版不实现 —— 这条与「dev-tools-enhancements / 共享 core」工作流有接口约束。整章总量约 18-26 人天,LINK-3/4 是关键路径。

## 5. editor-debug 加固与人机工程

> 工作流 id: WS4 | 依赖: 无 | 工作量: L(约 1.5-2 人天;ED-DESCRIBE 0.5 + ED-4 0.5 + ED-6 0.5-1)

本章覆盖 editor-debug 包的剩余加固与一致性工作。撰写前已逐文件核实当前实现,与设计文档(docs/superpowers/specs/2026-06-13-dev-tools-enhancements-design.md)对照后修正了若干过时断言:设计文档把 `/batch`、`/console`、`/ping` 编辑器态都列为「待做(later/do-next)」,但代码里它们均已落地(EditorDebugMCP.cs:117-118、115-116、138-142);序列化去重(ED-2-SERIALIZE-DEDUP)也已通过 `ResultSerializer.ToPayload` 完成。因此本章把已完成项标注清楚后聚焦真正未做的三件事。

#### ED-4 只读模式 / 方法 denylist(opt-in,默认关)

问题:
- kill-switch 风格只覆盖 `/eval`。`EditorDebugMCP.cs:18` 仅有 `c_AllowEval = true` 一个开关,且 `/invoke`、`/batch`、`/eval` 一旦放行就能调用任意成员,包括破坏性 API。
- 写类反射路径完全无防护。`ReflectionInvoker.cs:86` 的 `DoSet`(set 字段/属性)与 `ReflectionInvoker.cs:111` 的 `DoCall`(调用任意方法)对成员名零过滤——agent 误调 `AssetDatabase.DeleteAsset`、`EditorApplication.Exit`、`FileUtil.DeleteFileOrDirectory`、`AssetDatabase.MoveAsset` 等会直接落地副作用。troubleshooting.md:52 自己也承认「默认开放所有反射调用」。
- `/batch`(EditorDebugMCP.cs:163-195)逐项调 `ReflectionInvoker.Execute`,同样无防护,放大了 fan-out 误伤面。
- 设计文档(ENH:60、169)把 ED-4 定位为「防御纵深、非关键漏洞(仅绑 127.0.0.1)、denylist 天然不完整、opt-in 默认关、镜像 c_AllowEval」——本方案严格遵守这个定位,不做成强安全边界。

方案:
- 新增 `MethodGuard` 静态类(新建 `Editor/MethodGuard.cs`),持有两个开关与一个 denylist:
  - `c_ReadOnly`(默认 `false`):为 `true` 时拒绝一切 `kind=="set"` 与 `kind=="call"`,只放行 `get`/`index`。这是最简单、零漏的「只读总闸」,语义对 agent 清晰(「我现在只读」)。
  - `c_EnforceDenylist`(默认 `false`):为 `true` 时,对 `call`/`set` 的目标成员名按 denylist 通配匹配(`Delete*`/`Destroy*`/`Save*`/`Exit`/`Quit`/`MoveAsset`/`ImportAsset`/`RequestScriptCompilation`/`DeleteFileOrDirectory`/`Refresh` 等,见设计文档 ENH:169)拒绝。denylist 用前缀+精确名混合表(`HashSet<string>` 精确 + `string[]` 前缀),`MethodGuard.Check(kind, member)` 返回 `(bool allowed, string reason)`。
- 关键设计取舍:
  - denylist 只按「成员简单名」匹配,不解析声明类型——这是刻意的「够用即可」折中(设计文档 ENH:169 已接受)。坑:同名良性方法(如某类自己的 `Save`)会被误杀,但因为是 opt-in 且面向「autonomous agent 跑批前临时收紧」的场景,误杀可接受,文档须明说。
  - `c_ReadOnly` 与 denylist 正交:只读是「类别闸」,denylist 是「点名闸」,两者可独立开。只读优先级更高(先判只读再判 denylist)。
  - 拒绝路径必须复用现有错误信封:抛 `NotSupportedException`(与 `c_AllowEval=false` 时 EditorDebugMCP.cs:121 同款),让 `ErrorEnvelope`(EditorDebugMCP.cs:197)统一整形为 `{ok:false,error:{type:"System.NotSupportedException",message:...}}`,协议零改动。
  - 注入点必须在 `ExecuteStep`(ReflectionInvoker.cs:52)入口,而非 `DoSet`/`DoCall` 内部——这样链式 `steps` 的每一步、`/eval` 经 `ExecuteStep` 的每一步(EvalParser.cs:46)、`/batch` 的每个子请求都被同一道闸覆盖,无遗漏路径。坑:`/eval` 的 call 也走 `ExecuteStep`(EvalParser.cs:44-46),所以只读模式会连带禁掉 eval 里的方法调用,这是正确行为,但文档要写清。
- kill-switch 风格对齐 `c_AllowEval`:三个开关都是 `EditorDebugMCP`(或新 `MethodGuard`)里的 `private static readonly bool`,改源码重编译生效,不走运行时配置——与现有 `c_AllowEval`(EditorDebugMCP.cs:18)完全同构,不引入新的配置面/端点(避免把「关写」这件事本身变成可远程打开的攻击面)。

改动文件:
- 新建 `Packages/com.yoji.editor-debug/Editor/MethodGuard.cs`(开关 + denylist + `Check`)。
- 修改 `Packages/com.yoji.editor-debug/Editor/ReflectionInvoker.cs`:`ExecuteStep`(:52)开头插入 `MethodGuard.Check`,被拒则抛 `NotSupportedException`。
- 修改 `Packages/com.yoji.editor-debug/Agent~/skills/unity-editor-debug-mcp/references/troubleshooting.md`:第 51-53 行的安全说明段补 `c_ReadOnly`/`c_EnforceDenylist` 两个开关及默认值(对齐已有 `c_AllowEval` 那条:54)。
- 修改 `Packages/com.yoji.editor-debug/Agent~/skills/unity-editor-debug-mcp/references/protocol.md`:`/invoke` 段补一句「被 guard 拒绝时返 NotSupportedException」。
- 新建 `Packages/com.yoji.editor-debug/Tests/Editor/MethodGuardTests.cs`(只读放行 get/拒 set/call;denylist 命中前缀与精确名;两开关关闭时全放行)。

步骤:
1. 写 `MethodGuard.cs`:两 bool 开关 + denylist 表 + `Check(kind, member) -> (allowed, reason)`,默认两开关全 `false`(行为与现状完全一致,纯加法)。
2. 在 `ReflectionInvoker.ExecuteStep`(:52)入口调 `MethodGuard.Check`,被拒抛 `NotSupportedException(reason)`。
3. 写 `MethodGuardTests.cs` 覆盖:默认全放行回归、只读拒 set/call 放行 get、denylist 前缀(`Delete*`)与精确(`Exit`)命中、白名单同名良性方法被误杀的已知行为(断言文档化)。
4. 跑 EditMode 测试套件确认现有 `ReflectionInvokerTests`/`EvalParserTests` 全绿(默认关时零回归)。
5. 同步 troubleshooting.md + protocol.md 开关说明。

验收:
- 默认配置下,既有全部 EditMode 测试与 e2e 全绿(证明纯加法、零回归)。
- 手动把 `c_ReadOnly=true` 重编译后,`/invoke` 一个 `kind=call`/`kind=set` 返 `ok:false` 且 `error.type=="System.NotSupportedException"`;`kind=get`/`index` 仍正常。
- `c_EnforceDenylist=true` 时,call `AssetDatabase.DeleteAsset` 被拒、call `AssetDatabase.FindAssets`(良性)放行;`MethodGuardTests` 覆盖前缀与精确两类命中。
- `/batch` 内含被拒子请求时,该项 `ok:false`、其余项不受影响(沿用 EditorDebugMCP.cs:182-185 的 per-item 容错)。

依赖/阻塞:
- 无前置硬依赖,自包含,可独立先做。与 ED-DESCRIBE/ED-6 无耦合。
- 与 WS-ARCH(editor-core 抽取)弱相关:guard 属 per-tool 业务逻辑,设计文档 ENH:126 明确 `ReflectionInvoker` 不进 core,故 guard 也留在 editor-debug,抽 core 不影响本项。

#### ED-DESCRIBE 对齐 describe 与 invoke 的成员枚举(base-walk 一致性)

问题:
- describe 与 invoke 对「同一类型能看到哪些成员」给出不一致的答案,这是直接误导 agent 的人机工程 bug:agent 先 `/describe` 看清单、再据此 `/invoke`,两者枚举口径必须一致。
- `DescribeHandler.Describe`(DescribeHandler.cs:21)用 `type.GetMethods(k_All)`、`:23` 用 `GetProperties(k_All)`、`:28` 用 `GetFields(k_All)`,其中 `k_All`(DescribeHandler.cs:11-12)含 `NonPublic` 但**不含** `DeclaredOnly`。.NET 反射语义:`GetMethods` 默认沿继承链返回**公有**成员,但对**非公有**(private/internal)成员**只返回声明类型自身的**,不返回基类的——所以 describe 会漏掉从基类继承来的 internal/private 方法。
- `ReflectionInvoker` 恰恰相反:`DoGet`(:71-82)、`DoSet`(:89-108)、`DoCall`(:114-116)都显式 `for (var t = type; t != null; t = t.BaseType)` 配 `DeclaredOnly` 手动 base-walk,**能**调到基类的非公有成员。
- 净后果:一个继承自 internal 基类的方法,invoke 调得到,describe 列不出——agent 看不到却能用,或反过来不敢用。这正是设计文档隐含的「describe 与 invoke 成员枚举不一致」缺口(条目 ED-DESCRIBE-OVERLOAD-COLLISION)。当前 `DescribeHandlerTests.cs`(:10-39)没有覆盖继承非公有成员,所以这个不一致没被测试网住。

方案:
- 把 describe 的成员枚举改成与 `ReflectionInvoker` 同款的 base-walk:沿 `BaseType` 链、每层 `k_All | DeclaredOnly` 收集 methods/properties/fields,跨层按「成员标识」去重(method 按 `Name + 参数类型签名`,property/field 按 `Name`,因为派生类 `new`/`override` 会同名遮蔽)。
- 抽一个共享的枚举辅助,消除 describe 与 invoke 各写一份 base-walk 的发散风险:
  - 方案 A(推荐):在 `ReflectionInvoker` 暴露 `internal static IEnumerable<MethodInfo> WalkMethods(Type, string member)` / `WalkMembers(Type)`,describe 复用。但 invoke 的 walk 是「按 member 名找候选」,describe 是「全量枚举」,语义不同,强行共用会拧巴。
  - 方案 B(更干净):新建 `Editor/MemberWalker.cs`,提供 `AllMethods(Type)`/`AllProperties(Type)`/`AllFields(Type)`(全量、base-walk、去重)与 `MethodsNamed(Type, name)`(按名,供 invoke 用)。`ReflectionInvoker.DoCall`(:114-116)改调 `MemberWalker.MethodsNamed`,`DescribeHandler` 改调 `MemberWalker.All*`。两者从此共用同一套 base-walk + 去重语义,根除发散。选 B。
- 去重关键坑:
  - method 去重键必须含参数签名,否则会把不同重载误并;但 `new`/`override` 遮蔽的同签名方法应只保留最派生那个(walk 从派生类往基类走,先见即派生,后见即跳过)。
  - property 索引器(`Item`)同名不同参,去重键要含索引参数类型,否则多索引器只剩一个。
  - 静态成员也要枚举(`k_All` 含 `Static`),base-walk 对静态同样适用。
- describe 输出向后兼容:仍是 `Methods/Properties/Fields` 三个 `JArray of 签名字符串`(DescribeHandler.cs:17-31 结构不变),只是数组内容因补回继承非公有成员而**变多**——既有断言(DescribeHandlerTests.cs:15 `Count > 0`、:32 含 `Min(`)不会因此失败。

改动文件:
- 新建 `Packages/com.yoji.editor-debug/Editor/MemberWalker.cs`(`AllMethods`/`AllProperties`/`AllFields`/`MethodsNamed`,base-walk + 去重)。
- 修改 `Packages/com.yoji.editor-debug/Editor/DescribeHandler.cs`:`:21/:23/:28` 三处 `type.Get*(k_All)` 改调 `MemberWalker.All*(type)`。
- 修改 `Packages/com.yoji.editor-debug/Editor/ReflectionInvoker.cs`:`DoCall`(:113-116)的候选收集改调 `MemberWalker.MethodsNamed`;`DoGet`/`DoSet` 的 base-walk 视情况也收敛到 walker(保持行为等价,纯重构)。
- 修改 `Packages/com.yoji.editor-debug/Tests/Editor/DescribeHandlerTests.cs`:加一个「继承自非公有基类的成员被 describe 列出」回归用例。
- 新建/扩 `Packages/com.yoji.editor-debug/Tests/Editor/MemberWalkerTests.cs`:去重(override 不双列)、索引器同名不同参不并、继承非公有成员可见。

步骤:
1. 写 `MemberWalker.cs`,把 `ReflectionInvoker` 现有 base-walk 逻辑(:71-82、:89-108、:114-116)提炼为全量与按名两套 API,加跨层去重。
2. `DescribeHandler` 三处枚举改调 walker(:21/:23/:28)。
3. `ReflectionInvoker.DoCall` 候选收集改调 `MemberWalker.MethodsNamed`,确认 `DescribeHandler.Signature`(:34)仍可用于歧义错误消息。
4. 写 `MemberWalkerTests` + 扩 `DescribeHandlerTests` 的继承用例。
5. 跑 EditMode 套件 + e2e case 07/08(protocol describe,run-e2e.py:146-159)确认 describe 输出不破坏既有断言。

验收:
- 构造一个继承自 internal 基类、基类有 internal 方法的测试类型,`describe` 输出的 `Methods` 含该继承方法,且对同一类型 `invoke kind=call` 该方法成功——两端口径一致(这是本项的核心验收标准:describe 列得出 == invoke 调得到)。
- `MemberWalkerTests` 证明 override 方法只出现一次、多索引器不被去重并掉。
- 既有 `DescribeHandlerTests`(:10-39)与 `ReflectionInvokerTests` 全绿(重构等价)。

依赖/阻塞:
- 与 ED-4 无耦合,可并行。
- 若 ED-4 也注入 `ExecuteStep`,两项都改 `ReflectionInvoker.cs` 但触点不同(ED-4 在 ExecuteStep 入口、本项在 DoCall/DoGet/DoSet 候选收集),合并时注意 diff 不冲突;建议先做本项重构再叠 ED-4 的 guard(在已收敛的入口上加闸更干净)。

#### ED-6 /describe 成员过滤 + 多类型批量 describe

问题:
- describe 一次只能描述单类型且全量 dump。`DescribeHandler.Describe`(DescribeHandler.cs:14)签名 `Describe(string typeName)`,`EditorDebugMCP.Route`(:113-114)只取 `req.Value<string>("type")` 单值;`client.py:126` 也只传单 `type`。对成员众多的大类型(如 `EditorApplication`、`AssetDatabase`),全量返回是 token/延迟浪费——设计文档 ENH:62 把 ED-6 定位为「discovery 阶段的 token/延迟优化」。
- 没有成员名过滤:agent 想确认「这类型有没有叫 `Find*` 的方法」必须拉全量自己 grep。
- 没有批量:fan-out 描述多个类型要 N 次往返。设计文档 ENH:62 指出「若 ED-2 落地,batch-describe 几乎免费附带」——而 ED-2(/batch)确已落地(EditorDebugMCP.cs:117),故本项现在成本更低。
- protocol.md 中 `/describe` 无任何文档段(已核实:Grep `/describe` 在 protocol.md 零命中),说明该端点的请求形状从未正式化,扩参数时无破坏面。

方案:
- 扩 `/describe` 请求形状(向后兼容,旧 `{type:"X"}` 仍工作):
  - `{type?:string, types?:string[], filter?:string, kinds?:string[]}`。
  - `type` 与 `types` 二选一(都给则合并去重);`types` 走多类型批量。
  - `filter`:成员名子串/通配过滤(如 `Find` 只回名字含 Find 的成员),大小写不敏感。
  - `kinds`:`["methods","properties","fields"]` 子集,缺省全要——让 agent 能只问方法、省掉属性字段。
- `DescribeHandler` 改造:
  - `Describe(string typeName)` 保留为薄包装,新增 `Describe(JObject req)` 读上述参数,内部对每个类型调一个 `DescribeOne(Type, filter, kinds)`。
  - 多类型时返回 `{types:[{FullName,Assembly,Methods,...}, ...]}`;单类型保持现有扁平 `{FullName,Assembly,Methods,Properties,Fields}` 形状(向后兼容,既有 e2e case 07/08 断言 `result.FullName`、`result.Methods` 不破)。坑:单/多类型返回形状不同会让客户端分支,但「单类型扁平」是既有契约不能动;折中是 `types` 非空才走数组形状,文档写清两种形状的触发条件。
  - 成员枚举必须复用 ED-DESCRIBE 的 `MemberWalker`(base-walk 一致),不能在这里另写一份枚举,否则 filter 又会基于不一致的成员集。
- 批量上限:与 `/batch` 的 `>64 拒`(EditorDebugMCP.cs:167-168)对齐,`types.Count > 64` 拒;单类型全量已可能逼近 4MB(EditorDebugMCP.cs:219 的 `k_MaxBodyBytes`),多类型更要靠 `filter`/`kinds` 收窄,文档建议批量描述务必配 filter。
- 主线程:describe 经 `RunOnMain`(EditorDebugMCP.cs:114),多类型在**单次**主线程跳内循环(类比 `/batch` 的 EditorDebugMCP.cs:171-188),不要每类型一跳。
- client.py:扩 `cmd_describe`(client.py:124-126)支持 `--types a b c`、`--filter`、`--kinds`。

改动文件:
- 修改 `Packages/com.yoji.editor-debug/Editor/DescribeHandler.cs`:加 `Describe(JObject)`、`DescribeOne(Type, filter, kinds)`,过滤逻辑;枚举改走 `MemberWalker`(依赖 ED-DESCRIBE)。
- 修改 `Packages/com.yoji.editor-debug/Editor/EditorDebugMCP.cs`:`Route` 的 `/describe` 分支(:113-114)从 `DescribeHandler.Describe(type-string)` 改为 `DescribeHandler.Describe(req)`。
- 修改 `Packages/com.yoji.editor-debug/Agent~/skills/unity-editor-debug-mcp/client.py`:`cmd_describe`(:124-126)+ argparse 加 `--types/--filter/--kinds`。
- 修改 `Packages/com.yoji.editor-debug/Agent~/skills/unity-editor-debug-mcp/references/protocol.md`:新增 `/describe` 端点段(请求形状、单/多类型两种响应形状、filter/kinds 语义)。
- 修改 `Packages/com.yoji.editor-debug/Tests/Editor/DescribeHandlerTests.cs`:加 filter、kinds 子集、多类型用例。
- 修改 `Packages/com.yoji.editor-debug/Agent~/skills/unity-editor-debug-mcp/references/run-e2e.py`:加多类型/filter 的 e2e case。

步骤:
1. 先完成 ED-DESCRIBE(MemberWalker 落地),本项直接消费它做过滤前的成员集。
2. `DescribeHandler` 加 `Describe(JObject)` + `DescribeOne` + filter/kinds 实现;保留单类型扁平形状。
3. `EditorDebugMCP.Route`(:113)切到 `Describe(req)`。
4. 扩 `client.py` cmd_describe 与 argparse。
5. 扩 EditMode 测试(filter/kinds/多类型)+ e2e case;补 protocol.md `/describe` 段。
6. 跑既有 e2e case 07/08 确认单类型扁平形状未破。

验收:
- 旧请求 `{type:"UnityEditor.Selection"}` 响应形状与字段与现状逐字一致(既有 e2e case 07 通过 == 向后兼容证明)。
- `{type:"UnityEditor.AssetDatabase", filter:"Find"}` 只回名字含 Find 的成员;`{type:..., kinds:["methods"]}` 不含 Properties/Fields 键。
- `{types:["UnityEngine.Mathf","UnityEditor.Selection"]}` 单跳返回 `types:[...]` 两项,`elapsedMs` 体现单次主线程跳。
- `types.Count>64` 返 `ok:false`。
- 过滤后的成员集与 ED-DESCRIBE 的 base-walk 成员集一致(filter 不引入第二套枚举口径)。

依赖/阻塞:
- 硬依赖 ED-DESCRIBE(必须先有 `MemberWalker`,否则 filter 基于不一致成员集,等于把 bug 带进新功能)。
- 软受益于已落地的 `/batch` 模式(EditorDebugMCP.cs:171-188 的单跳循环范式可照抄)。

#### ED-2-SERIALIZE-DEDUP(已完成,记录现状)

问题(原条目假设):设计文档 ENH:115 与原始条目认为 `/invoke` 与 `/batch` 各写一份结果整形、需抽 `SerializeRaw` 去重。

现状核实:**已完成**。`ResultSerializer.ToPayload`(ResultSerializer.cs:39-46)即共享整形 helper,处理 `VoidResult -> {void:true}` / 已是 `JToken` 透传 / 否则 `ToJson` 三态。`/invoke` 经 `RunOnMain`(EditorDebugMCP.cs:150)调它,`/batch` 在 EditorDebugMCP.cs:178 调同一个它。两路径不发散,且 `ToPayload` 注释(ResultSerializer.cs:36-38)明确写「/invoke 与 /batch 共用」。故原 partial 状态应升级为 done,本章不再安排工作,仅在文档化时把它从「待办」勾掉,避免后续误重复实现。

#### ED-EVAL-LIMITATIONS(确认维持 skip)

问题(原条目):`/eval` 仅支持 `Type.Member(args)` 链(EvalParser.cs:10-12 注释、:40-45 仅 get/call 两 kind),不支持 lambda/new/typeof/运算符;api-cookbook 与 SKILL.md:22 已如实描述为「链式属性/方法访问」。

结论:**维持 skip,不上 Roslyn**(设计文档 ENH:175 非目标、ENH:40 评分 2/5)。理由已被设计文档充分论证:Roslyn 是多 MB 重依赖 + 编辑器 assembly-resolution 痛点 + 域重载重入 + 把调试端口变成任意代码执行面,而 invoke-chain 已覆盖约 80% 场景。当前 `/eval` 的限制已在 client.py:153、troubleshooting.md:46、api-cookbook 中如实标注,无「死参数诚实化」遗留(ED-3 的 `--usings` 清理属 test-runner/其它章范畴,本包 EvalParser 无该参数)。本章对 ED-EVAL 不安排实现工作。

### 本章顺序与工作量

推荐内部顺序(按依赖):
1. **ED-DESCRIBE(base-walk 对齐)** — 先做,因为它是 ED-6 的硬前置,且独立修一个 agent 误导 bug。约 0.5 人天(MemberWalker + 重构 + 测试)。
2. **ED-4(只读/denylist)** — 可与 ED-DESCRIBE 并行;若串行则排其后(在已收敛的 `ExecuteStep` 入口上加闸更干净)。约 0.5 人天。
3. **ED-6(describe 过滤+批量)** — 必须在 ED-DESCRIBE 之后。约 0.5–1 人天(含 client.py、protocol.md、e2e)。

工作量粗估:全章 ~1.5–2 人天。ED-2-SERIALIZE-DEDUP 与 ED-EVAL 不计工时(前者已完成、后者 skip)。

跨章依赖:
- 与 WS-ARCH(editor-core 抽取)解耦:本章所有新增(`MethodGuard`、`MemberWalker`、describe 扩展)都是 per-tool 业务逻辑,设计文档 ENH:126 明确 `ReflectionInvoker`/信封整形不进 core,故抽 core 不阻塞本章、本章也不增加 core 的抽取面。
- 与 test-runner 各章(WS1-3)无代码耦合,仅共享「kill-switch 默认关、opt-in 加固」的设计哲学(对齐 c_AllowEval 风格)。
- 三项均为纯加法/向后兼容,可在 editor-debug 当前 21891 端口在线时增量验证,不需要 u3d-ai-linker 构建完成作为前置。

## 6. editor-core 抽取(ARCH-1,共享基建去重)

> 工作流 id: WS2 | 依赖: WS6(u3d-ai-linker)、WS7-TR1(PlayMode 路径) | 工作量: L(纯编码约 2-2.5 人天:决策+dispatcher ~1 天 / host+recompile 抽取 ~1-1.5 天 / registry 建模决策 ~0.5 天且代码计入 linker),外加两处需用户拍板的决策门

### 章节范围与现状核实

本章覆盖 ARCH-1 全部子项:UPM 同级依赖阻断(硬前置)、信封分歧守卫、dispatcher 抽取(ARCH-1a)、host+recompile 原语抽取(ARCH-1b)、Registry 建模(ARCH-1-registry-model)。

核实结论(全部带证据):

- **无 editor-core 包**:`Packages/com.yoji.editor-core/**` glob 无命中,仓库当前只有 editor-debug / test-runner / lua-device-debug 三包。
- **两工具无任何 com.yoji.* 依赖**:`editor-debug/package.json:7-9` 仅依赖 `com.unity.nuget.newtonsoft-json`;`test-runner/package.json:7-10` 依赖 test-framework + newtonsoft;均无 `com.yoji.*`。这意味着抽 core 后两包 package.json 必须新增依赖,而该依赖在 Git-URL 安装下不会被 UPM 自动拉取(见 ARCH-1-upm-sibling-dep-blocker)。
- **两份 Editor dispatcher 字节一致**:`editor-debug/Editor/MainThreadDispatcher.cs` 与 `test-runner/Editor/MainThreadDispatcher.cs` 仅 `namespace`(Yoji.EditorDebug vs Yoji.TestRunner)与 2 行注释(test-runner L11 多一句"未来抽 com.yoji.editor-core 时统一",editor-debug L45 有一行 doc)不同,逻辑、字段、`Install/Pump/Run` 完全相同。
- **lua dispatcher 是另一物种,不并入**:`lua-device-debug/Runtime/MainThreadDispatcher.cs` 位于 Runtime asmdef,是泛型 `Run<T>`、基于 `SynchronizationContext.Post`、带 16 任务队列上限(L10)、4 态生命周期(L81-84)、抛 `LuaDeviceDebugException`(429 QUEUE_FULL / 408 EXECUTION_TIMEOUT)。它跑在 shipped player 而非 `EditorApplication.update`,与 Editor 版本无任何公共可抽部分。
- **两份 RecompileHandler 共享脚手架、发散契约**:见 ARCH-1b 详述。

#### ARCH-1-upm-sibling-dep-blocker:Git-URL UPM 同级依赖不解析(core 抽取的硬前置)

**问题**
抽 editor-core 的前提是两工具能依赖它,但这条路被 UPM 的安装语义阻断。lesson `2026-06-13-git-url-upm-no-sibling-dependency-resolution.md` 已核实:当一个包以 `https://repo.git?path=/Packages/X` 安装时,UPM 不会自动拉取 X 在 package.json `dependencies` 里声明的同级 `com.foo.Y`(自动解析只覆盖 registry 包,不覆盖任意 git 子包),消费工程的 `manifest.json` 必须显式追加 Y 自己的 git URL。证据:`editor-debug/package.json:7-9`、`test-runner/package.json:7-10` 当前都没有 `com.yoji.*` 依赖,一旦加入 `com.yoji.editor-core`,所有经 Git-URL 安装这两个工具的工程都会在 manifest 未同步前静默编译失败(找不到 core asmdef)。这把"透明的内部重构"变成了"改变安装契约的破坏性变更"。同时 linker 设计(LINK:126)已承认"同一 Git 仓库的多个 UPM 子包会被 Unity 重复获取"是 monorepo 已知成本——再加一个 core 子包会放大该成本。

**方案**
这是决策前置而非编码任务:在翻动任何 package.json 依赖前,先在三个缓解方案中定调,落到 linker 的安装编排逻辑:

- **方案 A(推荐,linker 编排注入)**:core 不进两工具的 `dependencies`,改由 u3d-ai-linker 在 `Install All` 时把 `com.yoji.editor-core` 的 git URL 作为独立条目串行注入消费工程 manifest(linker 本就用 `Client.Add` 串行装包,见 LINK:98、146-149)。这样 core 成为 linker 显式安装的一等条目,而非工具的隐式传递依赖——绕开 UPM 不解析同级依赖的限制。代价:手动从 Git URL 装单个工具(不经 linker)的用户必须自行加 core URL,需在 SKILL.md / README 写明。
- **方案 B(隐式携带 + 文档兜底)**:仍在两工具 package.json 写 `com.yoji.editor-core` 依赖(语义正确、面向未来真发 registry 的情形),但明确文档化"Git-URL 安装下该依赖不自动解析,必须手动加 core 的 git URL",linker 把 core 当 out-of-band 依赖一并装。这是 OQ3(LINK:168)里"暂由工具 package.json 隐式携带 core 依赖、Registry 视其为 out-of-band"的方向。
- **方案 C(放弃抽取)**:lesson 末句已给出反向论点——被注释明确标注的字节级重复(dispatcher)可能比跨包安装耦合更便宜,尤其当抽取的其它驱动力(TR-1c reattach 需要干净 host hook)已蒸发时。若 TR-1a(DisableDomainReload)被采纳使 TR-1 不再触碰 host 外壳,则 ARCH-1b 的紧迫性下降,此时只做零成本的 dispatcher 合并(ARCH-1a)、host/recompile 保持原地双份。

取舍坑:方案 A 让工具的 semver tag 与回滚不再完全独立(工具只能与其 core 一起回滚),这与 linker"每工具独立封装、独立声明版本"原则(LINK:12)有张力,属 OQ6(ENH:171)需用户拍板的让步。

**改动文件**
- 修改:`docs/superpowers/specs/2026-06-13-dev-tools-enhancements-design.md`(把选定方案从 OQ3 升级为决策记录)。
- (方案 A 选定后)修改:`Packages/com.yoji.u3d-ai-linker/` 安装编排代码 + `Registry/stable.json` / `Registry/dev.json`(把 editor-core 列为独立安装条目)。
- (方案 B 选定后)修改:`editor-debug/package.json`、`test-runner/package.json`(加 `com.yoji.editor-core` 依赖)+ 两包 SKILL.md / repo README 写明手动加 URL。

**步骤**
1. 把本前置作为 ARCH-1 任何编码动作的 gate,在 WS 看板上标为"决策未定则 ARCH-1b 不开工"。
2. 与用户就 OQ3 + OQ6 拍板缓解方案(A/B/C)。
3. 若选 A/B:确认 editor-core 将拥有独立 git tag 前缀(如 `editor-core-vX.Y.Z`,对齐 LINK:134-140 的 Tag 命名),并在 linker 安装顺序里把 core 排在两工具之前(依赖先于消费者)。
4. 把结论写回设计文档,解除 ARCH-1a/1b 的阻塞。

**验收**
- 设计文档中 OQ3 由开放问题变为带选项理由的决策记录,明确"Git-URL 下 core 依赖如何被装"。
- 选定方案有可执行的安装顺序描述(core 先于工具)。
- 若选 A:linker Registry 增加 editor-core 条目且 `Install All` 顺序把它排在工具前(可在 dry-run 日志验证)。
- 若选 C:文档明确"不抽 core,仅做 dispatcher 合并",ARCH-1b 关闭。

**依赖/阻塞**
- 阻塞 ARCH-1a 的"删两份副本改依赖"收尾步骤、阻塞 ARCH-1b 全部、阻塞 ARCH-1-registry-model。
- 与 WS(u3d-ai-linker 构建)强耦合:方案 A 需要 linker 已能编排安装;linker 尚未构建,故方案 A 落地依赖 linker workstream 先到达"能串行装包"的里程碑。
- 与 TR-1 路径相关:若 TR-1a 被采纳(TR-1 不碰 host),ARCH-1b 的 forcing function 消失,倾向方案 C 仅合并 dispatcher。

#### ARCH-1-envelope-guard:抽取时保留刻意的信封分歧(恒 200 vs 真状态码)

**问题**
两工具的 HTTP 响应契约是刻意发散的,抽 core 时若把信封整形并进 core 会破坏协议。证据:
- editor-debug 文件头 `EditorDebugMCP.cs:13` 明示"HTTP 状态码恒 200,错误走 body.error";其 `WriteResponse`(L216-238)无条件 `ctx.Response.StatusCode = 200`,信封是 flat `{ok, elapsedMs, result|error}`(L197-202)。
- test-runner 文件头 `TestRunnerMCP.cs:14-16` 明示"真 HTTP 状态码 + 每端点独立 JSON 形状(与 editor-debug 恒 200 flat envelope 不同)";其 `WriteResponse`(L245-257)接受 `int status` 参数并写真实码,`Route` 返 `(int status, JObject body)`,各端点返 200/202/400/404/409/500(L130-137、L166、L176)。
- recompile 同一分歧:`editor-debug/RecompileHandler.cs:31` 返 `JObject` 信封;`test-runner/RecompileHandler.cs:33` 返 `(int status, JObject body)`,busy 时返 409(L52),其文件头注释 L11-13 明确标注三处有意改动并写"未来抽 com.yoji.editor-core 时需先统一响应契约"。
设计文档 ENH:58、126、179 均把"不把响应信封并入 editor-core"列为硬约束("恒 200 flat 与真状态码是刻意的协议契约")。风险:抽 host 外壳时,若让 core 的 `WriteResponse` 决定状态码或信封形状,会强行统一两套契约,破坏 agent 客户端(editor-debug 客户端从不看状态码、test-runner 客户端依赖状态码区分 202/409)。

**方案**
把信封/状态码作为 host 抽取的显式边界,在 `LoopbackHttpHost` 的 handler 契约上做到信封无关:

- core 的 host 入参 handler delegate 签名定为返回 `(int status, byte[] body)` 或 `(int status, string contentType, string body)` 这类**中性传输三元组**,由各工具的 `Route` 自己决定 status 与 body 字符串。core 只负责"把这个 status 和这段 bytes 写回 ctx",绝不构造 JObject、绝不默认 200。
- editor-debug 适配:其 `Route` 全程返 `(200, envelope.ToString())`(状态码恒 200 由工具侧而非 core 钉死),保持 `ErrorEnvelope`/`ErrorJson`/`ToPayload` 整形 helper 全部留在 editor-debug。
- test-runner 适配:其 `Route` 已经返 `(int, JObject)`,只需在边界把 JObject 转 string 交给 core,真实状态码透传。
- 4MB 截断逻辑(`EditorDebugMCP.cs:219-225`)是 editor-debug 特有的信封改写(往 envelope 注 `__truncated`),**不进 core**——core 至多提供一个中性的"超大 body 拒绝/告警"原语,改写信封的动作留在工具侧。

取舍坑:诱惑是"既然都在写 bytes,不如让 core 统一加 `application/json; charset=utf-8` 头"——这个可以进 core(两侧 contentType 一致,见 L229 vs L251),但**状态码与 body 形状必须留在工具侧**。判据:凡是"两个文件头注释明确声明为有意分歧"的,一律不进 core。

**改动文件**
- 新建:`Packages/com.yoji.editor-core/Editor/LoopbackHttpHost.cs`(handler delegate 用中性传输三元组,不含信封语义)。
- 修改:`EditorDebugMCP.cs`(Route 适配新 handler 签名,保留 flat 200 信封 + 4MB 改写 + 全部 envelope helper)。
- 修改:`TestRunnerMCP.cs`(Route 适配,保留真状态码 + per-endpoint 形状 + `Err` helper)。

**步骤**
1. 定义 core handler delegate 的中性传输契约,评审签名确认不泄露任何信封语义。
2. 让 editor-debug 在 Route 边界把现有 JObject 信封序列化为 `(200, string)`,移除对 core WriteResponse 设状态码的任何依赖。
3. 让 test-runner 在边界把 `(int, JObject)` 转 `(int, string)`,真实码透传。
4. 全套两包 EditMode + e2e 回归,断言响应状态码与 body 形状逐字节不变。

**验收**
- core 代码中 grep 不到 `["ok"]`/`["error"]`/`StatusCode = 200` 等信封或状态码字面量(信封 0 泄漏)。
- editor-debug e2e:所有响应仍恒 200,错误仍走 `body.error`,4MB `__truncated` 行为不变。
- test-runner e2e:`/run-tests` 仍返 202、busy 仍返 409、未知端点仍返 404、bad JSON 仍返 400。
- 两包 `WriteResponse` 删除/收敛后,对外可观测响应与抽取前 diff 为空。

**依赖/阻塞**
- 是 ARCH-1b 的设计约束(host 抽取必须遵守本守卫),与 ARCH-1b 同步落地。
- 不阻塞 ARCH-1a(dispatcher 不涉及信封)。

#### ARCH-1a:MainThreadDispatcher 抽入 com.yoji.editor-core(零风险)

**问题**
`editor-debug/Editor/MainThreadDispatcher.cs` 与 `test-runner/Editor/MainThreadDispatcher.cs` 逻辑字节一致(仅 namespace + 2 行注释不同,已逐行核实),两份并行维护。这是并发竞态最微妙的代码(`ConcurrentQueue` + `ManualResetEventSlim` + 主线程重入判断 + `ExceptionDispatchInfo` 原栈重抛),改一处忘改另一处会静默腐化。设计文档把它列为 ENH:30、140 的 do-now 零风险独立小提交。lua 那份(Runtime,SynchronizationContext)不并入(见章节现状核实)。

**方案**
逐字移入 core、改 namespace、删两份副本、两边加 using。这是纯机械搬运:
- 在 core 建 `Yoji.EditorCore` namespace 下的 `MainThreadDispatcher`(`internal static` 改为 `public static`,因为要跨 asmdef 被两工具调用——当前是 internal,跨包后必须 public 或经 InternalsVisibleTo;两包 InternalsVisibleTo 当前只对自身 test asmdef(`AssemblyInfo.cs:3` 两处),不存在跨工具 internal 访问,故抽 core 不与任何现有 internal 边界冲突)。
- 两工具删除本地副本,asmdef 加 core 引用,源码加 `using Yoji.EditorCore;`。
- 调用点零改动:两工具都只调 `MainThreadDispatcher.Run(work, out elapsed)` / `Run(work, out elapsed, timeoutMs)`,签名不变。

坑:`public` 暴露面要克制——只 `Run` 公开,内部 `Job`/`s_Queue`/`Pump`/`Install` 保持 private。`[InitializeOnLoadMethod]` 的 `Install` 在 core asmdef 里仍会被 Unity 自动调用(它扫所有 Editor asmdef),无需工具侧触发。

**改动文件**
- 新建:`Packages/com.yoji.editor-core/package.json`(name `com.yoji.editor-core`,unity `2022.3`,仅依赖 `com.unity.nuget.newtonsoft-json`,Editor-only)。
- 新建:`Packages/com.yoji.editor-core/Editor/Yoji.EditorCore.Editor.asmdef`(`includePlatforms:["Editor"]`,references 空,precompiledReferences 视是否用 newtonsoft 而定——dispatcher 本身不用 JSON,故可空)。
- 新建:`Packages/com.yoji.editor-core/Editor/MainThreadDispatcher.cs`。
- 删除:`editor-debug/Editor/MainThreadDispatcher.cs`、`test-runner/Editor/MainThreadDispatcher.cs`。
- 修改:`editor-debug/Editor/Yoji.EditorDebug.Editor.asmdef`(references 加 `Yoji.EditorCore.Editor`)、`test-runner/Editor/Yoji.TestRunner.Editor.asmdef`(references 数组追加 core)。
- 修改:两工具 package.json(依赖处理见 ARCH-1-upm-sibling-dep-blocker 选定方案)。

**步骤**
1. 建 editor-core 包骨架(package.json + asmdef),先只放 dispatcher。
2. 逐字搬运 dispatcher,`Run` 改 public,改 namespace 为 `Yoji.EditorCore`。
3. 两工具 asmdef 加 core reference,源码加 using,删本地副本。
4. 按 ARCH-1-upm-sibling-dep-blocker 的缓解方案处理 package.json / manifest / linker 注入。
5. 两包 EditMode + e2e 全套回归。

**验收**
- 仓库内 `MainThreadDispatcher.cs` 只剩 core 一份(editor-debug/test-runner 各删除,lua Runtime 那份保留)。
- 两工具编译通过,`/invoke`、`/run-tests`、`/recompile` 等所有走主线程跳的端点 e2e 行为不变。
- 在一个真实 Git-URL 安装工程(或模拟 manifest)验证:按选定缓解方案,core 能被解析,两工具不报 missing assembly。

**依赖/阻塞**
- package.json 依赖追加 + manifest/linker 注入步骤被 ARCH-1-upm-sibling-dep-blocker 阻塞;纯代码搬运部分不阻塞(可先在本地 Packages/ 内联开发,本地同仓子包默认可解析)。
- 不依赖 ARCH-1b、ARCH-1-envelope-guard。

#### ARCH-1b:抽 LoopbackHttpHost + RecompilePrimitive(留 on-start hook)

**问题**
两工具的 HTTP host 外壳与 recompile 编译事件脚手架近乎逐行复制,约 150-200 行并行维护。核实:
- **host 外壳**:`EditorDebugMCP.Start`(L34-64)与 `TestRunnerMCP.Start`(L38-72)共享同一结构——遍历 `k_Ports`、`new HttpListener` + `Prefixes.Add("http://127.0.0.1:"+port+"/")` + `listener.Start()`、失败 try 下一个、全占则 LogError、起后台 `Loop` 线程。`Loop`(ED L75-85 / TR L82-92)、`Stop`(ED L66-73 / TR L74-80,都先 `RecompileHandler.WaitForPendingResponse(2000)`)、`WriteResponse` 字节写出(ED L216-238 / TR L245-257)高度同构。差异仅在:TR 在 Start 内多了 `JobStore`/`TestRunService` 初始化 + `EnsureRegistered()`(L46-48);ED 的 `WriteResponse` 恒 200 而 TR 带 status。
- **recompile 原语**:`editor-debug/RecompileHandler.cs` 与 `test-runner/RecompileHandler.cs` 共享:三 volatile flag(`s_CompilationStarted/Finished/HasErrors`)、`Install` 接 `compilationStarted/compilationFinished/assemblyCompilationFinished`(两文件 L19-31/L21-31 逐字相同)、`s_Pending` 计数、5s no-start + 180s finish 轮询(ED L60-63 / TR L62-66 逐字相同)、`WaitForPendingResponse`(ED L81-87 / TR L83-90 逐字相同)。差异仅在 `Run` 体:busy 判据(ED `isCompiling` / TR `!jobs.IsIdle || isCompiling`)、pre-compile 动作(ED 无 / TR `SetCompiling(true)+AssetDatabase.Refresh()`)、返回契约(ED `JObject` 信封 / TR `(int,JObject)` 含 409)、耗时精度(ED 1 位 / TR 2 位小数)。
设计文档 ENH:34、123-124、158 把这块定为 do-next、约 1-1.5 天含测试回归,且要求 host 暴露干净的 on-start hook 供 TR-1c reattach 挂入(避免把 reattach 焊进单体 Start,见 ENH:158 的"先浇混凝土再钻孔"论证)。

**方案**
抽两个原语,把可变部分做成注入点,差异留在工具侧:

- **`LoopbackHttpHost`**:拥有 `Start`(端口 fallback)/`Loop`/`Stop`/字节写出。入参 = `int[] ports` + `string serviceName` + handler delegate(返中性传输三元组,见 ARCH-1-envelope-guard)+ 一个 `Action onStarted`(on-start hook)。**关键设计**:`onStarted` 在 listener 成功绑定、`Loop` 线程起来后回调,test-runner 把 `EnsureRegistered()` 挂进去、未来 TR-1c 的 reattach 也挂这里——reattach 成为挂进稳定 host 的回调,而非焊死在 Start。`Stop` 前调 `WaitForPendingResponse` 的责任移交:host 暴露一个 `preStop` hook(或直接接受 RecompilePrimitive 引用),让 recompile 的 pending 等待在 Stop 前执行。`WriteResponse` 写真实 status(由 handler 返回决定)+ 固定 `application/json; charset=utf-8` 头;状态码不在 core 内硬编码(守卫见 ARCH-1-envelope-guard)。
- **`RecompilePrimitive`**:拥有三 volatile flag、`Install` 编译事件接线、`s_Pending`、5s/180s 轮询、`WaitForPendingResponse`。暴露 `Trigger(Action preCompile, Func<bool> busyPredicate)` 返回**中性结果** `{started, finished, hasErrors, elapsedMs}`(不含信封)。editor-debug 传 `preCompile=null` + `busyPredicate=() => EditorApplication.isCompiling`,把中性结果整形成 flat 信封;test-runner 传 `preCompile=() => { jobs.SetCompiling(true); AssetDatabase.Refresh(); }` + `busyPredicate=() => !jobs.IsIdle || EditorApplication.isCompiling`,把中性结果整形成 `(200, flat)` 并在 busy 时自造 `(409, ...)`。注意 test-runner 在无重载路径要 `jobs.SetCompiling(false)` 复位(TR L69),这个收尾留工具侧或作为 primitive 的 `onComplete` 回调。

坑:
- `[InitializeOnLoadMethod] Install` 在 core 里仍自动跑,但编译事件是全局单例——两工具共用同一个 core RecompilePrimitive 实例会让 flag 串台。**必须**让 RecompilePrimitive 是可实例化的(每工具 new 一个),或 flag 按调用方隔离;不能做成 core 里的 static 单例(当前两份各自 static 是天然隔离的,抽 core 不能丢掉这层隔离)。这是本子项最容易踩的竞态坑。
- `s_Pending`/`WaitForPendingResponse` 与 host 的 `Stop` 有时序耦合(Stop 前要等 pending 写完),抽开后要保证 host 能在 Stop 前回调到对应 primitive 的 wait。

**改动文件**
- 新建:`Packages/com.yoji.editor-core/Editor/LoopbackHttpHost.cs`、`Packages/com.yoji.editor-core/Editor/RecompilePrimitive.cs`。
- 修改:`EditorDebugMCP.cs`(Start/Loop/Stop/WriteResponse 委托给 host,`RecompileHandler` 改为薄整形层调 primitive)。
- 修改:`TestRunnerMCP.cs`(同上,`EnsureRegistered` 改挂 onStarted hook)。
- 修改/删除:两包 `RecompileHandler.cs`(收敛为调 primitive 的整形薄层,或删除并内联到 MCP 文件)。

**步骤**
1. 先抽 `RecompilePrimitive`(实例化、隔离 flag),两工具 RecompileHandler 改为整形薄层调它,e2e 验 `/recompile`(含 busy/409、无变更 5s 快返、有错 hasErrors)行为不变。
2. 再抽 `LoopbackHttpHost`,带 `onStarted` hook,两工具 Start/Loop/Stop/WriteResponse 委托给它。
3. test-runner 把 `EnsureRegistered()` 移进 onStarted 回调,确认域重载后重注册时序不变。
4. 遵守 ARCH-1-envelope-guard:状态码/信封形状全留工具侧。
5. 两包 EditMode + e2e 全套回归(6000.3.16f1)作为无回归门禁。

**验收**
- 两工具 Start/Loop/Stop/WriteResponse/recompile 脚手架代码量显著下降,core 各原语单一来源。
- recompile 三场景 e2e 不变:无变更(5s 内成功)、有编译错误(`hasErrors:true`)、busy(editor-debug 走信封 conflict / test-runner 返 409)。
- 两工具各自 new 独立 RecompilePrimitive,并发触发不串 flag(可用一个并发 recompile 压测或代码评审证明实例隔离)。
- onStarted hook 存在且 test-runner 的 `EnsureRegistered` 经它调用;hook 签名足以让未来 TR-1c reattach 直接挂入(无需再改 host)。
- 端口 fallback 行为不变:editor-debug 仍 21891-21893,test-runner 仍 21890/21896/21897。

**依赖/阻塞**
- 被 ARCH-1-upm-sibling-dep-blocker 阻塞(需要 core 包能被消费工程解析)。
- 受 ARCH-1-envelope-guard 约束(host 不得统一信封/状态码)。
- 排序约束(ENH:158):ARCH-1b 必须排在 TR-1b/TR-1c(SessionState reattach)之前,否则 reattach 会被焊进单体 Start,日后还要把它当 hook 重新撬出。但若 TR-1a(DisableDomainReload)被采纳使 TR-1 不碰 host,本子项的 forcing function 减弱,可降级或延后。
- 依赖 ARCH-1a 先落(core 包骨架已建、dispatcher 已在 core,host 才有地方放)。

#### ARCH-1-registry-model:把 editor-core 建模成无 Skill 的 infra 包(OQ3)

**问题**
linker Registry 的 `status` 枚举只有 `ready` / `skill-only` / `planned` 三态(`u3d-ai-linker-design.md:82-88`),全部以"是否有公开 Package + Skill 可安装"为语义,无法表达 editor-core 这种"无 Skill、`defaultEnabled=false`、纯 infra、是其它工具的安装前置"的包。Registry 条目 schema(LINK:70-79)也无字段表达"包间 UPM 依赖与安装顺序"。OQ3(LINK:168 区域,ENH:168)正是把这列为开放问题:"editor-core 是无 Skill、defaultEnabled=false 的纯 infra 包,需要 planned-then-ready 之外的语义,且 Registry schema 当前不表达包间依赖与安装顺序"。若不解决,Install All 的白名单校验(LINK:108-116:`packageName` 必须以 `com.yoji.` 开头、ID/Package/Skill 名必须唯一)会因 editor-core 无 Skill 而需要特判,且安装顺序无法保证 core 先于工具。

**方案**
扩 Registry schema 表达 infra 包与依赖顺序,落到 linker 的解析+校验+安装编排:

- **新增 status 或新增字段**:两条路——(a) 加第四态 `infra`(语义:有 Package、无 Skill、不参与 Agent 同步、`Install All` 仍安装但不建 Junction);(b) 保持三态,改用条目级布尔字段 `providesSkill:false` + `infrastructure:true`。推荐 (b),因为它正交于 status(infra 包也有 planned/ready 生命周期),改动更小,且避免 `Install All` 只处理 ready(LINK:88)的逻辑被新态搅乱。
- **依赖与顺序**:Registry 条目加可选 `requiredBy` 或 `installOrder` 语义,使 editor-core 的 `order`(现有字段,LINK:74)严格小于两工具,linker 串行安装(LINK:98)时 core 先装。这直接服务 ARCH-1-upm-sibling-dep-blocker 的方案 A(linker 编排注入)。
- **Agent 同步豁免**:`ready` 工具靠 `Agent~/`(LINK:170)同步 Skill,editor-core 无 `Agent~/`,必须让同步逻辑跳过 infra 包(不报"missing SKILL.md")。Registry schema 与 linker 校验都要认 infra 包"无 Skill 合法"。
- **白名单不破**:editor-core 的 `packageName=com.yoji.editor-core` 满足 `com.yoji.` 前缀(LINK:111),`packagePath=Packages/com.yoji.editor-core` 满足路径约束(LINK:112),Tag 用 `editor-core-v<semver>` 满足 revision 正则(LINK:113)。唯一要放宽的是"工具 ID/Package/Skill 名唯一"中的 Skill 唯一性——infra 包无 Skill,该校验对它跳过。

坑:这是 linker workstream 的工作,但 linker 尚未构建——本子项是"为未来 linker 预留 schema 决策",当下产出是设计文档里的 schema 草案,而非可运行代码。不要在 linker 不存在时强行写 Registry JSON。

**改动文件**
- 修改:`docs/superpowers/specs/2026-06-12-u3d-ai-linker-design.md`(status 枚举段 L82-88 加 infra 语义、条目 schema L70-79 加 `infrastructure`/依赖字段、Agent 同步段 L170 加 infra 豁免、OQ3 转为决策)。
- (linker 构建时)修改:`Registry/stable.json`、`Registry/dev.json`(加 editor-core 条目)+ linker 解析/校验/安装编排代码。

**步骤**
1. 在设计文档定 infra 包建模方案((a) 新态 vs (b) 新字段,推荐 b)。
2. 定 editor-core 的 Registry 条目骨架(packageName/packagePath/order/Tag 前缀/infrastructure 标记/无 Skill)。
3. 定依赖顺序表达(core 的 order < 工具 order),对齐 ARCH-1-upm-sibling-dep-blocker 方案 A。
4. 把 OQ3 从开放问题转为决策记录。
5. linker workstream 实现时据此扩 schema 校验与安装编排,e2e dry-run 验证 core 先装、不建 Junction、不报缺 Skill。

**验收**
- 设计文档明确 editor-core 在 Registry 中的状态语义(infra)、依赖顺序、Skill 豁免,OQ3 关闭。
- schema 草案能让白名单校验(LINK:108-116)接受一个无 Skill 的 infra 包而不特判炸裂。
- (linker 落地后)Install All dry-run 日志显示 core 在两工具之前安装,且 Agent 同步跳过 editor-core。

**依赖/阻塞**
- 与 ARCH-1-upm-sibling-dep-blocker 方案 A 强耦合(linker 编排注入需要 Registry 能表达 infra + 顺序)。
- 被 u3d-ai-linker workstream 阻塞:linker 尚未构建,本子项当下只能产出 schema 决策,代码实现随 linker 落地。
- 依赖 ARCH-1a/1b 先确定 editor-core 真的会被抽出(否则无需建模)。

### 本章顺序与工作量

推荐内部顺序(强依赖驱动):

1. **ARCH-1-upm-sibling-dep-blocker(决策,~0.5 天)** —— 硬前置,gate 住后续一切。先与用户拍板 A/B/C 缓解方案,因为它决定是否、以及如何抽。若选 C(放弃),后续只剩 ARCH-1a 的 dispatcher 合并。
2. **ARCH-1a(~0.5 天)** —— 零风险机械搬运,建 core 包骨架 + 搬 dispatcher。纯代码部分可在前置决策前于本地 Packages/ 内联开发(同仓子包本地默认可解析),但 package.json 依赖/manifest 注入收尾受前置阻塞。
3. **ARCH-1-envelope-guard(随 ARCH-1b,~0.25 天增量)** —— 不是独立编码任务,而是 ARCH-1b 的设计约束,与之同步落地。
4. **ARCH-1b(~1-1.5 天含测试回归)** —— 真正的活:先抽 RecompilePrimitive(注意实例隔离 flag),再抽 LoopbackHttpHost(带 onStarted hook)。必须排在 TR-1b/1c reattach 之前;若 TR-1a 被采纳则可降级。
5. **ARCH-1-registry-model(决策 ~0.5 天,代码随 linker)** —— schema 决策当下可定,Registry/linker 代码随 linker workstream 落地。

粗估总工作量:决策 + dispatcher ~1 天;host+recompile 抽取 ~1-1.5 天;registry 建模决策 ~0.5 天(代码计入 linker)。合计本章纯编码约 2-2.5 天,外加两处需用户拍板的决策(OQ3 缓解方案、OQ6 独立性让步)。

跨章依赖:
- **WS(u3d-ai-linker 构建)**:ARCH-1-upm-sibling-dep-blocker 方案 A 与 ARCH-1-registry-model 都需 linker 已能串行装包并表达 infra+依赖顺序;linker 尚未构建是本章方案 A/registry 落地的最大外部阻塞。
- **TR-1(PlayMode workstream)**:ARCH-1b 与 TR-1b/1c 有排序耦合(host hook 必须先于 reattach 浇筑);TR-1a 是否被采纳直接决定 ARCH-1b 的紧迫性。建议先做 TR-1a spike(ENH:160 称其为"真正的第一动作"),再据结果定 ARCH-1b 体量。
- do-now 小赢(TR-2/TR-4/ED-1/ED-5,经核实多数已在源码落地)与本章无依赖,可并行。

## 7. test-runner PlayMode 与健壮性

> 工作流 id: WS3 | 依赖: WS2(ARCH-1b 的 LoopbackHttpHost on-start hook,供 TR-1c 挂入) | 工作量: 主体 4-5 人天(三项补漏 ~1.5 + TR-1a spike+实现 ~3 + k_StaleJobMs ~0.25);条件触发的 TR-1b/c 另加 3-4 人天

### 现状校准(动手前必读)

本章给定条目的"价值/成本/证据/状态"是设计阶段(`docs/superpowers/specs/2026-06-13-dev-tools-enhancements-design.md`)的快照,落动手前已与实际代码核对,发现若干条目状态已过期,必须以下面校准为准,否则会重复劳动:

- **TR-2/TR-3/TR-4 已全部落地**(commit `b72d241 feat(test-runner): per-test failures[], 0-match guard, /list-tests, port fix`)。证据:`TestRunService.TestFinished` 已实现失败收集(`TestRunService.cs:93-106`);`RunStarted` 已实现 0 命中守卫并清 `m_ActiveJobId` 防 RunFinished 覆盖(`TestRunService.cs:75-89`);`/list-tests` 端点已存在(`TestRunnerMCP.cs:136,193-209` + `TestRunService.ListTests` `TestRunService.cs:140-164`);端口已改为 `{ 21890, 21896, 21897 }`(`TestRunnerMCP.cs:22`),避开 lua-device-debug 钉死的 21894。**本章不重做这三项。**
- **TR-1 全家(PlayMode)确实 not-started**:全包 grep 无任何 `EnterPlayMode`/`DisableDomainReload`/`SessionState`/`isPlaying` 引用;`BuildFilter` 仍写死 `TestMode.EditMode`(`TestRunService.cs:65`);`/run-tests` 仍硬拒 PlayMode(`TestRunnerMCP.cs:160-161`)。这是本章主体。
- 给定条目里 `TR-listtests-null-api` 标 partial、`TR-client-groups` 标 partial、`TR-failmsg-mismatch` 标 partial,经核实它们是"功能已建、但有具体缺陷"的补漏项,确实尚未修。

因此本章把工作重组为三组:**A. PlayMode workstream(TR-1a→TR-1b/c,条件触发)**;**B. 已落地端点的健壮性补漏(TR-listtests-null-api / TR-client-groups / TR-failmsg-mismatch)**;**C. TR-5 维持 skip,仅做 k_StaleJobMs 可配**。

---

#### TR-1a PlayMode via DisableDomainReload spike + 实现

**问题**
当前 test-runner 只能验证纯 C# helper,碰不到运行时行为。`BuildFilter` 写死 `new Filter { testMode = TestMode.EditMode }`(`TestRunService.cs:65`)并完全忽略 `spec.TestMode`;`/run-tests` 在 `TestRunnerMCP.cs:160-161` 对 `spec.TestMode == "PlayMode"` 直接返 400("PlayMode is not supported in phase 1")。对一个 SLG 客户端,MonoBehaviour 生命周期、协程、物理、场景加载、UI 控制器这些最该测的集成形测试全部落在 PlayMode,agent 现在完全够不到(设计文档 ENH:9,94-96,145)。注意:`ListTests` 已经能接受 `mode == "PlayMode"` 参数(`TestRunService.cs:143`),但 `/list-tests` 路由层仍在 `TestRunnerMCP.cs:196` 拒 PlayMode——服务内核已半通,只差打开闸门。

PlayMode 的核心难点是域重载:正常 PlayMode 进入会触发 domain reload,把发起 run 的 `m_ActiveJobId`(`TestRunService.cs:22`,实例字段)冲掉,`RunFinished` 回调挂在被销毁的旧 domain 上。TR-1a 用 `EnterPlayModeOptions.DisableDomainReload` 绕开整个重载难题——不发生重载,现有 field-based `m_ActiveJobId` 与 `EnsureRegistered/RunFinished` 路径原样工作(`TestRunService.cs:34-39,111-136`)。这是 spike,也是决定 TR-1b/c 是否需要的分叉点(ENH:54,145,159)。

**方案**
分两步:先 spike 验证可行性与还原正确性,再固化实现。

1. **设置 scoped 切换**:在 `TestRunService` 新增私有方法 `ApplyPlayModeSpike()` / `RestorePlayModeSettings()`。进 run 前 snapshot 用户当前两个值:`EditorSettings.enterPlayModeOptionsEnabled`(bool)与 `EditorSettings.enterPlayModeOptions`(`EnterPlayModeOptions` flags)。临时设 `enterPlayModeOptionsEnabled = true`,并 `enterPlayModeOptions = oldOptions | EnterPlayModeOptions.DisableDomainReload`——**必须按位叠加,不能直接赋值**,否则丢掉用户已有的 `DisableSceneReload` 等选项(ENH:96)。`DisableSceneReload` 只作为可选 spike 变量,不强加。
2. **完整还原**:在 `RunFinished`(`TestRunService.cs:111`)末尾以及"超时未触发"两条路径都还原这两个设置。还原必须是 per-run scoped,绝不能污染用户项目级 Editor 配置(`EditorSettings.asset` 会被写盘)。坑:若 `RunFinished` 因任何原因不触发,设置会永久残留——必须配一个看门狗(见步骤 5)。
3. **BuildFilter 按 mode 选**:`BuildFilter`(`TestRunService.cs:63`)签名不变,把 `testMode = spec.TestMode == "PlayMode" ? TestMode.PlayMode : TestMode.EditMode`。
4. **放开闸门**:删 `TestRunnerMCP.cs:160-161` 的 400;`/list-tests` 路由 `TestRunnerMCP.cs:196` 放开 PlayMode(`ListTests` 内核已支持)。
5. **前置守卫 + 看门狗**:进 PlayMode run 前,若 `EditorApplication.isPlaying` 为 true 或有脏未存场景,拒(返 409 "editor is already in play mode" / 400 "unsaved scene")。看门狗:发起后记时间戳,若超时(沿用现有 600s `k_StaleJobMs` 兜底,或更短的 init-timeout)无 `RunFinished`,在 `SweepStale` 路径里既 `FailJob` 又调 `RestorePlayModeSettings()`——保证设置不会因孤儿 run 永久残留。
6. **SKILL.md caveat**:注明依赖静态状态跨域重置的测试在 DisableDomainReload 下行为不同(test-authoring 问题,非 bug)(ENH:96)。

**关键坑**:`isPlaying`/`EditorSettings.*` 都是主线程 API,而 `StartRun` 已经经 `MainThreadDispatcher.Run` 在主线程跑(`TestRunnerMCP.cs:165`),所以 snapshot/apply/restore 天然在主线程,无需额外 marshalling。但还原逻辑必须放在 `RunFinished`(主线程回调)和 `SweepStale`(可能从 HTTP 线程 `/ping` 触发,见 `TestRunnerMCP.cs:143`)两处——后者改设置需 dispatch 回主线程,否则在 HTTP 线程直写 `EditorSettings` 会抛。

**改动文件**
- 修改:`Packages/com.yoji.test-runner/Editor/TestRunService.cs`(BuildFilter 按 mode;新增 ApplyPlayModeSpike/RestorePlayModeSettings;RunFinished 还原)
- 修改:`Packages/com.yoji.test-runner/Editor/TestRunnerMCP.cs`(删 PlayMode 400;/list-tests 放开 PlayMode;PlayMode 前置守卫)
- 修改:`Packages/com.yoji.test-runner/Editor/JobStore.cs`(SweepStale 在置 error 时触发设置还原回调,或新增 restore hook)
- 修改:`Packages/com.yoji.test-runner/Agent~/skills/test-runner-mcp/client.py`(`run-tests --mode PlayMode` 已是 choices,无需改;`list-tests --mode` 当前 `choices=["EditMode"]` 在 `client.py:106` 需加 PlayMode)
- 修改:`Packages/com.yoji.test-runner/Agent~/skills/test-runner-mcp/SKILL.md`(去掉"阶段 1 仅 EditMode""PlayMode 返 400"的措辞 `SKILL.md:22,42,107,179`,补 DisableDomainReload caveat)
- 修改:`Packages/com.yoji.test-runner/Agent~/skills/test-runner-mcp/references/run-e2e.py`(改 case 05:PlayMode 不再期望 400;新增 PlayMode pass+fail 各一)

**步骤**
1. 先做纯 spike:在 TestProjects/test-runner 加一个最小 PlayMode 测试 asmdef(标 `UNITY_INCLUDE_TESTS`、引用 `UnityEngine.TestRunner`),含一个 `AlwaysPasses`/`AlwaysFails` PlayMode 夹具。
2. 临时硬改 `BuildFilter` 走 PlayMode + 设 DisableDomainReload,手动 `curl /run-tests {"testMode":"PlayMode",...}`,观测 `RunFinished` 是否在不重载的前提下正常回填 `/test-status`。
3. 验证 `EditorSettings.enterPlayModeOptions` 在 run 后被还原(读 `EditorSettings.asset` 或反射比对 snapshot)。
4. spike 通过后,把硬改替换为 scoped Apply/Restore + mode 派发 + 前置守卫 + 看门狗。
5. 删 400、放开 /list-tests、更新 client.py/SKILL.md/run-e2e.py。
6. 跑 run-e2e.py 全套(含新 PlayMode case),确认 EditMode 路径无回归。

**验收**
- live e2e:`run-e2e.py` 对 PlayMode pass 用例返 `status=completed passed==1 failed==0`,对 PlayMode fail 用例返 `failed==1 overallResult=Failed` 且 `failures[0].message` 含预期串(复用 TR-2 通道)。
- run 前后 `EditorSettings.enterPlayModeOptionsEnabled` 与 `enterPlayModeOptions` 二者数值完全一致(快照对比),证明 scoped 还原无副作用。
- 故意制造 PlayMode run 后 kill Editor 前不还原的场景,确认看门狗在超时后既 FailJob 又还原设置(可通过缩短 stale 阈值断言)。
- EditMode 全套 e2e(case 01-09b)继续全绿,证明无回归。

**依赖/阻塞**
- 无前置代码依赖,可立即开 spike。
- spike 结论决定是否需要 TR-1b/c:若 DisableDomainReload 被判**可接受**,TR-1b/c 直接丢弃;若**不可接受**,则进入条件触发路径(见下)。
- 与 ARCH-1b(另一章)弱依赖:TR-1a 不发生重载、几乎不碰 listener 外壳(只删 400、改 BuildFilter、scoped 设置),所以**不需要等 ARCH-1b**(ENH:159)。

---

#### TR-1b/c 域重载存活路径(条件触发,默认不做)

**问题**
仅当 TR-1a 的 DisableDomainReload 被判**不可接受**(目标 PlayMode 测试集依赖静态状态跨域重置,见 ENH:166 开放问题),才需要真正的"域重载存活"路径。此时 PlayMode 进入会重载,`m_ActiveJobId`(实例字段,`TestRunService.cs:22`)被冲掉,`RunFinished` 挂在旧 domain 上丢失;`SweepStale` 只看内存 `m_Current`(`JobStore.cs:137`),重载后为 null,对孤儿 run 全盲(ENH:100,149,167)。

**这是整条 workstream 成败未知的核心**:"pre-reload 发起的 PlayMode run,其 `RunFinished` 是否会在 post-reload 的新 domain 上触发"无法单测(runner runs itself),**必须 live e2e 验证**(ENH:167)。confidence 低,故默认不建。

**方案**(仅在条件满足时)
- **TR-1b — SessionState 跨域存活 active jobId**:单一 `SessionState` key(如 `"Yoji.TestRunner.ActiveJobId"`)在 `StartRun`(`TestRunService.cs:42`)写、`RunFinished` 读、收尾 erase。SessionState 跨域重载存活、Editor 重启自清,生命周期正好。镜像进 `JobStore`:构造时(`JobStore` ctor `JobStore.cs:46`)若 SessionState 有 jobId 且 `LoadFromDisk(jobId)` 显示 `status==running`,重新认领为 `m_Current`,使 `State==Running`(`JobStore.cs:55-66`)且 `SweepStale` 能看见孤儿。为可单测,把 SessionState 包成可注入接口 `ISessionStore`(默认实现包 `UnityEditor.SessionState`,测试用内存 fake),沿用 `JobStore` 已有的"目录与时钟从构造注入"模式(`JobStore.cs:46-51`)。
- **TR-1c — 域重载后重挂 RunFinished + init-timeout 收尾**:`TestRunnerMCP.Start` 已在每次 `[InitializeOnLoad]` 调 `s_Service.EnsureRegistered()`(`TestRunnerMCP.cs:48`),fresh `ICallbacks` 已注册——缺的只是"它没有存活 jobId 可收尾"。TR-1b 认领后,已注册回调位于可接 post-reload `RunFinished` 的位置(**必须 live e2e 验证**)。加 init-timeout:重挂时记时间戳,若 30-60s 内无 `RunStarted/RunFinished`,`FailJob(jobId, "orphaned by domain reload")`,保证 agent 轮询有界终止(ENH:101);现有 600s `k_StaleJobMs` 作最后兜底。

**关键设计取舍/坑**
- 设计文档明确(ENH:157):TR-1 的 reload-survival 大部分是 per-tool 业务(SessionState job 指针、reattach、init-timeout 都落在 `TestRunService`/`JobStore`/`TestRunnerMCP`),core 刻意不拥有 `JobStore/TestRunService`,所以"第三份发散副本"之说对 SessionState 部分略夸大。
- 真正成立的窄论点:TR-1c 的 reattach 要改 `Start()` 路径。当前 `Start()`(`TestRunnerMCP.cs:38-72`)是单体,无 on-start hook。**TR-1c 必须排在 ARCH-1b 之后**:若 ARCH-1b 已把外壳抽成 `LoopbackHttpHost` 并暴露干净的 on-start hook,reattach 是挂进稳定 host 的回调;若未抽,reattach 被焊进单体 `Start()`,日后还得撬出来——"先浇混凝土再钻孔"(ENH:158)。

**改动文件**(仅条件触发时)
- 新建:`Packages/com.yoji.test-runner/Editor/ISessionStore.cs`(SessionState 抽象 + 默认实现)
- 修改:`TestRunService.cs`(StartRun 写 SessionState;RunFinished 读+erase;重挂时间戳)
- 修改:`JobStore.cs`(ctor 注入 ISessionStore;构造时认领孤儿;SweepStale 覆盖重载孤儿)
- 修改:`TestRunnerMCP.cs`(经 ARCH-1b on-start hook 挂 reattach;init-timeout)
- 修改:`Tests/Editor/JobStoreTests.cs`(SessionState 认领 round-trip 单测)

**步骤**
1. 仅在 TR-1a spike 判定 DisableDomainReload 不可接受后启动。
2. 先做 live spike 验证"post-reload RunFinished 是否触发"——这是 go/no-go 闸门,若不触发整条路径不可行(ENH:167)。
3. spike 通过才实现 TR-1b(SessionState + JobStore 认领),单测覆盖认领逻辑。
4. 等 ARCH-1b 的 LoopbackHttpHost on-start hook 就绪后实现 TR-1c。
5. live e2e 验证跨重载 pass+fail 各一收尾正确。

**验收**
- live e2e:发起 PlayMode run -> 触发域重载 -> 重载完成后 `/test-status?jobId=` 最终返 completed 且结果正确(非孤儿 error)。
- 单测:`JobStoreTests` 验证"SessionState 有 running jobId + 磁盘记录" -> 构造后 `State==Running` 且认领为 m_Current。
- init-timeout:模拟重挂后无 RunFinished,30-60s 内 jobId 转 error "orphaned by domain reload"。

**依赖/阻塞**
- **强阻塞**:TR-1a spike 结论(DisableDomainReload 不可接受)+ live RunFinished-survival spike(必须先证明可行)。
- **强依赖**:ARCH-1b(LoopbackHttpHost on-start hook)必须先于 TR-1c 落地。
- 若 TR-1a 够用,本子项整体丢弃。

---

#### TR-listtests-null-api /list-tests 缺 Idle 前置守卫

**问题**
`/list-tests`(`TestRunnerMCP.cs:193-209`)与其底层 `TestRunService.ListTests`(`TestRunService.cs:140-164`)无任何 busy/Idle 前置守卫。对比 `/run-tests` 在非 Idle 时经 `StartJob` 抛 -> 返 409(`TestRunnerMCP.cs:174-176`、`JobStore.cs:80`),`/list-tests` 在编译中或有 run 在跑时仍会照常进入:`ListTests` 经 `MainThreadDispatcher.Run` 把 `RetrieveTestList` 派到主线程(`TestRunService.cs:147`),但编译期间主线程的 `EditorApplication.update` 泵会停摆/`RetrieveTestList` 回调迟迟不来,HTTP 线程在 `done.Wait(30000)`(`TestRunService.cs:161`)上一直阻塞到 30s 超时才返 500。agent 拿不到快速、明确的"忙、请稍后"信号,只能干等 30s(条目证据:TestRunService.cs:140-164 无 busy 守卫;对比 run-tests 409)。

**方案**
在 `/list-tests` 路由入口加 Idle 前置守卫,与 `/run-tests`/`/recompile` 的 busy 语义对齐:返 409 而非阻塞。

1. 在 `TestRunnerMCP.ListTests`(`TestRunnerMCP.cs:193`)进入 `s_Service.ListTests` 之前,先查 `s_Jobs.IsIdle`(`JobStore.cs:68`)与 `EditorApplication.isCompiling`。非 Idle 或编译中 -> 返 `(409, Err("service busy: list-tests requires Idle state"))`。
2. 取舍:`isCompiling` 是主线程 API,但此处只读 bool,实测在 HTTP 线程读基本安全(`RecompileHandler.cs:41` 已在主线程读它);更稳妥可缓存(镜像 ED-5 缓存方案),但 list-tests 频率低,直接读可接受,文档标注。
3. 把 `done.Wait(30000)` 的 30s 超时降到更合理值(如 10s)并在超时时返 408/409 语义,而非现在的 500("list-tests failed: timeout")——让 agent 能区分"忙"与"真错"。

**改动文件**
- 修改:`Packages/com.yoji.test-runner/Editor/TestRunnerMCP.cs`(ListTests 路由加 Idle 守卫)
- 修改:`Packages/com.yoji.test-runner/Agent~/skills/test-runner-mcp/SKILL.md`(/list-tests 段补"非 Idle 返 409")
- 修改:`Packages/com.yoji.test-runner/Agent~/skills/test-runner-mcp/references/run-e2e.py`(加"编译中 /list-tests -> 409"用例,可选)

**步骤**
1. ListTests 入口加 `if (!s_Jobs.IsIdle || EditorApplication.isCompiling) return (409, Err(...))`。
2. 降低/明确 `done.Wait` 超时语义。
3. SKILL.md /list-tests 段补守卫说明。
4. e2e 验证:在有 run 在跑时调 /list-tests 立即返 409(可借 run-all 长 run 制造窗口)。

**验收**
- 在 state=Running 或 Compiling 时 `GET /list-tests` 立即(<1s)返 409,不再阻塞 30s。
- Idle 时 /list-tests 行为不变(返 200 + tests[])。
- SKILL.md /list-tests 段落明示 busy 语义。

**依赖/阻塞**
- 无前置依赖,可独立做。与 TR-1a 解耦(若 TR-1a 放开 PlayMode,守卫逻辑同样适用 PlayMode list-tests)。

---

#### TR-client-groups client.py 缺 --groups + mode 面不一致

**问题**
两处面不一致:
1. **缺 --groups**:服务端 `TestFilterBuilder.Parse` 已解析 `groupNames`(`TestFilterBuilder.cs:34`),`BuildFilter` 已把它转给 Unity `filter.groupNames`(`TestRunService.cs:70`),SKILL.md 也宣传 `groupNames`(`SKILL.md:21`)。但 `client.py` 的 `run-tests` 子命令只有 `--names`/`--assemblies`/`--categories`(`client.py:96-98`),**没有 `--groups`**。CLI 用户无法走 group 过滤,只能手拼 JSON。
2. **mode 面不一致**:`run-tests` 的 `--mode` 是 `choices=["EditMode", "PlayMode"]`(`client.py:95`),而 `list-tests` 的 `--mode` 是 `choices=["EditMode"]`(`client.py:106`)。即使在 TR-1a 之前,这两处对 PlayMode 的开放度也不一致(run-tests 允许传 PlayMode 让服务端拒,list-tests 在 CLI 层就拒)。TR-1a 落地后这个不一致会变成实质 bug(list-tests 支持 PlayMode 但 CLI 传不进去)。

**方案**
1. `client.py` `cmd_run`(`client.py:61-69`)加 `--groups`:`r.add_argument("--groups", nargs="*", ...)`,并在 payload 里 `if a.groups: payload["groupNames"] = a.groups`。
2. 统一 `--mode` 处理:把 `list-tests` 的 `--mode` choices 与 `run-tests` 对齐(TR-1a 后均为 `["EditMode","PlayMode"]`;TR-1a 前可暂保持 list-tests 仅 EditMode,但应在同一处常量定义,避免发散)。取舍:让 CLI 始终允许传 PlayMode、由服务端裁决(单一真相源在服务端),CLI 不重复 phase-gating 逻辑——这样 TR-1a 一开闸,CLI 零改动即可用。

**改动文件**
- 修改:`Packages/com.yoji.test-runner/Agent~/skills/test-runner-mcp/client.py`(run-tests 加 --groups;统一 --mode choices)
- 修改:`Packages/com.yoji.test-runner/Agent~/skills/test-runner-mcp/SKILL.md`(若 run-all 扩展段提及 groupNames CLI 用法则同步)

**步骤**
1. `cmd_run` 与 `run-tests` 子 parser 加 `--groups`。
2. 把 mode choices 收敛到单一定义,list-tests 与 run-tests 共用。
3. 手测 `client.py run-tests --mode EditMode --groups SomeGroup` 透传到服务端。

**验收**
- `python client.py run-tests --groups X` 能把 `groupNames:["X"]` 发到 `/run-tests`(抓包或服务端日志确认)。
- list-tests 与 run-tests 的 `--mode` 接受相同取值集合。

**依赖/阻塞**
- 与 TR-1a 协同:mode 统一应与 TR-1a 同批或之后做,使 CLI 在 PlayMode 开闸后无需二次改动。--groups 部分可独立先做。

---

#### TR-failmsg-mismatch completed message 固定串 vs SKILL.md 写有耗时

**问题**
`JobStore.CompleteJob` 把 completed 任务的 `Message` 写死为 `"测试完成"`(`JobStore.cs:101`),不含耗时。但 SKILL.md 的 /test-status completed 示例写的是 `"测试完成，耗时 8.19s"`(`SKILL.md:147`)。文档与实现不符:agent 若依赖 message 里的耗时会落空。同时 `JobRecord` 已有 `StartedMs`/`UpdatedMs`(`JobStore.cs:29-30`),`CompleteJob` 里 `UpdatedMs = m_NowMs()`(`JobStore.cs:101`)——耗时数据现成,只是没用上。`/recompile` 已经在 message 里带耗时(`RecompileHandler.cs:75` "编译成功，耗时 Ns"),test-status 没对齐这个惯例。

**方案**
让 `CompleteJob` 把耗时算进 message,与 SKILL.md 和 /recompile 惯例对齐。

1. `JobStore.CompleteJob`(`JobStore.cs:92`)里:`var elapsedMs = rec.UpdatedMs - rec.StartedMs;`(rec 来自 `Target(jobId)`,保留了原 StartedMs,见 `JobStore.cs:148-153`),`rec.Message = "测试完成，耗时 " + Math.Round(elapsedMs / 1000.0, 2) + "s";`。
2. 取舍:用 `UpdatedMs - StartedMs`(墙钟跨度,含发起到 RunFinished 全程,含域重载/编译)而非纯测试执行时间——这与 SKILL.md 语义("单测 jobId 流程总耗时 = 编译 + 域重载 + 测试",`SKILL.md:235`)一致,且无需引入新计时字段。坑:晚到的 complete 复用缓存记录时 StartedMs 已保留(`JobStore.cs:148` 注释明示),所以跨域场景下耗时仍可算。
3. 可选:在 `JobRecord` 加 `public long DurationMs;` 并在 `JobJson`(`TestRunnerMCP.cs:211`)的 completed 分支 emit `["durationMs"]`,让 agent 拿到结构化耗时而非只能 parse message。若做,需同步 `JobStoreTests.cs` 与 SKILL.md。建议做,结构化优于字符串。

**改动文件**
- 修改:`Packages/com.yoji.test-runner/Editor/JobStore.cs`(CompleteJob message 带耗时;可选 DurationMs 字段)
- 修改(可选):`Packages/com.yoji.test-runner/Editor/TestRunnerMCP.cs`(JobJson emit durationMs)
- 修改(若加字段):`Packages/com.yoji.test-runner/Tests/Editor/JobStoreTests.cs`(CompleteJob 断言更新)
- 修改:`Packages/com.yoji.test-runner/Agent~/skills/test-runner-mcp/SKILL.md`(确认 :147 示例与实现一致)

**步骤**
1. CompleteJob 算 elapsed 写进 message。
2. (可选)加 DurationMs 字段并 emit。
3. 跑 `JobStoreTests` 确认无回归;更新断言。
4. 核对 SKILL.md:147 与实现措辞一致。

**验收**
- `/test-status` completed 响应的 `message` 含 "耗时 N.NNs",数值合理(>0)。
- (若做字段)响应含 `durationMs` 整数。
- SKILL.md:147 示例与实际输出措辞一致。
- JobStoreTests 全绿。

**依赖/阻塞**
- 无前置依赖,完全独立,最小工作量,可作为 warm-up 项先做。

---

#### TR-5 /cancel 维持 skip,仅做 k_StaleJobMs 可配

**问题**
设计文档已论证 `/cancel` 应 skip(ENH:39,64,177):`TestRunnerApi` 无受支持的 abort,忠实 cancel 基本不可实现;state-only shim 会对 agent 撒谎(引擎仍在跑)。EditMode run 很短,409 阻塞窗口小,stale sweeper 已能回收。当前代码无 /cancel,符合设计。唯一杠杆:`k_StaleJobMs` 现为硬编码 `600_000`(`TestRunnerMCP.cs:20`),若 agent 想缩短楔死延迟无从配置。

**方案**
**不实现 /cancel**。仅把 `k_StaleJobMs` 做成可配,降低 stale 回收延迟的调节门槛。

1. 把 `k_StaleJobMs`(`TestRunnerMCP.cs:20`)的来源改为:优先读环境变量(如 `TESTRUNNER_STALE_MS`)或一个 EditorPrefs/ScriptableObject 配置,缺省回落 600_000。最低成本是环境变量:`Start()` 里 `int.TryParse(Environment.GetEnvironmentVariable("TESTRUNNER_STALE_MS"), out var v)`。
2. 取舍:不引入新配置文件/UI,环境变量足够(本地 dev 工具)。注意 PlayMode(TR-1a)落地后 run 可能更长,stale 阈值需相应放宽——这是把它做可配的额外动机(ENH:64 "PlayMode 落地后再议")。
3. 范围严格限定在阈值可配,不碰 abort 语义。

**改动文件**
- 修改:`Packages/com.yoji.test-runner/Editor/TestRunnerMCP.cs`(k_StaleJobMs 改为可配,从 const 变可读取的字段)
- 修改:`Packages/com.yoji.test-runner/Agent~/skills/test-runner-mcp/SKILL.md`(故障排除段说明可配阈值)

**步骤**
1. 把 const 改为 Start 时初始化的字段,读环境变量回落 600_000。
2. SKILL.md 注明可调及与 PlayMode 长 run 的关系。

**验收**
- 设 `TESTRUNNER_STALE_MS=30000` 后,孤儿 run 在 30s 而非 600s 被 SweepStale 置 error。
- 不设时行为不变(600s)。
- 确认仍无 /cancel 端点(skip 决策保持)。

**依赖/阻塞**
- 无前置依赖。与 TR-1a 协同:PlayMode 长 run 落地后应复核默认阈值。

---

### 本章顺序与工作量

**推荐内部顺序**(由独立性、依赖、决策杠杆排序):

1. **TR-failmsg-mismatch**(warm-up,~0.5 人天)——零依赖、最小改动,先消文档/实现漂移。
2. **TR-listtests-null-api**(~0.5 人天)——零依赖,补 busy 守卫消除 30s 阻塞。
3. **TR-client-groups**(~0.5 人天)——零依赖(--groups 部分);mode 统一部分与 TR-1a 协同。
4. **TR-1a spike + 实现**(~2-3 人天)——**本章决策枢纽**。先 live spike(0.5-1 天)验证 DisableDomainReload 可行性与设置还原,再固化实现。spike 结论决定 TR-1b/c 是否需要。
5. **k_StaleJobMs 可配**(~0.25 人天)——零依赖;建议与 TR-1a 同批,因 PlayMode 长 run 需复核阈值。
6. **TR-1b/c**(条件触发,~3-4 人天,confidence 低)——**仅当** TR-1a 判 DisableDomainReload 不可接受 **且** live RunFinished-survival spike 通过才建。**强依赖 ARCH-1b**(WS:ARCH 章的 LoopbackHttpHost on-start hook)必须先于 TR-1c。

**跨章依赖**:
- TR-1c **强依赖** ARCH-1b 的 `LoopbackHttpHost` on-start hook(避免把 reattach 焊进单体 `TestRunnerMCP.Start()`,`TestRunnerMCP.cs:38`)。ARCH-1b 应排在重 TR-1b/c 之前(ENH:158)。
- TR-1a **不依赖** ARCH-1b(不重载、几乎不碰外壳),可与 ARCH 章并行。
- TR-2 e2e 通道(`failures[]`)被 TR-1a 的 PlayMode fail 用例复用,已存在(`run-e2e.py:94-97`),无需新建。

**净工作量**:不含条件触发的 TR-1b/c,本章主体约 **4-5 人天**(三项补漏 ~1.5 天 + TR-1a spike+实现 ~3 天 + k_StaleJobMs ~0.25 天);若进入 TR-1b/c 路径再加 **3-4 人天**。

## 8. 验证与一致性欠账(让已落地的东西可信)

> 工作流 id: WS1 | 依赖: WS2(ARCH-1 的无回归门禁以本章 CI + 刷新基线为前置基础设施,设计文档 :191) | 工作量: M(约 3-3.5 人天;以等待 Unity 资源与配置 CI/license 为主,纯代码改动很小)

### 总览与前置说明

本章面对的是一类特殊欠账:功能代码已落地(diff 已合入 main 1ebe62c),但"它真的能跑/能编译"这件事从未被在线 Unity 证明过。所有相关 e2e 断言已写进各包 `references/run-e2e.py`(`Packages/com.yoji.test-runner/Agent~/skills/test-runner-mcp/references/run-e2e.py:85-131`、`Packages/com.yoji.editor-debug/Agent~/skills/unity-editor-debug-mcp/references/run-e2e.py:202-216`),但这些脚本顶部都写明前提是"Unity Editor 已打开对应 TestProject 且服务在线"(`run-e2e.py:4`),离线无法执行。验证基线表(`progress.md:101-105`)的通过计数明确标注为"增强端点落地前的基线"(`progress.md:107-109`),增强后的真实数字至今空缺。

两项文档一致性欠账已是 done,本章不再展开,仅记录现状供组装者勾稽:
- progress.md 头部 SHA 已更新为 `main = 1ebe62c`(`progress.md:5`)。
- README 端口表已修正为 `21890 (fallback 21896/21897)`(`README.md:132`),与 `TestRunnerMCP.cs:22` 的 `k_Ports = { 21890, 21896, 21897 }` 一致。

本章把剩余欠账分为四个子项:在线 e2e 重跑并刷新基线(合并 TR-e2e-rerun 与 HYG-VERIFY-DEBT,二者是同一次在线会话的两个产物)、Unity 6.4 真机编译验证(ED-6.4-COMPILE)、`/console` 内部 API 跨版本健壮性补测(ED-1-VERSION-ROBUSTNESS)、headless batchmode runner 接 CI 并去硬编码(HYG-BATCHMODE-NOT-WIRED)。

#### 在线 Editor 重跑全部 e2e 并刷新验证基线计数

问题:
- 两个包的 e2e 脚本已为新端点写好断言但从未在在线 Editor 跑过。test-runner 侧 `run-e2e.py` 已覆盖 case 03b(TR-2 failures[0] 含 name+message,`run-e2e.py:95-97`)、case 08/08b(TR-3a 0 命中判 error 且晚到 RunFinished 不回填,`run-e2e.py:114-122`)、case 09/09b(TR-3b `/list-tests`,`run-e2e.py:125-131`)。editor-debug 侧已覆盖 case 14(ED-5 `/ping` 暴露 `isPlaying`/`isCompiling` bool,`run-e2e.py:204-209`)、case 15(ED-1 `/console` 返回 entries 数组,`run-e2e.py:215-216`)。
- 这些断言里有几条结构上无法被 EditMode 单测覆盖,必须 live HTTP 才能证明。最关键的是 TR-3a 的"晚到 RunFinished 不回填":守卫逻辑在 `TestRunService.RunStarted` 里 0 命中时清掉 `m_ActiveJobId`(`TestRunService.cs:86`),依赖随后必到的 `RunFinished` 看到 `m_ActiveJobId==null` 走早退(`TestRunService.cs:113-115`)。这是 Unity TestRunnerApi 的运行时回调时序行为,runner-runs-itself 无法单测(测试框架自身在跑),只能靠 e2e case 08b 在真实 Editor 里轮询验证(`run-e2e.py:118-122`)。
- 验证基线表(`progress.md:103-105`)写死 editor-debug "74 EditMode + 13 HTTP e2e",test-runner "EditMode 套件通过",均标注为增强前数字(`progress.md:107-109`);README 同样写 "74 Unity EditMode tests and 13 HTTP end-to-end checks"(`README.md:81-82`)。增强后新增了 case 03b/08/08b/09/09b(test-runner)与 case 14/15(editor-debug)等用例,真实通过数必然变化,但从未更新。设计文档的验证清单 ENH §验证(`docs/superpowers/specs/2026-06-13-dev-tools-enhancements-design.md:184-191`)全部未勾。

方案:
- 这是一次性"在线验证会话",把 TR-e2e-rerun 与 HYG-VERIFY-DEBT 合并执行,因为它们是同一会话的两个产物(跑完即可读出新基线)。
- 在装有 Unity 6000.3.16f1 的机器上,分别打开 `TestProjects/test-runner` 与 `TestProjects/editor-debug`(两者 `ProjectVersion.txt` 均为 6000.3.16f1,已核 `TestProjects/editor-debug/ProjectSettings/ProjectVersion.txt:1`),确认 Console 出现 `[TestRunnerMCP] 服务已启动` / `[EditorDebugMCP] 服务已启动`。
- 跑两套 e2e:`python run-e2e.py -v`(test-runner,默认端口 21890)与对应 editor-debug 脚本;test-runner 侧建议带 `--include-recompile`(`run-e2e.py:133-135`)以覆盖 `/recompile` 路径,但要接受其 ~200s 超时与会触发域重载的副作用。
- 同步跑一遍 EditMode 全套件(可直接用本章 HYG-BATCHMODE-NOT-WIRED 修好的 `tools/run-editmode.ps1`,或 Test Runner 窗口),读出 total/passed/failed。
- 用实测数字回填三处:`progress.md:103-105` 的验证基线表、`progress.md:107-109` 的"增强后完整通过数待重跑"那段注记(改为已确认)、`README.md:81-82` 的 "74 ... 13 ..." 计数;并删除 `progress.md:120` 的待办勾选项"重跑增强端点的完整 EditMode + e2e 套件,更新验证基线计数"。
- 坑:e2e case 08/08b 依赖 0 命中守卫真在线生效;若 `RunStarted` 的 `CollectTestCaseNames`(`TestRunService.cs:82`)对某些 `[Explicit]` fixture 的枚举语义与预期不符(本包 `FixtureTests` 即 `[Explicit]`,设计文档 ENH:84 已点名需快速核),case 08 可能假阴。重跑时若 08 失败,先核 fixture 的 RunStarted 枚举是否把 `[Explicit]` 用例计入 planned,再决定是改守卫还是改断言。
- 坑:run-all(case 07)与 `/recompile` 会触发真实编译/域重载,跑序上应放在非重载用例之后,避免重载打断同会话后续 HTTP。

改动文件:
- 修改:`progress.md`(基线表 101-105、注记 107-112、待办 120)。
- 修改:`README.md`(81-84 的计数与 6.4 说明措辞)。
- 不改代码;若 e2e 暴露真实回归才另开修复(本子项产出为"验证通过"或"发现的缺陷清单")。

步骤:
1. 在 6000.3.16f1 机器打开 `TestProjects/test-runner`,确认服务在线。
2. `python run-e2e.py -v`,记录每条 PASS/FAIL 与最终 "N passed, M failed"。
3. 打开 `TestProjects/editor-debug`,跑其 e2e,重点确认 case 14/15(ED-5/ED-1)。
4. 跑两工程 EditMode 全套件,读 total/passed/failed/skipped。
5. 用实测数回填 progress.md 三处与 README 计数,删除 progress.md:120 待办。
6. 若有 FAIL,逐条记录为缺陷(file:line + 现象),交回对应功能章节修复后重跑。

验收:
- 两套 `run-e2e.py` 在线跑出 "全 PASS"(test-runner 含 02/03/03b/04/05/06/07/08/08b/09/09b,editor-debug 含 14/15),且有终端输出或截图为证。
- `progress.md:103-105` 与 `README.md:81-82` 的计数为本次实测值,且 `progress.md` 不再含"增强后完整通过数需在 Unity 在线时重跑确认"这类未决措辞、不再含 120 行待办。
- 设计文档 ENH:184-186、189 对应的验证条目可被勾(TR-2/TR-3/TR-4/ED-1/ED-5)。

依赖/阻塞:
- 阻塞于一台装有 Unity 6000.3.16f1 的在线机器(本环境无法直接执行 Unity)。
- 弱依赖 HYG-BATCHMODE-NOT-WIRED(用其修好的 runner 跑 EditMode 更省事,但非强依赖,Test Runner 窗口亦可)。
- 与"功能落地"章节解耦:本子项只验证不开发,但若发现回归会反向给该章节派工。

#### Unity 6.4 entity-id 分支真机编译验证

问题:
- editor-debug 的 object 引用序列化与反序列化按 Unity 版本分三支:`ArgumentCoercer.ResolveUnityObject` 用 `#if UNITY_6000_4_OR_NEWER` 走 `EditorUtility.EntityIdToObject(EntityId.FromULong(...))`、`#elif UNITY_6000_3_OR_NEWER` 走 `EntityIdToObject(int)`、`#else` 走 `InstanceIDToObject`(`ArgumentCoercer.cs:54-60`);`ResultSerializer.SerializeObjectId` 用 `#if UNITY_6000_4_OR_NEWER` 走 `EntityId.ToULong(uo.GetEntityId()).ToString(...)`、`#else` 走 `GetInstanceID()`(`ResultSerializer.cs:119-124`)。
- 6.4 分支用到的 `EntityId.FromULong` / `EntityId.ToULong` / `uo.GetEntityId()` 这些 API 只在 Unity 6.4+ 存在,而本仓库所有 TestProject 都是 6000.3.16f1(`TestProjects/editor-debug/ProjectSettings/ProjectVersion.txt:1`),`UNITY_6000_4_OR_NEWER` 从不为真,这条分支从未被编译过一次。asmdef `versionDefines:[]`(`Yoji.EditorDebug.Editor.asmdef:12`),即没有自定义版本符号,完全依赖 Unity 内置的 `UNITY_6000_x_OR_NEWER` 平台宏 —— 这意味着只有真在 6.4 安装上打开工程才会激活该分支编译,无法靠加宏在 6.3 上 dry-run。
- README 已诚实标注 "still requires compilation on a Unity 6.4 installation before release"(`README.md:83-84`),progress.md 也把"在 Unity 6.4 安装上编译并验证 entity id 分支后发布"列为待办(`progress.md:117`)。即风险已知、待清。

方案:
- 在一台装有 Unity 6.4(任一 6000.4.x)的机器上,把 `Packages/com.yoji.editor-debug` 以 `file:` 方式装进一个 6.4 工程(或临时把 `TestProjects/editor-debug` 升级到 6.4,但升级不可逆,推荐另建 6.4 工程引用包,见 README:58-62 的安装方式)。
- 让 6.4 工程完成一次干净编译,确认 `UNITY_6000_4_OR_NEWER` 分支(`ArgumentCoercer.cs:55`、`ResultSerializer.cs:120`)无编译错误 —— 重点核 `EntityId.FromULong(ulong)`、`EntityId.ToULong(EntityId)`、`UnityEngine.Object.GetEntityId()`、`EditorUtility.EntityIdToObject(EntityId)` 这四个符号签名在该 6.4 小版本上确实存在且参数类型匹配(`ParseEntityIdRaw` 产出 `ulong`,`ArgumentCoercer.cs:63-66`)。
- 跑一次 round-trip 冒烟:`describe`/`invoke` 一个真实 GameObject,确认 `ResultSerializer` emit 的 objectId 是"全宽 EntityId 十进制字符串"(`ResultSerializer.cs:120-121`),再把该字符串作为 `--target-entity-id` 传回 `ArgumentCoercer.ResolveUnityObject`(`ArgumentCoercer.cs:63-66` 走 string 解析分支)能正确解析回同一对象。这正是 6.4 上 string 形 EntityId 与 6.3 上 int 形 instanceID 的关键差异点,必须端到端验证不是只编译过。
- 坑:`EntityId` API 在 6.4 不同 patch 间可能仍有签名微调,验证时记录确切 6.4 小版本号;若签名不符,改的是这两个 `#if` 分支而非协议。
- 坑:不要为了省事把 6.3 TestProject 直接升 6.4 后再也回不去 —— 那会让本仓库失去 6.3 基线。用独立 6.4 工程引用包。

改动文件:
- 通常无代码改动(目标是确认现有分支可编译)。若发现 API 签名不符,修改:`Packages/com.yoji.editor-debug/Editor/ArgumentCoercer.cs`(54-60)、`Packages/com.yoji.editor-debug/Editor/ResultSerializer.cs`(119-124)。
- 修改:`README.md:83-84`(把"still requires compilation"改为"verified on 6.4")、`progress.md:117`(勾掉 6.4 待办)。

步骤:
1. 在 Unity 6.4 机器新建/选定一个 6.4 工程,manifest 加 `"com.yoji.editor-debug": "file:..."`。
2. 打开工程,等编译完成,确认无 error(尤其 6.4 分支)。
3. 启动服务(Console 出 `[EditorDebugMCP] 服务已启动`),`client.py ping` 通。
4. 对一个场景对象做 invoke get name -> 拿 objectId 字符串 -> 用 `--target-entity-id <字符串>` 再 invoke 同对象,确认解析回原对象。
5. 回填 README:83-84 与 progress.md:117,记录确切 6.4 版本号。

验收:
- 6.4 工程编译零 error,`ArgumentCoercer.cs:55` 与 `ResultSerializer.cs:120` 分支被实际编译(可通过故意改坏再恢复确认该行参与编译)。
- entity-id 字符串 round-trip 冒烟通过(序列化出的字符串能反解析回同一对象)。
- README 与 progress.md 不再把"6.4 编译验证"列为未决项,且记录了验证所用 6.4 小版本。

依赖/阻塞:
- 阻塞于一台装有 Unity 6.4 的机器(本仓库无 6.4 工程)。
- 与在线 e2e 重跑子项独立(不同 Unity 版本),可并行。

#### /console 内部 API 跨版本健壮性补测

问题:
- ED-1 `/console` 整条读日志链路全靠反射拿 Unity editor-internal 的 `UnityEditor.LogEntries`/`LogEntry`(`ConsoleHandler.cs:8-10` 注释已声明"版本敏感")。关键反射点:方法名 `StartGettingEntries`/`EndGettingEntries`/`GetEntryInternal`(`ConsoleHandler.cs:100-102`),字段名 `message`/`file`/`line`/`mode`/`instanceID`/`callstackText`(`ConsoleHandler.cs:103-108`)。日志级别分类靠手写位掩码常量 `k_ModeError`/`k_ModeWarning`(`ConsoleHandler.cs:14-17`)对 `entry.mode` 做按位与(`ConsoleHandler.cs:53,126-127`)。
- 这些内部 API 的方法名、字段名、位掩码值都无官方文档、随 Unity 版本可变。`callstackText` 字段已被显式标注"可能不存在,best-effort"(`ConsoleHandler.cs:108`),`includeStack` 时找不到就跳过(`ConsoleHandler.cs:65-68`)。设计文档 ENH:107 也明确 6000.3.16f1 反射可见 `LogEntry` 有 `message/file/line/column/mode/instanceID/...` 但无稳定 `stackTrace` 字段,`includeStack` 只能 best-effort。
- 当前没有任何 e2e 校验"位掩码分类是否真对":case 15 只断言 `/console` 返回了 entries 数组(`run-e2e.py:215-216`),不验证一条人为 `Debug.LogError` 是否被分类成 `type=="Error"`。若 `k_ModeError`(`ConsoleHandler.cs:14-15`)的某一位在某 Unity 版本上语义漂移,`/console` 会静默把 Error 误标成 Log/Warning,agent 仍读到日志但 `filter:'error'` 会漏掉它 —— 这是"看起来工作、实际过滤错"的隐性失真。

方案:
- 强化 e2e case 15(或新增 15b):在调用 `/console` 前,先通过 editor-debug 自己的 `/eval` 或 `/invoke` 让 Unity 打一条已知 `Debug.LogError("E2E_CONSOLE_PROBE_ERROR")` 和一条 `Debug.LogWarning(...)`,再 `GET /console?filter=error&count=10`,断言返回里能找到 message 含 `E2E_CONSOLE_PROBE_ERROR` 且 `type=="Error"` 的条目;再 `filter=warning` 断言 warning 可见、error 仍可见(`PassesFilter` 语义:warning 含 error,`ConsoleHandler.cs:129-133`);再 `filter=error` 断言 warning 不出现。这把"位掩码分类正确性"从无人验证变成可回归。
- `ConsoleHandler.EnsureReflection`(`ConsoleHandler.cs:85-124`)已对必需成员缺失返回 `{__unavailable}`(`ConsoleHandler.cs:35,109-114`),这部分降级路径已健壮,无需改;补测的重点是"在场版本上分类语义正确",而非"缺失时不崩"。
- 坑:`Debug.LogError` 在 EditMode 会被 Unity 计为测试失败/报错,e2e 是 HTTP 路径不是测试上下文,正常;但若用 `/eval` 注入,注意 `LogError` 会留在 Console 影响同会话后续 `filter=all` 计数,probe 串要够独特以便精确匹配。
- 坑:位掩码常量(`ConsoleHandler.cs:14-17`)是按"在 6000.3.16f1 上观察到的 LogMessageFlags 子集"硬编码的;补测只能证明当前版本对,不能证明跨版本对。验收应明确"本测试锚定在 6000.3.16f1",未来上新版本需重跑此 probe。

改动文件:
- 修改:`Packages/com.yoji.editor-debug/Agent~/skills/unity-editor-debug-mcp/references/run-e2e.py`(扩 case 15 / 增 15b probe-then-classify)。
- 通常不改 `ConsoleHandler.cs`;仅当 probe 暴露当前版本分类错误才改 `ConsoleHandler.cs:14-17` 的位掩码或 `103-108` 的字段名。

步骤:
1. 在 editor-debug e2e 里新增 probe helper:经 `/eval` 或 `/invoke` 触发一条独特串的 `Debug.LogError` 与一条 `Debug.LogWarning`。
2. 增 case:`/console?filter=error` 断言能匹配到 probe error 且 `type=="Error"`。
3. 增 case:`/console?filter=warning` 断言含 warning 与 error;`filter=error` 断言不含 warning。
4. 在 6000.3.16f1 在线跑通(可并入"在线 e2e 重跑"会话)。
5. 若分类错误,调 `ConsoleHandler.cs:14-17` 位掩码并记录该版本的正确位集。

验收:
- editor-debug e2e 含一条"注入已知 Error/Warning -> `/console` 分类正确"的用例,并在 6000.3.16f1 在线 PASS。
- `filter=error`/`filter=warning`/`filter=all` 三态过滤语义有断言覆盖(对应 `ConsoleHandler.cs:129-133`)。
- 注记当前位掩码锚定版本为 6000.3.16f1。

依赖/阻塞:
- 与"在线 e2e 重跑"子项强相关:probe 用例本身就该在那次在线会话里一起跑,建议合并执行。
- 阻塞于在线 6000.3.16f1 Editor。

#### headless batchmode runner 接 CI 并去硬编码 Unity 路径

问题:
- `tools/run-editmode.ps1` 已是一个可用的 headless batchmode EditMode runner(`-batchmode -nographics -runTests -testPlatform EditMode`,`run-editmode.ps1:65-71`),会解析 NUnit3 XML 报 pass/fail、无 XML 时扫 log 找编译错误(`run-editmode.ps1:81-109`),并清理 stale `UnityLockfile`(`run-editmode.ps1:53-58`)。这本是 Rule-0 的"编译+测试门禁"。
- 但它有两处使其无法进 CI:一是 Unity 路径硬编码为 `E:\Unity\Unity Editor\6000.3.16f1\Editor\Unity.exe`(`run-editmode.ps1:26`),只在作者本机成立,换机/CI runner 必失败;二是仓库根没有任何 CI 配置(`.github/` 不存在,已确认 Glob 无命中),这个 runner 从不被自动触发,只能人工本地跑。progress.md 仅一句"另已加入 headless batchmode EditMode runner 作为离线执行路径"(`progress.md:111-112`),README 完全未提及这个 runner。
- 后果:三个 TestProject 的 EditMode 套件没有任何自动化回归网;每次改包后是否破坏编译/测试,完全依赖人工记得手跑,这与本仓库"让已落地的东西可信"的目标直接矛盾。

方案:
- 去硬编码:把 `run-editmode.ps1` 的 Unity 路径改为可被环境变量/参数覆盖。具体把 `param` 的 `$Unity` 默认值(`run-editmode.ps1:26`)改为优先读环境变量(如 `$env:UNITY_EDITOR_PATH`),其次回退到 Unity Hub 默认安装位置探测,最后才是当前硬编码值作为本机兜底;保留 `-Unity` 显式参数最高优先。已有的 `Test-Path $Unity` 守卫(`run-editmode.ps1:42-45`)会在找不到时报错退出 3,逻辑可复用。
- 接 CI:新建 `.github/workflows/editmode.yml`,用 GameCI(`game-ci/unity-test-runner` 或自托管 runner + 已装 Unity)对 `TestProjects/{test-runner,editor-debug,lua-device-debug}` 三个工程分别跑 EditMode。坑:Unity CI 需要 license 激活(`UNITY_LICENSE`/`UNITY_EMAIL`/`UNITY_PASSWORD` secrets)与可缓存的 `Library/`;`TestProjects/*/Library` 与 `TestResults/` 是构建产物(`progress.md:110-111`),CI 应缓存 Library、归档 TestResults。
- 取舍:GitHub-hosted runner 默认不带 Unity,激活 license + 下载 6000.3.16f1 编辑器很重;若没有自托管 runner,更务实的第一步是让 `run-editmode.ps1` 可移植 + 写一个能在自托管 Windows runner 上调它的 workflow,而非强上 GameCI 全家桶。本子项标 partial,建议先做"去硬编码 + workflow 骨架(self-hosted 触发本地 runner)",GameCI 全自动激活作为后续。
- 坑:`-runTests` 会自动退出 Editor,不能同时传 `-quit`(`run-editmode.ps1:11-12` 注释已警告);CI 脚本不要叠 `-quit`。
- 坑:不能对另一个 Editor 正打开的工程跑(会撞 lockfile,`run-editmode.ps1:14-15`);CI 上工程是干净 checkout,无此问题,但本地并行跑要注意。
- 文档:在 README 增一节说明该 runner 的用途与 `UNITY_EDITOR_PATH` 用法,把它从"progress.md 一句话"提升为可发现的离线门禁。

改动文件:
- 修改:`tools/run-editmode.ps1`(26 行的 `$Unity` 默认值改为环境变量/Hub 探测/兜底链)。
- 新建:`.github/workflows/editmode.yml`(三工程矩阵 EditMode 跑、Library 缓存、TestResults 归档)。
- 修改:`README.md`(增 headless runner / CI 说明小节)、`progress.md:111-112`(把 runner 从离线脚注升级为 CI 门禁描述)。

步骤:
1. 改 `run-editmode.ps1:26`:`$Unity` 默认 = `$env:UNITY_EDITOR_PATH` ?? Hub 探测 ?? 现硬编码;保留 `-Unity` 覆盖。
2. 本机用 `$env:UNITY_EDITOR_PATH` 验证 runner 仍正常(三工程各跑一次 EditMode 出绿)。
3. 新建 `.github/workflows/editmode.yml`:matrix=[test-runner,editor-debug,lua-device-debug],step 调 runner 或 GameCI;配置 Library 缓存与 TestResults artifact。
4. 配 CI secrets(若用 GameCI:UNITY_LICENSE/EMAIL/PASSWORD)或落到 self-hosted Windows runner。
5. 推一次让 workflow 跑绿,README/progress.md 补 CI 与 `UNITY_EDITOR_PATH` 说明。

验收:
- `run-editmode.ps1` 在未设硬编码路径的机器上,仅靠 `UNITY_EDITOR_PATH`(或 `-Unity`)即可跑通,且无路径时给出清晰报错(复用 `run-editmode.ps1:43` 的 exit 3)。
- `.github/workflows/editmode.yml` 存在,push/PR 触发,对三工程跑 EditMode 并在 Actions 上可见绿/红与 TestResults artifact。
- README 有 headless runner 一节,`progress.md:111-112` 反映 CI 门禁状态。

依赖/阻塞:
- CI 全自动化阻塞于 Unity license 与(理想情况下)一台装 6000.3.16f1 的 self-hosted runner 或 GameCI license。
- "去硬编码"部分无外部阻塞,可立即做。
- 与"在线 e2e 重跑"互补:CI 跑 EditMode,e2e 仍需在线 Editor 手跑(e2e 依赖 HTTP 服务在线,`run-e2e.py:4`,不适合无 GUI batchmode)。

### 本章顺序与工作量

推荐内部顺序:
1. HYG-BATCHMODE-NOT-WIRED 的"去硬编码"部分先做(无阻塞、纯本地、让后续验证更省事)——约 0.5 天。
2. 在线 e2e 重跑 + 刷新基线(合并 TR-e2e-rerun + HYG-VERIFY-DEBT)+ `/console` 健壮性补测,三者放进同一次 6000.3.16f1 在线会话一起跑——约 1 天(含写 probe 用例与回填文档)。
3. Unity 6.4 entity-id 真机编译验证,独立 6.4 机器,可与 2 并行——约 0.5 天。
4. HYG-BATCHMODE-NOT-WIRED 的 CI 骨架(workflow + license)作为收尾——约 1-1.5 天(license/self-hosted 配置是主要不确定性)。

粗估总量:S-M 偏向"等资源"而非"写代码"——本章绝大多数工作是验证与文档回填,真正的代码改动仅 `run-editmode.ps1` 去硬编码、e2e probe 用例、可能的位掩码/EntityId 分支微调,合计约 3-3.5 人天,其中 CI 与跨版本编译的不确定性最大。

跨章依赖:
- 本章是"功能落地章节"的下游验证关:若在线 e2e 暴露 TR-2/TR-3/ED-1/ED-5 的真实回归,会反向给那些章节派修复工。
- 与 ARCH-1(editor-core 抽取章节)弱相关:设计文档把"两包 EditMode + e2e 全套通过"列为 ARCH-1 的无回归门禁(`docs/superpowers/specs/2026-06-13-dev-tools-enhancements-design.md:191`),本章建立的 CI + 刷新后的基线正是该门禁的基础设施,应在 ARCH-1 动工前先就位。
- 不依赖 PlayMode(TR-1)章节:本章只覆盖已落地的 EditMode 与现有端点。

## 9. lua-device-debug 端到端可用

> 工作流 id: WS5 | 依赖: WS2(editor-core / MainThreadDispatcher 抽取需协调先后)、WS7(跨工程协作:LDD-ANDROID-LIVE 经协作通道交接样例并回收真机结果) | 工作量: 仓内独立部分 M-L(参考样例 Samples~ 占大头,其余为 S/XS 的修补与测试);跨工程真机闭环 LDD-ANDROID-LIVE 另计 L 且不在本仓库人天内

### 背景与现状基线

`Packages/com.yoji.lua-device-debug` 是"通用 C# 传输层 + 项目 Lua Debug Adapter"架构里的通用层(设计见 `docs/superpowers/specs/2026-06-13-lua-device-debug-design.md:22-27`)。当前仓内只包含传输层:

- HTTP/JSON 服务 `LuaDeviceDebugServer.cs`(`/ping` `/commands` `/execute`, `TcpListener` 绑 loopback)。
- Host 契约 `ILuaDeviceDebugHost.cs`、注册表 `LuaDeviceDebugRuntime.cs`、主线程派发 `MainThreadDispatcher.cs`、JSON 限额 `JsonGuard.cs`、双授权 mutation gate。
- Editor/Player 启动器、Agent CLI `Agent~/skills/unity-lua-device-debug/client.py`。
- EditMode 测试用 `Tests/Editor/FakeHost.cs` 充当 host, `progress.md:105` 记录 6000.3.16f1 上 8/8 通过。

关键事实: 仓内**没有任何真实 Host 实现, 也没有 `Samples~`**(`Agent~` 是唯一的 `~` 目录, 全包内 `*sample*` 零命中)。设计 `:65-82`、`progress.md:81-82,118` 明确把 xLua adapter、Lua dispatcher、SLG 命令划到消费工程(`SLG_Prototype`)、本仓库范围外。因此"端到端可用"在本仓库内**永远无法靠 FakeHost 自证**——必须提供一份可被复制的参考适配器样例, 把传输层的契约具象化, 否则消费工程开发者只能照着 8 行接口和散落在设计文档里的约定盲写。这正是本章把 `LDD-ADAPTER-EXAMPLE` 列为最高价值项的原因。

本章遵循一条主线: **区分"仓内工作"与"消费工程工作"**。仓内能完整交付的是参考样例、SyncContext 加固、Release 构建期断言、测试与文档补齐; 真正的 Android 真机全流程因依赖 xLua VM 与目标 App, 只能定义验收脚本和待办、由消费工程闭环。

#### 参考适配器样例 + Lua dispatcher/serializer 样例 (Samples~) [LDD-ADAPTER-EXAMPLE]

问题: 传输层只暴露 8 行的 `ILuaDeviceDebugHost`(`ILuaDeviceDebugHost.cs:3-8`: `IsReady` / `DescribeCommands()` / `Execute(command, argsJson, allowMutation)`), 但消费工程要正确实现它, 必须同时满足分散在设计与代码里的多条隐性契约, 而仓内没有任何一处把它们一次性示范出来:
- `DescribeCommands()` 必须返回"数组或含 `commands` 数组的对象", 每项至少有 `name` 且写 `mutating`——这是 `LuaDeviceDebugServer.cs:224-238` `FindMutatingFlag` 硬编码读取的形状, 形状不符直接 500 `INVALID_COMMAND_DESCRIPTOR`(`:228,234`)。
- `Execute()` 返回的 JSON 必须能过 `JsonGuard`(`JsonGuard.cs:13-51`: 深度<=32、成员<=4096、仅 JSON 基本类型), 且不得含 `UnityEngine.Object`/userdata/delegate(设计 `:219`)。
- mutating 命令的双重授权、参数 schema 校验、`found:false` vs 裸 `null` 的语义(设计 `:482-490`)、敏感字段脱敏(设计 `:302,413,570`)全部是 **Host/Lua 层职责**, 传输层一概不做。
现状只有 `FakeHost.cs` 一个"够用即可"的桩(`FakeHost.cs:16-51`, 仅返回 `command/args/allowMutation` 回显), 它**不演示**参数校验、`found` 语义、脱敏、异常上下文回流, 因此不能当样例。设计仓库结构 `:88-128` 列出的 `Samples~` 与目标工程 `Lua/debug/{debug_dispatcher,debug_commands,debug_serializer}.lua` 在仓内均不存在。

方案: 在包内新增 `Samples~/ReferenceAdapter`, 提供一份**编译可过、与 xLua 解耦**的参考实现, 通过 UPM `package.json` 的 `samples` 字段声明, 让消费工程从 Package Manager 一键导入:
- `ReferenceLuaDeviceDebugHost.cs`: 实现 `ILuaDeviceDebugHost`, 不引用 xLua/`LuaEnv`, 而是定义一个最小 `ILuaDebugBridge { string Describe(); string Execute(string cmd, string argsJson, bool allowMutation); }` 抽象, 把"调 Lua 全局入口 `__slg_debug_describe_commands` / `__slg_debug_execute`"(设计 `:237-244`)的位置用注释标出, 并示范捕获 Lua 异常 -> 转 `LUA_EXECUTION_FAILED` envelope + 完整堆栈写 `LogService`(设计 `:233,398`)、HTTP 只回安全上下文。坑: 样例**绝不能**真的 `#r` xLua, 否则包失去"通用层不依赖 xLua"的卖点(README `:26`、设计 `:24-27`); 用接口注入把 runtime 细节挡在样例外。
- `debug_dispatcher.lua` / `debug_commands.lua` / `debug_serializer.lua`: 按设计 `:246-304` 给出可读样例——dispatcher 负责注册/拒重名/参数校验/`mutating` vs `allowMutation` 检查/错误附 command 上下文; commands 给 1-2 个只读命令(如 `system.info`、`config.get`)演示 `found:false` 语义和敏感字段脱敏; serializer 演示深度/集合/循环引用限制与"非字符串 key 拒绝或显式数组识别"。这些 `.lua` 作为**文本资产**随样例分发, 不参与 C# 编译。
- `Samples~/ReferenceAdapter/README.md`: 串起 wire 形状契约、双授权流程、`found` 语义、脱敏要求、异常回流, 并明确"这是参考不是 drop-in, xLua 绑定需消费工程补完"。

改动文件:
- 新建 `Packages/com.yoji.lua-device-debug/Samples~/ReferenceAdapter/ReferenceLuaDeviceDebugHost.cs`
- 新建 `Samples~/ReferenceAdapter/ILuaDebugBridge.cs`
- 新建 `Samples~/ReferenceAdapter/lua/debug_dispatcher.lua`、`debug_commands.lua`、`debug_serializer.lua`
- 新建 `Samples~/ReferenceAdapter/README.md`
- 修改 `Packages/com.yoji.lua-device-debug/package.json`: 增加 `samples` 数组项(`displayName`/`description`/`path:"Samples~/ReferenceAdapter"`)

步骤:
1. 确定 wire 契约的"金标准": 把 `FindMutatingFlag`(`LuaDeviceDebugServer.cs:224-238`)与 `JsonGuard`(`JsonGuard.cs`)读取/校验的字段固化成一段文档化的 JSON schema 注释。
2. 写 `ILuaDebugBridge.cs` + `ReferenceLuaDeviceDebugHost.cs`, 用一个内联 fake bridge 使样例在无 xLua 时也能编译并跑通(可在 Tests 里实例化验证 `DescribeCommands()` 形状被 `FindMutatingFlag` 接受)。
3. 写三个 `.lua` 样例, 覆盖 register/拒重名/参数校验/mutating gate/`found` 语义/脱敏/序列化限额。
4. 写 `package.json` `samples` 项与样例 README。
5. 加一个 EditMode 测试: 用样例 Host(经 fake bridge)替换 FakeHost 跑一遍 `/commands` `/execute`, 证明样例输出能被传输层正确消费。

验收:
- Package Manager 中能看到并导入 "Reference Adapter" 样例。
- 新增 EditMode 测试: 样例 Host 的 `DescribeCommands()` 经 `/commands` 返回 200 且首条命令名正确; 其只读命令经 `/execute` 返回 200, mutating 命令无 `--allow-mutation` 返回 403 `MUTATION_DENIED`。
- 样例 Runtime 代码 grep 无 `xlua`/`LuaEnv`/`CS.` 命中(保持通用层解耦)。
- 样例 README 明确标注"参考、非 drop-in", 并覆盖 wire 形状/双授权/`found` 语义/脱敏/异常回流五条契约。

依赖/阻塞: 无前置, 可立即开工。它是 `LDD-ANDROID-LIVE` 的前置——消费工程需要这份样例才能高效实现 xLua adapter; 也为 `LDD-CMDDESC-DEAD`、`LDD-SKILL-ASSETS` 提供 wire 形状的权威出处。

#### IL2CPP/Android 上 SyncContext 抓取加固 [LDD-SYNCCTX-INIT]

问题: `MainThreadDispatcher.Initialize()`(`MainThreadDispatcher.cs:15-19`)在被调用线程上抓 `SynchronizationContext.Current` 并记 `s_MainThreadId`; 它由 `LuaDeviceDebugServer.Start()`(`LuaDeviceDebugServer.cs:49`)调用, 而 Player 路径的 `Start()` 跑在 `[RuntimeInitializeOnLoadMethod(AfterAssembliesLoaded)]`(`LuaDeviceDebugPlayerBootstrap.cs:10`)。在 IL2CPP/Android 上 `AfterAssembliesLoaded` 时机较早, `SynchronizationContext.Current` 有为 `null` 的风险。一旦为 `null`, 派发器在 HTTP 工作线程上走到 `MainThreadDispatcher.cs:31-32` 直接抛 500 `INTERNAL_ERROR`——意味着 `/commands` `/execute` 在设备上**全程不可用**, 而这恰恰是端到端的核心路径。当前实现没有任何"context 为 null 时延迟重抓或换钩子"的兜底。`progress.md:105` 的 8/8 验证全在 **Editor** 完成, Editor 下 `delayCall`(`LuaDeviceDebugEditorBootstrap.cs:13`)时机晚、context 已就绪, 因此这条风险被 Editor 测试掩盖。该项标记 landed-needs-verify: 代码写了, 但设备侧 context 非空从未被证明。

方案: 把 SyncContext 的获取从"启动时一次性抓"改为"对 null 健壮 + 可重抓", 并补一条 Player 侧的二次初始化钩子:
- `MainThreadDispatcher` 增加 `EnsureContext()`: 当 `s_Context == null` 时尝试重新读取 `SynchronizationContext.Current`(在主线程上调用才有效), 并允许 Player bootstrap 在更晚的钩子(如 `BeforeSceneLoad` 或第一帧)再调一次 `Initialize()`/`EnsureContext()`。考虑把 `Initialize` 改为幂等且记录"是否已拿到非空 context"。
- `LuaDeviceDebugPlayerBootstrap` 增加 `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]` 兜底, 若 `AfterAssembliesLoaded` 时 context 为 null 则在此补抓; 服务启动本身可保持在 `AfterAssembliesLoaded`(尽早监听), 但 dispatcher 的 context 允许迟一拍补齐。坑: 不能在 HTTP 工作线程上抓 context(那永远是 null); 重抓只能调度回主线程或在主线程钩子里做。另一坑: Unity 的 `UnitySynchronizationContext` 安装时机随版本/平台变化, 不能假设某个固定钩子一定非空, 故设计成"多钩子尝试 + 首次非空即锁定"。
- 当 context 始终拿不到时, 错误信息要可诊断: 把 500 的 message 从泛化的 "not initialized" 改为指明"主线程 SynchronizationContext 不可用", 便于设备侧定位。

改动文件:
- 修改 `Packages/com.yoji.lua-device-debug/Runtime/MainThreadDispatcher.cs`(`Initialize` 幂等化 + `EnsureContext`)
- 修改 `Packages/com.yoji.lua-device-debug/Runtime/LuaDeviceDebugPlayerBootstrap.cs`(补 `BeforeSceneLoad` 兜底钩子)
- 可能修改 `Packages/com.yoji.lua-device-debug/Runtime/LuaDeviceDebugServer.cs`(`Start()` 调 `Initialize` 处)
- 新建/修改 `Tests/Editor` 用例覆盖 context 为 null 的分支

步骤:
1. 把 `Initialize()` 改为幂等, 区分"已抓到非空 context"与"尚未抓到"; 暴露 `EnsureContext()`。
2. Player bootstrap 增加二次钩子, 在更晚时机补抓 context。
3. EditMode 测试: 模拟 `s_Context == null` 时 `Run()` 抛 500 并带可诊断 message; 模拟 `EnsureContext` 在主线程补抓后 `Run()` 恢复直跑。
4. 在文档(README "Security/Lifecycle"段或设计文档生命周期段)记录"Player 侧 context 抓取时机与兜底"。

验收:
- EditMode 测试覆盖 context 为 null -> 500、补抓后恢复两条分支。
- 设备侧验证(归并到 `LDD-ANDROID-LIVE` 的验收脚本): Android Dev Build 上 `/execute system.info` 返回 200 而非 500 `INTERNAL_ERROR`, 即证明 context 在设备上成功抓取。
- 500 错误 message 在 context 缺失时明确指向主线程 context 不可用。

依赖/阻塞: 设备侧最终验证依赖 `LDD-ANDROID-LIVE`(需真机)。仓内代码与 EditMode 测试可独立完成。

#### Android Dev Build 真机全流程验证 [LDD-ANDROID-LIVE]

问题: 设计 `:573-585` 的端到端验收 10 步里第 3-10 步(构建 Android Dev Build、装到真机、`adb forward`、`/ping` 确认设备态、跑 8 个诊断命令、非法路径拒绝、断 USB 后游戏续跑、Release 无监听、500 次无泄漏)**从未执行过**: `progress.md:105` 的验证基线仅"8/8 fake-host EditMode"(Editor、无真机、无 xLua)。Player 路径代码(`LuaDeviceDebugPlayerBootstrap.cs` 整文件 `#if DEVELOPMENT_BUILD && !UNITY_EDITOR` + 运行时 `Application.platform == Android` 双闸 `:1,13`)从未在设备上跑过。标记 partial: 传输层和 CLI 的 adb forward 逻辑(`client.py:185-237`)写好了, 但闭环缺两块——真机和 xLua Host, 后者本仓库范围外。

方案: 这是**跨工程协作项**, 仓内只能做"可验证脚本 + 待办锚点", 真正闭环在消费工程:
- 仓内: 把设计 `:573-585` 的 10 步固化成一份可执行/可勾选的 e2e checklist(放 `Agent~/skills/unity-lua-device-debug/references/android-e2e.md`), 每步给出对应 CLI 命令(`python client.py adb-forward` / `ping` / `execute system.info` ...)和期望 JSON 形状/HTTP 码。CLI 侧补"设备不可达/forward 冲突/断线"的稳定错误输出已具备(`client.py:54-55,202`), 在 checklist 中引用即可。
- 消费工程(范围外, 但本章需点明交接物): 实现 xLua adapter + Lua dispatcher(以 `LDD-ADAPTER-EXAMPLE` 样例为蓝本) -> 出 Android Dev Build -> 按 checklist 跑通 -> 回填验证基线。坑: Android socket 后台行为不确定, 设计 `:534` 已声明首版不承诺后台可用性, checklist 第 8 步只验"断 USB 后游戏续跑", 不验后台响应。坑: 真机端口需 `adb forward` 而非 `adb reverse`(设计 `:80-86`), 服务只监听设备回环。
- 把 `LDD-SYNCCTX-INIT` 的设备侧验证(context 在 Android 非空)并入本项 checklist 第 5/6 步, 一次真机会话同时收两项。

改动文件:
- 新建 `Packages/com.yoji.lua-device-debug/Agent~/skills/unity-lua-device-debug/references/android-e2e.md`(10 步可勾选 checklist)
- 修改 `progress.md`(真机验证完成后回填基线行 `:105`)
- 交接(范围外): 消费工程的 xLua adapter / `Lua/debug/*.lua` / Android Dev Build

步骤:
1. 仓内编写 android-e2e checklist, 每步绑定 CLI 命令与期望输出。
2. (消费工程)按 `LDD-ADAPTER-EXAMPLE` 样例实现 xLua adapter 并注册 Host。
3. (消费工程)出 Android Development Build, `adb forward tcp:21894 tcp:21894`。
4. (消费工程)逐步执行 checklist, 记录每步实际 HTTP 码/JSON。
5. 回填 `progress.md` 验证基线, 把 unity-lua-device-debug 状态从 transport-only 升级为 device-verified。

验收:
- 仓内: android-e2e.md 覆盖设计 `:573-585` 全部 10 步, 每步可独立执行。
- 端到端(跨工程): 真机上 8 个诊断命令全 200; 非法命令 404 `COMMAND_NOT_FOUND`、非法参数 400/`INVALID_ARGUMENT`、未授权写 403 `MUTATION_DENIED`; 断 USB 后客户端请求失败而游戏续跑; 500 次连续查询无 Lua 引用/线程/队列泄漏(对照 `MainThreadDispatcher` 的 `s_QueuedJobs` 回到 0)。

依赖/阻塞: 强依赖 `LDD-ADAPTER-EXAMPLE`(样例)与 `LDD-SYNCCTX-INIT`(设备侧 context); 强依赖消费工程(`SLG_Prototype`)与真机, 属本仓库范围外的闭环。仓内交付物(checklist)无阻塞。

#### Release Build 排除的构建期断言 [LDD-RELEASE-BUILD-VERIFY]

问题: 设计安全要求 `:402-403` 明确"Release Build 不注册不启动服务, 且**构建验证必须确认调试类型未进入 Player**"。当前只有 `LuaDeviceDebugPlayerBootstrap.cs:1` 用 `#if DEVELOPMENT_BUILD && !UNITY_EDITOR` 把**启动器**排除掉; 但 `LuaDeviceDebugServer.cs`、`MainThreadDispatcher.cs`、`LuaDeviceDebugRuntime.cs`、`JsonGuard.cs` 等**传输层类全文无任何 `#if` 守卫**(已 grep 确认零命中), 在 Release Build 里这些类型仍会被编译进 Player——虽然无人 `new` 它们, 但"调试类型不进入构建"这条断言不成立, 且没有任何构建期检查去证伪。这正是端到端验收第 9 步(设计 `:583`: Release 确认 21894 无监听且调试服务类型未进入构建)无法自动化的原因。标记 partial。

方案: 用"编译期排除 + 构建期断言"双管:
- 编译期: 给整个 Runtime 程序集加 `defineConstraints` 或对核心类加 `#if UNITY_EDITOR || DEVELOPMENT_BUILD` 包裹, 使 Release Player 里这些类型根本不参与编译。最干净的做法是在 `Yoji.LuaDeviceDebug.Runtime.asmdef` 加 `"defineConstraints"`——但 asmdef 的 defineConstraints 是"全有或全无", 一旦加上, Release 下整个 Runtime 程序集不编译, 这会影响样例/其它引用。取舍: 若样例需在 Release 也可引用类型则不能整程序集排除, 改为对 `LuaDeviceDebugServer` 等运行期类加 `#if` 源码守卫, 保留 `ILuaDeviceDebugHost`/`LuaDeviceDebugRuntime` 这些纯契约类型(它们不含监听逻辑, 留着无害且方便消费工程编译)。需先定清"哪些类型属于必须排除的调试实现, 哪些是无害契约"。
- 构建期断言: 写一个 `IPostprocessBuildWithReport`(Editor 程序集)在非 Development Build 下扫描产物/或断言 `LuaDeviceDebugServer` 类型在目标 define 下不可达, 不满足则 `BuildFailedException`。坑: 直接扫 IL2CPP 产物成本高且脆; 更可靠的是断言"当前构建若非 DEVELOPMENT_BUILD 则 `LuaDeviceDebugServer` 类型经条件编译已不存在"——用反射在 build callback 里检查类型是否带条件编译标记, 或更简单地断言 scripting define 组合正确并由 `#if` 保证。

改动文件:
- 修改核心 Runtime 类加 `#if UNITY_EDITOR || DEVELOPMENT_BUILD` 守卫(`LuaDeviceDebugServer.cs`、`MainThreadDispatcher.cs`、`JsonGuard.cs`、`LuaDeviceDebugException.cs`、`CommandDescriptor.cs`; 保留契约类 `ILuaDeviceDebugHost.cs`/`LuaDeviceDebugRuntime.cs`/`LuaDeviceDebugPackage.cs` 待评估)
- 新建 `Packages/com.yoji.lua-device-debug/Editor/LuaDeviceDebugBuildGuard.cs`(`IPostprocessBuildWithReport`)
- 修改 README/设计文档记录 Release 排除的实现与验证方式

步骤:
1. 给核心调试实现类加 `#if UNITY_EDITOR || DEVELOPMENT_BUILD` 守卫, 确认 Editor + Dev Build 仍编译通过、契约类不受影响。
2. 写 `LuaDeviceDebugBuildGuard`: 非 Development 构建时断言调试类型未进入, 失败抛 `BuildFailedException`。
3. EditMode 不易测构建回调, 至少加注释级/集成级说明; 真机侧验证归并到 `LDD-ANDROID-LIVE` 第 9 步。
4. 文档化"Release 排除靠 #if + 构建期 guard, 验证靠 21894 无监听"。

验收:
- Release(非 Development)构建时, 若调试服务类型意外被引用, 构建以 `BuildFailedException` 失败。
- Dev Build 与 Editor 下编译与现有 8/8 测试不受影响。
- 端到端第 9 步(`LDD-ANDROID-LIVE`)真机确认 Release 下 21894 无监听。

依赖/阻塞: 与 `LDD-SYNCCTX-INIT` 同改 `LuaDeviceDebugPlayerBootstrap`/Runtime, 建议同批改以免 `#if` 冲突。设备侧最终确认依赖 `LDD-ANDROID-LIVE`。

#### Per-command 超时测试与 arg 校验职责文档化 [LDD-PER-COMMAND-TIMEOUT]

问题: 该项实为"补测试 + 补文档", 名字易误导成"要做 per-command 超时配置"。现状: 超时是**全局单一**值(`LuaDeviceDebugServer.cs:34-37` 构造 `m_TimeoutMs`, 默认 5000ms; `MainThreadDispatcher.Run` `:66-71` 用它), 设计 `:178-180` 也只要求统一 5 秒队列上限 16, 并无 per-command 粒度要求, 故无需新增功能。真正缺口有二: (1) 队列满路径 `MainThreadDispatcher.cs:34-38` 抛 429 `QUEUE_FULL`、超时路径 `:66-71` 抛 408 `EXECUTION_TIMEOUT`, 但 `Tests/Editor` 里**只有 408 测试**(`LuaDeviceDebugServerTests.cs:109-123` `Commands_TimeoutBeforeMainThreadRuns`), **没有任何 429 测试**——队列上限这条安全闸从未被自动化覆盖。(2) 参数校验职责边界未文档化: `JsonGuard.cs:8-9` 只管字节/深度/成员数限额, 不校验业务参数; 真正的 `INVALID_ARGUMENT`(arg 类型/必填)是 **Host/Lua dispatcher 职责**(设计 `:256-259` dispatcher"验证请求参数"), 传输层只在 `LuaDeviceDebugServer.cs:182-183` 校验"command 非空"。这条边界没在任何 README/设计落到文字, 容易让消费工程误以为传输层会替它校验 arg。

方案:
- 补 429 队列满测试: 构造一个会阻塞主线程的 FakeHost(在 `Execute` 里阻塞), 并发打满 16 个 in-flight 请求后第 17 个应得 429 `QUEUE_FULL`。坑: EditMode 主线程模型下要触发 `s_QueuedJobs > 16` 需精心编排——让主线程迟迟不 pump、HTTP 工作线程并发 Post; 可借 `LuaDeviceDebugServerTests` 已有的 `PostAsync` 多线程模式(`:125-146`)起多个并发请求。验证后 `s_QueuedJobs` 应回到 0(无泄漏)。
- 可选补 408 在"主线程长时间不空"场景的第二条路径(当前 408 测试覆盖的是"timeout 在主线程跑之前"`:110-123`, 另一条"主线程正在跑别的活"路径未覆盖)。
- 文档化职责边界: 在 README "Security Boundary"与样例 README 写明"arg schema 校验是 Host/Lua dispatcher 职责, 传输层只保证 JSON 限额与 command 非空; `INVALID_ARGUMENT` 由 Host 返回"。

改动文件:
- 修改 `Packages/com.yoji.lua-device-debug/Tests/Editor/LuaDeviceDebugServerTests.cs`(加 429 测试, 可选第二条 408)
- 可能修改 `Tests/Editor/FakeHost.cs`(加可阻塞模式)
- 修改 `Packages/com.yoji.lua-device-debug/README.md`(arg 校验职责段)

步骤:
1. 给 FakeHost 加"可阻塞 Execute"开关。
2. 写并发测试打满队列, 断言第 17 个请求 429 `QUEUE_FULL`、释放后 `s_QueuedJobs == 0`。
3. (可选)写"主线程忙时"的 408 路径测试。
4. README/样例 README 补 arg 校验职责文字。

验收:
- 新增 EditMode 测试: 第 17 个并发请求返回 HTTP 429、code `QUEUE_FULL`; 阻塞解除后队列计数归零。
- README 明确记载 arg schema 校验为 Host 职责、传输层不代劳。
- 现有 8/8 测试仍通过。

依赖/阻塞: 无前置, 仓内独立完成。文档段与 `LDD-ADAPTER-EXAMPLE` 样例 README 内容呼应, 建议同期写。

#### CommandDescriptor 死代码与敏感字段缺失 [LDD-CMDDESC-DEAD]

问题: `CommandDescriptor.cs:3-10` 定义了 `Name/Description/Mutating/ArgsSchemaJson/ResultSchemaJson` 五个字段的 C# 类, 但它**完全不在 wire 路径上**: 传输层从 Host 拿到的是 JSON 字符串(`ILuaDeviceDebugHost.DescribeCommands()` 返回 `string`), 服务直接对该 JSON 用 `FindMutatingFlag`(`LuaDeviceDebugServer.cs:224-238`)**只读 `name` 和 `mutating` 两个字段**, 从不构造或读取 `CommandDescriptor` 实例。全仓 grep 该类型无任何使用方。同时设计 `:206` 要求 descriptor"可选敏感字段声明", 而该类**没有 sensitive 字段**, 与设计的脱敏要求(`:302,413,570`)脱节。标记 partial(价值 2/成本 1, 低)。

方案: 两条路二选一, 推荐前者:
- (推荐)既然 wire 路径是纯 JSON、`CommandDescriptor` 是孤儿死代码, 直接**删除** `CommandDescriptor.cs`, 把"命令描述的权威形状"完全交给 wire JSON schema(由 `LDD-ADAPTER-EXAMPLE` 样例 + README 文档化), 避免一个永不被读的 C# 类误导消费工程以为要用它。删除前确认无 meta 引用残留。
- (备选)若想保留它作为消费工程可选的强类型构造辅助, 则补上 `Sensitive`(string[] 或 bool)字段对齐设计 `:206`, 并在样例里演示"用 `CommandDescriptor` 序列化出 `DescribeCommands()` JSON"的用法, 让它真正进入(可选)路径。但这会增加表面积且与"wire 是 JSON"的极简取舍相悖。

取舍: 设计哲学是"接口以 JSON 字符串为边界"(`:195`), 强类型 descriptor 与之冗余; 删除更符合 Simplify Ruthlessly。最终保留与否取决于样例是否需要它做构造辅助——若 `LDD-ADAPTER-EXAMPLE` 的 Lua dispatcher 直接产 JSON(更贴近真实 xLua 场景), 则 C# descriptor 无存在必要, 删之。

改动文件:
- 删除 `Packages/com.yoji.lua-device-debug/Runtime/CommandDescriptor.cs`(+ `.meta`) — 或 — 修改它补 `Sensitive` 字段并在样例使用

步骤:
1. 确认 `CommandDescriptor` 全仓零引用(已 grep 确认)。
2. 决策: 随 `LDD-ADAPTER-EXAMPLE` 一起定——样例若不需要 C# descriptor 则删除。
3. 删除文件与 meta, 或补 `Sensitive` 字段 + 样例用法。
4. README/设计文档同步(若删除, 移除对 `CommandDescriptor` 的提及)。

验收:
- 若删除: 编译通过、8/8 测试不受影响、文档无悬挂引用。
- 若保留: `Sensitive` 字段存在且样例演示了它如何产出脱敏后的 wire JSON。

依赖/阻塞: 决策与 `LDD-ADAPTER-EXAMPLE` 绑定(样例决定 descriptor 去留), 建议同批处理。

#### JsonGuard 错误码对齐设计错误码表 [LDD-ERRCODE-MAP]

问题: `JsonGuard.cs` 抛出的错误码偏离设计 `:386-396` 的标准错误码表: `:23` 抛 `RESULT_TOO_DEEP`、`:50` 抛 `INVALID_JSON_TYPE`, 而设计错误码表里**没有这两个**——设计只有 `RESULT_NOT_SERIALIZABLE`、`RESULT_TOO_LARGE`、`INVALID_REQUEST`、`INTERNAL_ERROR` 等。此外 `LuaDeviceDebugServer.cs:228,234` 抛的 `INVALID_COMMAND_DESCRIPTOR` 也是表外码。设计 `:398` 要求错误只返"错误码、消息和安全上下文", 错误码偏离表会让 CLI/agent 无法稳定按码分支。`JsonGuard.cs:57` 的 `RESULT_TOO_LARGE` 是对齐的, 但 `:23` 深度超限也归到 413 却用了表外码 `RESULT_TOO_DEEP`。标记 partial(价值 2/成本 1)。

方案: 把 `JsonGuard` 与 server 的错误码收敛到设计码表:
- 深度超限(`JsonGuard.cs:23`): 改用 `RESULT_TOO_LARGE` 或 `RESULT_NOT_SERIALIZABLE`(深度本质是不可序列化的一种), 取消 `RESULT_TOO_DEEP`; HTTP 仍 413。坑: 请求体 vs 响应体两个方向都走 `EnsureWithinLimits`(`LuaDeviceDebugServer.cs:135` 校验入站 body、`:220` 校验 Host 出站结果), 入站超限语义偏 `INVALID_REQUEST`(400)、出站超限偏 `RESULT_*`(413)。当前 `JsonGuard` 不区分方向, 一律用同一码——需评估是否按调用点传入方向参数, 让入站走 400/`INVALID_REQUEST`、出站走 413/`RESULT_TOO_LARGE`。
- 非法 JSON 类型(`JsonGuard.cs:50`): `INVALID_JSON_TYPE` 改为设计已有的 `RESULT_NOT_SERIALIZABLE`(出站)或 `INVALID_REQUEST`(入站)。
- `INVALID_COMMAND_DESCRIPTOR`(`LuaDeviceDebugServer.cs:228,234`): 设计码表无此码, 归并到 `INTERNAL_ERROR`(它本质是 Host 返回了不合规形状, 属服务/Host 内部错误, 500)。
- 同步设计文档错误码表或在文档加"传输层内部码"小节, 让码表成为单一事实源。

改动文件:
- 修改 `Packages/com.yoji.lua-device-debug/Runtime/JsonGuard.cs`(`:23,50` 错误码 + 可选方向参数)
- 修改 `Packages/com.yoji.lua-device-debug/Runtime/LuaDeviceDebugServer.cs`(`:228,234` 改码, 调用点传方向)
- 修改 `Tests/Editor`(若有断言这些码的测试)与设计文档错误码表

步骤:
1. 定稿映射: `RESULT_TOO_DEEP`->`RESULT_TOO_LARGE`/`RESULT_NOT_SERIALIZABLE`; `INVALID_JSON_TYPE`->`RESULT_NOT_SERIALIZABLE`/`INVALID_REQUEST`; `INVALID_COMMAND_DESCRIPTOR`->`INTERNAL_ERROR`。
2. (可选)给 `EnsureWithinLimits` 加方向参数, 入站/出站分别映射到 400/413 与对应码。
3. 改码并更新受影响测试; 确认 `LuaDeviceDebugServerTests.cs:105` 对 `INVALID_COMMAND_DESCRIPTOR` 的断言同步更新。
4. 同步设计文档错误码表。

验收:
- `JsonGuard` 与 server 抛出的所有错误码均在设计 `:386-396` 码表内(或文档明确登记为内部码)。
- 入站超限返 400/`INVALID_REQUEST` 系、出站超限返 413/`RESULT_*` 系, 方向语义正确。
- 相关测试更新后全绿。

依赖/阻塞: 注意 `LuaDeviceDebugServerTests.cs:97-107` 现断言 `INVALID_COMMAND_DESCRIPTOR`, 改码须同步该测试。无外部阻塞, 仓内独立。

#### /execute 双趟主线程跳消除 [LDD-EXECUTE-DOUBLE-DISPATCH]

问题: 每个 `/execute` 请求要做**两次主线程往返**: `LuaDeviceDebugServer.cs:189-192` 先派发 `host.DescribeCommands()` 到主线程查 `mutating` 标志, 拿到后 `:200-203` 再派发一次 `host.Execute(...)`。两次都过 `MainThreadDispatcher.Run`(各占一次 `s_QueuedJobs` 名额、各受 5s 超时与 16 上限约束)。这在设备上有两重代价: (1) 延迟翻倍、主线程被打断两次; (2) 队列压力翻倍——16 上限下并发能力实际减半, 且 describe 与 execute 之间存在 TOCTOU 窗口(命令集理论上可在两跳间变化)。设计 `:173-182` 期望命令是短操作、串行执行, 但没要求每请求两跳。标记 partial(价值 2/成本 2)。

方案: 把 mutation gate 检查并入单次主线程往返:
- 方案 A(推荐): 给 `ILuaDeviceDebugHost` 增加一个在主线程内"先查 mutating 再执行"的合并入口, 或让 `Execute` 自身在 Host 侧做双授权检查并通过错误码回报。但改接口影响契约稳定性(设计 `:184-195` 接口以 JSON 为边界, 不宜频繁动)。
- 方案 B(更轻、推荐落地): 在**一次** `MainThreadDispatcher.Run` 闭包内顺序调用 `DescribeCommands()` 解析 mutating + 条件性调用 `Execute()`, 把 `:189-203` 的两次 `Run` 合成一次。mutation 拒绝(403)在闭包内提前 return 一个哨兵, HTTP 层据此回 403。坑: 闭包内既要能返回"403 拒绝"也要能返回"执行结果", 需用一个能表达两种结局的返回类型(如 `JToken` + 状态枚举, 或抛 `LuaDeviceDebugException(403,...)` 由外层捕获——后者更简洁, 利用已有 `HandleClient` 的 `catch (LuaDeviceDebugException)` `:114-117`)。
- 方案 C: 在 HTTP 工作线程缓存命令集的 mutating map(带 TTL/失效), 避免每请求都查 describe。但缓存失效与 Host 热重载命令集的一致性复杂, 成本高于收益, 不推荐首版。

取舍: 方案 B 最小改动且消除 TOCTOU(describe 与 execute 在同一主线程切片内原子完成), 推荐。

改动文件:
- 修改 `Packages/com.yoji.lua-device-debug/Runtime/LuaDeviceDebugServer.cs`(`Execute` 合并两次 `Run` 为一次, `:189-203`)

步骤:
1. 重构 `Execute()`: 单个 `MainThreadDispatcher.Run` 闭包内 describe -> 查 mutating -> 命令不存在抛 404、未授权抛 403、否则 execute 并返回结果。
2. 用 `LuaDeviceDebugException` 表达 403/404 拒绝, 由 `HandleClient` 既有 catch 处理。
3. 确认 `elapsedMs` 仍只计 execute 时间(当前 `:199` 的 `Stopwatch` 语义)。
4. 跑现有 `Execute_*` 测试(`LuaDeviceDebugServerTests.cs:62-107`)确保 200/403/500 行为不变。

验收:
- `/execute` 每请求只占用一次主线程往返(可在测试中用计数 Host 断言 `DescribeCount` 与 `ExecuteCount` 在单请求内各 +1 且发生在同一主线程切片)。
- 现有 `Execute_ReadOnly`/`MutatingWithoutAllow`/`InvalidCommandDescriptor` 测试全绿, 状态码不变。
- 16 队列上限下并发能力不再因双跳减半。

依赖/阻塞: 与 `LDD-ERRCODE-MAP` 同改 `LuaDeviceDebugServer.cs` 的 `INVALID_COMMAND_DESCRIPTOR` 路径, 建议同批; 与 `LDD-PER-COMMAND-TIMEOUT` 的 429 测试相互印证(双跳消除后队列计数行为更清晰)。仓内独立。

#### Skill 资产补全: references/scripts + 错误码文档 [LDD-SKILL-ASSETS]

问题: 设计仓库结构 `:105-116` 规定 `Agent~/skills/unity-lua-device-debug/` 下应有 `scripts/` 与 `references/`, 但当前**只有 `client.py` 和 `SKILL.md`**(已确认 `scripts/`、`references/` 目录不存在)。后果: (1) agent 没有错误码参考表, 无法把 HTTP 状态/error code 稳定映射成可执行决策——而 CLI 退出码与 `ok` 一致(`client.py:81-84`)、HTTP 码语义全在设计 `:370-396` 散着, 缺一份机读/人读的 references; (2) 没有 `android-e2e`、`troubleshooting` 等操作脚本/文档, agent 上手 adb forward 排错全靠试。`SKILL.md` 本身较薄(`:1-33`), 未覆盖错误码、超时/队列满/host 未就绪的应对。标记 partial(价值 2/成本 2)。

方案: 补齐 skill 资产, 对齐另两个工具包(editor-debug/test-runner 已有 references 的)惯例:
- `references/error-codes.md`: 把设计 `:370-396` 的 HTTP 状态码表 + 错误码表落成单一参考, 每码给"含义/触发场景/agent 应对"(如 409 `HOST_NOT_READY` -> 等待 Lua VM 启动后重试; 429 `QUEUE_FULL` -> 退避重试; 403 `MUTATION_DENIED` -> 需 `--allow-mutation`)。与 `LDD-ERRCODE-MAP` 收敛后的码表保持一致(单一事实源)。
- `references/android-e2e.md`: 即 `LDD-ANDROID-LIVE` 的 checklist, 此处复用。
- `references/troubleshooting.md`: adb 未找到/零设备/多设备/forward 冲突(对应 `client.py:170-202` 的 `NO_DEVICE`/`MULTIPLE_DEVICES`/`ADB_FORWARD_CONFLICT`)、连接失败 `CONNECTION_FAILED`(`client.py:54-55`)的排查。
- 视需要 `scripts/`: 若有可复用的 e2e/烟测脚本放此(参考 test-runner 的 `references/run-e2e.py` 惯例); 首版可只放一个 `smoke.py` 串 ping->commands->execute。
- 扩充 `SKILL.md`: 增加"错误码速查""典型故障处置"指引, 链到 references。

改动文件:
- 新建 `Agent~/skills/unity-lua-device-debug/references/error-codes.md`
- 新建 `references/troubleshooting.md`
- 新建(与 `LDD-ANDROID-LIVE` 共用)`references/android-e2e.md`
- 可选新建 `scripts/smoke.py`
- 修改 `Agent~/skills/unity-lua-device-debug/SKILL.md`

步骤:
1. 从设计 `:370-396` + `LDD-ERRCODE-MAP` 收敛结果生成 `error-codes.md`。
2. 从 `client.py` 的 adb/连接错误分支生成 `troubleshooting.md`。
3. (可选)写 `smoke.py` 串三端点。
4. 扩充 `SKILL.md` 链接 references。

验收:
- `references/` 含 error-codes、troubleshooting(+android-e2e), 错误码表与传输层实际抛出的码逐一对应(`LDD-ERRCODE-MAP` 落地后核对)。
- `SKILL.md` 覆盖错误码速查与典型故障处置。
- (若有)`scripts/smoke.py` 能对在线服务跑通 ping->commands->execute 并以退出码反映结果。

依赖/阻塞: `error-codes.md` 内容依赖 `LDD-ERRCODE-MAP` 收敛后的最终码表(否则文档会记录待改的旧码); `android-e2e.md` 与 `LDD-ANDROID-LIVE` 共用。建议在 `LDD-ERRCODE-MAP` 之后写文档。

### 本章顺序与工作量

推荐内部顺序(先打地基、再修传输层、最后写文档与真机):

1. `LDD-ADAPTER-EXAMPLE`(M, 价值5)— 最高优先级, 端到端可用的钥匙, 是 `LDD-ANDROID-LIVE`/`LDD-CMDDESC-DEAD`/`LDD-SKILL-ASSETS` 的 wire 形状权威源, 先做。
2. `LDD-SYNCCTX-INIT`(S-M, 价值4)+ `LDD-RELEASE-BUILD-VERIFY`(S-M)— 同批改 `LuaDeviceDebugPlayerBootstrap`/Runtime 的 `#if` 与初始化, 避免守卫互相冲突, 合并一次过。
3. `LDD-EXECUTE-DOUBLE-DISPATCH`(S)+ `LDD-ERRCODE-MAP`(S)— 同批改 `LuaDeviceDebugServer.cs`(都动 `INVALID_COMMAND_DESCRIPTOR`/错误路径), 一次重构到位。
4. `LDD-PER-COMMAND-TIMEOUT`(S)— 补 429 测试 + arg 职责文档, 与上一步队列行为相互印证。
5. `LDD-CMDDESC-DEAD`(XS)— 随 `LDD-ADAPTER-EXAMPLE` 决策删除或补字段, 顺手做。
6. `LDD-SKILL-ASSETS`(S-M)— 在 `LDD-ERRCODE-MAP` 收敛后写 error-codes 文档, 收尾。
7. `LDD-ANDROID-LIVE`(L, 跨工程/真机)— 仓内只交付 android-e2e checklist; 真机闭环依赖消费工程实现 xLua adapter + 出 Dev Build, 属本仓库范围外, 放最后并行推进。

粗估工作量: 仓内可独立完成部分约 M-L(参考样例占大头, 其余多为 S/XS 的修补与测试); 跨工程的 `LDD-ANDROID-LIVE` 真机闭环不计入仓内人天, 但是"端到端可用"状态从 transport-only 升级为 device-verified 的唯一证明步骤。

跨章依赖:
- 与 WS(test-runner / editor-debug 所在章)有**架构层共性**: 三个工具各自维护一份 `MainThreadDispatcher.cs` 与 loopback HTTP 脚手架(见 `progress.md:95-97` ARCH-1 `editor-core` 抽取计划)。本章 `LDD-SYNCCTX-INIT`/`LDD-EXECUTE-DOUBLE-DISPATCH` 对 dispatcher 的加固若与 ARCH-1 抽取同期发生, 需协调谁先动 dispatcher, 避免重复劳动或抽取后回退。
- `LDD-ANDROID-LIVE` 依赖消费工程(`SLG_Prototype`)与跨工程协作通道(`docs/superpowers/specs/2026-06-13-cross-project-collaboration-solution.md`), 与"跨工程协作/u3d-ai-linker"相关章节存在交接依赖。

## 10. 协作工作流落地

> 工作流 id: WS7 | 依赖: WS6(u3d-ai-linker;仅 7.3 通道切换的自动化执行被其阻塞) | 工作量: 约 1 人天(7.1=M, 7.2=S, 7.3=S);全文档/配置,零生产代码

本章把跨工程协作方案(以下简称 COLLAB,即 `docs/superpowers/specs/2026-06-13-cross-project-collaboration-solution.md`)从一份纯描述性文档,落地成仓库里真实存在、可被 AI 与人反复执行的机制。COLLAB 第 5 节给出了 "AI 做文件级工作 + 人做 git 写" 的协作契约,但全篇没有产出任何模板、脚本或目录约定;`_planning/handoff.md` 这个被反复引用的核心制品(COLLAB:116-117、COLLAB:172)在仓库里根本不存在。本章三个子项分别解决:交接制品的物化、planning 产物的可追踪性、以及通道切换机制(后者受 WS6 阻塞,本章只做可现在落地的部分并把边界划清)。

#### 7.1 物化 handoff 交接制品: 模板 + AI 写入例程 + 人执行入口

问题:

- COLLAB:116-117 把 "文件化交接清单(handoff)" 列为协作契约第 2 条核心机制,要求 AI 完成一批改动后 "写一个文件(如 `_planning/handoff.md`),列出: 改了哪些文件、属于哪个工程、建议的提交信息、建议的 tag、以及人需要在终端执行的 git 命令"。COLLAB:172 的速查表又把 "AI 写 handoff 清单" 定为提交/推送/打 tag 的标准路径。
- 但全仓库搜索 `handoff` 只命中 COLLAB 这一个文件自身(grep `handoff` --include=*.md 仅返回 `2026-06-13-cross-project-collaboration-solution.md`),没有任何模板文件、没有字段定义、没有 AI 生成它时该遵循的例程。`_planning/` 当前只有 `mission_plan.md` 与 `mission_notes.md`(均为某次 mission-runner 任务的产物),没有 `handoff.md`。
- 后果: "写 handoff" 是一句口头约定。每次 AI 自由发挥格式,人无法预期清单结构,也没有 "AI 用文件通道只读自检 git 状态" 的固定步骤(COLLAB:121-123、第 8 节速查表要求读 `.git/refs/heads/main` + `.git/refs/remotes/origin/main` + `.git/logs/HEAD`,但没有落成可执行模板)。这正是 COLLAB 想根治的 "把正确性押在 shell 回显上" 的反面没有被工具化。

方案:

- 新建一份 handoff 模板 `docs/superpowers/templates/handoff.template.md`,作为 AI 生成 `_planning/handoff.md` 的唯一格式来源。模板必须是确定性结构,字段直接对应 COLLAB:116-117 的清单要求,且把 COLLAB 第 5 节的失败语义编码进去。建议字段(用占位符 `{{...}}`):
  - `## 改动批次` — 时间戳、所属工程(`U3D-Dev-Tools-AI` 还是某游戏工程)、关联的 spec/mission。
  - `## 改动文件` — 按工程分组的文件清单,每条带一句改动意图;明确标注哪些是内循环 `file:` 包内改动、哪些需要外循环发布。
  - `## git 自检快照(AI 只读文件通道得出)` — 三个引用的原始值: `.git/refs/heads/<branch>`、`.git/refs/remotes/origin/<branch>`、`.git/logs/HEAD` 末段;以及由此推导的 "本地领先 N 提交、上次确认远端在 X"。模板里写死这三条读法,杜绝 AI 用 `git status` 回显(COLLAB:169-170 明令禁止)。
  - `## 建议提交信息` / `## 建议 tag` — tag 必须符合 linker 设计的 `<tool-id>-v<semver>` 前缀约定(linker 设计 `2026-06-12-u3d-ai-linker-design.md`:113、:136-140),为外循环发布对齐。
  - `## 人需在终端执行的 git 命令` — 可直接复制粘贴的命令块,末尾固定附 COLLAB:153-159、第 7 节的文件通道核实步骤(push 后读 `.git/refs/remotes/origin/main` 确认引用前进)。
  - `## 未确认项` — 对应 COLLAB:126 的失败语义,凡 AI 无法用文件通道交叉核实的结果一律进这里,不进 "已完成"。
- 在 COLLAB 文档新增一节 "5.x handoff 生成例程",把 "AI 何时写、写在哪、读哪几个文件做自检、写完如何提示人" 固化为有序步骤,并从模板反向链接回 COLLAB。这一步是把口头契约升级为文档化 SOP。
- 关键取舍: 模板放 `docs/superpowers/templates/` 而非 `_planning/`,因为模板是要长期纳入版本管理、跨任务复用的资产;而 `_planning/handoff.md` 是每次任务的一次性实例(且当前被 gitignore,见 7.2)。模板与实例分离,避免把 "格式标准" 和 "本次清单" 混为一谈。
- 坑: 不要让模板退化成又一份散文。字段必须机器友好(固定标题层级、固定占位符),这样未来 7.3 的 linker 或任何脚本才可能解析 handoff 的 "建议 tag" 字段去驱动通道切换。模板里 git 命令块要按工程拆分(工具 monorepo 与游戏工程是两个仓库),避免人把命令跑错目录。

改动文件:

- 新建 `docs/superpowers/templates/handoff.template.md`(模板本体)。
- 修改 `docs/superpowers/specs/2026-06-13-cross-project-collaboration-solution.md`(新增 "5.x handoff 生成例程" 小节,并在第 5 条第 2 点处补一个指向模板的链接)。

步骤:

1. 依据 COLLAB:116-117 + 第 5 节失败语义,确定 handoff 的最小字段集与标题层级。
2. 写 `docs/superpowers/templates/handoff.template.md`,把三条 git 引用文件读法、tag 前缀约定、未确认项区块全部编码为占位符结构。
3. 在 COLLAB 增 "5.x handoff 生成例程" 小节,描述触发时机/写入位置/自检读法/人提示话术,并双向链接模板。
4. 用一次真实改动做 dry-run: 让 AI 按例程读三个引用文件、填出一份样例 `_planning/handoff.md`,人按命令块执行后读 `.git/refs/remotes/origin/main` 验证引用前进。

验收:

- `docs/superpowers/templates/handoff.template.md` 存在,且含 "改动文件 / git 自检快照(三引用读法) / 建议提交信息 / 建议 tag(带 `<tool-id>-v<semver>` 约定) / 人需执行命令 / 未确认项" 全部区块。
- COLLAB 文档存在 "handoff 生成例程" 小节并链接到模板;模板内不出现任何 `git status`/`git rev-parse` 作为自检手段(只允许读引用文件)。
- dry-run 产出的样例 `_planning/handoff.md` 完整可执行: 人按其命令块 push 后,读 `.git/refs/remotes/origin/main` 能看到引用前进到预期 commit;若未前进,handoff 的 "未确认项" 能解释。

依赖/阻塞:

- 无前置阻塞,可立即开工。tag 字段格式与 WS6(linker)的 Registry revision 约定耦合,但此处只是写约定文本,不依赖 linker 已构建。

#### 7.2 让 planning 产物可追踪: 拆分 .gitignore 的 _planning 一刀切

问题:

- `.gitignore:11` 是一行 `_planning/`,把整个 `_planning/` 目录无差别忽略。`git check-ignore -v _planning/handoff.md` 确认任何放进该目录的文件(包括 handoff)都不会被追踪;`git log --all -- _planning/` 为空,证明 `_planning/` 从未进过版本历史。
- 当前 `_planning/` 里有 `mission_plan.md`(11.5K)与 `mission_notes.md`(8.2K),是 mission-runner 的 filesystem-as-memory 中间态 — 这类临时草稿被忽略是合理的。但 COLLAB 的整个协作设计把 handoff 当作 "AI 意图与人执行之间的可靠桥梁"(COLLAB:118-119),桥梁却落在一个 git 永远看不见的目录里,产生根本矛盾: AI 写的交接清单无法被提交、无法跨机器/跨会话被人或另一个 AI 复查、人执行完也无法在历史里留痕。COLLAB 第一性原则把文件通道当 "主干"(COLLAB:44),却把主干产物挡在版本控制之外。
- 这是个 "对错混在一行" 的问题: 临时 mission 草稿该忽略,但可追踪的协作制品(handoff、发布记录)不该忽略。一行 `_planning/` 没有区分能力。

方案:

- 把 `.gitignore:11` 的粗粒度 `_planning/` 改为 "默认忽略目录内容、但显式放行需要追踪的协作制品" 的白名单模式。具体写法:
  - `_planning/` (忽略目录下全部)
  - `!_planning/handoff.md` (放行交接清单)
  - 视需要再放行一个发布/同步记录(如 `!_planning/release-log.md`),用于沉淀 "哪个 tag 何时由谁 push、文件通道核实结果如何"。
  - `mission_plan.md` / `mission_notes.md` 等一次性草稿继续被忽略(不加 `!` 例外)。
- 取舍一(放行清单 vs. 迁出目录): 另一种做法是把 handoff 移出 `_planning/` 到一个本就被追踪的目录(如 `docs/handoffs/`)。否决理由: COLLAB 全篇(:116、:172)已把路径写死为 `_planning/handoff.md`,迁目录要改文档+改用户肌肉记忆,收益不抵成本;且 `_planning/` 作为 "本次任务工作区" 的语义本身合理,问题只在 ignore 粒度。用 gitignore 白名单精准放行,改动面最小。
- 取舍二(单文件放行 vs. 全目录追踪): 不要简单删掉 `_planning/` 这行让整个目录入库 — 那会把 mission-runner 的大量易变中间态(notes 每轮迭代都重写)灌进 git,污染历史。白名单是唯一兼顾 "草稿忽略 + 制品追踪" 的方案。
- 坑: gitignore 的 "先忽略目录、再用 `!` 放行目录内文件" 有一个经典陷阱 — 若父目录被 `dir/` 形式忽略,Git 不会递归进入该目录,导致 `!dir/file` 失效。必须用 `_planning/` + `!_planning/handoff.md` 这种 "忽略的是目录内容而非目录本身可进入性" 的写法,并实测 `git check-ignore -v _planning/handoff.md` 返回 "不被忽略" 才算对。这一点必须在验收里硬验。

改动文件:

- 修改 `.gitignore`(把第 11 行 `_planning/` 改为忽略 + 白名单放行块)。

步骤:

1. 编辑 `.gitignore`,将 `_planning/` 替换为 `_planning/` 后紧跟 `!_planning/handoff.md`(及可选的 `!_planning/release-log.md`)。
2. 运行 `git check-ignore -v _planning/handoff.md`,确认它不再被忽略(命令应无输出或显示放行规则);同时确认 `git check-ignore -v _planning/mission_notes.md` 仍被第 11 行忽略。
3. 用一份 7.1 产出的样例 `_planning/handoff.md` 跑 `git add -n _planning/handoff.md`,确认 Git 愿意暂存它。
4. 在 COLLAB 文档相应位置补一句说明: handoff 是被追踪制品,mission 草稿仍忽略,二者由 .gitignore 白名单区分。

验收:

- `git check-ignore -v _planning/handoff.md` 不再命中忽略规则(可暂存);`git check-ignore -v _planning/mission_notes.md` 与 `_planning/mission_plan.md` 仍命中忽略(保持忽略)。
- `git add -n _planning/handoff.md` 显示该文件会被加入暂存,不报 "paths are ignored"。
- 改动只触及 `.gitignore` 一处,不误放行 mission 草稿。

依赖/阻塞:

- 依赖 7.1: 放行 `handoff.md` 的前提是这个文件有明确格式和用途(模板已定义),否则放行一个无定义的文件没有意义。建议 7.1 与 7.2 同批提交。
- 无外部阻塞。

#### 7.3 dev(file:)/stable(#tag) 通道切换: 现在固化约定, 机制等 WS6 linker

问题:

- COLLAB:101-108 描述了 dev/stable 双通道: dev 用 `file:` 本地引用(COLLAB:102、:68 的实例 `"com.yoji.lua-device-debug": "file:../../../Packages/com.yoji.lua-device-debug"`),stable 用 `#<tag>` Git URL(COLLAB:104)。COLLAB:105-108 明确说 "这正好接 u3d-ai-linker 设计的双通道",即切换机制本身要由 linker 实现(Dev 通道固定到 main 的 commit SHA、Stable 固定到 tag)。
- 但 linker 尚未构建: `Packages/` 目录下只有 `com.yoji.editor-debug`、`com.yoji.lua-device-debug`、`com.yoji.test-runner` 三个包,没有 `com.yoji.u3d-ai-linker`;根目录也没有 `Registry/`(linker 设计 :40-43 要求的 `Registry/stable.json`+`dev.json` 全缺)。这与任务前提 "u3d-ai-linker 尚未构建" 一致。
- 因此通道切换的执行机制(Install/Refresh、改写 manifest、`git ls-remote` 取 SHA、串行 `Client.Add`)是 WS6 linker 的实现范围,本章无法、也不应在 linker 缺位时另起炉灶手搓一个会被 linker 取代的临时切换脚本。
- 但有一部分现在就能做且不依赖 linker: 把 "什么时候从 file: 切到 #tag、tag 怎么命名、切换前后如何用文件通道核实" 这套人工 SOP 写进 handoff 流程。COLLAB:86-98 的外循环步骤(人打 tag→游戏工程切依赖→读 `.git/refs/remotes/origin/main` 核实)目前也只是散文,没有进入 7.1 的可执行 handoff 模板。

方案:

- 划清边界,分两段:
  - 现在做(不依赖 linker): 在 7.1 的 handoff 模板里增加一个可选的 "## 通道切换建议" 区块。当一批改动达到可发布里程碑(COLLAB:88),AI 在 handoff 中给出: 建议的 tag(`<tool-id>-v<semver>`,对齐 linker 设计 :136-140)、人需在工具库执行的打 tag+push 命令、以及游戏工程 manifest 里从 `file:` 切到 `https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/<pkg>#<tag>` 的具体改写行(URL 格式严格照 linker 设计 :120-124)。这把外循环 SOP(COLLAB:86-98)从散文变成 handoff 里可复制的命令,是纯人工通道、零 linker 依赖。
  - 等 WS6 做(linker 范围,本章只登记依赖): 自动改写 manifest、`Refresh Dev` 取 main 最新 SHA、串行安装与域重载恢复、回滚到上一 tag/file: — 这些全部由 linker 实现,本章在依赖里指明 "通道切换的自动化执行由 WS6 交付",不在 WS7 内重复实现。
- 取舍: 抵制 "linker 没好,先写个 PowerShell 帮我改 manifest" 的诱惑。理由: linker 设计 :108-118 对 manifest 改写有严格白名单校验(仓库固定、packageName 前缀、路径禁 `..`、dev 必须解析成 40 位 SHA),临时脚本若不实现这些校验会引入安全/正确性漏洞,且最终必被 linker 取代,是负资产。本章只交付 "人能照着手动切" 的 SOP,把自动化让位给 WS6。
- 坑: handoff 里给出的 `#<tag>` URL 必须用 linker 设计钦定的精确格式(`...?path=/Packages/<package>#<revision>`,设计 :122-124),否则人手动改完 manifest,将来 linker 接管时会因 URL 形态不一致而判为 "非托管依赖" 拒绝管理。现在就对齐格式,为 linker 接管铺路。另一个坑: COLLAB:83 的约束 — `file:` 是本机路径,换机即断,绝不能提交进游戏工程的共享/CI 依赖。handoff 的通道切换区块必须显式提醒 "提交进共享依赖的只能是 #tag,file: 仅本机内循环"。

改动文件:

- 修改 `docs/superpowers/templates/handoff.template.md`(新增 "## 通道切换建议" 可选区块,含 tag 命名、打 tag 命令、manifest 改写行、file: 不可提交的警示)。
- 修改 `docs/superpowers/specs/2026-06-13-cross-project-collaboration-solution.md`(在 4.3 节末尾补一句: 自动切换由 u3d-ai-linker 实现,linker 落地前用 handoff 的通道切换区块手动执行;并交叉链接 linker 设计的 URL 格式)。

步骤:

1. 在 handoff 模板加 "## 通道切换建议" 区块,字段: 建议 tag、工具库打 tag+push 命令、游戏工程 manifest 改写前后行对照、file: 不可入共享依赖的警示。
2. URL 格式严格抄 linker 设计 :120-124,确保与未来 linker 生成的 URL 字节级一致。
3. 在 COLLAB 4.3 节标注 "自动化执行依赖 WS6 linker;当前为手动 SOP"。
4. 等 WS6 linker 交付后,回到本子项把 "手动改 manifest" 替换为 "经 Linker Install/Refresh"(此步登记为 WS6 完成后的后续动作,不在本章工作量内)。

验收:

- handoff 模板含 "通道切换建议" 区块,其中 `#<tag>` URL 形态与 `2026-06-12-u3d-ai-linker-design.md`:122-124 给出的格式逐字一致;区块内含 "file: 仅本机、不可提交进共享依赖" 的明确警示(对齐 COLLAB:83)。
- COLLAB 4.3 节明确标注通道切换的自动化执行属于 WS6 linker 范围,linker 落地前用手动 SOP。
- 不新增任何会改写 Unity manifest 的脚本/代码(确认本章未越界进入 linker 实现范围)。

依赖/阻塞:

- 阻塞(自动化部分): u3d-ai-linker 尚未构建(`Packages/` 无 `com.yoji.u3d-ai-linker`、根目录无 `Registry/`)。通道切换的自动化执行(Install/Refresh/Refresh Dev、manifest 安全改写、回滚)由 WS6 交付,本章不实现。
- 依赖 7.1: 通道切换建议作为 handoff 模板的一个区块存在,必须先有模板。
- 现在可做的部分(手动 SOP 文本 + URL 格式对齐)无阻塞,可与 7.1/7.2 同批完成。

### 本章顺序与工作量

推荐内部顺序: 7.1 → 7.2 → 7.3。7.1 先把 handoff 模板物化(其余两项都挂在模板上),7.2 紧跟着放行该制品(无模板则放行无意义,二者宜同批提交),7.3 在模板已有的前提下追加通道切换区块并划清 linker 边界。

粗估工作量:

- 7.1 handoff 模板 + 例程: M(模板设计要确定性、要把三引用自检读法编码进去,且需一次真实 dry-run 验证 push 后引用前进)。
- 7.2 .gitignore 白名单: S(单行改动,核心在用 `git check-ignore` 实测白名单生效、避开目录忽略递归陷阱)。
- 7.3 通道切换 SOP: S(纯文档,抄齐 linker 设计的 URL 格式 + 划清 WS6 边界;自动化部分不在本章)。
- 本章合计约 1 人天,产出全部为文档/配置,零生产代码,可验证性高。

跨章依赖:

- 依赖 WS6(u3d-ai-linker): 仅 7.3 的通道切换 "自动化执行" 部分被 WS6 阻塞;本章只交付手动 SOP 与格式对齐,WS6 落地后需回补一次 "手动改 manifest → 经 Linker 执行" 的替换(登记为 WS6 后续动作)。
- 与发布相关工作流的接口: 7.1 handoff 的 "建议 tag" 字段采用 `<tool-id>-v<semver>` 约定,与 WS6 linker 的 Registry stable revision 校验(linker 设计 :113)及任何外循环发布章节共用同一 tag 命名空间,需保持一致。
- 不依赖三个 HTTP 工具(test-runner/editor-debug/lua-device-debug)的运行态: 本章产物均为文件通道资产,符合 COLLAB 把正确性押在文件通道而非 shell 的第一性原则。

## 附录 A. 全量项索引

下表汇总全部审计项。价值/成本为 1-5 评分(越高越大),状态取审计核实后的真实状态,Phase 对应第 3 节里程碑。

| id | 主题 | 价值 | 成本 | 状态 | 所属 Phase |
|----|------|:----:|:----:|------|:----------:|
| TR-e2e-rerun | run-e2e.py 未在在线 Editor 重跑(TR-2/3/4 落地后) | 4 | 2 | landed-needs-verify | A |
| HYG-VERIFY-DEBT | 重跑完整 EditMode+e2e,更新旧基线计数 | 4 | 3 | landed-needs-verify | A |
| ED-6.4-COMPILE | Unity 6.4 entity-id 分支真机编译验证 | 4 | 2 | landed-needs-verify | A |
| ED-1-VERSION-ROBUSTNESS | /console 内部 API 跨版本健壮性(字段名/位掩码) | 3 | 2 | landed-needs-verify | A |
| HYG-BATCHMODE-NOT-WIRED | headless batchmode runner 接 CI + 去硬编码 Unity 路径 | 3 | 3 | partial | A |
| ARCH-1-upm-sibling-dep-blocker | Git-URL UPM 同级依赖不解析(core 依赖硬前置) | 4 | 2 | not-started | A(决策)/B |
| ARCH-1-envelope-guard | 抽取时保留刻意的信封分歧(恒200 vs 真状态码) | 4 | 1 | not-started | A(决策)/B |
| ARCH-1a | MainThreadDispatcher 抽入 com.yoji.editor-core(零风险) | 3 | 1 | not-started | B |
| ARCH-1b-host | 抽 LoopbackHttpHost + RecompilePrimitive(留 on-start hook) | 3 | 4 | not-started | B |
| ARCH-1-registry-model | 把 editor-core 建模成无 Skill 的 infra 包(OQ3) | 2 | 3 | not-started | B |
| TR-1a | PlayMode via DisableDomainReload spike + 实现 | 4 | 3 | not-started | C |
| TR-1b | SessionState 跨域重载保存 active jobId | 3 | 4 | not-started | C(条件触发) |
| TR-1c | 域重载后重挂 RunFinished + init-timeout 收尾 | 3 | 4 | not-started | C(条件触发) |
| TR-listtests-null-api | /list-tests 无 Idle 前置守卫,编译中可阻塞 30s | 2 | 2 | partial | A |
| TR-5 | /cancel(跳过理由成立;唯一杠杆是 k_StaleJobMs 可配) | 2 | 3 | skip-candidate | A |
| TR-client-groups | client.py 缺 --groups; mode 面不一致 | 1 | 1 | partial | A |
| TR-failmsg-mismatch | completed message 固定串 vs SKILL.md 写有耗时 | 1 | 1 | partial | A |
| ED-4 | 只读模式 / 方法 denylist(opt-in 默认关) | 3 | 2 | not-started | C |
| ED-6 | /describe 成员过滤 + 多类型批量 | 3 | 2 | not-started | C |
| ED-2-SERIALIZE-DEDUP | 统一 /invoke 与 /batch 的 SerializeRaw | 2 | 1 | partial(实为 done) | C |
| ED-DESCRIBE-OVERLOAD-COLLISION | describe 与 invoke 成员枚举不一致(无 base-walk) | 2 | 2 | not-started | C |
| ED-EVAL-LIMITATIONS | /eval 仅支持 Type.Member(args)链(已接受跳过 Roslyn) | 2 | 1 | skip-candidate | C(skip) |
| LDD-ADAPTER-EXAMPLE | 参考 ILuaDeviceDebugHost 适配器 + Lua dispatcher/serializer 样例(Samples~) | 5 | 3 | not-started | C |
| LDD-SYNCCTX-INIT | IL2CPP/Android 上 AfterAssembliesLoaded 抓 SyncContext 可能为 null | 4 | 2 | landed-needs-verify | C |
| LDD-ANDROID-LIVE | Android Dev Build 真机全流程从未验证 | 5 | 4 | partial | C |
| LDD-RELEASE-BUILD-VERIFY | Release 排除需构建期断言而非仅 #if | 3 | 2 | partial | C |
| LDD-PER-COMMAND-TIMEOUT | 补 429/408 EditMode 测试 + 文档化 arg 校验为 host 职责 | 3 | 2 | partial | A |
| LDD-CMDDESC-DEAD | CommandDescriptor 未用于 wire 路径 + 缺 sensitive 字段 | 2 | 1 | partial | A |
| LDD-ERRCODE-MAP | JsonGuard 错误码偏离设计错误码表 | 2 | 1 | partial | A |
| LDD-EXECUTE-DOUBLE-DISPATCH | /execute 每请求两趟主线程跳查 mutating | 2 | 2 | partial | A |
| LDD-SKILL-ASSETS | Skill 缺 references/scripts + 错误码文档 | 2 | 2 | partial | A |
| LINK-0-probe | Agent~/BundledSkills~ Git-Package-Cache 存活探针(硬前置) | 5 | 2 | not-started | D |
| LINK-1-skeleton | linker 包骨架(com.yoji.u3d-ai-linker) | 5 | 2 | not-started | D |
| LINK-2-registry-schema | Registry schema + stable/dev.json + 严格白名单校验 | 5 | 3 | not-started | D |
| LINK-3-upm-queue | 持久化 UPM 安装队列 + 域重载恢复 | 5 | 5 | not-started | D |
| LINK-4-skill-copy | 事务式 skill 复制 + ownership + Windows junction | 5 | 4 | not-started | D |
| LINK-5-fragment-merge | CLAUDE/AGENTS 托管区块合并 + .gitignore 块 | 4 | 3 | not-started | D |
| LINK-6-project-settings-provider | Project Settings 面板(Edit > Project Settings > U3D AI Linker) | 4 | 3 | not-started | D |
| LINK-7-tool-migration | 工具 planned->skill-only/ready(fragments+tags+status) | 4 | 3 | not-started | D |
| LINK-8-editor-tests | linker Editor 测试套件 | 4 | 3 | not-started | D |
| HYG-COLLAB-NOT-OPERATIONAL | 协作落地: handoff.md + 可追踪 planning + 通道切换 | 3 | 3 | not-started | E |
