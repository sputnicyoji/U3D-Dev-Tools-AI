# U3D Dev Tools AI

Unity Package Manager tools for AI-assisted Unity Editor automation.

This repository is distributed as one UPM package:

```text
com.sputnicyoji.u3d-dev-tools-ai
```

It contains local Editor services and agent-side clients so an AI coding agent can run Unity tests, inspect Editor state, diagnose Lua runtime state through a project adapter, and sync agent skills into a Unity project.

## Install

Add the package from Git URL:

```json
{
  "dependencies": {
    "com.sputnicyoji.u3d-dev-tools-ai": "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/com.sputnicyoji.u3d-dev-tools-ai#u3d-dev-tools-ai-v0.2.0"
  }
}
```

For local development, replace `<repo-root>` with an absolute path on your workstation:

```json
{
  "dependencies": {
    "com.sputnicyoji.u3d-dev-tools-ai": "file:<repo-root>/Packages/com.sputnicyoji.u3d-dev-tools-ai"
  }
}
```

If the repository is private, configure Git authentication before opening Unity. Use an SSH key or a Git credential helper. Do not put access tokens in `manifest.json`.

## Contents

| Module | Path | Purpose |
|---|---|---|
| Core | `Editor/Core` | Shared Editor-only infrastructure: main-thread dispatch, bounded request body reading, service lifecycle, and project-aware port registry. |
| Test Runner | `Editor/TestRunner` | HTTP service for recompilation, EditMode tests, PlayMode tests, and test discovery. |
| Editor Debug | `Editor/EditorDebug` | HTTP+JSON reflection/debug service for Unity Editor inspection. |
| Lua Device Debug | `Runtime/LuaDeviceDebug`, `Editor/LuaDeviceDebug` | Transport layer for project-provided Lua runtime diagnostics in Editor and Android Development Builds. |
| U3D Dev Tools AI Settings | `Editor/U3DAILinker` | Project Settings UI for agent asset sync and package registry inspection. |
| Agent assets | `Agent~` | Python clients, skills, and managed rule fragments for Claude Code / Codex. |

The package keeps multiple asmdef assemblies internally. Users install one UPM package.

## Tool summary

### Test Runner MCP

Default ports: legacy `21890`, `21896`, `21897`; project offset `+0`.

```powershell
python Packages/com.sputnicyoji.u3d-dev-tools-ai/Agent~/skills/test-runner-mcp/client.py ping
python Packages/com.sputnicyoji.u3d-dev-tools-ai/Agent~/skills/test-runner-mcp/client.py list-tests --mode EditMode
python Packages/com.sputnicyoji.u3d-dev-tools-ai/Agent~/skills/test-runner-mcp/client.py run-tests --mode EditMode
```

PlayMode tests are supported through a temporary `EnterPlayModeOptions.DisableDomainReload` overlay. The service restores the user's PlayMode settings when the run ends.

### Editor Debug MCP

Default ports: legacy `21891`, `21892`, `21893`; project offset `+1`.

```powershell
cd Packages/com.sputnicyoji.u3d-dev-tools-ai/Agent~/skills/unity-editor-debug-mcp
python client.py ping
python client.py describe --type UnityEngine.Application
python client.py invoke --type UnityEngine.Application --member isPlaying --kind get
python client.py console --count 30 --filter error
```

`/eval` is compiled into the service but disabled by default. Prefer `describe`, `invoke`, `console`, and `batch`.

### Lua Device Debug

Default port: legacy `21894`; project offset `+4`.

The package provides transport only. The target project must implement and register `ILuaDeviceDebugHost`.

```powershell
cd Packages/com.sputnicyoji.u3d-dev-tools-ai/Agent~/skills/unity-lua-device-debug
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

### U3D Dev Tools AI settings

Open:

```text
Edit > Project Settings > U3D Dev Tools AI
```

The linker manages:

- stable/dev registry inspection
- agent asset sync
- Windows junction repair for `.claude/skills` and `.agents/skills`
- managed fragments for Claude Code and Codex

## Repository layout

```text
Packages/com.sputnicyoji.u3d-dev-tools-ai/
  package.json
  Editor/
  Runtime/
  Tests/
  Agent~/
  Registry/
Registry/stable.json
Registry/dev.json
TestProjects/<fixture>/
tools/run-editmode.ps1
```

## Validation

Run syntax checks after editing scripts or registry files:

```powershell
python -m py_compile Packages/com.sputnicyoji.u3d-dev-tools-ai/Agent~/skills/test-runner-mcp/client.py
python -m py_compile Packages/com.sputnicyoji.u3d-dev-tools-ai/Agent~/skills/unity-editor-debug-mcp/client.py
python -m py_compile Packages/com.sputnicyoji.u3d-dev-tools-ai/Agent~/skills/unity-lua-device-debug/client.py
python -m json.tool Registry/stable.json
python -m json.tool Registry/dev.json
python tools/test-single-package-layout.py
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
- No telemetry service is included.

## Publishing model

Current distribution is Git URL UPM. A future Asset Store UPM release can use the same package folder, but the package namespace must match the publisher namespace claimed in the Unity Publisher Portal.
