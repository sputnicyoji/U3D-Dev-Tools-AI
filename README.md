# U3D Dev Tools AI

Unity Package Manager tools for AI-assisted Unity Editor automation.

This repository is a UPM monorepo. It provides local Editor services and agent-side clients so an AI coding agent can run Unity tests, inspect Editor state, diagnose Lua runtime state through a project adapter, and install the toolset into another Unity project.

## Packages

| Package | Version | Purpose |
|---|---:|---|
| `com.yoji.editor-core` | `0.1.2` | Shared Editor-only infrastructure: main-thread dispatch, bounded request body reading, service lifecycle, and project-aware port registry. |
| `com.yoji.editor-debug` | `0.1.2` | HTTP+JSON reflection/debug service for Unity Editor inspection. |
| `com.yoji.test-runner` | `0.1.6` | HTTP service for recompilation, EditMode tests, PlayMode tests, and test discovery. |
| `com.yoji.lua-device-debug` | `0.1.2` | Transport layer for project-provided Lua runtime diagnostics in Editor and Android Development Builds. |
| `com.yoji.u3d-ai-linker` | `0.1.6` | Project Settings package that installs the tools and syncs Claude Code / Codex skills into a target project. |

## Install

### Recommended: install the linker

Add the linker from Git URL:

```json
{
  "dependencies": {
    "com.yoji.u3d-ai-linker": "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/com.yoji.u3d-ai-linker#u3d-ai-linker-v0.1.6"
  }
}
```

Then open Unity and use:

```text
Edit > Project Settings > U3D AI Linker
```

The linker reads `Registry/stable.json` and installs the selected tools as top-level UPM dependencies.

### Direct Git URL install

Unity does not resolve sibling Git packages from this monorepo automatically. If installing tools directly, add `editor-core` as a top-level dependency.

```json
{
  "dependencies": {
    "com.yoji.editor-core": "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/com.yoji.editor-core#editor-core-v0.1.2",
    "com.yoji.editor-debug": "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/com.yoji.editor-debug#editor-debug-v0.1.2",
    "com.yoji.test-runner": "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/com.yoji.test-runner#test-runner-v0.1.6"
  }
}
```

For Lua diagnostics:

```json
{
  "dependencies": {
    "com.yoji.editor-core": "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/com.yoji.editor-core#editor-core-v0.1.2",
    "com.yoji.lua-device-debug": "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/com.yoji.lua-device-debug#lua-device-debug-v0.1.2"
  }
}
```

### Local development install

Replace `<repo-root>` with an absolute path on your workstation:

```json
{
  "dependencies": {
    "com.yoji.editor-core": "file:<repo-root>/Packages/com.yoji.editor-core",
    "com.yoji.test-runner": "file:<repo-root>/Packages/com.yoji.test-runner"
  }
}
```

### Private repository access

If the GitHub repository is private, configure Git authentication before adding the package in Unity. Use an SSH key or a Git credential helper. Do not put tokens in `manifest.json`.

## Tool summary

### Test Runner MCP

Package path: `Packages/com.yoji.test-runner`

Default ports: legacy `21890`, `21896`, `21897`; project offset `+0`.

```powershell
python Packages/com.yoji.test-runner/Agent~/skills/test-runner-mcp/client.py ping
python Packages/com.yoji.test-runner/Agent~/skills/test-runner-mcp/client.py list-tests --mode EditMode
python Packages/com.yoji.test-runner/Agent~/skills/test-runner-mcp/client.py run-tests --mode EditMode
```

PlayMode tests are supported through a temporary `EnterPlayModeOptions.DisableDomainReload` overlay. The service restores the user's PlayMode settings when the run ends.

### Editor Debug MCP

Package path: `Packages/com.yoji.editor-debug`

Default ports: legacy `21891`, `21892`, `21893`; project offset `+1`.

```powershell
cd Packages/com.yoji.editor-debug/Agent~/skills/unity-editor-debug-mcp
python client.py ping
python client.py describe --type UnityEngine.Application
python client.py invoke --type UnityEngine.Application --member isPlaying --kind get
python client.py console --count 30 --filter error
```

`/eval` is compiled into the service but disabled by default. Prefer `describe`, `invoke`, `console`, and `batch`.

### Lua Device Debug

Package path: `Packages/com.yoji.lua-device-debug`

Default port: legacy `21894`; project offset `+4`.

The package provides transport only. The target project must implement and register `ILuaDeviceDebugHost`.

```powershell
cd Packages/com.yoji.lua-device-debug/Agent~/skills/unity-lua-device-debug
python client.py ping
python client.py commands
python client.py execute system.info
```

For Android Development Builds:

```powershell
python client.py adb-forward
python client.py ping
python client.py adb-remove
```

### U3D AI Linker

Package path: `Packages/com.yoji.u3d-ai-linker`

The linker provides a Project Settings surface for:

- stable/dev registry inspection
- manifest transaction planning
- tool install/update
- agent asset sync
- Windows junction repair for `.claude/skills` and `.agents/skills`

## Repository layout

```text
Packages/com.yoji.editor-core/
Packages/com.yoji.editor-debug/
Packages/com.yoji.test-runner/
Packages/com.yoji.lua-device-debug/
Packages/com.yoji.u3d-ai-linker/
Registry/stable.json
Registry/dev.json
TestProjects/<package>/
tools/run-editmode.ps1
```

Each package keeps its Unity code under `Editor/` or `Runtime/`. Agent clients and skills live under `Agent~/`, which Unity does not compile.

## Validation

Run syntax checks after editing scripts or registry files:

```powershell
python -m py_compile Packages/com.yoji.test-runner/Agent~/skills/test-runner-mcp/client.py
python -m py_compile Packages/com.yoji.editor-debug/Agent~/skills/unity-editor-debug-mcp/client.py
python -m py_compile Packages/com.yoji.lua-device-debug/Agent~/skills/unity-lua-device-debug/client.py
python -m json.tool Registry/stable.json
python -m json.tool Registry/dev.json
```

Run EditMode tests with an installed Unity Editor:

```powershell
pwsh -File tools/run-editmode.ps1 -Project test-runner -Unity <path-to-Unity.exe>
pwsh -File tools/run-editmode.ps1 -Project editor-debug -Unity <path-to-Unity.exe>
pwsh -File tools/run-editmode.ps1 -Project lua-device-debug -Unity <path-to-Unity.exe>
pwsh -File tools/run-editmode.ps1 -Project u3d-ai-linker -Unity <path-to-Unity.exe>
```

You can also set `UNITY_EXE` or `UNITY_EDITOR_PATH` instead of passing `-Unity`.

## Requirements

- Unity 2022.3 or newer.
- Python 3.8 or newer for agent clients.
- Git on the machine that installs Git URL UPM packages.
- Android SDK platform tools for Android `lua-device-debug` forwarding.
- Windows for the first linker junction workflow.

## Security model

- Editor services bind to `127.0.0.1` by default.
- Debug services are intended for local development automation.
- `lua-device-debug` starts in Player only for Android Development Builds.
- Mutating Lua commands require both a mutating descriptor and an explicit CLI flag.
- No telemetry service is included in these packages.

## Publishing model

Current distribution is Git URL UPM. A future Asset Store UPM release can use the same package folders, but package namespaces must match the publisher namespace claimed in the Unity Publisher Portal.
