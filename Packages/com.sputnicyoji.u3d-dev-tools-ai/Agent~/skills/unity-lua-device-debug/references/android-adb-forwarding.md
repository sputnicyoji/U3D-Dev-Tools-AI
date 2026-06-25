# Android adb forwarding

Use this flow for a Unity Android Development Build.

1. Connect the device and confirm `adb devices` shows it.
2. Verify the installed build is a Development Build with Lua Device Debug enabled.
3. Run `python client.py adb-forward` to create the local forward.
4. Run `python client.py ping` to prove the service is reachable.
5. Run `python client.py commands` to inspect registered diagnostics.
6. Run `python client.py execute <command-id>` only for listed commands.
7. Run `python client.py adb-remove` when this CLI created the forward.

If ping fails after forwarding, verify the app is running, the device is authorized, and the project registered `ILuaDeviceDebugHost`.
