# Unity Lua Device Debug protocol

This transport exposes registered diagnostic commands.
It requires the target project to provide `ILuaDeviceDebugHost`.
It does not expose arbitrary Lua eval.

## Commands

- `ping`: verifies the Lua debug service and host state.
- `commands`: returns registered command descriptors.
- `execute`: runs one registered command by id with descriptor-defined arguments.
- `adb-forward`: creates an Android device port forward for a Development Build.
- `adb-remove`: removes the adb forward created by this CLI.

## Descriptor contract

Each command descriptor defines id, label, argument schema, and `mutating`.
Execute only command ids returned by `commands`.
Mutating commands require descriptor `mutating=true` and CLI `--allow-mutation`.
Without both gates, do not execute the mutation.

## Endpoint resolution

Editor diagnostics should use `python client.py --project <unity-project-root> ping`.
Android diagnostics should create the adb forward before calling `ping`.
Use `--port` only for a forced endpoint.
