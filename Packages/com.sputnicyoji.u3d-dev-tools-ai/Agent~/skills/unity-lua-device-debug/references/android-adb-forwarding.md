# Android adb forwarding

Use this flow for a Unity Android Development Build.

1. Connect the device and confirm `adb devices` shows it.
2. Verify the installed build is a Development Build with Lua Device Debug enabled.
3. Run `python client.py adb-forward` to create the local forward.
4. Run `python client.py --port <local-forward-port> ping` using the local port reported by the forward (default `21894`).
5. Run `python client.py --port <local-forward-port> commands` to inspect registered diagnostics.
6. Run `python client.py --port <local-forward-port> execute <command-id>` only for listed commands.
7. Run `python client.py adb-remove` when this CLI created the forward.

If ping fails after forwarding, verify the app is running, the device is authorized, and the project registered `ILuaDeviceDebugHost`.
The explicit port is required when cwd is a Unity project: Android Players have no Editor project identity to satisfy automatic discovery.
