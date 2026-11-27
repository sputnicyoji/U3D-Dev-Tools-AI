# Changelog

Notable changes to `com.sputnicyoji.u3d-dev-tools-ai`.
This log starts with the single-package release, 0.2.0. Release dates follow Git tag timestamps (UTC+8).

## Unreleased

### Added
- U3D AI Linker: agent skill sync works on macOS (verified on 6000.3.13f1) and uses the same code on Linux (untested). `.claude/skills/<name>` and `.agents/skills/<name>` become absolute symlinks to `.u3d-ai-linker/skills/<tool>` (created with libc `symlink`), while Windows keeps directory junctions. Previously the linker always used the Windows junction manager (`kernel32.dll`), so sync could not create links off Windows.

### Fixed
- U3D AI Linker: `Sync Agent Skills` installs skills and their links before it writes the `CLAUDE.md`/`AGENTS.md` managed blocks and the `.gitignore` block, so a failed skill sync no longer leaves managed blocks that point at skills that were never installed. Fragment order and duplicate-skill checks still run before any file is touched.
- Agent clients on macOS and Linux find the global port registry: `port_resolver.py` reads `instances.json` from `$XDG_DATA_HOME` (or `~/.local/share`), where the Editor's `LocalApplicationData` folder is under Mono, instead of `~/AppData/Local`. Windows still uses `LOCALAPPDATA`.
- `tools/run-editmode.py` reports a missing result XML as a license failure (exit 5) only for exit code 199 or a licensing timeout/abort line. The old pattern also matched the normal `LicensingClient` handshake that every log contains, so compile failures were reported as license failures.
- `tools/run-editmode.py` leaves `Temp/UnityLockfile` alone and stops (exit 3) when a running Unity holds it; on macOS and Linux the old script would have deleted a live Editor's lockfile.
- Tests: `PackageShapeTests.ServerAssembly_IsMarkedAlwaysLinkAssembly` no longer looks for `LuaDeviceDebugPlayerBootstrap` in the Editor, where that type is never compiled (`DEVELOPMENT_BUILD && !UNITY_EDITOR`); it still asserts `[assembly: AlwaysLinkAssembly]`.
- Tests: `UiObserverHttpTests` TearDown removes the Game view size it added by its total index, as `GameViewSizeGroup.RemoveCustomSize` expects. It used to fail with `Invalid index` and leave a "HTTP viewport test" size in the user-level Unity preferences.
- editor-debug: `/ui/act` world targets resolved by `iid` go through the same version-branched object lookup as `/invoke`, removing the CS0618 `EditorUtility.InstanceIDToObject(int)` warning on Unity 6000.3.
- test-runner: when the service starts after a domain reload, jobs that were still `running` are marked `error` ("orphaned: a domain reload interrupted the run before it reported a result"). Before, `/test-status` reported such a job as `running` forever, because the reloaded service can never receive its result.
- test-runner: a PlayMode run saves the Enter Play Mode settings it overrides (`DisableDomainReload`) to `Library/TestRunnerMCP/playmode-settings-backup.json`, and the service restores them and deletes the file on its next start. A domain reload or Editor crash during the run no longer leaves `DisableDomainReload` in the project settings. The file is in `Library` because Unity empties `Temp` when it opens a project.
- U3D AI Linker: rewriting an existing `CLAUDE.md`, `AGENTS.md` or `.gitignore` swaps in the new file with `File.Replace` instead of deleting the old one and moving the temp file in. A crash between those two steps could leave the file missing, with its content only in `<file>.u3dtmp`.
- U3D AI Linker: the `git ls-remote` lookups behind Refresh, Install and channel switches (dev branch SHA, stable tag SHAs) read stdout and stderr at the same time, give up after 15 s with `git ls-remote timed out ...`, kill git, and run with `GIT_TERMINAL_PROMPT=0`. Before, a remote that never answered or a credential prompt could freeze the Editor, because the call runs on the main thread with no timeout.

