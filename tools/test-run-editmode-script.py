"""run-editmode.ps1 behaviour against a fake Unity (no real Editor needed).

- -EditorVersion picks the Editor by version and writes it into ProjectVersion.txt for the run;
  ProjectSettings files are restored afterwards, also when the run fails.
- Exit code 199 (licensing client unavailable) is reported as a license failure, not a compile failure.
"""
from __future__ import annotations

import os
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SCRIPT = ROOT / "tools" / "run-editmode.ps1"

FAKE_UNITY = r'''
import sys, pathlib
args = sys.argv[1:]
def arg(name):
    return args[args.index(name) + 1]
mode = pathlib.Path(__file__).with_name("mode.txt").read_text().strip()
project = pathlib.Path(arg("-projectPath"))
pathlib.Path(__file__).with_name("seen-version.txt").write_text(
    (project / "ProjectSettings" / "ProjectVersion.txt").read_text())
log = pathlib.Path(arg("-logFile"))
if mode == "license":
    log.write_text("[Licensing::Module] Timed-out after 60.01s, waiting for channel\n"
                   "IPC channel to LicensingClient doesn't exist; aborting\n")
    sys.exit(199)
# simulate an Editor upgrade rewriting ProjectSettings, then a passing run
(project / "ProjectSettings" / "ProjectVersion.txt").write_text("m_EditorVersion: 9999.0.0f1\n")
(project / "ProjectSettings" / "ProjectSettings.asset").write_text("upgraded\n")
log.write_text("ok\n")
pathlib.Path(arg("-testResults")).write_text(
    '<?xml version="1.0"?><test-run total="1" passed="1" failed="0" skipped="0" result="Passed"></test-run>')
sys.exit(0)
'''


def pwsh() -> str:
    return shutil.which("pwsh") or shutil.which("powershell") or "pwsh"


def make_sandbox() -> tuple[Path, Path]:
    tmp = Path(tempfile.mkdtemp(prefix="run-editmode-test-"))
    project = tmp / "proj"
    (project / "ProjectSettings").mkdir(parents=True)
    (project / "ProjectSettings" / "ProjectVersion.txt").write_text("m_EditorVersion: 2022.3.62f2c1\n")
    (project / "ProjectSettings" / "ProjectSettings.asset").write_text("original\n")
    editor = tmp / "Editors" / "6000.3.12f1" / "Editor"
    editor.mkdir(parents=True)
    (editor / "fake_unity.py").write_text(FAKE_UNITY)
    (editor / "Unity.cmd").write_text(f'@"{sys.executable}" "%~dp0fake_unity.py" %*\r\n@exit /b %ERRORLEVEL%\r\n')
    return tmp, project


def run(tmp: Path, project: Path, mode: str) -> subprocess.CompletedProcess:
    editor = tmp / "Editors" / "6000.3.12f1" / "Editor"
    (editor / "mode.txt").write_text(mode)
    env = dict(os.environ, U3D_EDITOR_ROOTS=str(tmp / "Editors"))
    env.pop("UNITY_EXE", None)
    env.pop("UNITY_EDITOR_PATH", None)
    return subprocess.run(
        [pwsh(), "-NoProfile", "-File", str(SCRIPT), "-Project", str(project),
         "-EditorVersion", "6000.3.12f1", "-TimeoutSec", "60"],
        capture_output=True, text=True, env=env)


def test_editor_version_is_used_and_project_settings_are_restored() -> None:
    tmp, project = make_sandbox()
    try:
        result = run(tmp, project, "pass")
        out = result.stdout + result.stderr
        assert result.returncode == 0, out
        seen = (tmp / "Editors" / "6000.3.12f1" / "Editor" / "seen-version.txt").read_text()
        assert "m_EditorVersion: 6000.3.12f1" in seen, seen
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


if __name__ == "__main__":
    for test in [
        test_editor_version_is_used_and_project_settings_are_restored,
        test_exit_199_is_reported_as_license_failure_and_settings_restored,
    ]:
        test()
        print("OK", test.__name__)