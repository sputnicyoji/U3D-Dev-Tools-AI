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
    "com.sputnicyoji.u3d-dev-tools-ai": "https://github.com/sputnicyoji/U3D-Dev-Tools-AI.git?path=/Packages/com.sputnicyoji.u3d-dev-tools-ai#u3d-dev-tools-ai-v0.2.23"
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
| Lua Device Debug | `Runtime/LuaDeviceDebug`, `Runtime/LuaDeviceDebugServer`, `Editor/LuaDeviceDebug` | Transport layer for project-provided Lua runtime diagnostics in Editor and Android Development Builds. The host contract is always compiled; the server compiles only in the Editor and Development Builds. |
| U3D Dev Tools AI Settings | `Editor/U3DAILinker` | Project Settings UI for agent asset sync and package registry inspection. |
| Agent assets | `Agent~` | Python clients, skills, and managed rule fragments for Claude Code / Codex. |

The package keeps multiple asmdef assemblies internally. Users install one UPM package.

## Tool summary

Automatic endpoint discovery verifies the `/ping` project root and process ID against `--project`, the Unity project inferred from the working directory, and `--pid` when supplied.
When a project or PID is scoped, discovery fails with `ENDPOINT_IDENTITY_MISMATCH` or `ENDPOINT_NOT_FOUND` instead of falling back to another Editor or an unverified port.
Without either scope, legacy discovery remains available.
`--port <port>` deliberately overrides discovery; use it only for a known endpoint.

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

`/eval` reads one-line member chains such as `UnityEditor.EditorApplication.applicationPath.Length` through the same reflection path as `invoke`; it does not compile arbitrary code. Prefer `describe`, `invoke`, `console`, and `batch` when types or overloads are uncertain.

### UI Observe

Semantic uGUI state from the same Editor Debug service: layer stack, per-control `hitTop` / `coveredBy`, and guide-mask detection.
`act` clicks or drags a control by id in Play mode and refuses with a `reason` when a real touch could not reach it. See `docs/ui-observation-layer.md`.
`world` clicks a world object by scene path or a map cell through the game's registered scripted pointer and checks what the game actually hit; it also drags, scrolls and pinches.

```powershell
cd Packages/com.sputnicyoji.u3d-dev-tools-ai/Agent~/skills/unity-ui-observe
python client.py guide
python client.py snapshot --max 100
python client.py hit --x 120 --y 640
python client.py act --id CityHud/SafeArea/BuildButton
python client.py act --id <scroll-or-joystick-id> --to 443 1200
python client.py world --path <scene-path-of-a-world-object>
python client.py world --cell 12 34
python client.py world --at 540 960 --action pinch --scale 1.5
```

`/ui/act` accepts only POST with `Content-Type: application/json`. See Security model for the rules every service applies.

### Lua Device Debug

Default port: legacy `21894`; project offset `+4`.

The package provides transport only. The target project must implement and register `ILuaDeviceDebugHost`.
`ILuaDeviceDebugHost` and `LuaDeviceDebugRuntime` live in `Yoji.LuaDeviceDebug.Runtime`, which every build compiles, so host code needs no `#if`.
The HTTP server lives in `Yoji.LuaDeviceDebug.Server` (define constraint `UNITY_EDITOR || DEVELOPMENT_BUILD`) and is absent from release builds.

```powershell
cd Packages/com.sputnicyoji.u3d-dev-tools-ai/Agent~/skills/unity-lua-device-debug
python client.py ping
python client.py commands
python client.py execute system.info
```

For Android Development Builds:

```powershell
python client.py adb-forward
python client.py --port 21894 ping
python client.py adb-remove
```

Use the local port reported by `adb-forward` with every Android HTTP command (`ping`, `commands`, `execute`).
Android Players do not carry the Editor project identity used by automatic discovery.

### U3D Dev Tools AI settings

Open:

```text
Edit > Project Settings > U3D Dev Tools AI
```

The linker manages:

- stable/dev registry inspection
- agent skill sync
- link repair for `.claude/skills` and `.agents/skills` (directory junctions on Windows, symlinks on macOS and Linux)
- managed fragments for Claude Code and Codex

### Updating agent skills

The package is the source of truth for bundled skills.
Do not edit `Library/PackageCache`.
Do not git-pull directly into `.claude/skills` or `.agents/skills`.

Update flow:

