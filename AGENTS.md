# Repository Guidelines

## Purpose

This repository contains Unity Package Manager packages for AI-assisted Unity Editor automation.

Packages:

- `com.yoji.editor-core`: shared Editor-only infrastructure.
- `com.yoji.editor-debug`: local HTTP+JSON Editor inspection and reflection service.
- `com.yoji.test-runner`: local HTTP test runner and recompilation service.
- `com.yoji.lua-device-debug`: Lua diagnostics transport for project-provided hosts.
- `com.yoji.u3d-ai-linker`: Project Settings UI for installing tools and syncing agent assets.

## Development rules

- Keep packages independently understandable and installable.
- Do not commit credentials, private project data, device identifiers, local machine paths, Unity caches, or generated logs.
- Prefer placeholders such as `<repo-root>` and `<project-root>` in docs.
- Keep `Registry/stable.json` and `Packages/com.yoji.u3d-ai-linker/Registry/stable.json` synchronized.
- Keep `Registry/dev.json` and `Packages/com.yoji.u3d-ai-linker/Registry/dev.json` synchronized.
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
