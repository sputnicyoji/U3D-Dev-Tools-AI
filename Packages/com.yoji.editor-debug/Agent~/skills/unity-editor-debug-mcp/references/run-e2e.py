#!/usr/bin/env python3
# 版权所有[成都创人所爱科技股份有限公司]
"""EditorDebugMCP 端到端冒烟测试。

前提：Unity Editor 已打开目标工程，HTTP 服务已启动。
用法：
    python run-e2e.py                 # 跑除 /recompile 之外的全部用例（~10s）
    python run-e2e.py --include-recompile  # 加测 /recompile（~30-60s，会触发 domain reload）
    python run-e2e.py --port 21892    # 指定端口
    python run-e2e.py --project G:/Project # 项目感知解析端口

每个用例独立 PASS/FAIL，最后给汇总。
"""

from __future__ import annotations

import argparse
import json
import sys
import urllib.error
import urllib.request
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Callable, Optional

SKILL_DIR = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(SKILL_DIR))
from port_resolver import resolve_endpoint  # noqa: E402

DEFAULT_HOST = "127.0.0.1"
DEFAULT_PORT = 21891
LEGACY_PORTS = (21891, 21892, 21893)
SERVICE_ID = "unity-editor-debug-mcp"


@dataclass
class TestCase:
    """单个端到端用例。"""

    name: str
    endpoint: str
    payload: dict
    expect_ok: bool = True
    expect_error_type: Optional[str] = None
    extra_check: Optional[Callable[[dict], Optional[str]]] = None
    timeout: float = 30.0


def http_post(base: str, endpoint: str, payload: dict, timeout: float) -> dict:
    """POST JSON 并返回反序列化后的 dict。"""
    req = urllib.request.Request(
        f"{base}{endpoint}",
        data=json.dumps(payload).encode("utf-8"),
        headers={"Content-Type": "application/json; charset=utf-8"},
        method="POST",
    )
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            return json.loads(resp.read().decode("utf-8"))
    except urllib.error.URLError as e:
        return {"ok": False, "error": {"type": "URLError", "message": str(e.reason)}}
    except Exception as e:
        return {"ok": False, "error": {"type": e.__class__.__name__, "message": str(e)}}


def run_case(base: str, case: TestCase) -> tuple[bool, str, dict]:
    """跑单条用例，返回 (pass, message, raw_response)。"""
    resp = http_post(base, case.endpoint, case.payload, case.timeout)

    if case.expect_ok and not resp.get("ok"):
        err = resp.get("error", {})
        return False, f"expect ok=true, got error.type={err.get('type')} msg={err.get('message')}", resp

    if not case.expect_ok and resp.get("ok"):
        return False, "expect ok=false but got ok=true", resp

    if case.expect_error_type:
        actual = (resp.get("error") or {}).get("type", "")
        if actual != case.expect_error_type:
            return False, f"expect error.type={case.expect_error_type}, got {actual}", resp

    if case.extra_check:
        try:
            err = case.extra_check(resp)
            if err:
                return False, err, resp
        except Exception as e:
            return False, f"extra_check threw {e.__class__.__name__}: {e}", resp

    return True, "", resp


