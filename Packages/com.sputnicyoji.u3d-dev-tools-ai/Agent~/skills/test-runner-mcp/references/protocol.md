# Test Runner MCP protocol

Use `python client.py --project <unity-project-root> <command>` when the project root is known.
Project-aware resolution prefers `.u3d-ai-linker/ports.json`.
Legacy ports are `21890`, `21896`, `21897`.

## Endpoints

- `/ping` (GET or POST): returns service identity, Editor state, project name, project path, and port metadata.
- `/recompile` (POST only): requests script recompilation and returns compile state plus `hasErrors`.
- `/list-tests` (GET): accepts `mode` as `EditMode` or `PlayMode`; returns discovered test full names and count.
- `/run-tests` (POST only): starts an EditMode or PlayMode test job with optional filters; returns a job id.
- `/test-status` (GET): polls a job id until running ends and returns result counts plus failures.

Requests with an `Origin` header get 403; GET on a POST-only route gets 400.

## Test behavior

PlayMode uses temporary `DisableDomainReload` while the test job is active.
The client must treat filtered zero matches as error.
Run `list-tests` before exact filtered runs so a miss is caught before starting a job.
