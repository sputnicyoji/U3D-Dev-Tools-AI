---
name: unity-editor-debug-mcp
description: Inspect a live Unity Editor through HTTP+JSON. Use for Console, Hierarchy, Selection, assets, UI Toolkit, profiler, type description, or reflection invoke evidence.
---

# Unity Editor Debug MCP

Use this skill to inspect the running Editor.
Prefer evidence reads over GUI assumptions.
Do not enable `/eval`; it is disabled by default.

## Operational loop

1. Resolve the endpoint.
   Run `python client.py --project <unity-project-root> ping` when the project root is known.
   Use `--pid <editor-pid>` for duplicate Editors.
   Use `--port <port>` only to force a known endpoint.
   Completion: ping returns the debug service identity and Editor state.

2. Name the evidence target.
   Choose Console, Selection, Hierarchy, asset database, UI Toolkit tree, profiler, frame debugger, type, member, or Play Mode state.
   Completion: the target is explicit before any invoke call.

3. Describe before uncertain reflection.
   Run `describe --type <Full.Type.Name>` when the type, member, overload, or static/instance shape is uncertain.
   Completion: the selected member signature is known.

4. Invoke or batch.
   Use `invoke`, `invoke-chain`, `console`, or `batch`.
   Prefer batch for related reads.
   Completion: the response is parsed and checked for `error` and truncation markers.

5. Verify state-changing claims with a second signal.
   Use Console, hierarchy readback, ping state, asset lookup, or another available Unity inspection tool.
   Completion: the report names both the action and the verification source.

6. Report bounded evidence.
   Quote only the relevant fields and file paths.
   Completion: the answer includes enough data to reproduce the inspected state.

## Fallback

If ping fails, report the exact resolver command and whether `.u3d-ai-linker/ports.json` existed.
Do not guess Editor state from stale logs.

## References

- `references/api-cookbook.md` for common inspection recipes.
- `references/protocol.md` for request and response contracts.
- `references/troubleshooting.md` for connection, type, overload, and truncation failures.
- `references/manual-verification.md` for service smoke checks.
- `references/run-e2e.py` for end-to-end verification.
