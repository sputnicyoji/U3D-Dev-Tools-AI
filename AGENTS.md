# Repository Guidelines

## Purpose

This repository contains one Unity Package Manager package for AI-assisted Unity Editor automation.

Package:

- `com.sputnicyoji.u3d-dev-tools-ai`

Internal modules:

- `Editor/Core`: shared Editor-only infrastructure.
- `Editor/EditorDebug`: local HTTP+JSON Editor inspection and reflection service.
- `Editor/TestRunner`: local HTTP test runner and recompilation service.
- `Runtime/LuaDeviceDebug` and `Editor/LuaDeviceDebug`: Lua diagnostics transport for project-provided hosts.
- `Editor/U3DAILinker`: Project Settings UI for registry inspection and agent asset sync.

## Development rules

- Keep internal modules independently understandable inside the single package.
- Do not commit credentials, private project data, device identifiers, local machine paths, Unity caches, or generated logs.
- Prefer placeholders such as `<repo-root>` and `<project-root>` in docs.
- Keep `Registry/stable.json` and `Packages/com.sputnicyoji.u3d-dev-tools-ai/Registry/stable.json` synchronized.
- Keep `Registry/dev.json` and `Packages/com.sputnicyoji.u3d-dev-tools-ai/Registry/dev.json` synchronized.
- If a protocol, CLI, or port changes, update the matching `README.md`, `SKILL.md`, and e2e reference script.

## Validation

```powershell
python -m py_compile <script.py>
python -m json.tool <file.json>
pwsh -File tools/run-editmode.ps1 -Project <test-runner|editor-debug|lua-device-debug|u3d-ai-linker> -Unity <path-to-Unity.exe>
```

`tools/run-editmode.ps1` accepts `-Unity`, `UNITY_EXE`, or `UNITY_EDITOR_PATH`.

## Package boundaries

- Unity code lives in `Editor/` or `Runtime/`.
- Agent assets live in `Agent~/`; Unity does not compile this folder.
- Test hosts live under `TestProjects/`.
- Unity generated folders such as `Library/`, `Temp/`, `Logs/`, and `TestResults/` are not source.

## Security

- Local debug services bind to loopback by default.
- Do not add remote listeners or telemetry without an explicit design review.
- Do not put tokens in Unity `manifest.json` examples.
