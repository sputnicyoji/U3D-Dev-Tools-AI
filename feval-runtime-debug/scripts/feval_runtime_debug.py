#!/usr/bin/env python3
"""优先通过标准输入输出 CLI 调用 feval，必要时回退到真实 Windows 控制台。"""

from __future__ import annotations

import argparse
import ctypes
import json
import os
import os.path
import subprocess
import sys
import tempfile
import time
import uuid
from pathlib import Path
from typing import Any

from ctypes import wintypes


kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
user32 = ctypes.WinDLL("user32", use_last_error=True)

GENERIC_READ = 0x80000000
GENERIC_WRITE = 0x40000000
FILE_SHARE_READ = 0x00000001
FILE_SHARE_WRITE = 0x00000002
OPEN_EXISTING = 3
KEY_EVENT = 0x0001
SHIFT_PRESSED = 0x0010
LEFT_CTRL_PRESSED = 0x0008
LEFT_ALT_PRESSED = 0x0002
INVALID_HANDLE_VALUE = ctypes.c_void_p(-1).value
PROMPT = ">>"
FEVAL_REPO_URL = "https://git.tap4fun.com/tfw/com.tfw.feval"


class COORD(ctypes.Structure):
    _fields_ = [("X", wintypes.SHORT), ("Y", wintypes.SHORT)]


class SMALL_RECT(ctypes.Structure):
    _fields_ = [
        ("Left", wintypes.SHORT),
        ("Top", wintypes.SHORT),
        ("Right", wintypes.SHORT),
        ("Bottom", wintypes.SHORT),
    ]


class CONSOLE_SCREEN_BUFFER_INFO(ctypes.Structure):
    _fields_ = [
        ("dwSize", COORD),
        ("dwCursorPosition", COORD),
        ("wAttributes", wintypes.WORD),
        ("srWindow", SMALL_RECT),
        ("dwMaximumWindowSize", COORD),
    ]


class CHAR_UNION(ctypes.Union):
    _fields_ = [("UnicodeChar", wintypes.WCHAR), ("AsciiChar", ctypes.c_char)]


class KEY_EVENT_RECORD(ctypes.Structure):
    _fields_ = [
        ("bKeyDown", wintypes.BOOL),
        ("wRepeatCount", wintypes.WORD),
        ("wVirtualKeyCode", wintypes.WORD),
        ("wVirtualScanCode", wintypes.WORD),
        ("uChar", CHAR_UNION),
        ("dwControlKeyState", wintypes.DWORD),
    ]


class EVENT_UNION(ctypes.Union):
    _fields_ = [("KeyEvent", KEY_EVENT_RECORD)]


class INPUT_RECORD(ctypes.Structure):
    _fields_ = [("EventType", wintypes.WORD), ("Event", EVENT_UNION)]


kernel32.AttachConsole.argtypes = [wintypes.DWORD]
kernel32.AttachConsole.restype = wintypes.BOOL
kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
kernel32.CloseHandle.restype = wintypes.BOOL
kernel32.FreeConsole.argtypes = []
kernel32.FreeConsole.restype = wintypes.BOOL
kernel32.CreateFileW.argtypes = [
    wintypes.LPCWSTR,
    wintypes.DWORD,
    wintypes.DWORD,
    wintypes.LPVOID,
    wintypes.DWORD,
    wintypes.DWORD,
    wintypes.HANDLE,
]
kernel32.CreateFileW.restype = wintypes.HANDLE
kernel32.GetConsoleScreenBufferInfo.argtypes = [
    wintypes.HANDLE,
    ctypes.POINTER(CONSOLE_SCREEN_BUFFER_INFO),
]
kernel32.GetConsoleScreenBufferInfo.restype = wintypes.BOOL
kernel32.ReadConsoleOutputCharacterW.argtypes = [
    wintypes.HANDLE,
    wintypes.LPWSTR,
    wintypes.DWORD,
    COORD,
    ctypes.POINTER(wintypes.DWORD),
]
kernel32.ReadConsoleOutputCharacterW.restype = wintypes.BOOL
kernel32.WriteConsoleInputW.argtypes = [
    wintypes.HANDLE,
    ctypes.POINTER(INPUT_RECORD),
    wintypes.DWORD,
    ctypes.POINTER(wintypes.DWORD),
]
kernel32.WriteConsoleInputW.restype = wintypes.BOOL
user32.VkKeyScanW.argtypes = [wintypes.WCHAR]
user32.VkKeyScanW.restype = wintypes.SHORT
user32.MapVirtualKeyW.argtypes = [wintypes.UINT, wintypes.UINT]
user32.MapVirtualKeyW.restype = wintypes.UINT


