# CLAUDE.md

This repository is a Unity Package Manager monorepo for AI-assisted Unity Editor automation.

## Packages

- `com.yoji.editor-core`: shared Editor service primitives.
- `com.yoji.editor-debug`: local Editor reflection/debug HTTP service.
- `com.yoji.test-runner`: local Unity recompilation and test HTTP service.
- `com.yoji.lua-device-debug`: transport for project-provided Lua diagnostics hosts.
- `com.yoji.u3d-ai-linker`: Project Settings linker for tool install and agent asset sync.

## Working rules

- Treat `Packages/com.yoji.*` as the product surface.
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
