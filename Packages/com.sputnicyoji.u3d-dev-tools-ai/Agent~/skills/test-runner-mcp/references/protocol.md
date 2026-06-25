# Test Runner MCP protocol

Use `python client.py --project <unity-project-root> <command>` when the project root is known.
Project-aware resolution prefers `.u3d-ai-linker/ports.json`.
Legacy ports are `21890`, `21896`, `21897`.

## Endpoints

- `/ping`: returns service identity, Editor state, project name, project path, and port metadata.
- `/recompile`: requests script recompilation and returns compile state plus `hasErrors`.
- `/list-tests`: accepts `mode` as `EditMode` or `PlayMode`; returns discovered test full names and count.
- `/run-tests`: starts an EditMode or PlayMode test job with optional filters; returns a job id.
- `/test-status`: polls a job id until running ends and returns result counts plus failures.

## Test behavior

PlayMode uses temporary `DisableDomainReload` while the test job is active.
The client must treat filtered zero matches as error.
Run `list-tests` before exact filtered runs so a miss is caught before starting a job.