def cases() -> list[TestCase]:
    """所有用例（不含 /recompile）。"""
    return [
        # ===== /ping =====
        TestCase(
            name="01 /ping smoke",
            endpoint="/ping",
            payload={},
            extra_check=lambda r: None
            if r.get("result", {}).get("service") == "EditorDebugMCP"
            else f"service name unexpected: {r.get('result')}",
        ),
        # ===== /invoke =====
        TestCase(
            name="02 /invoke public static property (EditorApplication.applicationPath)",
            endpoint="/invoke",
            payload={"type": "UnityEditor.EditorApplication", "member": "applicationPath", "kind": "get"},
            extra_check=lambda r: None
            if isinstance(r.get("result"), str) and len(r["result"]) > 0
            else f"expect non-empty string, got {type(r.get('result')).__name__}",
        ),
        TestCase(
            name="03 /invoke internal static method (LogEntries.GetCount)",
            endpoint="/invoke",
            payload={"type": "UnityEditor.LogEntries", "member": "GetCount", "kind": "call"},
            extra_check=lambda r: None
            if isinstance(r.get("result"), int) and r["result"] >= 0
            else f"expect int >= 0, got {r.get('result')!r}",
        ),
        TestCase(
            name="04 /invoke chained (SceneManager.GetActiveScene().GetRootGameObjects())",
            endpoint="/invoke",
            payload={
                "type": "UnityEngine.SceneManagement.SceneManager",
                "steps": [
                    {"member": "GetActiveScene", "kind": "call"},
                    {"member": "GetRootGameObjects", "kind": "call"},
                ],
            },
            extra_check=lambda r: None
            if isinstance(r.get("result"), list)
            else f"expect list, got {type(r.get('result')).__name__}",
        ),
        TestCase(
            name="05 /invoke type-not-found returns TypeAccessException",
            endpoint="/invoke",
            payload={"type": "Foo.NotExisting.For.E2E", "member": "X", "kind": "get"},
            expect_ok=False,
            expect_error_type="System.TypeAccessException",
        ),
        TestCase(
            name="06 /invoke argTypes overload resolution (Mathf.Min(int,int))",
            endpoint="/invoke",
            payload={
                "type": "UnityEngine.Mathf",
                "member": "Min",
                "kind": "call",
                "args": [3, 7],
                "argTypes": ["System.Int32", "System.Int32"],
            },
            extra_check=lambda r: None if r.get("result") == 3 else f"expect 3, got {r.get('result')!r}",
        ),
        # ===== /describe =====
        TestCase(
            name="07 /describe public type (UnityEditor.Selection)",
            endpoint="/describe",
            payload={"type": "UnityEditor.Selection"},
            extra_check=lambda r: None
            if r.get("result", {}).get("FullName") == "UnityEditor.Selection"
            and isinstance(r["result"].get("Methods"), list)
            and len(r["result"]["Methods"]) > 0
            else "expect FullName=UnityEditor.Selection with non-empty Methods",
        ),
        TestCase(
            name="08 /describe internal type (UnityEditorInternal.ProfilerDriver)",
            endpoint="/describe",
            payload={"type": "UnityEditorInternal.ProfilerDriver"},
            extra_check=lambda r: None
            if r.get("result", {}).get("FullName") == "UnityEditorInternal.ProfilerDriver"
            else f"FullName unexpected: {r.get('result', {}).get('FullName')}",
        ),
        TestCase(
            name="09 /describe unknown type",
            endpoint="/describe",
            payload={"type": "Foo.Bar.Whatever.For.E2E"},
            expect_ok=False,
            expect_error_type="System.TypeAccessException",
        ),
        # ===== /eval =====
        TestCase(
            name="10 /eval disabled by default (property access)",
            endpoint="/eval",
            payload={"code": "UnityEditor.EditorApplication.applicationPath"},
            expect_ok=False,
            expect_error_type="System.NotSupportedException",
        ),
        TestCase(
            name="11 /eval disabled by default (method call)",
            endpoint="/eval",
            payload={"code": "UnityEditor.LogEntries.GetCount()"},
            expect_ok=False,
            expect_error_type="System.NotSupportedException",
        ),
        TestCase(
            name="12 /eval disabled by default (chained access)",
            endpoint="/eval",
            payload={"code": "UnityEditor.EditorApplication.applicationPath.Length"},
            expect_ok=False,
            expect_error_type="System.NotSupportedException",
        ),
        TestCase(
            name="13 /eval disabled before parsing syntax errors",
            endpoint="/eval",
            payload={"code": 'UnityEditor.LogEntries.GetCount("unterminated'},
            expect_ok=False,
            expect_error_type="System.NotSupportedException",
        ),
        # ===== /ping editor state (ED-5) =====
        TestCase(
            name="14 /ping reports editor state fields (ED-5)",
            endpoint="/ping",
            payload={},
            extra_check=lambda r: None
            if isinstance(r.get("result", {}).get("isPlaying"), bool)
            and isinstance(r.get("result", {}).get("isCompiling"), bool)
            and (r.get("result", {}).get("timeSinceStartup") or 0) > 0
            else f"missing editor-state fields: {r.get('result')}",
        ),
        # ===== /console (ED-1) =====
        TestCase(
            name="15 /console returns log entries array (ED-1)",
            endpoint="/console",
            payload={"count": 20, "filter": "all"},
            extra_check=lambda r: None
            if isinstance(r.get("result", {}).get("entries"), list)
            else f"no entries array: {r.get('result')}",
        ),
        # ===== /batch (ED-2) =====
        TestCase(
            name="16 /batch runs two invokes in one hop (ED-2)",
            endpoint="/batch",
            payload={"requests": [
                {"type": "UnityEditor.EditorApplication", "member": "applicationPath", "kind": "get"},
                {"type": "UnityEditor.LogEntries", "member": "GetCount", "kind": "call"},
            ]},
            extra_check=lambda r: None
            if isinstance(r.get("results"), list) and len(r["results"]) == 2
            and all(x.get("ok") for x in r["results"])
            else f"batch results unexpected: {r.get('results')}",
        ),
        TestCase(
            name="17 /batch over 64 requests rejected (ED-2)",
            endpoint="/batch",
            payload={"requests": [
                {"type": "UnityEditor.EditorApplication", "member": "applicationPath", "kind": "get"}
            ] * 65},
            expect_ok=False,
        ),
    ]


