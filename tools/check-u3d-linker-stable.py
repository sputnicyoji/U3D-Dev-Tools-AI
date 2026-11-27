#!/usr/bin/env python3
"""Check the stable registry before a release.

  python tools/check-u3d-linker-stable.py [--repo-root <repo-root>] [--require-remote-tags]

Registry/stable.json must match the package snapshot, hold exactly one ready entry for this package,
and its revision must be u3d-dev-tools-ai-v<package.json version> with that local tag present
(and on origin with --require-remote-tags).
"""
from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
from pathlib import Path

PACKAGE_NAME = "com.sputnicyoji.u3d-dev-tools-ai"
PACKAGE_PATH = "Packages/com.sputnicyoji.u3d-dev-tools-ai"
ENTRY_ID = "u3d-dev-tools-ai"


def read_text(path: Path) -> str:
    return path.read_text(encoding="utf-8-sig")


def git(root: Path, *args: str) -> str:
    # GIT_TERMINAL_PROMPT=0: fail instead of waiting on a hidden credential prompt for ls-remote.
    result = subprocess.run(["git", "-C", str(root), *args], capture_output=True, text=True,
                            env=dict(os.environ, GIT_TERMINAL_PROMPT="0"))
    if result.returncode != 0:
        raise SystemExit(f"git {' '.join(args)} failed: {result.stderr.strip()}")
    return result.stdout


def check(root: Path, require_remote_tags: bool) -> None:
    stable_path = root / "Registry" / "stable.json"
    snapshot_path = root / PACKAGE_PATH / "Registry" / "stable.json"
    stable_text = read_text(stable_path)
    stable = json.loads(stable_text)
    json.loads(read_text(snapshot_path))

    if stable_text.replace("\r\n", "\n").rstrip("\n") != read_text(snapshot_path).replace("\r\n", "\n").rstrip("\n"):
        raise SystemExit("stable registry snapshot differs from root Registry/stable.json")

    entries = stable.get("entries") or []
    if len(entries) != 1:
        raise SystemExit("stable registry must contain exactly one package entry")
    entry = entries[0]
    if entry.get("id") != ENTRY_ID:
        raise SystemExit(f"unexpected stable entry id: {entry.get('id')}")
    if entry.get("status") != "ready":
        raise SystemExit("stable registry entry must be ready")
    if entry.get("packageName") != PACKAGE_NAME:
        raise SystemExit(f"unexpected packageName: {entry.get('packageName')}")
    if entry.get("packagePath") != PACKAGE_PATH:
        raise SystemExit(f"unexpected packagePath: {entry.get('packagePath')}")

    package_json = root / PACKAGE_PATH / "package.json"
    if not package_json.is_file():
        raise SystemExit(f"missing package.json: {package_json}")
    expected_revision = f"{ENTRY_ID}-v{json.loads(read_text(package_json)).get('version')}"
    revision = entry.get("revision")
    if revision != expected_revision:
        raise SystemExit(f"revision mismatch: registry={revision} expected={expected_revision}")

    if not git(root, "tag", "--list", revision).strip():
        raise SystemExit(f"missing local tag {revision}")
    if require_remote_tags and not git(root, "ls-remote", "--tags", "origin", revision).strip():
        raise SystemExit(f"missing remote origin tag {revision}")


def main() -> int:
    parser = argparse.ArgumentParser(description="Check the stable registry before a release.")
    parser.add_argument("--repo-root", default=".", help="repository root (default: current directory)")
    parser.add_argument("--require-remote-tags", action="store_true")
    args = parser.parse_args()
    check(Path(args.repo_root).resolve(), args.require_remote_tags)
    print("stable release checks passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
