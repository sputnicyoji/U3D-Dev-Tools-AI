# Lua Device Debug

Generic HTTP+JSON transport for Unity Lua runtime diagnostics.

This package targets Unity 2022.3 LTS or newer. It provides the transport, lifecycle, safety gates, and agent CLI for device diagnostics. It does not include a project Lua adapter; target projects register their own `ILuaDeviceDebugHost`.

Android Development Build players can override the remote listen port with `Assets/Resources/YojiLuaDeviceDebugRuntimeConfig.asset`.

## Install

Add this package to the target Unity project's `Packages/manifest.json`:

```json
"com.yoji.lua-device-debug": "file:<path-to-repo>/Packages/com.yoji.lua-device-debug"
```

Open the project in Unity 2022.3 LTS or newer.

## Runtime Boundary

The generic package exposes:

- `ILuaDeviceDebugHost`
- `LuaDeviceDebugRuntime.RegisterHost(...)`
- `LuaDeviceDebugRuntime.UnregisterHost(...)`
- HTTP endpoints on a project-aware Editor port. Android Development Build players use `21894` unless a runtime config asset overrides it.

It does not reference xLua, SLG project types, HybridCLR, C# reflection eval, or arbitrary Lua eval.

## Endpoints

All endpoints accept JSON over HTTP POST:

| Endpoint | Purpose |
|---|---|
| `/ping` | Service, Unity, platform, and host readiness |
| `/commands` | Project-registered command descriptors |
| `/execute` | Execute one project-registered command |

`/execute` request:

```json
{
  "requestId": "uuid",
  "command": "config.get",
  "args": {
    "table": "Activity",
    "id": 1001
  },
  "allowMutation": false
}
```

## Security Boundary

- Editor server starts automatically.
- Player server compiles only for `DEVELOPMENT_BUILD && !UNITY_EDITOR`.
- Player startup is runtime-gated to Android.
- Player remote port resolves from the runtime config asset when present; otherwise it falls back to `21894`.
- Listener binds to loopback only.
- Request body limit: 1 MB.
- Response body limit: 4 MB.
- Main-thread queue limit: 16.
- Default main-thread wait timeout: 5 seconds. If a request times out before Unity main-thread execution starts, the queued work is skipped.
- Mutating commands require descriptor `mutating=true` and CLI `--allow-mutation`.

## CLI

```powershell
cd Packages/com.yoji.lua-device-debug/Agent~/skills/unity-lua-device-debug
python client.py ping
python client.py commands
python client.py execute config.get --arg table=Activity --arg id=1001
python client.py adb-forward
python client.py adb-remove
```

`adb-forward` reuses an existing same-target forward without taking ownership.
`adb-remove` only removes a forward created by this CLI invocation family; it
does not delete unrelated existing forwards.

Global flags must come before the subcommand:

```powershell
python client.py --port 21894 --timeout 10 ping
python client.py --serial <device-serial> adb-forward
```

## Project Adapter

The target Unity project should implement an adapter that owns Lua-runtime-specific details and registers itself:

```csharp
LuaDeviceDebugRuntime.RegisterHost(myHost);
```

The host returns JSON strings from `DescribeCommands()` and `Execute(...)`. Keep project commands whitelisted and short-running.
