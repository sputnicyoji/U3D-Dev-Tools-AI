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

Physics raycasters and world objects are out of scope. A guide that points at a world object reports
`holeRect` with `targetId=null`.