def default_state_file() -> Path:
    """返回默认会话状态文件路径。"""
    return Path(tempfile.gettempdir()) / "feval-runtime-debug.json"


def load_commands_from_file(path: Path) -> list[str]:
    """从 UTF-8 文本读取 feval 表达式列表，每行一条；`#` 开头行为注释。"""
    if not path.is_file():
        raise FileNotFoundError(f"commands 文件不存在: {path}")
    out: list[str] = []
    for line in path.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        out.append(line)
    return out


def emit_json(payload: dict[str, Any]) -> None:
    """稳定输出 JSON，方便外部工具直接解析。"""
    print(json.dumps(payload, ensure_ascii=False, indent=2))


def check_bool(result: bool, func_name: str) -> None:
    """统一包装 Win32 API 错误。"""
    if not result:
        raise ctypes.WinError(ctypes.get_last_error(), f"{func_name} failed")


def load_state(path: Path) -> dict[str, Any]:
    """加载会话状态。"""
    if not path.exists():
        raise FileNotFoundError(f"会话状态文件不存在: {path}")
    return json.loads(path.read_text(encoding="utf-8"))


def save_state(path: Path, state: dict[str, Any]) -> None:
    """保存会话状态。"""
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(state, ensure_ascii=False, indent=2), encoding="utf-8")


def is_process_alive(pid: int) -> bool:
    """判断记录的 console 进程是否仍然存活。"""
    result = subprocess.run(
        ["tasklist", "/FI", f"PID eq {pid}"],
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="ignore",
        check=False,
    )
    return str(pid) in result.stdout


def find_feval_command() -> str | None:
    """查找 feval 命令是否已安装并可从当前环境直接调用。"""
    result = subprocess.run(
        ["where.exe", "feval"],
        capture_output=True,
        text=True,
        encoding="gbk",
        errors="ignore",
        check=False,
    )
    if result.returncode != 0:
        return None

    for line in result.stdout.splitlines():
        candidate = line.strip()
        if candidate:
            return candidate
    return None


def ensure_feval_available() -> str:
    """确保 feval 已安装且命令可用，否则给出明确提示。"""
    feval_path = find_feval_command()
    if feval_path is None:
        raise RuntimeError(
            "未找到 feval 命令。请先安装 feval，并确认它已加入 PATH。"
            f" 参考仓库: {FEVAL_REPO_URL}"
        )
    return feval_path


def get_feval_version(feval_path: str) -> str | None:
    """读取 feval 版本号文本。"""
    result = subprocess.run(
        [feval_path, "--version"],
        capture_output=True,
        text=True,
        errors="ignore",
        check=False,
    )
    version_text = (result.stdout or result.stderr or "").strip()
    return version_text or None


def open_console_handles(pid: int) -> tuple[int, int]:
    """附着到目标控制台并打开输入/输出句柄。"""
    kernel32.FreeConsole()
    check_bool(kernel32.AttachConsole(pid), "AttachConsole")

    handle_in = kernel32.CreateFileW(
        "CONIN$",
        GENERIC_READ | GENERIC_WRITE,
        FILE_SHARE_READ | FILE_SHARE_WRITE,
        None,
        OPEN_EXISTING,
        0,
        None,
    )
    if handle_in == INVALID_HANDLE_VALUE:
        raise ctypes.WinError(ctypes.get_last_error(), "CreateFileW(CONIN$) failed")

    handle_out = kernel32.CreateFileW(
        "CONOUT$",
        GENERIC_READ | GENERIC_WRITE,
        FILE_SHARE_READ | FILE_SHARE_WRITE,
        None,
        OPEN_EXISTING,
        0,
        None,
    )
    if handle_out == INVALID_HANDLE_VALUE:
        raise ctypes.WinError(ctypes.get_last_error(), "CreateFileW(CONOUT$) failed")

    return handle_in, handle_out


def close_handle(handle: int | None) -> None:
    """关闭 Win32 句柄，避免在当前进程里泄漏句柄。"""
    if handle and handle != INVALID_HANDLE_VALUE:
        kernel32.CloseHandle(handle)


def kill_process_tree(pid: int) -> subprocess.CompletedProcess[str]:
    """强制终止指定 PID 对应的整个进程树。"""
    return subprocess.run(
        ["taskkill", "/PID", str(pid), "/T", "/F"],
        capture_output=True,
        text=True,
        encoding="gbk",
        errors="ignore",
        check=False,
    )


