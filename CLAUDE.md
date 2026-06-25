# CLAUDE.md

This repository is a single Unity Package Manager package for AI-assisted Unity Editor automation.

## Package

- `com.sputnicyoji.u3d-dev-tools-ai`

Internal modules:

- `Editor/Core`: shared Editor service primitives.
- `Editor/EditorDebug`: local Editor reflection/debug HTTP service.
- `Editor/TestRunner`: local Unity recompilation and test HTTP service.
- `Runtime/LuaDeviceDebug` and `Editor/LuaDeviceDebug`: transport for project-provided Lua diagnostics hosts.
- `Editor/U3DAILinker`: Project Settings linker for registry inspection and agent asset sync.

## Working rules

- Treat `Packages/com.sputnicyoji.*` as the product surface.
- Treat `TestProjects/*` as validation fixtures.
- Do not commit machine-specific paths, tokens, private project names, generated Unity caches, or device identifiers.
- Keep README examples copyable. Use `<repo-root>` and `<project-root>` for placeholders.
- Use `UNITY_EXE` or `UNITY_EDITOR_PATH` instead of hard-coded local Unity paths.

## Validation

```powershell
python -m py_compile <script.py>
python -m json.tool <file.json>
pwsh -File tools/run-editmode.ps1 -Project <tool> -Unity <path-to-Unity.exe>
```

## Ports

- `test-runner`: legacy `21890/21896/21897`, project offset `+0`.
- `editor-debug`: legacy `21891/21892/21893`, project offset `+1`.
- `lua-device-debug`: legacy `21894`, project offset `+4`.

All services are local-development tools and should bind to `127.0.0.1` by default.
