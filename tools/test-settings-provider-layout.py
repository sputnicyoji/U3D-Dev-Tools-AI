from __future__ import annotations

import re
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
PROVIDER = ROOT / "Packages/com.sputnicyoji.u3d-dev-tools-ai/Editor/U3DAILinker/Settings/U3DAILinkerSettingsProvider.cs"


def _method_body(source: str, signature: str) -> str:
    marker = source.index(signature)
    start = source.index("{", marker)
    depth = 0
    for index in range(start, len(source)):
        char = source[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return source[start + 1:index]
    raise AssertionError(f"method body not found: {signature}")


def main() -> None:
    source = PROVIDER.read_text(encoding="utf-8")
    on_gui = _method_body(source, "private static void OnGui(string searchContext)")
    draw_tool_list = _method_body(source, "private static void DrawToolList()")

    assert "private static Vector2 _pageScroll;" in source, "settings page must keep a top-level scroll position"
    assert re.search(r"new\s+EditorGUILayout\.ScrollViewScope\(_pageScroll\)", on_gui), (
        "OnGui must wrap the full settings page in a top-level scroll view"
    )
    assert on_gui.index("DrawAgentSkillsSection();") < on_gui.index("DrawToolList();"), (
        "Agent Skills must be above the long tool list so it is immediately visible"
    )
    assert "_toolScroll" in draw_tool_list, "tool list must keep its own scroll state"
    assert "_pageScroll" not in draw_tool_list, "tool list must not reuse the page scroll state"


if __name__ == "__main__":
    main()