def normalize_console_text(raw_text: str, width: int, cursor_x: int, cursor_y: int) -> str:
    """按控制台宽度还原文本，并裁掉每行末尾的填充空格。"""
    if width <= 0:
        return ""

    lines: list[str] = []
    index = 0
    total_rows = cursor_y + 1
    for row in range(total_rows):
        if row == cursor_y:
            segment = raw_text[index : index + cursor_x]
        else:
            segment = raw_text[index : index + width]
        lines.append(segment.rstrip())
        index += width
    return "\n".join(lines)


def read_console_text(handle_out: int) -> str:
    """读取控制台从起点到当前光标位置的文本。"""
    info = CONSOLE_SCREEN_BUFFER_INFO()
    check_bool(
        kernel32.GetConsoleScreenBufferInfo(handle_out, ctypes.byref(info)),
        "GetConsoleScreenBufferInfo",
    )

    width = info.dwSize.X
    cursor_x = info.dwCursorPosition.X
    cursor_y = info.dwCursorPosition.Y
    length = width * cursor_y + cursor_x
    if length <= 0:
        return ""

    buffer = ctypes.create_unicode_buffer(length + 1)
    chars_read = wintypes.DWORD(0)
    check_bool(
        kernel32.ReadConsoleOutputCharacterW(
            handle_out,
            buffer,
            length,
            COORD(0, 0),
            ctypes.byref(chars_read),
        ),
        "ReadConsoleOutputCharacterW",
    )

    raw_text = buffer[: chars_read.value]
    return normalize_console_text(raw_text, width, cursor_x, cursor_y)


def build_startup_error_message(endpoint: str, last_screen: str, last_error: str) -> str:
    """根据启动阶段的屏幕内容，输出更贴脸的错误提示。"""
    screen = last_screen.strip()
    if "不是内部或外部命令" in screen or "not recognized" in screen.lower():
        return (
            "feval 启动失败：当前环境无法识别 `feval` 命令。"
            f" 请先安装 feval 并确认 PATH 配置正确。参考仓库: {FEVAL_REPO_URL}"
        )

    if "连接中" in screen or "connected" not in screen.lower():
        return (
            "feval 已启动但未成功进入可执行状态。"
            f" 请确认目标应用已正确接入 feval 服务，并且 endpoint `{endpoint}` 可连通。"
            f" 参考仓库: {FEVAL_REPO_URL}"
            + (f" 最后输出: {screen}" if screen else "")
        )

    if last_error:
        return f"启动 feval 失败：{last_error}"

    return (
        "启动 feval 后未等到 prompt。"
        f" 请确认 feval 已安装，且目标应用已正确接入 feval 服务。参考仓库: {FEVAL_REPO_URL}"
    )


def build_cli_error_message(endpoint: str, stdout: str, stderr: str) -> str:
    """构造标准输入输出 CLI 模式下的错误信息。"""
    details = stderr.strip() or stdout.strip()
    if "not recognized" in details.lower() or "不是内部或外部命令" in details:
        return (
            "feval 启动失败：当前环境无法识别 `feval` 命令。"
            f" 请先安装 feval 并确认 PATH 配置正确。参考仓库: {FEVAL_REPO_URL}"
        )

    if details:
        return (
            f"通过标准输入输出 CLI 调用 feval 失败。"
            f" 请确认 endpoint `{endpoint}` 可连通，且目标应用已正确接入 feval 服务。"
            f" 错误输出: {details}"
        )

    return (
        f"通过标准输入输出 CLI 调用 feval 失败。"
        f" 请确认 endpoint `{endpoint}` 可连通，且目标应用已正确接入 feval 服务。"
        f" 参考仓库: {FEVAL_REPO_URL}"
    )


def normalize_output_lines(stdout: str) -> list[str]:
    """把标准输出拆成逐行结果，并去掉末尾空行。"""
    normalized = stdout.replace("\r\n", "\n").rstrip("\n")
    if not normalized:
        return []
    return normalized.split("\n")


