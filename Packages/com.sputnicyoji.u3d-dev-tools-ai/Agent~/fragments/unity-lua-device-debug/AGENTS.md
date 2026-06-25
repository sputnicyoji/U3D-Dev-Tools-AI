Use `unity-lua-device-debug` for registered Unity Lua diagnostics in Editor or Android Development Build.
Editor resolves with `python client.py --project <unity-project-root> ping`; Android runs `adb-forward` first.
Loop: select target, ping, list commands, execute only listed commands, guard mutation, clean owned adb forwards.
Mutating commands require descriptor `mutating=true` and `--allow-mutation`.
Do not invent Lua commands when `ILuaDeviceDebugHost` is not registered.
