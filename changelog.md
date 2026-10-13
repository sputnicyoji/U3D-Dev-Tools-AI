# Changelog

Notable changes to `com.sputnicyoji.u3d-dev-tools-ai`.
This log starts with the single-package release, 0.2.0. Release dates follow Git tag timestamps (UTC+8).

## Unreleased

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
