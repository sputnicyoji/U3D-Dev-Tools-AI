#!/usr/bin/env python3
"""
语义 UI 观测 CLI：读 EditorDebugMCP 的 /ui/snapshot 与 /ui/hit。

与 unity-editor-debug-mcp 是同一个 Editor 服务，端口解析同一套（service id unity-editor-debug-mcp）。
响应原样以 JSON 打到 stdout；失败走非零退出码。
"""

from __future__ import annotations

import argparse
import json
import sys
import urllib.error
import urllib.parse
import urllib.request
from typing import Any

from port_resolver import EndpointResolutionError, resolve_endpoint

DEFAULT_HOST = "127.0.0.1"
DEFAULT_PORT = 21891
LEGACY_PORTS = (21891, 21892, 21893)
SERVICE_ID = "unity-editor-debug-mcp"


def get(args: argparse.Namespace, path: str, query: dict[str, Any]) -> dict[str, Any]:
    params = {k: v for k, v in query.items() if v is not None}
    suffix = "?" + urllib.parse.urlencode(params) if params else ""
    return request(args, path + suffix, None)


def post(args: argparse.Namespace, path: str, body: dict[str, Any]) -> dict[str, Any]:
    """/ui/act 只收 POST + application/json（服务端拒绝 GET 与 form 编码）。"""
    return request(args, path, json.dumps({k: v for k, v in body.items() if v is not None}).encode("utf-8"))


def request(args: argparse.Namespace, path: str, data: bytes | None) -> dict[str, Any]:
    host, port, source = resolve_endpoint(
        SERVICE_ID, args.host, args.port, DEFAULT_PORT, args.project, args.pid, args.timeout, LEGACY_PORTS
    )
    req = urllib.request.Request(f"http://{host}:{port}{path}", data=data,
                                 headers={"Content-Type": "application/json"} if data is not None else {})
    try:
        with urllib.request.urlopen(req, timeout=args.timeout) as resp:
            return json.loads(resp.read().decode("utf-8"))
    except (urllib.error.URLError, TimeoutError, OSError) as e:
        reason = getattr(e, "reason", e)
        return {"ok": False, "error": {"type": e.__class__.__name__, "message": str(reason),
                                       "context": {"port": port, "source": source}}}


def cmd_snapshot(args: argparse.Namespace) -> dict[str, Any]:
    return get(args, "/ui/snapshot", {
        "include": "all" if args.all else None,
        "max": args.max,
        "offset": args.offset,
        "step": args.step,
    })


def cmd_guide(args: argparse.Namespace) -> dict[str, Any]:
    """只回答"有没有被引导抢占"：guide 块 + 顶层 layer，省掉 nodes。"""
    body = get(args, "/ui/snapshot", {"max": 1, "step": args.step})
    result = body.get("result") if body.get("ok") else None
    if not isinstance(result, dict):
        return body
    top = next((layer for layer in result.get("layers") or [] if layer.get("topmost")), None)
    body["result"] = {"meta": result.get("meta"), "topLayer": top, "guide": result.get("guide")}
    return body


def cmd_hit(args: argparse.Namespace) -> dict[str, Any]:
    return get(args, "/ui/hit", {"x": args.x, "y": args.y})


def point(xy: list[float] | None) -> dict[str, float] | None:
    return {"x": xy[0], "y": xy[1]} if xy else None


def cmd_act(args: argparse.Namespace) -> dict[str, Any]:
    action = "drag" if args.to else "click"
    return post(args, "/ui/act", {"id": args.id, "iid": args.iid, "action": action, "at": point(args.at),
                                  "to": point(args.to), "steps": args.steps})


def cmd_world(args: argparse.Namespace) -> dict[str, Any]:
    """世界目标（Phase 3）：按场景路径 / iid 点世界物体，或在屏幕点上拖动、滚轮、双指缩放。"""
    if args.cell:
        target = {"cell": {"x": args.cell[0], "y": args.cell[1]}}
    else:
        target = {"path": args.path, "iid": args.iid} if (args.path or args.iid is not None) else None
    return post(args, "/ui/act", {"target": target, "at": point(args.at), "action": args.action,
                                  "to": point(args.to), "by": point(args.by), "steps": args.steps,
                                  "scroll": args.scroll, "scale": args.scale, "span": args.span,
                                  "camera": args.camera})