def recompile_case() -> TestCase:
    """单独的 /recompile 用例（慢、会触发 domain reload）。"""
    return TestCase(
        name="R1 /recompile triggers full compilation",
        endpoint="/recompile",
        payload={},
        timeout=180.0,
        extra_check=lambda r: None
        if isinstance(r.get("result"), dict) and r["result"].get("success") is True
        else f"recompile reported failure: {r.get('result')}",
    )


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description="EditorDebugMCP e2e smoke test")
    parser.add_argument("--host", default=DEFAULT_HOST, help="HTTP service host (default 127.0.0.1)")
    parser.add_argument("--port", type=int, default=None, help="HTTP service port. Bypasses project-aware resolution.")
    parser.add_argument("--project", help="Unity project root. Defaults to walking up from cwd.")
    parser.add_argument("--pid", type=int, help="Unity Editor process id when multiple instances are open.")
    parser.add_argument("--resolve-timeout", type=float, default=5.0, help="Endpoint resolution probe timeout in seconds.")
    parser.add_argument("--include-recompile", action="store_true",
                        help="Also test /recompile (slow, triggers domain reload)")
    parser.add_argument("--verbose", "-v", action="store_true",
                        help="Print response body on failure")
    args = parser.parse_args(argv[1:])

    host, port, source = resolve_endpoint(
        SERVICE_ID,
        args.host,
        args.port,
        DEFAULT_PORT,
        args.project,
        args.pid,
        args.resolve_timeout,
        LEGACY_PORTS,
    )
    base = f"http://{host}:{port}"

    print(f"== EditorDebugMCP e2e ==")
    print(f"target: {base} source={source}")

    # 前置 ping
    ping = http_post(base, "/ping", {}, timeout=5.0)
    if not ping.get("ok"):
        print(f"\n[FATAL] /ping failed: {ping.get('error')}")
        print("        Editor 没开 / 服务没起 / 端口被占。先解决再跑。")
        return 2
    print(f"service: {ping['result'].get('service')} v{ping['result'].get('version')} on port {ping['result'].get('port')}")
    print(f"unity: {ping['result'].get('unityVersion')} project: {ping['result'].get('projectName')}")
    print()

    suite = cases()
    if args.include_recompile:
        suite.append(recompile_case())

    print(f"running {len(suite)} cases...\n")

    passed = 0
    failed = 0
    failures: list[tuple[str, str, dict]] = []

    for case in suite:
        ok, msg, resp = run_case(base, case)
        status = "PASS" if ok else "FAIL"
        print(f"  [{status}] {case.name}")
        if ok:
            passed += 1
        else:
            failed += 1
            failures.append((case.name, msg, resp))
            print(f"         -> {msg}")

    print()
    print(f"== result: {passed} passed, {failed} failed ==")

    if failed > 0 and args.verbose:
        print("\n=== failure details ===")
        for name, msg, resp in failures:
            print(f"\n[{name}]")
            print(f"  msg: {msg}")
            print(f"  body: {json.dumps(resp, ensure_ascii=False)[:500]}")

    return 0 if failed == 0 else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv))
