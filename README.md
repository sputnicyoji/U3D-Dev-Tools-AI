# U3D-Dev-Tools-AI

Unity Editor packages and agent-side assets for AI-assisted Unity development.

This repository targets non-HybridCLR Unity projects. It provides local UPM
source packages for headless test execution, Editor inspection, Lua runtime
diagnostics, and an Editor-side linker that syncs agent skills/rules into a
Unity project.

Last reviewed: 2026-06-23

## Status

| Package | Purpose | Agent assets | Unity package | Current state |
|---------|---------|--------------|---------------|---------------|
| `com.yoji.editor-core` | Shared Editor-only infrastructure: main-thread dispatch, bounded request bodies, service lifecycle helpers | No | Yes | Internal dependency package |
| `com.yoji.test-runner` | HTTP MCP service for Unity recompilation and EditMode/PlayMode test runs | `client.py`, `SKILL.md`, e2e script | Yes | Stable `0.1.4` tag published; Unity 2022 Git UPM smoke verified; Unity 6000 typed path retained |
| `com.yoji.editor-debug` | HTTP+JSON reflection/debug service inside Unity Editor | `client.py`, `SKILL.md`, references | Yes | Stable `0.1.0` Git install verified |
| `com.yoji.lua-device-debug` | HTTP+JSON diagnostics transport for project-provided Lua runtime adapters | `client.py`, `SKILL.md` | Yes | Stable `0.1.0`; Unity 2022.3 compatible; target project must register a host |
| `com.yoji.u3d-ai-linker` | Project Settings package for installing tools and syncing Claude/Codex skills/rules | Tool fragments | Yes | Stable `0.1.4` tag includes `lua-device-debug` in stable registry |

The old client-only runtime expression debugger (`feval-runtime-debug`) has
been removed. This toolset is not a HybridCLR integration layer.

## Quick Start

Clone this repository, then add the packages you need to a target Unity
project's `Packages/manifest.json`.

For `test-runner`, `editor-debug`, or `lua-device-debug`, install
`editor-core` as a top-level local dependency too:

```json
{
  "dependencies": {
    "com.yoji.editor-core": "file:E:/Yoji/U3D-Dev-Tools-AI/Packages/com.yoji.editor-core",
    "com.yoji.test-runner": "file:E:/Yoji/U3D-Dev-Tools-AI/Packages/com.yoji.test-runner"
  }
}
```

For the linker UI:

```json
{
  "dependencies": {
    "com.yoji.u3d-ai-linker": "file:E:/Yoji/U3D-Dev-Tools-AI/Packages/com.yoji.u3d-ai-linker"
  }
}
```

Open the Unity project after editing the manifest. Services start from Editor
initialization and log a startup message in the Console.

Stable Git install uses tags instead of local paths:

```json
{
  "dependencies": {
    "com.yoji.u3d-ai-linker": "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/com.yoji.u3d-ai-linker#u3d-ai-linker-v0.1.4"
  }
}
```

Direct `test-runner` install must include `editor-core` as a top-level Git
dependency:

```json
{
  "dependencies": {
    "com.yoji.editor-core": "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/com.yoji.editor-core#editor-core-v0.1.0",
    "com.yoji.test-runner": "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/com.yoji.test-runner#test-runner-v0.1.4"
  }
}
```

Direct `lua-device-debug` install is also a two-package Git install:

```json
{
  "dependencies": {
    "com.yoji.editor-core": "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/com.yoji.editor-core#editor-core-v0.1.0",
    "com.yoji.lua-device-debug": "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/com.yoji.lua-device-debug#lua-device-debug-v0.1.0"
  }
}
```

## Tools

### Test Runner MCP

Package: `Packages/com.yoji.test-runner`
Port: `21890` with fallback `21896/21897`

Runs Unity tests from an AI agent without opening the Test Runner UI.

```powershell
python Packages/com.yoji.test-runner/Agent~/skills/test-runner-mcp/client.py ping
python Packages/com.yoji.test-runner/Agent~/skills/test-runner-mcp/client.py list-tests --mode EditMode
python Packages/com.yoji.test-runner/Agent~/skills/test-runner-mcp/client.py run-tests --mode EditMode
```

PlayMode is supported through a temporary
`EnterPlayModeOptions.DisableDomainReload` overlay. The service restores the
user's PlayMode settings when the run ends. If the Editor is already in Play
Mode or loaded scenes are dirty, PlayMode test requests return `409`.

Unity compatibility is split by compile symbol. Unity 2022 uses the reflection
adapter in `UnityTestRunnerApiAdapter.cs` under `!UNITY_6000_0_OR_NEWER`.
Unity 6000 keeps the typed `TestRunnerApi` implementation in
`UnityTestRunnerApiAdapter.Unity6000.cs` under `UNITY_6000_0_OR_NEWER`.
The minimum `com.unity.test-framework` dependency is `1.1.33` so Unity 2022
registries do not have to resolve `1.5.1`.

### Editor Debug MCP

Package: `Packages/com.yoji.editor-debug`
Port: `21891` with fallback `21892/21893`

Reflects Unity Editor and UnityEngine APIs over HTTP+JSON for agent debugging.