def run_stdio_cli(
    endpoint: str,
    cwd: Path,
    commands: list[str],
    timeout: float,
    *,
    feval_path: str,
    action: str,
    state_file: Path | None = None,
) -> dict[str, Any]:
    """通过 feval 的标准输入输出 CLI 执行一条或多条命令。"""
    version_text = get_feval_version(feval_path)
    args = [feval_path, endpoint, "-e", *commands]

    try:
        result = subprocess.run(
            args,
            cwd=str(cwd),
            capture_output=True,
            text=True,
            errors="ignore",
            timeout=timeout,
            check=False,
        )
    except subprocess.TimeoutExpired as ex:
        raise TimeoutError(
            f"标准输入输出 CLI 执行超时。endpoint={endpoint} commands={commands} timeout={timeout}s"
        ) from ex

    stdout = (result.stdout or "").replace("\r\n", "\n").rstrip("\n")
    stderr = (result.stderr or "").replace("\r\n", "\n").rstrip("\n")
    output_lines = normalize_output_lines(stdout)
    # 若 feval 不支持 -e（如 1.3.7 报 Option 'e' is unknown），视为失败以便回退到控制台
    stdio_ok = result.returncode == 0 and "Option 'e' is unknown" not in stderr and "ERROR(S):" not in stderr
    payload = {
        "ok": stdio_ok,
        "action": action,
        "transport": "stdio-cli",
        "endpoint": endpoint,
        "cwd": str(cwd),
        "feval_path": feval_path,
        "feval_version": version_text,
        "state_file": str(state_file) if state_file else None,
        "commands": commands,
        "command": commands[0] if len(commands) == 1 else None,
        "output": stdout,
        "output_lines": output_lines,
        "stdout": stdout,
        "stderr": stderr,
        "returncode": result.returncode,
    }
    if not payload["ok"]:
        payload["error"] = build_cli_error_message(endpoint, stdout, stderr)
    return payload


def has_prompt(text: str) -> bool:
    """判断屏幕末尾是否已经回到 feval prompt。"""
    stripped = text.rstrip()
    return stripped.endswith(PROMPT)


def wait_for_prompt(handle_out: int, timeout: float, min_length: int = 0) -> str:
    """轮询控制台，直到看到 prompt。"""
    deadline = time.time() + timeout
    last_text = ""
    while time.time() < deadline:
        text = read_console_text(handle_out)
        last_text = text
        if len(text) >= min_length and has_prompt(text):
            return text
        time.sleep(0.1)
    raise TimeoutError(f"等待 feval prompt 超时。最后输出:\n{last_text}")


def make_key_records(ch: str) -> tuple[INPUT_RECORD, INPUT_RECORD]:
    """将字符转换为控制台输入事件。"""
    vk_scan = user32.VkKeyScanW(ch)
    if vk_scan == -1:
        virtual_key = 0
        shift_state = 0
    else:
        virtual_key = vk_scan & 0xFF
        shift_state = (vk_scan >> 8) & 0xFF

    if ch == "\r":
        virtual_key = 0x0D
        shift_state = 0

    scan_code = user32.MapVirtualKeyW(virtual_key, 0) if virtual_key else 0
    control_state = 0
    if shift_state & 0x01:
        control_state |= SHIFT_PRESSED
    if shift_state & 0x02:
        control_state |= LEFT_CTRL_PRESSED
    if shift_state & 0x04:
        control_state |= LEFT_ALT_PRESSED

    def build_record(is_key_down: bool) -> INPUT_RECORD:
        record = INPUT_RECORD()
        record.EventType = KEY_EVENT

        key_event = KEY_EVENT_RECORD()
        key_event.bKeyDown = 1 if is_key_down else 0
        key_event.wRepeatCount = 1
        key_event.wVirtualKeyCode = virtual_key
        key_event.wVirtualScanCode = scan_code
        key_event.uChar.UnicodeChar = ch
        key_event.dwControlKeyState = control_state

        record.Event.KeyEvent = key_event
        return record

    return build_record(True), build_record(False)


def write_console_text(handle_in: int, text: str) -> None:
    """把整段文本作为按键事件写入目标控制台。"""
    records: list[INPUT_RECORD] = []
    for ch in text:
        key_down, key_up = make_key_records(ch)
        records.append(key_down)
        records.append(key_up)

    if not records:
        return

    array_type = INPUT_RECORD * len(records)
    written = wintypes.DWORD(0)
    check_bool(
        kernel32.WriteConsoleInputW(
            handle_in,
            array_type(*records),
            len(records),
            ctypes.byref(written),
        ),
        "WriteConsoleInputW",
    )
    if written.value != len(records):
        raise RuntimeError(f"控制台输入写入不完整: {written.value}/{len(records)}")


def strip_prompt(text: str) -> str:
    """移除末尾 prompt，保留真实轮次内容。"""
    stripped = text.rstrip()
    if stripped.endswith(PROMPT):
        stripped = stripped[: -len(PROMPT)].rstrip()
    return stripped


def calc_delta(before: str, after: str) -> str:
    """尽量从完整屏幕快照中裁出本轮新增内容。"""
    prefix = os.path.commonprefix([before, after])
    return after[len(prefix) :].lstrip("\n")


