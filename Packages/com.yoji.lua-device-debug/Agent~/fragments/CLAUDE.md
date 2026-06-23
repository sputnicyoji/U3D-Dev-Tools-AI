## unity-lua-device-debug

HTTP+JSON transport for Unity Lua runtime diagnostics on Unity 2022.3 LTS or newer.

Editor mode listens on fixed local port 21894. Android Development Build players
use port 21894 by default, or `Assets/Resources/YojiLuaDeviceDebugRuntimeConfig.asset`
when a custom remote port is needed.

Agent entrypoint: `client.py`.
Commands: `ping`, `commands`, `execute`, `adb-forward`, `adb-remove`.

Only project-registered `ILuaDeviceDebugHost` commands are exposed. There is no
arbitrary Lua eval, no C# reflection eval, and no HybridCLR dependency.
