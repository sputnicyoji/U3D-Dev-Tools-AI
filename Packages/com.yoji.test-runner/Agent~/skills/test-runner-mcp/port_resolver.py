#!/usr/bin/env python3
"""Project-aware port resolver for Unity agent clients."""
from __future__ import annotations

import json
import os
import urllib.error
import urllib.request
from pathlib import Path
from typing import Any


DEFAULT_VALIDATE_TIMEOUT = 3.0
PROJECT_PORTS_FILE = Path(".u3d-ai-linker") / "ports.json"
GLOBAL_REGISTRY_FILE = Path("Yoji") / "U3D-Dev-Tools-AI" / "instances.json"


def resolve_endpoint(
    service_id: str,
    host: str,
    port: int | None,
    default_port: int,
    project: str | None = None,
    pid: int | None = None,
    timeout: float | None = None,
) -> tuple[str, int, str]:
    if port is not None:
        return host, port, "explicit"

    project_root = _resolve_project_root(project)
    records = _candidate_records(service_id, project_root, pid)
    valid = []
    for record in records:
        if _validate_candidate(service_id, record["host"], record["port"], timeout):
            valid.append(record)

    if len(valid) == 1:
        record = valid[0]
        return record["host"], record["port"], record["source"]

    if len(valid) > 1:
        summary = ", ".join(_format_summary(record) for record in valid)
        raise SystemExit(f"ambiguous {service_id} instances; pass --pid or --port: {summary}")

    return host, default_port, "legacy-default"


def _candidate_records(service_id: str, project_root: Path | None, pid: int | None) -> list[dict[str, Any]]:
    records: list[dict[str, Any]] = []
    seen: set[tuple[int | None, int, str]] = set()

    if project_root is not None:
        records.extend(_load_scoped_records(project_root, "project-registry"))
        records.extend(_load_global_records(project_root))
    else:
        records.extend(_load_global_records(None))

    filtered: list[dict[str, Any]] = []
    for record in records:
        if record["serviceId"] != service_id:
            continue
        if pid is not None and record["processId"] != pid:
            continue
        key = (record["processId"], record["port"], record["instanceId"])
        if key in seen:
            continue
        seen.add(key)
        filtered.append(record)
    return filtered


def _load_scoped_records(project_root: Path, source: str) -> list[dict[str, Any]]:
    path = project_root / PROJECT_PORTS_FILE
    return _normalize_records(_load_json(path), source, project_root, True)


def _load_global_records(project_root: Path | None) -> list[dict[str, Any]]:
    path = _local_appdata() / GLOBAL_REGISTRY_FILE
    records = _normalize_records(_load_json(path), "global-registry", project_root, False)
    if project_root is None:
        return records

    root_key = _normalized_root_text(project_root)
    return [record for record in records if _normalized_root_text(record["projectRoot"]) == root_key]


def _normalize_records(
    data: Any,
    source: str,
    project_root: Path | None,
    fill_missing_root: bool,
) -> list[dict[str, Any]]:
    if not isinstance(data, dict):
        return []

    instances = data.get("instances")
    if not isinstance(instances, list):
        return []

    normalized: list[dict[str, Any]] = []
    for raw in instances:
        if not isinstance(raw, dict):
            continue
        port = _as_int(raw.get("port"))
        if port is None or port <= 0:
            continue
        record = {
            "serviceId": _as_text(raw.get("serviceId")),
            "host": _as_text(raw.get("host")) or "127.0.0.1",
            "port": port,
            "processId": _as_int(raw.get("processId")),
            "instanceId": _as_text(raw.get("instanceId")),
            "projectRoot": _normalize_project_root(raw.get("projectRoot"), project_root, fill_missing_root),
            "source": source,
        }
        if not record["instanceId"]:
            continue
        normalized.append(record)
    return normalized


def _normalize_project_root(raw: Any, project_root: Path | None, fill_missing_root: bool) -> str:
    text = _as_text(raw)
    if text:
        return _normalized_root_text(text)
    if fill_missing_root and project_root is not None:
        return _normalized_root_text(project_root)
    return ""


def _resolve_project_root(project: str | None) -> Path | None:
    if project:
        path = _coerce_path(project)
        if path is not None:
            return path

    cwd = _coerce_path(Path.cwd())
    if cwd is None:
        return None

    for current in [cwd, *cwd.parents]:
        if (current / PROJECT_PORTS_FILE).exists():
            return current
        if (current / "ProjectSettings" / "ProjectVersion.txt").exists():
            return current
    return None


def _coerce_path(path: str | Path) -> Path | None:
    try:
        current = Path(path).expanduser()
        return current.resolve()
    except Exception:
        return None


def _local_appdata() -> Path:
    root = os.environ.get("LOCALAPPDATA")
    if root:
        return Path(root)
    return Path.home() / "AppData" / "Local"


def _load_json(path: Path) -> Any | None:
    try:
        text = path.read_text(encoding="utf-8")
    except Exception:
        return None
    try:
        return json.loads(text)
    except Exception:
        return None


def _validate_candidate(service_id: str, host: str, port: int, timeout: float | None) -> bool:
    ping_timeout = _validation_timeout(timeout)
    for method in ("GET", "POST"):
        payload = _ping(host, port, method, ping_timeout)
        if _response_service_id(payload) == service_id:
            return True
    return False


def _ping(host: str, port: int, method: str, timeout: float) -> Any | None:
    url = f"http://{host}:{port}/ping"
    try:
        if method == "POST":
            req = urllib.request.Request(
                url,
                data=b"{}",
                headers={"Content-Type": "application/json; charset=utf-8"},
                method="POST",
            )
        else:
            req = urllib.request.Request(url, method="GET")
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            return _json_from_bytes(resp.read())
    except urllib.error.HTTPError as exc:
        try:
            return _json_from_bytes(exc.read())
        except Exception:
            return None
    except urllib.error.URLError:
        return None
    except Exception:
        return None


def _json_from_bytes(raw: bytes) -> Any | None:
    try:
        return json.loads(raw.decode("utf-8"))
    except Exception:
        return None


def _response_service_id(payload: Any) -> str:
    if not isinstance(payload, dict):
        return ""
    value = payload.get("serviceId")
    if isinstance(value, str) and value:
        return value
    result = payload.get("result")
    if isinstance(result, dict):
        value = result.get("serviceId")
        if isinstance(value, str) and value:
            return value
    body = payload.get("body")
    if isinstance(body, dict):
        value = body.get("serviceId")
        if isinstance(value, str) and value:
            return value
    return ""


def _validation_timeout(timeout: float | None) -> float:
    if timeout is None:
        return DEFAULT_VALIDATE_TIMEOUT
    try:
        value = float(timeout)
    except Exception:
        return DEFAULT_VALIDATE_TIMEOUT
    if value <= 0:
        return DEFAULT_VALIDATE_TIMEOUT
    return min(value, DEFAULT_VALIDATE_TIMEOUT)


def _as_int(value: Any) -> int | None:
    if isinstance(value, bool):
        return None
    try:
        return int(value)
    except Exception:
        return None


def _as_text(value: Any) -> str:
    if value is None:
        return ""
    text = str(value).strip()
    return text


def _normalized_root_text(value: str | Path) -> str:
    text = str(value).replace("\\", "/")
    try:
        text = str(Path(text).resolve())
    except Exception:
        pass
    return text.replace("\\", "/").rstrip("/").lower()


def _format_summary(record: dict[str, Any]) -> str:
    return f"pid={record['processId']} port={record['port']} project={record['projectRoot']}"