def session_title() -> str:
    """生成唯一窗口标题，便于人工辨认。"""
    return f"FEVAL_SESSION_{uuid.uuid4().hex[:8].upper()}"


def build_cmd_command(title: str, endpoint: str) -> str:
    """构造 cmd 启动命令。"""
    return f"title {title} && feval {endpoint}"


def start_console_session(
    endpoint: str,
    cwd: Path,
    timeout: float,
    state_file: Path,
    *,
    feval_path: str,
    version_text: str | None,
) -> dict[str, Any]:
    """启动旧版控制台会话，并把状态写回 state_file。"""
    title = session_title()
    process = subprocess.Popen(
        ["cmd.exe", "/d", "/k", build_cmd_command(title, endpoint)],
        cwd=str(cwd),
        creationflags=subprocess.CREATE_NEW_CONSOLE,
    )

    deadline = time.time() + timeout
    initial_screen = ""
    last_error = ""
    handle_in = None
    handle_out = None
    try:
        while time.time() < deadline:
            try:
                handle_in, handle_out = open_console_handles(process.pid)
                initial_screen = wait_for_prompt(
                    handle_out,
                    timeout=max(0.5, min(2.0, deadline - time.time())),
                )
                break
            except Exception as ex:  # noqa: BLE001
                last_error = str(ex)
                close_handle(handle_in)
                close_handle(handle_out)
                handle_in = None
                handle_out = None
                time.sleep(0.2)
            finally:
                kernel32.FreeConsole()
        else:
            raise TimeoutError(build_startup_error_message(endpoint, initial_screen, last_error))

        state = {
            "transport": "console",
            "pid": process.pid,
            "endpoint": endpoint,
            "feval_path": feval_path,
            "feval_version": version_text,
            "cwd": str(cwd),
            "title": title,
            "state_file": str(state_file),
            "created_at": time.strftime("%Y-%m-%d %H:%M:%S"),
        }
        save_state(state_file, state)
        return {
            "ok": True,
            "action": "start",
            "transport": "console",
            "state_file": str(state_file),
            "pid": process.pid,
            "endpoint": endpoint,
            "feval_path": feval_path,
            "feval_version": version_text,
            "title": title,
            "screen": initial_screen,
        }
    except Exception:
        kill_process_tree(process.pid)
        raise
    finally:
        close_handle(handle_in)
        close_handle(handle_out)
        kernel32.FreeConsole()


def start_session(endpoint: str, cwd: Path, timeout: float, state_file: Path) -> dict[str, Any]:
    """记录自动模式会话，后续 exec 默认先尝试 stdio-cli。"""
    feval_path = ensure_feval_available()
    version_text = get_feval_version(feval_path)
    state = {
        "transport": "auto",
        "pid": None,
        "endpoint": endpoint,
        "feval_path": feval_path,
        "feval_version": version_text,
        "cwd": str(cwd),
        "state_file": str(state_file),
        "created_at": time.strftime("%Y-%m-%d %H:%M:%S"),
    }
    save_state(state_file, state)
    return {
        "ok": True,
        "action": "start",
        "transport": "auto",
        "state_file": str(state_file),
        "pid": None,
        "endpoint": endpoint,
        "feval_path": feval_path,
        "feval_version": version_text,
        "message": "后续 exec 会默认先尝试标准输入输出 CLI；若失败，再自动回退到控制台会话。",
    }


def exec_console_command(pid: int, command: str, timeout: float) -> dict[str, Any]:
    """在控制台会话里执行单条命令。"""
    if not is_process_alive(pid):
        raise RuntimeError(f"会话进程不存在或已退出: pid={pid}")

    handle_in = None
    handle_out = None
    try:
        handle_in, handle_out = open_console_handles(pid)
        before_screen = read_console_text(handle_out)
        write_console_text(handle_in, command + "\r")
        after_screen = wait_for_prompt(
            handle_out,
            timeout=timeout,
            min_length=len(before_screen) + 1,
        )
    finally:
        close_handle(handle_in)
        close_handle(handle_out)
        kernel32.FreeConsole()

    raw_delta = calc_delta(before_screen, after_screen)
    output = strip_prompt(raw_delta)
    return {
        "command": command,
        "output": output,
        "raw_delta": raw_delta,
        "screen": after_screen,
    }