### Changed
- **Breaking:** the local HTTP services share one request guard (`Yoji.EditorCore.RequestGuard`; lua-device-debug has its own copy in the server assembly). State-changing routes accept POST only: test-runner `/recompile` and `/run-tests`, editor-debug `/invoke`, `/batch`, `/eval`, `/recompile` and `/ui/act`, lua-device-debug `/execute`. Before, a plain GET `/recompile` (for example from an `<img>` tag on a web page) recompiled scripts on both Editor services. Any request with an `Origin` header (including `Origin: null`) is now rejected by all three services; before, only editor-debug checked it. Responses keep each service's style: test-runner returns 403 for `Origin` and 400 for a non-POST method, editor-debug returns its `ok:false` envelope, lua-device-debug returns 403 `ORIGIN_REJECTED` and 400 `INVALID_REQUEST`. Read-only routes still accept GET, and Content-Type is still checked only on `/ui/act`.
- **Breaking:** lua-device-debug rejects requests whose Host name is not `127.0.0.1`, `localhost` or `[::1]` (403 `HOST_REJECTED`; any port is accepted, so adb forwarding keeps working). This blocks DNS rebinding, which the Editor services already got from `HttpListener`.
- The bundled test-runner client (`client.py recompile`) and `references/run-e2e.py` call `/recompile` with POST; older copies of the client that use GET get 400.
- README: describe agent skill links as junctions on Windows and symlinks on macOS and Linux instead of listing Windows as a requirement.
- The PowerShell scripts are replaced by Python 3.8+ scripts that run on Windows, macOS and Linux; Windows users now run them with `python` too:
  - `tools/run-editmode.py` (was `run-editmode.ps1`) keeps the options (`--project`, `--unity`, `--editor-version`, `--timeout-sec`), the ProjectSettings restore, the result paths and the exit codes, and also finds Editors in the macOS Unity Hub folder (`/Applications/Unity/Hub/Editor/<version>/Unity.app`). `U3D_EDITOR_ROOTS` is split on the platform path separator (`;` on Windows, `:` elsewhere).
  - `tools/check-u3d-linker-stable.py` (was `check-u3d-linker-stable.ps1`) runs the same stable registry and tag checks.
  - `Agent~/install_skills.py [skill ...] [--dest <dir>]` replaces the four per-skill `install.ps1` scripts and copies the same files to `~/.claude/skills/<skill>`.
  - `unity-ui-observe` evidence is saved with `python evidence.py act|shot` on every platform.
- U3D AI Linker: the `Run Windows Junction Smoke` menu item uses the platform link manager, so off Windows it would exercise symlinks (not run there); on Windows it is unchanged.
- README: list the validated Editors (2022.3.62f2c1, 6000.3.12f1, 2022.3.62f3c1, 6000.3.13f1) and state that 6000.4+ is unverified and 6000.5 is known incompatible until the `InstanceID` to `EntityId` migration.
- Test projects: every `TestProjects/*/Packages/packages-lock.json` is ignored, and `TestProjects/editor-debug/Packages/packages-lock.json` is no longer tracked. This reverses the 0.2.19 change that committed it: only one fixture had a tracked lock, the test-runner one was neither tracked nor ignored, and Unity rewrites the lock for each Editor version.
- CI: a GitHub Actions workflow (`.github/workflows/ci.yml`, ubuntu-latest, on push and pull request) runs `tools/test-*.py`, byte-compiles the Python under `tools/` and `Agent~`, and checks `Registry/*.json`, the package's `package.json` and its `Registry/*.json` with `python -m json.tool`. Unity tests still run locally.
- Docs: removed local absolute repo paths and a downstream project name from `docs/`, the `unity-ui-observe` protocol reference and test comments. `docs/superpowers/` is no longer in `.gitignore`, since its files are tracked.

### Removed
- PowerShell scripts: the four `Agent~/skills/*/install.ps1`, `unity-ui-observe` `act.ps1` and `shot.ps1`, `tools/run-editmode.ps1` and `tools/check-u3d-linker-stable.ps1`. Use the Python scripts listed under Changed.

## [0.2.23] - 2026-11-27

### Fixed
- lua-device-debug: mark `Yoji.LuaDeviceDebug.Server` with `[assembly: AlwaysLinkAssembly]` so IL2CPP preserves `LuaDeviceDebugPlayerBootstrap` and the HTTP debug server in Development builds without requiring consuming projects to add a `link.xml`, while still excluding the assembly from Release builds via `defineConstraints`.

## [0.2.22] - 2026-11-24

### Fixed
- editor-debug: sample HTTP UI snapshots and hit tests from the active Play Mode game frame so viewport dimensions and raycasts agree.

## [0.2.21] - 2026-10-27

### Changed
- lua-device-debug: the HTTP server, Player bootstrap, main-thread dispatcher and JSON guard move to a new assembly `Yoji.LuaDeviceDebug.Server` with define constraint `UNITY_EDITOR || DEVELOPMENT_BUILD`, so release builds no longer contain them. `ILuaDeviceDebugHost`, `LuaDeviceDebugRuntime`, `CommandDescriptor`, `LuaDeviceDebugPackage` and `LuaDeviceDebugRuntimeConfig` stay in `Yoji.LuaDeviceDebug.Runtime` (same name and GUID), so host code compiles in every build without `#if`. Code that used `LuaDeviceDebugServer` directly must reference `Yoji.LuaDeviceDebug.Server` and compile under the same define.
- `Yoji.LuaDeviceDebug.Runtime` no longer references `Newtonsoft.Json.dll`.

