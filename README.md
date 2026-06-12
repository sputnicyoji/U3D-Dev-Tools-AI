# U3D-Dev-Tools-AI

Unity3D development-tool assets for AI-assisted workflows.

> [!IMPORTANT]
> This repository is currently a migration workspace, not a complete public
> distribution of all three Unity-side services. See the status table below
> before trying to install or run a tool.

## Current Status

| Tool | Agent-side assets in this repo | Unity-side service in this repo | Status |
|------|--------------------------------|---------------------------------|--------|
| test-runner-mcp | HTTP interface specification only | No | Planned; currently depends on the private `com.tfw.test-runner-mcp` package |
| unity-editor-debug-mcp | Python client, skill, and references | No | Client-only; requires an existing EditorDebugMCP installation |
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

- **Included here**: `client.py`, skill, and references
- **Not included**: the Unity Editor service/package
- **References**: protocol spec, API cookbook, troubleshooting guide
- **Docs**: [unity-editor-debug-mcp/SKILL.md](unity-editor-debug-mcp/SKILL.md)

```bash
python unity-editor-debug-mcp/client.py ping
python unity-editor-debug-mcp/client.py describe --type UnityEngine.Application
python unity-editor-debug-mcp/client.py invoke --type UnityEngine.Application --member isPlaying --kind get
python unity-editor-debug-mcp/client.py recompile
```

These commands require an existing EditorDebugMCP service in the opened Unity
project.

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

There is no repository-wide quick start yet because the public Unity packages
have not been migrated. For the current client-only tools:

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
