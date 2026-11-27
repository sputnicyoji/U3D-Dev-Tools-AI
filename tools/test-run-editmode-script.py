"""run-editmode.py behaviour against a fake Unity (no real Editor needed).

- --editor-version picks the Editor by version and writes it into ProjectVersion.txt for the run;
  ProjectSettings files are restored afterwards, also when the run fails.
- Exit code 199 (licensing client unavailable) is reported as a license failure, not a compile failure.
- A missing result XML with a healthy licensing handshake in the log is a compile failure.
- Temp/UnityLockfile is removed only when no Unity holds it.
"""
from __future__ import annotations

import os
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SCRIPT = ROOT / "tools" / "run-editmode.py"
VERSION = "6000.3.12f1"

FAKE_UNITY = r'''
import sys, pathlib
args = sys.argv[1:]
def arg(name):
    return args[args.index(name) + 1]
here = pathlib.Path(__file__).parent
mode = (here / "mode.txt").read_text().strip()
project = pathlib.Path(arg("-projectPath"))
(here / "seen-version.txt").write_text((project / "ProjectSettings" / "ProjectVersion.txt").read_text())
(here / "seen-args.txt").write_text("\n".join(args))
log = pathlib.Path(arg("-logFile"))
if mode == "license":
    log.write_text("[Licensing::Module] Timed-out after 60.01s, waiting for channel\n"
                   "IPC channel to LicensingClient doesn't exist; aborting\n")
    sys.exit(199)
if mode == "compile":
    log.write_text("[Licensing::Client] Handshaking with LicensingClient:\n"
                   "[Licensing::Module] LicensingClient has failed validation; ignoring\n"
                   "Assets/Broken.cs(3,5): error CS0103: The name 'x' does not exist in the current context\n")
    sys.exit(1)
# simulate an Editor upgrade rewriting ProjectSettings, then a passing run
(project / "ProjectSettings" / "ProjectVersion.txt").write_text("m_EditorVersion: 9999.0.0f1\n")
(project / "ProjectSettings" / "ProjectSettings.asset").write_text("upgraded\n")
log.write_text("ok\n")
pathlib.Path(arg("-testResults")).write_text(
    '<?xml version="1.0"?><test-run total="1" passed="1" failed="0" skipped="0" result="Passed"></test-run>')
sys.exit(0)
'''


def editor_dir(tmp: Path) -> Path:
    if os.name == "nt":
        return tmp / "Editors" / VERSION / "Editor"
    return tmp / "Editors" / VERSION / "Unity.app" / "Contents" / "MacOS"


def make_sandbox() -> tuple[Path, Path]:
    tmp = Path(tempfile.mkdtemp(prefix="run-editmode-test-"))
    project = tmp / "proj"
    (project / "ProjectSettings").mkdir(parents=True)
    (project / "ProjectSettings" / "ProjectVersion.txt").write_text("m_EditorVersion: 2022.3.62f2c1\n")
    (project / "ProjectSettings" / "ProjectSettings.asset").write_text("original\n")
    editor = editor_dir(tmp)
    editor.mkdir(parents=True)
    (editor / "fake_unity.py").write_text(FAKE_UNITY)
    if os.name == "nt":
        (editor / "Unity.cmd").write_text(f'@"{sys.executable}" "%~dp0fake_unity.py" %*\r\n@exit /b %ERRORLEVEL%\r\n')
    else:
        unity = editor / "Unity"
        unity.write_text(f'#!/bin/sh\nexec "{sys.executable}" "$(dirname "$0")/fake_unity.py" "$@"\n')
        unity.chmod(0o755)
    return tmp, project


def run(tmp: Path, project: Path, mode: str) -> subprocess.CompletedProcess:
    (editor_dir(tmp) / "mode.txt").write_text(mode)
    env = dict(os.environ, U3D_EDITOR_ROOTS=str(tmp / "Editors"))
    env.pop("UNITY_EXE", None)
    env.pop("UNITY_EDITOR_PATH", None)
    return subprocess.run(
        [sys.executable, "-B", str(SCRIPT), "--project", str(project),
         "--editor-version", VERSION, "--timeout-sec", "60"],
        capture_output=True, text=True, env=env)


def test_editor_version_is_used_and_project_settings_are_restored() -> None:
    tmp, project = make_sandbox()
    try:
        result = run(tmp, project, "pass")
        out = result.stdout + result.stderr
        assert result.returncode == 0, out
        assert "total=1 passed=1 failed=0 skipped=0" in out, out
        seen = (editor_dir(tmp) / "seen-version.txt").read_text()
        assert f"m_EditorVersion: {VERSION}" in seen, seen
        seen_args = (editor_dir(tmp) / "seen-args.txt").read_text().splitlines()
        assert "-runTests" in seen_args and "-quit" not in seen_args, seen_args
        assert (project / "Assets").is_dir()
        assert (project / "ProjectSettings" / "ProjectVersion.txt").read_text() == "m_EditorVersion: 2022.3.62f2c1\n"
        assert (project / "ProjectSettings" / "ProjectSettings.asset").read_text() == "original\n"
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


def test_exit_199_is_reported_as_license_failure_and_settings_restored() -> None:
    tmp, project = make_sandbox()
    try:
        result = run(tmp, project, "license")
        out = result.stdout + result.stderr
        assert result.returncode == 5, out
        assert "LICENSE" in out.upper(), out
        assert "likely compile failure" not in out.lower(), out
        assert (project / "ProjectSettings" / "ProjectVersion.txt").read_text() == "m_EditorVersion: 2022.3.62f2c1\n"
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


def test_compile_failure_is_not_mistaken_for_a_license_failure() -> None:
    tmp, project = make_sandbox()
    try:
        result = run(tmp, project, "compile")
        out = result.stdout + result.stderr
        assert result.returncode == 3, out
        assert "likely compile failure" in out.lower(), out
        assert "error CS0103" in out, out
        assert "LICENSE FAILURE" not in out, out
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


def test_stale_lockfile_is_removed_but_a_held_one_stops_the_run() -> None:
    tmp, project = make_sandbox()
    try:
        lock = project / "Temp" / "UnityLockfile"
        lock.parent.mkdir()
        lock.write_text("")
        result = run(tmp, project, "pass")
        assert result.returncode == 0, result.stdout + result.stderr
        assert not lock.exists()
        if os.name != "nt":
            import fcntl
            lock.write_text("")
            (editor_dir(tmp) / "seen-args.txt").unlink()
            with open(lock, "rb") as handle:
                fcntl.flock(handle, fcntl.LOCK_EX | fcntl.LOCK_NB)  # what a running Unity does
                result = run(tmp, project, "pass")
            out = result.stdout + result.stderr
            assert result.returncode == 3, out
            assert "holds" in out, out
            assert lock.exists()
            assert not (editor_dir(tmp) / "seen-args.txt").exists(), "Unity was launched"
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


if __name__ == "__main__":
    for test in [
        test_editor_version_is_used_and_project_settings_are_restored,
        test_exit_199_is_reported_as_license_failure_and_settings_restored,
        test_compile_failure_is_not_mistaken_for_a_license_failure,
        test_stale_lockfile_is_removed_but_a_held_one_stops_the_run,
    ]:
        test()
        print("OK", test.__name__)
