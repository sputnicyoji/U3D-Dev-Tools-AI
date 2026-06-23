---
name: unity-lua-device-debug
description: Connect to Unity Lua Device Debug over HTTP+JSON for Editor and Android Development Build diagnostics. Use when inspecting project-registered Lua debug commands on Unity 2022.3 LTS or newer without arbitrary eval.
---

# Unity Lua Device Debug

`com.yoji.lua-device-debug` is the generic transport package for Lua runtime diagnostics in Unity 2022.3 LTS or newer.

Editor mode listens on fixed local port 21894. Android Development Build players use port 21894 by default, or `Assets/Resources/YojiLuaDeviceDebugRuntimeConfig.asset` when a custom remote port is needed.

Use this skill when the target Unity project has installed the package and registered an `ILuaDeviceDebugHost`.

## Commands

```powershell
python client.py adb-forward
python client.py ping
python client.py --port 21894 ping
python client.py commands
python client.py execute system.info
python client.py adb-remove
```


## Editor port resolution

Resolution order:

1. Explicit `--port` wins.
2. Otherwise the client uses fixed default `21894`.

`--project` and `--pid` are accepted by shared CLI plumbing, but `lua-device-debug` `0.1.0` does not publish project registry records from the Editor bootstrap. Do not rely on them for disambiguation. If multiple Editor instances need Lua diagnostics, only one can own port `21894`.

`adb-forward` and `adb-remove` do not use the Editor resolver; without `--port`, they use local tunnel port `21894`.

Global flags must come before the subcommand:

```powershell
python client.py --port 21894 --timeout 10 ping
python client.py --serial <device-serial> adb-forward
python client.py execute config.get --arg table=Activity --arg id=1001
```

Mutating commands are blocked unless both the Unity command descriptor declares `mutating=true` and the CLI call passes `--allow-mutation`.

`adb-forward` reuses an existing same-target forward without taking ownership. `adb-remove` only removes forwards recorded as created by this CLI.
