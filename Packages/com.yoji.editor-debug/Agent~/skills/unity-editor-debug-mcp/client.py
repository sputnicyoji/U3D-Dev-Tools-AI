#!/usr/bin/env python3
# 版权所有[成都创人所爱科技股份有限公司]
"""
EditorDebugMCP 客户端 CLI。

通过 HTTP 调本地 Unity Editor 内的 EditorDebugMCP 服务，规避 Bash/curl 中的 JSON 转义地狱。
所有命令默认连 127.0.0.1:21891；可通过 --port 切换。
所有响应原样以 JSON 形式打印到 stdout，错误走非零退出码。
"""

from __future__ import annotations

import argparse
import json
import sys
import urllib.error
import urllib.request
from typing import Any


DEFAULT_HOST = "127.0.0.1"
DEFAULT_PORT = 21891


def http_post(url: str, payload: dict[str, Any], timeout: float = 30.0) -> dict[str, Any]:
    """POST JSON 并返回反序列化后的 dict。HTTP 非 200 时仍尝试解析 body。"""
    body = json.dumps(payload).encode("utf-8")
    req = urllib.request.Request(
        url,
        data=body,
        headers={"Content-Type": "application/json; charset=utf-8"},
        method="POST",
    )
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            return json.loads(resp.read().decode("utf-8"))
    except urllib.error.HTTPError as e:
        # 服务端永远应该返回 200；走到这里通常是奇怪的网关错误，仍尝试读 body
        try:
            return json.loads(e.read().decode("utf-8"))
        except Exception:
            return {"ok": False, "error": {"type": "HTTPError", "message": str(e)}}
    except urllib.error.URLError as e:
        return {"ok": False, "error": {"type": "URLError", "message": str(e.reason)}}


def http_get(url: str, timeout: float = 5.0) -> dict[str, Any]:
    """GET JSON。"""
    try:
        with urllib.request.urlopen(url, timeout=timeout) as resp:
            return json.loads(resp.read().decode("utf-8"))
    except urllib.error.URLError as e:
        return {"ok": False, "error": {"type": "URLError", "message": str(e.reason)}}


def base_url(args: argparse.Namespace) -> str:
    return f"http://{args.host}:{args.port}"


def cmd_ping(args: argparse.Namespace) -> dict[str, Any]:
    """/ping —— 健康检查。Server 端 GET 也走同一 handler。"""
    return http_post(f"{base_url(args)}/ping", payload={})


def parse_target(target_instance_id: int | None) -> dict[str, Any] | None:
    """把 --target-instance-id 翻译成 {instanceID:xxx}。"""
    if target_instance_id is None:
        return None
    return {"instanceID": target_instance_id}


def parse_args_list(raw: list[str]) -> list[Any]:
    """把命令行 --args 列表里每项尝试 JSON 解析；失败的当字符串。"""
    out: list[Any] = []
    for item in raw or []:
        try:
            out.append(json.loads(item))
        except json.JSONDecodeError:
            out.append(item)
    return out


def cmd_invoke(args: argparse.Namespace) -> dict[str, Any]:
    """/invoke —— 单步反射调用。"""
    payload: dict[str, Any] = {
        "type": args.type,
        "member": args.member,
        "kind": args.kind,
    }
    if args.args:
        payload["args"] = parse_args_list(args.args)
    if args.arg_types:
        payload["argTypes"] = list(args.arg_types)
    target = parse_target(args.target_instance_id)
    if target is not None:
        payload["target"] = target
    return http_post(f"{base_url(args)}/invoke", payload, timeout=args.timeout)


def cmd_invoke_chain(args: argparse.Namespace) -> dict[str, Any]:
    """/invoke 链式 steps —— 一次请求完成多步访问。

    --steps 接受形如 'member:kind' 或 'member:kind:arg1,arg2' 的字符串列表。
    """
    steps: list[dict[str, Any]] = []
    for raw in args.steps:
        parts = raw.split(":")
        if len(parts) < 2:
            raise SystemExit(f"steps 项格式错误，期望 'member:kind[:arg1,arg2]': {raw!r}")
        step: dict[str, Any] = {"member": parts[0], "kind": parts[1]}
        if len(parts) >= 3 and parts[2]:
            step["args"] = parse_args_list(parts[2].split(","))
        steps.append(step)

    payload: dict[str, Any] = {"type": args.type, "steps": steps}
    target = parse_target(args.target_instance_id)
    if target is not None:
        payload["target"] = target
    return http_post(f"{base_url(args)}/invoke", payload, timeout=args.timeout)


