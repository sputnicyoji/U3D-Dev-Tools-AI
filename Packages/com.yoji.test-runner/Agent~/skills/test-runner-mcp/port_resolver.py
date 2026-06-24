#!/usr/bin/env python3
"""Project-aware port resolver for Unity agent clients."""
from __future__ import annotations

import json
import os
import socket
import urllib.error
import urllib.request
from pathlib import Path
from typing import Any, Sequence


DEFAULT_VALIDATE_TIMEOUT = 3.0
PROJECT_PORTS_FILE = Path(".u3d-ai-linker") / "ports.json"
GLOBAL_REGISTRY_FILE = Path("Yoji") / "U3D-Dev-Tools-AI" / "instances.json"


class EndpointResolutionError(RuntimeError):
    def __init__(self, code: str, message: str, *, service_id: str, context: dict[str, Any] | None = None):
        super().__init__(message)
        self.code = code
        self.service_id = service_id
        self.context = context or {}

    def as_error(self) -> dict[str, Any]:
        return {"code": self.code, "message": str(self), "serviceId": self.service_id, "context": self.context}


def resolve_endpoint(
    service_id: str,
    host: str,
    port: int | None = None,
    default_port: int | None = None,
    project: str | None = None,
    pid: int | None = None,
    timeout: float | None = None,
    legacy_ports: Sequence[int] | None = None,
    *,
    explicit_port: int | None = None,
) -> tuple[str, int, str]:
    if explicit_port is not None:
        if port is not None and port != explicit_port:
            raise ValueError("port and explicit_port disagree")
        port = explicit_port
    if port is not None:
        return host, port, "explicit"
    if default_port is None:
        raise TypeError("default_port is required")

    project_root = _resolve_project_root(project)
    records = _candidate_records(service_id, project_root, pid)
    valid: list[dict[str, Any]] = []
    probe_results: list[dict[str, Any]] = []
    for record in records:
        result = _probe_candidate(service_id, record["host"], record["port"], timeout)
        probe_results.append(_result_context(record["host"], record["port"], record["source"], result))
        if result["status"] == "healthy":
            valid.append(record)

    if len(valid) == 1:
        record = valid[0]
        return record["host"], record["port"], record["source"]

    if len(valid) > 1:
        summary = ", ".join(_format_summary(record) for record in valid)
        raise SystemExit(f"ambiguous {service_id} instances; pass --pid or --port: {summary}")

    legacy_results: list[dict[str, Any]] = []
    for legacy_port in _legacy_port_candidates(default_port, legacy_ports):
        result = _probe_candidate(service_id, host, legacy_port, timeout)
        legacy_results.append(_result_context(host, legacy_port, "legacy", result))
        if result["status"] == "healthy":
            return host, legacy_port, "legacy-healthy"

    all_results = probe_results + legacy_results
    if any(item["source"] == "legacy" and item["status"] == "service_mismatch" for item in legacy_results):
        raise EndpointResolutionError(
            "SERVICE_MISMATCH",
            f"legacy port responded with a different service for {service_id}",
            service_id=service_id,
            context={"candidates": legacy_results},
        )

    if any(item["status"] == "timeout" for item in all_results):
        raise EndpointResolutionError(
            "ENDPOINT_TIMEOUT",
            f"timed out while resolving {service_id}; pass --pid or --port if needed",
            service_id=service_id,
            context={"candidates": all_results},
        )

    return host, default_port, "legacy-default-unverified"


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
    return _probe_candidate(service_id, host, port, timeout)["status"] == "healthy"


def _probe_candidate(service_id: str, host: str, port: int, timeout: float | None) -> dict[str, Any]:
    results = [_ping(host, port, method, _validation_timeout(timeout)) for method in ("GET", "POST")]
    for result in results:
        if result["status"] == "response" and _response_service_id(result.get("payload")) == service_id:
            return {"status": "healthy"}

    statuses = [result["status"] for result in results]
    if "timeout" in statuses:
        return {"status": "timeout", "details": results}
    if any(result["status"] == "response" for result in results):
        return {"status": "service_mismatch", "details": results}
    if "bad_json" in statuses:
        return {"status": "bad_json", "details": results}
    return {"status": "connection_failed", "details": results}


def _ping(host: str, port: int, method: str, timeout: float) -> dict[str, Any]:
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
            payload = _json_from_bytes(resp.read())
            if payload is None:
                return {"method": method, "status": "bad_json"}
            return {"method": method, "status": "response", "payload": payload}
    except urllib.error.HTTPError as exc:
        payload = _json_from_bytes(exc.read())
        if payload is None:
            return {"method": method, "status": "bad_json", "error": str(exc)}
        return {"method": method, "status": "response", "payload": payload, "httpStatus": exc.code}
    except urllib.error.URLError as exc:
        if _is_timeout(exc.reason):
            return {"method": method, "status": "timeout", "error": str(exc.reason)}
        return {"method": method, "status": "connection_failed", "error": str(exc.reason)}
    except (TimeoutError, socket.timeout) as exc:
        return {"method": method, "status": "timeout", "error": str(exc)}
    except OSError as exc:
        if _is_timeout(exc):
            return {"method": method, "status": "timeout", "error": str(exc)}
        return {"method": method, "status": "connection_failed", "error": str(exc)}
    except ValueError as exc:
        return {"method": method, "status": "bad_json", "error": str(exc)}


def _is_timeout(exc: Any) -> bool:
    if isinstance(exc, (TimeoutError, socket.timeout)):
        return True
    text = str(exc).lower()
    return "timed out" in text or "timeout" in text


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


def _legacy_port_candidates(default_port: int, legacy_ports: Sequence[int] | None) -> list[int]:
    raw = list(legacy_ports) if legacy_ports is not None else [default_port]
    result: list[int] = []
    seen: set[int] = set()
    for value in raw:
        port = _as_int(value)
        if port is None or port <= 0 or port in seen:
            continue
        seen.add(port)
        result.append(port)
    return result


def _result_context(host: str, port: int, source: str, result: dict[str, Any]) -> dict[str, Any]:
    return {
        "host": host,
        "port": port,
        "source": source,
        "status": result.get("status", "unknown"),
    }


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