## [0.2.20] - 2026-10-27

### Changed
- editor-debug `/eval` is on by default (service 0.1.5). It parses one-line member chains and runs each step through the same reflection path as `/invoke`, so keeping it off removed no attack surface; `c_AllowEval = false` stays as an emergency switch. A syntax error returns `System.FormatException`.

### Added
- EditMode test for a static member declared on a generic base and read through the derived type (`Xxx.I` singletons) via `/eval`.

## [0.2.19] - 2026-10-14

### Added
- `unity-ui-observe` evidence helper: `evidence.py act|shot` (`act.ps1` / `shot.ps1` wrappers) saves the act result, a Game view screenshot and a `.frame.json` sidecar (`Time.frameCount` before the capture request and after the file appeared) under one auto-incremented number. Existing evidence is never overwritten unless `--force` / `-Force`.
- `/ui/act` world `drag` reads the camera back like `scroll` and `pinch`: `cameraBefore` before the first frame and `cameraAfter` after the game ran the last frame, so a map pan is verified from `position` instead of a separate camera read.

### Changed
- `tools/run-editmode.ps1` takes `-EditorVersion` (writes it into ProjectVersion.txt for the run), restores every ProjectSettings file afterwards, and reports exit code 199 / licensing-client errors as a license failure (exit 5) instead of a compile failure.
- The editor-debug test project commits its `packages-lock.json`; `.u3d-ai-linker/` and `YojiDevToolPorts.json` are ignored.

### Fixed
- `/invoke` results format NaN, Infinity, enums, numeric dictionary keys and UI Toolkit picking modes with the invariant culture (floats and doubles round-trip with `R`); on a Chinese (zh-TW) system NaN used to come back as `非數值`.
- `EditorServiceEndpoint` tests no longer probe the real port 29980: `StartForTests` takes an optional `IPortProbe`, and the fixed-port conflict test is deterministic.

## [0.2.18] - 2026-10-14

### Added
- `install.ps1` for the `unity-lua-device-debug` skill, matching the other three skills: copies `SKILL.md`, `client.py`, `port_resolver.py` and `references/*.md` to `~/.claude/skills/unity-lua-device-debug`.

## [0.2.17] - 2026-10-13

### Added
- Optional `IScriptedPointerInputBlocker`: when the game reports that it is swallowing world input as a whole (for example an open panel that makes the map ignore taps even where it does not cover the press point), `/ui/act` world and cell gestures (click, drag, scroll, pinch) are refused with `blocked` and the blocker in `coveredBy` before any frame is fed. A UI graphic on the press point still answers `covered` first; a null or destroyed blocker does not block.

### Fixed
- Apply scripted world-pointer frames before the game's `Update` methods instead of from the Editor update loop; completion and cancellation remove only the gesture's PlayerLoop node.
- Terminate world gestures with `pointer_error` when pointer callbacks fail, without turning a failed game hit report into a physics-verified success. Interrupted gestures can have partial effects and are not safe to retry automatically.
- Require primary-display, non-RenderTexture cameras on every world/cell camera-selection path, and require an object camera to render the target's layer.
- Point the README installation URL at the current 0.2.16 release and document the `world --cell` command.

## [0.2.16] - 2026-10-13

### Added
- `/ui/act` cell targets: `target: {cell: {x, y}}` clicks a map cell through a registered pointer that also implements `IScriptedPointerCells`; the cell is round-tripped through the game before pressing (`ambiguous_cell`), refusals include `no_cell_target`, `no_camera`, `out_of_map`, `behind_camera`, `offscreen` and `covered` with `coveredBy`, and the click is verified against the cell the game reports (`cell_mismatch`, `unverified`) with the reported unit merged in as `unit {path, iid}`. The UI client `world` command gains `--cell`.
- World `scroll` and `pinch` results carry `cameraBefore` and `cameraAfter` (`orthographic`, `orthographicSize` or `fieldOfView`, `position`).

### Changed
- World targets with a screen-space depth of zero or less are refused as `behind_camera` for perspective and orthographic cameras; targets nearer than the near clip plane are `offscreen`.

### Fixed
- Verify project/PID identity during automatic endpoint discovery instead of falling back to a different Editor or an unverified port; explicit `--port` remains an override.
- Refresh UI hits before release so targets revealed by the final drag step receive drop events.
- Resolve click/drag handlers after pointer-down callbacks, including handlers enabled or disabled by those callbacks.
- Select the topmost guide mask within the same Canvas layer.
- Respect `overrideSorting` boundaries for inherited raycast blocking without changing alpha/interactable inheritance.
- Exclude non-primary display canvases consistently from UI observation and interaction.