def cmd_describe(args: argparse.Namespace) -> dict[str, Any]:
    """/describe —— 列出类型成员清单。"""
    return http_post(f"{base_url(args)}/describe", payload={"type": args.type}, timeout=args.timeout)


def cmd_recompile(args: argparse.Namespace) -> dict[str, Any]:
    """/recompile —— 触发脚本重编译。挂住直到完成（typically 30-60s）。"""
    return http_post(f"{base_url(args)}/recompile", payload={}, timeout=args.timeout)


def cmd_eval(args: argparse.Namespace) -> dict[str, Any]:
    """/eval —— 表达式求值。当前阶段：会返回 NotImplemented 错误信封。"""
    payload = {"code": args.code}
    if args.usings:
        payload["usings"] = list(args.usings)
    return http_post(f"{base_url(args)}/eval", payload, timeout=args.timeout)


def build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(prog="client.py", description="EditorDebugMCP CLI client")
    p.add_argument("--host", default=DEFAULT_HOST, help="目标主机，默认 127.0.0.1")
    p.add_argument("--port", type=int, default=DEFAULT_PORT, help="目标端口，默认 21891")
    p.add_argument("--timeout", type=float, default=30.0, help="单次请求超时秒，默认 30")

    sub = p.add_subparsers(dest="cmd", required=True)

    sp_ping = sub.add_parser("ping", help="健康检查")
    sp_ping.set_defaults(func=cmd_ping)

    sp_invoke = sub.add_parser("invoke", help="反射调用单步")
    sp_invoke.add_argument("--type", required=True, help="目标类型全名（可带 ', AssemblyName'）")
    sp_invoke.add_argument("--member", required=True, help="成员名")
    sp_invoke.add_argument("--kind", default="get", choices=["get", "set", "call", "index"], help="操作类型")
    sp_invoke.add_argument("--args", nargs="*", help="实参列表（每项尝试 JSON 解析，失败按字符串）")
    sp_invoke.add_argument("--arg-types", nargs="*", help="参数类型列表，重载决议用")
    sp_invoke.add_argument("--target-instance-id", type=int, help="实例调用时的 InstanceID")
    sp_invoke.set_defaults(func=cmd_invoke)

    sp_chain = sub.add_parser("invoke-chain", help="反射链式调用（多步）")
    sp_chain.add_argument("--type", required=True, help="起点类型全名")
    sp_chain.add_argument("--steps", nargs="+", required=True,
                          help="形如 'member:kind' 或 'member:kind:arg1,arg2' 的步骤列表")
    sp_chain.add_argument("--target-instance-id", type=int, help="起点实例 InstanceID")
    sp_chain.set_defaults(func=cmd_invoke_chain)

    sp_desc = sub.add_parser("describe", help="列出类型成员清单")
    sp_desc.add_argument("--type", required=True, help="类型全名")
    sp_desc.set_defaults(func=cmd_describe)

    sp_rec = sub.add_parser("recompile", help="触发脚本重编译并等待完成")
    sp_rec.set_defaults(func=cmd_recompile)

    sp_eval = sub.add_parser("eval", help="表达式求值")
    sp_eval.add_argument("--code", required=True, help="C# 表达式字符串")
    sp_eval.add_argument("--usings", nargs="*", help="额外 using 命名空间列表")
    sp_eval.set_defaults(func=cmd_eval)

    return p


def main() -> int:
    parser = build_parser()
    args = parser.parse_args()
    result = args.func(args)
    json.dump(result, sys.stdout, ensure_ascii=False, indent=2)
    sys.stdout.write("\n")
    return 0 if result.get("ok") else 1


if __name__ == "__main__":
    sys.exit(main())