```powershell
cd Packages/com.yoji.editor-debug/Agent~/skills/unity-editor-debug-mcp
python client.py ping
python client.py describe --type UnityEngine.Application
python client.py invoke --type UnityEngine.Application --member isPlaying --kind get
python client.py invoke --type UnityEngine.GameObject --target-entity-id <object-id> --member name --kind get
python client.py recompile
```

`/eval` exists in the service code but is disabled by default. Prefer
structured `describe`, `invoke`, `/console`, and `/batch` flows.

### Lua Device Debug

Package: `Packages/com.yoji.lua-device-debug`
Port: `21894`

Provides a generic diagnostics transport for Unity Lua runtimes in Editor and
Android Development Builds. It does not ship a Lua adapter; the target project
must implement and register `ILuaDeviceDebugHost`.

```powershell
cd Packages/com.yoji.lua-device-debug/Agent~/skills/unity-lua-device-debug
python client.py ping
python client.py commands
python client.py execute config.get --arg table=Activity --arg id=1001
```

For Android Development Builds:

```powershell
python client.py adb-forward
python client.py ping
python client.py adb-remove
```

### U3D AI Linker

Package: `Packages/com.yoji.u3d-ai-linker`

Adds a Project Settings surface for tool registry state, install planning, and
agent asset synchronization. Its design covers local/stable/dev channels,
manifest transactions, Windows junctions, and Claude Code / Codex skill
fragments.

Current stable scope is `editor-core`, `editor-debug`, `test-runner`,
`lua-device-debug`, and `u3d-ai-linker`. The current stable revisions are
`editor-core-v0.1.0`, `editor-debug-v0.1.0`, `test-runner-v0.1.4`,
`lua-device-debug-v0.1.0`, and `u3d-ai-linker-v0.1.4`.
The stable registry points consumers to those Git tags; consumer lock files
should resolve them as `source=git` after install. `lua-device-debug` still
requires the target project to register an `ILuaDeviceDebugHost`, but its Git
UPM install and EditMode suite are verified on Unity `2022.3.62f2c1`. Agent
asset probe, `Sync Agent Assets`, `Repair Links`, and the Windows Junction
smoke menu all passed in the live Editor.

## Ports

| Tool | Port | Protocol |
|------|------|----------|
| `test-runner-mcp` | `21890`, fallback `21896/21897` | HTTP |
| `unity-editor-debug-mcp` | `21891`, fallback `21892/21893` | HTTP+JSON |
| `unity-lua-device-debug` | `21894` | HTTP+JSON |

## Verification Baseline

Latest local batchmode runs on Unity `6000.3.16f1`:

| Test project | Result |
|--------------|--------|
| `TestProjects/editor-debug` | `82/82` EditMode passed |
| `TestProjects/test-runner` | `29` total, `27` passed, `2` skipped, `0` failed |
| `TestProjects/lua-device-debug` | `14/14` EditMode passed |
| `TestProjects/u3d-ai-linker` | `266/266` EditMode passed |

Additional `test-runner` `0.1.4` compatibility checks:

| Check | Result |
|-------|--------|
| Unity `2022.3.62f2c1` Roslyn compile for `Yoji.TestRunner.Editor` | Passed |
| Unity `2022.3.62f2c1` HD2D Git UPM smoke for `test-runner-v0.1.4` + `u3d-ai-linker-v0.1.3` | Passed |
| TestRunnerMCP `ping` / `list-tests` / `recompile` on Unity `2022.3.62f2c1` | Passed |
| Missing-test run returns controlled `status=error` / `overallResult=Error` instead of a false green | Passed |
| `UNITY_6000_0_OR_NEWER` define-simulated Roslyn compile for `Yoji.TestRunner.Editor` | Passed |
| Stable registry remote tag check for `test-runner-v0.1.4` and `u3d-ai-linker-v0.1.3` | Passed |

Additional `lua-device-debug` `0.1.0` compatibility checks:

| Check | Result |
|-------|--------|
| Unity `2022.3.62f2c1` Git UPM smoke in `G:\PurgeDemo\arttest-c6` (`/ping` + `14/14` EditMode) | Passed |
| Stable registry remote tag check for `lua-device-debug-v0.1.0` and `u3d-ai-linker-v0.1.4` | Passed |

HTTP e2e scripts live under each tool's `Agent~/skills/.../references/`
folder. They require the matching Unity project to be open with the service
running.

## Requirements

- Unity `2022.3+` for most Editor packages.
- Unity `2022.3+` for `lua-device-debug`.
- Python `3.8+` for agent clients.
- Android SDK platform tools for `lua-device-debug` device forwarding.
- Windows for the first `u3d-ai-linker` junction-based synchronization path.

## Project Docs

- [Progress](progress.md)
- [U3D AI Linker design](docs/superpowers/specs/2026-06-12-u3d-ai-linker-design.md)
- [U3D AI Linker implementation plan](docs/superpowers/plans/2026-06-14-u3d-ai-linker-implementation.md)
- [Dev-tools enhancement design](docs/superpowers/specs/2026-06-13-dev-tools-enhancements-design.md)
- [Cross-project collaboration solution](docs/superpowers/specs/2026-06-13-cross-project-collaboration-solution.md)
