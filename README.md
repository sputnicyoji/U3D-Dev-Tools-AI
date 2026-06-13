# U3D-Dev-Tools-AI

Unity3D development-tool assets for AI-assisted workflows.

> [!IMPORTANT]
> This repository is currently a migration workspace. The unity-editor-debug-mcp
> Unity-side service is now included as a UPM package; the other two tools still
> lack their Unity-side services. See the status table below before trying to
> install or run a tool.

## Current Status

| Tool | Agent-side assets in this repo | Unity-side service in this repo | Status |
|------|--------------------------------|---------------------------------|--------|
| test-runner-mcp | HTTP interface specification only | No | Planned; currently depends on the private `com.tfw.test-runner-mcp` package |
| unity-editor-debug-mcp | Python client, skill, and references | Yes (`Packages/com.yoji.editor-debug`) | Usable; verified on Unity 6000.3.16f1 |
| feval-runtime-debug | Python clients, skill, and references | No | Client-only; requires the target project's feval/HybridCLR listener |

The planned public UPM packages and migration constraints are documented in
[the U3D AI Linker design](docs/superpowers/specs/2026-06-12-u3d-ai-linker-design.md).

## Tools

### 1. test-runner-mcp

Interface specification for a private HTTP service on port **21890** that
triggers Unity recompilation and runs EditMode/PlayMode tests.

- **Included here**: protocol and usage documentation only
- **Not included**: `client.py` and the Unity Editor service/package
- **Docs**: [test-runner-mcp/SKILL.md](test-runner-mcp/SKILL.md)

```bash
curl http://127.0.0.1:21890/ping
```

This command works only after the private Unity package documented in the
skill has been installed. There is no public package in this repository yet.

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

### 3. feval-runtime-debug

C# expression-evaluator clients for a feval TCP listener on port **9999**.
They connect to a Unity/HybridCLR runtime in the Editor or on an Android
device.

- **Entry points**:
  - `feval-runtime-debug/scripts/feval_runtime_debug.py` -- core Python client
  - `feval-runtime-debug/scripts/unity_bridge.py` -- Unity Editor bridge
- **Not included**: the target project's feval/HybridCLR listener
- **Reference**: [feval-runtime-debug/references/feval-syntax.md](feval-runtime-debug/references/feval-syntax.md)
- **Docs**: [feval-runtime-debug/SKILL.md](feval-runtime-debug/SKILL.md)

```bash
# Local Unity Editor
python feval-runtime-debug/scripts/feval_runtime_debug.py --expr "GameObject.Find(\"Main Camera\").name"

# Android device (needs adb forward tcp:9999 tcp:9999)
python feval-runtime-debug/scripts/feval_runtime_debug.py --host 127.0.0.1 --port 9999 --expr "Time.time"
```

## Quick Start

There is no repository-wide quick start yet; only unity-editor-debug-mcp ships
its Unity-side package (see its install section above). For the remaining
client-only tools:

1. Install or enable the corresponding Unity-side service in the target project.
2. Open the Unity project and verify that its listener is active.
3. Run the included client using the repository-relative paths shown above.

## Port Map

| Tool | Port | Protocol |
|------|------|----------|
| test-runner-mcp | 21890 | HTTP |
| unity-editor-debug-mcp | 21891 (fallback 21892/21893) | HTTP+JSON |
| feval-runtime-debug | 9999 | TCP (feval) |

## Requirements

- Unity 2022.3+ (for the separately installed Editor-side services)
- Python 3.8+
- HybridCLR (for feval-runtime-debug runtime expression evaluation)
- Android: adb + port forwarding (`adb forward tcp:9999 tcp:9999`)
