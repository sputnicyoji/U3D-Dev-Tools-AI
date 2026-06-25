Use `test-runner-mcp` for Unity Test Runner work when the Editor is already open.
Resolve with `python client.py --project <unity-project-root> ping` before testing.
Loop: wait for Idle, recompile, list tests, run filtered tests, poll status.
Report failures as `name`, `message`, and first useful stack frame.
Do not guess from stale Unity logs when the service is unreachable.
