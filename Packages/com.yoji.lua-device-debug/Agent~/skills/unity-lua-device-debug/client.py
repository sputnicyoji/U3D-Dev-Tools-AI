#!/usr/bin/env python3
"""Unity Lua Device Debug CLI."""
from __future__ import annotations

import argparse
import json
import shutil
import subprocess
import sys
import tempfile
import urllib.error
import urllib.request
import uuid
from pathlib import Path
from typing import Any

from port_resolver import EndpointResolutionError, resolve_endpoint


DEFAULT_HOST = "127.0.0.1"
DEFAULT_PORT = 21894
LEGACY_PORTS = (21894,)
SERVICE_ID = "unity-lua-device-debug"


def emit(payload: dict[str, Any]) -> None:
    json.dump(payload, sys.stdout, ensure_ascii=False, indent=2)
    sys.stdout.write("\n")


def error(code: str, message: str, **context: Any) -> tuple[int, dict[str, Any]]:
    body: dict[str, Any] = {"ok": False, "error": {"code": code, "message": message}}
    if context:
        body["error"]["context"] = context
    return 1, body


def endpoint(args: argparse.Namespace) -> tuple[str, int, str]:
    return resolve_endpoint(
        SERVICE_ID,
        args.host,
        args.port,
        DEFAULT_PORT,
        getattr(args, "project", None),
        getattr(args, "pid", None),
        getattr(args, "timeout", None),
        LEGACY_PORTS,
    )


def request_base(args: argparse.Namespace) -> tuple[str, int, str]:
    host, port, source = endpoint(args)
    return f"http://{host}:{port}", port, source


def endpoint_context(port: int | None, source: str | None) -> dict[str, Any]:
    context: dict[str, Any] = {}
    if port is not None:
        context["port"] = port
    if source is not None:
        context["source"] = source
    return context


def with_endpoint_context(body: Any, port: int | None, source: str | None) -> Any:
    if isinstance(body, dict) and body.get("ok") is False:
        error_info = body.get("error")
        if isinstance(error_info, dict):
            context = error_info.setdefault("context", {})
            if isinstance(context, dict):
                context.update(endpoint_context(port, source))
    return body


def transport_error(code: str, message: str, port: int | None, source: str | None) -> dict[str, Any]:
    body: dict[str, Any] = {"ok": False, "error": {"code": code, "message": message}}
    context = endpoint_context(port, source)
    if context:
        body["error"]["context"] = context
    return body


def post_json(
    url: str,
    payload: dict[str, Any],
    timeout: float,
    *,
    port: int | None = None,
    source: str | None = None,
) -> tuple[int, dict[str, Any]]:
    raw = json.dumps(payload, separators=(",", ":")).encode("utf-8")
    req = urllib.request.Request(
        url,
        data=raw,
        headers={"Content-Type": "application/json; charset=utf-8"},
        method="POST",
    )
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            return resp.getcode(), json.loads(resp.read().decode("utf-8"))
    except urllib.error.HTTPError as exc:
        try:
            return exc.code, with_endpoint_context(json.loads(exc.read().decode("utf-8")), port, source)
        except Exception:
            return exc.code, transport_error("HTTP_ERROR", str(exc), port, source)
    except urllib.error.URLError as exc:
        return 0, transport_error("CONNECTION_FAILED", str(exc.reason), port, source)
    except (TimeoutError, OSError) as exc:
        return 0, transport_error("CONNECTION_FAILED", str(exc), port, source)


def request_payload() -> dict[str, Any]:
    return {"requestId": str(uuid.uuid4())}


def cmd_ping(args: argparse.Namespace) -> tuple[int, dict[str, Any]]:
    base, port, source = request_base(args)
    status, body = post_json(f"{base}/ping", request_payload(), args.timeout, port=port, source=source)
    return exit_code(status, body), {"httpStatus": status, "body": body}


