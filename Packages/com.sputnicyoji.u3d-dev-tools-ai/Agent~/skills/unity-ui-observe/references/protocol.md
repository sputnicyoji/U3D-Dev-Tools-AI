# UI observe protocol

The endpoints live on the EditorDebug service. `/ui/snapshot` and `/ui/hit` accept GET query parameters or a JSON POST body;
`/ui/act` accepts only POST with `Content-Type: application/json`.
Requests carrying an `Origin` header are rejected; the listener itself answers `400 Invalid host` for any `Host` other than `127.0.0.1`.
Query parameters are honoured only on `/ui/snapshot` and `/ui/hit`; every other route takes a JSON body.
Envelope: `{ "ok": bool, "elapsedMs": int, "result": {...} }`; errors use `ok: false` and `error`.

Coordinates in requests and responses are screen pixels with a top-left origin.

## GET /ui/snapshot

| Param | Default | Meaning |
|---|---|---|
| `include` | interactive | `all` adds every active Graphic |
| `max` | 500 | page size |
| `offset` | 0 | page start |
| `step` | 24 | guide probe grid in pixels |

A parameter that is present but not an integer is an error, not a silent fallback.

Result:

- `meta`: `frame`, `scene`, `playing`, `screen {w,h}`, `eventSystemPresent`, `currentSelected`.
- `layers`: root canvases and `overrideSorting` canvases, topmost first.
  Fields `id`, `name`, `sortingOrder`, `overrideSorting`, `renderMode`, `receivesInput`, `blocksBelow`, `topmost`.
  `receivesInput` means the canvas has an enabled `GraphicRaycaster`; decorative canvases (a guide finger, rings) are listed but cannot take clicks.
  `topmost` marks the highest layer that receives input, which may not be index 0.
  `blocksBelow` means a raycast-target graphic, reachable by a raycaster, covers at least 80% of the screen on that layer.
- `nodes`: interactive controls (an enabled `IEventSystemHandler` on the object: Selectable, ScrollRect, custom click handlers).
  Fields `id`, `iid`, `path`, `type`, `text`, `rect {x,y,w,h}`, `visible`, `interactable`, `raycastTarget`, `hitTop`, `coveredBy`.
  `hitTop` is computed by hitting the control's centre; true when the top hit is the control or its child.
- `total`, `offset`, `truncated`: paging.
- `guide`: `{ active }`, or when a mask exists `{ active, maskId, step, holeRect, targetId }`.

Guide detection: a mask is an active raycast-target Graphic whose type implements `ICanvasRaycastFilter`,
is outside `UnityEngine*` / `TMPro` / `Unity.*` namespaces, and covers at least 80% of the screen.
The topmost such mask wins. Its filter is asked on a `step` grid plus every control centre;
points it lets through form `holeRect` (precision +-step). `targetId` is the smallest `hitTop` control whose centre passes.
`holeRect=null` means the mask blocks everything.

## GET /ui/hit?x=&y=

Result: `x`, `y`, `screen`, `top` (id of the top hit graphic), `clickHandler` (object that receives the click),
`total`, `hits` (up to 8, topmost first, each `id`, `type`, `sortingOrder`, `depth`).

## Ids

`id` is the hierarchy path, with `[i]` only for same-name siblings (scene roots included).
It starts at the root canvas when that canvas is a scene root, otherwise at the scene root,
so same-named canvases under different parents (per-character click areas) stay distinct.
It is stable for authored panels; list cells are stable only while the list content is unchanged.
`iid` is the instance id and is valid for the current frame only. `path` is the full scene path without indices.

## POST /ui/act

