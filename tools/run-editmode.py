#!/usr/bin/env python3
"""Headless Unity batchmode EditMode test runner for the U3D-Dev-Tools-AI TestProjects.

Launches Unity in batchmode (-runTests -testPlatform EditMode), waits for it to exit, then parses
the NUnit3 result XML and reports pass/fail counts. If the result XML is missing it tells a
licensing failure apart from a compile failure by scanning the log.

  python tools/run-editmode.py --project test-runner
  python tools/run-editmode.py --project editor-core --editor-version 6000.3.13f1

--project       test-runner | editor-core | editor-debug | lua-device-debug | u3d-ai-linker
                (folder under TestProjects/), or a project path.
--unity         Unity executable. Otherwise UNITY_EXE, UNITY_EDITOR_PATH, then the Unity Hub folders
                (U3D_EDITOR_ROOTS, separated by os.pathsep, replaces the Hub folders).
--editor-version
                Run with this Editor version instead of the one in ProjectVersion.txt. ProjectVersion.txt is
                set to it for the run; every ProjectSettings file is restored afterwards (an Editor upgrade
                rewrites ProjectSettings.asset), so the worktree stays clean.

Notes:
- -runTests quits the Editor by itself; -quit is never passed with it.
- A stale Temp/UnityLockfile is removed first, only when no Unity process holds it. Library is never touched.
- Results go to <project>/TestResults/editmode-<stamp>.xml and .log.

Exit codes: 0 all green, 2 test failures, 3 setup error or no result XML (likely compile failure),
4 timeout, 5 Unity licensing failure.
"""
from __future__ import annotations

import argparse
import os
import re
import subprocess
import sys
import time
import xml.etree.ElementTree as ET
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]
HUB_ROOTS = {
    "win32": [r"C:\Program Files\Unity\Hub\Editor", r"C:\Program Files\Unity Hub\Editor"],
    "darwin": ["/Applications/Unity/Hub/Editor"],
}
# Unity.cmd is the test seam tools/test-run-editmode-script.py uses on Windows.
EDITOR_LAYOUTS = ["Editor/Unity.exe", "Editor/Unity.cmd", "Unity.app/Contents/MacOS/Unity"]
# Every healthy log mentions LicensingClient, so only a failed handshake counts.
LICENSE_FAILURE = re.compile(r"Licensing::.*(Timed-out|aborting)|LicensingClient.*aborting", re.IGNORECASE)
UPM_PATH_BUG = re.compile(r'The "path" argument must be of type string\. Received undefined', re.IGNORECASE)
COMPILE_ERROR = re.compile(r"error CS|Compilation failed|: error|is not a valid Unity version", re.IGNORECASE)


def say(text: str) -> None:
    print(text, flush=True)


def fail(text: str) -> None:
    print(text, file=sys.stderr, flush=True)
    sys.exit(3)


def resolve_unity(project: Path, requested: str, version: str) -> Path:
    if requested.strip():
        if Path(requested).exists():
            return Path(requested).resolve()
        fail(f"Unity not found: {requested}")

    for name in ("UNITY_EXE", "UNITY_EDITOR_PATH"):
        value = os.environ.get(name, "")
        if value.strip():
            if Path(value).exists():
                return Path(value).resolve()
            fail(f"{name} points to a missing Unity executable: {value}")

    project_version = version.strip()
    if not project_version:
        match = re.search(r"^m_EditorVersion:\s*(.+)$", (project / "ProjectSettings" / "ProjectVersion.txt").read_text(encoding="utf-8"), re.MULTILINE)
        project_version = match.group(1).strip() if match else ""

    roots = HUB_ROOTS.get(sys.platform, [])
    if os.environ.get("U3D_EDITOR_ROOTS", "").strip():
        roots = os.environ["U3D_EDITOR_ROOTS"].split(os.pathsep)

    if project_version:
        for root in roots:
            for layout in EDITOR_LAYOUTS:
                candidate = Path(root) / project_version / layout
                if candidate.exists():
                    return candidate.resolve()
        fail(f"Unity {project_version} not found. Pass --unity or set UNITY_EXE / UNITY_EDITOR_PATH to use a different editor explicitly.")

    for root in roots:
        if Path(root).is_dir():
            for editor in sorted((p for p in Path(root).iterdir() if p.is_dir()), key=lambda p: p.name, reverse=True):
                for layout in EDITOR_LAYOUTS:
                    if (editor / layout).exists():
                        return (editor / layout).resolve()
    fail("Unity not found. Pass --unity or set UNITY_EXE / UNITY_EDITOR_PATH.")
    raise AssertionError("unreachable")


def remove_stale_lockfile(lock: Path) -> bool:
    """Remove Temp/UnityLockfile unless a running Unity holds it. Returns False when it is held."""
    if not lock.exists():
        return True
    try:
        import fcntl
    except ImportError:
        fcntl = None  # Windows: a running Unity keeps the file open, so the unlink below fails instead
    if fcntl is not None:
        with open(lock, "rb") as handle:
            try:
                fcntl.flock(handle, fcntl.LOCK_EX | fcntl.LOCK_NB)  # Unity holds an flock while it runs
            except OSError:
                return False
    say(f"[run-editmode] removing stale lockfile: {lock}")
    try:
        lock.unlink()
    except OSError:
        return False
    return True