def exec_session(
    state_file: Path,
    commands: list[str],
    timeout: float,
    *,
    no_console_fallback: bool = False,
) -> dict[str, Any]:
    """在现有会话里执行一条或多条命令。"""
    state = load_state(state_file)
    transport = state.get("transport", "console")
    endpoint = state.get("endpoint")
    cwd = Path(state.get("cwd", os.getcwd()))
    feval_path = state.get("feval_path") or ensure_feval_available()
    version_text = state.get("feval_version") or get_feval_version(feval_path)

    if transport == "stdio-cli":
        transport = "auto"

    if transport == "console":
        if no_console_fallback:
            return {
                "ok": False,
                "action": "exec",
                "transport": "console",
                "state_file": str(state_file),
                "error": (
                    "状态文件指向控制台会话，与 --no-console-fallback 冲突。"
                    "请 `stop` 后删除状态文件，或改用不含控制台会话的新 `start`。"
                ),
            }
        pid = int(state["pid"])
        results = [exec_console_command(pid, command, timeout) for command in commands]
        output_parts = [item["output"] for item in results if item["output"]]
        raw_delta_parts = [item["raw_delta"] for item in results if item["raw_delta"]]
        return {
            "ok": True,
            "action": "exec",
            "transport": "console",
            "state_file": str(state_file),
            "pid": pid,
            "endpoint": endpoint,
            "cwd": str(cwd),
            "feval_path": feval_path,
            "feval_version": version_text,
            "commands": commands,
            "command": commands[0] if len(commands) == 1 else None,
            "output": "\n".join(output_parts),
            "raw_delta": "\n".join(raw_delta_parts),
            "screen": results[-1]["screen"] if results else "",
            "results": results,
        }

    stdio_attempt = None
    stdio_error = None
    try:
        stdio_attempt = run_stdio_cli(
            endpoint=endpoint,
            cwd=cwd,
            commands=commands,
            timeout=timeout,
            feval_path=feval_path,
            action="exec",
            state_file=state_file,
        )
        if stdio_attempt.get("ok"):
            return stdio_attempt
    except Exception as ex:  # noqa: BLE001
        stdio_error = str(ex)

    if no_console_fallback:
        err = stdio_error or (
            stdio_attempt.get("error") if stdio_attempt else None
        ) or "stdio-cli 执行失败且已禁止控制台回退 (--no-console-fallback)。"
        payload: dict[str, Any] = {
            "ok": False,
            "action": "exec",
            "transport": "stdio-cli",
            "no_console_fallback": True,
            "state_file": str(state_file),
            "endpoint": endpoint,
            "cwd": str(cwd),
            "feval_path": feval_path,
            "feval_version": version_text,
            "commands": commands,
            "command": commands[0] if len(commands) == 1 else None,
            "error": err,
        }
        if stdio_attempt is not None:
            payload["stdio_attempt"] = stdio_attempt
        if stdio_error is not None:
            payload["stdio_error"] = stdio_error
        return payload

    console_start = start_console_session(
        endpoint=endpoint,
        cwd=cwd,
        timeout=timeout,
        state_file=state_file,
        feval_path=feval_path,
        version_text=version_text,
    )
    pid = int(console_start["pid"])
    results = [exec_console_command(pid, command, timeout) for command in commands]
    output_parts = [item["output"] for item in results if item["output"]]
    raw_delta_parts = [item["raw_delta"] for item in results if item["raw_delta"]]
    return {
        "ok": True,
        "action": "exec",
        "transport": "console",
        "fallback_from": "stdio-cli",
        "state_file": str(state_file),
        "pid": pid,
        "endpoint": endpoint,
        "cwd": str(cwd),
        "feval_path": feval_path,
        "feval_version": version_text,
        "commands": commands,
        "command": commands[0] if len(commands) == 1 else None,
        "output": "\n".join(output_parts),
        "raw_delta": "\n".join(raw_delta_parts),
        "screen": results[-1]["screen"] if results else "",
        "results": results,
        "console_start": console_start,
        "stdio_attempt": stdio_attempt,
        "stdio_error": stdio_error,
    }


