"""Agent~/install_skills.py installs the same file set the old per-skill install.ps1 scripts did."""
from __future__ import annotations

import os
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SCRIPT = ROOT / "Packages" / "com.sputnicyoji.u3d-dev-tools-ai" / "Agent~" / "install_skills.py"
EXPECTED = {
    "test-runner-mcp": {
        "SKILL.md", "client.py", "port_resolver.py",
        "references/protocol.md", "references/run-e2e.py", "references/troubleshooting.md",
    },
    "unity-editor-debug-mcp": {
        "SKILL.md", "client.py", "port_resolver.py",
        "references/api-cookbook.md", "references/manual-verification.md",
        "references/protocol.md", "references/troubleshooting.md",
    },
    "unity-lua-device-debug": {
        "SKILL.md", "client.py", "port_resolver.py",
        "references/android-adb-forwarding.md", "references/protocol.md",
    },
    "unity-ui-observe": {
        "SKILL.md", "client.py", "evidence.py", "port_resolver.py",
        "references/protocol.md", "references/troubleshooting.md",
    },
}


def install(dest: Path, *skills: str) -> subprocess.CompletedProcess:
    return subprocess.run([sys.executable, "-B", str(SCRIPT), "--dest", str(dest), *skills],
                          capture_output=True, text=True)


def installed(path: Path) -> set[str]:
    return {p.relative_to(path).as_posix() for p in path.rglob("*") if p.is_file()}


def test_installs_every_skill_with_the_old_file_set() -> None:
    dest = Path(tempfile.mkdtemp(prefix="install-skills-test-"))
    try:
        result = install(dest)
        assert result.returncode == 0, result.stdout + result.stderr
        assert {p.name for p in dest.iterdir()} == set(EXPECTED)
        for skill, files in EXPECTED.items():
            assert installed(dest / skill) == files, (skill, sorted(installed(dest / skill)))
            assert (dest / skill / "SKILL.md").read_bytes() == (SCRIPT.parent / "skills" / skill / "SKILL.md").read_bytes()
    finally:
        shutil.rmtree(dest, ignore_errors=True)


def test_reinstall_replaces_the_folder_and_only_unlinks_a_symlinked_destination() -> None:
    tmp = Path(tempfile.mkdtemp(prefix="install-skills-test-"))
    try:
        dest = tmp / "skills"
        stale = dest / "unity-ui-observe" / "act.ps1"
        stale.parent.mkdir(parents=True)
        stale.write_text("old")
        kept = tmp / "elsewhere" / "keep.txt"
        kept.parent.mkdir()
        kept.write_text("user data")
        if os.name != "nt":
            os.symlink(str(kept.parent), str(dest / "test-runner-mcp"))
        result = install(dest, "unity-ui-observe", "test-runner-mcp")
        assert result.returncode == 0, result.stdout + result.stderr
        assert "removing existing destination" in result.stdout
        assert installed(dest / "unity-ui-observe") == EXPECTED["unity-ui-observe"]
        assert not (dest / "test-runner-mcp").is_symlink()
        assert installed(dest / "test-runner-mcp") == EXPECTED["test-runner-mcp"]
        assert kept.read_text() == "user data"
        assert install(dest, "no-such-skill").returncode == 2
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


if __name__ == "__main__":
    for test in [
        test_installs_every_skill_with_the_old_file_set,
        test_reinstall_replaces_the_folder_and_only_unlinks_a_symlinked_destination,
    ]:
        test()
        print("OK", test.__name__)
