Use `unity-ui-observe` to judge live uGUI state before taking a screenshot.
Which panel is on top, whether a control can be clicked, whether a guide mask steals input: run
`python client.py --project <unity-project-root> guide`, then `snapshot` or `hit --x --y` (top-left pixels).
Read `hitTop` / `coveredBy` per control and `guide.active` / `targetId`. Screenshots are for visual checks only.
