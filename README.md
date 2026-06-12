# U3D-Dev-Tools-AI

Unity3D Development Tools for AI-assisted workflows. Three HTTP/TCP services that let AI agents interact with a running Unity Editor -- run tests, debug at runtime, and invoke arbitrary C# reflection -- without touching the GUI.

## Tools

### 1. test-runner-mcp

HTTP service on port **21890** that triggers Unity recompilation and runs EditMode/PlayMode tests headlessly.

- **Entry point**: `client.py` -- `recompile`, `run_editmode`, `run_playmode`, `status`, `cancel`
- **Docs**: [test-runner-mcp/SKILL.md](test-runner-mcp/SKILL.md)

```bash
python client.py recompile
python client.py run_editmode --assembly MyGame.Editor.Tests
python client.py status
```

### 2. unity-editor-debug-mcp

HTTP+JSON reflection on port **21891** (fallback 21892/21893). Call any Unity Editor API (including `internal`/`private`) from outside the process.

- **Entry point**: `client.py` -- `invoke`, `describe`, `eval`, `recompile`, `ping`
- **References**: protocol spec, API cookbook, troubleshooting guide
- **Docs**: [unity-editor-debug-mcp/SKILL.md](unity-editor-debug-mcp/SKILL.md)

```bash
python client.py ping
python client.py describe --type UnityEngine.Application
python client.py invoke --type UnityEngine.Application --member isPlaying --kind get
python client.py recompile
```

### 3. feval-runtime-debug

C# expression evaluator via feval TCP on port **9999**. Connects to Unity/HybridCLR runtime (Editor or Android device). Evaluate arbitrary C# expressions, read state, call project methods.

- **Entry points**:
  - `feval_runtime_debug.py` -- core Python client
  - `feval_runtime_debug.ps1` -- PowerShell wrapper
  - `unity_bridge.py` -- auto-launches Unity Editor when needed
- **Reference**: [feval-runtime-debug/references/feval-syntax.md](feval-runtime-debug/references/feval-syntax.md)
- **Docs**: [feval-runtime-debug/SKILL.md](feval-runtime-debug/SKILL.md)

```bash
# Local Unity Editor
python feval_runtime_debug.py --expr "GameObject.Find(\"Main Camera\").name"

# Android device (needs adb forward tcp:9999 tcp:9999)
python feval_runtime_debug.py --host 127.0.0.1 --port 9999 --expr "Time.time"
```

## Quick Start

1. Open your Unity project.
2. The Unity-side MCP server starts automatically with the Editor.
3. For `feval-runtime-debug`, the feval listener must be active (built into HybridCLR hot-reload).
4. Call any tool's `client.py` from your AI agent or terminal.

## Port Map

| Tool | Port | Protocol |
|------|------|----------|
| test-runner-mcp | 21890 | HTTP |
| unity-editor-debug-mcp | 21891 (fallback 21892/21893) | HTTP+JSON |
| feval-runtime-debug | 9999 | TCP (feval) |

## Requirements

- Unity 2022.3+ (Editor-side services)
- Python 3.8+
- HybridCLR (for feval-runtime-debug runtime expression evaluation)
- Android: adb + port forwarding (`adb forward tcp:9999 tcp:9999`)
