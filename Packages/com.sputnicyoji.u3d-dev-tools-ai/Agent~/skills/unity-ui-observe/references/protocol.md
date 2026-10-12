# UI observe protocol

Both endpoints live on the EditorDebug service and accept GET query parameters or a JSON POST body.
Requests carrying an `Origin` header are rejected; the listener itself answers `400 Invalid host` for any `Host` other than `127.0.0.1`.
Query parameters are honoured only on `/ui/` routes; other EditorDebug routes take a JSON body.
Envelope: `{ "ok": bool, "elapsedMs": int, "result": {...} }`; errors use `ok: false` and `error`.

Coordinates in responses and in `/ui/hit` are screen pixels with a top-left origin.

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
