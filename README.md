# U3D-Dev-Tools-AI

Unity3D development-tool assets for AI-assisted workflows.

> [!IMPORTANT]
> This repository is a migration workspace. The unity-editor-debug-mcp,
> test-runner-mcp, and lua-device-debug Unity-side services live here as
> in-repo UPM source packages under `Packages/com.yoji.*`. They are NOT yet
> published as public, Git-URL-installable UPM packages and carry no release
> tags; today they are consumed via a local `file:` manifest entry. A planned
> U3D AI Linker will orchestrate their public install once each tool reaches
> the `ready` state. This toolset targets non-HybridCLR Unity projects; the old
> client-only runtime expression debugger asset has been removed.

## Current Status

| Tool | Agent-side assets in this repo | Unity-side service in this repo | Status (in-repo `file:`, not yet public UPM) |
|------|--------------------------------|---------------------------------|--------|
| test-runner-mcp | Python client, skill, and references | Yes (`Packages/com.yoji.test-runner`) | Works via `file:` (EditMode only; PlayMode planned); verified on Unity 6000.3.16f1; Registry status: skill-only |
| unity-editor-debug-mcp | Python client, skill, and references | Yes (`Packages/com.yoji.editor-debug`) | Works via `file:`; verified on Unity 6000.3.16f1; Registry status: skill-only |
| unity-lua-device-debug | Python client and skill | Yes (`Packages/com.yoji.lua-device-debug`) | Transport-only via `file:`; targets Unity 6000.3.16f1; project Lua adapter still required; Registry status: planned |

The planned public UPM packages and migration constraints are documented in
[the U3D AI Linker design](docs/superpowers/specs/2026-06-12-u3d-ai-linker-design.md).

## Tools

### 1. test-runner-mcp

HTTP service on port **21890** that triggers Unity recompilation and runs
EditMode tests headlessly, returning results to AI agents (PlayMode planned).

- **Included here**: the Unity Editor service as a UPM package
  (`Packages/com.yoji.test-runner`), plus `client.py` and `references/run-e2e.py`
  under `Packages/com.yoji.test-runner/Agent~/skills/test-runner-mcp/`
- **Docs**: [SKILL.md](Packages/com.yoji.test-runner/Agent~/skills/test-runner-mcp/SKILL.md)
- **Phase**: EditMode only; `testMode:"PlayMode"` returns 400 until phase 2.
  Empty test filters run the whole suite (run-all). Real HTTP status codes
  (200/202/400/404/409).

```bash
python Packages/com.yoji.test-runner/Agent~/skills/test-runner-mcp/client.py ping
```

The HTTP service starts automatically with the Editor (look for
`[TestRunnerMCP] 服务已启动` in the Console).

### 2. unity-editor-debug-mcp

Client assets for HTTP+JSON reflection on port **21891** (fallback
21892/21893).

- **Included here**: the Unity Editor service as a UPM package
  (`Packages/com.yoji.editor-debug`), plus `client.py`, skill, and references
  under `Packages/com.yoji.editor-debug/Agent~/skills/unity-editor-debug-mcp/`
- **References**: protocol spec, API cookbook, troubleshooting guide
- **Docs**: [SKILL.md](Packages/com.yoji.editor-debug/Agent~/skills/unity-editor-debug-mcp/SKILL.md)
- **Compatibility**: Unity 2022.3+; Unity 6.4+ object references use the
  full-width `EntityId` as a decimal string while retaining the existing
  `instanceID` JSON field

**Install (Unity side)**: add to your project `Packages/manifest.json`:

    "com.yoji.editor-debug": "file:<path-to-repo>/Packages/com.yoji.editor-debug"

or copy `Packages/com.yoji.editor-debug/` into your project's `Packages/`.
The HTTP service starts automatically with the Editor (look for
`[EditorDebugMCP] 服务已启动` in the Console).

```bash
cd Packages/com.yoji.editor-debug/Agent~/skills/unity-editor-debug-mcp
python client.py ping
python client.py describe --type UnityEngine.Application
python client.py invoke --type UnityEngine.Application --member isPlaying --kind get
python client.py invoke --type UnityEngine.GameObject --target-entity-id <object-id> --member name --kind get
python client.py recompile
```

These commands require the package to be installed in the opened Unity
project. Note: global flags (`--host` / `--port` / `--timeout`) must come
before the subcommand, e.g. `python client.py --timeout 120 recompile`.
`--target-instance-id` remains supported as an alias for
`--target-entity-id`.

Current verification baseline: 74 Unity EditMode tests and 13 HTTP end-to-end
checks pass on Unity 6000.3.16f1. The Unity 6.4+ branch follows the official
`EntityId.ToULong` / `EntityId.FromULong` API and still requires compilation
on a Unity 6.4 installation before release.

### 3. unity-lua-device-debug

HTTP+JSON transport on port **21894** for Unity Lua runtime diagnostics in
Editor and Android Development Build.

- **Included here**: generic Unity package (`Packages/com.yoji.lua-device-debug`),
  `client.py`, and skill under
  `Packages/com.yoji.lua-device-debug/Agent~/skills/unity-lua-device-debug/`
- **Docs**: [README.md](Packages/com.yoji.lua-device-debug/README.md) and
  [SKILL.md](Packages/com.yoji.lua-device-debug/Agent~/skills/unity-lua-device-debug/SKILL.md)
- **Compatibility**: Unity 6.3 LTS (`6000.3.x`), validated target
  `6000.3.16f1`
- **Boundary**: generic transport only; target projects must register an
  `ILuaDeviceDebugHost` adapter for their Lua runtime

**Install (Unity side)**: add to your project `Packages/manifest.json`:

    "com.yoji.lua-device-debug": "file:<path-to-repo>/Packages/com.yoji.lua-device-debug"

```bash
cd Packages/com.yoji.lua-device-debug/Agent~/skills/unity-lua-device-debug
python client.py ping
python client.py commands
python client.py execute config.get --arg table=Activity --arg id=1001
```

For Android Development Build:

```bash
python client.py adb-forward
python client.py ping
python client.py adb-remove
```

The package does not provide arbitrary Lua eval, C# reflection eval, or
HybridCLR integration.

## Quick Start

Add the package path to the target Unity project's `Packages/manifest.json`,
open the project, then run the matching `client.py`.

## Port Map

| Tool | Port | Protocol |
|------|------|----------|
| test-runner-mcp | 21890 (fallback 21896/21897) | HTTP |
| unity-editor-debug-mcp | 21891 (fallback 21892/21893) | HTTP+JSON |
| unity-lua-device-debug | 21894 | HTTP+JSON |

## Requirements

- Unity 6000.3.16f1 for `unity-lua-device-debug`; existing Editor tools also
  run on Unity 6000.3.16f1
- Python 3.8+
- Android: adb + port forwarding (`adb forward tcp:21894 tcp:21894` for
  `unity-lua-device-debug`)
