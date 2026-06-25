from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "Packages" / "com.sputnicyoji.u3d-dev-tools-ai" / "Editor" / "Core" / "Ports" / "ServicePortSettingsStore.cs"


def port_persistence_io_source() -> str:
    text = SOURCE.read_text(encoding="utf-8-sig")
    start = text.index("internal static class PortPersistenceIO")
    end = text.index("internal static class PortPersistenceLock")
    return text[start:end]


def test_write_json_atomic_retries_transient_replace_failures():
    text = port_persistence_io_source()
    assert "ReplaceOrMoveWithRetry" in text
    assert "Thread.Sleep" in text
    assert "IOException" in text
    assert "UnauthorizedAccessException" in text
    assert "c_HResultSharingViolation" in text
    assert "c_HResultLockViolation" in text


def test_write_json_atomic_preserves_existing_file_when_retries_fail():
    text = port_persistence_io_source()
    assert "failed to atomically replace JSON file after retries" in text
    assert "File.Delete(path)" not in text
    assert "DeleteThenMove" not in text


if __name__ == "__main__":
    for name, fn in sorted(globals().items()):
        if name.startswith("test_") and callable(fn):
            fn()
            print(f"OK {name}")
