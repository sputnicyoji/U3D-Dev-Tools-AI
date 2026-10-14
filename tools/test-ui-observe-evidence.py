"""unity-ui-observe evidence helper: auto-numbered act/shot evidence that never overwrites silently.

Runs evidence.py against fake ui-observe / editor-debug clients, so no Unity Editor is needed.
"""
from __future__ import annotations

import json
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SKILL = ROOT / "Packages" / "com.sputnicyoji.u3d-dev-tools-ai" / "Agent~" / "skills" / "unity-ui-observe"
EVIDENCE = SKILL / "evidence.py"

FAKE_DEBUG = r'''
import json, pathlib, sys
args = sys.argv[1:]
counter = pathlib.Path(__file__).with_name("frame.txt")
member = args[args.index("--member") + 1] if "--member" in args else ""
if member == "frameCount":
    n = int(counter.read_text()) if counter.exists() else 100
    counter.write_text(str(n + 3))
    print(json.dumps({"ok": True, "data": {"result": n}}))
elif member == "CaptureScreenshot":
    path = pathlib.Path(args[args.index("--args") + 1])
    path.write_bytes(b"\x89PNG fake")
    print(json.dumps({"ok": True, "data": {"void": True, "result": None}}))
else:
    print(json.dumps({"ok": True, "data": {"void": True, "result": None}}))
'''

FAKE_UI = r'''
import json, sys
print(json.dumps({"ok": True, "result": {"acted": True, "argv": sys.argv[1:]}}))
'''


def sandbox() -> Path:
    tmp = Path(tempfile.mkdtemp(prefix="ui-evidence-test-"))
    (tmp / "debug.py").write_text(FAKE_DEBUG)
    (tmp / "ui.py").write_text(FAKE_UI)
    (tmp / "out").mkdir()
    return tmp


def run(tmp: Path, command: str, *args: str) -> subprocess.CompletedProcess:
    # common options first: act takes everything after "--" as world arguments
    return subprocess.run(
        [sys.executable, str(EVIDENCE), command,
         "--dir", str(tmp / "out"), "--project", str(tmp), "--settle", "0",
         "--debug-client", str(tmp / "debug.py"), "--ui-client", str(tmp / "ui.py"), *args],
        capture_output=True, text=True)


def test_next_number_continues_after_the_highest_existing_prefix() -> None:
    tmp = sandbox()
    try:
        (tmp / "out" / "001-start.png").write_bytes(b"x")
        (tmp / "out" / "007-drag.json").write_text("{}")
        (tmp / "out" / "notes.txt").write_text("")
        result = run(tmp, "shot", "--label", "after drag")
        assert result.returncode == 0, result.stdout + result.stderr
        assert (tmp / "out" / "008-after-drag.png").is_file()
        sidecar = json.loads((tmp / "out" / "008-after-drag.frame.json").read_text())
        assert sidecar["frameBefore"] == 100 and sidecar["frameAfter"] == 103, sidecar
        assert sidecar["png"] == "008-after-drag.png"
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


def test_explicit_number_refuses_to_overwrite_without_force() -> None:
    tmp = sandbox()
    try:
        (tmp / "out" / "004-cell.png").write_bytes(b"old")
        refused = run(tmp, "shot", "--label", "cell", "--seq", "4")
        assert refused.returncode == 3, refused.stdout + refused.stderr
        assert "exists" in (refused.stdout + refused.stderr)
        assert (tmp / "out" / "004-cell.png").read_bytes() == b"old"

        forced = run(tmp, "shot", "--label", "cell", "--seq", "4", "--force")
        assert forced.returncode == 0, forced.stdout + forced.stderr
        assert (tmp / "out" / "004-cell.png").read_bytes() != b"old"
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


def test_act_writes_result_and_screenshot_under_one_number() -> None:
    tmp = sandbox()
    try:
        result = run(tmp, "act", "--label", "cell 498 503", "--", "--cell", "498", "503")
        assert result.returncode == 0, result.stdout + result.stderr
        act = json.loads((tmp / "out" / "001-cell-498-503.json").read_text())
        assert act["result"]["acted"] is True
        assert act["result"]["argv"][-4:] == ["world", "--cell", "498", "503"], act
        assert (tmp / "out" / "001-cell-498-503.png").is_file()
        assert (tmp / "out" / "001-cell-498-503.frame.json").is_file()
        again = run(tmp, "act", "--label", "cell 498 503", "--", "--cell", "498", "503")
        assert again.returncode == 0
        assert (tmp / "out" / "002-cell-498-503.json").is_file(), "same label gets the next number, no overwrite"
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


if __name__ == "__main__":
    for test in [
        test_next_number_continues_after_the_highest_existing_prefix,
        test_explicit_number_refuses_to_overwrite_without_force,
        test_act_writes_result_and_screenshot_under_one_number,
    ]:
        test()
        print("OK", test.__name__)