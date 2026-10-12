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
    host, port, source = resolve_endpoint(
        SERVICE_ID, args.host, args.port, DEFAULT_PORT, args.project, args.pid, args.timeout, LEGACY_PORTS
    )
    params = {k: v for k, v in query.items() if v is not None}
    url = f"http://{host}:{port}{path}" + ("?" + urllib.parse.urlencode(params) if params else "")
    try:
        with urllib.request.urlopen(url, timeout=args.timeout) as resp:
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
    return 0 if result.get("ok") else 1


if __name__ == "__main__":
    sys.exit(main())
