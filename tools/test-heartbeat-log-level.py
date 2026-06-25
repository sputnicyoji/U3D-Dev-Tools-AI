from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "Packages" / "com.sputnicyoji.u3d-dev-tools-ai" / "Editor" / "Core" / "Ports" / "EditorServiceEndpoint.cs"


def test_heartbeat_persistence_failure_is_warning_not_error():
    text = SOURCE.read_text(encoding="utf-8-sig")
    marker = '"[EditorServiceEndpoint] 心跳刷新失败: "'
    assert marker in text
    marker_index = text.index(marker)
    nearby = text[max(0, marker_index - 80): marker_index + 120]
    assert "Debug.LogWarning" in nearby
    assert "Debug.LogError" not in nearby


if __name__ == "__main__":
    for name, fn in sorted(globals().items()):
        if name.startswith("test_") and callable(fn):
            fn()
            print(f"OK {name}")