## [0.2.15] - 2026-10-13

### Added
- World-object `/ui/act` through a game-registered `IScriptedPointer`, with click verification against the game's reported hit and the UI client's `world` command.
- Frame-based world drag, scroll and two-finger pinch gestures, with pre-input refusal for behind-camera, offscreen and UI-covered targets.
- Optional 3D/2D physics fallback for unreported object clicks; physics modules are used only when installed by the project.

## [0.2.14] - 2026-10-13

### Added
- `/ui/act` and the UI client `act` command for clicking or dragging uGUI controls by ID, with optional instance-ID disambiguation.
- Fresh hit testing before interaction, with explicit refusal reasons for covered, offscreen, non-interactable, or otherwise unreachable targets.
- POST-only, `application/json` validation for `/ui/act`.

Interactions require Play mode and an active EventSystem. Events are injected in one frame; device input, long presses, double clicks, and frame-timed handlers are not emulated.

## [0.2.13] - 2026-10-13

### Fixed
- Skip Graphics without a CanvasRenderer during UI observation instead of failing the snapshot.

## [0.2.12] - 2026-10-12

### Added
- Semantic uGUI observation through `/ui/snapshot` and `/ui/hit`, including layer stacks, control state, hit targets, occlusion, and sampled guide-mask/hole detection.
- The `unity-ui-observe` skill, Python client, and managed Claude Code / Codex rule fragments.

### Changed
- Reject Editor Debug requests carrying an `Origin` header.
- Add the uGUI package dependency and include the UI skill in agent sync and package checks.

## [0.2.11] - 2026-10-12

### Changed
- Set the package author to `Yoji` so Package Manager groups it with the author's other packages.

## [0.2.10] - 2026-09-11

### Changed
- Drive TestRunner running-job heartbeats through `EditorApplication.update` instead of a background timer.

### Fixed
- Update Unity compatibility source checks to use the merged package's `Editor/TestRunner` paths.

## [0.2.9] - 2026-09-11

### Fixed
- Refresh running test-job heartbeats so stale-job cleanup does not reap long-running tests.

## [0.2.8] - 2026-08-28

### Fixed
- Include missing package folder `.meta` files and `package.json.meta` for Unity imports.

## [0.2.7] - 2026-07-14

### Fixed
- Retry additional recoverable Windows file-replacement errors when persisting port settings and registries.

## [0.2.6] - 2026-07-14

### Fixed
- Prevent Editor services from starting in Unity asset-import worker processes.

## [0.2.5] - 2026-07-06

### Added
- Editor Debug `invoke --defer` for calls that trigger domain reload or Play mode transitions after the HTTP response is sent.

### Fixed
- Avoid Editor Debug shutdown deadlocks during domain reload by draining pending main-thread work.
- Repair missing live-service registry entries and remove dead-process records from project and global port registries.
- Keep live processes discoverable when their recorded heartbeat is stale.

## [0.2.4] - 2026-06-25

### Fixed
- Include missing Unity metadata for the agent skill state model and its tests.
- Resolve annotated Git tags to their peeled commit SHA, with fallback for lightweight tags.

## [0.2.3] - 2026-06-25

### Fixed
- Keep Agent Skills visible above the tool list and add a top-level Settings page scroll view with independent tool-list scrolling.

## [0.2.2] - 2026-06-25

### Added
- Per-skill sync status in Project Settings: `Synced`, `Stale`, `Missing`, `Conflict`, and `SourceMissing`.

### Changed
- Refine bundled skills into project-aware operational loops with protocol and troubleshooting references.
- Document the package-update and `Sync Agent Skills` workflow.

## [0.2.1] - 2026-06-25

### Fixed
- Retry transient sharing and lock failures during atomic port-persistence writes without deleting the existing file on failure.
- Report heartbeat persistence failures as warnings rather than errors.

## [0.2.0] - 2026-06-25

### Changed
- Merge Core, Editor Debug, TestRunner, Lua Device Debug, and U3D AI Linker into one UPM package, `com.sputnicyoji.u3d-dev-tools-ai`.
- Preserve independent internal assemblies while bundling Python clients, skills, and managed agent rule fragments under `Agent~`.
- Migrate stable/dev registries, test hosts, installation examples, and agent sync to the single package.