def cmd_commands(args: argparse.Namespace) -> tuple[int, dict[str, Any]]:
    base, port, source = request_base(args)
    status, body = post_json(f"{base}/commands", request_payload(), args.timeout, port=port, source=source)
    return exit_code(status, body), {"httpStatus": status, "body": body}


def cmd_execute(args: argparse.Namespace) -> tuple[int, dict[str, Any]]:
    payload = request_payload()
    payload["command"] = args.command
    payload["args"] = parse_args(args.arg)
    payload["allowMutation"] = bool(args.allow_mutation)
    base, port, source = request_base(args)
    status, body = post_json(f"{base}/execute", payload, args.timeout, port=port, source=source)
    return exit_code(status, body), {"httpStatus": status, "body": body}


def exit_code(status: int, body: dict[str, Any]) -> int:
    if 200 <= status < 300 and body.get("ok") is True:
        return 0
    return 1


def parse_args(items: list[str]) -> dict[str, Any]:
    parsed: dict[str, Any] = {}
    for item in items or []:
        if "=" not in item:
            raise SystemExit(f"--arg expects key=value, got: {item}")
        key, value = item.split("=", 1)
        if not key:
            raise SystemExit("--arg key cannot be empty")
        parsed[key] = parse_scalar(value)
    return parsed


def parse_scalar(raw: str) -> Any:
    lowered = raw.lower()
    if lowered == "true":
        return True
    if lowered == "false":
        return False
    if lowered == "null":
        return None
    try:
        return int(raw)
    except ValueError:
        pass
    try:
        return float(raw)
    except ValueError:
        return raw


def adb_path() -> str | None:
    return shutil.which("adb")


def adb_base(args: argparse.Namespace) -> list[str]:
    exe = adb_path()
    if exe is None:
        raise RuntimeError("adb not found in PATH")
    cmd = [exe]
    if args.serial:
        cmd.extend(["-s", args.serial])
    return cmd


def run_adb(args: argparse.Namespace, extra: list[str]) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        adb_base(args) + extra,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=False,
    )


def marker_path(serial: str, port: int) -> Path:
    safe_serial = "".join(ch if ch.isalnum() or ch in ("-", "_", ".") else "_" for ch in serial)
    return Path(tempfile.gettempdir()) / f"yoji-lua-device-debug-forward-{safe_serial}-{port}.json"


def parse_forward_list(raw: str) -> list[tuple[str, str, str]]:
    forwards: list[tuple[str, str, str]] = []
    for line in raw.splitlines():
        fields = line.split()
        if len(fields) >= 3:
            forwards.append((fields[0], fields[1], fields[2]))
    return forwards


def current_forward(args: argparse.Namespace, local_port: int) -> tuple[str, str, str] | None:
    proc = run_adb(args, ["forward", "--list"])
    if proc.returncode != 0:
        raise RuntimeError("adb forward --list failed: " + proc.stderr.strip())
    local = f"tcp:{local_port}"
    for serial, existing_local, remote in parse_forward_list(proc.stdout):
        if serial == args.serial and existing_local == local:
            return serial, existing_local, remote
    return None


def resolve_device(args: argparse.Namespace) -> tuple[int, dict[str, Any]]:
    if args.serial:
        return 0, {"serial": args.serial}
    proc = run_adb(args, ["devices"])
    if proc.returncode != 0:
        return error("ADB_FAILED", "adb devices failed", stderr=proc.stderr.strip())
    devices: list[str] = []
    for line in proc.stdout.splitlines()[1:]:
        fields = line.split()
        if len(fields) >= 2 and fields[1] == "device":
            devices.append(fields[0])
    if not devices:
        return error("NO_DEVICE", "no Android device is connected")
    if len(devices) > 1:
        return error("MULTIPLE_DEVICES", "multiple devices connected; pass --serial", devices=devices)
    args.serial = devices[0]
    return 0, {"serial": args.serial}


