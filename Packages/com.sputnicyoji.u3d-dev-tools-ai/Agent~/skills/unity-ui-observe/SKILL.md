---
name: unity-ui-observe
description: Read live uGUI state in a Unity Editor as facts - top panel, whether a control can be clicked, whether a guide mask steals input - and act on reachable controls, world objects or map cells. Use before screenshots or scripted world interaction.
---

# Unity UI Observe

Use this skill to answer UI-state questions with facts, not pixels.
It talks to the EditorDebug service (`unity-editor-debug-mcp`); `act` and `world` change game state. Observation commands are read-only.
A screenshot shows pixels; it cannot tell which canvas wins the click or where a guide hole is.

## Operational loop

When automatic discovery has a project/PID scope (explicit or inferred from cwd), ping identity must match. Missing or mismatched identity is an error, not permission to use another Editor. Without a scope, legacy discovery remains available; `--port` explicitly overrides discovery.
Observation and interaction cover display 0 only; canvases targeting other displays are excluded.

1. Resolve the endpoint.
   Run `python client.py --project <unity-project-root> guide`.
   Use `--pid <editor-pid>` when several Editors are open.
   Completion: the response has `ok: true` and `meta.playing` matches the expected Editor state.

2. Check for input stealing first.
   Read `guide.active`, `guide.targetId` and `topLayer` (the highest layer that receives input).
   `active=true` with a `targetId` other than the control you expect means a guide owns the input.
   `active=true`, `holeRect` set and `targetId=null` means nothing uGUI sits under the hole (a pure world target).
   A world target that carries a world-space canvas button reports that button as `targetId`; check its layer's `renderMode`.
   Completion: you can say whether a guide is active and what it lets through.

3. Read the controls.
   Run `snapshot` (add `--all` only when you need non-interactive graphics).
   For a control, read `visible`, `interactable`, `hitTop` and `coveredBy`.
   `hitTop=false` with `coveredBy=<id>` names exactly what sits on top.
   Completion: the control id and its four flags are known.

4. Ask about a point when the question is spatial.
   Run `hit --x <px> --y <px>` (screen pixels, top-left origin, same as a screenshot).
   `clickHandler` is the object that would receive the click.
   Completion: the top hit and the click receiver are known.

5. Act only through the same hit test (Play mode).
   Run `act --id <node id>` to click, or `act --id <id> --to <x> <y>` to drag.
   It recomputes the hit first and refuses (`acted=false`, `reason`, exit code 2) when a real touch could not reach the control:
   `covered` (with `coveredBy`), `offscreen`, `not_interactable`, `not_playing`, `no_event_system` and others.
   `ambiguous` means the id exists in two scenes: pass `--iid` from the same snapshot.
   Events land in one frame: long-press, double-click and frame-timed handlers are not emulated.
   Completion: `acted=true` and a fresh `snapshot` shows the state you expected.

   World objects (city buildings, world-map units) are not canvas controls: use `world`.
   `world --path <scene path> [--iid <iid>]` clicks the object through the game's own scripted pointer, frame by frame,
   and checks the object the game reports it hit: `hit_mismatch` or `unverified` (exit 2) means the click did not land on the target.
   `world --at <x> <y> --action drag --by <dx> <dy>`, `--action scroll --scroll 1` and `--action pinch --scale 1.5` pan and zoom.
   It needs the game to register a scripted pointer (`no_scripted_pointer` otherwise).
   `world --cell <x> <y>` clicks a map cell when that pointer also implements `IScriptedPointerCells` (`no_cell_target` otherwise).
   The cell is projected and round-tripped through the game before pressing; `ambiguous_cell` refuses a different cell,
   and `cell_mismatch` or `unverified` means the game's reported tap did not verify the requested cell.
   `pointer_error`, cancellation or timeout can follow partial input. Read the resulting game state; do not retry automatically.

6. Report bounded evidence.
   Quote the ids and flags you relied on, not the whole snapshot.
   Use a screenshot only for visual checks the snapshot cannot express (art, layout polish).
   Completion: the answer can be reproduced from the quoted fields.

## Fallback

If the endpoint does not resolve, run `python client.py --project <root> guide` again with `--pid`, then report the exact error.
If `truncated` is true, page with `--offset` and `--max`.
Do not infer UI state from stale screenshots or logs.

## References

- `references/protocol.md` for endpoint parameters and response fields.
- `references/troubleshooting.md` for empty results, wrong screen size and id stability.
