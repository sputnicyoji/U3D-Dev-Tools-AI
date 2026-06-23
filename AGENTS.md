# Repository Guidelines

## Project Purpose

本仓库是面向 AI 辅助工作流的 Unity3D 自动化开发工具 monorepo。四个 UPM 包让 AI agent 不经 GUI 操作运行中的 Unity Editor，并把工具批量接入目标工程：

- `com.yoji.test-runner`：headless 跑 EditMode 测试 + 触发重编译（HTTP 21890）。
- `com.yoji.editor-debug`：任意 C# 反射调用 / 检视 Unity 类型与成员（HTTP+JSON 21891）。
- `com.yoji.lua-device-debug`：Unity Lua 运行时诊断的通用传输层（HTTP+JSON 21894）。
- `com.yoji.u3d-ai-linker`：Editor-only 编排包，按 Registry 白名单批量安装上述工具并同步 Skills/规则到 Claude Code / Codex。

目标环境：Windows + Unity 2022.3+ + Python 3.8+。`lua-device-debug` 已在 Unity 2022.3.62f2c1 Git UPM 安装和 EditMode 中验证，但目标工程必须注册 `ILuaDeviceDebugHost`。仅面向**非 HybridCLR** 工程；调试服务只绑定 127.0.0.1。每个包应保持可独立理解、验证和迁移。

## Project Structure & Module Organization

```text
Packages/com.yoji.{test-runner,editor-debug,lua-device-debug,u3d-ai-linker}/
  Editor/ | Runtime/       # Unity 侧 C# 服务/编排代码 + asmdef
  Tests/Editor/            # EditMode 测试
  Agent~/skills/<skill>/   # SKILL.md + client.py + references/（~ 目录不进 Unity 编译）
Registry/{stable,dev}.json     # 双通道分发清单（u3d-ai-linker 消费）
TestProjects/<tool>/           # 各包独立 headless 测试宿主（file: 引用对应包）
tools/run-editmode.ps1         # headless EditMode 测试入口
docs/superpowers/{specs,plans}/
README.md, progress.md
```

新增工具按 `Packages/com.yoji.<tool>/` 建包，Agent 资产统一放 `Agent~/skills/<skill>/`。不依赖仓库外隐式相对路径；外部 UPM 包、服务端或私有仓库必须在 SKILL.md 显式声明。

## Development & Validation Commands

无统一构建步骤，按模块验证：

```powershell
python -m py_compile <script.py>      # 改 Python 至少执行一次
python -m json.tool <file.json>       # JSON 校验

# headless EditMode（不需活 Editor，需本机装 Unity 6000.3.16f1）
pwsh -File tools/run-editmode.ps1 -Project <test-runner|editor-debug|lua-device-debug|u3d-ai-linker> -Unity <unity.exe>
# 退出码：0 全绿 / 2 测试失败 / 3 非 Unity 工程或 Unity 未找到 / 4 超时
# -runTests 自动退出 Editor，不要加 -quit；不要对已被另一 Editor 打开的工程跑

# E2E 冒烟（需活 Editor，服务在线）
python Packages/com.yoji.test-runner/Agent~/skills/test-runner-mcp/references/run-e2e.py
python Packages/com.yoji.editor-debug/Agent~/skills/unity-editor-debug-mcp/references/run-e2e.py
```

涉及 Unity 服务时，先确认对应 Editor、UPM 包与端口已就绪。无法运行 Unity 侧验证时，明确记录未验证边界，不要用"服务未启动"代替代码验证结论。

## Coding Style & Naming Conventions

- C#：遵循 Unity 2022.3 / C# 风格；私有字段 `m_` / 静态只读 `k_` / 编译期常量 `c_` 前缀沿用各包既有约定，asmdef 按包隔离。
- Python：4 空格缩进、类型标注、`snake_case`。
- PowerShell：完整参数名，路径用 `-LiteralPath`。
- 包名 `com.yoji.<tool>`、skill 名、目录名统一小写 `kebab-case`。
- 热路径与自动化脚本禁止无意义日志、弹窗及交互阻塞。
- 示例必须可复制执行，不得保留真实账号、令牌或机器专属绝对路径。

## Testing Guidelines

- 每次修改至少执行语法检查（Python `py_compile`、JSON `json.tool`）。
- C# 改动跑 `tools/run-editmode.ps1 -Project <tool>`；结果落 `TestProjects/<tool>/TestResults/editmode-<stamp>.{xml,log}`。
- 协议、CLI 参数或传输逻辑变化时，同步补充/更新 E2E（各包 `Agent~/skills/<skill>/references/run-e2e.py`）、README 与对应 SKILL.md。测试名描述行为，例如 `test-equality-trap`。
- 文档与真实 CLI 不得漂移。

## Commit & Pull Request Guidelines

- 提交信息使用简短祈使句，可采用 `docs:`、`fix:`、`feat:` 前缀。一个提交只处理一个工具或一个明确问题。
- PR 说明用途、目标 Unity 版本、外部依赖、验证命令与结果。涉及 Editor UI 时附截图；涉及协议或端口时列出兼容性影响。
- 同步更新 README.md 与对应 SKILL.md，避免文档与真实 CLI 漂移。

## Security & Integration

- 调试服务默认只绑定 `127.0.0.1`。禁止提交凭据、内网地址和项目私有数据。
- 接入其他 Unity 工程优先用 UPM `file:` / git-URL 或 u3d-ai-linker 的可撤销流程，不直接污染目标工程配置。
- 端口与 Tap4Fun 内部包 `com.tfw.test-runner-mcp`(21890) / `com.tfw.unity-editor-debug-mcp`(21891 + 21892/21893) 完全重叠；目标工程若已装 com.tfw 这套，二选一，避免端口竞争。本工具集仅非 HybridCLR。