def cmd_adb_forward(args: argparse.Namespace) -> tuple[int, dict[str, Any]]:
    code, payload = resolve_device(args)
    if code != 0:
        return code, payload
    local_port = args.port if args.port is not None else DEFAULT_PORT
    existing = current_forward(args, local_port)
    expected_remote = f"tcp:{args.remote_port}"
    if existing is not None:
        _, local, remote = existing
        if remote == expected_remote:
            return 0, {
                "ok": True,
                "serial": args.serial,
                "local": local,
                "remote": remote,
                "reused": True,
                "owned": False,
            }
        return error("ADB_FORWARD_CONFLICT", "local port is already forwarded to a different remote", local=local, remote=remote)

    proc = run_adb(args, ["forward", f"tcp:{local_port}", f"tcp:{args.remote_port}"])
    if proc.returncode != 0:
        return error("ADB_FORWARD_FAILED", "adb forward failed", stderr=proc.stderr.strip())
    marker_path(args.serial, local_port).write_text(
        json.dumps({"serial": args.serial, "local": f"tcp:{local_port}", "remote": expected_remote}, ensure_ascii=False),
        encoding="utf-8",
    )
    return 0, {
        "ok": True,
        "serial": args.serial,
        "local": f"tcp:{local_port}",
        "remote": expected_remote,
        "reused": False,
        "owned": True,
    }


def cmd_adb_remove(args: argparse.Namespace) -> tuple[int, dict[str, Any]]:
    code, payload = resolve_device(args)
    if code != 0:
        return code, payload
    local_port = args.port if args.port is not None else DEFAULT_PORT
    marker = marker_path(args.serial, local_port)
    if not marker.exists():
        return 0, {
            "ok": True,
            "serial": args.serial,
            "removed": False,
            "reason": "forward was not created by this CLI",
        }
    proc = run_adb(args, ["forward", "--remove", f"tcp:{local_port}"])
    if proc.returncode != 0:
        return error("ADB_REMOVE_FAILED", "adb forward --remove failed", stderr=proc.stderr.strip())
    marker.unlink(missing_ok=True)
    return 0, {"ok": True, "serial": args.serial, "removed": f"tcp:{local_port}"}


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(prog="client.py", description="Unity Lua Device Debug CLI")
    parser.add_argument("--host", default=DEFAULT_HOST)
    parser.add_argument("--port", type=int, default=None)
    parser.add_argument("--project", help="Unity project root. Used to resolve project ports before falling back to legacy 21894.")
    parser.add_argument("--pid", type=int, help="Unity Editor process id when multiple instances are open.")
    parser.add_argument("--timeout", type=float, default=10.0)
    parser.add_argument("--serial", help="adb device serial; required when multiple devices are connected")
    sub = parser.add_subparsers(dest="cmd", required=True)

    sub.add_parser("ping", help="POST /ping").set_defaults(func=cmd_ping)
    sub.add_parser("commands", help="POST /commands").set_defaults(func=cmd_commands)

    execute = sub.add_parser("execute", help="POST /execute")
    execute.add_argument("command")
    execute.add_argument("--arg", action="append", default=[], help="command argument as key=value")
    execute.add_argument("--allow-mutation", action="store_true", help="allow commands declared mutating=true")
    execute.set_defaults(func=cmd_execute)

    adb_forward = sub.add_parser("adb-forward", help="adb forward tcp:PORT tcp:REMOTE_PORT")
    adb_forward.add_argument("--remote-port", type=int, default=DEFAULT_PORT)
    adb_forward.set_defaults(func=cmd_adb_forward)

    sub.add_parser("adb-remove", help="remove adb forward for --port").set_defaults(func=cmd_adb_remove)
    return parser


def main() -> int:
    args = build_parser().parse_args()
    try:
        code, payload = args.func(args)
    except EndpointResolutionError as exc:
        code, payload = error(exc.code, str(exc), **exc.context)
    except RuntimeError as exc:
        code, payload = error("RUNTIME_ERROR", str(exc))
    emit(payload)
    return code


if __name__ == "__main__":
    sys.exit(main())