def build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(prog="client.py", description="Semantic uGUI observation (read-only)")
    p.add_argument("--host", default=DEFAULT_HOST)
    p.add_argument("--port", type=int, default=None, help="force a port; default is project-aware resolution")
    p.add_argument("--project", help="Unity project root. Defaults to walking up from cwd.")
    p.add_argument("--pid", type=int, help="Unity Editor process id when several Editors are open.")
    p.add_argument("--timeout", type=float, default=30.0)
    sub = p.add_subparsers(dest="cmd", required=True)

    sp = sub.add_parser("snapshot", help="layers stack + interactive nodes (hitTop/coveredBy) + guide")
    sp.add_argument("--all", action="store_true", help="every Graphic, not only interactive controls")
    sp.add_argument("--max", type=int, help="page size (server default 500)")
    sp.add_argument("--offset", type=int, help="page start")
    sp.add_argument("--step", type=int, help="guide hole probe grid in pixels (default 24)")
    sp.set_defaults(func=cmd_snapshot)

    sg = sub.add_parser("guide", help="is a guide mask stealing input? guide block + top layer only")
    sg.add_argument("--step", type=int)
    sg.set_defaults(func=cmd_guide)

    sh = sub.add_parser("hit", help="who receives a click at screen pixel (top-left origin)")
    sh.add_argument("--x", type=float, required=True)
    sh.add_argument("--y", type=float, required=True)
    sh.set_defaults(func=cmd_hit)

    sa = sub.add_parser("act", help="click (or drag with --to) a control by snapshot id; refused if a real click could not reach it")
    sa.add_argument("--id", required=True, help="node id from snapshot")
    sa.add_argument("--iid", type=int, help="node iid from the same snapshot, to pick one of several objects sharing an id")
    sa.add_argument("--at", type=float, nargs=2, metavar=("X", "Y"), help="press point inside the control (default: its center)")
    sa.add_argument("--to", type=float, nargs=2, metavar=("X", "Y"), help="drag to this screen point instead of clicking")
    sa.add_argument("--steps", type=int, help="drag move events (default 8)")
    sa.set_defaults(func=cmd_act)

    sw = sub.add_parser("world", help="drive the game's scripted pointer at a world object (path/iid) or screen point; "
                                      "click checks the game's actual hit against the target")
    sw.add_argument("--path", help="scene path of the world object (hierarchy path, [i] for same-name siblings)")
    sw.add_argument("--iid", type=int, help="instance id of the world object")
    sw.add_argument("--cell", type=int, nargs=2, metavar=("X", "Y"),
                    help="map cell to click (the game resolves cells; click only, verified by the cell the game reports)")
    sw.add_argument("--at", type=float, nargs=2, metavar=("X", "Y"), help="screen point instead of a target (drag/scroll/pinch only)")
    sw.add_argument("--action", choices=["click", "drag", "scroll", "pinch"], default="click")
    sw.add_argument("--to", type=float, nargs=2, metavar=("X", "Y"), help="drag end point")
    sw.add_argument("--by", type=float, nargs=2, metavar=("DX", "DY"), help="drag offset in pixels (top-left origin)")
    sw.add_argument("--scroll", type=float, help="wheel notches, positive = away from the user (zoom in)")
    sw.add_argument("--scale", type=float, help="pinch finger span end/start; > 1 spreads the fingers")
    sw.add_argument("--span", type=float, help="pinch start span in pixels (default 200)")
    sw.add_argument("--steps", type=int, help="frames for the move/scroll/pinch")
    sw.add_argument("--camera", help="scene path of the camera to project with (default: Camera.main, then by depth)")
    sw.set_defaults(func=cmd_world)
    return p


def main() -> int:
    args = build_parser().parse_args()
    try:
        result = args.func(args)
    except EndpointResolutionError as exc:
        result = {"ok": False, "error": {"type": exc.code, "message": str(exc), "context": exc.context}}
    # snapshot 在大界面上可达百 KB，紧凑输出省 token；其余命令保留缩进便于阅读
    if args.cmd == "snapshot":
        json.dump(result, sys.stdout, ensure_ascii=False, separators=(",", ":"))
    else:
        json.dump(result, sys.stdout, ensure_ascii=False, indent=2)
    sys.stdout.write("\n")
    if not result.get("ok"):
        return 1
    # act 被拒（被遮、离屏等）是语义失败：ok=true 但 acted=false，退出码 2 与传输/参数错误区分
    if args.cmd == "act" and not (result.get("result") or {}).get("acted"):
        return 2
    # world：被拒或点中的不是目标（hit_mismatch / unverified）都带 reason
    if args.cmd == "world" and (result.get("result") or {}).get("reason"):
        return 2
    return 0


if __name__ == "__main__":
    sys.exit(main())