def status_session(state_file: Path) -> dict[str, Any]:
    """查看会话是否还活着，并抓取当前屏幕。"""
    state = load_state(state_file)
    transport = state.get("transport", "console")
    if transport != "console":
        return {
            "ok": True,
            "action": "status",
            "transport": transport,
            "state_file": str(state_file),
            "pid": None,
            "alive": True,
            "stateless": True,
            "screen": "",
            "endpoint": state.get("endpoint"),
            "cwd": state.get("cwd"),
            "feval_path": state.get("feval_path"),
            "feval_version": state.get("feval_version"),
            "message": "当前还没有持久控制台会话；exec 会默认先尝试标准输入输出 CLI，失败后再回退到控制台。",
        }

    pid = int(state["pid"])
    alive = is_process_alive(pid)
    screen = ""
    handle_in = None
    handle_out = None
    if alive:
        try:
            handle_in, handle_out = open_console_handles(pid)
            screen = read_console_text(handle_out)
        finally:
            close_handle(handle_in)
            close_handle(handle_out)
            kernel32.FreeConsole()

    return {
        "ok": True,
        "action": "status",
        "transport": "console",
        "state_file": str(state_file),
        "pid": pid,
        "alive": alive,
        "screen": screen,
        "endpoint": state.get("endpoint"),
        "cwd": state.get("cwd"),
        "feval_path": state.get("feval_path"),
        "feval_version": state.get("feval_version"),
    }


def stop_session(state_file: Path) -> dict[str, Any]:
    """停止记录中的整个控制台进程树。"""
    state = load_state(state_file)
    transport = state.get("transport", "console")
    if transport != "console":
        if state_file.exists():
            state_file.unlink()
        return {
            "ok": True,
            "action": "stop",
            "transport": transport,
            "state_file": str(state_file),
            "pid": None,
            "stdout": "",
            "stderr": "",
            "message": "当前没有持久控制台进程，已清理状态文件。",
        }

    pid = int(state["pid"])
    result = kill_process_tree(pid)
    if state_file.exists():
        state_file.unlink()

    return {
        "ok": result.returncode == 0,
        "action": "stop",
        "transport": "console",
        "state_file": str(state_file),
        "pid": pid,
        "stdout": result.stdout.strip(),
        "stderr": result.stderr.strip(),
    }


def run_once(
    endpoint: str,
    cwd: Path,
    commands: list[str],
    timeout: float,
    *,
    no_console_fallback: bool = False,
) -> dict[str, Any]:
    """单次启动、执行、停止，适合快速验证。"""
    feval_path = ensure_feval_available()
    version_text = get_feval_version(feval_path)
    stdio_attempt = None
    stdio_error = None
    try:
        stdio_attempt = run_stdio_cli(
            endpoint=endpoint,
            cwd=cwd,
            commands=commands,
            timeout=timeout,
            feval_path=feval_path,
            action="run",
        )
        if stdio_attempt.get("ok"):
            return stdio_attempt
    except Exception as ex:  # noqa: BLE001
        stdio_error = str(ex)

    if no_console_fallback:
        err = stdio_error or (
            stdio_attempt.get("error") if stdio_attempt else None
        ) or "stdio-cli 执行失败且已禁止控制台回退 (--no-console-fallback)。"
        out: dict[str, Any] = {
            "ok": False,
            "action": "run",
            "transport": "stdio-cli",
            "no_console_fallback": True,
            "endpoint": endpoint,
            "cwd": str(cwd),
            "feval_path": feval_path,
            "feval_version": version_text,
            "commands": commands,
            "command": commands[0] if len(commands) == 1 else None,
            "error": err,
        }
        if stdio_attempt is not None:
            out["stdio_attempt"] = stdio_attempt
        if stdio_error is not None:
            out["stdio_error"] = stdio_error
        return out

    state_file = Path(tempfile.gettempdir()) / f"feval-runtime-debug-{uuid.uuid4().hex}.json"
    start_result = None
    exec_result = None
    payload = None
    try:
        start_result = start_console_session(
            endpoint=endpoint,
            cwd=cwd,
            timeout=timeout,
            state_file=state_file,
            feval_path=feval_path,
            version_text=version_text,
        )
        exec_result = exec_session(state_file, commands, timeout)
        payload = {
            "ok": True,
            "action": "run",
            "transport": "console",
            "fallback_from": "stdio-cli",
            "endpoint": endpoint,
            "state_file": str(state_file),
            "start": start_result,
            "exec": exec_result,
            "stop": None,
            "stdio_attempt": stdio_attempt,
            "stdio_error": stdio_error,
        }
    finally:
        stop_result = None
        if state_file.exists():
            stop_result = stop_session(state_file)
        if payload is not None:
            payload["stop"] = stop_result

    if payload is None:
        raise RuntimeError("run 模式未能生成结果。")
    return payload


