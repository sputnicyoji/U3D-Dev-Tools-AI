"""Unity Editor 自动化桥接模块。

通过 AirTest 文件 IPC 控制 Play Mode；运行时一切 C# 反射 / 表达式调用一律走
feval-runtime-debug skill (TCP 127.0.0.1:9999)。
"""

import json
import os
import re
import socket
import subprocess
import sys
import time


# ---------------------------------------------------------------------------
# Unity Editor 检测与启动
# ---------------------------------------------------------------------------

def find_unity_editor(version: str | None = None) -> str:
    """自动检测 Unity Editor 安装路径。

    优先从项目 ProjectVersion.txt 读取版本号，
    然后依次查找 Unity Hub editors.json 和默认安装目录。
    """
    if version is None:
        version = _read_project_version()

    appdata = os.environ.get("APPDATA", "")
    editors_json = os.path.join(appdata, "UnityHub", "editors.json")
    if os.path.isfile(editors_json):
        with open(editors_json, "r", encoding="utf-8") as f:
            editors = json.load(f)
        if version in editors:
            entry = editors[version]
            path = entry if isinstance(entry, str) else entry.get("location", "")
            if path:
                exe = os.path.join(path, "Editor", "Unity.exe") if "Editor" not in path else path
                if os.path.isfile(exe):
                    return exe

    default = rf"C:\Program Files\Unity\Hub\Editor\{version}\Editor\Unity.exe"
    if os.path.isfile(default):
        return default

    hub_root = r"C:\Program Files\Unity\Hub\Editor"
    if os.path.isdir(hub_root) and version:
        prefix = version.split("f")[0] if "f" in version else version
        for d in sorted(os.listdir(hub_root), reverse=True):
            if d.startswith(prefix):
                candidate = os.path.join(hub_root, d, "Editor", "Unity.exe")
                if os.path.isfile(candidate):
                    return candidate

    raise FileNotFoundError(f"Unity Editor {version} not found")


def _read_project_version(project_path: str = ".") -> str:
    pv = os.path.join(project_path, "ProjectSettings", "ProjectVersion.txt")
    if not os.path.isfile(pv):
        return ""
    with open(pv, "r") as f:
        for line in f:
            m = re.match(r"m_EditorVersion:\s*(.+)", line.strip())
            if m:
                return m.group(1).strip()
    return ""


def launch_unity(project_path: str = ".", extra_args: list[str] | None = None) -> subprocess.Popen:
    """启动 Unity Editor 并打开指定项目。"""
    project_path = os.path.abspath(project_path)
    version = _read_project_version(project_path)
    unity_exe = find_unity_editor(version)
    cmd = [unity_exe, "-projectPath", project_path]
    if extra_args:
        cmd.extend(extra_args)
    return subprocess.Popen(cmd)


def is_unity_running(project_path: str = ".") -> bool:
    """检查 Unity 是否已经打开了该项目（通过锁文件判断）。"""
    lock_file = os.path.join(os.path.abspath(project_path), "Temp", "UnityLockfile")
    return os.path.exists(lock_file)


# ---------------------------------------------------------------------------
# Play Mode 控制（文件 IPC，对应 AirTestFileWatcher）
# ---------------------------------------------------------------------------

def _write_command(project_path: str, command: str):
    filepath = os.path.join(os.path.abspath(project_path), "RemoteControlUpdate.txt")
    with open(filepath, "w") as f:
        f.write(command)


def start_play_mode(project_path: str = "."):
    _write_command(project_path, "start")


def stop_play_mode(project_path: str = "."):
    _write_command(project_path, "stop")


def clear_save_data(project_path: str = "."):
    _write_command(project_path, "clear_save_data")


# ---------------------------------------------------------------------------
# feval 服务可达性
# ---------------------------------------------------------------------------

def is_feval_ready(host: str = "127.0.0.1", port: int = 9999, timeout: float = 1.0) -> bool:
    """探测 feval EvaluationService 是否在监听。"""
    try:
        with socket.create_connection((host, port), timeout=timeout):
            return True
    except OSError:
        return False


def wait_for_feval_ready(
    host: str = "127.0.0.1",
    port: int = 9999,
    timeout: float = 180,
    poll_interval: float = 2,
) -> bool:
    """轮询 feval 端口，直到可连接或超时。

    端口可连只代表 EvaluationService 已启动；具体业务级就绪
    （如登录完成）请通过 feval-runtime-debug 跑表达式判定。
    """
    deadline = time.time() + timeout
    while time.time() < deadline:
        if is_feval_ready(host, port):
            return True
        time.sleep(poll_interval)
    return False


# ---------------------------------------------------------------------------
# CLI 入口
# ---------------------------------------------------------------------------

def _test():
    print("=== Unity Bridge Self-Test ===\n")

    try:
        exe = find_unity_editor()
        print(f"[OK] Unity Editor: {exe}")
    except FileNotFoundError as e:
        print(f"[FAIL] {e}")
        return

    running = is_unity_running()
    print(f"[INFO] Unity running: {running}")

    if is_feval_ready():
        print("[OK] feval EvaluationService reachable on 127.0.0.1:9999")
    else:
        print("[INFO] feval not reachable (game not in Play Mode, or AppInfos.IsDebug=false)")


if __name__ == "__main__":
    if len(sys.argv) > 1 and sys.argv[1] == "--test":
        _test()
    else:
        print("Usage: python unity_bridge.py --test")