[0.2.23]: https://github.com/sputnicyoji/U3D-Dev-Tools-AI/compare/u3d-dev-tools-ai-v0.2.22...u3d-dev-tools-ai-v0.2.23
[0.2.22]: https://github.com/sputnicyoji/U3D-Dev-Tools-AI/compare/u3d-dev-tools-ai-v0.2.21...u3d-dev-tools-ai-v0.2.22
[0.2.21]: https://github.com/sputnicyoji/U3D-Dev-Tools-AI/compare/u3d-dev-tools-ai-v0.2.20...u3d-dev-tools-ai-v0.2.21
[0.2.20]: https://github.com/sputnicyoji/U3D-Dev-Tools-AI/compare/u3d-dev-tools-ai-v0.2.19...u3d-dev-tools-ai-v0.2.20
[0.2.19]: https://github.com/sputnicyoji/U3D-Dev-Tools-AI/compare/u3d-dev-tools-ai-v0.2.18...u3d-dev-tools-ai-v0.2.19
[0.2.18]: https://github.com/sputnicyoji/U3D-Dev-Tools-AI/compare/u3d-dev-tools-ai-v0.2.17...u3d-dev-tools-ai-v0.2.18
[0.2.17]: https://github.com/sputnicyoji/U3D-Dev-Tools-AI/compare/u3d-dev-tools-ai-v0.2.16...u3d-dev-tools-ai-v0.2.17
[0.2.16]: https://github.com/sputnicyoji/U3D-Dev-Tools-AI/compare/u3d-dev-tools-ai-v0.2.15...u3d-dev-tools-ai-v0.2.16
[0.2.15]: https://github.com/sputnicyoji/U3D-Dev-Tools-AI/compare/u3d-dev-tools-ai-v0.2.14...u3d-dev-tools-ai-v0.2.15
[0.2.14]: https://github.com/sputnicyoji/U3D-Dev-Tools-AI/compare/u3d-dev-tools-ai-v0.2.13...u3d-dev-tools-ai-v0.2.14
[0.2.13]: https://github.com/sputnicyoji/U3D-Dev-Tools-AI/compare/u3d-dev-tools-ai-v0.2.12...u3d-dev-tools-ai-v0.2.13
[0.2.12]: https://github.com/sputnicyoji/U3D-Dev-Tools-AI/compare/u3d-dev-tools-ai-v0.2.11...u3d-dev-tools-ai-v0.2.12
[0.2.11]: https://github.com/sputnicyoji/U3D-Dev-Tools-AI/compare/u3d-dev-tools-ai-v0.2.10...u3d-dev-tools-ai-v0.2.11
[0.2.10]: https://github.com/sputnicyoji/U3D-Dev-Tools-AI/compare/u3d-dev-tools-ai-v0.2.9...u3d-dev-tools-ai-v0.2.10
[0.2.9]: https://github.com/sputnicyoji/U3D-Dev-Tools-AI/compare/u3d-dev-tools-ai-v0.2.8...u3d-dev-tools-ai-v0.2.9
[0.2.8]: https://github.com/sputnicyoji/U3D-Dev-Tools-AI/compare/u3d-dev-tools-ai-v0.2.7...u3d-dev-tools-ai-v0.2.8
[0.2.7]: https://github.com/sputnicyoji/U3D-Dev-Tools-AI/compare/u3d-dev-tools-ai-v0.2.6...u3d-dev-tools-ai-v0.2.7
[0.2.6]: https://github.com/sputnicyoji/U3D-Dev-Tools-AI/compare/u3d-dev-tools-ai-v0.2.5...u3d-dev-tools-ai-v0.2.6
[0.2.5]: https://github.com/sputnicyoji/U3D-Dev-Tools-AI/compare/u3d-dev-tools-ai-v0.2.4...u3d-dev-tools-ai-v0.2.5
[0.2.4]: https://github.com/sputnicyoji/U3D-Dev-Tools-AI/compare/u3d-dev-tools-ai-v0.2.3...u3d-dev-tools-ai-v0.2.4
[0.2.3]: https://github.com/sputnicyoji/U3D-Dev-Tools-AI/compare/u3d-dev-tools-ai-v0.2.2...u3d-dev-tools-ai-v0.2.3
[0.2.2]: https://github.com/sputnicyoji/U3D-Dev-Tools-AI/compare/u3d-dev-tools-ai-v0.2.1...u3d-dev-tools-ai-v0.2.2
[0.2.1]: https://github.com/sputnicyoji/U3D-Dev-Tools-AI/compare/u3d-dev-tools-ai-v0.2.0...u3d-dev-tools-ai-v0.2.1
[0.2.0]: https://github.com/sputnicyoji/U3D-Dev-Tools-AI/tree/u3d-dev-tools-ai-v0.2.0
