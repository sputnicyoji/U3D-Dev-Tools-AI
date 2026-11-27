#!/usr/bin/env python3
"""Copy the bundled agent skills to ~/.claude/skills/<skill> so Claude Code loads them.

Usage:
    python install_skills.py                      # every skill folder that has a SKILL.md
    python install_skills.py unity-ui-observe     # only the named skills
    python install_skills.py --dest <dir>         # install into <dir>/<skill> instead

Each destination skill folder is deleted and recreated. Copied: SKILL.md, the top-level *.py
files and references/*.md (test-runner-mcp copies every file in references/).
Run it again after editing a skill. The U3D AI Linker settings page installs skills per project.
"""
from __future__ import annotations

import argparse
import shutil
import sys
from pathlib import Path

SKILLS_ROOT = Path(__file__).resolve().parent / "skills"
# The old test-runner-mcp install.ps1 copied every reference file; the other skills copied *.md only.
COPY_ALL_REFERENCES = {"test-runner-mcp"}


def available_skills() -> list[str]:
    return sorted(p.name for p in SKILLS_ROOT.iterdir() if (p / "SKILL.md").is_file())


def install(skill: str, dest_root: Path) -> None:
    src = SKILLS_ROOT / skill
    dst = dest_root / skill
    print("src: " + str(src))
    print("dst: " + str(dst))
    if dst.is_symlink() or dst.exists():
        print("  removing existing destination...")
    if dst.is_symlink() or dst.is_file():
        dst.unlink()  # a link: remove the link only, never what it points to
    elif dst.exists():
        shutil.rmtree(dst)  # Python 3.8+ refuses a Windows junction instead of emptying its target

    (dst / "references").mkdir(parents=True)
    for path in sorted(src.iterdir()):
        if path.is_file() and (path.name == "SKILL.md" or path.suffix == ".py"):
            shutil.copy2(path, dst)
    refs = src / "references"
    if refs.is_dir():
        for path in sorted(refs.iterdir()):
            if path.is_file() and (skill in COPY_ALL_REFERENCES or path.suffix == ".md"):
                shutil.copy2(path, dst / "references")

    print("done. installed files:")
    for path in sorted(p for p in dst.rglob("*") if p.is_file()):
        print("  " + str(path))


def main(argv: list[str] | None = None) -> int:
    known = available_skills()
    parser = argparse.ArgumentParser(description="Install bundled agent skills for Claude Code.")
    parser.add_argument("skills", nargs="*", metavar="skill", help="skill names (default: all of " + ", ".join(known) + ")")
    parser.add_argument("--dest", help="destination skills folder (default: ~/.claude/skills)")
    args = parser.parse_args(argv)

    unknown = [name for name in args.skills if name not in known]
    if unknown:
        parser.error("unknown skill(s): " + ", ".join(unknown))
    dest_root = Path(args.dest).expanduser() if args.dest else Path.home() / ".claude" / "skills"
    for skill in args.skills or known:
        install(skill, dest_root)
    return 0


if __name__ == "__main__":
    sys.exit(main())
