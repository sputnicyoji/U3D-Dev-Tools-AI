---
name: test-runner-mcp
description: Run Unity Test Runner through the online Editor service. Use for EditMode/PlayMode tests, recompilation, listing tests, or TDD verification when Unity is already open.
---

# Test Runner MCP

Use this skill for the online Unity test loop.
Do not use it when the Editor is closed or the package is not installed.

## Operational loop

1. Resolve the endpoint.
   Run `python client.py --project <unity-project-root> ping` when the project root is known.
   Use `--pid <editor-pid>` to disambiguate multiple Editors for the same project.
   Use `--port <port>` only to force a known endpoint.
   Completion: ping returns `service`, `state`, `projectName`, and port metadata.

2. Gate on Idle.
   If state is `Compiling` or `Running`, poll or report the blocking state.
   Completion: state is `Idle`, or the response proves why testing cannot start.

3. Recompile after code changes.
   Run `python client.py --project <unity-project-root> recompile`.
   Stop when `hasErrors=true`.
   Completion: compile response has `hasErrors=false`, or compile errors are reported.

4. Verify test filters.
   Run `list-tests --mode EditMode` or `list-tests --mode PlayMode` before exact filtered runs.
   Skip this only for intentional run-all.
   Completion: each requested test name or filter matches at least one discovered test.

5. Run and poll.
   Run `run-tests --mode <EditMode|PlayMode>` with explicit filters or run-all.
   Poll `python client.py --project <unity-project-root> status --job-id <jobId>` until the job is no longer running.
   Completion: final status is `completed` or `error`.

6. Report failures as edit targets.
   Include `name`, `message`, and the first useful stack frame from each failure.
   Completion: the next edit can start without parsing NUnit XML.

## Fallback

If ping cannot reach the service, report the exact command, timeout, and resolver path used.
Then fall back to Unity batchmode only when the task still requires tests.
Batchmode is outside this skill.

## References

- `references/protocol.md` for endpoints, request bodies, and response fields.
- `references/troubleshooting.md` for service failure cases.
- `references/run-e2e.py` for package-level smoke verification.