Body: `{ "id": "<node id>", "action": "click" | "drag", "at": {x,y}?, "to": {x,y}?, "steps": int?, "iid": int? }`.
`action` defaults to `click`; `drag` requires `to`; `steps` (1..120, default 8) is the number of drag events; `at` is a press point inside the control (default: its centre).
`iid` (the node's `iid` from the same snapshot) picks one object when an id is shared by same-named roots in different scenes.
JSON `null` means absent; any other non-numeric value is an error naming the key.

Before injecting, the hit at the press point is recomputed with the same rules as `snapshot`.
The request is refused with `ok: true`, `acted: false`, `reason`, `message` when a real touch could not reach the control:

| reason | meaning |
|---|---|
| `not_playing` | Editor is not in Play mode (Edit-mode clicks would fire persistent listeners on scene assets) |
| `no_event_system` | no active EventSystem, so real input would not reach UI either |
| `not_found` / `ambiguous` | no active object has the id (and `iid`), or several do and no `iid` was given |
| `not_ui` | the target has no RectTransform |
| `not_interactable` | the Selectable that would receive the event (`receiver`) is not interactable, e.g. the label of a disabled button |
| `offscreen` / `outside_target` | press point outside the screen, or `at` outside the control |
| `no_hit` / `covered` | nothing hit, or another control is on top (`coveredBy` names it) |
| `no_handler` / `no_drag_handler` | no pointer-down/click (or drag) handler on the hit object or its parents |

On success: `acted: true`, `at`, `top`, `pressHandler`, `clickHandler` (null when the click was not delivered), and for drags `dragHandler`, `dragged` (false when the move stayed under the drag threshold), `to`, `release`.
Visibility (CanvasGroup alpha) is not checked: raycasts ignore alpha, so a transparent control still takes a real touch.
Malformed parameters are errors (`ok: false`).

Event order follows `StandaloneInputModule`'s mouse path step by step: move (enter/exit up the parent chain, as `BaseInputModule.HandlePointerExitAndEnter`),
press (deselects the current selection when the press lands elsewhere, down, initializePotentialDrag),
per drag step move + beginDrag once past `pixelDragThreshold` + drag (a press handler that is not the drag handler gets up and loses the click),
release (up, click if still eligible and released over the same click handler, drop, endDrag), then a final exit as when a finger lifts.
Hits are recomputed after every drag step, so a drop zone shown by beginDrag can receive the drop.
It bypasses the input module and device layer and runs in one frame: long-press, double-click (`clickCount` is always 1) and frame-timed handlers are not emulated.

## POST /ui/act with a world target (Phase 3)

Body: `{ "target": {"path", "iid"}?, "at": {x,y}?, "action": "click" | "drag" | "scroll" | "pinch", "to"?, "by"?, "steps"?, "scroll"?, "scale"?, "span"?, "camera"? }`.
A request with `target`, or with `at` and no `id`, takes this path; everything else is the canvas `act` above.

- `target.path` is the hierarchy path in the same `id` format (`[i]` for same-name siblings, from the scene root); `target.iid` the instance id.
  Give either or both: with both, a path that no longer matches the iid is refused as `stale`.
- `at` (instead of `target`) is a screen point for `drag`, `scroll` and `pinch`. `click` always needs a `target`.
- `drag` takes exactly one of `to` (end point) or `by` (offset, top-left origin); `steps` move frames (default 8).
- `scroll` takes `scroll` in wheel notches (positive = away from the user), spread over `steps` frames (default 1).
- `pinch` puts two fingers `span` pixels apart (default 200) centred on the point and moves them to `span * scale` over `steps` frames (default 8); `scale > 1` spreads them.
- `camera` is a camera's scene path; by default `Camera.main` is used when it renders the target's layer, then other cameras by depth.

The package does not inject into the game's input itself. The game registers an `IScriptedPointer` with
`Yoji.EditorDebug.Ui.ScriptedPointerRegistry.Register` (Editor code), and the act feeds it one `ScriptedPointerFrame`
(button phase, position, wheel delta, touches) per player frame, so the game's own tap rule, drag threshold and UI check run unchanged.
Frames advance only when `Time.frameCount` does: `Down` and `Up` are each fed for exactly one frame (a click is Down, Held, Up, None),
and a paused Editor stalls the gesture instead of repeating a frame. The HTTP call returns when the gesture ends (at most 20 s, then `timeout`).

The screen point is the target camera's `WorldToScreenPoint` of the target's renderer bounds centre (collider bounds, then its position, when it has no renderer).
Refusals (`acted: false`, nothing was fed):

| reason | meaning |
|---|---|
| `not_playing` / `no_scripted_pointer` / `busy` | not in Play mode, no pointer registered, or another world gesture is running |
| `not_found` / `ambiguous` / `stale` | no active object, several objects with the path (pass `iid`), or the iid now has another path |
| `not_world` | the target is a canvas control; use `id` |
| `no_camera` | no active camera renders the target's layer, or `camera` names none |
| `behind_camera` / `offscreen` | the point is behind the camera, past its far plane, or outside the screen or camera viewport (a pinch finger included) |
| `covered` | a UI graphic (GraphicRaycaster) is on top; `coveredBy` names it, as `hitTop` would |

After a `click`, the result reports what the game hit: `hit {path, iid}` and `hitSource`.
`game` means the game reported the object its tap resolved to (`IScriptedPointer.TryGetReportedHit`);
when it reported nothing the package falls back to a 3D then 2D physics ray (`physics`), and `none` when that finds nothing.
`verified` is true only when the hit is the target or its child. Otherwise `acted: true` with
`hit_mismatch` (the game hit something else or nothing) or `unverified` (no report and no collider: the click was sent but its effect is unknown).
Results also carry `camera`, `at`, `world` (aim point), `framesApplied`, and for drags `to`, for pinches `span {from,to}`.