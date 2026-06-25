---
name: unity-lua-device-debug
description: Inspect Unity Lua runtime diagnostics through registered commands. Use for Editor or Android Development Build Lua state, command listing, command execution, and adb forwarding.
---

# Unity Lua Device Debug

Use this skill for project-registered Lua diagnostics.
It exposes command transport only.
It does not provide arbitrary Lua eval.

## Operational loop

1. Select the target.
   Use Editor mode for an open Unity Editor.
   Use Android mode for a Development Build device.
   Completion: the report states `Editor` or `Android` target mode.

2. Resolve or forward.
   Editor: run `python client.py --project <unity-project-root> ping`.
   Android: run `python client.py adb-forward`, then `python client.py ping`.
   Use `--port <port>` only for a forced endpoint.
   Completion: ping proves the Lua debug service is reachable.

3. List registered commands.
   Run `python client.py commands` before execution.
   Completion: command descriptors are visible.

4. Execute only listed commands.
   Run `python client.py execute <command-id>` with `--arg key=value` pairs from the descriptor contract.
   Completion: the command id exists in the descriptor list and returns JSON.

5. Guard mutation.
   Mutating commands require descriptor `mutating=true` and CLI `--allow-mutation`.
   Completion: the mutation gate is satisfied, or the command is not executed.

6. Clean up owned Android forwards.
   Run `python client.py adb-remove` after Android diagnostics when this CLI created the forward.
   Completion: owned adb forward is removed or reported as already absent.

## Fallback

If `commands` is empty or ping reports no host, state that the target project must register `ILuaDeviceDebugHost`.
Do not invent Lua commands.

## References

- `references/protocol.md` for command descriptors and mutation rules.
- `references/android-adb-forwarding.md` for Development Build transport.