1. Update the UPM Git URL or package revision in `Packages/manifest.json`.
2. Let Unity resolve the package.
3. Open `Edit > Project Settings > U3D Dev Tools AI`.
4. Press `Sync Agent Skills`.
5. The linker copies skills to `.u3d-ai-linker/skills` and repairs links into `.claude/skills` and `.agents/skills` (junctions on Windows, symlinks on macOS and Linux).

The Settings panel reports each skill as `Synced`, `Stale`, `Missing`, `Conflict`, or `Source Missing`.

Without the linker, `python <package-root>/Agent~/install_skills.py [skill ...] [--dest <dir>]` copies the bundled skills into `~/.claude/skills` (or `<dir>`), replacing each installed skill folder.

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
tools/run-editmode.py
```

## Validation

Run syntax checks after editing scripts or registry files:

```powershell
python -m py_compile Packages/com.sputnicyoji.u3d-dev-tools-ai/Agent~/skills/test-runner-mcp/client.py
python -m py_compile Packages/com.sputnicyoji.u3d-dev-tools-ai/Agent~/skills/unity-editor-debug-mcp/client.py
python -m py_compile Packages/com.sputnicyoji.u3d-dev-tools-ai/Agent~/skills/unity-lua-device-debug/client.py
python -m py_compile Packages/com.sputnicyoji.u3d-dev-tools-ai/Agent~/skills/unity-ui-observe/client.py
python -m json.tool Registry/stable.json
python -m json.tool Registry/dev.json
python tools/test-single-package-layout.py
```

Run EditMode tests with an installed Unity Editor:

```powershell
python tools/run-editmode.py --project test-runner --unity <path-to-Unity>
python tools/run-editmode.py --project editor-core --unity <path-to-Unity>
python tools/run-editmode.py --project editor-debug --unity <path-to-Unity>
python tools/run-editmode.py --project lua-device-debug --unity <path-to-Unity>
python tools/run-editmode.py --project u3d-ai-linker --unity <path-to-Unity>
```

You can also set `UNITY_EXE` or `UNITY_EDITOR_PATH` instead of passing `--unity`.
Without either, the script looks for the project's Editor version in the default Unity Hub folders on Windows and macOS (`/Applications/Unity/Hub/Editor/<version>/Unity.app`).
`--editor-version <version>` runs with another installed Editor and restores every `ProjectSettings` file afterwards.

Check the stable registry before a release with `python tools/check-u3d-linker-stable.py` (add `--require-remote-tags` to check `origin` too).

## Requirements

- Supported Unity range: Unity 2022.3 LTS through Unity 6000.3.x.
- Validated Editor versions: Unity 2022.3.62f2c1 and Unity 6000.3.12f1 (earlier releases); Unity 2022.3.62f3c1 and Unity 6000.3.13f1 on macOS arm64 (2026-11-28).
- Unity 6000.4 and newer are unverified. Unity 6000.5 is known incompatible: it turns the obsolete `InstanceID` APIs into compile errors, and `editor-debug` has not yet migrated to `EntityId`.
- Python 3.8 or newer for agent clients.
- Git on the machine that installs Git URL UPM packages.
- Android SDK platform tools for Android `lua-device-debug` forwarding.
- Agent skill sync links skills with directory junctions on Windows and symlinks on macOS and Linux. The symlink path is verified on macOS; Linux is untested.

## Security model

- Editor services bind to `127.0.0.1` by default.
- Debug services are intended for local development automation.
- `lua-device-debug` starts in Player only for Android Development Builds; release builds do not contain the server assembly.
- Mutating Lua commands require both a mutating descriptor and an explicit CLI flag.
- Every HTTP service rejects a request that carries an `Origin` header, whatever its value (including `null`), so a web page cannot drive them.
- State-changing routes accept POST only: test-runner `/recompile` and `/run-tests`; editor-debug `/invoke`, `/batch`, `/eval`, `/recompile` and `/ui/act`; lua-device-debug `/execute`. Read-only routes (`/ping`, `/describe`, `/console`, `/ui/snapshot`, `/ui/hit`, `/test-status`, `/list-tests`, `/commands`) still accept GET. Content-Type is not checked except on `/ui/act`, so `curl -d` keeps working.
- The Editor services get Host checking from `HttpListener` (a foreign Host gets 400). lua-device-debug accepts only the Host names `127.0.0.1`, `localhost` and `[::1]`, with any port so adb-forwarded requests work.
- No telemetry service is included.

## Publishing model

Current distribution is Git URL UPM. A future Asset Store UPM release can use the same package folder, but the package namespace must match the publisher namespace claimed in the Unity Publisher Portal.
