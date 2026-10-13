# Troubleshooting

## Every control has hitTop=false and coveredBy=null

Nothing was hit. Check `meta.screen`: the Game view size drives the hit test.
A hidden or zero-size Game view gives wrong results; open the Game view.
Controls under a `GraphicRaycaster`-less canvas are never hit, which matches real input.

## guide.active=false while a guide is visible

The mask is not a custom `ICanvasRaycastFilter` graphic covering 80% of the screen,
or it is not a raycast target. The detector does not know game-specific guide classes by name.

## Ids changed between calls

Same-name siblings get `[i]` by position, so reordering a list changes ids.
Prefer authored panel paths; for list cells match on `text` as well.

## World-space clicks

Physics raycasters and world objects are out of scope; `act` cannot click a world object.
A guide that points at a world object reports `holeRect` with `targetId=null` only when no uGUI control sits under the hole.
Objects that carry a world-space canvas button (a click area on a character) report that button as `targetId`.

## act refused

Read `reason` first; `covered` comes with `coveredBy`, the control on top.
A guide mask makes everything outside its hole `covered`: act on `guide.targetId` instead.
`not_found` after the UI changed means the id moved; take a fresh `snapshot`.
A control whose handler needs a held press or several frames reacts differently to `act` than to a finger.