def parse_args() -> argparse.Namespace:
    """解析命令行参数。"""
    parser = argparse.ArgumentParser(description="优先通过标准输入输出 CLI 调用 feval，会自动回退到控制台方案。")
    subparsers = parser.add_subparsers(dest="action", required=True)

    start_parser = subparsers.add_parser("start", help="记录调试会话，默认后续 exec 先尝试 stdio-cli")
    start_parser.add_argument("--endpoint", required=True, help="feval endpoint，例如 127.0.0.1:9999 或 local")
    start_parser.add_argument("--cwd", default=os.getcwd(), help="启动 feval 的工作目录")
    start_parser.add_argument("--state-file", default=str(default_state_file()), help="会话状态文件路径")
    start_parser.add_argument("--timeout", type=float, default=20.0, help="等待首个 prompt 的超时秒数")

    exec_parser = subparsers.add_parser("exec", help="在已有会话里执行一轮命令")
    exec_parser.add_argument(
        "--command",
        action="append",
        default=[],
        help="要发送给 feval 的表达式；可重复传入以顺序执行多条",
    )
    exec_parser.add_argument(
        "--commands-file",
        default=None,
        metavar="PATH",
        help="UTF-8 文件，每行一条表达式；# 开头为注释。与 --command 可混用（先命令行参数，再文件中的行）",
    )
    exec_parser.add_argument("--state-file", default=str(default_state_file()), help="会话状态文件路径")
    exec_parser.add_argument("--timeout", type=float, default=20.0, help="等待下一次 prompt 的超时秒数")
    exec_parser.add_argument(
        "--no-console-fallback",
        action="store_true",
        help="stdio 失败时不新建 cmd 控制台（无弹窗；自动化与 Agent 应始终加此开关）",
    )

    status_parser = subparsers.add_parser("status", help="查看当前会话状态")
    status_parser.add_argument("--state-file", default=str(default_state_file()), help="会话状态文件路径")

    stop_parser = subparsers.add_parser("stop", help="停止会话")
    stop_parser.add_argument("--state-file", default=str(default_state_file()), help="会话状态文件路径")

    run_parser = subparsers.add_parser("run", help="执行一次性启动-执行-停止")
    run_parser.add_argument("--endpoint", required=True, help="feval endpoint，例如 127.0.0.1:9999 或 local")
    run_parser.add_argument("--cwd", default=os.getcwd(), help="启动 feval 的工作目录")
    run_parser.add_argument(
        "--command",
        action="append",
        default=[],
        help="要发送给 feval 的表达式；可重复传入以顺序执行多条",
    )
    run_parser.add_argument(
        "--commands-file",
        default=None,
        metavar="PATH",
        help="UTF-8 文件，每行一条表达式；# 开头为注释。与 --command 可混用（先命令行参数，再文件中的行）",
    )
    run_parser.add_argument("--timeout", type=float, default=20.0, help="每个阶段的超时秒数")
    run_parser.add_argument(
        "--no-console-fallback",
        action="store_true",
        help="stdio 失败时不新建 cmd 控制台（无弹窗；自动化与 Agent 应始终加此开关）",
    )

    return parser.parse_args()


def main() -> int:
    """脚本入口。"""
    args = parse_args()
    commands = list(getattr(args, "command", None) or [])
    commands_file = getattr(args, "commands_file", None)
    if commands_file:
        commands.extend(load_commands_from_file(Path(commands_file)))

    if args.action in ("run", "exec") and not commands:
        emit_json(
            {
                "ok": False,
                "action": args.action,
                "error": "至少需要一条 --command，或 --commands-file 中至少一行非注释、非空内容",
            }
        )
        return 1

    try:
        if args.action == "start":
            result = start_session(
                endpoint=args.endpoint,
                cwd=Path(args.cwd),
                timeout=args.timeout,
                state_file=Path(args.state_file),
            )
        elif args.action == "exec":
            result = exec_session(
                state_file=Path(args.state_file),
                commands=commands,
                timeout=args.timeout,
                no_console_fallback=bool(getattr(args, "no_console_fallback", False)),
            )
        elif args.action == "status":
            result = status_session(Path(args.state_file))
        elif args.action == "stop":
            result = stop_session(Path(args.state_file))
        elif args.action == "run":
            result = run_once(
                endpoint=args.endpoint,
                cwd=Path(args.cwd),
                commands=commands,
                timeout=args.timeout,
                no_console_fallback=bool(getattr(args, "no_console_fallback", False)),
            )
        else:
            raise RuntimeError(f"未知 action: {args.action}")

        emit_json(result)
        return 0 if result.get("ok", False) else 1
    except Exception as ex:  # noqa: BLE001
        emit_json(
            {
                "ok": False,
                "action": getattr(args, "action", None),
                "error": str(ex),
            }
        )
        return 1


if __name__ == "__main__":
    sys.exit(main())
