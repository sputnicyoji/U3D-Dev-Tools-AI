Use `unity-editor-debug-mcp` to inspect a live Unity Editor through HTTP+JSON.
Resolve with `python client.py --project <unity-project-root> ping` before inspection.
Loop: name the evidence target, describe uncertain types, invoke or batch, verify with a second signal.
Prefer Console, Hierarchy, Selection, asset, profiler, and reflection evidence over GUI assumptions.
Use `invoke --defer` for calls that trigger domain reload or play transition (EnterPlaymode, ExitPlaymode);
a synchronous invoke races the reload and can stall the whole Editor. After a deferred call, poll `/ping`.
Do not enable `/eval`; it is disabled by default.