def run_unity(unity: Path, project: Path, results_xml: Path, log_file: Path, timeout_sec: int) -> int:
    args = [str(unity), "-batchmode", "-nographics", "-runTests",
            "-projectPath", str(project),
            "-testPlatform", "EditMode",
            "-testResults", str(results_xml),
            "-logFile", str(log_file)]
    proc = subprocess.Popen(args)
    try:
        exit_code = proc.wait(timeout=timeout_sec)
    except subprocess.TimeoutExpired:
        say(f"[run-editmode] TIMEOUT after {timeout_sec}s; killing pid {proc.pid}")
        proc.kill()
        proc.wait()
        return 4
    say(f"[run-editmode] Unity exited with code {exit_code}")

    if not results_xml.exists():
        log_lines = log_file.read_text(encoding="utf-8", errors="replace").splitlines() if log_file.exists() else []
        license_line = next((line for line in log_lines if LICENSE_FAILURE.search(line)), None)
        if exit_code == 199 or license_line:
            say(f"[run-editmode] LICENSE FAILURE: the Unity licensing client was not available (exit {exit_code}).")
            if license_line:
                say(f"  {license_line}")
            say("[run-editmode] Not a compile failure. Wait for the Unity Hub / licensing client, then rerun.")
            return 5
        say("[run-editmode] NO result XML -> likely compile failure. Compile errors in log:")
        upm_line = next((line for line in log_lines if UPM_PATH_BUG.search(line)), None)
        if upm_line:
            say("[run-editmode] Package Manager failed before script compilation:")
            say(f"  {upm_line}")
            say("[run-editmode] This is a Unity/UPM startup failure, not a C# compile failure.")
        for line in [line for line in log_lines if COMPILE_ERROR.search(line)][:40]:
            say(f"  {line}")
        return 3

    run = ET.parse(results_xml).getroot()
    result = run.get("result", "")
    failed = int(run.get("failed", "0"))
    say("")
    say(f"[run-editmode] ===== RESULT: {result} =====")
    say(f"[run-editmode] total={run.get('total')} passed={run.get('passed')} failed={failed} skipped={run.get('skipped')}")
    if failed > 0 or result != "Passed":
        say("[run-editmode] failing tests:")
        for case in run.iter("test-case"):
            if case.get("result") == "Failed":
                say(f"  FAIL: {case.get('fullname')}")
                message = case.find("failure/message")
                if message is not None and message.text:
                    say(f"        {message.text.strip()}")
        return 2
    say("[run-editmode] all green")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description="Run Unity EditMode tests in batchmode for a TestProject.")
    parser.add_argument("--project", required=True)
    parser.add_argument("--unity", default="")
    parser.add_argument("--editor-version", default="")
    parser.add_argument("--timeout-sec", type=int, default=600)
    args = parser.parse_args()

    project = Path(args.project).resolve() if Path(args.project).exists() else REPO_ROOT / "TestProjects" / args.project
    settings_dir = project / "ProjectSettings"
    if not (settings_dir / "ProjectVersion.txt").exists():
        fail(f"Not a Unity project: {project}")
    (project / "Assets").mkdir(exist_ok=True)
    unity = resolve_unity(project, args.unity, args.editor_version)

    results_dir = project / "TestResults"
    results_dir.mkdir(exist_ok=True)
    stamp = time.strftime("%Y%m%d-%H%M%S")
    results_xml = results_dir / f"editmode-{stamp}.xml"
    log_file = results_dir / f"editmode-{stamp}.log"

    if not remove_stale_lockfile(project / "Temp" / "UnityLockfile"):
        fail(f"[run-editmode] a running Unity holds {project / 'Temp' / 'UnityLockfile'}; close that Editor first.")

    # Opening the project in another Editor version rewrites ProjectVersion.txt and ProjectSettings.asset.
    # Restored in finally, whatever the outcome.
    snapshot = {path: path.read_bytes() for path in settings_dir.iterdir() if path.is_file()}
    if args.editor_version:
        (settings_dir / "ProjectVersion.txt").write_bytes(f"m_EditorVersion: {args.editor_version}\n".encode("utf-8"))

    say(f"[run-editmode] project : {project}")
    say(f"[run-editmode] unity   : {unity}")
    if args.editor_version:
        say(f"[run-editmode] version : {args.editor_version} (ProjectSettings restored after the run)")
    say(f"[run-editmode] results : {results_xml}")
    say(f"[run-editmode] log     : {log_file}")
    say(f"[run-editmode] launching Unity batchmode (timeout {args.timeout_sec}s)...")

    try:
        return run_unity(unity, project, results_xml, log_file, args.timeout_sec)
    finally:
        restored = 0
        for path, original in snapshot.items():
            if not path.exists() or path.read_bytes() != original:
                path.write_bytes(original)
                restored += 1
        if restored:
            say(f"[run-editmode] restored {restored} ProjectSettings file(s)")


if __name__ == "__main__":
    sys.exit(main())
