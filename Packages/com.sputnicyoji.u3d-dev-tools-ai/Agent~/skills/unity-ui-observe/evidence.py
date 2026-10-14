"""Numbered act/shot evidence for unity-ui-observe; never overwrites silently.

    python evidence.py act  --dir <evidence dir> --label <text> --project <root> [--pid N] -- <world args>
    python evidence.py shot --dir <evidence dir> --label <text> --project <root> [--pid N]

Each run takes the next free number in --dir (001, 002, ...) and writes
<NNN>-<label>.json (act result), <NNN>-<label>.png (Game view screenshot) and
<NNN>-<label>.frame.json (Time.frameCount just before the capture request and after the file appeared;
the captured frame lies in (frameBefore, frameAfter]).
--seq N picks the number; an existing target file then refuses (exit 3) unless --force.
Screenshots go through the EditorDebug service (unity-editor-debug-mcp client.py, a sibling skill).
"""
from __future__ import annotations

import argparse
import datetime as _dt
import json
import re
import subprocess
import sys
import time
from pathlib import Path
from typing import Any

HERE = Path(__file__).resolve().parent
DEFAULT_UI_CLIENT = HERE / "client.py"
DEFAULT_DEBUG_CLIENT = HERE.parent / "unity-editor-debug-mcp" / "client.py"
SUFFIXES = (".json", ".png", ".frame.json")
EXIT_EXISTS = 3
EXIT_NO_SHOT = 4


class EvidenceExists(Exception):
    pass


def slug(label: str) -> str:
    text = re.sub(r"[^A-Za-z0-9._-]+", "-", label).strip("-.")
    if not text:
        raise SystemExit("--label must contain at least one letter or digit")
    return text


def next_seq(directory: Path) -> int:
    highest = 0
    for path in directory.iterdir():
        match = re.match(r"^(\d{3,})-", path.name)
        if match:
            highest = max(highest, int(match.group(1)))
    return highest + 1


def reserve(directory: Path, label: str, seq: int | None, force: bool) -> Path:
    directory.mkdir(parents=True, exist_ok=True)
    number = seq if seq is not None else next_seq(directory)
    stem = directory / f"{number:03d}-{slug(label)}"
    existing = [stem.name + s for s in SUFFIXES if Path(str(stem) + s).exists()]
    if existing and not force:
        raise EvidenceExists(f"evidence exists: {', '.join(existing)} (pass --force to overwrite, or omit --seq)")
    return stem


def client_base(client: Path, args: argparse.Namespace) -> list[str]:
    base = [sys.executable, str(client), "--project", args.project]
    if args.pid:
        base += ["--pid", str(args.pid)]
    return base + ["--timeout", str(args.timeout)]


def invoke(args: argparse.Namespace, type_name: str, member: str, kind: str, *call_args: str) -> Any:
    cmd = client_base(Path(args.debug_client), args) + ["invoke", "--type", type_name, "--member", member, "--kind", kind]
    if call_args:
        cmd += ["--args", *call_args, "--arg-types", "System.String"]
    proc = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8")
    try:
        reply = json.loads(proc.stdout)
    except json.JSONDecodeError:
        return None
    data = reply.get("data") if isinstance(reply.get("data"), dict) else reply
    return data.get("result")


def frame_count(args: argparse.Namespace) -> int | None:
    value = invoke(args, "UnityEngine.Time, UnityEngine.CoreModule", "frameCount", "get")
    return value if isinstance(value, int) else None


def shoot(args: argparse.Namespace, stem: Path) -> dict[str, Any]:
    png = Path(str(stem) + ".png")
    if png.exists():
        png.unlink()  # reserve() already refused unless --force
    invoke(args, "UnityEditorInternal.InternalEditorUtility, UnityEditor", "RepaintAllViews", "call")
    time.sleep(min(args.settle, 1.0))
    before = frame_count(args)
    requested = _dt.datetime.now().astimezone().isoformat(timespec="milliseconds")
    invoke(args, "UnityEngine.ScreenCapture, UnityEngine.ScreenCaptureModule", "CaptureScreenshot", "call",
           png.resolve().as_posix())
    deadline = time.time() + args.shot_timeout
    last_size = -1
    while time.time() < deadline:
        if png.exists():
            size = png.stat().st_size
            if size > 0 and size == last_size:
                break
            last_size = size
        time.sleep(0.25)
    after = frame_count(args)
    sidecar = {
        "png": png.name if png.exists() else None,
        "frameBefore": before,
        "frameAfter": after,
        "requestedAt": requested,
        "note": "captured frame is in (frameBefore, frameAfter]",
    }
    Path(str(stem) + ".frame.json").write_text(json.dumps(sidecar, indent=2) + "\n", encoding="utf-8")
    return sidecar


def cmd_shot(args: argparse.Namespace, stem: Path) -> int:
    sidecar = shoot(args, stem)
    print(json.dumps({"png": sidecar["png"], "frame": sidecar, "dir": str(stem.parent)}, ensure_ascii=False))
    return 0 if sidecar["png"] else EXIT_NO_SHOT


def cmd_act(args: argparse.Namespace, stem: Path) -> int:
    world = list(args.world)
    if world and world[0] == "--":
        world = world[1:]
    proc = subprocess.run(client_base(Path(args.ui_client), args) + ["world", *world],
                          capture_output=True, text=True, encoding="utf-8")
    out = proc.stdout + (proc.stderr if proc.stderr.strip() else "")
    Path(str(stem) + ".json").write_text(out, encoding="utf-8")
    time.sleep(args.settle)
    sidecar = shoot(args, stem)
    sys.stdout.write(out)
    print(json.dumps({"evidence": stem.name, "png": sidecar["png"], "frame": sidecar}, ensure_ascii=False))
    if not sidecar["png"]:
        return EXIT_NO_SHOT
    return proc.returncode


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command", required=True)
    for name in ("act", "shot"):
        p = sub.add_parser(name)
        p.add_argument("--dir", required=True, help="evidence directory")
        p.add_argument("--label", required=True, help="short description; becomes part of the file name")
        p.add_argument("--seq", type=int, help="use this number instead of the next free one")
        p.add_argument("--force", action="store_true", help="overwrite existing evidence with the same number and label")
        p.add_argument("--project", required=True, help="Unity project root")
        p.add_argument("--pid", type=int, help="Unity Editor process id when several Editors are open")
        p.add_argument("--timeout", type=float, default=40.0)
        p.add_argument("--settle", type=float, default=2.0, help="seconds to wait after the act before the screenshot")
        p.add_argument("--shot-timeout", type=float, default=15.0)
        p.add_argument("--ui-client", default=str(DEFAULT_UI_CLIENT))
        p.add_argument("--debug-client", default=str(DEFAULT_DEBUG_CLIENT))
        if name == "act":
            p.add_argument("world", nargs=argparse.REMAINDER, help="-- then the arguments of `client.py world`")
    return parser


def main() -> int:
    args = build_parser().parse_args()
    try:
        stem = reserve(Path(args.dir), args.label, args.seq, args.force)
    except EvidenceExists as exc:
        print(str(exc), file=sys.stderr)
        return EXIT_EXISTS
    return cmd_act(args, stem) if args.command == "act" else cmd_shot(args, stem)


if __name__ == "__main__":
    sys.exit(main())