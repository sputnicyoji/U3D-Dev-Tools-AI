# Test Runner MCP troubleshooting

## Ping timeout

Check the exact resolver command first.
Prefer `--project` so `.u3d-ai-linker/ports.json` can select the matching Editor.
Use `--pid` when several Editors share a project path.
Use `--port` only when the endpoint is already known.

## Non-Idle state

`Compiling` and `Running` block new test work.
Poll until `Idle`, or report the state as the blocker.

## Compile errors

Stop when `/recompile` returns `hasErrors=true`.
Report compiler errors before any test command.

## 409 running job

A test job is already running.
Poll `python client.py --project <unity-project-root> status --job-id <jobId>` for the known job when available. The matching HTTP endpoint is `/test-status?jobId=<jobId>`. Otherwise, wait until ping reports `Idle`.

## 404 job id

The job id is stale or belongs to another Editor process.
Resolve the endpoint again and start a new run only after confirming the filter.

## PlayMode dirty scene guard

PlayMode may be blocked by unsaved scene state.
Save or discard through the user-approved Unity workflow before rerunning.

## Batchmode fallback

Use batchmode only after the online service cannot be reached and the task still requires tests.
Record the failed ping command and timeout first.
