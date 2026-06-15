#!/usr/bin/env python3
"""TestRunnerMCP 客户端 CLI。

通过 HTTP 调本地 Unity Editor 内的 TestRunnerMCP 服务。全局 flag（--host/--port/--timeout）
必须放在子命令之前，例如：python client.py --port 21890 ping。
"""
from __future__ import annotations

import argparse
import json
import sys
import urllib.error
import urllib.request

DEFAULT_HOST = "127.0.0.1"
DEFAULT_PORT = 21890


def http_get(url: str, timeout: float):
    try:
        with urllib.request.urlopen(url, timeout=timeout) as r:
            return r.getcode(), json.loads(r.read().decode("utf-8"))
    except urllib.error.HTTPError as e:
        try:
            return e.code, json.loads(e.read().decode("utf-8"))
        except Exception:
            return e.code, {"error": str(e)}
    except urllib.error.URLError as e:
        return 0, {"error": str(e.reason)}


def http_post(url: str, payload: dict, timeout: float):
    body = json.dumps(payload).encode("utf-8")
    req = urllib.request.Request(
        url, data=body,
        headers={"Content-Type": "application/json; charset=utf-8"}, method="POST")
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            return r.getcode(), json.loads(r.read().decode("utf-8"))
    except urllib.error.HTTPError as e:
        try:
            return e.code, json.loads(e.read().decode("utf-8"))
        except Exception:
            return e.code, {"error": str(e)}
    except urllib.error.URLError as e:
        return 0, {"error": str(e.reason)}


def base(a) -> str:
    return f"http://{a.host}:{a.port}"


def cmd_ping(a):
    return http_get(f"{base(a)}/ping", a.timeout)


def cmd_recompile(a):
    return http_get(f"{base(a)}/recompile", a.timeout)


def cmd_run(a):
    payload = {"testMode": a.mode}
    if a.names:
        payload["testNames"] = a.names
    if a.assemblies:
        payload["assemblyNames"] = a.assemblies
    if a.categories:
        payload["categoryNames"] = a.categories
    return http_post(f"{base(a)}/run-tests", payload, a.timeout)


def cmd_status(a):
    url = f"{base(a)}/test-status"
    if a.job_id:
        url += f"?jobId={a.job_id}"
    return http_get(url, a.timeout)


def cmd_list_tests(a):
    url = f"{base(a)}/list-tests?mode={a.mode}"
    return http_get(url, a.timeout)


def build_parser():
    p = argparse.ArgumentParser(prog="client.py", description="TestRunnerMCP CLI client")
    p.add_argument("--host", default=DEFAULT_HOST)
    p.add_argument("--port", type=int, default=DEFAULT_PORT)
    p.add_argument("--timeout", type=float, default=30.0)
    sub = p.add_subparsers(dest="cmd", required=True)

    sub.add_parser("ping", help="连通性 + state").set_defaults(func=cmd_ping)
    sub.add_parser("recompile", help="触发重编译并等待完成").set_defaults(func=cmd_recompile)

    r = sub.add_parser("run-tests", help="发起测试（异步，立即返回 jobId）")
    r.add_argument("--mode", default="EditMode", choices=["EditMode", "PlayMode"])
    r.add_argument("--names", nargs="*", help="完整测试名 Namespace.Class.Method；全空 = 跑全套件（run-all 扩展）")
    r.add_argument("--assemblies", nargs="*", help="按程序集名跑（run-all 扩展）")
    r.add_argument("--categories", nargs="*", help="按 NUnit Category 跑（run-all 扩展）")
    r.set_defaults(func=cmd_run)

    s = sub.add_parser("status", help="轮询任务状态 / 取最近结果（completed 时含 failures[]）")
    s.add_argument("--job-id", help="省略则返回活跃任务或最近一次缓存")
    s.set_defaults(func=cmd_status)

    lt = sub.add_parser("list-tests", help="列出可发现的测试用例全名（拼 testNames 前先查）")
    lt.add_argument("--mode", default="EditMode", choices=["EditMode", "PlayMode"])
    lt.set_defaults(func=cmd_list_tests)
    return p


def main() -> int:
    args = build_parser().parse_args()
    status, body = args.func(args)
    json.dump({"httpStatus": status, "body": body}, sys.stdout, ensure_ascii=False, indent=2)
    sys.stdout.write("\n")
    return 0 if 200 <= status < 300 else 1


if __name__ == "__main__":
    sys.exit(main())
