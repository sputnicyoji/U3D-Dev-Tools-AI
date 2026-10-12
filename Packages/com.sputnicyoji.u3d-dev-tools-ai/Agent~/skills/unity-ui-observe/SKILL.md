---
name: unity-ui-observe
description: Read the live uGUI state of a Unity Editor as structured facts - which panel is on top, whether a control can be clicked, whether a guide mask steals input. Use before screenshots when judging UI state.
---

# Unity UI Observe

Use this skill to answer UI-state questions with facts, not pixels.
It is read-only and talks to the EditorDebug service (`unity-editor-debug-mcp`).
A screenshot shows pixels; it cannot tell which canvas wins the click or where a guide hole is.

## Operational loop

1. Resolve the endpoint.
   Run `python client.py --project <unity-project-root> guide`.
   Use `--pid <editor-pid>` when several Editors are open.
   Completion: the response has `ok: true` and `meta.playing` matches the expected Editor state.

2. Check for input stealing first.
   Read `guide.active`, `guide.targetId` and `topLayer` (the highest layer that receives input).
   `active=true` with a `targetId` other than the control you expect means a guide owns the input.
   `active=true`, `holeRect` set and `targetId=null` means the guide points at a world-space target.
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

5. Report bounded evidence.
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
