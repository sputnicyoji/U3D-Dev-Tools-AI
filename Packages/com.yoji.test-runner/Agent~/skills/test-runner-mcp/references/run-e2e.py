#!/usr/bin/env python3
"""TestRunnerMCP 端到端冒烟。

前提：Unity Editor 已打开 TestProjects/test-runner 工程，服务在 21890。
用法：python run-e2e.py [--port 21890] [--include-recompile] [-v]
"""
from __future__ import annotations

import argparse
import json
import sys
import time
import urllib.error
import urllib.request

PASS_FIX = "Yoji.TestRunner.Tests.FixtureTests.AlwaysPasses"
FAIL_FIX = "Yoji.TestRunner.Tests.FixtureTests.AlwaysFails"


def get(base, path, timeout=10):
    try:
        with urllib.request.urlopen(f"{base}{path}", timeout=timeout) as r:
            return r.getcode(), json.loads(r.read().decode("utf-8"))
    except urllib.error.HTTPError as e:
        try:
            return e.code, json.loads(e.read().decode("utf-8"))
        except Exception:
            return e.code, {}
    except urllib.error.URLError as e:
        return 0, {"error": str(e.reason)}


def post(base, path, payload, timeout=15):
    data = json.dumps(payload).encode("utf-8")
    req = urllib.request.Request(
        f"{base}{path}", data=data,
        headers={"Content-Type": "application/json"}, method="POST")
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            return r.getcode(), json.loads(r.read().decode("utf-8"))
    except urllib.error.HTTPError as e:
        try:
            return e.code, json.loads(e.read().decode("utf-8"))
        except Exception:
            return e.code, {}
    except urllib.error.URLError as e:
        return 0, {"error": str(e.reason)}


def run_and_wait(base, payload, timeout_s=120):
    code, body = post(base, "/run-tests", payload)
    if code != 202:
        return code, body, None
    job = body.get("jobId")
    for _ in range(timeout_s):
        _, s = get(base, f"/test-status?jobId={job}")
        if s.get("status") != "running":
            return code, body, s
        time.sleep(1)
    return code, body, {"status": "timeout"}


def main(argv):
    ap = argparse.ArgumentParser()
    ap.add_argument("--port", type=int, default=21890)
    ap.add_argument("--include-recompile", action="store_true")
    ap.add_argument("-v", "--verbose", action="store_true")
    args = ap.parse_args(argv[1:])
    base = f"http://127.0.0.1:{args.port}"

    code, ping = get(base, "/ping", timeout=5)
    if code != 200:
        print(f"[FATAL] /ping failed: {ping}")
        return 2
    print(f"service state={ping.get('state')} unity={ping.get('unityVersion')} project={ping.get('projectName')}\n")

    results = []

    def check(name, ok, detail=""):
        results.append(ok)
        print(f"  [{'PASS' if ok else 'FAIL'}] {name}" + (f"  -> {detail}" if not ok else ""))

    check("01 /ping state==Idle", ping.get("state") == "Idle", f"state={ping.get('state')}")

    _, _, st = run_and_wait(base, {"testMode": "EditMode", "testNames": [PASS_FIX]})
    check("02 run passing test -> completed passed==1",
          bool(st) and st.get("status") == "completed" and st.get("passed") == 1 and st.get("failed") == 0,
          f"st={st}")

    _, _, st = run_and_wait(base, {"testMode": "EditMode", "testNames": [FAIL_FIX]})
    check("03 run failing test -> failed==1 overall Failed",
          bool(st) and st.get("status") == "completed" and st.get("failed") == 1 and st.get("overallResult") == "Failed",
          f"st={st}")
    fails = (st or {}).get("failures") or []
    check("03b failing test -> failures[0] has name+message (TR-2)",
          len(fails) >= 1 and FAIL_FIX in (fails[0].get("name") or "") and bool(fails[0].get("message")),
          f"failures={fails}")

    code, _ = post(base, "/run-tests", {"testNames": [PASS_FIX]})
    check("04 missing testMode -> 400", code == 400, f"code={code}")

    code, _, st = run_and_wait(base, {"testMode": "PlayMode", "testNames": ["Bogus.NoSuch.PlayModeTest_xyz"]})
    check("05 PlayMode no-match -> accepted then status=error",
          code == 202 and bool(st) and st.get("status") == "error" and st.get("overallResult") == "Error",
          f"code={code} st={st}")

    code, _ = get(base, "/test-status?jobId=deadbeefdeadbeefdeadbeefdeadbeef")
    check("06 unknown jobId -> 404", code == 404, f"code={code}")

    _, _, st = run_and_wait(base, {"testMode": "EditMode"}, timeout_s=180)
    check("07 run-all EditMode -> completed passed>0 failed==0",
          bool(st) and st.get("status") == "completed" and (st.get("passed") or 0) > 0 and st.get("failed") == 0,
          f"st={st}")

    # TR-3a: 拼错的 testName 命中 0 用例必须判 error（不是假绿 Passed），且晚到的 RunFinished 不得改回。
    _, body8, st8 = run_and_wait(base, {"testMode": "EditMode", "testNames": ["Bogus.NoSuch.Test_xyz"]})
    ok8 = bool(st8) and st8.get("status") == "error" and st8.get("overallResult") == "Error"
    check("08 bogus testName -> status=error (TR-3a)", ok8, f"st={st8}")
    job8 = (body8 or {}).get("jobId")
    if ok8 and job8:
        time.sleep(1)
        _, st8b = get(base, f"/test-status?jobId={job8}")
        check("08b error not reverted by late RunFinished",
              st8b.get("status") == "error" and st8b.get("overallResult") == "Error", f"st={st8b}")

    # TR-3b: /list-tests 发现端点
    code9, body9 = get(base, "/list-tests?mode=EditMode", timeout=30)
    tests9 = body9.get("tests") or []
    check("09 /list-tests -> count>0 contains a known test (TR-3b)",
          code9 == 200 and (body9.get("count") or 0) > 0 and any("JobStoreTests" in t for t in tests9),
          f"code={code9} count={body9.get('count')}")
    code9p, body9p = get(base, "/list-tests?mode=PlayMode")
    check("09b /list-tests PlayMode -> 200 with test list",
          code9p == 200 and isinstance(body9p.get("tests"), list) and isinstance(body9p.get("count"), int),
          f"code={code9p} body={body9p}")

    if args.include_recompile:
        code, body = get(base, "/recompile", timeout=200)
        check("R1 /recompile -> success", code == 200 and body.get("success") is True, f"code={code} body={body}")

    passed = sum(1 for ok in results if ok)
    print(f"\n== result: {passed} passed, {len(results) - passed} failed ==")
    return 0 if passed == len(results) else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv))
