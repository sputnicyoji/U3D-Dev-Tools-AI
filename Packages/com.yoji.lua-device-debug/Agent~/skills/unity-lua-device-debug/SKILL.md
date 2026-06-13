---
name: unity-lua-device-debug
description: Connect to Unity Lua Device Debug over HTTP+JSON for Editor and Android Development Build diagnostics. Use when inspecting project-registered Lua debug commands on Unity 6.3 LTS without arbitrary eval.
---

# Unity Lua Device Debug

`com.yoji.lua-device-debug` is the generic transport package for Lua runtime diagnostics in Unity 6.3 LTS.

Use this skill when the target Unity project has installed the package and registered an `ILuaDeviceDebugHost`.

## Commands

```powershell
python client.py adb-forward
python client.py ping
python client.py commands
python client.py execute system.info
python client.py adb-remove
```

Global flags must come before the subcommand:

```powershell
python client.py --port 21894 --timeout 10 ping
python client.py --serial <device-serial> adb-forward
python client.py execute config.get --arg table=Activity --arg id=1001
```

Mutating commands are blocked unless both the Unity command descriptor declares `mutating=true` and the CLI call passes `--allow-mutation`.

`adb-forward` reuses an existing same-target forward without taking ownership. `adb-remove` only removes forwards recorded as created by this CLI.
