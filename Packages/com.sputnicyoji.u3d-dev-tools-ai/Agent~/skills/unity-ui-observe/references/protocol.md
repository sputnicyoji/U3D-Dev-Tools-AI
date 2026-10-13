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
